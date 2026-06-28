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

        // Per-instance flags
        public bool CastShadow = true;
        public bool UseAlphaTest = true;

        // Current LOD level being rendered (set every frame by StaticObjectManager.Draw)
        public int CurrentLOD = 0;

        // Occlusion culling flag (di-set oleh OC system setiap frame)
        public bool IsVisible = true;

        // Apakah object ini bisa menjadi occluder (menghalangi object lain)
        public bool IsOccluder = false;

        // Apakah object ini bisa ditabrak (collision untuk player/NPC/camera)
        public bool IsCollidable = false;

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
        private int ObjectDrawn = 0;
        private int TotalObject = 0;
        private readonly Dictionary<string, GltfModelGpuData> _modelCache = [];
        private readonly Dictionary<string, List<StaticObjectGroup>> _modelGroups = [];
        private readonly List<StaticObject> _objects = [];
        private readonly uint _shaderProgram;

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

        public IReadOnlyList<StaticObject> GetObjects() => _objects;

        public Vector3 RotationCorrection = Vector3.Zero;

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
        }

        private void AnalyzeGltfGroups(string path, GltfModelGpuData gpuData)
        {
            if (_modelGroups.ContainsKey(path)) return;

            var groups = new Dictionary<string, StaticObjectGroup>();

            for (int i = 0; i < gpuData.Data.Meshes.Length; i++)
            {
                string meshName = gpuData.Data.Meshes[i].Name ?? $"mesh_{i}";
                string baseName = meshName;
                int lodLevel = 1;

                var lodMatch = System.Text.RegularExpressions.Regex.Match(meshName, @"^(.*)_LOD(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (lodMatch.Success)
                {
                    baseName = lodMatch.Groups[1].Value.Trim();
                    int.TryParse(lodMatch.Groups[2].Value, out lodLevel);
                }
                else
                {
                    var numericMatch = System.Text.RegularExpressions.Regex.Match(meshName, @"^(.*)_(\d+)");
                    if (numericMatch.Success)
                    {
                        baseName = numericMatch.Groups[1].Value.Trim();
                        int.TryParse(numericMatch.Groups[2].Value, out lodLevel);
                    }
                }

                if (!groups.TryGetValue(baseName, out var group))
                {
                    group = new StaticObjectGroup { BaseName = baseName };
                    groups[baseName] = group;
                }

                if (!group.Lods.ContainsKey(lodLevel)) group.Lods[lodLevel] = new List<int>();
                group.Lods[lodLevel].Add(i);
            }

            // Pre-compute LodFallback for each group
            foreach (var g in groups.Values)
            {
                var sorted = g.Lods.Keys.OrderBy(k => k).ToArray();
                g.MaxLOD = sorted.Last();
                for (int t = 0; t < 4; t++)
                    g.LodFallback[t] = sorted.FirstOrDefault(k => k >= t, sorted.Last());

                // Compute per-group AABB from all meshes in this group (lowest LOD has most detail)
                // Terapkan node transform agar AABB sesuai dengan visual rendering
                Vector3 mn = new(float.PositiveInfinity);
                Vector3 mx = new(float.NegativeInfinity);
                bool hasVerts = false;
                // Gather all mesh indices from all LOD levels of this group
                var allMeshIndices = g.Lods.Values.SelectMany(mi => mi).Distinct().ToArray();
                foreach (int mi in allMeshIndices)
                {
                    if (mi < 0 || mi >= gpuData.Data.Meshes.Length) continue;

                    // Cari node transform untuk mesh ini (sama seperti rendering: nodeMatrix * baseWorldMat)
                    int nodeIdx = (gpuData.MeshToNode != null && mi < gpuData.MeshToNode.Length)
                        ? gpuData.MeshToNode[mi] : -1;
                    Matrix4x4 nodeMat = (nodeIdx >= 0 && gpuData.Data.Nodes != null && nodeIdx < gpuData.Data.Nodes.Length)
                        ? gpuData.Data.Nodes[nodeIdx].LocalMatrix
                        : Matrix4x4.Identity;

                    var verts = gpuData.Data.Meshes[mi].Vertices;
                    if (verts == null || verts.Length == 0) continue;
                    hasVerts = true;
                    for (int vi = 0; vi < verts.Length; vi++)
                    {
                        // Transform vertex ke model-local space pakai node matrix
                        Vector3 modelLocal = Vector3.Transform(verts[vi].Position, nodeMat);
                        mn = Vector3.Min(mn, modelLocal);
                        mx = Vector3.Max(mx, modelLocal);
                    }
                }
                g.LocalAABB = hasVerts
                    ? new AABB(mn, mx)
                    : gpuData.LocalAABB;  // fallback to global AABB

                // Debug: log LOD structure
                var lodInfo = string.Join(", ", g.Lods.Select(kv => $"LOD{kv.Key}: meshes[{string.Join(",", kv.Value)}]"));
            }

            _modelGroups[path] = groups.Values.ToList();
        }

        public void AddObject(string path, Vector3 pos, float yaw = 0, float scale = 1.0f, string groupName = "", bool snapToTerrain = false, TerrainChunk? terrain = null, string? collisionPart = null, float overrideCollisionSizeX = 0f, float overrideCollisionSizeZ = 0f)
        {
            if (!_modelCache.TryGetValue(path, out var gpuData))
            {
                var data = GltfLoader.Load(path);
                gpuData = new GltfModelGpuData(data);
                _modelCache[path] = gpuData;
                AnalyzeGltfGroups(path, gpuData);
            }

            var availableGroups = _modelGroups[path];
            if (availableGroups.Count == 0) return;

            StaticObjectGroup selectedGroup;
            if (string.IsNullOrEmpty(groupName))
            {
                var rng = new Random();
                selectedGroup = availableGroups[rng.Next(availableGroups.Count)];
            }
            else
            {
                selectedGroup = availableGroups.FirstOrDefault(g => g.BaseName.Equals(groupName, StringComparison.OrdinalIgnoreCase))
                                ?? availableGroups[0];
            }

            // Snap to terrain height if requested
            if (snapToTerrain && terrain != null)
            {
                float terrainY = terrain.GetHeightAt(pos.X, pos.Z);
                pos.Y = terrainY;
            }

            // Pre-compute correction quaternion for this manager
            float rx = RotationCorrection.X * MathF.PI / 180f;
            float ry = RotationCorrection.Y * MathF.PI / 180f;
            float rz = RotationCorrection.Z * MathF.PI / 180f;
            var corrQuat = Quaternion.CreateFromYawPitchRoll(ry, rx, rz);

            var sobj = new StaticObject(gpuData, selectedGroup, pos, yaw, scale);
            sobj.CorrectionQuat = corrQuat;
            // Pre-compute cached values (static objects never move)
            // Use per-group AABB instead of combined GPU AABB for better accuracy
            var localAABB = selectedGroup.LocalAABB.Min != selectedGroup.LocalAABB.Max
                ? selectedGroup.LocalAABB
                : sobj.GpuData.LocalAABB;
            sobj.CollisionPart = collisionPart;
            // Compute BaseWorldMat DULU (sama persis dengan rendering), pakai ini untuk AABB
            sobj.CachedBaseWorldMat = Matrix4x4.CreateScale(sobj.Scale) *
                                       Matrix4x4.CreateFromQuaternion(sobj.CorrectionQuat) *
                                       Matrix4x4.CreateFromQuaternion(sobj.Rotation) *
                                       Matrix4x4.CreateTranslation(sobj.Position);
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

            sobj.OverrideCollisionSizeX = overrideCollisionSizeX;
            sobj.OverrideCollisionSizeZ = overrideCollisionSizeZ;

            _objects.Add(sobj);
            TotalObject++;
        }

        public void AddRandomObjects(string path, int count, Vector3 center, float radius, float scale, TerrainChunk terrain, Action<float>? onProgress = null, string? collisionPart = null, float overrideCollisionSizeX = 0f, float overrideCollisionSizeZ = 0f)
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
                AddObject(path, new Vector3(x, y, z), (float)(rng.NextDouble() * 360), scale, collisionPart: collisionPart, overrideCollisionSizeX: overrideCollisionSizeX, overrideCollisionSizeZ: overrideCollisionSizeZ);

                if (onProgress != null && (i % reportInterval == 0 || i == count - 1))
                    onProgress((float)(i + 1) / count);
            }
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

            ObjectDrawn = 0;

            // ── Step 1: compute visible objects, determine LOD, build instance lists ──
            // Reuse instance lists — clear from previous frame instead of new alloc
            foreach (var kv in _drawInstanceLists)
                kv.Value.Mats.Clear();
            _drawInstanceLists.Clear();

            Matrix4x4 viewProj = cullFreezeEnabled && frozenViewProj.HasValue
                ? frozenViewProj.Value
                : view * proj;
            Plane[] cameraFrustum = ExtractCameraFrustum(viewProj);

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

                // Clamp targetLOD ke MaxLOD yang tersedia — jangan cull object hanya karena
                // tidak punya LOD variant tinggi. Model dengan 1 mesh (MaxLOD=0/1) akan tetap
                // dirender dengan mesh yang sama untuk semua jarak.
                if (targetLOD > group.MaxLOD)
                    targetLOD = group.MaxLOD;

                // CullAtMaxLOD: skip di LOD tertinggi meskipun model punya LOD itu
                if (CullAtMaxLOD && targetLOD >= 3)
                    continue;

                int actualLOD = group.LodFallback[targetLOD];
                obj.CurrentLOD = actualLOD; // Store for debug overlay

                var meshIndices = group.Lods[actualLOD];
                var baseWorldMat = obj.CachedBaseWorldMat;

                foreach (int meshIdx in meshIndices)
                {
                    int nodeIdx = (obj.GpuData.MeshToNode != null && meshIdx < obj.GpuData.MeshToNode.Length)
                                  ? obj.GpuData.MeshToNode[meshIdx] : -1;

                    var key = (RuntimeHelpers.GetHashCode(obj.GpuData), meshIdx, nodeIdx);
                    if (!_drawInstanceLists.TryGetValue(key, out var entry))
                    {
                        entry = new InstanceGroup { Mesh = obj.GpuData.Meshes[meshIdx], Gpu = obj.GpuData };
                        _drawInstanceLists[key] = entry;
                    }

                    Matrix4x4 modelMat = baseWorldMat;
                    if (nodeIdx >= 0 && obj.GpuData.Data.Nodes != null)
                        modelMat = obj.GpuData.Data.Nodes[nodeIdx].LocalMatrix * baseWorldMat;

                    entry.Mats.Add(modelMat);
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

                ObjectDrawn += mats.Count;
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

                // CullAtMaxLOD: skip shadow di LOD tertinggi meskipun model punya LOD itu
                if (CullAtMaxLOD && targetLOD >= 3)
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
                    if (nodeIdx >= 0 && obj.GpuData.Data.Nodes != null)
                        modelMat = obj.GpuData.Data.Nodes[nodeIdx].LocalMatrix * baseWorldMat;

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

            for (int mi = 0; mi < meshes.Length; mi++)
            {
                string? meshName = meshes[mi].Name;
                if (string.IsNullOrEmpty(meshName)) continue;
                if (!meshName.Contains(partName, StringComparison.OrdinalIgnoreCase)) continue;

                var verts = meshes[mi].Vertices;
                if (verts == null || verts.Length == 0) continue;

                // Cari node transform untuk mesh ini (sama seperti rendering: nodeMatrix * baseWorldMat)
                int nodeIdx = (gpuData.MeshToNode != null && mi < gpuData.MeshToNode.Length)
                    ? gpuData.MeshToNode[mi] : -1;
                Matrix4x4 nodeMat = (nodeIdx >= 0 && gpuData.Data.Nodes != null && nodeIdx < gpuData.Data.Nodes.Length)
                    ? gpuData.Data.Nodes[nodeIdx].LocalMatrix
                    : Matrix4x4.Identity;

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

        private static bool IsAABBInFrustum(Plane[] planes, AABB aabb, float margin = 3.0f)
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
