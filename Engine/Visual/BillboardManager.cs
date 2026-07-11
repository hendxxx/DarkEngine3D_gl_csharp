using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Scene;
using System.Numerics;
using System.Runtime.InteropServices;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>
    /// BillboardManager — handles baking billboard texture atlases and rendering
    /// camera-facing billboard quads for objects at LOD 3+.
    ///
    /// Baking pipeline:
    ///   1. At initialization, for each StaticObjectGroup that may need billboards,
    ///      the group's meshes are rendered from 8 angles around the Y axis into
    ///      a horizontal strip atlas (8 tiles × 128px × 128px = 1024×128).
    ///   2. At runtime, the impostor shader samples the atlas using the horizontal
    ///      angle from camera to object center, blending between the two nearest views.
    ///
    /// IMPORTANT: Baking is done during initialization (PreBakeAll), NOT during Draw().
    /// This avoids OpenGL state corruption from changing viewport/FBO/shader mid-frame.
    /// </summary>
    public unsafe class BillboardManager
    {
        private const int NumViews = 8;
        private const int ViewSize = 128;
        private const int AtlasW = NumViews * ViewSize; // 1024
        private const int AtlasH = ViewSize * 2;        // 256 (2 rows: albedo+roughness, normal+metallic)

        // ── Impostor (rendering) shader ─────────────────────────────────────
        private readonly uint _impostorShader;
        private readonly int _bbViewLoc, _bbProjLoc;
        private readonly int _bbCenterLoc, _bbRadiusLoc;
        private readonly int _bbCamRightLoc, _bbCamUpLoc;
        private readonly int _bbAtlasLoc, _bbAtlasTilesLoc;
        private readonly int _bbViewPosLoc, _bbSunDirLoc;
        private readonly int _bbLightColorLoc, _bbFogColorLoc, _bbUseFogLoc;
        private readonly int _bbDebugOpacityLoc;
        // CSM shadow uniforms (used for real-time shadow on billboard quads)
        private readonly int _bbShadowMap0Loc, _bbShadowMap1Loc, _bbShadowMap2Loc;
        private readonly int _bbShadowOffsetYLoc;
        private readonly int _bbLightSpace0Loc, _bbLightSpace1Loc, _bbLightSpace2Loc;
        private readonly int _bbCascade0Loc, _bbCascade1Loc, _bbCascade2Loc;
        private readonly int _bbShadowFilterLoc;

        // ── GL.ReadPixels uses float* for pixel data ───────────────────────
        // We'll pack our byte data and cast via fixed pointer

        // ── Baking shaders ─────────────────────────────────────────────────
        private readonly uint _bakeGbufferShader;        // G-buffer (albedo, roughness, normal, metallic)
        private readonly int _bakeGbufferModeLoc;

        // ── Unit quad for billboard rendering ──────────────────────────────
        private uint _quadVAO, _quadVBO;
        private const int QuadVertexCount = 6;

        // ── FBO for baking ─────────────────────────────────────────────────
        private uint _bakeFBO = 0;
        private uint _bakeColorRBO = 0;
        private uint _bakeDepthRBO = 0;

        // ── Saved window viewport (restored after baking) ──────────────────
        private int _savedViewX = 0, _savedViewY = 0, _savedViewW = 0, _savedViewH = 0;
        private uint _savedFBO = 0;

        // ── All created atlas textures (for cleanup in Dispose) ────────────
        private readonly List<uint> _allAtlases = [];

        /// <summary>Baked atlas texture IDs, exposed for debug rendering.</summary>
        public IReadOnlyList<uint> DebugBakedAtlases => _allAtlases;

        /// <summary>Save all baked atlas textures as PNG files to the specified directory.</summary>
        public void DumpAllAtlasesToPng(string dumpDir)
        {
            if (string.IsNullOrEmpty(dumpDir))
                dumpDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug");
            Directory.CreateDirectory(dumpDir);

            for (int i = 0; i < _allAtlases.Count; i++)
            {
                uint tex = _allAtlases[i];
                if (tex == 0) continue;
                string path = Path.Combine(dumpDir, $"atlas_{i}_{tex}.png");
                SaveManager.SaveTextureAsPng(path, tex, AtlasW, AtlasH);
            }

            Console.WriteLine($"[Billboard] Dumped {_allAtlases.Count} atlas textures to {dumpDir}");
        }

        // ── LOD fade configuration ─────────────────────────────────────────
        // Billboard activates at LOD2_Distance (200m). Fade starts there and
        // completes after FadeRange meters for a smooth mesh→billboard transition.
        private static float LODStart =>
            DarkEngine3D_gl_csharp.Engine.Config.LODConfig.ObjectLOD2_Distance;
        private const float FadeRange = 20f;

        // ── Per-frame billboard instances ──────────────────────────────────
        private readonly List<(StaticObject obj, float opacity)> _instances = [];

        // ── Baking state: keep a ref to all groups that need billboards ─────
        private readonly Dictionary<GltfModelGpuData, List<(StaticObjectGroup group, bool useNodeHierarchy)>> _pendingBakes = [];

        public BillboardManager()
        {
            _impostorShader = ShaderHelpers.LoadShader(
                "Artifacts/shaders/impostor_vertex.glsl",
                "Artifacts/shaders/impostor_fragment.glsl"
            );

            _bbViewLoc = GL.GetUniformLocation(_impostorShader, "view");
            _bbProjLoc = GL.GetUniformLocation(_impostorShader, "projection");
            _bbCenterLoc = GL.GetUniformLocation(_impostorShader, "center");
            _bbRadiusLoc = GL.GetUniformLocation(_impostorShader, "radius");
            _bbCamRightLoc = GL.GetUniformLocation(_impostorShader, "cameraRight");
            _bbCamUpLoc = GL.GetUniformLocation(_impostorShader, "cameraUp");
            _bbAtlasLoc = GL.GetUniformLocation(_impostorShader, "impostorAtlas");
            _bbAtlasTilesLoc = GL.GetUniformLocation(_impostorShader, "atlasTiles");
            _bbViewPosLoc = GL.GetUniformLocation(_impostorShader, "viewPos");
            _bbSunDirLoc = GL.GetUniformLocation(_impostorShader, "sunDir");
            _bbLightColorLoc = GL.GetUniformLocation(_impostorShader, "lightColor");
            _bbFogColorLoc = GL.GetUniformLocation(_impostorShader, "fogColor");
            _bbUseFogLoc = GL.GetUniformLocation(_impostorShader, "useFog");
            _bbDebugOpacityLoc = GL.GetUniformLocation(_impostorShader, "debugOpacity");

            // CSM shadow uniforms for impostor shader
            _bbShadowMap0Loc = GL.GetUniformLocation(_impostorShader, "shadowMap0");
            _bbShadowMap1Loc = GL.GetUniformLocation(_impostorShader, "shadowMap1");
            _bbShadowMap2Loc = GL.GetUniformLocation(_impostorShader, "shadowMap2");
            _bbLightSpace0Loc = GL.GetUniformLocation(_impostorShader, "lightSpaceMatrices[0]");
            _bbLightSpace1Loc = GL.GetUniformLocation(_impostorShader, "lightSpaceMatrices[1]");
            _bbLightSpace2Loc = GL.GetUniformLocation(_impostorShader, "lightSpaceMatrices[2]");
            _bbCascade0Loc = GL.GetUniformLocation(_impostorShader, "cascadeEnds[0]");
            _bbCascade1Loc = GL.GetUniformLocation(_impostorShader, "cascadeEnds[1]");
            _bbCascade2Loc = GL.GetUniformLocation(_impostorShader, "cascadeEnds[2]");
            _bbShadowFilterLoc = GL.GetUniformLocation(_impostorShader, "shadowFilterMode");
            _bbShadowOffsetYLoc = GL.GetUniformLocation(_impostorShader, "shadowOffsetY");

            // Load G-buffer bake shader for deferred impostor
            _bakeGbufferShader = ShaderHelpers.LoadShader(
                "Artifacts/shaders/bake_vertex.glsl",
                "Artifacts/shaders/bake_gbuffer_fragment.glsl"
            );
            _bakeGbufferModeLoc = GL.GetUniformLocation(_bakeGbufferShader, "gbufferMode");

            SetupQuad();
            SetupBakeFBO();
        }

        // ───────────────────────────────────────────────────────────────────
        //  Setup unit quad VAO for billboard rendering
        // ───────────────────────────────────────────────────────────────────
        private void SetupQuad()
        {
            float[] quadVerts =
            [
                -1f, -1f,   1f,  1f,  -1f,  1f,
                -1f, -1f,   1f, -1f,   1f,  1f,
            ];

            fixed (uint* pVao = &_quadVAO) GL.GenVertexArrays(1, pVao);
            fixed (uint* pVbo = &_quadVBO) GL.GenBuffers(1, pVbo);

            GL.BindVertexArray(_quadVAO);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _quadVBO);
            fixed (float* p = quadVerts)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(quadVerts.Length * sizeof(float)), p, Const.GL_STATIC_DRAW);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 2 * sizeof(float), null);
            GL.BindVertexArray(0);
        }

        // ───────────────────────────────────────────────────────────────────
        //  Setup offscreen FBO for atlas baking
        // ───────────────────────────────────────────────────────────────────
        private void SetupBakeFBO()
        {
            fixed (uint* p = &_bakeFBO) GL.GenFramebuffers(1, p);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _bakeFBO);

            fixed (uint* p = &_bakeColorRBO) GL.GenRenderbuffers(1, p);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, _bakeColorRBO);
            GL.RenderbufferStorage(Const.GL_RENDERBUFFER, (int)Const.GL_RGBA8, ViewSize, ViewSize);
            GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                Const.GL_RENDERBUFFER, _bakeColorRBO);

            fixed (uint* p = &_bakeDepthRBO) GL.GenRenderbuffers(1, p);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, _bakeDepthRBO);
            GL.RenderbufferStorage(Const.GL_RENDERBUFFER, Const.GL_DEPTH_COMPONENT, ViewSize, ViewSize);
            GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_ATTACHMENT,
                Const.GL_RENDERBUFFER, _bakeDepthRBO);

            int status = GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                Console.WriteLine($"[Billboard] WARN: Bake FBO incomplete (status={status})");

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, 0);
        }

        // ───────────────────────────────────────────────────────────────────
        //  Registration: queue groups for baking at initialization time
        // ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Register a group + gpuData for billboard baking.
        /// Must be called for every group that may need billboard LOD, BEFORE
        /// BakeAllPending() is called.
        /// </summary>
        public void RegisterForBaking(GltfModelGpuData gpuData, StaticObjectGroup group, bool useNodeHierarchy)
        {
            if (group == null || gpuData == null) return;
            if (group.HasBillboard) return; // already baked

            if (!_pendingBakes.TryGetValue(gpuData, out var list))
            {
                list = [];
                _pendingBakes[gpuData] = list;
            }

            // Dedup: skip if this exact group reference is already pending
            foreach (var (g, _) in list)
                if (g == group) return;

            list.Add((group, useNodeHierarchy));
        }

        /// <summary>
        /// Bake all registered billboard atlases.
        /// Call ONCE during initialization, AFTER all objects are loaded.
        /// This runs the full bake pipeline: for each group, render the meshes
        /// from 8 angles into an atlas texture.
        /// </summary>
        public void BakeAllPending()
        {
            if (_pendingBakes.Count == 0) return;

            int totalGroups = 0;
            foreach (var kv in _pendingBakes)
                totalGroups += kv.Value.Count;

            Console.WriteLine($"[Billboard] Baking {totalGroups} billboard atlases...");
            int baked = 0;

            foreach (var kv in _pendingBakes)
            {
                var gpuData = kv.Key;
                foreach (var (group, useNodeHierarchy) in kv.Value)
                {
                    if (group.HasBillboard) continue;
                    if (BakeOne(gpuData, group, useNodeHierarchy))
                        baked++;
                }
            }

            _pendingBakes.Clear();
            Console.WriteLine($"[Billboard] Baked {baked}/{totalGroups} atlases.");
        }

        // ───────────────────────────────────────────────────────────────────
        //  Single-group atlas baking
        // ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bake ONE group's billboard atlas. Renders the group's meshes from
        /// 8 angles around the Y axis into a horizontal strip atlas.
        /// Stores the resulting OpenGL texture on group.BillboardAtlasTexture.
        /// </summary>
        private bool BakeOne(GltfModelGpuData gpuData, StaticObjectGroup group, bool useNodeHierarchy)
        {
            if (group.Lods.Count == 0) return false;

            // Use the most detailed available mesh for best quality atlas.
            // With detailed LOD sorting (BuildAssetGroups), Lods.Keys.Max() would
            // pick LOD3 (simplified) — baking from that gives a blurry atlas.
            int bakeLOD = group.Lods.ContainsKey(0) ? 0 : group.Lods.Keys.Min();
            var meshIndices = group.Lods[bakeLOD];
            if (meshIndices == null || meshIndices.Count == 0) return false;

            var nodes = gpuData.Data.Nodes;
            var meshes = gpuData.Data.Meshes;

            // Compute AABB of group meshes (local space, with node transforms)
            Vector3 mn = new(float.PositiveInfinity);
            Vector3 mx = new(float.NegativeInfinity);
            bool hasVerts = false;

            foreach (int mi in meshIndices)
            {
                if (mi < 0 || mi >= meshes.Length) continue;

                int nodeIdx = (gpuData.MeshToNode != null && mi < gpuData.MeshToNode.Length)
                    ? gpuData.MeshToNode[mi] : -1;
                Matrix4x4 nodeMat = Matrix4x4.Identity;
                if (nodeIdx >= 0 && nodes != null && nodeIdx < nodes.Length)
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

            if (!hasVerts) return false;

            // Compute billboard radius from max extent
            Vector3 extents = mx - mn;
            float maxExtent = MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z));
            float radius = maxExtent * 0.6f;
            Vector3 centerOffset = (mn + mx) * 0.5f;

            // Allocate atlas pixel buffer (2 rows: row 0 = albedo+roughness, row 1 = normal+metallic)
            byte[] atlasPixels = new byte[AtlasW * AtlasH * 4];
            Array.Fill<byte>(atlasPixels, 0);

            // Save current GL state so we can restore it
            _savedViewX = 0; _savedViewY = 0;
            _savedViewW = Glfw.WindowWidth;
            _savedViewH = Glfw.WindowHeight;
            _savedFBO = 0;

            GL.Viewport(0, 0, ViewSize, ViewSize);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _bakeFBO);

            float orthoSize = MathF.Max(maxExtent * 0.7f, 0.01f);
            float camDist = orthoSize * 2f;
            float nearP = 0.5f;
            float farP = camDist * 3f;
            Matrix4x4 bakeProj = Matrix4x4.CreateOrthographic(orthoSize * 2f, orthoSize * 2f, nearP, farP);

            Vector3 bakeSunDir = Vector3.Normalize(new Vector3(0.707f, 0.707f, 0.0f));
            Vector3 bakeLightColor = new(0.95f, 0.93f, 0.88f);

            GL.Enable(Const.GL_DEPTH_TEST);
            GL.Enable(Const.GL_CULL_FACE);
            GL.CullFace(Const.GL_BACK);
            GL.FrontFace(Const.GL_CCW);

            // ── Compute group center from MeshOriginalCenters ──
            Vector3 bakedGroupCenter = Vector3.Zero;
            int bakedGcCount = 0;
            bool hasOriginalCenters = gpuData.MeshOriginalCenters.Count > 0;
            if (hasOriginalCenters)
            {
                foreach (int gmi in meshIndices)
                {
                    if (gpuData.MeshOriginalCenters.TryGetValue(gmi, out var c))
                    { bakedGroupCenter += c; bakedGcCount++; }
                }
                if (bakedGcCount > 0)
                    bakedGroupCenter /= bakedGcCount;
            }

            // ── Set PBR sampler units (used by both bake passes) ──
            uint bakeProg = _bakeGbufferShader;
            GL.UseProgram(bakeProg);
            int bmLoc = GL.GetUniformLocation(bakeProg, "model");
            int bvLoc = GL.GetUniformLocation(bakeProg, "view");
            int bpLoc = GL.GetUniformLocation(bakeProg, "projection");
            int bSunDirLoc = GL.GetUniformLocation(bakeProg, "sunDir");
            int bLightColorLoc = GL.GetUniformLocation(bakeProg, "lightColor");
            int bViewPosLoc = GL.GetUniformLocation(bakeProg, "viewPos");
            int bBaseColorLoc = GL.GetUniformLocation(bakeProg, "baseColorFactor");
            int bUseAlbedoLoc = GL.GetUniformLocation(bakeProg, "useAlbedo");
            int bAlbedoMapLoc = GL.GetUniformLocation(bakeProg, "albedoMap");
            int bMetalLoc = GL.GetUniformLocation(bakeProg, "metallicFactor");
            int bRoughLoc = GL.GetUniformLocation(bakeProg, "roughnessFactor");
            int bNormScaleLoc = GL.GetUniformLocation(bakeProg, "normalScale");
            int bOccStrengthLoc = GL.GetUniformLocation(bakeProg, "occlusionStrength");
            int bHasNormLoc = GL.GetUniformLocation(bakeProg, "hasNormalTexture");
            int bHasMRLoc = GL.GetUniformLocation(bakeProg, "hasMetallicRoughnessTexture");
            int bHasOccLoc = GL.GetUniformLocation(bakeProg, "hasOcclusionTexture");
            int bHasEmissLoc = GL.GetUniformLocation(bakeProg, "hasEmissiveTexture");
            int bEmissFactorLoc = GL.GetUniformLocation(bakeProg, "emissiveFactor");
            int bNormMapLoc = GL.GetUniformLocation(bakeProg, "normalMap");
            int bMRMapLoc = GL.GetUniformLocation(bakeProg, "metallicRoughnessMap");
            int bOccMapLoc = GL.GetUniformLocation(bakeProg, "occlusionMap");
            int bEmissMapLoc = GL.GetUniformLocation(bakeProg, "emissiveMap");

            // Set fixed uniforms
            GL.Uniform3f(bSunDirLoc, bakeSunDir.X, bakeSunDir.Y, bakeSunDir.Z);
            GL.Uniform3f(bLightColorLoc, bakeLightColor.X, bakeLightColor.Y, bakeLightColor.Z);
            GL.UniformMatrix4fv(bpLoc, 1, false, &bakeProj.M11);

            // Set PBR sampler units
            if (bNormMapLoc != -1) GL.Uniform1i(bNormMapLoc, 3);
            if (bMRMapLoc != -1) GL.Uniform1i(bMRMapLoc, 4);
            if (bOccMapLoc != -1) GL.Uniform1i(bOccMapLoc, 5);
            if (bEmissMapLoc != -1) GL.Uniform1i(bEmissMapLoc, 6);

            // Helper: render all meshes for current view with current gbufferMode
            void RenderMeshes(int gbufferMode)
            {
                GL.Uniform1i(_bakeGbufferModeLoc, gbufferMode);
                foreach (int mi in meshIndices)
                {
                    if (mi < 0 || mi >= gpuData.Meshes.Length) continue;
                    var mesh = gpuData.Meshes[mi];

                    int nodeIdx = (gpuData.MeshToNode != null && mi < gpuData.MeshToNode.Length)
                        ? gpuData.MeshToNode[mi] : -1;
                    Matrix4x4 nodeMat = Matrix4x4.Identity;
                    if (nodeIdx >= 0 && nodes != null && nodeIdx < nodes.Length)
                        nodeMat = useNodeHierarchy
                            ? GetNodeWorldMatrix(nodes, nodeIdx)
                            : nodes[nodeIdx].LocalMatrix;

                    Vector3 bakeOffset = Vector3.Zero;
                    if (hasOriginalCenters && bakedGcCount > 0)
                    {
                        if (gpuData.MeshOriginalCenters.TryGetValue(mi, out var mc))
                            bakeOffset = mc - bakedGroupCenter;
                    }
                    Matrix4x4 bakeModel = Matrix4x4.CreateTranslation(bakeOffset) * nodeMat;
                    GL.UniformMatrix4fv(bmLoc, 1, false, &bakeModel.M11);

                    // Base color
                    GL.Uniform4f(bBaseColorLoc, mesh.Material.BaseColorFactor.X,
                        mesh.Material.BaseColorFactor.Y, mesh.Material.BaseColorFactor.Z,
                        mesh.Material.BaseColorFactor.W);

                    if (mesh.Material.HasBaseColorTexture && mesh.Material.BaseColorTextureID != 0)
                    {
                        GL.ActiveTexture(Const.GL_TEXTURE0);
                        GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.BaseColorTextureID);
                        if (bUseAlbedoLoc != -1) { GL.Uniform1i(bUseAlbedoLoc, 1); }
                        if (bAlbedoMapLoc != -1) { GL.Uniform1i(bAlbedoMapLoc, 0); }
                    }
                    else
                    {
                        if (bUseAlbedoLoc != -1) GL.Uniform1i(bUseAlbedoLoc, 0);
                    }

                    // PBR flags
                    if (bHasNormLoc != -1)
                        GL.Uniform1i(bHasNormLoc, mesh.Material.HasNormalTexture ? 1 : 0);
                    if (bHasMRLoc != -1)
                        GL.Uniform1i(bHasMRLoc, mesh.Material.HasMetallicRoughnessTexture ? 1 : 0);
                    if (bHasOccLoc != -1)
                        GL.Uniform1i(bHasOccLoc, mesh.Material.HasOcclusionTexture ? 1 : 0);
                    if (bHasEmissLoc != -1)
                        GL.Uniform1i(bHasEmissLoc, mesh.Material.HasEmissiveTexture ? 1 : 0);

                    // PBR factors
                    if (bMetalLoc != -1) GL.Uniform1f(bMetalLoc, mesh.Material.MetallicFactor);
                    if (bRoughLoc != -1) GL.Uniform1f(bRoughLoc, mesh.Material.RoughnessFactor);
                    if (bNormScaleLoc != -1) GL.Uniform1f(bNormScaleLoc, mesh.Material.NormalScale);
                    if (bOccStrengthLoc != -1) GL.Uniform1f(bOccStrengthLoc, mesh.Material.OcclusionStrength);
                    if (bEmissFactorLoc != -1) GL.Uniform3f(bEmissFactorLoc,
                        mesh.Material.EmissiveFactor.X, mesh.Material.EmissiveFactor.Y, mesh.Material.EmissiveFactor.Z);

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

                    if (mesh.Material.DoubleSided) GL.Disable(Const.GL_CULL_FACE);
                    else GL.Enable(Const.GL_CULL_FACE);

                    GL.BindVertexArray(mesh.VAO);
                    if (mesh.IndexCount > 0)
                        GL.DrawElements(Const.GL_TRIANGLES, mesh.IndexCount, Const.GL_UNSIGNED_INT, null);
                    else
                        GL.DrawArrays(Const.GL_TRIANGLES, 0, mesh.VertexCount);
                }
            }

            for (int vi = 0; vi < NumViews; vi++)
            {
                float angleRad = vi * (MathF.PI * 2f / NumViews);

                Vector3 camPos = new(
                    centerOffset.X + camDist * MathF.Sin(angleRad),
                    centerOffset.Y,
                    centerOffset.Z + camDist * MathF.Cos(angleRad)
                );

                Matrix4x4 bakeView = Matrix4x4.CreateLookAt(camPos, centerOffset, Vector3.UnitY);

                GL.ClearColor(0f, 0f, 0f, 0f);
                GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
                GL.UniformMatrix4fv(bvLoc, 1, false, &bakeView.M11);
                GL.Uniform3f(bViewPosLoc, camPos.X, camPos.Y, camPos.Z);

                // ── Pass 1: Albedo + Roughness (gbufferMode = 0) ──
                RenderMeshes(0);

                // Read row 0 pixels into atlas (rows 0..127)
                int tileX = vi * ViewSize;
                byte[] tileRowBuf = new byte[ViewSize * 4];
                for (int srcRow = 0; srcRow < ViewSize; srcRow++)
                {
                    fixed (byte* pRowBuf = tileRowBuf)
                        GL.ReadPixels(0, srcRow, ViewSize, 1, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (float*)pRowBuf);

                    int dstOff = srcRow * AtlasW * 4 + tileX * 4;
                    Buffer.BlockCopy(tileRowBuf, 0, atlasPixels, dstOff, ViewSize * 4);
                }

                // ── Alpha dilation: expand tree edge colors into background ──
                // Prevents dark edges from GL_LINEAR filtering blending tree
                // colors with black (0,0,0) background pixels.
                DilateRow0(atlasPixels, tileX, ViewSize, AtlasW);

                // ── Pass 2: Normal + Metallic (gbufferMode = 1) ──
                // No need to clear depth — same geometry at same position
                RenderMeshes(1);

                // Read row 1 pixels into atlas (rows 128..255)
                for (int srcRow = 0; srcRow < ViewSize; srcRow++)
                {
                    fixed (byte* pRowBuf = tileRowBuf)
                        GL.ReadPixels(0, srcRow, ViewSize, 1, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (float*)pRowBuf);

                    int dstOff = (srcRow + ViewSize) * AtlasW * 4 + tileX * 4;
                    Buffer.BlockCopy(tileRowBuf, 0, atlasPixels, dstOff, ViewSize * 4);
                }
            }

            // ── Upload atlas as OpenGL texture (1024×256) ──
            uint atlasTexture;
            GL.GenTextures(1, &atlasTexture);
            GL.BindTexture(Const.GL_TEXTURE_2D, atlasTexture);

            fixed (byte* pData = atlasPixels)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                    AtlasW, AtlasH, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, pData);
            }

            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            // Store on group
            group.BillboardAtlasTexture = atlasTexture;
            group.BillboardRadius = radius;

            Console.WriteLine($"[BakeOne] Group '{group.BaseName}': G-buffer atlas baked ({AtlasW}×{AtlasH})");

            _allAtlases.Add(atlasTexture);

            // Restore GL state
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _savedFBO);
            GL.BindVertexArray(0);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, 0);
            if (_savedViewW > 0 && _savedViewH > 0)
                GL.Viewport(_savedViewX, _savedViewY, _savedViewW, _savedViewH);

            return true;
        }

        // ───────────────────────────────────────────────────────────────────
        //  Alpha dilation: expand tree edge colors into background pixels
        // ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Dilate row 0 (albedo+roughness) in the given tile: for each background
        /// pixel (roughness < threshold), copy the average RGB from neighboring
        /// foreground pixels. This prevents GL_LINEAR filtering from blending
        /// tree edge colors into black (0,0,0,0) background, which causes dark
        /// outlines around the billboard.
        ///
        /// Threshold: roughness byte < 5 (float 0.02 * 255). The bake shader
        /// clamps roughness to min 0.04, so anything below 0.02 is background.
        /// </summary>
        private static void DilateRow0(byte[] pixels, int tileX, int tileSize, int atlasW)
        {
            const byte threshold = 5; // roughness < 0.02 (float) → byte < 5

            // Work on a copy of the tile to avoid cascading (dilated pixels
            // influencing neighbors in the same pass)
            int tileByteSize = tileSize * tileSize * 4;
            byte[] tileCopy = new byte[tileByteSize];

            // Run 3 passes for a ~3-pixel dilation border (thicker = less dark edge)
            for (int pass = 0; pass < 3; pass++)
            {
                // Copy current tile state (in atlas) into temp buffer
                for (int ty = 0; ty < tileSize; ty++)
                {
                    int srcOff = ty * atlasW * 4 + tileX * 4;
                    int dstOff = ty * tileSize * 4;
                    Buffer.BlockCopy(pixels, srcOff, tileCopy, dstOff, tileSize * 4);
                }

                // Iterate over each pixel in the tile
            for (int ty = 0; ty < tileSize; ty++)
            {
                for (int tx = 0; tx < tileSize; tx++)
                {
                    int pixelOff = ty * tileSize * 4 + tx * 4;

                    // Check if this pixel is background (roughness < threshold)
                    if (tileCopy[pixelOff + 3] >= threshold)
                        continue; // foreground pixel, skip

                    // Check if any of the 4 neighbors are non-background
                    int rSum = 0, gSum = 0, bSum = 0, count = 0;

                    // Up
                    if (ty > 0)
                    {
                        int nOff = (ty - 1) * tileSize * 4 + tx * 4;
                        if (tileCopy[nOff + 3] >= threshold)
                        { rSum += tileCopy[nOff]; gSum += tileCopy[nOff + 1]; bSum += tileCopy[nOff + 2]; count++; }
                    }
                    // Down
                    if (ty < tileSize - 1)
                    {
                        int nOff = (ty + 1) * tileSize * 4 + tx * 4;
                        if (tileCopy[nOff + 3] >= threshold)
                        { rSum += tileCopy[nOff]; gSum += tileCopy[nOff + 1]; bSum += tileCopy[nOff + 2]; count++; }
                    }
                    // Left
                    if (tx > 0)
                    {
                        int nOff = ty * tileSize * 4 + (tx - 1) * 4;
                        if (tileCopy[nOff + 3] >= threshold)
                        { rSum += tileCopy[nOff]; gSum += tileCopy[nOff + 1]; bSum += tileCopy[nOff + 2]; count++; }
                    }
                    // Right
                    if (tx < tileSize - 1)
                    {
                        int nOff = ty * tileSize * 4 + (tx + 1) * 4;
                        if (tileCopy[nOff + 3] >= threshold)
                        { rSum += tileCopy[nOff]; gSum += tileCopy[nOff + 1]; bSum += tileCopy[nOff + 2]; count++; }
                    }

                    if (count > 0)
                    {
                        // Write dilated color back to the ATLAS (not the copy)
                        int dstOff = ty * atlasW * 4 + tileX * 4 + tx * 4;
                        pixels[dstOff]     = (byte)(rSum / count); // R
                        pixels[dstOff + 1] = (byte)(gSum / count); // G
                        pixels[dstOff + 2] = (byte)(bSum / count); // B
                        // Alpha (roughness) stays 0 — background
                    }
                }
            }
            } // end pass loop
        }

        // ───────────────────────────────────────────────────────────────────
        //  Per-frame: collect and render billboard instances
        // ───────────────────────────────────────────────────────────────────

        public void Collect(StaticObject obj, float opacity)
        {
            if (obj.Group == null || !obj.Group.HasBillboard) return;
            _instances.Add((obj, Math.Clamp(opacity, 0f, 1f)));
        }

        public void Flush(Camera camera, Lights light, CSM csm = null)
        {
            if (_instances.Count == 0) return;

            GL.UseProgram(_impostorShader);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.Disable(Const.GL_CULL_FACE);
            GL.Enable(Const.GL_BLEND);
            GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);

            Matrix4x4 view = camera.GetViewMatrix();
            Matrix4x4 proj = camera.GetProjectionMatrix();
            Vector3 camRight = camera.Right;
            Vector3 camUp = camera.Up;

            GL.UniformMatrix4fv(_bbViewLoc, 1, false, &view.M11);
            GL.UniformMatrix4fv(_bbProjLoc, 1, false, &proj.M11);

            GL.Uniform3f(_bbCamRightLoc, camRight.X, camRight.Y, camRight.Z);
            GL.Uniform3f(_bbCamUpLoc, camUp.X, camUp.Y, camUp.Z);
            GL.Uniform3f(_bbViewPosLoc, camera.Position.X, camera.Position.Y, camera.Position.Z);

            GL.Uniform3f(_bbSunDirLoc, light.SunDir.X, light.SunDir.Y, light.SunDir.Z);
            GL.Uniform3f(_bbLightColorLoc, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
            GL.Uniform3f(_bbFogColorLoc, light.FogColor.X, light.FogColor.Y, light.FogColor.Z);
            if (_bbUseFogLoc != -1)
                GL.Uniform1i(_bbUseFogLoc, Inputs.Keyboard.GetIsFogActive() ? 1 : 0);

            // ── CSM Shadow uniforms ────────────────────────────────────────
            // Shadow textures are bound by GameScene.Render at units 6, 7, 8
            // before Draw() is called. We only need to tell our shader which
            // texture units to sample from.
            if (csm != null)
            {
                if (_bbShadowMap0Loc != -1) GL.Uniform1i(_bbShadowMap0Loc, 6);
                if (_bbShadowMap1Loc != -1) GL.Uniform1i(_bbShadowMap1Loc, 7);
                if (_bbShadowMap2Loc != -1) GL.Uniform1i(_bbShadowMap2Loc, 8);

                unsafe
                {
                    fixed (float* p0 = &csm.LightSpaceMatrices[0].M11)
                        GL.UniformMatrix4fv(_bbLightSpace0Loc, 1, false, p0);
                    fixed (float* p1 = &csm.LightSpaceMatrices[1].M11)
                        GL.UniformMatrix4fv(_bbLightSpace1Loc, 1, false, p1);
                    fixed (float* p2 = &csm.LightSpaceMatrices[2].M11)
                        GL.UniformMatrix4fv(_bbLightSpace2Loc, 1, false, p2);
                }

                if (_bbCascade0Loc != -1) GL.Uniform1f(_bbCascade0Loc, csm.CascadeEnds[0]);
                if (_bbCascade1Loc != -1) GL.Uniform1f(_bbCascade1Loc, csm.CascadeEnds[1]);
                if (_bbCascade2Loc != -1) GL.Uniform1f(_bbCascade2Loc, csm.CascadeEnds[2]);

                if (_bbShadowFilterLoc != -1)
                    GL.Uniform1i(_bbShadowFilterLoc, Inputs.Keyboard.GetIsHardShadow());
            }

            GL.Uniform2f(_bbAtlasTilesLoc, NumViews, 2); // 2 rows: row 0=albedo+roughness, row 1=normal+metallic
            GL.Uniform1i(_bbAtlasLoc, 0);

            GL.BindVertexArray(_quadVAO);

            foreach (var (obj, opacity) in _instances)
            {
                var group = obj.Group;
                if (group == null || !group.HasBillboard) continue;

                uint atlasTex = group.BillboardAtlasTexture;
                if (atlasTex == 0) continue;

                Vector3 center = obj.Position;
                float radius = group.BillboardRadius * obj.Scale;

                GL.Uniform3f(_bbCenterLoc, center.X, center.Y, center.Z);
                GL.Uniform1f(_bbRadiusLoc, radius);
                GL.Uniform1f(_bbDebugOpacityLoc, opacity);

                // Shadow sample offset: place the shadow sample at ~half the
                // billboard extent above ground so it detects canopy/trunk
                // shadows instead of comparing against the terrain surface.
                // Minimum 1m ensures even small objects (bushes, rocks) stay
                // clear of the terrain self-shadow issue.
                if (_bbShadowOffsetYLoc != -1)
                    GL.Uniform1f(_bbShadowOffsetYLoc, MathF.Max(radius * 0.5f, 1.0f));

                GL.ActiveTexture(Const.GL_TEXTURE0);
                GL.BindTexture(Const.GL_TEXTURE_2D, atlasTex);

                GL.DrawArrays(Const.GL_TRIANGLES, 0, QuadVertexCount);
            }

            GL.BindVertexArray(0);
            GL.Disable(Const.GL_BLEND);
            GL.Enable(Const.GL_CULL_FACE);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        }

        public void Clear() => _instances.Clear();

        public int InstanceCount => _instances.Count;

        // ───────────────────────────────────────────────────────────────────
        //  LOD fade helpers
        // ───────────────────────────────────────────────────────────────────

        public static float ComputeBillboardOpacity(float dist)
        {
            float fadeStart = LODStart;
            float fadeEnd = fadeStart + FadeRange;

            if (dist <= fadeStart) return 0f;
            if (dist >= fadeEnd) return 1f;

            float t = (dist - fadeStart) / (fadeEnd - fadeStart);
            return t * t * (3f - 2f * t); // smoothstep
        }

        /// <summary>Release all GPU resources.</summary>
        public void Dispose()
        {
            // Delete all created billboard atlas textures
            if (_allAtlases.Count > 0)
            {
                uint[] texArray = [.. _allAtlases];
                fixed (uint* p = texArray)
                    GL.DeleteTextures(texArray.Length, p);
            }
            _allAtlases.Clear();
            _pendingBakes.Clear();

            // Delete FBO and RBOs
            if (_bakeFBO != 0) { fixed (uint* p = &_bakeFBO) GL.DeleteFramebuffers(1, p); _bakeFBO = 0; }
            if (_bakeColorRBO != 0) { fixed (uint* p = &_bakeColorRBO) GL.DeleteRenderbuffers(1, p); _bakeColorRBO = 0; }
            if (_bakeDepthRBO != 0) { fixed (uint* p = &_bakeDepthRBO) GL.DeleteRenderbuffers(1, p); _bakeDepthRBO = 0; }
            if (_quadVAO != 0) { fixed (uint* p = &_quadVAO) GL.DeleteVertexArrays(1, p); _quadVAO = 0; }
            if (_quadVBO != 0) { fixed (uint* p = &_quadVBO) GL.DeleteBuffers(1, p); _quadVBO = 0; }

            GL.DeleteProgram(_impostorShader);
            GL.DeleteProgram(_bakeGbufferShader);
        }
    }
}
