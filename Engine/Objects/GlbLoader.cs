using DarkEngine3D_gl_csharp.Engine.Terrains;
using System.Numerics;
using System.Text.RegularExpressions;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    // ===========================================================================
    //  GlbAsset — represents a loaded GLB file (asset or scene mode)
    //
    //  Pipeline:
    //    GLB → Import Nodes → Store Hierarchy → Store Meshes (RemoveWorldTransform)
    //    → Store Materials (PBR) → Store Textures → Scene Instance
    //    → Snap To Terrain → Y Offset
    //
    //  For "asset" mode: RemoveWorldTransform is applied, so node transforms are
    //    baked into vertex positions and the model sits at origin. Individual meshes
    //    can be instantiated via CreateInstance().
    //
    //  For "scene" mode: Original GLB node hierarchy is preserved, rendering follows
    //    the GLB's world transforms as-is.
    // ===========================================================================
    public class GlbAsset
    {
        /// <summary>The GLB file path.</summary>
        public string Path { get; }

        /// <summary>GPU data for all meshes in this GLB.</summary>
        public GltfModelGpuData GpuData { get; }

        /// <summary>Whether this was loaded as a scene (keeps node transforms).</summary>
        public bool IsScene { get; }

        /// <summary>Mesh index by name dictionary (base name → mesh indices). Strips _LOD suffixes.</summary>
        public Dictionary<string, int[]> MeshesByName { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Groups available for this GLB.</summary>
        public List<StaticObjectGroup> Groups { get; } = [];

        /// <summary>Local AABB of all meshes combined (after RemoveWorldTransform if asset).</summary>
        public AABB LocalAABB { get; private set; }

        /// <summary>World position this asset was loaded at.</summary>
        public Vector3 LoadPosition { get; set; }

        public GlbAsset(string path, GltfModelGpuData gpuData, bool isScene)
        {
            Path = path;
            GpuData = gpuData;
            IsScene = isScene;

            // Build name → mesh index lookup (strip _LOD suffixes)
            var nameToMeshes = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int mi = 0; mi < gpuData.Data.Meshes.Length; mi++)
            {
                string? meshName = gpuData.Data.Meshes[mi].Name;
                if (string.IsNullOrEmpty(meshName)) continue;

                // Get base name without _LOD suffix
                string baseName = Regex.Replace(
                    meshName, @"_LOD\d+.*$", "",
                    RegexOptions.IgnoreCase);

                if (!nameToMeshes.ContainsKey(baseName))
                    nameToMeshes[baseName] = new List<int>();
                nameToMeshes[baseName].Add(mi);
            }

            foreach (var kv in nameToMeshes)
                MeshesByName[kv.Key] = [.. kv.Value];

            LocalAABB = gpuData.LocalAABB;
        }

        /// <summary>Get mesh indices for a named mesh part. Returns null if not found.</summary>
        public int[]? GetMesh(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // Try exact match first
            if (MeshesByName.TryGetValue(name, out var indices))
                return indices;

            // Try substring match
            foreach (var kv in MeshesByName)
            {
                if (kv.Key.Contains(name, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }

            return null;
        }

        /// <summary>Get all unique mesh base names.</summary>
        public string[] GetMeshNames() => [.. MeshesByName.Keys];
    }

    // ===========================================================================
    //  GlbLoader — static helper for the revamped GLB loading pipeline
    //
    //  Usage:
    //    var asset = GlbLoader.Load(manager, "path.glb", pos, 0f, 1f, false, terrain, 0f);
    //    var house = GlbLoader.CreateInstance(manager, asset, "House01", pos, 0f, 1f, terrain, 0f);
    // ===========================================================================
    public static class GlbLoader
    {
        /// <summary>
        /// Load a GLB file as an asset or scene using the revamped pipeline.
        ///
        /// Pipeline: GLB → Import Nodes → Store Hierarchy → Store Meshes
        ///   (RemoveWorldTransform if asset) → Store Materials (PBR) → Store Textures
        ///   → Scene Instance → Snap To Terrain → Y Offset
        ///
        /// Scenario 1 (asset): obj = loadobj(x, terrainHeight + offset, y, asset)
        ///   → use LoadGlb(manager, pos, ... isScene: false)
        /// 
        /// Scenario 2 (asset + instance): city = loadobj(...); CreateInstance(city.GetMesh("House01"))
        ///   → use LoadGlb(manager, pos, ... isScene: false) then CreateInstance(manager, asset, "House01", ...)
        ///
        /// Scenario 3 (scene): cityBig = loadobj(x, terrainHeight + offset, y, scene)
        ///   → use LoadGlb(manager, pos, ... isScene: true)
        /// </summary>
        /// <param name="manager">The StaticObjectManager to add objects to.</param>
        /// <param name="path">Path to the .glb file.</param>
        /// <param name="position">World position to place the loaded asset/scene.</param>
        /// <param name="yawDegrees">Yaw rotation in degrees.</param>
        /// <param name="scale">Uniform scale.</param>
        /// <param name="isScene">True = scene mode (keep transforms), false = asset mode (RemoveWorldTransform).</param>
        /// <param name="terrain">Terrain for snapping (optional).</param>
        /// <param name="yOffset">Additional Y offset applied after terrain snap.</param>
        /// <returns>A GlbAsset representing the loaded file, or null on failure.</returns>
        public static GlbAsset? Load(
            StaticObjectManager manager,
            string path,
            Vector3 position,
            float yawDegrees = 0f,
            float scale = 1.0f,
            bool isScene = false,
            TerrainChunk? terrain = null,
            float yOffset = 0f,
            bool autoCreateInstances = true)
        {
            if (manager == null) return null;

            // 1. Parse GLB file
            var data = GltfLoader.Load(path);
            if (data == null || data.Meshes.Length == 0)
            {
                Console.WriteLine($"[GlbLoader] Failed to load: {path}");
                return null;
            }

            // 2. Apply RemoveWorldTransform if this is an asset (not a scene)
            bool useNodeHierarchy = isScene;

            if (!isScene && data.Nodes != null && data.Nodes.Length > 0)
            {
                // Build temporary mesh-to-node mapping
                int[] meshToNode = new int[data.Meshes.Length];
                Array.Fill(meshToNode, -1);
                for (int ni = 0; ni < data.Nodes.Length; ni++)
                    if (data.Nodes[ni].Mesh >= 0 && data.Nodes[ni].Mesh < meshToNode.Length)
                        meshToNode[data.Nodes[ni].Mesh] = ni;

                // Bake world transforms into vertex positions
                RemoveWorldTransform(data, meshToNode);
                Console.WriteLine($"[GlbLoader] RemoveWorldTransform applied to '{System.IO.Path.GetFileName(path)}' ({data.Meshes.Length} meshes)");
            }

            // 3. Upload to GPU
            var gpuData = new GltfModelGpuData(data, useNodeHierarchy);

            // 4. Create GlbAsset
            var asset = new GlbAsset(path, gpuData, isScene)
            {
                LoadPosition = position
            };

            // 5. Build groups from mesh names
            BuildAssetGroups(asset, data, gpuData);

            // 6. Create instances (unless autoCreateInstances is false):
            //    Scene mode → one StaticObject per mesh group (preserves node hierarchy)
            //    Asset mode  → one StaticObject with all meshes (root group), snapped to terrain
            if (autoCreateInstances)
            {
                if (isScene)
                {
                    CreateSceneInstance(manager, asset, position, yawDegrees, scale, terrain, yOffset);
                }
                else
                {
                    // Asset mode: create a single root object containing ALL meshes,
                    // with RemoveWorldTransform already baked into vertices.
                    CreateAssetInstance(manager, asset, position, yawDegrees, scale, terrain, yOffset);
                }
            }

            Console.WriteLine($"[GlbLoader] Loaded '{System.IO.Path.GetFileName(path)}' as {(isScene ? "scene" : "asset")} ({data.Meshes.Length} meshes, {data.Nodes?.Length ?? 0} nodes, {asset.MeshesByName.Count} named parts)");
            return asset;
        }

        /// <summary>
        /// Create a static object instance from a named mesh in a loaded GlbAsset.
        ///
        /// Scenario 2: city = loadobj(x, y, z, asset); CreateInstance(city.GetMesh("House01"))
        ///
        /// Terrain snapping uses the ROOT group's AABB (all meshes) so the instance
        /// sits at the correct Y relative to the bottom of the entire model, regardless
        /// of which individual mesh is being placed (e.g. "Roof" whose AABB min Y
        /// is above the floor).
        /// </summary>
        /// <param name="manager">The StaticObjectManager to add the instance to.</param>
        /// <param name="asset">The loaded asset.</param>
        /// <param name="meshName">Name of the mesh to instantiate (e.g. "House01", "Door").</param>
        /// <param name="position">World position.</param>
        /// <param name="yawDegrees">Yaw rotation.</param>
        /// <param name="scale">Scale.</param>
        /// <param name="terrain">Terrain for snapping.</param>
        /// <param name="yOffset">Y offset after terrain snap.</param>
        /// <returns>The created StaticObject, or null if mesh not found.</returns>
        public static StaticObject? CreateInstance(
            StaticObjectManager manager,
            GlbAsset asset,
            string meshName,
            Vector3 position,
            float yawDegrees = 0f,
            float scale = 1.0f,
            TerrainChunk? terrain = null,
            float yOffset = 0f)
        {
            if (manager == null || asset == null) return null;

            // Look up the mesh by name
            var meshIndices = asset.GetMesh(meshName);
            if (meshIndices == null || meshIndices.Length == 0)
            {
                var names = string.Join(", ", asset.GetMeshNames());
                Console.WriteLine($"[GlbLoader] Mesh '{meshName}' not found in '{System.IO.Path.GetFileName(asset.Path)}'. Available: {names}");
                return null;
            }

            // Create a single-mesh group for this instance
            // Sort mesh indices into proper LOD levels so only one LOD renders
            // at a time based on distance, rather than all LODs simultaneously.
            var group = new StaticObjectGroup
            {
                BaseName = meshName
            };
            var lods = new Dictionary<int, List<int>>();
            var meshes = asset.GpuData.Data.Meshes;
            if (meshes != null)
            {
                foreach (int mi in meshIndices)
                {
                    if (mi < 0 || mi >= meshes.Length) continue;
                    string? mName = meshes[mi].Name ?? "";
                    int lodLevel = 0;
                    if (mName.Contains("_LOD3", StringComparison.OrdinalIgnoreCase)) lodLevel = 3;
                    else if (mName.Contains("_LOD2", StringComparison.OrdinalIgnoreCase)) lodLevel = 2;
                    else if (mName.Contains("_LOD1", StringComparison.OrdinalIgnoreCase)) lodLevel = 1;
                    if (!lods.ContainsKey(lodLevel)) lods[lodLevel] = new List<int>();
                    lods[lodLevel].Add(mi);
                }
            }
            // Fallback: if no _LOD suffix found, put all in LOD 1
            if (lods.Count == 0)
                lods[1] = [.. meshIndices];

            int maxLod = 0;
            foreach (var kv in lods)
            {
                group.Lods[kv.Key] = kv.Value;
                if (kv.Key > maxLod) maxLod = kv.Key;
            }
            group.MaxLOD = maxLod;
            for (int t = 0; t < 4; t++)
            {
                int nearest = Math.Min(t, maxLod);
                while (nearest >= 0 && !group.Lods.ContainsKey(nearest))
                    nearest--;
                group.LodFallback[t] = Math.Max(nearest, 0);
            }

            // Compute local AABB from LOD0 meshes only (original detail mesh).
            // Pass manager.UseNodeHierarchy so AABB matches rendering:
            //   - asset mode (false): uses LocalMatrix (all Identity after RWT)
            //   - scene mode (true):  uses GetNodeWorldMatrix (walks parent chain)
            bool useHierarchy = manager.UseNodeHierarchy;
            if (group.Lods.TryGetValue(0, out var lod0) && lod0.Count > 0)
                group.LocalAABB = ComputeLocalAABB([.. lod0], asset.GpuData.MeshToNode, asset.GpuData.Data.Nodes, asset.GpuData.Data.Meshes, useHierarchy);
            else
                group.LocalAABB = ComputeLocalAABB(meshIndices, asset.GpuData.MeshToNode, asset.GpuData.Data.Nodes, asset.GpuData.Data.Meshes, useHierarchy);

            // ── Compute terrain snap using the ROOT group's AABB ──
            Vector3 finalPos = position;
            if (terrain != null)
            {
                float terrainY = terrain.GetHeightAt(finalPos.X, finalPos.Z);
                var rootGroup = asset.Groups.FirstOrDefault(g => g.BaseName.Equals("root", StringComparison.OrdinalIgnoreCase));
                if (rootGroup != null)
                {
                    var noTrans = Matrix4x4.CreateScale(scale)
                                * Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f));
                    var rotatedAABB = rootGroup.LocalAABB.Transform(noTrans);
                    finalPos.Y = terrainY - rotatedAABB.Min.Y + yOffset;
                }
            }

            // Create via StaticObjectManager's AddObject (reuse the existing API)
            var sobj = new StaticObject(asset.GpuData, group, finalPos, yawDegrees, scale);
            sobj.CachedBaseWorldMat = Matrix4x4.CreateScale(sobj.Scale) *
                                       Matrix4x4.CreateFromQuaternion(sobj.Rotation) *
                                       Matrix4x4.CreateTranslation(sobj.Position);
            sobj.CachedWorldAABB = group.LocalAABB.Transform(sobj.CachedBaseWorldMat);



            manager.AddObject(sobj);

            Console.WriteLine($"[GlbLoader] Created instance '{meshName}' from '{System.IO.Path.GetFileName(asset.Path)}' at ({finalPos.X:F1}, {finalPos.Y:F1}, {finalPos.Z:F1})");
            return sobj;
        }

        /// <summary>
        /// Create a single StaticObject from the root group (all meshes) for asset mode.
        /// RemoveWorldTransform was already applied, so the root group AABB and vertex
        /// positions are in a unified local space. The object is snapped to terrain.
        /// </summary>
        private static void CreateAssetInstance(
            StaticObjectManager manager,
            GlbAsset asset,
            Vector3 position,
            float yawDegrees,
            float scale,
            TerrainChunk? terrain,
            float yOffset)
        {
            // Use the root group (contains ALL meshes)
            var rootGroup = asset.Groups.FirstOrDefault(g => g.BaseName.Equals("root", StringComparison.OrdinalIgnoreCase));
            if (rootGroup == null) return;

            Vector3 finalPos = position;
            if (terrain != null)
            {
                float terrainY = terrain.GetHeightAt(finalPos.X, finalPos.Z);
                var noTrans = Matrix4x4.CreateScale(scale)
                            * Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f));
                var rotatedAABB = rootGroup.LocalAABB.Transform(noTrans);
                finalPos.Y = terrainY - rotatedAABB.Min.Y + yOffset;
            }

            var sobj = new StaticObject(asset.GpuData, rootGroup, finalPos, yawDegrees, scale);
            sobj.CachedBaseWorldMat = Matrix4x4.CreateScale(sobj.Scale) *
                                       Matrix4x4.CreateFromQuaternion(sobj.Rotation) *
                                       Matrix4x4.CreateTranslation(sobj.Position);
            sobj.CachedWorldAABB = rootGroup.LocalAABB.Transform(sobj.CachedBaseWorldMat);

            manager.AddObject(sobj);

            Console.WriteLine($"[GlbLoader] Created asset instance from '{System.IO.Path.GetFileName(asset.Path)}' at ({finalPos.X:F1}, {finalPos.Y:F1}, {finalPos.Z:F1})");
        }

        /// <summary>
        /// Create scene instances from a loaded GlbAsset (scene mode).
        /// Creates one StaticObject per named mesh group, preserving the GLB's layout.
        ///
        /// Terrain snapping is applied ONCE using the ROOT group's AABB (all meshes),
        /// then the same Y offset is used for all mesh instances. This prevents
        /// individual mesh groups from drifting apart vertically.
        /// </summary>
        private static void CreateSceneInstance(
            StaticObjectManager manager,
            GlbAsset asset,
            Vector3 position,
            float yawDegrees,
            float scale,
            TerrainChunk? terrain,
            float yOffset)
        {
            // Scene mode requires node hierarchy for correct rendering
            manager.UseNodeHierarchy = true;

            // ── Compute terrain snap ONCE using the root group AABB ──
            float snapY = position.Y;
            if (terrain != null)
            {
                // Find the root group (all meshes combined)
                var rootGroup = asset.Groups.FirstOrDefault(g => g.BaseName.Equals("root", StringComparison.OrdinalIgnoreCase));
                if (rootGroup != null)
                {
                    float terrainY = terrain.GetHeightAt(position.X, position.Z);
                    var noTrans = Matrix4x4.CreateScale(scale)
                                * Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f));
                    var rotatedAABB = rootGroup.LocalAABB.Transform(noTrans);
                    snapY = terrainY - rotatedAABB.Min.Y + yOffset;
                }
            }

            // ── Create one StaticObject per named mesh group ──
            // All use the same snapY to preserve the GLB's relative Y layout.
            foreach (var group in asset.Groups)
            {
                if (group.BaseName.Equals("root", StringComparison.OrdinalIgnoreCase))
                    continue;

                Vector3 pos = new(position.X, snapY, position.Z);

                var sobj = new StaticObject(asset.GpuData, group, pos, yawDegrees, scale);
                sobj.CachedBaseWorldMat = Matrix4x4.CreateScale(sobj.Scale) *
                                           Matrix4x4.CreateFromQuaternion(sobj.Rotation) *
                                           Matrix4x4.CreateTranslation(sobj.Position);
                sobj.CachedWorldAABB = group.LocalAABB.Transform(sobj.CachedBaseWorldMat);

                manager.AddObject(sobj);
            }

            Console.WriteLine($"[GlbLoader] Created scene with {asset.Groups.Count - 1} objects from '{System.IO.Path.GetFileName(asset.Path)}' at Y={snapY:F2}");
        }

        /// <summary>
        /// Build groups for the asset from mesh names.
        /// </summary>
        private static void BuildAssetGroups(GlbAsset asset, GltfData data, GltfModelGpuData gpuData)
        {
            // Build per-mesh name groups from the MeshesByName dictionary
            foreach (var kv in asset.MeshesByName)
            {
                if (kv.Value.Length == 0) continue;

                var group = new StaticObjectGroup
                {
                    BaseName = kv.Key,
                    MaxLOD = 1
                };
                group.Lods[1] = [.. kv.Value];
                for (int t = 0; t < 4; t++)
                    group.LodFallback[t] = 1;

                group.LocalAABB = ComputeLocalAABB(kv.Value, gpuData.MeshToNode, data.Nodes, data.Meshes, asset.IsScene);
                asset.Groups.Add(group);
            }

            // Also create a root group with ALL meshes
            var rootGroup = new StaticObjectGroup
            {
                BaseName = "root",
                MaxLOD = 1
            };
            var allMeshIndices = new List<int>();
            for (int i = 0; i < data.Meshes.Length; i++)
                allMeshIndices.Add(i);
            rootGroup.Lods[1] = allMeshIndices;
            for (int t = 0; t < 4; t++)
                rootGroup.LodFallback[t] = 1;
            rootGroup.LocalAABB = gpuData.LocalAABB;
            asset.Groups.Insert(0, rootGroup);
        }

        /// <summary>
        /// Compute the local AABB for a set of mesh indices, accounting for node transforms.
        /// When useNodeHierarchy=true (scene mode), uses GetNodeWorldMatrix (walks parent
        /// chain) to match the rendering pipeline. When false (asset mode), uses LocalMatrix
        /// directly — after RemoveWorldTransform all nodes are Identity, giving raw vertex AABB.
        /// </summary>
        private static AABB ComputeLocalAABB(int[] meshIndices, int[]? meshToNode, GltfNode[]? nodes, GltfMeshData[] meshes, bool useNodeHierarchy = false)
        {
            if (meshes == null || meshIndices.Length == 0)
                return new AABB(Vector3.Zero, Vector3.One);

            Vector3 mn = new(float.PositiveInfinity);
            Vector3 mx = new(float.NegativeInfinity);
            bool any = false;

            foreach (int mi in meshIndices)
            {
                if (mi < 0 || mi >= meshes.Length) continue;
                var verts = meshes[mi].Vertices;
                if (verts == null || verts.Length == 0) continue;

                // Match rendering pipeline: asset mode uses LocalMatrix, scene mode uses GetNodeWorldMatrix
                Matrix4x4 nodeMat = Matrix4x4.Identity;
                int nodeIdx = (meshToNode != null && mi < meshToNode.Length) ? meshToNode[mi] : -1;
                if (nodeIdx >= 0 && nodes != null && nodeIdx < nodes.Length)
                    nodeMat = useNodeHierarchy
                        ? GetNodeWorldMatrix(nodes, nodeIdx)
                        : nodes[nodeIdx].LocalMatrix;

                foreach (var v in verts)
                {
                    var p = Vector3.Transform(v.Position, nodeMat);
                    if (!any)
                    {
                        mn = p; mx = p; any = true;
                    }
                    else
                    {
                        mn = Vector3.Min(mn, p);
                        mx = Vector3.Max(mx, p);
                    }
                }
            }

            if (!any)
                return new AABB(Vector3.Zero, Vector3.One);

            return new AABB(mn, mx);
        }

        /// <summary>
        /// Create multiple random instances of named meshes from a loaded asset.
        /// Each instance is placed at a random position within a circular radius,
        /// with a random yaw rotation. Progress is reported as instances are created.
        /// </summary>
        /// <param name="manager">The StaticObjectManager to add objects to.</param>
        /// <param name="asset">The loaded asset containing the meshes.</param>
        /// <param name="meshVariants">Array of mesh names to randomly pick from.</param>
        /// <param name="count">Number of instances to create.</param>
        /// <param name="radius">Maximum distance from center for random placement.</param>
        /// <param name="center">Center of the circular placement area. Defaults to (0,0,0).</param>
        /// <param name="terrain">Terrain for snapping (optional).</param>
        /// <param name="yOffset">Y offset after terrain snap.</param>
        /// <param name="progressMin">Progress value at start (0 instances created).</param>
        /// <param name="progressMax">Progress value at end (all instances created).</param>
        /// <param name="progressLabel">Label used in progress messages (e.g. "trees").</param>
        /// <param name="rng">Optional Random instance. A new one is created if omitted.</param>
        /// <param name="configureInstance">Optional callback to configure each created instance (collision, etc.).</param>
        /// <param name="onProgress">Optional progress callback (progress 0-1, status message).</param>
        public static void CreateRandomInstances(
            StaticObjectManager manager,
            GlbAsset asset,
            string[] meshVariants,
            int count,
            float radius,
            Vector3? center = null,
            TerrainChunk? terrain = null,
            float yOffset = 0f,
            float progressMin = 0f,
            float progressMax = 1f,
            string progressLabel = "",
            Random? rng = null,
            Action<StaticObject>? configureInstance = null,
            Action<float, string>? onProgress = null)
        {
            if (manager == null || asset == null || meshVariants == null || meshVariants.Length == 0)
                return;

            rng ??= new Random();
            Vector3 ctr = center ?? Vector3.Zero;

            for (int i = 0; i < count; i++)
            {
                float a = (float)(rng.NextDouble() * Math.PI * 2);
                float d = (float)(rng.NextDouble() * radius);
                float x = ctr.X + MathF.Cos(a) * d;
                float z = ctr.Z + MathF.Sin(a) * d;
                string variant = meshVariants[rng.Next(meshVariants.Length)];

                var sobj = CreateInstance(
                    manager, asset, variant,
                    new Vector3(x, 0, z),
                    (float)(rng.NextDouble() * 360),
                    1f, terrain, yOffset);

                if (sobj != null)
                    configureInstance?.Invoke(sobj);

                if (onProgress != null)
                {
                    float p = progressMin + ((i + 1f) / count) * (progressMax - progressMin);
                    onProgress(p, $"Static: loading {progressLabel} ({i + 1}/{count})");
                }
            }
        }


    }
}
