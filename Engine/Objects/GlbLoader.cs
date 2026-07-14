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

        /// <summary>Uniform scale used when this asset was loaded.</summary>
        public float LoadScale { get; set; } = 1f;

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

            // Local dict for billboard bake offsets (null for scene mode).
            Dictionary<int, Vector3>? meshCenters = null;

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

                // Center each variant's mesh vertices around origin so that instance
                // positions become actual world positions (no offset compensation needed).
                // Returns a dict of meshIndex → originalMeshCenter (before centering).
                meshCenters = CenterVariantGroups(data);
            }

            // 3. Upload to GPU
            var gpuData = new GltfModelGpuData(data, useNodeHierarchy);

            // Store original mesh centers on gpuData for BakeOne offset computation.
            // BakeOne computes the group center from these and applies per-mesh offsets
            // to restore relative sub-mesh positions during atlas baking.
            if (meshCenters != null)
                gpuData.MeshOriginalCenters = new Dictionary<int, Vector3>(meshCenters);

            // 4. Create GlbAsset
            var asset = new GlbAsset(path, gpuData, isScene)
            {
                LoadPosition = position,
                LoadScale = scale
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
            var allVariantIndices = asset.GetMesh(meshName);
            if (allVariantIndices == null || allVariantIndices.Length == 0)
            {
                var names = string.Join(", ", asset.GetMeshNames());
                Console.WriteLine($"[GlbLoader] Mesh '{meshName}' not found in '{System.IO.Path.GetFileName(asset.Path)}'. Available: {names}");
                return null;
            }

            // ── Only use the FIRST mesh and its LOD chain ──
            // Some GLBs have multiple instances of the same variant at different world
            // positions.  Using ALL variant meshes in a single StaticObject would cause
            // the AABB to span the full GLB extent (thousands of units).
            //
            // Instead, we pick the first mesh and find all its LOD variants by matching
            // the unique base name (before '_LOD') across ALL meshes in the asset.
            var meshes = asset.GpuData.Data.Meshes;
            if (meshes == null) return null;

            int primaryIdx = allVariantIndices[0];
            string? primaryName = meshes[primaryIdx].Name ?? "";
            string primaryBase = Regex.Replace(primaryName, @"_LOD\d+.*$", "", RegexOptions.IgnoreCase);

            var filtered = new List<int>();
            for (int mi = 0; mi < meshes.Length; mi++)
            {
                string? mName = meshes[mi].Name ?? "";
                string mBase = Regex.Replace(mName, @"_LOD\d+.*$", "", RegexOptions.IgnoreCase);
                if (mBase.Equals(primaryBase, StringComparison.OrdinalIgnoreCase))
                    filtered.Add(mi);
            }

            var meshIndices = filtered.ToArray();
            if (meshIndices.Length == 0)
                meshIndices = [primaryIdx];

            bool useHierarchy = manager.UseNodeHierarchy;

            // ── Reuse the asset variant group for this instance ──
            // BuildAssetGroups already created variant groups with proper LOD sorting.
            // Reusing the SAME group object across instances means billboard data
            // (BillboardAtlasTexture, BillboardRadius) is shared without duplication.
            var group = asset.Groups.FirstOrDefault(g => g.BaseName.Equals(meshName, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                // Fallback: create a new group (shouldn't normally happen)
                group = new StaticObjectGroup
                {
                    BaseName = meshName
                };
                var lods = new Dictionary<int, List<int>>();
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
                group.LocalAABB = ComputeLocalAABB(meshIndices, asset.GpuData.MeshToNode, asset.GpuData.Data.Nodes, asset.GpuData.Data.Meshes, useHierarchy);
            }

            // Register for billboard baking if billboards are enabled
            // (BillboardMgr must be set up BEFORE calling CreateInstance)
            if (manager.UseBillboards && manager.BillboardMgr != null && !group.HasBillboard)
                manager.BillboardMgr.RegisterForBaking(asset.GpuData, group, manager.UseNodeHierarchy);



            // ── Compute terrain snap using the VARIANT's group AABB ──
            // Using the ROOT group's AABB (which spans ALL variants) would give a Min.Y
            // lower than the actual variant's bottom, making the instance float above terrain.
            // Each variant has its own centered AABB — use that for accurate ground contact.
            Vector3 finalPos = position;
            if (terrain != null)
            {
                float terrainY = terrain.GetHeightAt(finalPos.X, finalPos.Z);

                var snapNoTrans = Matrix4x4.CreateScale(scale)
                                * Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f));
                var snapRotatedAABB = group.LocalAABB.Transform(snapNoTrans);
                finalPos.Y = terrainY - snapRotatedAABB.Min.Y + yOffset;
            }

            // Create via StaticObjectManager's AddObject (reuse the existing API)
            var sobj = new StaticObject(asset.GpuData, group, finalPos, yawDegrees, scale);
            sobj.CachedBaseWorldMat = Matrix4x4.CreateScale(sobj.Scale) *
                                       Matrix4x4.CreateFromQuaternion(sobj.Rotation) *
                                       Matrix4x4.CreateTranslation(sobj.Position);
            sobj.CachedWorldAABB = group.LocalAABB.Transform(sobj.CachedBaseWorldMat);

            manager.AddObject(sobj);

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

        }

        /// <summary>
        /// Build groups for the asset from mesh names.
        /// Each variant group gets detailed LOD sorting (same logic as CreateInstance)
        /// so that billboard data baked on these groups can be shared across instances.
        ///
        /// Note: BakeOne now computes per-mesh centering offsets on the fly from
        /// gpuData.MeshOriginalCenters, so no pre-computed offsets are needed here.
        /// </summary>
        private static void BuildAssetGroups(GlbAsset asset, GltfData data, GltfModelGpuData gpuData)
        {
            // Build per-mesh name groups from the MeshesByName dictionary
            foreach (var kv in asset.MeshesByName)
            {
                if (kv.Value.Length == 0) continue;

                // ── Same LOD sorting as CreateInstance ──
                // Sort meshes by _LOD suffix first, so that the group's LOD fallback
                // structure matches what Draw() expects._
                var group = new StaticObjectGroup
                {
                    BaseName = kv.Key,
                    MaxLOD = 0
                };
                var lods = new Dictionary<int, List<int>>();
                foreach (int mi in kv.Value)
                {
                    string? mName = data.Meshes[mi].Name ?? "";
                    int lodLevel = 0;
                    if (mName.Contains("_LOD3", StringComparison.OrdinalIgnoreCase)) lodLevel = 3;
                    else if (mName.Contains("_LOD2", StringComparison.OrdinalIgnoreCase)) lodLevel = 2;
                    else if (mName.Contains("_LOD1", StringComparison.OrdinalIgnoreCase)) lodLevel = 1;
                    if (!lods.ContainsKey(lodLevel)) lods[lodLevel] = new List<int>();
                    lods[lodLevel].Add(mi);
                }
                // Fallback: if no _LOD suffix found, put all in LOD 1
                if (lods.Count == 0)
                    lods[1] = [.. kv.Value];

                int maxLod = 0;
                foreach (var lkv in lods)
                {
                    group.Lods[lkv.Key] = lkv.Value;
                    if (lkv.Key > maxLod) maxLod = lkv.Key;
                }
                group.MaxLOD = maxLod;
                for (int t = 0; t < 4; t++)
                {
                    int nearest = Math.Min(t, maxLod);
                    while (nearest >= 0 && !group.Lods.ContainsKey(nearest))
                        nearest--;
                    group.LodFallback[t] = Math.Max(nearest, 0);
                }

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

        /// <summary>        /// After RemoveWorldTransform, center each variant's mesh vertices around origin
        /// so that instance positions become actual world positions.
        ///
        /// Without this, variant meshes retain their original GLB world positions (e.g.
        /// Christmas tree at X=1550). CreateInstance at (0,19,0) would render at X=1550.
        ///
        /// IMPORTANT: Each mesh is centered INDEPENDENTLY (by its own AABB), not as a group.
        /// GLB files often contain MULTIPLE COPIES of the same variant (e.g. 3 Christmas
        /// trees at different world positions). If we center them ALL together, the copies
        /// remain spread out (e.g. Z=-1500 to Z=+1500), making the AABB HUGE.
        ///
        /// Centering each mesh individually moves every copy to origin, so the combined
        /// AABB becomes just the extent of ONE tree copy (small).
        ///
        /// RETURNS a dict mapping meshIndex → originalMeshCenter (before centering).
        /// Used by BuildAssetGroups → BakeOne to compute bake offsets.
        /// </summary>
        private static Dictionary<int, Vector3> CenterVariantGroups(GltfData data)
        {
            var meshCenters = new Dictionary<int, Vector3>();
            if (data.Meshes == null || data.Meshes.Length == 0) return meshCenters;

            // Group meshes by variant name (same logic as GlbAsset constructor)
            var variantMeshes = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int mi = 0; mi < data.Meshes.Length; mi++)
            {
                string? meshName = data.Meshes[mi].Name;
                if (string.IsNullOrEmpty(meshName)) continue;
                string baseName = Regex.Replace(meshName, @"_LOD\d+.*$", "", RegexOptions.IgnoreCase);
                if (!variantMeshes.ContainsKey(baseName))
                    variantMeshes[baseName] = new List<int>();
                variantMeshes[baseName].Add(mi);
            }

            foreach (var kv in variantMeshes)
            {
                var meshIndices = kv.Value;

                // ── Center EACH mesh individually ──
                // Each copy of the same variant at a different world position gets centered
                // by its OWN AABB, so they all end up at origin instead of spreading out.
                for (int ii = 0; ii < meshIndices.Count; ii++)
                {
                    int mi = meshIndices[ii];
                    if (mi < 0 || mi >= data.Meshes.Length) continue;
                    var verts = data.Meshes[mi].Vertices;
                    if (verts == null || verts.Length == 0) continue;

                    // Compute AABB center for this single mesh
                    Vector3 mn = verts[0].Position;
                    Vector3 mx = verts[0].Position;
                    for (int vi = 1; vi < verts.Length; vi++)
                    {
                        mn = Vector3.Min(mn, verts[vi].Position);
                        mx = Vector3.Max(mx, verts[vi].Position);
                    }
                    Vector3 meshCenter = (mn + mx) * 0.5f;

                    // Store the original mesh center for BakeOne offset computation
                    meshCenters[mi] = meshCenter;

                    // Center this single mesh by its own AABB center
                    for (int vi = 0; vi < verts.Length; vi++)
                        verts[vi].Position -= meshCenter;
                }
            }

            return meshCenters;
        }

        /// <summary>
        /// Create multiple random instances of named meshes from a loaded asset.
        /// Each instance is placed at a random position within a circular radius,
        /// with a random yaw rotation. Uses a spatial grid (terrain chunk dimensions)
        /// for O(n) overlap detection + Parallel.For for multi-threaded position generation.
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
        /// <param name="minOverlapDist">
        ///   Minimum distance between instance centers (XZ plane) to prevent overlap.
        ///   If ≤ 0, auto-computed from the variant's local AABB diagonal.
        ///   When auto-compute is active, instances are separated by at least the
        ///   combined radii of both instances' AABB XZ diagonals.
        /// </param>
        /// <param name="overlapRetries">
        ///   Max retries per instance when overlap detected. Default 8.
        ///   If no valid position found after all retries, the instance is skipped.
        /// </param>
        /// <param name="collisionSizeX">
        ///   Override collision box width (X axis). 0 = use full mesh AABB.
        ///   Set to e.g. 1.2f for narrow trunk-only collision (trees).
        /// </param>
        /// <param name="collisionSizeZ">
        ///   Override collision box depth (Z axis). 0 = use full mesh AABB.
        /// </param>
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
            Action<float, string>? onProgress = null,
            float minOverlapDist = 0f,
            int overlapRetries = 3,
            float collisionSizeX = 0f,
            float collisionSizeZ = 0f,
            bool allowOverlap = false,
            float scale = 1f)
        {
            if (manager == null || asset == null || meshVariants == null || meshVariants.Length == 0)
                return;

            Vector3 ctr = center ?? Vector3.Zero;

            // Compute terrain map bounds
            float halfMapWorld = (TerrainChunk.ChunksPerSide * TerrainChunk.ChunkSize / 2f) * TerrainChunk.TerrainScale;
            float mapMargin = halfMapWorld * 0.01f;
            float boundMin = -halfMapWorld + mapMargin;
            float boundMax = halfMapWorld - mapMargin;

            // Pre-compute collision radius for each variant from its local AABB
            var variantRadii = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (string vn in meshVariants)
            {
                int[]? idxs = asset.GetMesh(vn);
                if (idxs == null || idxs.Length == 0) continue;

                var groupAABB = ComputeLocalAABB(idxs,
                    asset.GpuData.MeshToNode, asset.GpuData.Data.Nodes,
                    asset.GpuData.Data.Meshes, false);

                float halfX = (groupAABB.Max.X - groupAABB.Min.X) * 0.5f;
                float halfZ = (groupAABB.Max.Z - groupAABB.Min.Z) * 0.5f;
                variantRadii[vn] = MathF.Sqrt(halfX * halfX + halfZ * halfZ);
            }

            // ── Phase 1: Parallel position generation with spatial grid ──
            // Build grid matching terrain chunk dimensions
            int gridCells = TerrainChunk.ChunksPerSide;
            float cellSize = TerrainChunk.ChunkSize * TerrainChunk.TerrainScale;
            int halfMapSize = (TerrainChunk.ChunksPerSide * TerrainChunk.ChunkSize) / 2;
            float originX = -halfMapSize * TerrainChunk.TerrainScale;
            float originZ = -halfMapSize * TerrainChunk.TerrainScale;
            float invCellSize = 1f / cellSize;

            var grid = new List<int>[gridCells, gridCells];
            for (int gx = 0; gx < gridCells; gx++)
                for (int gz = 0; gz < gridCells; gz++)
                    grid[gx, gz] = new List<int>();

            // Pre-allocate result arrays
            var resultVariants = new string[count];
            var resultPosX = new float[count];
            var resultPosZ = new float[count];
            var resultYaw = new float[count];
            var resultValid = new bool[count];

            var gridLock = new object();
            int maxMapAttempts = 32;
            int progressInterval = Math.Max(count / 10000, 1); // report every ~1k objects

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            // Thread-safe counter for Phase 1 progress
            int phase1Generated = 0;

            Parallel.For(0, count, parallelOptions,
                () => new Random(),
                (i, loop, localRng) =>
                {
                    for (int attempt = 0; attempt < maxMapAttempts * (1 + overlapRetries); attempt++)
                    {
                        string variant = meshVariants[localRng.Next(meshVariants.Length)];

                        float a = (float)(localRng.NextDouble() * Math.PI * 2);
                        float d = (float)(localRng.NextDouble() * radius);
                        float px = ctr.X + MathF.Cos(a) * d;
                        float pz = ctr.Z + MathF.Sin(a) * d;

                        px = Math.Clamp(px, boundMin, boundMax);
                        pz = Math.Clamp(pz, boundMin, boundMax);

                        // Compute grid cell
                        int gx = (int)((px - originX) * invCellSize);
                        int gz = (int)((pz - originZ) * invCellSize);
                        gx = Math.Clamp(gx, 0, gridCells - 1);
                        gz = Math.Clamp(gz, 0, gridCells - 1);

                        // When allowOverlap=true, skip overlap check entirely
                        if (allowOverlap)
                        {
                            resultVariants[i] = variant;
                            resultPosX[i] = px;
                            resultPosZ[i] = pz;
                            resultYaw[i] = (float)(localRng.NextDouble() * 360);
                            resultValid[i] = true;

                            int gen = Interlocked.Increment(ref phase1Generated);
                            if (gen % progressInterval == 0)
                                Console.WriteLine($"[Phase1] Generated {gen} / {count} positions...");
                            break;
                        }

                        float vRadius = variantRadii.GetValueOrDefault(variant, 1f);
                        float neededSep = minOverlapDist > 0f ? minOverlapDist : vRadius * 2.2f;

                        // Check overlap against neighbour cells (thread-safe via lock)
                        bool overlap;
                        lock (gridLock)
                        {
                            overlap = false;
                            for (int nx = -1; nx <= 1 && !overlap; nx++)
                            {
                                for (int nz = -1; nz <= 1 && !overlap; nz++)
                                {
                                    int cx = gx + nx;
                                    int cz = gz + nz;
                                    if (cx < 0 || cx >= gridCells || cz < 0 || cz >= gridCells) continue;
                                    foreach (int idx in grid[cx, cz])
                                    {
                                        float dx = px - resultPosX[idx];
                                        float dz = pz - resultPosZ[idx];
                                        float existRadius = variantRadii.GetValueOrDefault(resultVariants[idx], 1f);
                                        float minDist = neededSep + existRadius * 2.2f;
                                        if (dx * dx + dz * dz < minDist * minDist)
                                        {
                                            overlap = true;
                                            break;
                                        }
                                    }
                                }
                            }

                            if (!overlap)
                            {
                                grid[gx, gz].Add(i);
                                resultVariants[i] = variant;
                                resultPosX[i] = px;
                                resultPosZ[i] = pz;
                                resultYaw[i] = (float)(localRng.NextDouble() * 360);
                                resultValid[i] = true;
                            }
                        }

                        if (!overlap)
                        {
                            int gen = Interlocked.Increment(ref phase1Generated);
                            if (gen % progressInterval == 0)
                                Console.WriteLine($"[Phase1] Generated {gen} / {count} positions...");
                            break;
                        }
                    }

                    return localRng;
                },
                localRng => { }
            );

            // Report Phase 1 completion
            if (onProgress != null)
            {
                float phase1P = progressMin + ((float)phase1Generated / count) * (progressMax - progressMin);
                onProgress(phase1P, $"Static: generating {progressLabel} positions done ({phase1Generated} valid)");
            }

            // ── Phase 2: Create instances, set up collision, report progress (main thread) ──
            int createdCount = 0;

            for (int i = 0; i < count; i++)
            {
                if (!resultValid[i]) continue;

                var sobj = CreateInstance(
                    manager, asset, resultVariants[i],
                    new Vector3(resultPosX[i], 0, resultPosZ[i]),
                    resultYaw[i], scale, terrain, yOffset);

                if (sobj != null)
                {
                    createdCount++;
                    configureInstance?.Invoke(sobj);


                }

                if (onProgress != null && (i % progressInterval == 0 || i == count - 1))
                {
                    float p = progressMin + ((i + 1f) / count) * (progressMax - progressMin);
                    onProgress(p, $"Static: loading {progressLabel} ({createdCount} trees)");
                }
            }
        }


    }
}
