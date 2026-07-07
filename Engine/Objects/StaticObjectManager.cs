using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    public class StaticObjectGroup
    {
        public string BaseName = "";
        public Dictionary<int, List<int>> Lods = new();
        // Pre-computed LOD fallback: for target LOD 0-3, which actual LOD to use
        public int[] LodFallback = [0, 1, 2, 3];
        // Highest LOD level available in this group (for auto-cull at max distance)
        public int MaxLOD = 3;
        // Per-group local AABB (computed from meshes belonging to this group only)
        public AABB LocalAABB;
    }

    public class StaticObject
    {
        public GltfModelGpuData GpuData;
        public StaticObjectGroup Group;
        public Vector3 Position;
        public Quaternion Rotation;
        public Quaternion CorrectionQuat = Quaternion.Identity;
        public float Scale = 1f;

        // Pre-computed once (static objects never move)
        public AABB CachedWorldAABB;
        public Matrix4x4 CachedBaseWorldMat;

        // Collision: nama mesh part yang dipakai untuk collision AABB (misal "bark")
        // Null/kosong = pakai full AABB (semua mesh)
        public string? CollisionPart = null;
        // Cached collision AABB (hanya dari mesh yang namanya mengandung CollisionPart)
        // Null = pakai CachedWorldAABB
        public AABB? CachedCollisionAABB = null;

        // Manual override ukuran collision AABB (0 = tidak di-override, pakai computed AABB)
        // OverrideSizeX = lebar (sumbu X), OverrideSizeZ = panjang/depth (sumbu Z)
        // Tinggi (sumbu Y) tetap menggunakan hasil compute dari mesh.
        public float OverrideCollisionSizeX = 0f;
        public float OverrideCollisionSizeZ = 0f;

        // Collision type (Box by default for static objects)
        public CollisionType ColType = CollisionType.Box;

        // Per-instance flags
        public bool CastShadow = true;
        public bool UseAlphaTest = true;
        public bool UseLocalMatrix = false;

        // Current LOD level being rendered (set every frame by StaticObjectManager.Draw)
        public int CurrentLOD = 0;

        // Occlusion culling flag (di-set oleh OC system setiap frame)
        public bool IsVisible = true;

        // Apakah object ini bisa menjadi occluder (menghalangi object lain)
        public bool IsOccluder = false;

        // Apakah object ini bisa ditabrak (collision untuk player/NPC/camera)
        public bool IsCollidable = false;

        // BVH collision: more accurate mesh-based collision detection
        // If null, falls back to AABB collision
        public BVH? CollisionBVH = null;

        public StaticObject(GltfModelGpuData gpuData, StaticObjectGroup group, Vector3 pos, float yaw, float scale)
        {
            GpuData = gpuData;
            Group = group;
            Position = pos;
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180f);
            Scale = scale;
        }
    }

    public unsafe class StaticObjectManager
    {
        public int GetObjectDrawn => ObjectDrawn;
        public int GetTotalObject => TotalObject;
        public int RenderedTriangles => _renderedTriangles;
        public int TotalTriangles => _totalTriangles;
        private int ObjectDrawn = 0;
        private int TotalObject = 0;
        private int _renderedTriangles = 0;
        private int _totalTriangles = 0;
        private readonly Dictionary<string, GltfModelGpuData> _modelCache = [];
        private readonly Dictionary<string, List<StaticObjectGroup>> _modelGroups = [];
        private readonly List<StaticObject> _objects = [];
        private readonly uint _shaderProgram;

        // ── Spatial Grid ──
        private const float _gridCellSize = 16f;       // 16×16m cells
        private float _gridVisibleRange = LOD3_Dist;  // Max visible range (matches LOD3_Distance by default)
        /// <summary>Override for grid visible range. Set > 0 to extend beyond LOD3_Dist (e.g. for impostor coverage).</summary>
        public float GridVisibleRange = -1f;  // -1 = use default (_gridVisibleRange = LOD3_Dist)
        private List<int>[,] _gridCells;
        private float _gridOriginX, _gridOriginZ;       // Stored for cell-center distance check in Draw

        // ── HLOD (per-instance, controlled by UseHLOD property) ──
        /// <summary>Enable HLOD merged meshes for this manager (per-instance, not global).</summary>
        public bool UseHLOD = false;
        private float _hlodRegionSize = 64f;     // 64×64m HLOD regions (adjusted when UseTerrainGrid)
        /// <summary>Near range: individual instancing (HLOD starts rendering & fading beyond this).</summary>
        public float HLODNearDist = LOD0_Dist;
        /// <summary>Mid range: HLOD merged meshes fully faded out at this distance.</summary>
        public float HLODMidDist = LOD3_Dist;
        private HlodRegion[,] _hlodRegions;
        private bool _hlodEnabled = false ;

        // ── HLOD Statistics (populated after BuildHLOD) ──
        private int _hlodTotalIndividualTris = 0;       // Total triangles if ALL objects rendered individually at LOD0
        private int _hlodTotalMergedTris = 0;           // Total triangles in merged HLOD meshes
        private int _hlodRegionCount = 0;               // Number of non-empty HLOD regions
        private int _hlodTotalObjects = 0;              // Total objects covered by HLOD
        private int _hlodVisibleRegions = 0;            // Regions that passed frustum + distance cull this frame
        public int HLODTotalIndividualTris => _hlodTotalIndividualTris;
        public int HLODTotalMergedTris => _hlodTotalMergedTris;
        public int HLODRegionCount => _hlodRegionCount;
        public int HLODTotalObjects => _hlodTotalObjects;
        public int HLODVisibleRegions => _hlodVisibleRegions;
        // -- Octahedral Impostors (per-instance, independent of HLOD) --
        /// <summary>Enable Octahedral Impostors for this manager.</summary>
        public bool UseImpostors = false;
        private bool _impostorsBuilt = false;
        private readonly List<ImpostorEntry> _impostorEntries = [];
        /// <summary>Near distance: impostors start fading in beyond this.</summary>
        public float ImpostorNearDist = 300f;
        /// <summary>Distance range for impostor cross-fade (overlap with individual rendering).</summary>
        public float ImpostorFadeDist = 0f;
        /// <summary>Far distance: impostors culled beyond this.</summary>
        public float ImpostorFarDist = 600f;
        /// <summary>Total non-empty impostor entries baked.</summary>
        public int ImpostorRegionCount => _impostorEntries.Count;
        /// <summary>Visible impostor regions this frame (pass distance + frustum cull).</summary>
        public int ImpVisibleRegions { get; private set; } = 0;
        private const int IMPOSTOR_ATLAS_W = 1024;
        private const int IMPOSTOR_ATLAS_H = 1024;
        private const int IMPOSTOR_ATLAS_COUNT = 8;
        private uint _impShaderProgram = 0;
        // Impostor shader uniform locations — matching impostor_vertex.glsl and impostor_fragment.glsl
        private int _impViewLoc = -1, _impProjLoc = -1;
        private int _impCenterLoc = -1, _impRadiusLoc = -1;
        private int _impCameraRightLoc = -1, _impCameraUpLoc = -1;
        private int _impAtlasLoc = -1, _impAtlasTilesLoc = -1;
        private int _impViewPosLoc = -1, _impSunDirLoc = -1;
        private int _impLightColorLoc = -1, _impFogColorLoc = -1;
        private int _impUseFogLoc = -1, _impRealSunDirLoc = -1, _impDebugOpacityLoc = -1;
        private static uint _impBillboardVAO = 0;
        private static bool _impBillboardVAOSetup = false;
        private static uint _atlasPreviewVAO = 0;
        private static uint _atlasPreviewVBO = 0;

        // ── Shared identity instance VBO for HLOD meshes ──
        // The static_vertex.glsl shader reads model matrix from instanced attributes
        // at locations 5-8. HLOD meshes (non-instanced) need identity at these locations.
        private static uint _identityInstanceVBO = 0;
        private static bool _identityInstanceVBOSetup = false;

        private static void EnsureIdentityInstanceVBO()
        {
            if (_identityInstanceVBOSetup) return;
            _identityInstanceVBOSetup = true;

            // One identity Matrix4x4 (16 floats)
            float[] identity = [
                1f, 0f, 0f, 0f,
                0f, 1f, 0f, 0f,
                0f, 0f, 1f, 0f,
                0f, 0f, 0f, 1f
            ];

            uint vbo;
            GL.GenBuffers(1, &vbo);
            _identityInstanceVBO = vbo;
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _identityInstanceVBO);
            fixed (float* p = identity)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(16 * sizeof(float)), p, Const.GL_STATIC_DRAW);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, 0);
        }
        private static void SetupImpostorVAO()
        {
            if (_impBillboardVAOSetup) return;
            _impBillboardVAOSetup = true;
            // Unit quad: vec2 position, vec2 uv (shader expects vec2 aPos at location 0)
            float[] verts = [
                -1f, -1f,  0f, 0f,
                 1f, -1f,  1f, 0f,
                 1f,  1f,  1f, 1f,
                -1f,  1f,  0f, 1f
            ];
            uint[] idx = [0, 1, 2, 0, 2, 3];
            uint vao, vbo, ebo;
            GL.GenVertexArrays(1, &vao);
            GL.GenBuffers(1, &vbo);
            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
            fixed (float* p = verts)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(verts.Length * sizeof(float)), p, Const.GL_STATIC_DRAW);
            GL.GenBuffers(1, &ebo);
            GL.BindBuffer(Const.GL_ELEMENT_ARRAY_BUFFER, ebo);
            fixed (uint* ip = idx)
                GL.BufferData(Const.GL_ELEMENT_ARRAY_BUFFER, (nuint)(idx.Length * sizeof(uint)), ip, Const.GL_STATIC_DRAW);
            // Location 0: vec2 aPos (stride = 4 floats = 16 bytes)
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)0);
            // Location 1: vec2 UV (stride = 4 floats = 16 bytes)
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
            GL.BindVertexArray(0);
            _impBillboardVAO = vao;
        }

        private class HlodRegion
        {
            public MeshGpu MergedMesh;
            public AABB WorldAABB;
            public int ObjectCount;
            public float CenterX, CenterZ;
        }
        private class ImpostorEntry
        {
            public uint AtlasTexture;
            public Vector3 WorldPosition;
            public float WorldRadius;
            public AABB WorldAABB;
        }

        // Shadow map uniforms
        private readonly int _shadowMap0Loc;
        private readonly int _shadowMap1Loc;
        private readonly int _shadowMap2Loc;
        private readonly int _lightSpaceLoc0;
        private readonly int _lightSpaceLoc1;
        private readonly int _lightSpaceLoc2;
        private readonly int _cascadeEndsLoc0;
        private readonly int _cascadeEndsLoc1;
        private readonly int _cascadeEndsLoc2;

        public bool CastShadow = true;
        public bool UseAlpha = true;

        // Object distance thresholds (dari Config, bisa diedit di satu tempat)
        private static float LOD0_Dist => DarkEngine3D_gl_csharp.Engine.Config.LODConfig.ObjectLOD0_Distance;
        private static float LOD1_Dist => DarkEngine3D_gl_csharp.Engine.Config.LODConfig.ObjectLOD1_Distance;
        private static float LOD2_Dist => DarkEngine3D_gl_csharp.Engine.Config.LODConfig.ObjectLOD2_Distance;
        private static float LOD3_Dist => DarkEngine3D_gl_csharp.Engine.Config.LODConfig.ObjectLOD3_Distance;

        public bool CullAtMaxLOD = false;
        // Skip terrain ray-march test untuk object kecil di tanah (daisies, grass, dll)
        // — object tetap ikut AABB occlusion test terhadap wall/occluders (Phase 2B)
        public bool SkipTerrainRayMarch = false;

        /// <summary>Enable spatial grid optimization for this manager (reduces per-frame iteration).</summary>
        public bool EnableSpatialGrid = false;

        /// <summary>
        /// If true, use TerrainChunk chunk dimensions for the grid instead of computing
        /// bounds from object positions. Set for objects placed on terrain (daisies, grass).
        /// Grid dimensions become ChunksPerSide × ChunksPerSide with cell size = ChunkSize × TerrainScale.
        /// </summary>
        public bool UseTerrainGrid = false;

        public IReadOnlyList<StaticObject> GetObjects() => _objects;

        public Vector3 RotationCorrection = Vector3.Zero;
        public bool UseNodeHierarchy =  false;

        private readonly int _modelLoc, _viewLoc, _projLoc;
        private readonly int _sunDirLoc, _realSunDirLoc, _lightColorLoc, _viewPosLoc;
        private readonly int _baseColorLoc, _useAlbedoLoc, _albedoMapLoc;

        // PBR uniforms
        private readonly int _normalMapLoc;
        private readonly int _metallicRoughnessMapLoc;
        private readonly int _occlusionMapLoc;
        private readonly int _emissiveMapLoc;
        private readonly int _metallicFactorLoc;
        private readonly int _roughnessFactorLoc;
        private readonly int _emissiveFactorLoc;
        private readonly int _normalScaleLoc;
        private readonly int _occlusionStrengthLoc;
        private readonly int _hasNormalTextureLoc;
        private readonly int _hasMetallicRoughnessTextureLoc;
        private readonly int _hasOcclusionTextureLoc;
        private readonly int _hasEmissiveTextureLoc;
        private readonly int _hlodAlphaLoc;

        // Instance batching cache: (gpuDataHash, meshIdx) -> instance VBO handle
        private readonly Dictionary<(int, int), uint> _instanceVBOs = [];

        // Reusable instance lists — cleared each frame to avoid re-allocation
        private class InstanceGroup
        {
            public readonly List<Matrix4x4> Mats = [];
            public MeshGpu Mesh;
            public GltfModelGpuData Gpu;
        }
        private readonly Dictionary<(int gpuHash, int meshIdx, int nodeIdx), InstanceGroup> _drawInstanceLists = [];
        private readonly Dictionary<(int gpuHash, int meshIdx, int nodeIdx, bool hasAlpha), InstanceGroup> _shadowInstanceLists = [];

        // Reusable matrix buffer for UploadInstances (avoids [..mats] copy alloc)
        private Matrix4x4[] _matrixUploadBuffer = [];

        public StaticObjectManager()
        {
            _shaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/static_vertex.glsl",
                "Artifacts/shaders/gltf_fragment.glsl"
            );

            _modelLoc = GL.GetUniformLocation(_shaderProgram, "model");
            _viewLoc = GL.GetUniformLocation(_shaderProgram, "view");
            _projLoc = GL.GetUniformLocation(_shaderProgram, "projection");
            _sunDirLoc = GL.GetUniformLocation(_shaderProgram, "sunDir");
            _realSunDirLoc = GL.GetUniformLocation(_shaderProgram, "realSunDir");
            _lightColorLoc = GL.GetUniformLocation(_shaderProgram, "lightColor");
            _viewPosLoc = GL.GetUniformLocation(_shaderProgram, "viewPos");
            _baseColorLoc = GL.GetUniformLocation(_shaderProgram, "baseColorFactor");
            _useAlbedoLoc = GL.GetUniformLocation(_shaderProgram, "useAlbedo");
            _albedoMapLoc = GL.GetUniformLocation(_shaderProgram, "albedoMap");

            _shadowMap0Loc = GL.GetUniformLocation(_shaderProgram, "shadowMap0");
            _shadowMap1Loc = GL.GetUniformLocation(_shaderProgram, "shadowMap1");
            _shadowMap2Loc = GL.GetUniformLocation(_shaderProgram, "shadowMap2");
            _lightSpaceLoc0 = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[0]");
            _lightSpaceLoc1 = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[1]");
            _lightSpaceLoc2 = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[2]");
            _cascadeEndsLoc0 = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[0]");
            _cascadeEndsLoc1 = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[1]");
            _cascadeEndsLoc2 = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[2]");

            _normalMapLoc = GL.GetUniformLocation(_shaderProgram, "normalMap");
            _metallicRoughnessMapLoc = GL.GetUniformLocation(_shaderProgram, "metallicRoughnessMap");
            _occlusionMapLoc = GL.GetUniformLocation(_shaderProgram, "occlusionMap");
            _emissiveMapLoc = GL.GetUniformLocation(_shaderProgram, "emissiveMap");
            _metallicFactorLoc = GL.GetUniformLocation(_shaderProgram, "metallicFactor");
            _roughnessFactorLoc = GL.GetUniformLocation(_shaderProgram, "roughnessFactor");
            _emissiveFactorLoc = GL.GetUniformLocation(_shaderProgram, "emissiveFactor");
            _normalScaleLoc = GL.GetUniformLocation(_shaderProgram, "normalScale");
            _occlusionStrengthLoc = GL.GetUniformLocation(_shaderProgram, "occlusionStrength");
            _hasNormalTextureLoc = GL.GetUniformLocation(_shaderProgram, "hasNormalTexture");
            _hasMetallicRoughnessTextureLoc = GL.GetUniformLocation(_shaderProgram, "hasMetallicRoughnessTexture");
            _hasOcclusionTextureLoc = GL.GetUniformLocation(_shaderProgram, "hasOcclusionTexture");
            _hasEmissiveTextureLoc = GL.GetUniformLocation(_shaderProgram, "hasEmissiveTexture");
            _hlodAlphaLoc = GL.GetUniformLocation(_shaderProgram, "hlodAlpha");
            // -- Octahedral Impostor shader --
            try
            {
                _impShaderProgram = Helpers.ShaderHelpers.LoadShader(
                    "Artifacts/shaders/impostor_vertex.glsl",
                    "Artifacts/shaders/impostor_fragment.glsl"
                );
                if (_impShaderProgram != 0)
                {
                    _impViewLoc = GL.GetUniformLocation(_impShaderProgram, "view");
                    _impProjLoc = GL.GetUniformLocation(_impShaderProgram, "projection");
                    _impCenterLoc = GL.GetUniformLocation(_impShaderProgram, "center");
                    _impRadiusLoc = GL.GetUniformLocation(_impShaderProgram, "radius");
                    _impCameraRightLoc = GL.GetUniformLocation(_impShaderProgram, "cameraRight");
                    _impCameraUpLoc = GL.GetUniformLocation(_impShaderProgram, "cameraUp");
                    _impAtlasLoc = GL.GetUniformLocation(_impShaderProgram, "impostorAtlas");
                    _impAtlasTilesLoc = GL.GetUniformLocation(_impShaderProgram, "atlasTiles");
                    _impViewPosLoc = GL.GetUniformLocation(_impShaderProgram, "viewPos");
                    _impSunDirLoc = GL.GetUniformLocation(_impShaderProgram, "sunDir");
                    _impLightColorLoc = GL.GetUniformLocation(_impShaderProgram, "lightColor");
                    _impFogColorLoc = GL.GetUniformLocation(_impShaderProgram, "fogColor");
                    _impUseFogLoc = GL.GetUniformLocation(_impShaderProgram, "useFog");
                    _impRealSunDirLoc = GL.GetUniformLocation(_impShaderProgram, "realSunDir");
                    _impDebugOpacityLoc = GL.GetUniformLocation(_impShaderProgram, "debugOpacity");
                }
            }
            catch (System.Exception ex)
            {
                Console.Error.WriteLine("[Impostor] Failed to load shader: " + ex.Message);
                _impShaderProgram = 0;
            }
        }

        private void AnalyzeGltfGroups(string path, GltfModelGpuData gpuData)
        {
            if (_modelGroups.ContainsKey(path)) return;

            var nodes = gpuData.Data.Nodes;
            var groups = new List<StaticObjectGroup>();

            // ── Helper: recursively collect mesh indices under a node (LOD variants included,
            // since vertex clustering already handles LOD generation).
            HashSet<int> CollectSubtreeMeshes(int nodeIdx)
            {
                var result = new HashSet<int>();
                if (nodes == null || nodeIdx < 0 || nodeIdx >= nodes.Length) return result;

                var stack = new Stack<int>();
                stack.Push(nodeIdx);

                while (stack.Count > 0)
                {
                    int idx = stack.Pop();
                    if (idx < 0 || idx >= nodes.Length) continue;

                    if (nodes[idx].Mesh >= 0 && nodes[idx].Mesh < gpuData.Data.Meshes.Length)
                        result.Add(nodes[idx].Mesh);

                    foreach (var child in nodes[idx].Children)
                        stack.Push(child);
                }

                return result;
            }

            // ── Pass 1: create per-node groups for top-level children that have meshes ──
            // A "top-level" node is any node whose parent has a DIFFERENT mesh set,
            // or is the root (parent == -1). We skip nodes whose parent has the exact
            // same mesh set to avoid duplicates (e.g. a single-mesh model).
            if (nodes != null && nodes.Length > 0)
            {
                // Pre-compute mesh sets for all nodes
                var nodeMeshes = new Dictionary<int, int[]>();
                for (int ni = 0; ni < nodes.Length; ni++)
                {
                    var ms = CollectSubtreeMeshes(ni);
                    if (ms.Count > 0)
                        nodeMeshes[ni] = [.. ms];
                }

                foreach (var kv in nodeMeshes)
                {
                    int ni = kv.Key;
                    var meshes = kv.Value;

                    // Skip if parent has the exact same mesh set (duplicate / already covered)
                    int parent = nodes[ni].Parent;
                    if (parent >= 0 && nodeMeshes.TryGetValue(parent, out var parentMeshes))
                    {
                        if (parentMeshes.Length == meshes.Length &&
                            parentMeshes.OrderBy(m => m).SequenceEqual(meshes.OrderBy(m => m)))
                            continue;
                    }

                    // Skip container nodes that have no mesh of their own
                    // and only exist to group children (e.g. "Scene" root nodes).
                    if (nodes[ni].Mesh < 0 && nodes[ni].Children.Length > 0 && meshes.Length > 1)
                        continue;

                    string groupName = nodes[ni].Name ?? $"node_{ni}";

                    var g = new StaticObjectGroup { BaseName = groupName };
                    g.Lods[1] = [.. meshes];
                    g.MaxLOD = 1;
                    for (int t = 0; t < 4; t++)
                        g.LodFallback[t] = 1;
                    g.LocalAABB = ComputeGroupAABB(meshes, gpuData.MeshToNode, nodes, gpuData.Data.Meshes, UseNodeHierarchy);

                    groups.Add(g);
                }
            }

            // ── Pass 2: create "root" group with ALL meshes (LOD variants included).
            // LOD filtering is disabled because vertex clustering handles LOD generation.
            var rootGroup = new StaticObjectGroup { BaseName = "root" };
            rootGroup.Lods[1] = new List<int>();

            for (int i = 0; i < gpuData.Data.Meshes.Length; i++)
                rootGroup.Lods[1].Add(i);

            rootGroup.MaxLOD = 1;
            for (int t = 0; t < 4; t++)
                rootGroup.LodFallback[t] = 1;
            rootGroup.LocalAABB = ComputeGroupAABB([.. rootGroup.Lods[1]], gpuData.MeshToNode, nodes, gpuData.Data.Meshes, UseNodeHierarchy);

            // Insert root at the beginning (so it's the default first option)
            groups.Insert(0, rootGroup);

            // ── Fallback: if no per-node groups were created, use filename as group name ──
            // This preserves backward compatibility for single-mesh models.
            if (groups.Count == 1) // only root
            {
                string fallbackName = Path.GetFileNameWithoutExtension(path);
                rootGroup.BaseName = string.IsNullOrEmpty(fallbackName) ? "default" : fallbackName;

                // Also compute AABB for root (may differ from gpuData.LocalAABB which includes LOD variants)
                // Already done above via ComputeGroupAABB
            }

            // ── Log available groups ──
            var groupNames = string.Join(", ", groups.Select(g => g.BaseName));
            //Console.WriteLine($"[Groups] '{path}': {groupNames} ({groups.Count} groups, {rootGroup.Lods[1].Count} root meshes)");

            _modelGroups[path] = groups;
        }

        public void AddObject(string path, Vector3 pos, float yaw = 0, float scale = 1.0f, string groupName = "", bool snapToTerrain = false, TerrainChunk? terrain = null, string? collisionPart = null, float overrideCollisionSizeX = 0f, float overrideCollisionSizeZ = 0f)
        {
            if (!_modelCache.TryGetValue(path, out var gpuData))
            {
                var data = GltfLoader.Load(path);
                gpuData = new GltfModelGpuData(data, true);
                _modelCache[path] = gpuData;
                AnalyzeGltfGroups(path, gpuData);
            }

            var availableGroups = _modelGroups[path];
            if (availableGroups.Count == 0) return;

            StaticObjectGroup selectedGroup;
            if (string.IsNullOrEmpty(groupName))
            {
                // Pick random from non-root groups (variants like tree names)
                var nonRootGroups = availableGroups
                    .Where(g => !g.BaseName.Equals("root", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (nonRootGroups.Count > 0)
                {
                    var rng = new Random();
                    selectedGroup = nonRootGroups[rng.Next(nonRootGroups.Count)];
                }
                else
                {
                    selectedGroup = availableGroups[0];
                }
            }
            else
            {
                // Support comma-separated list: "pohon1,pohon2,pohon3" → pick random one
                var names = groupName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var rng = new Random();
                string chosen = names[rng.Next(names.Length)];

                // First, try to find an existing group that matches by node name
                selectedGroup = availableGroups.FirstOrDefault(g => g.BaseName.Equals(chosen, StringComparison.OrdinalIgnoreCase));

                if (selectedGroup == null)
                {
                    // No node group matches — search individual meshes by name instead
                    var matchingIndices = new List<int>();
                    // Strip _LOD + everything after it to get the variant's base name
                    // e.g. "Christmas tree_LOD0" → "Christmas tree"
                    //      "Christmas tree_2_LOD0_Bark_Mat_0" base would be "Christmas tree_2"
                    string chosenBase = System.Text.RegularExpressions.Regex.Replace(chosen, @"_LOD\d+.*$", "",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    for (int mi = 0; mi < gpuData.Data.Meshes.Length; mi++)
                    {
                        string? meshName = gpuData.Data.Meshes[mi].Name;
                        if (string.IsNullOrEmpty(meshName)) continue;

                        // Match by StartsWith: handles material suffixes like _Bark_Mat_0, _Brunches_Mat_0
                        // e.g. "Christmas tree_LOD0_Bark_Mat_0" starts with chosen="Christmas tree_LOD0"
                        bool exactLODMatch = meshName.StartsWith(chosen, StringComparison.OrdinalIgnoreCase);

                        // Cross-LOD + material matching: strip _LOD+suffix from mesh name,
                        // then compare base names. This avoids false positives between
                        // "Christmas tree" and "Christmas tree_2" (which StartsWith would confuse).
                        bool baseMatch = false;
                        if (!string.IsNullOrEmpty(chosenBase))
                        {
                            string meshBase = System.Text.RegularExpressions.Regex.Replace(meshName, @"_LOD\d+.*$", "",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            baseMatch = meshBase.Equals(chosenBase, StringComparison.OrdinalIgnoreCase);
                        }

                        if (exactLODMatch || baseMatch)
                        {
                            matchingIndices.Add(mi);
                        }
                    }

                    if (matchingIndices.Count > 0)
                    {
                        // Filter out original GLB LOD[1-3] variants (single _LOD occurrence).
                        // Auto-generated vertex-clustering LODs have TWO _LOD occurrences.
                        var filtered = new List<int>();
                        foreach (int mi in matchingIndices)
                        {
                            string? mn = gpuData.Data.Meshes[mi].Name;
                            if (string.IsNullOrEmpty(mn)) { filtered.Add(mi); continue; }
                            int lodCount = 0, lastLodIdx = -1, searchFrom = 0;
                            while (searchFrom >= 0 && searchFrom < mn.Length)
                            {
                                int found = mn.IndexOf("_LOD", searchFrom, StringComparison.OrdinalIgnoreCase);
                                if (found < 0) break;
                                lodCount++;
                                lastLodIdx = found;
                                searchFrom = found + 4;
                            }
                            bool isOriginalLod = lodCount == 1 && lastLodIdx >= 0 && lastLodIdx + 4 < mn.Length
                                && mn[lastLodIdx + 4] >= '1' && mn[lastLodIdx + 4] <= '3';
                            if (!isOriginalLod) filtered.Add(mi);
                        }

                        // Sort into LOD buckets: LOD0 = original, LOD2/3 = auto-generated.
                        selectedGroup = new StaticObjectGroup { BaseName = chosen };
                        var lods = new Dictionary<int, List<int>>();
                        foreach (int mi in filtered)
                        {
                            string? meshName = gpuData.Data.Meshes[mi].Name ?? "";
                            int lodLevel = 0;
                            if (meshName.Contains("_LOD3", StringComparison.OrdinalIgnoreCase)) lodLevel = 3;
                            else if (meshName.Contains("_LOD2", StringComparison.OrdinalIgnoreCase)) lodLevel = 2;
                            else if (meshName.Contains("_LOD1", StringComparison.OrdinalIgnoreCase)) lodLevel = 1;
                            if (!lods.ContainsKey(lodLevel)) lods[lodLevel] = new List<int>();
                            lods[lodLevel].Add(mi);
                        }
                        int maxLod = 0;
                        foreach (var kv in lods)
                        {
                            selectedGroup.Lods[kv.Key] = kv.Value;
                            if (kv.Key > maxLod) maxLod = kv.Key;
                        }
                        selectedGroup.MaxLOD = maxLod;
                        for (int t = 0; t < 4; t++)
                        {
                            int nearest = Math.Min(t, maxLod);
                            while (nearest >= 0 && !selectedGroup.Lods.ContainsKey(nearest))
                                nearest--;
                            selectedGroup.LodFallback[t] = Math.Max(nearest, 0);
                        }
                        selectedGroup.LocalAABB = ComputeGroupAABB(
                            [.. filtered], gpuData.MeshToNode, gpuData.Data.Nodes, gpuData.Data.Meshes, UseNodeHierarchy);
                    }
                    else
                    {
                        // Last resort: pick ONE random mesh from all available meshes
                        // rather than falling back to the root group which includes ALL meshes.
                        var lastRng = new Random();
                        int pickIdx = lastRng.Next(gpuData.Data.Meshes.Length);
                        selectedGroup = new StaticObjectGroup { BaseName = chosen };
                        selectedGroup.Lods[0] = [pickIdx];
                        selectedGroup.MaxLOD = 0;
                        for (int t = 0; t < 4; t++)
                            selectedGroup.LodFallback[t] = 0;
                        selectedGroup.LocalAABB = ComputeGroupAABB(
                            [pickIdx], gpuData.MeshToNode, gpuData.Data.Nodes, gpuData.Data.Meshes, UseNodeHierarchy);
                        Console.WriteLine($"[WARN] No matching group/mesh for '{chosen}' in '{path}' — using random mesh {pickIdx}");
                    }
                }
            }

            // Pre-compute correction quaternion for this manager.
            // NOTE: correction should NOT affect the world-space AABB size computation
            // other than via rotation only (handled by CachedBaseWorldMat).
            // (translation is applied by CachedBaseWorldMat later)
            float rx = RotationCorrection.X * MathF.PI / 180f;
            float ry = RotationCorrection.Y * MathF.PI / 180f;
            float rz = RotationCorrection.Z * MathF.PI / 180f;
            var corrQuat = Quaternion.CreateFromYawPitchRoll(ry, rx, rz);


            // Snap to terrain — use LOD0-only AABB for accurate ground contact.
            // Combined AABB (all LODs) may include LOD3 merged meshes with different
            // node transforms that inflate the bottom Y, causing floating/sinking.
            if (snapToTerrain && terrain != null)
            {
                float terrainY = terrain.GetHeightAt(pos.X, pos.Z);

                // noTrans = Scale * Correction * Yaw (matching CachedBaseWorldMat order)
                var yawQuat = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathF.PI / 180f);
                var noTrans = Matrix4x4.CreateScale(scale)
                            * Matrix4x4.CreateFromQuaternion(corrQuat)
                            * Matrix4x4.CreateFromQuaternion(yawQuat);

                // Compute AABB from LOD0 meshes only — these are the original high-detail
                // meshes whose node transforms match the actual object ground position.
                AABB snapAABB;
                if (selectedGroup.Lods.TryGetValue(0, out var lod0Meshes) && lod0Meshes.Count > 0)
                    snapAABB = ComputeGroupAABB([.. lod0Meshes], gpuData.MeshToNode, gpuData.Data.Nodes, gpuData.Data.Meshes, UseNodeHierarchy);
                else
                    snapAABB = selectedGroup.LocalAABB; // fallback to combined

                var rotatedAABB = snapAABB.Transform(noTrans);
                pos.Y = terrainY - rotatedAABB.Min.Y;

                //Console.WriteLine($"[Snap] group='{selectedGroup.BaseName}' lod0AABB.Min.Y={snapAABB.Min.Y:F4} max.Y={snapAABB.Max.Y:F4} " + $"(combined bottom={selectedGroup.LocalAABB.Min.Y:F4}) terrainY={terrainY:F4} -> pos.Y={pos.Y:F4}");
            }

            var sobj = new StaticObject(gpuData, selectedGroup, pos, yaw, scale);
            sobj.CorrectionQuat = corrQuat;
            // Pre-compute cached values (static objects never move)
            // Use per-group AABB instead of combined GPU AABB for better accuracy
            var localAABB = selectedGroup.LocalAABB.Min != selectedGroup.LocalAABB.Max
                ? selectedGroup.LocalAABB
                : sobj.GpuData.LocalAABB;
            sobj.CollisionPart = collisionPart;

            // IMPORTANT: CachedBaseWorldMat must match how vertices are transformed in Draw().
            // In particular, if UseNodeHierarchy=true, the per-mesh instance model matrix becomes:
            //   modelMat = (nodeWorldMatrix * baseWorldMat)
            // where baseWorldMat already includes correction/yaw/scale/translation.
            // Therefore AABB must be computed using node hierarchy as well.

            // Compute base/world matrix WITHOUT any node hierarchy contribution.
            sobj.CachedBaseWorldMat = Matrix4x4.CreateScale(sobj.Scale) *
                                       Matrix4x4.CreateFromQuaternion(sobj.CorrectionQuat) *
                                       Matrix4x4.CreateFromQuaternion(sobj.Rotation) *
                                       Matrix4x4.CreateTranslation(sobj.Position);

            // If node hierarchy is enabled, selectedGroup.LocalAABB was already computed in model-local space
            // including node transforms (ComputeGroupAABB uses GetNodeWorldMatrix when UseNodeHierarchy=true).
            // That means we only need to apply base world (translation/rotation/scale) here.
            sobj.CachedWorldAABB = localAABB.Transform(sobj.CachedBaseWorldMat);


            // Compute per-mesh collision AABB jika CollisionPart di-set
            // Pakai Transform(CachedBaseWorldMat) biar transform order sama persis dengan rendering
            if (!string.IsNullOrEmpty(collisionPart))
            {
                var collLocalAABB = ComputeCollisionLocalAABB(gpuData, collisionPart, sobj);
                sobj.CachedCollisionAABB = collLocalAABB.Transform(sobj.CachedBaseWorldMat);
            }

            // Apply manual override size untuk collision AABB (X dan Z saja, Y tetap)
            // Hanya berlaku jika CachedCollisionAABB != null dan override > 0
            if (sobj.CachedCollisionAABB.HasValue && (overrideCollisionSizeX > 0f || overrideCollisionSizeZ > 0f))
            {
                var ca = sobj.CachedCollisionAABB.Value;
                Vector3 center = (ca.Min + ca.Max) * 0.5f;
                Vector3 halfSize = (ca.Max - ca.Min) * 0.5f;
                if (overrideCollisionSizeX > 0f) halfSize.X = overrideCollisionSizeX * 0.5f;
                if (overrideCollisionSizeZ > 0f) halfSize.Z = overrideCollisionSizeZ * 0.5f;
                // Y tetap dari hasil compute mesh
                sobj.CachedCollisionAABB = new AABB(center - halfSize, center + halfSize);
            }
            // Copy Y dari CachedWorldAABB (tinggi sudah benar dari OC/frustum —
            // mencakup semua mesh group, bukan cuma mesh "bark")
            // Ini penting karena mesh "bark" mungkin tidak mencakup tinggi penuh pohon,
            // dan random rotation/position membuat AABB collision Y tidak akurat.
            if (sobj.CachedCollisionAABB.HasValue)
            {
                var ca = sobj.CachedCollisionAABB.Value;
                ca.Min.Y = sobj.CachedWorldAABB.Min.Y;
                ca.Max.Y = sobj.CachedWorldAABB.Max.Y;
                sobj.CachedCollisionAABB = ca;
            }

            // ── Count total triangles for this instance (LOD0 / most detailed) ──
            int instanceTriangles = 0;
            if (selectedGroup.Lods.TryGetValue(0, out var lod0Indices))
            {
                var meshes = gpuData.Data.Meshes;
                foreach (int mi in lod0Indices)
                {
                    if (mi >= 0 && mi < meshes!.Length)
                        instanceTriangles += meshes[mi].Indices.Length / 3;
                }
            }
            else
            {
                // Fallback: use the lowest available LOD level
                int lowestKey = selectedGroup.Lods.Keys.Min();
                if (selectedGroup.Lods.TryGetValue(lowestKey, out var fallbackIndices))
                {
                    var meshes = gpuData.Data.Meshes;
                    foreach (int mi in fallbackIndices)
                    {
                        if (mi >= 0 && mi < meshes!.Length)
                            instanceTriangles += meshes[mi].Indices.Length / 3;
                    }
                }
            }
            _totalTriangles += instanceTriangles;

            sobj.OverrideCollisionSizeX = overrideCollisionSizeX;
            sobj.OverrideCollisionSizeZ = overrideCollisionSizeZ;

            _objects.Add(sobj);
            TotalObject++;
        }

        public void AddRandomObjects(string path, int count, Vector3 center, float radius, float scale, TerrainChunk terrain, Action<float>? onProgress = null, string groupName = "", string? collisionPart = null, float overrideCollisionSizeX = 0f, float overrideCollisionSizeZ = 0f)
        {
            var rng = new Random();
            int reportInterval = Math.Max(count / 100, 1); // report ~100x selama loading
            for (int i = 0; i < count; i++)
            {
                float a = (float)(rng.NextDouble() * Math.PI * 2);
                float d = (float)(rng.NextDouble() * radius);
                float x = center.X + MathF.Cos(a) * d;
                float z = center.Z + MathF.Sin(a) * d;
                float y = terrain.GetHeightAt(x, z);
                AddObject(path, new Vector3(x, y, z), (float)(rng.NextDouble() * 360), scale, groupName, true, terrain, collisionPart: collisionPart, overrideCollisionSizeX: overrideCollisionSizeX, overrideCollisionSizeZ: overrideCollisionSizeZ);

                if (onProgress != null && (i % reportInterval == 0 || i == count - 1))
                    onProgress((float)(i + 1) / count);
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  Spatial Grid + optional HLOD Builder
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Partition objects into a 2D spatial grid for faster per-frame iteration.
        /// If UseHLOD is true, also builds HLOD merged meshes.
        /// Must be called after all objects are added.
        /// </summary>
        public void BuildSpatialGrid()
        {
            if (!EnableSpatialGrid || _objects.Count == 0) return;

            int gridW, gridH;
            float originX, originZ;

            if (UseTerrainGrid && TerrainChunk.ChunksPerSide > 0)
            {
                // Use terrain chunk grid — ChunksPerSide × ChunksPerSide
                // (e.g. 16×16 = 256 cells for a 256×256 map)
                gridW = TerrainChunk.ChunksPerSide;
                gridH = TerrainChunk.ChunksPerSide;
                int halfMapSize = (TerrainChunk.ChunksPerSide * TerrainChunk.ChunkSize) / 2;
                originX = -halfMapSize * TerrainChunk.TerrainScale;
                originZ = -halfMapSize * TerrainChunk.TerrainScale;
            }
            else
            {
                // Compute world bounds from actual object positions
                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                for (int i = 0; i < _objects.Count; i++)
                {
                    var pos = _objects[i].Position;
                    if (pos.X < minX) minX = pos.X;
                    if (pos.X > maxX) maxX = pos.X;
                    if (pos.Z < minZ) minZ = pos.Z;
                    if (pos.Z > maxZ) maxZ = pos.Z;
                }
                float worldSizeX = maxX - minX;
                float worldSizeZ = maxZ - minZ;
                if (worldSizeX < 1f || worldSizeZ < 1f) return;

                gridW = (int)Math.Ceiling(worldSizeX / _gridCellSize) + 1;
                gridH = (int)Math.Ceiling(worldSizeZ / _gridCellSize) + 1;
                originX = minX;
                originZ = minZ;
            }

            _gridCells = new List<int>[gridW, gridH];
            _gridOriginX = originX;
            _gridOriginZ = originZ;
            for (int gx = 0; gx < gridW; gx++)
                for (int gz = 0; gz < gridH; gz++)
                    _gridCells[gx, gz] = new List<int>();

            float invCellSize = 1f / _gridCellSize;
            for (int i = 0; i < _objects.Count; i++)
            {
                var pos = _objects[i].Position;
                int gx = (int)((pos.X - originX) * invCellSize);
                int gz = (int)((pos.Z - originZ) * invCellSize);
                gx = Math.Clamp(gx, 0, gridW - 1);
                gz = Math.Clamp(gz, 0, gridH - 1);
                _gridCells[gx, gz].Add(i);
            }

            Console.WriteLine($"[Grid] Built {gridW}×{gridH} grid for {_objects.Count} objects.");

            // ── Optional: Build HLOD merged meshes (per-instance flag) ──
            if (UseHLOD)
            {
                // Reset HLOD stats before building
                _hlodTotalIndividualTris = 0;
                _hlodTotalMergedTris = 0;
                _hlodRegionCount = 0;
                _hlodTotalObjects = 0;

                // Count individual tris for all objects (prefer lowest available LOD)
                foreach (var obj in _objects)
                {
                    if (obj.Group == null || obj.Group.Lods.Count == 0) continue;
                    int lodKey = obj.Group.Lods.ContainsKey(3) ? 3 : obj.Group.Lods.ContainsKey(2) ? 2 : obj.Group.Lods.ContainsKey(1) ? 1 : 0;
                    if (lodKey < 0 || !obj.Group.Lods.ContainsKey(lodKey)) lodKey = obj.Group.Lods.Keys.Min();
                    if (!obj.Group.Lods.TryGetValue(lodKey, out var miList)) continue;
                    var meshes = obj.GpuData.Data.Meshes;
                    if (meshes == null) continue;
                    foreach (int mi in miList)
                    {
                        if (mi >= 0 && mi < meshes!.Length)
                            _hlodTotalIndividualTris += meshes[mi].Indices.Length / 3;
                    }
                }
                _hlodTotalObjects = _objects.Count;

                BuildHLOD(originX, originZ);
            }
            // ── Optional: Build Octahedral Impostor atlas from HLOD regions ──
            if (UseImpostors)
            {
                BuildImpostors();
            }
        }

        /// <summary>Build HLOD merged meshes per region. Called after spatial grid is ready.</summary>
        private void BuildHLOD(float worldMinX, float worldMinZ)
        {
            int hlodCountX, hlodCountZ;

            if (UseTerrainGrid && TerrainChunk.ChunksPerSide > 0)
            {
                // Align HLOD regions to terrain chunks — reuse existing grid partition
                hlodCountX = _gridCells.GetLength(0);
                hlodCountZ = _gridCells.GetLength(1);
                _hlodRegionSize = TerrainChunk.ChunkSize * TerrainChunk.TerrainScale;
            }
            else
            {
                float worldSizeX = _gridCells.GetLength(0) * _gridCellSize;
                float worldSizeZ = _gridCells.GetLength(1) * _gridCellSize;
                hlodCountX = Math.Max(1, (int)Math.Ceiling(worldSizeX / _hlodRegionSize));
                hlodCountZ = Math.Max(1, (int)Math.Ceiling(worldSizeZ / _hlodRegionSize));
            }
            _hlodRegions = new HlodRegion[hlodCountX, hlodCountZ];

            // Collect object indices per HLOD region
            var hlodObjLists = new List<int>[hlodCountX, hlodCountZ];
            for (int hx = 0; hx < hlodCountX; hx++)
                for (int hz = 0; hz < hlodCountZ; hz++)
                    hlodObjLists[hx, hz] = new List<int>();

            if (UseTerrainGrid && TerrainChunk.ChunksPerSide > 0)
            {
                // Reuse grid cell partitions directly — each grid cell = one terrain chunk = one HLOD region
                for (int gx = 0; gx < hlodCountX; gx++)
                    for (int gz = 0; gz < hlodCountZ; gz++)
                        if (_gridCells[gx, gz] != null && _gridCells[gx, gz].Count > 0)
                            hlodObjLists[gx, gz].AddRange(_gridCells[gx, gz]);
            }
            else
            {
                for (int i = 0; i < _objects.Count; i++)
                {
                    var pos = _objects[i].Position;
                    int hx = (int)((pos.X - worldMinX) / _hlodRegionSize);
                    int hz = (int)((pos.Z - worldMinZ) / _hlodRegionSize);
                    hx = Math.Clamp(hx, 0, hlodCountX - 1);
                    hz = Math.Clamp(hz, 0, hlodCountZ - 1);
                    hlodObjLists[hx, hz].Add(i);
                }
            }

            Console.WriteLine($"[HLOD] Building {hlodCountX}×{hlodCountZ} regions ({_objects.Count} objects)...");
            int totalTris = 0;
            int nonEmptyCount = 0;

            for (int hx = 0; hx < hlodCountX; hx++)
            {
                for (int hz = 0; hz < hlodCountZ; hz++)
                {
                    var indices = hlodObjLists[hx, hz];
                    if (indices.Count == 0) continue;

                    var mergedVerts = new List<SkinnedVertex>();
                    var mergedIdx = new List<uint>();
                    Vector3 regMin = new(float.PositiveInfinity);
                    Vector3 regMax = new(float.NegativeInfinity);
                    var mat = default(MeshMaterialGpu);
                    bool matSet = false;
                    uint vertOff = 0;

                    foreach (int oi in indices)
                    {
                        var obj = _objects[oi];

                        if (obj.Group == null || obj.Group.Lods.Count == 0) continue;
                        // Use lowest available LOD (prefer LOD3 > LOD2 > LOD1 > LOD0) for memory-efficient merged meshes
                        int lodKey = obj.Group.Lods.ContainsKey(3) ? 3 : obj.Group.Lods.ContainsKey(2) ? 2 : obj.Group.Lods.ContainsKey(1) ? 1 : 0;
                        if (lodKey < 0 || !obj.Group.Lods.ContainsKey(lodKey)) lodKey = obj.Group.Lods.Keys.Min();
                        if (!obj.Group.Lods.TryGetValue(lodKey, out var miList) || miList.Count == 0)
                            continue;

                        var meshes = obj.GpuData.Data.Meshes;
                        if (meshes == null) continue;

                                foreach (int mi in miList)
                                {
                                    if (mi < 0 || mi >= meshes.Length) continue;
                                    var src = meshes[mi];
                                    if (src.Vertices.Length < 3) continue;

                                    if (!matSet && mi < obj.GpuData.Meshes.Length)
                                    {
                                        mat = obj.GpuData.Meshes[mi].Material;
                                        matSet = true;
                                    }

                                    // Compute model matrix same as in Draw() — apply node transform
                                    int nodeIdx = (obj.GpuData.MeshToNode != null && mi < obj.GpuData.MeshToNode.Length)
                                        ? obj.GpuData.MeshToNode[mi] : -1;
                                    Matrix4x4 worldMat = obj.CachedBaseWorldMat;
                                    if (nodeIdx >= 0 && obj.GpuData.Data.Nodes != null && nodeIdx < obj.GpuData.Data.Nodes.Length)
                                        //worldMat = GetNodeWorldMatrix(obj.GpuData.Data.Nodes, nodeIdx) * obj.CachedBaseWorldMat;
                                        worldMat = (UseNodeHierarchy ? GetNodeWorldMatrix(obj.GpuData.Data.Nodes, nodeIdx) : obj.GpuData.Data.Nodes[nodeIdx].LocalMatrix) * obj.CachedBaseWorldMat;


                            // Rotation-only for normals (remove translation)
                            Matrix4x4 normMat = worldMat;
                            normMat.M41 = 0; normMat.M42 = 0; normMat.M43 = 0;

                            for (int vi = 0; vi < src.Vertices.Length; vi++)
                            {
                                ref var sv = ref src.Vertices[vi];
                                var wp = Vector3.Transform(sv.Position, worldMat);
                                var wn = Vector3.TransformNormal(sv.Normal, normMat);
                                mergedVerts.Add(new SkinnedVertex(wp, Vector3.Normalize(wn), sv.TexCoord, sv.BoneWeights, sv.BoneIds));
                                regMin = Vector3.Min(regMin, wp);
                                regMax = Vector3.Max(regMax, wp);
                            }
                            for (int ii = 0; ii < src.Indices.Length; ii++)
                                mergedIdx.Add(src.Indices[ii] + vertOff);
                            vertOff += (uint)src.Vertices.Length;
                        }
                    }

                    if (mergedVerts.Count < 3 || mergedIdx.Count < 3) continue;
                    nonEmptyCount++;

                    var mm = CreateMeshGpu([.. mergedVerts], [.. mergedIdx], ref mat);

                    _hlodRegions[hx, hz] = new HlodRegion
                    {
                        MergedMesh = mm,
                        WorldAABB = new AABB(regMin, regMax),
                        ObjectCount = indices.Count,
                        CenterX = worldMinX + hx * _hlodRegionSize + _hlodRegionSize * 0.5f,
                        CenterZ = worldMinZ + hz * _hlodRegionSize + _hlodRegionSize * 0.5f
                    };
                    totalTris += mergedIdx.Count / 3;
                }
            }

            _hlodEnabled = true;
            _hlodTotalMergedTris = totalTris;
            _hlodRegionCount = nonEmptyCount;
            Console.WriteLine($"[HLOD] Done! {totalTris:N0} tris, {nonEmptyCount} regions, memory ~{totalTris * 64 / 1024 / 1024}MB (est).");
        }

        /// <summary>Create MeshGpu from raw vertex/index data for HLOD.</summary>
        private static MeshGpu CreateMeshGpu(SkinnedVertex[] verts, uint[] idx, ref MeshMaterialGpu mat)
        {
            uint vao, vbo, ebo = 0;
            GL.GenVertexArrays(1, &vao);
            GL.GenBuffers(1, &vbo);
            GL.BindVertexArray(vao);

            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
            fixed (SkinnedVertex* p = verts)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(verts.Length * sizeof(SkinnedVertex)), p, Const.GL_STATIC_DRAW);

            if (idx.Length > 0)
            {
                GL.GenBuffers(1, &ebo);
                GL.BindBuffer(Const.GL_ELEMENT_ARRAY_BUFFER, ebo);
                fixed (uint* ip = idx)
                    GL.BufferData(Const.GL_ELEMENT_ARRAY_BUFFER, (nuint)(idx.Length * sizeof(uint)), ip, Const.GL_STATIC_DRAW);
            }

            // ── Vertex attributes (locations 0-4) ──
            int stride = Marshal.SizeOf<SkinnedVertex>();
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)12);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, Const.GL_FLOAT, false, stride, (void*)24);
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 4, Const.GL_FLOAT, false, stride, (void*)32);
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribIPointer(4, 4, Const.GL_INT, stride, (void*)48);

            // ── Instanced identity attributes (locations 5-8) for static_vertex.glsl ──
            // The static vertex shader requires model matrix as instanced attributes.
            // HLOD draws a single mesh (non-instanced), but the shader still reads
            // from these locations. Without them, the model matrix is mat4(0) = garbage.
            EnsureIdentityInstanceVBO();
            int instanceStride = 16 * sizeof(float); // 4 vec4 rows
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _identityInstanceVBO);
            for (int i = 0; i < 4; i++)
            {
                uint attr = (uint)(5 + i);
                GL.EnableVertexAttribArray(attr);
                GL.VertexAttribPointer(attr, 4, Const.GL_FLOAT, false, instanceStride, (void*)(i * 16));
                GL.VertexAttribDivisor(attr, 1u); // divisor=1 so it stays for the single instance
            }
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, 0);

            GL.BindVertexArray(0);

            return new MeshGpu { VAO = vao, VBO = vbo, EBO = ebo, VertexCount = verts.Length, IndexCount = idx.Length, Material = mat };
        }

        // ────────────────────────────────────────────────────────────────
        //  GPU Instancing: group by (gpuData, meshIdx), draw all at once
        // ────────────────────────────────────────────────────────────────

        /// <summary>Set up instanced vertex attributes for the model matrix on the given VAO.</summary>
        private static void SetupInstanceAttribs(uint vao, uint instanceVBO, int stride)
        {
            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, instanceVBO);

            // 4 consecutive vec4 attributes for the 4 rows of the model matrix
            for (int i = 0; i < 4; i++)
            {
                uint attr = (uint)(5 + i);
                GL.EnableVertexAttribArray(attr);
                GL.VertexAttribPointer(attr, 4, Const.GL_FLOAT, false, stride, (void*)(i * 16));
                GL.VertexAttribDivisor(attr, 1u); // advance per instance
            }

            GL.BindVertexArray(0);
        }

        /// <summary>Get or create an instance VBO for a given (gpuDataHash, meshIdx) pair.</summary>
        private uint GetInstanceVBO(int gpuDataHash, int meshIdx)
        {
            var key = (gpuDataHash, meshIdx);
            if (_instanceVBOs.TryGetValue(key, out uint vbo))
                return vbo;

            GL.GenBuffers(1, &vbo);
            _instanceVBOs[key] = vbo;
            return vbo;
        }

        /// <summary>Upload model matrices to an instance VBO and set up attributes on the mesh VAO.</summary>
        private void UploadInstances(uint vao, uint instanceVBO, List<Matrix4x4> modelMatrices)
        {
            int count = modelMatrices.Count;
            int stride = sizeof(float) * 16; // 16 floats per mat4
            int byteSize = count * stride;

            // Ensure reusable buffer is large enough
            if (_matrixUploadBuffer.Length < count)
                Array.Resize(ref _matrixUploadBuffer, Math.Max(count, _matrixUploadBuffer.Length * 2));
            modelMatrices.CopyTo(_matrixUploadBuffer, 0);

            GL.BindBuffer(Const.GL_ARRAY_BUFFER, instanceVBO);
            GCHandle handle = GCHandle.Alloc(_matrixUploadBuffer, GCHandleType.Pinned);
            try
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)byteSize, (void*)handle.AddrOfPinnedObject(), Const.GL_DYNAMIC_DRAW);
            }
            finally
            {
                handle.Free();
            }

            SetupInstanceAttribs(vao, instanceVBO, stride);
        }

        // ---- Per-Object Octahedral Impostors ----
        private void BuildImpostors()
        {
            if (!UseImpostors) return;
            _impostorEntries.Clear();
            
            // Per-object atlas size: 256x128 (8 views of 32x128 each = 128KB per object)
            // Small atlas is sufficient at 300m+ distance
            const int IMP_ATLAS_W = 256;
            const int IMP_ATLAS_H = 128;
            int viewW = IMP_ATLAS_W / IMPOSTOR_ATLAS_COUNT;
            int viewH = IMP_ATLAS_H;

            uint tempFbo;
            GL.GenFramebuffers(1, &tempFbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, tempFbo);
            uint depthRb;
            GL.GenRenderbuffers(1, &depthRb);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, depthRb);
            GL.RenderbufferStorage(Const.GL_RENDERBUFFER, Const.GL_DEPTH_COMPONENT24, IMP_ATLAS_W, IMP_ATLAS_H);
            GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_ATTACHMENT, Const.GL_RENDERBUFFER, depthRb);

            GL.UseProgram(_shaderProgram);
            int useFogLocBake = GL.GetUniformLocation(_shaderProgram, "useFog");
            if (useFogLocBake != -1) GL.Uniform1i(useFogLocBake, 0);
            if (_hlodAlphaLoc != -1) GL.Uniform1f(_hlodAlphaLoc, 1.0f);

            GL.Enable(Const.GL_DEPTH_TEST);
            GL.Enable(Const.GL_CULL_FACE);
            GL.CullFace(Const.GL_BACK);
            GL.FrontFace(Const.GL_CCW);

            // Shadow uniform setup - white tex for shadow samplers
            uint whiteTex;
            GL.GenTextures(1, &whiteTex);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 7);
            GL.BindTexture(Const.GL_TEXTURE_2D, whiteTex);
            byte[] whitePixels = [255, 255, 255, 255];
            fixed (byte* p = whitePixels)
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA8, 1, 1, 0, (int)Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, p);
            GL.TexParameteri(Const.GL_TEXTURE_2D, (int)Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_2D, (int)Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_NEAREST);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 8);
            GL.BindTexture(Const.GL_TEXTURE_2D, whiteTex);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 9);
            GL.BindTexture(Const.GL_TEXTURE_2D, whiteTex);
            GL.ActiveTexture(Const.GL_TEXTURE0);

            if (_shadowMap0Loc != -1) GL.Uniform1i(_shadowMap0Loc, 7);
            if (_shadowMap1Loc != -1) GL.Uniform1i(_shadowMap1Loc, 8);
            if (_shadowMap2Loc != -1) GL.Uniform1i(_shadowMap2Loc, 9);

            int shadowFilterLocBake = GL.GetUniformLocation(_shaderProgram, "shadowFilterMode");
            if (shadowFilterLocBake != -1) GL.Uniform1i(shadowFilterLocBake, 1);

            int cascade0LocBake = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[0]");
            int cascade1LocBake = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[1]");
            int cascade2LocBake = GL.GetUniformLocation(_shaderProgram, "cascadeEnds[2]");
            if (cascade0LocBake != -1) GL.Uniform1f(cascade0LocBake, 1e10f);
            if (cascade1LocBake != -1) GL.Uniform1f(cascade1LocBake, 1e10f);
            if (cascade2LocBake != -1) GL.Uniform1f(cascade2LocBake, 1e10f);

            var identityMatrix = Matrix4x4.Identity;
            int ls0LocBake = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[0]");
            int ls1LocBake = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[1]");
            int ls2LocBake = GL.GetUniformLocation(_shaderProgram, "lightSpaceMatrices[2]");
            if (ls0LocBake != -1) GL.UniformMatrix4fv(ls0LocBake, 1, false, (float*)Unsafe.AsPointer(ref identityMatrix));
            if (ls1LocBake != -1) GL.UniformMatrix4fv(ls1LocBake, 1, false, (float*)Unsafe.AsPointer(ref identityMatrix));
            if (ls2LocBake != -1) GL.UniformMatrix4fv(ls2LocBake, 1, false, (float*)Unsafe.AsPointer(ref identityMatrix));

            //  45deg sun for directional shading
            var bakeSunDir = Vector3.Normalize(new Vector3(0.5f, 0.707f, 0.5f));
            if (_sunDirLoc != -1) GL.Uniform3f(_sunDirLoc, bakeSunDir.X, bakeSunDir.Y, bakeSunDir.Z);
            if (_realSunDirLoc != -1) GL.Uniform3f(_realSunDirLoc, bakeSunDir.X, bakeSunDir.Y, bakeSunDir.Z);
            if (_lightColorLoc != -1) GL.Uniform3f(_lightColorLoc, 0.95f, 0.93f, 0.88f);

            uint tempModelVBO;
            GL.GenBuffers(1, &tempModelVBO);

            int bakedCount = 0;
            // Bake each object individually - each gets its own atlas
            foreach (var obj in _objects)
            {
                if (obj.Group == null || obj.Group.Lods.Count == 0) continue;
                var aabb = obj.CachedWorldAABB;
                float extX = Math.Max((aabb.Max.X - aabb.Min.X) * 0.5f, 0.3f);
                float extZ = Math.Max((aabb.Max.Z - aabb.Min.Z) * 0.5f, 0.3f);
                float extY = Math.Max((aabb.Max.Y - aabb.Min.Y) * 0.5f, 0.3f);
                float realHoriz = MathF.Max(extX, extZ);
                float realVert = extY;
                const float PAD = 1.5f;
                float vpHoriz = realHoriz * PAD;
                float vpVert = realVert * PAD;

                Vector3 objCenter = new(
                    (aabb.Min.X + aabb.Max.X) * 0.5f,
                    (aabb.Min.Y + aabb.Max.Y) * 0.5f,
                    (aabb.Min.Z + aabb.Max.Z) * 0.5f
                );

                uint atlasTex;
                GL.GenTextures(1, &atlasTex);
                GL.BindTexture(Const.GL_TEXTURE_2D, atlasTex);
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA8, IMP_ATLAS_W, IMP_ATLAS_H, 0, (int)Const.GL_RGBA, (int)Const.GL_UNSIGNED_BYTE, null);
                GL.TexParameteri(Const.GL_TEXTURE_2D, (int)Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, (int)Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, (int)Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, (int)Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);

                GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0, Const.GL_TEXTURE_2D, atlasTex, 0);
                GL.ClearColor(0f, 0f, 0f, 0f);
                GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);

                for (int vi = 0; vi < IMPOSTOR_ATLAS_COUNT; vi++)
                {
                    float angle = vi * (360f / IMPOSTOR_ATLAS_COUNT);
                    float rad = angle * MathF.PI / 180f;
                    Vector3 eyePos = objCenter + new Vector3(MathF.Sin(rad) * vpHoriz, 0, MathF.Cos(rad) * vpHoriz);
                    eyePos.Y = objCenter.Y;
                    if (_viewPosLoc != -1) GL.Uniform3f(_viewPosLoc, eyePos.X, eyePos.Y, eyePos.Z);
                    Matrix4x4 viewMat = Matrix4x4.CreateLookAt(eyePos, objCenter, Vector3.UnitY);
                    Matrix4x4 projMat = Matrix4x4.CreateOrthographicOffCenter(-vpHoriz, vpHoriz, -vpVert, vpVert, 0.01f, vpHoriz * 4f);
                    int vx = vi * viewW;
                    GL.Viewport(vx, 0, viewW, viewH);
                    GL.UniformMatrix4fv(_viewLoc, 1, false, (float*)Unsafe.AsPointer(ref viewMat));
                    GL.UniformMatrix4fv(_projLoc, 1, false, (float*)Unsafe.AsPointer(ref projMat));

                    // Render this single object
                    var group = obj.Group;
                    int lodKey = group.Lods.ContainsKey(0) ? 0 : group.Lods.Keys.Min();
                    if (!group.Lods.TryGetValue(lodKey, out var miList)) continue;
                    var meshes = obj.GpuData.Data.Meshes;
                    if (meshes == null) continue;
                    foreach (int mi in miList)
                    {
                        if (mi < 0 || mi >= meshes!.Length) continue;
                        var src = meshes[mi];
                        if (src.Vertices.Length < 3) continue;
                        int nodeIdx = (obj.GpuData.MeshToNode != null && mi < obj.GpuData.MeshToNode.Length)
                            ? obj.GpuData.MeshToNode[mi] : -1;
                        Matrix4x4 modelMat = obj.CachedBaseWorldMat;
                        if (nodeIdx >= 0 && obj.GpuData.Data.Nodes != null && nodeIdx < obj.GpuData.Data.Nodes.Length)
                            modelMat = (UseNodeHierarchy ? GetNodeWorldMatrix(obj.GpuData.Data.Nodes, nodeIdx) : obj.GpuData.Data.Nodes[nodeIdx].LocalMatrix) * obj.CachedBaseWorldMat;
                        var meshGpu = obj.GpuData.Meshes[mi];
                        GL.BindBuffer(Const.GL_ARRAY_BUFFER, tempModelVBO);
                        GL.BufferData(Const.GL_ARRAY_BUFFER, 16 * sizeof(float), (float*)Unsafe.AsPointer(ref modelMat), Const.GL_DYNAMIC_DRAW);
                        GL.BindVertexArray(meshGpu.VAO);
                        int instStride = 16 * sizeof(float);
                        for (int instRow = 0; instRow < 4; instRow++)
                        {
                            uint attr = (uint)(5 + instRow);
                            GL.EnableVertexAttribArray(attr);
                            GL.VertexAttribPointer(attr, 4, Const.GL_FLOAT, false, instStride, (void*)(instRow * 16));
                            GL.VertexAttribDivisor(attr, 1u);
                        }
                        SetHLODMaterialUniforms(meshGpu);
                        GL.Uniform4f(_baseColorLoc, meshGpu.Material.BaseColorFactor.X,
                            meshGpu.Material.BaseColorFactor.Y,
                            meshGpu.Material.BaseColorFactor.Z, 1.0f);
                        if (meshGpu.IndexCount > 0)
                            GL.DrawElements(Const.GL_TRIANGLES, meshGpu.IndexCount, Const.GL_UNSIGNED_INT, null);
                        else
                            GL.DrawArrays(Const.GL_TRIANGLES, 0, meshGpu.VertexCount);
                    }
                }
                _impostorEntries.Add(new ImpostorEntry
                {
                    AtlasTexture = atlasTex,
                    WorldPosition = obj.Position,
                    WorldRadius = MathF.Max(realHoriz, realVert),
                    WorldAABB = obj.CachedWorldAABB
                });
                bakedCount++;
            }

            // Cleanup shared resources
            GL.ActiveTexture(Const.GL_TEXTURE0 + 7);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 8);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 9);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.DeleteTextures(1, &whiteTex);
            GL.DeleteBuffers(1, &tempModelVBO);

            GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.DeleteFramebuffers(1, &tempFbo);
            GL.DeleteRenderbuffers(1, &depthRb);

            _impostorsBuilt = true;
            Console.WriteLine($"[Impostor] Built {bakedCount} per-object impostor atlases.");
        }

        // ── Debug: save current FBO content (atlas texture) as TGA file ──
        private static void DebugSaveAtlasAsTGA(int w, int h)
        {
            int stride = w * 4;
            byte[] pixels = new byte[h * stride];
            fixed (byte* p = pixels)
            {
                GL.ReadPixels(0, 0, w, h, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (float*)p);
            }

            // Write TGA file (uncompressed true-color RGBA)
            string path = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"impostor_atlas_{DateTime.Now:HHmmss}.tga"
            );
            using (var fs = File.Create(path))
            {
                // TGA header: 18 bytes
                fs.WriteByte(0);                          // 0: ID length
                fs.WriteByte(0);                          // 1: Color map type (none)
                fs.WriteByte(2);                          // 2: Image type: uncompressed true-color
                fs.Write(new byte[5]);                    // 3-7: Color map spec
                fs.WriteByte(0); fs.WriteByte(0);         // 8-9: X origin
                fs.WriteByte(0); fs.WriteByte(0);         // 10-11: Y origin
                fs.WriteByte((byte)(w & 0xFF));           // 12: Width low
                fs.WriteByte((byte)((w >> 8) & 0xFF));    // 13: Width high
                fs.WriteByte((byte)(h & 0xFF));           // 14: Height low
                fs.WriteByte((byte)((h >> 8) & 0xFF));    // 15: Height high
                fs.WriteByte(32);                         // 16: Bits per pixel (RGBA)
                fs.WriteByte(0x00);                       // 17: Descriptor: bottom-left origin (matches GL.ReadPixels)

                // Pixel data: TGA expects BGRA format, swizzle from RGBA
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    fs.WriteByte(pixels[i + 2]); // B
                    fs.WriteByte(pixels[i + 1]); // G
                    fs.WriteByte(pixels[i + 0]); // R
                    fs.WriteByte(pixels[i + 3]); // A
                }
            }
            Console.WriteLine($"[Debug] Saved atlas to {path}");
        }

        // ────────────────────────────────────────────────────────────────
        //  DRAW — main color pass with GPU instancing
        // ────────────────────────────────────────────────────────────────

        public void Draw(Camera camera, Lights light, CSM csm = null, Matrix4x4? frozenViewProj = null, bool cullFreezeEnabled = false)
        {
            if (_objects.Count == 0) return;

            GL.UseProgram(_shaderProgram);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.Enable(Const.GL_CULL_FACE);
            GL.CullFace(Const.GL_BACK);
            GL.FrontFace(Const.GL_CCW);

            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            GL.UniformMatrix4fv(_viewLoc, 1, false, (float*)Unsafe.AsPointer(ref view));
            GL.UniformMatrix4fv(_projLoc, 1, false, (float*)Unsafe.AsPointer(ref proj));
            GL.Uniform3f(_sunDirLoc, light.SunDir.X, light.SunDir.Y, light.SunDir.Z);
            if (_realSunDirLoc != -1) GL.Uniform3f(_realSunDirLoc, light.RealSunDir.X, light.RealSunDir.Y, light.RealSunDir.Z);
            GL.Uniform3f(_lightColorLoc, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
            GL.Uniform3f(_viewPosLoc, camera.Position.X, camera.Position.Y, camera.Position.Z);

            int fogColLoc = GL.GetUniformLocation(_shaderProgram, "fogColor");
            if (fogColLoc != -1) GL.Uniform3f(fogColLoc, light.FogColor.X, light.FogColor.Y, light.FogColor.Z);
            int useFogLoc = GL.GetUniformLocation(_shaderProgram, "useFog");
            if (useFogLoc != -1) GL.Uniform1i(useFogLoc, DarkEngine3D_gl_csharp.Engine.Inputs.Keyboard.GetIsFogActive() ? 1 : 0);

            // Shadow uniforms
            if (csm != null)
            {
                GL.Uniform1i(_shadowMap0Loc, 6);
                GL.Uniform1i(_shadowMap1Loc, 7);
                GL.Uniform1i(_shadowMap2Loc, 8);
                unsafe
                {
                    fixed (float* p0 = &csm.LightSpaceMatrices[0].M11)
                        GL.UniformMatrix4fv(_lightSpaceLoc0, 1, false, p0);
                    fixed (float* p1 = &csm.LightSpaceMatrices[1].M11)
                        GL.UniformMatrix4fv(_lightSpaceLoc1, 1, false, p1);
                    fixed (float* p2 = &csm.LightSpaceMatrices[2].M11)
                        GL.UniformMatrix4fv(_lightSpaceLoc2, 1, false, p2);
                }
                GL.Uniform1f(_cascadeEndsLoc0, csm.CascadeEnds[0]);
                GL.Uniform1f(_cascadeEndsLoc1, csm.CascadeEnds[1]);
                GL.Uniform1f(_cascadeEndsLoc2, csm.CascadeEnds[2]);

                // Send shadow filter mode to this shader program
                int shadowFilterLoc = GL.GetUniformLocation(_shaderProgram, "shadowFilterMode");
                if (shadowFilterLoc != -1)
                    GL.Uniform1i(shadowFilterLoc, DarkEngine3D_gl_csharp.Engine.Inputs.Keyboard.GetIsHardShadow());
            }

            // Ensure HLOD alpha defaults to opaque for regular objects
            if (_hlodAlphaLoc != -1)
                GL.Uniform1f(_hlodAlphaLoc, 1.0f);

            ObjectDrawn = 0;
            _renderedTriangles = 0;

            // ── Step 1: compute visible objects, determine LOD, build instance lists ──
            // Reuse instance lists — clear from previous frame instead of new alloc
            foreach (var kv in _drawInstanceLists)
                kv.Value.Mats.Clear();
            _drawInstanceLists.Clear();

            Matrix4x4 viewProj = cullFreezeEnabled && frozenViewProj.HasValue
                ? frozenViewProj.Value
                : view * proj;
            Plane[] cameraFrustum = ExtractCameraFrustum(viewProj);

            // Determine camera position in world space
            float camX = camera.Position.X;
            float camZ = camera.Position.Z;
            Vector3 cameraPos = camera.Position;
            Vector3 cameraFront = camera.Front;

            // Use spatial grid if available, otherwise fall back to full iteration
            if (_gridCells != null)
            {
                // Apply GridVisibleRange override (if set > 0, extends beyond LOD3_Dist for impostor coverage)
                _gridVisibleRange = GridVisibleRange > 0f ? GridVisibleRange : LOD3_Dist;

                // ── Spatial Grid: only iterate cells within visible range ──
                // Compute visible cell range based on actual object bounds
                // Since _gridCells was built from actual object positions, we just
                // compute which cells cover objects within _gridVisibleRange of camera
                int gridW = _gridCells.GetLength(0);
                int gridH = _gridCells.GetLength(1);

                for (int gz = 0; gz < gridH; gz++)
                {
                    for (int gx = 0; gx < gridW; gx++)
                    {
                        var cellObjs = _gridCells[gx, gz];
                        if (cellObjs == null || cellObjs.Count == 0) continue;

                        // Distance check using cell center position (more accurate than first object)
                        float cellCenterX = _gridOriginX + gx * _gridCellSize + _gridCellSize * 0.5f;
                        float cellCenterZ = _gridOriginZ + gz * _gridCellSize + _gridCellSize * 0.5f;
                        float dx = cellCenterX - camX;
                        float dz = cellCenterZ - camZ;
                        if (dx * dx + dz * dz > _gridVisibleRange * _gridVisibleRange)
                            continue;

                        foreach (int objIdx in cellObjs)
                        {
                            var obj = _objects[objIdx];

                            // Occlusion culling
                            if (OcclusionCulling.Enabled && !obj.IsVisible)
                                continue;

                            if (!IsAABBInFrustum(cameraFrustum, obj.CachedWorldAABB, 5f))
                                continue;

                            var group = obj.Group;
                            if (group == null || group.Lods.Count == 0) continue;

                            // LOD selection — distance-based
                            float dist = Vector3.Distance(camera.Position, obj.Position);
                            int targetLOD;
                            if (dist < LOD0_Dist) targetLOD = 0;
                            else if (dist < LOD1_Dist) targetLOD = 1;
                            else if (dist < LOD2_Dist) targetLOD = 2;
                            else targetLOD = 3;

                            if (targetLOD > group.MaxLOD)
                                targetLOD = group.MaxLOD;

                            if (CullAtMaxLOD && targetLOD >= group.MaxLOD)
                                continue;

                            // Cull individual rendering when impostors handle this range
                            if (UseImpostors && _impostorsBuilt && dist >= ImpostorNearDist && dist <= ImpostorFarDist)
                                continue;

                            int actualLOD = group.LodFallback[targetLOD];
                            obj.CurrentLOD = actualLOD;

                            var meshIndices = group.Lods[actualLOD];
                            var baseWorldMat = obj.CachedBaseWorldMat;

                            foreach (int meshIdx in meshIndices)
                            {
                                int nodeIdx = (obj.GpuData.MeshToNode != null && meshIdx < obj.GpuData.MeshToNode.Length)
                                              ? obj.GpuData.MeshToNode[meshIdx] : -1;

                                // Compute modelMat once untuk culling + instance list
                                Matrix4x4 modelMat = baseWorldMat;
                                if (nodeIdx >= 0 && obj.GpuData.Data.Nodes != null && nodeIdx < obj.GpuData.Data.Nodes.Length)
                                    modelMat = (UseNodeHierarchy ? GetNodeWorldMatrix(obj.GpuData.Data.Nodes, nodeIdx) : obj.GpuData.Data.Nodes[nodeIdx].LocalMatrix) * baseWorldMat;

                                var key = (RuntimeHelpers.GetHashCode(obj.GpuData), meshIdx, nodeIdx);
                                if (!_drawInstanceLists.TryGetValue(key, out var entry))
                                {
                                    entry = new InstanceGroup { Mesh = obj.GpuData.Meshes[meshIdx], Gpu = obj.GpuData };
                                    _drawInstanceLists[key] = entry;
                                }

                                entry.Mats.Add(modelMat);
                            }
                        }
                    }
                }
            }
            else
            {
                // ── Original: iterate all objects (no spatial grid) ──
                foreach (var obj in _objects)
                {
                    // Occlusion culling: skip jika di belakang terrain
                    if (OcclusionCulling.Enabled && !obj.IsVisible)
                        continue;

                    if (!IsAABBInFrustum(cameraFrustum, obj.CachedWorldAABB, 5f))
                        continue;

                    var group = obj.Group;
                    if (group == null || group.Lods.Count == 0) continue;

                    // LOD selection — distance-based pake threshold dari Config
                    float dist = Vector3.Distance(camera.Position, obj.Position);
                    int targetLOD;
                    if (dist < LOD0_Dist) targetLOD = 0;
                    else if (dist < LOD1_Dist) targetLOD = 1;
                    else if (dist < LOD2_Dist) targetLOD = 2;
                    else targetLOD = 3;

                    // Clamp targetLOD ke MaxLOD yang tersedia
                    if (targetLOD > group.MaxLOD)
                        targetLOD = group.MaxLOD;

                    // CullAtMaxLOD: skip di LOD tertinggi
                    if (CullAtMaxLOD && targetLOD >= group.MaxLOD)
                        continue;

                    // Cull individual rendering when impostors handle this range
                    if (UseImpostors && _impostorsBuilt && dist >= ImpostorNearDist)
                        continue;

                    int actualLOD = group.LodFallback[targetLOD];
                    obj.CurrentLOD = actualLOD;

                    var meshIndices = group.Lods[actualLOD];
                    var baseWorldMat = obj.CachedBaseWorldMat;

                    foreach (int meshIdx in meshIndices)
                    {
                        int nodeIdx = (obj.GpuData.MeshToNode != null && meshIdx < obj.GpuData.MeshToNode.Length)
                                      ? obj.GpuData.MeshToNode[meshIdx] : -1;

                        // Compute modelMat once untuk culling + instance list
                        Matrix4x4 modelMat = baseWorldMat;
                        if (nodeIdx >= 0 && obj.GpuData.Data.Nodes != null && nodeIdx < obj.GpuData.Data.Nodes.Length)
                            modelMat = (UseNodeHierarchy ? GetNodeWorldMatrix(obj.GpuData.Data.Nodes, nodeIdx) : obj.GpuData.Data.Nodes[nodeIdx].LocalMatrix) * baseWorldMat;

                        var key = (RuntimeHelpers.GetHashCode(obj.GpuData), meshIdx, nodeIdx);
                        if (!_drawInstanceLists.TryGetValue(key, out var entry))
                        {
                            entry = new InstanceGroup { Mesh = obj.GpuData.Meshes[meshIdx], Gpu = obj.GpuData };
                            _drawInstanceLists[key] = entry;
                        }

                        entry.Mats.Add(modelMat);
                    }
                }
            }

            // ── Step 2: draw each instance group with a single instanced draw call ──
            foreach (var kv in _drawInstanceLists)
            {
                var entry = kv.Value;
                var mats = entry.Mats;
                if (mats.Count == 0) continue;
                var mesh = entry.Mesh;

                uint instanceVBO = GetInstanceVBO(kv.Key.gpuHash, kv.Key.meshIdx);
                UploadInstances(mesh.VAO, instanceVBO, mats);

                // Set material uniforms once per group (all instances share the same mesh)
                if (mesh.Material.DoubleSided) GL.Disable(Const.GL_CULL_FACE);
                else GL.Enable(Const.GL_CULL_FACE);

                GL.Uniform4f(_baseColorLoc, mesh.Material.BaseColorFactor.X, mesh.Material.BaseColorFactor.Y,
                             mesh.Material.BaseColorFactor.Z, mesh.Material.BaseColorFactor.W);

                if (mesh.Material.HasBaseColorTexture)
                {
                    GL.ActiveTexture(Const.GL_TEXTURE0);
                    GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.BaseColorTextureID);
                    GL.Uniform1i(_useAlbedoLoc, 1);
                    GL.Uniform1i(_albedoMapLoc, 0);
                }
                else GL.Uniform1i(_useAlbedoLoc, 0);

                // PBR uniforms
                if (_metallicFactorLoc != -1) GL.Uniform1f(_metallicFactorLoc, mesh.Material.MetallicFactor);
                if (_roughnessFactorLoc != -1) GL.Uniform1f(_roughnessFactorLoc, mesh.Material.RoughnessFactor);
                if (_normalScaleLoc != -1) GL.Uniform1f(_normalScaleLoc, mesh.Material.NormalScale);
                if (_occlusionStrengthLoc != -1) GL.Uniform1f(_occlusionStrengthLoc, mesh.Material.OcclusionStrength);
                if (_emissiveFactorLoc != -1) GL.Uniform3f(_emissiveFactorLoc, mesh.Material.EmissiveFactor.X, mesh.Material.EmissiveFactor.Y, mesh.Material.EmissiveFactor.Z);
                if (_hasNormalTextureLoc != -1) GL.Uniform1i(_hasNormalTextureLoc, mesh.Material.HasNormalTexture ? 1 : 0);
                if (_hasMetallicRoughnessTextureLoc != -1) GL.Uniform1i(_hasMetallicRoughnessTextureLoc, mesh.Material.HasMetallicRoughnessTexture ? 1 : 0);
                if (_hasOcclusionTextureLoc != -1) GL.Uniform1i(_hasOcclusionTextureLoc, mesh.Material.HasOcclusionTexture ? 1 : 0);
                if (_hasEmissiveTextureLoc != -1) GL.Uniform1i(_hasEmissiveTextureLoc, mesh.Material.HasEmissiveTexture ? 1 : 0);

                // Bind PBR textures
                if (mesh.Material.HasNormalTexture && mesh.Material.NormalTextureID != 0)
                {
                    GL.ActiveTexture(Const.GL_TEXTURE0 + 3);
                    GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.NormalTextureID);
                }
                if (mesh.Material.HasMetallicRoughnessTexture && mesh.Material.MetallicRoughnessTextureID != 0)
                {
                    GL.ActiveTexture(Const.GL_TEXTURE0 + 4);
                    GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.MetallicRoughnessTextureID);
                }
                if (mesh.Material.HasOcclusionTexture && mesh.Material.OcclusionTextureID != 0)
                {
                    GL.ActiveTexture(Const.GL_TEXTURE0 + 5);
                    GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.OcclusionTextureID);
                }
                if (mesh.Material.HasEmissiveTexture && mesh.Material.EmissiveTextureID != 0)
                {
                    GL.ActiveTexture(Const.GL_TEXTURE0 + 6);
                    GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.EmissiveTextureID);
                }

                GL.BindVertexArray(mesh.VAO);
                if (mesh.IndexCount > 0)
                    GL.DrawElementsInstanced(Const.GL_TRIANGLES, mesh.IndexCount, Const.GL_UNSIGNED_INT, null, mats.Count);
                else
                    GL.DrawArraysInstanced(Const.GL_TRIANGLES, 0, mesh.VertexCount, mats.Count);

                int trisPerInstance = mesh.IndexCount / 3;
                _renderedTriangles += mats.Count * trisPerInstance;
                ObjectDrawn += mats.Count;
            }

            // ── Step 3: render HLOD region meshes (mid range) — optional, gated by config ──
            _hlodVisibleRegions = 0;
            if (_hlodEnabled)
            {
                // Enable alpha blending for HLOD transparency fade
                GL.Enable(Const.GL_BLEND);
                GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);

                // Compute visible HLOD regions within mid range
                int hlodW = _hlodRegions.GetLength(0);
                int hlodH = _hlodRegions.GetLength(1);
                float rangeSq = HLODMidDist * HLODMidDist;
                float fadeRange = HLODMidDist - HLODNearDist;
                float invFadeRange = fadeRange > 0.001f ? 1.0f / fadeRange : 1.0f;

                for (int hz = 0; hz < hlodH; hz++)
                {
                    for (int hx = 0; hx < hlodW; hx++)
                    {
                        var reg = _hlodRegions[hx, hz];
                        if (reg == null || reg.ObjectCount == 0) continue;

                        // Frustum cull
                        if (!IsAABBInFrustum(cameraFrustum, reg.WorldAABB, 5f))
                            continue;

                        // Distance cull
                        float dxc = camera.Position.X - reg.CenterX;
                        float dzc = camera.Position.Z - reg.CenterZ;
                        float distSq = dxc * dxc + dzc * dzc;
                        if (distSq > rangeSq)
                            continue;

                        // Skip near-range regions (objects already rendered via grid instancing)
                        if (distSq < HLODNearDist * HLODNearDist)
                            continue;

                        // Distance-based alpha fade: 1.0 at nearDist → 0.0 at midDist
                        float dist = MathF.Sqrt(distSq);
                        float alpha = 1.0f - (dist - HLODNearDist) * invFadeRange;
                        alpha = Math.Clamp(alpha, 0.0f, 1.0f);
                        if (_hlodAlphaLoc != -1)
                            GL.Uniform1f(_hlodAlphaLoc, alpha);

                        _hlodVisibleRegions++;

                        var mesh = reg.MergedMesh;
                        var identity = Matrix4x4.Identity;
                        GL.UniformMatrix4fv(_modelLoc, 1, false, (float*)Unsafe.AsPointer(ref identity));

                        SetHLODMaterialUniforms(mesh);

                        GL.BindVertexArray(mesh.VAO);
                        if (mesh.IndexCount > 0)
                            GL.DrawElements(Const.GL_TRIANGLES, mesh.IndexCount, Const.GL_UNSIGNED_INT, null);
                        else
                            GL.DrawArrays(Const.GL_TRIANGLES, 0, mesh.VertexCount);

                        _renderedTriangles += mesh.IndexCount / 3;
                        ObjectDrawn++;
                    }
                }

                // Restore hlodAlpha to opaque for subsequent draws
                if (_hlodAlphaLoc != -1)
                    GL.Uniform1f(_hlodAlphaLoc, 1.0f);
                GL.Disable(Const.GL_BLEND);
            }
            // ---- Step 4: render per-object Octahedral Impostor billboards (far range) ----
            if (_impostorsBuilt && _impostorEntries.Count > 0 && _impShaderProgram != 0)
            {
                GL.UseProgram(_impShaderProgram);
                GL.Enable(Const.GL_DEPTH_TEST);
                GL.Enable(Const.GL_CULL_FACE);
                GL.CullFace(Const.GL_BACK);
                GL.UniformMatrix4fv(_impViewLoc, 1, false, (float*)Unsafe.AsPointer(ref view));
                GL.UniformMatrix4fv(_impProjLoc, 1, false, (float*)Unsafe.AsPointer(ref proj));
                GL.Uniform2f(_impAtlasTilesLoc, 8f, 1f);
                if (_impViewPosLoc != -1)
                    GL.Uniform3f(_impViewPosLoc, camera.Position.X, camera.Position.Y, camera.Position.Z);
                if (_impSunDirLoc != -1)
                    GL.Uniform3f(_impSunDirLoc, light.SunDir.X, light.SunDir.Y, light.SunDir.Z);
                if (_impLightColorLoc != -1)
                    GL.Uniform3f(_impLightColorLoc, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
                if (_impFogColorLoc != -1)
                    GL.Uniform3f(_impFogColorLoc, light.FogColor.X, light.FogColor.Y, light.FogColor.Z);
                if (_impRealSunDirLoc != -1)
                    GL.Uniform3f(_impRealSunDirLoc, light.RealSunDir.X, light.RealSunDir.Y, light.RealSunDir.Z);
                if (_impUseFogLoc != -1)
                    GL.Uniform1i(_impUseFogLoc, DarkEngine3D_gl_csharp.Engine.Inputs.Keyboard.GetIsFogActive() ? 1 : 0);
                Vector3 camRight = new Vector3(view.M11, view.M21, view.M31);
                Vector3 camUp = new Vector3(view.M12, view.M22, view.M32);
                float fadeStartDist = ImpostorNearDist - ImpostorFadeDist;
                if (fadeStartDist < 0f) fadeStartDist = 0f;
                float impNearSq = fadeStartDist * fadeStartDist;
                float impFarSq = ImpostorFarDist * ImpostorFarDist;
                float invFadeRange = ImpostorFadeDist > 0.001f ? 1.0f / ImpostorFadeDist : 1.0f;
                SetupImpostorVAO();
                GL.BindVertexArray(_impBillboardVAO);
                foreach (var imp in _impostorEntries)
                {
                    if (imp.AtlasTexture == 0) continue;
                        // Before (2D): trees on hills had different distances for LOD vs impostor.
                        //   -> individual LOD: 3D dist > ImpostorNearDist (culled OK)
                        //   -> impostor: 2D dist < fadeStartDist (still invisible!)
                        //   -> RESULT: trees disappeared in transition zone.
                        float centerY = (imp.WorldAABB.Min.Y + imp.WorldAABB.Max.Y) * 0.5f;
                        float dxc = camera.Position.X - imp.WorldPosition.X;
                        float dyc = camera.Position.Y - centerY;
                        float dzc = camera.Position.Z - imp.WorldPosition.Z;
                        float distSq = dxc * dxc + dyc * dyc + dzc * dzc;
                        if (distSq < impNearSq || distSq > impFarSq) continue;
                        // Cross-fade impostor opacity: 0 at fadeStartDist -> 1 at ImpostorNearDist
                        float fadeAlpha = (MathF.Sqrt(distSq) - fadeStartDist) * invFadeRange;
                        fadeAlpha = Math.Clamp(fadeAlpha, 0.0f, 1.0f);
                        if (_impDebugOpacityLoc != -1)
                            GL.Uniform1f(_impDebugOpacityLoc, fadeAlpha);
                        if (!IsAABBInFrustum(cameraFrustum, imp.WorldAABB, imp.WorldRadius + 5f)) continue;
                        // Bind atlas texture
                        GL.ActiveTexture(Const.GL_TEXTURE0);
                        GL.BindTexture(Const.GL_TEXTURE_2D, imp.AtlasTexture);
                        GL.Uniform1i(_impAtlasLoc, 0);
                        // Vertex shader: center + radius + cameraRight + cameraUp -> camera-facing billboard
                        if (_impCenterLoc != -1)
                            GL.Uniform3f(_impCenterLoc, imp.WorldPosition.X, (imp.WorldAABB.Min.Y + imp.WorldAABB.Max.Y) * 0.5f, imp.WorldPosition.Z);
                        if (_impRadiusLoc != -1)
                            GL.Uniform1f(_impRadiusLoc, imp.WorldRadius);
                        if (_impCameraRightLoc != -1)
                            GL.Uniform3f(_impCameraRightLoc, camRight.X, camRight.Y, camRight.Z);
                        if (_impCameraUpLoc != -1)
                            GL.Uniform3f(_impCameraUpLoc, camUp.X, camUp.Y, camUp.Z);
                        GL.DrawElements(Const.GL_TRIANGLES, 6, Const.GL_UNSIGNED_INT, null);
                        _renderedTriangles += 2;
                        ObjectDrawn++;
                }
                GL.BindVertexArray(0);
                GL.BindTexture(Const.GL_TEXTURE_2D, 0);
                GL.UseProgram(_shaderProgram);
            }

            GL.BindVertexArray(0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.Disable(Const.GL_CULL_FACE);
        }

        // ────────────────────────────────────────────────────────────────
        //  RENDER SHADOW — shadow pass with GPU instancing
        // ────────────────────────────────────────────────────────────────

        public void RenderShadow(Camera camera, CSM csm, int cascadeIndex, uint shadowShader, int modelLoc)
        {
            if (_objects.Count == 0 || !CastShadow) return;

            GL.UseProgram(shadowShader);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.Enable(Const.GL_CULL_FACE);
            GL.CullFace(Const.GL_BACK);
            GL.FrontFace(Const.GL_CCW);

            // Get alpha-uniform locations for the alpha shadow shader
            int useAlbedoLoc = GL.GetUniformLocation(shadowShader, "useAlbedo");
            int albedoMapLoc = GL.GetUniformLocation(shadowShader, "albedoMap");
            int alphaThresholdLoc = GL.GetUniformLocation(shadowShader, "alphaThreshold");
            int useAlphaTestLoc = GL.GetUniformLocation(shadowShader, "useAlphaTest");

            var planes = csm.OrthoCorners[cascadeIndex] != null
                ? CSM.BuildPlanesFromCorners(csm.OrthoCorners[cascadeIndex])
                : null;

            // ── Step 1: filter by CastShadow + frustum, group by (gpuData, meshIdx) ──
            // Reuse instance lists — clear from previous frame instead of new alloc
            foreach (var kv in _shadowInstanceLists)
                kv.Value.Mats.Clear();
            _shadowInstanceLists.Clear();
            foreach (var obj in _objects)
            {
                if (!obj.CastShadow) continue;

                // Shadow pass uses its own culling (light-space frustum + distance), NOT IsVisible.
                // Objects outside camera frustum can still cast shadows visible inside the frustum.


                if (planes != null)
                {
                    bool outside = false;
                    foreach (var plane in planes)
                    {
                        if (Vector3.Dot(plane.Normal, obj.Position) + plane.D < -5.0f) { outside = true; break; }
                    }
                    if (outside) continue;
                }

                var group = obj.Group;
                if (group == null || group.Lods.Count == 0) continue;

                // Shadow LOD — distance-based pake threshold dari Config
                float dist = Vector3.Distance(camera.Position, obj.Position);
                int targetLOD;
                if (dist < LOD0_Dist) targetLOD = 0;
                else if (dist < LOD1_Dist) targetLOD = 1;
                else if (dist < LOD2_Dist) targetLOD = 2;
                else targetLOD = 3;

                // Clamp targetLOD ke MaxLOD yang tersedia — jangan cull shadow object hanya karena
                // tidak punya LOD variant tinggi.
                if (targetLOD > group.MaxLOD)
                    targetLOD = group.MaxLOD;

                // CullAtMaxLOD: skip shadow di LOD tertinggi model
                if (CullAtMaxLOD && targetLOD >= group.MaxLOD)
                    continue;

                int actualLOD = group.LodFallback[targetLOD];

                // Auto-disable alpha test when shadow LOD > 1 (far away objects)
                bool useAlpha = obj.UseAlphaTest && UseAlpha && (targetLOD <= 1);

                var meshIndices = group.Lods[actualLOD];
                var baseWorldMat = obj.CachedBaseWorldMat;

                foreach (int meshIdx in meshIndices)
                {
                    int nodeIdx = (obj.GpuData.MeshToNode != null && meshIdx < obj.GpuData.MeshToNode.Length)
                                  ? obj.GpuData.MeshToNode[meshIdx] : -1;

                    var key = (RuntimeHelpers.GetHashCode(obj.GpuData), meshIdx, nodeIdx, useAlpha);
                    if (!_shadowInstanceLists.TryGetValue(key, out var entry))
                    {
                        entry = new InstanceGroup { Mesh = obj.GpuData.Meshes[meshIdx], Gpu = obj.GpuData };
                        _shadowInstanceLists[key] = entry;
                    }

                    Matrix4x4 modelMat = baseWorldMat;
                    if (nodeIdx >= 0 && obj.GpuData.Data.Nodes != null && nodeIdx < obj.GpuData.Data.Nodes.Length)
                        //modelMat = GetNodeWorldMatrix(obj.GpuData.Data.Nodes, nodeIdx) * baseWorldMat;
                        modelMat = (UseNodeHierarchy ? GetNodeWorldMatrix(obj.GpuData.Data.Nodes, nodeIdx) : obj.GpuData.Data.Nodes[nodeIdx].LocalMatrix) * baseWorldMat;

                    entry.Mats.Add(modelMat);
                }
            }

            // ── Step 2: draw each group ──
            foreach (var kv in _shadowInstanceLists)
            {
                var entry = kv.Value;
                var mats = entry.Mats;
                if (mats.Count == 0) continue;
                var mesh = entry.Mesh;
                bool hasAlpha = kv.Key.hasAlpha;

                uint instanceVBO = GetInstanceVBO(kv.Key.gpuHash, kv.Key.meshIdx);
                UploadInstances(mesh.VAO, instanceVBO, mats);

                if (mesh.Material.DoubleSided) GL.Disable(Const.GL_CULL_FACE);
                else GL.Enable(Const.GL_CULL_FACE);

                // Bind base color texture for alpha testing if needed
                if (hasAlpha && mesh.Material.HasBaseColorTexture)
                {
                    GL.ActiveTexture(Const.GL_TEXTURE0);
                    GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.BaseColorTextureID);
                    if (useAlbedoLoc != -1) GL.Uniform1i(useAlbedoLoc, 1);
                    if (albedoMapLoc != -1) GL.Uniform1i(albedoMapLoc, 0);
                    if (alphaThresholdLoc != -1) GL.Uniform1f(alphaThresholdLoc, 0.3f);
                    if (useAlphaTestLoc != -1) GL.Uniform1i(useAlphaTestLoc, 1);
                }
                else
                {
                    if (useAlbedoLoc != -1) GL.Uniform1i(useAlbedoLoc, 0);
                    if (useAlphaTestLoc != -1) GL.Uniform1i(useAlphaTestLoc, 0);
                }

                GL.BindVertexArray(mesh.VAO);
                if (mesh.IndexCount > 0)
                    GL.DrawElementsInstanced(Const.GL_TRIANGLES, mesh.IndexCount, Const.GL_UNSIGNED_INT, null, mats.Count);
                else
                    GL.DrawArraysInstanced(Const.GL_TRIANGLES, 0, mesh.VertexCount, mats.Count);
            }

            GL.BindVertexArray(0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        }

        // ── Helper: compute AABB from a set of mesh indices ──
        private static AABB ComputeGroupAABB(int[] meshIndices, int[] meshToNode, GltfNode[]? nodes, GltfMeshData[] meshes, bool useNodeHierarchy)
        {
            Vector3 mn = new(float.PositiveInfinity);
            Vector3 mx = new(float.NegativeInfinity);
            bool hasVerts = false;

            foreach (int mi in meshIndices)
            {
                if (mi < 0 || mi >= meshes.Length) continue;

                int nodeIdx = (meshToNode != null && mi < meshToNode.Length)
                    ? meshToNode[mi] : -1;
                Matrix4x4 nodeMat = Matrix4x4.Identity;
                if (nodeIdx >= 0 && nodes != null && nodeIdx < nodes.Length)
                    //nodeMat = GetNodeWorldMatrix(nodes, nodeIdx);
                    nodeMat = useNodeHierarchy
                           ? GetNodeWorldMatrix(nodes, nodeIdx)
                           : nodes[nodeIdx].LocalMatrix;

                var verts = meshes[mi].Vertices;
                if (verts == null || verts.Length == 0) continue;
                hasVerts = true;
                for (int vi = 0; vi < verts.Length; vi++)
                {
                    Vector3 modelLocal = Vector3.Transform(verts[vi].Position, nodeMat);
                    mn = Vector3.Min(mn, modelLocal);
                    mx = Vector3.Max(mx, modelLocal);
                }
            }

            return hasVerts ? new AABB(mn, mx) : new AABB(Vector3.Zero, Vector3.One);
        }

        /// <summary>
        /// Compute LOCAL AABB dari mesh-mesh yang namanya mengandung partName (case-insensitive).
        /// Menerapkan node transform GLTF agar AABB sesuai dengan visual rendering.
        /// World transform dilakukan oleh caller via CachedBaseWorldMat.
        /// </summary>
        private static AABB ComputeCollisionLocalAABB(GltfModelGpuData gpuData, string partName, StaticObject sobj)
        {
            Vector3 mn = new(float.PositiveInfinity);
            Vector3 mx = new(float.NegativeInfinity);
            bool found = false;

            var meshes = gpuData.Data.Meshes;
            if (meshes == null) return sobj.Group.LocalAABB;

            // Only search meshes that belong to this object's group (its children),
            // not meshes from other groups/variants.
            var groupMeshSet = new HashSet<int>();
            foreach (var lodKv in sobj.Group.Lods)
                foreach (int mi in lodKv.Value)
                    groupMeshSet.Add(mi);

            foreach (int mi in groupMeshSet)
            {
                if (mi < 0 || mi >= meshes.Length) continue;
                string? meshName = meshes[mi].Name;
                if (string.IsNullOrEmpty(meshName)) continue;
                if (!meshName.Contains(partName, StringComparison.OrdinalIgnoreCase)) continue;

                var verts = meshes[mi].Vertices;
                if (verts == null || verts.Length == 0) continue;

                // Cari node transform untuk mesh ini (sama seperti rendering: nodeMatrix * baseWorldMat)
                int nodeIdx = (gpuData.MeshToNode != null && mi < gpuData.MeshToNode.Length)
                    ? gpuData.MeshToNode[mi] : -1;
                Matrix4x4 nodeMat = Matrix4x4.Identity;
                if (nodeIdx >= 0 && gpuData.Data.Nodes != null && nodeIdx < gpuData.Data.Nodes.Length)
                    nodeMat = GetNodeWorldMatrix(gpuData.Data.Nodes, nodeIdx);

                found = true;
                for (int vi = 0; vi < verts.Length; vi++)
                {
                    // Transform vertex ke model-local space pakai node matrix
                    Vector3 modelLocal = Vector3.Transform(verts[vi].Position, nodeMat);
                    mn = Vector3.Min(mn, modelLocal);
                    mx = Vector3.Max(mx, modelLocal);
                }
            }

            if (!found)
                return sobj.Group.LocalAABB; // fallback ke group local AABB jika tidak ada mesh yang cocok

            return new AABB(mn, mx); // LOCAL AABB (belum di-transform ke world)
        }

        /// <summary>
        /// Build BVH from all meshes in the object's selected group.
        /// Combines vertices from all meshes and creates a hierarchical collision structure.
        /// </summary>
        private BVH? BuildBVHForObject(StaticObject sobj, StaticObjectGroup group)
        {
            var gpuData = sobj.GpuData;
            var meshes = gpuData.Data.Meshes;
            if (meshes == null || meshes.Length == 0)
                 return null;

            // Collect all triangles from all LOD0 meshes
            var verticesList = new List<Vector3>();
            var indicesList = new List<int>();
            var meshSet = new HashSet<int>();

            // Get LOD0 meshes, or fallback to lowest available LOD
            if (!group.Lods.TryGetValue(0, out var lodMeshes))
            {
                int lowestKey = group.Lods.Keys.Min();
                group.Lods.TryGetValue(lowestKey, out lodMeshes);
            }

            if (lodMeshes == null || lodMeshes.Count == 0)
                return null;

            foreach (int mi in lodMeshes)
            {
                if (mi < 0 || mi >= meshes.Length) continue;
                meshSet.Add(mi);
            }

            // Combine all mesh vertices and indices
            int vertexOffset = 0;
            foreach (int mi in meshSet)
            {
                var mesh = meshes[mi];
                if (mesh.Vertices == null || mesh.Indices == null)
                    continue;

                // Get node transform for this mesh
                int nodeIdx = (gpuData.MeshToNode != null && mi < gpuData.MeshToNode.Length)
                    ? gpuData.MeshToNode[mi] : -1;
                Matrix4x4 nodeMat = Matrix4x4.Identity;
                if (nodeIdx >= 0 && gpuData.Data.Nodes != null && nodeIdx < gpuData.Data.Nodes.Length)
                    nodeMat = GetNodeWorldMatrix(gpuData.Data.Nodes, nodeIdx);

                // Add vertices (transformed by node matrix, then world matrix)
                // This puts the BVH in world space so collision checks work directly with world-space coordinates
                Matrix4x4 worldMat = sobj.CachedBaseWorldMat;
                foreach (var vert in mesh.Vertices)
                {
                    Vector3 transformedPos = Vector3.Transform(vert.Position, nodeMat);
                    transformedPos = Vector3.Transform(transformedPos, worldMat);
                    verticesList.Add(transformedPos);
                }

                // Add indices (with offset)
                foreach (int idx in mesh.Indices)
                {
                    indicesList.Add(idx + vertexOffset);
                }

                vertexOffset += mesh.Vertices.Length;
            }

            if (verticesList.Count == 0 || indicesList.Count == 0)
                return null;

            // Build and return BVH
            var bvh = new BVH();
            bvh.Build([..verticesList], [..indicesList]);
            return bvh;
        }

        /// <summary>
        /// Build BVH for all objects whose ColType is set to CollisionType.BVH.
        /// Must be called after setting ColType on the objects.
        /// </summary>
        public void BuildBVHForCollidableObjects()
        {
            foreach (var sobj in _objects)
            {
                if (sobj.ColType == CollisionType.BVH && sobj.CollisionBVH == null)
                {
                    sobj.CollisionBVH = BuildBVHForObject(sobj, sobj.Group);
                    if (sobj.CollisionBVH != null)
                    {
                        Console.WriteLine($"[BVH] Built collision BVH for '{sobj.Group.BaseName}' at {sobj.Position}");
                    }
                }
            }
        }

        /// <summary>Set material uniforms for an HLOD merged mesh draw call.</summary>
        private void SetHLODMaterialUniforms(MeshGpu mesh)
        {
            if (mesh.Material.DoubleSided) GL.Disable(Const.GL_CULL_FACE);
            else GL.Enable(Const.GL_CULL_FACE);

            GL.Uniform4f(_baseColorLoc, mesh.Material.BaseColorFactor.X, mesh.Material.BaseColorFactor.Y,
                         mesh.Material.BaseColorFactor.Z, mesh.Material.BaseColorFactor.W);

            if (mesh.Material.HasBaseColorTexture)
            {
                GL.ActiveTexture(Const.GL_TEXTURE0);
                GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.BaseColorTextureID);
                GL.Uniform1i(_useAlbedoLoc, 1);
                GL.Uniform1i(_albedoMapLoc, 0);
            }
            else GL.Uniform1i(_useAlbedoLoc, 0);

            if (_metallicFactorLoc != -1) GL.Uniform1f(_metallicFactorLoc, mesh.Material.MetallicFactor);
            if (_roughnessFactorLoc != -1) GL.Uniform1f(_roughnessFactorLoc, mesh.Material.RoughnessFactor);
            if (_normalScaleLoc != -1) GL.Uniform1f(_normalScaleLoc, mesh.Material.NormalScale);
            if (_occlusionStrengthLoc != -1) GL.Uniform1f(_occlusionStrengthLoc, mesh.Material.OcclusionStrength);
            if (_emissiveFactorLoc != -1) GL.Uniform3f(_emissiveFactorLoc, mesh.Material.EmissiveFactor.X, mesh.Material.EmissiveFactor.Y, mesh.Material.EmissiveFactor.Z);
            if (_hasNormalTextureLoc != -1) GL.Uniform1i(_hasNormalTextureLoc, mesh.Material.HasNormalTexture ? 1 : 0);
            if (_hasMetallicRoughnessTextureLoc != -1) GL.Uniform1i(_hasMetallicRoughnessTextureLoc, mesh.Material.HasMetallicRoughnessTexture ? 1 : 0);
            if (_hasOcclusionTextureLoc != -1) GL.Uniform1i(_hasOcclusionTextureLoc, mesh.Material.HasOcclusionTexture ? 1 : 0);
            if (_hasEmissiveTextureLoc != -1) GL.Uniform1i(_hasEmissiveTextureLoc, mesh.Material.HasEmissiveTexture ? 1 : 0);

            if (mesh.Material.HasNormalTexture && mesh.Material.NormalTextureID != 0)
            {
                GL.ActiveTexture(Const.GL_TEXTURE0 + 3);
                GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.NormalTextureID);
            }
            if (mesh.Material.HasMetallicRoughnessTexture && mesh.Material.MetallicRoughnessTextureID != 0)
            {
                GL.ActiveTexture(Const.GL_TEXTURE0 + 4);
                GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.MetallicRoughnessTextureID);
            }
            if (mesh.Material.HasOcclusionTexture && mesh.Material.OcclusionTextureID != 0)
            {
                GL.ActiveTexture(Const.GL_TEXTURE0 + 5);
                GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.OcclusionTextureID);
            }
            if (mesh.Material.HasEmissiveTexture && mesh.Material.EmissiveTextureID != 0)
            {
                GL.ActiveTexture(Const.GL_TEXTURE0 + 6);
                GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.EmissiveTextureID);
            }
        }

        /// <summary>
        /// Draw HLOD region wireframe visualization in world space.
        /// Color coding:
        ///   Green (0,1,0):    Near range (< 35m) — individual instancing digunakan
        ///   Yellow (1,1,0):   Mid range (35-120m) — HLOD merged mesh DI-RENDER
        /// <summary>Render debugging visualization for Octahedral Impostor regions.</summary>
                public void DrawImpostorDebug(Camera camera, Plane[] cameraFrustum, HUD hud = null)
        {
            if (!_impostorsBuilt || _impostorEntries.Count == 0 || _impShaderProgram == 0) return;

            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            float fadeStartDist = ImpostorNearDist - ImpostorFadeDist;
            if (fadeStartDist < 0f) fadeStartDist = 0f;
            float impNearSq = fadeStartDist * fadeStartDist;
            float impFarSq = ImpostorFarDist * ImpostorFarDist;
            Vector3 cameraPos = camera.Position;

            ImpVisibleRegions = 0;
            foreach (var imp in _impostorEntries)
            {
                float centerY = (imp.WorldAABB.Min.Y + imp.WorldAABB.Max.Y) * 0.5f;
                float dxc = cameraPos.X - imp.WorldPosition.X;
                float dyc = cameraPos.Y - centerY;
                float dzc = cameraPos.Z - imp.WorldPosition.Z;
                float distSq = dxc * dxc + dyc * dyc + dzc * dzc;
                bool inRange = distSq >= impNearSq && distSq <= impFarSq;
                if (!inRange) continue;

                bool inFrustum = IsAABBInFrustum(cameraFrustum, imp.WorldAABB, imp.WorldRadius + 5f);
                if (!inFrustum) continue;

                ImpVisibleRegions++;
                Vector3 boxColor = new Vector3(0.0f, 1.0f, 0.5f);
                TerrainChunk.DrawAABBWireframe(imp.WorldAABB, boxColor, camera);

                int dbgMode = DarkEngine3D_gl_csharp.Engine.Inputs.Keyboard.GetImpostorDebugMode();
                if (dbgMode == 1)
                {
                    GL.UseProgram(_impShaderProgram);
                    GL.UniformMatrix4fv(_impViewLoc, 1, false, (float*)Unsafe.AsPointer(ref view));
                    GL.UniformMatrix4fv(_impProjLoc, 1, false, (float*)Unsafe.AsPointer(ref proj));
                    GL.Uniform3f(_impCenterLoc, imp.WorldPosition.X, centerY, imp.WorldPosition.Z);
                    GL.Uniform1f(_impRadiusLoc, imp.WorldRadius);
                    GL.Uniform3f(_impCameraRightLoc, view.M11, view.M21, view.M31);
                    GL.Uniform3f(_impCameraUpLoc, view.M12, view.M22, view.M32);
                    GL.Uniform2f(_impAtlasTilesLoc, 8f, 1f);
                    GL.Uniform3f(_impViewPosLoc, cameraPos.X, cameraPos.Y, cameraPos.Z);
                    GL.Uniform3f(_impSunDirLoc, 0.5f, 0.707f, 0.5f);
                    if (_impUseFogLoc != -1) GL.Uniform1i(_impUseFogLoc, 0);
                    if (_impDebugOpacityLoc != -1) GL.Uniform1f(_impDebugOpacityLoc, 0.4f);
                    GL.ActiveTexture(Const.GL_TEXTURE0);
                    GL.BindTexture(Const.GL_TEXTURE_2D, imp.AtlasTexture);
                    SetupImpostorVAO();
                    GL.BindVertexArray(_impBillboardVAO);
                    GL.DrawElements(Const.GL_TRIANGLES, 6, Const.GL_UNSIGNED_INT, null);
                }
            }

            // Atlas preview in HUD (use first impostor entry)
            if (hud != null && _impostorEntries.Count > 0)
            {
                uint previewTex = 0;
                for (int i = 0; i < _impostorEntries.Count; i++)
                {
                    if (_impostorEntries[i].AtlasTexture != 0)
                    {
                        previewTex = _impostorEntries[i].AtlasTexture;
                        break;
                    }
                }
                if (previewTex != 0)
                {
                    int w = Glfw.WindowWidth;
                    int h = Glfw.WindowHeight;
                    float previewW = Math.Min(w * 0.18f, 240f);
                    float previewH = previewW * 0.5f; // 2:1 ratio since atlas is 256x128
                    float px = w - previewW - 12f;
                    float py = 70f;
                    hud.DrawBox(px - 4f, py - 4f, previewW + 8f, previewH + 8f, new Vector3(0.05f, 0.05f, 0.08f));
                    hud.DrawBox(px, py, previewW, previewH, new Vector3(0.2f, 0.2f, 0.3f));

                    // Draw atlas texture using HUD shader
                    uint hudShader = Shader.GetHudShaderProgram();
                    if (hudShader != 0)
                    {
                        GL.UseProgram(hudShader);
                        GL.Enable(Const.GL_BLEND);
                        GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);
                        int texLoc = GL.GetUniformLocation(hudShader, "hudTexture");
                        if (texLoc != -1) GL.Uniform1i(texLoc, 0);
                        GL.ActiveTexture(Const.GL_TEXTURE0);
                        GL.BindTexture(Const.GL_TEXTURE_2D, previewTex);

                        float imgNdcX0 = (px / w) * 2f - 1f;
                        float imgNdcY0 = (py / h) * 2f - 1f;
                        float imgNdcX1 = ((px + previewW) / w) * 2f - 1f;
                        float imgNdcY1 = ((py + previewH) / h) * 2f - 1f;
                        float[] imgVerts = {
                            imgNdcX0, imgNdcY0, 0f, 0f,
                            imgNdcX1, imgNdcY0, 1f, 0f,
                            imgNdcX1, imgNdcY1, 1f, 1f,
                            imgNdcX0, imgNdcY1, 0f, 1f,
                        };
                        uint[] imgIdx2 = { 0, 1, 2, 0, 2, 3 };
                        uint imgVao, imgVbo, imgEbo;
                        GL.GenVertexArrays(1, &imgVao);
                        GL.GenBuffers(1, &imgVbo);
                        GL.BindVertexArray(imgVao);
                        GL.BindBuffer(Const.GL_ARRAY_BUFFER, imgVbo);
                        fixed (float* p = imgVerts)
                            GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(imgVerts.Length * sizeof(float)), p, Const.GL_DYNAMIC_DRAW);
                        GL.GenBuffers(1, &imgEbo);
                        GL.BindBuffer(Const.GL_ELEMENT_ARRAY_BUFFER, imgEbo);
                        fixed (uint* ip = imgIdx2)
                            GL.BufferData(Const.GL_ELEMENT_ARRAY_BUFFER, (nuint)(imgIdx2.Length * sizeof(uint)), ip, Const.GL_DYNAMIC_DRAW);
                        GL.EnableVertexAttribArray(0);
                        GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)0);
                        GL.EnableVertexAttribArray(1);
                        GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
                        GL.DrawElements(Const.GL_TRIANGLES, 6, Const.GL_UNSIGNED_INT, null);
                        GL.BindVertexArray(0);
                        GL.DeleteVertexArrays(1, &imgVao);
                        GL.DeleteBuffers(1, &imgVbo);
                        GL.DeleteBuffers(1, &imgEbo);
                    }
                }
            }
        }

public void DrawHLODDebug(Camera camera, Plane[] cameraFrustum)
        {
            if (!_hlodEnabled || _hlodRegions == null) return;

            int hlodW = _hlodRegions.GetLength(0);
            int hlodH = _hlodRegions.GetLength(1);
            float nearSq = HLODNearDist * HLODNearDist;
            float midSq = HLODMidDist * HLODMidDist;

            for (int hz = 0; hz < hlodH; hz++)
            {
                for (int hx = 0; hx < hlodW; hx++)
                {
                    var reg = _hlodRegions[hx, hz];
                    if (reg == null || reg.ObjectCount == 0) continue;

                    // Choose color based on distance from camera
                    float dxc = camera.Position.X - reg.CenterX;
                    float dzc = camera.Position.Z - reg.CenterZ;
                    float distSq = dxc * dxc + dzc * dzc;

                    // Check frustum
                    bool inFrustum = IsAABBInFrustum(cameraFrustum, reg.WorldAABB, 10f);

                    Vector3 color;
                    if (!inFrustum)
                        color = new Vector3(0.3f, 0.3f, 0.3f); // dim gray: outside frustum
                    else if (distSq < nearSq)
                        color = new Vector3(0.0f, 1.0f, 0.0f); // green: near range (individual instancing)
                    else if (distSq < midSq)
                        color = new Vector3(1.0f, 1.0f, 0.0f); // yellow: mid range (HLOD rendered)
                    else
                        color = new Vector3(1.0f, 0.0f, 0.0f); // red: far range (culled)

                    // Draw AABB wireframe with slightly extended Y bounds for visibility
                    var aabb = reg.WorldAABB;
                    float heightExt = (aabb.Max.Y - aabb.Min.Y) * 0.5f + 2f;
                    float centerY = (aabb.Min.Y + aabb.Max.Y) * 0.5f;
                    var visAABB = new Helpers.ObjectHelpers.AABB(
                        new Vector3(aabb.Min.X, centerY - heightExt, aabb.Min.Z),
                        new Vector3(aabb.Max.X, centerY + heightExt, aabb.Max.Z)
                    );
                    TerrainChunk.DrawAABBWireframe(visAABB, color, camera);
                }
            }
        }

        /// <summary>Cleanup GPU resources for HLOD merged meshes.</summary>
        public void DisposeHLOD()
        {
            if (_hlodRegions == null) return;
            for (int hx = 0; hx < _hlodRegions.GetLength(0); hx++)
            {
                for (int hz = 0; hz < _hlodRegions.GetLength(1); hz++)
                {
                    var reg = _hlodRegions[hx, hz];
                    if (reg?.MergedMesh.VAO == 0) continue;
                    if (reg != null)
                    {
                        uint vao = reg.MergedMesh.VAO;
                        uint vbo = reg.MergedMesh.VBO;
                        uint ebo = reg.MergedMesh.EBO;
                        GL.DeleteVertexArrays(1, &vao);
                        GL.DeleteBuffers(1, &vbo);
                        if (ebo != 0) GL.DeleteBuffers(1, &ebo);
                    }
                }
            }
            _hlodRegions = null;
            _hlodEnabled = false;
        }

        /// <summary>Cleanup GPU resources for Octahedral Impostors.</summary>
                public void DisposeImpostors()
        {
            if (_impostorEntries.Count == 0) return;
            foreach (var imp in _impostorEntries)
            {
                if (imp.AtlasTexture != 0)
                {
                    uint tex = imp.AtlasTexture;
                    GL.DeleteTextures(1, &tex);
                }
            }
            _impostorEntries.Clear();
            _impostorsBuilt = false;
        }


        // ────────────────────────────────────────────────────────────────
        //  STATIC HELPERS (unchanged)
        // ────────────────────────────────────────────────────────────────

        public static Plane[] ExtractPlanes(Matrix4x4 vp)
        {
            Plane[] planes = new Plane[6];
            planes[0] = Plane.Normalize(new Plane(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41));
            planes[1] = Plane.Normalize(new Plane(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41));
            planes[2] = Plane.Normalize(new Plane(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42));
            planes[3] = Plane.Normalize(new Plane(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42));
            // Near (a3 + a2 untuk row-major VP matrix)
            planes[4] = Plane.Normalize(new Plane(vp.M14 + vp.M13, vp.M24 + vp.M23, vp.M34 + vp.M33, vp.M44 + vp.M43));
            planes[5] = Plane.Normalize(new Plane(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43));
            return planes;
        }

        public static Plane[] ExtractCameraFrustum(Matrix4x4 vp)
        {
            Plane[] planes = new Plane[6];
            planes[0] = Plane.Normalize(new Plane(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41));
            planes[1] = Plane.Normalize(new Plane(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41));
            planes[2] = Plane.Normalize(new Plane(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42));
            planes[3] = Plane.Normalize(new Plane(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42));
            // Near (a3 + a2 untuk row-major VP matrix)
            planes[4] = Plane.Normalize(new Plane(vp.M14 + vp.M13, vp.M24 + vp.M23, vp.M34 + vp.M33, vp.M44 + vp.M43));
            planes[5] = Plane.Normalize(new Plane(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43));
            return planes;
        }

        public static bool IsAABBInFrustum(Plane[] planes, AABB aabb, float margin = 3.0f)
        {
            Vector3 min = aabb.Min - new Vector3(margin);
            Vector3 max = aabb.Max + new Vector3(margin);
            foreach (var pl in planes)
            {
                Vector3 p = new Vector3(
                    pl.Normal.X >= 0 ? max.X : min.X,
                    pl.Normal.Y >= 0 ? max.Y : min.Y,
                    pl.Normal.Z >= 0 ? max.Z : min.Z
                );
                if (Vector3.Dot(pl.Normal, p) + pl.D < 0f)
                    return false;
            }
            return true;
        }
    }
}
