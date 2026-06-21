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
    }

    public class StaticObject
    {
        public GltfModelGpuData GpuData;
        public StaticObjectGroup Group;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale = 1f;
        public AABB WorldAABB => GpuData.LocalAABB.ToWorld(Position, Scale);

        // Per-instance flags
        public bool CastShadow = true;
        public bool UseAlphaTest = true;

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
            _modelGroups[path] = groups.Values.ToList();
        }

        public void AddObject(string path, Vector3 pos, float yaw = 0, float scale = 1.0f, string groupName = "")
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

            _objects.Add(new StaticObject(gpuData, selectedGroup, pos, yaw, scale));
            TotalObject++;
        }

        public void AddRandomObjects(string path, int count, Vector3 center, float radius, float scale, TerrainChunk terrain)
        {
            var rng = new Random();
            for (int i = 0; i < count; i++)
            {
                float a = (float)(rng.NextDouble() * Math.PI * 2);
                float d = (float)(rng.NextDouble() * radius);
                float x = center.X + MathF.Cos(a) * d;
                float z = center.Z + MathF.Sin(a) * d;
                float y = terrain.GetHeightAt(x, z);
                AddObject(path, new Vector3(x, y, z), (float)(rng.NextDouble() * 360), scale);
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
        private void UploadInstances(uint vao, uint instanceVBO, Matrix4x4[] modelMatrices)
        {
            int stride = sizeof(float) * 16; // 16 floats per mat4
            int byteSize = modelMatrices.Length * stride;

            GL.BindBuffer(Const.GL_ARRAY_BUFFER, instanceVBO);
            GCHandle handle = GCHandle.Alloc(modelMatrices, GCHandleType.Pinned);
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

        public void Draw(Camera camera, Lights light, CSM csm = null)
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

            // Pre-calculate correction quaternion
            float rx = RotationCorrection.X * MathF.PI / 180f;
            float ry = RotationCorrection.Y * MathF.PI / 180f;
            float rz = RotationCorrection.Z * MathF.PI / 180f;
            var correctionQuat = Quaternion.CreateFromYawPitchRoll(ry, rx, rz);

            ObjectDrawn = 0;

            // ── Step 1: compute visible objects, determine LOD, build instance lists ──
            //   instanceLists: key = (gpuDataHash, meshIdx), value = list of model matrices
            var instanceLists = new Dictionary<(int gpuHash, int meshIdx, int nodeIdx), (List<Matrix4x4> mats, MeshGpu mesh, GltfModelGpuData gpu)>();
            var viewProj = view * proj;
            Plane[] cameraFrustum = ExtractCameraFrustum(viewProj);

            foreach (var obj in _objects)
            {
                if (!IsAABBInFrustum(cameraFrustum, obj.WorldAABB, 5f))
                    continue;

                float dist = Vector3.Distance(camera.Position, obj.Position);
                var group = obj.Group;
                if (group == null || group.Lods.Count == 0) continue;

                // LOD selection
                int targetLOD = (dist < 30f) ? 0 : (dist < 70f) ? 1 : (dist < 160f) ? 2 : 3;
                int actualLOD = targetLOD;
                if (!group.Lods.ContainsKey(actualLOD))
                {
                    var available = group.Lods.Keys.OrderBy(k => k).ToList();
                    actualLOD = available.FirstOrDefault(k => k >= targetLOD, available.Last());
                }

                var meshIndices = group.Lods[actualLOD];
                var baseWorldMat = Matrix4x4.CreateScale(obj.Scale) *
                                   Matrix4x4.CreateFromQuaternion(correctionQuat) *
                                   Matrix4x4.CreateFromQuaternion(obj.Rotation) *
                                   Matrix4x4.CreateTranslation(obj.Position);

                foreach (int meshIdx in meshIndices)
                {
                    int nodeIdx = (obj.GpuData.MeshToNode != null && meshIdx < obj.GpuData.MeshToNode.Length)
                                  ? obj.GpuData.MeshToNode[meshIdx] : -1;

                    var key = (RuntimeHelpers.GetHashCode(obj.GpuData), meshIdx, nodeIdx);
                    if (!instanceLists.TryGetValue(key, out var entry))
                    {
                        entry = (new List<Matrix4x4>(), obj.GpuData.Meshes[meshIdx], obj.GpuData);
                        instanceLists[key] = entry;
                    }

                    Matrix4x4 modelMat = baseWorldMat;
                    if (nodeIdx >= 0 && obj.GpuData.Data.Nodes != null)
                        modelMat = obj.GpuData.Data.Nodes[nodeIdx].LocalMatrix * baseWorldMat;

                    entry.mats.Add(modelMat);
                }
            }

            // ── Step 2: draw each instance group with a single instanced draw call ──
            foreach (var kv in instanceLists)
            {
                var (mats, mesh, gpu) = kv.Value;
                if (mats.Count == 0) continue;

                uint instanceVBO = GetInstanceVBO(kv.Key.gpuHash, kv.Key.meshIdx);
                UploadInstances(mesh.VAO, instanceVBO, [.. mats]);

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
            GL.Enable(Const.GL_CULL_FACE);
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

            float rx = RotationCorrection.X * MathF.PI / 180f;
            float ry = RotationCorrection.Y * MathF.PI / 180f;
            float rz = RotationCorrection.Z * MathF.PI / 180f;
            var correctionQuat = Quaternion.CreateFromYawPitchRoll(ry, rx, rz);

            // ── Step 1: filter by CastShadow + frustum, group by (gpuData, meshIdx) ──
            var instanceLists = new Dictionary<(int gpuHash, int meshIdx, int nodeIdx, bool hasAlpha), (List<Matrix4x4> mats, MeshGpu mesh, GltfModelGpuData gpu)>();

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

                float dist = Vector3.Distance(camera.Position, obj.Position);
                var group = obj.Group;
                if (group == null || group.Lods.Count == 0) continue;

                // Shadow LOD
                int targetLOD = (dist < 30f) ? 0 : (dist < 70f) ? 1 : (dist < 160f) ? 2 : 3;
                int actualLOD = targetLOD;
                if (!group.Lods.ContainsKey(actualLOD))
                {
                    var available = group.Lods.Keys.OrderBy(k => k).ToList();
                    actualLOD = available.FirstOrDefault(k => k >= targetLOD, available.Last());
                }

                // Auto-disable alpha test when shadow LOD > 1 (far away objects)
                bool useAlpha = obj.UseAlphaTest && UseAlpha && (targetLOD <= 1);

                var meshIndices = group.Lods[actualLOD];
                var baseWorldMat = Matrix4x4.CreateScale(obj.Scale) *
                                   Matrix4x4.CreateFromQuaternion(correctionQuat) *
                                   Matrix4x4.CreateFromQuaternion(obj.Rotation) *
                                   Matrix4x4.CreateTranslation(obj.Position);

                foreach (int meshIdx in meshIndices)
                {
                    int nodeIdx = (obj.GpuData.MeshToNode != null && meshIdx < obj.GpuData.MeshToNode.Length)
                                  ? obj.GpuData.MeshToNode[meshIdx] : -1;

                    var key = (RuntimeHelpers.GetHashCode(obj.GpuData), meshIdx, nodeIdx, useAlpha);
                    if (!instanceLists.TryGetValue(key, out var entry))
                    {
                        entry = (new List<Matrix4x4>(), obj.GpuData.Meshes[meshIdx], obj.GpuData);
                        instanceLists[key] = entry;
                    }

                    Matrix4x4 modelMat = baseWorldMat;
                    if (nodeIdx >= 0 && obj.GpuData.Data.Nodes != null)
                        modelMat = obj.GpuData.Data.Nodes[nodeIdx].LocalMatrix * baseWorldMat;

                    entry.mats.Add(modelMat);
                }
            }

            // ── Step 2: draw each group ──
            foreach (var kv in instanceLists)
            {
                var (mats, mesh, gpu) = kv.Value;
                bool hasAlpha = kv.Key.hasAlpha;
                if (mats.Count == 0) continue;

                uint instanceVBO = GetInstanceVBO(kv.Key.gpuHash, kv.Key.meshIdx);
                UploadInstances(mesh.VAO, instanceVBO, [.. mats]);

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
            planes[4] = Plane.Normalize(new Plane(vp.M13, vp.M23, vp.M33, vp.M43));
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
            planes[4] = Plane.Normalize(new Plane(vp.M13, vp.M23, vp.M33, vp.M43));
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
