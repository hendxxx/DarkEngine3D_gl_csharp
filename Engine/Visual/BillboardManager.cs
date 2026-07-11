using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
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
        private const int AtlasH = ViewSize;            // 128

        // ── Impostor (rendering) shader ─────────────────────────────────────
        private readonly uint _impostorShader;
        private readonly int _bbViewLoc, _bbProjLoc;
        private readonly int _bbCenterLoc, _bbRadiusLoc;
        private readonly int _bbCamRightLoc, _bbCamUpLoc;
        private readonly int _bbAtlasLoc, _bbAtlasTilesLoc;
        private readonly int _bbViewPosLoc, _bbSunDirLoc;
        private readonly int _bbLightColorLoc, _bbFogColorLoc, _bbUseFogLoc;
        private readonly int _bbDebugOpacityLoc;

        // ── GL.ReadPixels uses float* for pixel data ───────────────────────
        // We'll pack our byte data and cast via fixed pointer

        // ── Baking shader ──────────────────────────────────────────────────
        private readonly uint _bakeShader;
        private readonly int _bakeModelLoc, _bakeViewLoc, _bakeProjLoc;
        private readonly int _bakeSunDirLoc, _bakeRealSunDirLoc, _bakeLightColorLoc;
        private readonly int _bakeViewPosLoc;
        private readonly int _bakeBaseColorLoc, _bakeUseAlbedoLoc, _bakeAlbedoMapLoc;
        private readonly int _bakeMetallicFactorLoc, _bakeRoughnessFactorLoc;
        private readonly int _bakeEmissiveFactorLoc, _bakeNormalScaleLoc;
        private readonly int _bakeOcclusionStrengthLoc;
        private readonly int _bakeHasNormalTexLoc, _bakeHasMetallicRoughnessTexLoc;
        private readonly int _bakeHasOcclusionTexLoc, _bakeHasEmissiveTexLoc;
        private readonly int _bakeUseFogLoc;

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

        // ── LOD fade configuration ─────────────────────────────────────────
        private static float LOD2_Dist =>
            DarkEngine3D_gl_csharp.Engine.Config.LODConfig.ObjectLOD2_Distance;
        private const float FadeRange = 30f;

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

            _bakeShader = ShaderHelpers.LoadShader(
                "Artifacts/shaders/static_vertex.glsl",
                "Artifacts/shaders/gltf_fragment.glsl"
            );

            _bakeModelLoc = GL.GetUniformLocation(_bakeShader, "model");
            _bakeViewLoc = GL.GetUniformLocation(_bakeShader, "view");
            _bakeProjLoc = GL.GetUniformLocation(_bakeShader, "projection");
            _bakeSunDirLoc = GL.GetUniformLocation(_bakeShader, "sunDir");
            _bakeRealSunDirLoc = GL.GetUniformLocation(_bakeShader, "realSunDir");
            _bakeLightColorLoc = GL.GetUniformLocation(_bakeShader, "lightColor");
            _bakeViewPosLoc = GL.GetUniformLocation(_bakeShader, "viewPos");
            _bakeBaseColorLoc = GL.GetUniformLocation(_bakeShader, "baseColorFactor");
            _bakeUseAlbedoLoc = GL.GetUniformLocation(_bakeShader, "useAlbedo");
            _bakeAlbedoMapLoc = GL.GetUniformLocation(_bakeShader, "albedoMap");
            _bakeMetallicFactorLoc = GL.GetUniformLocation(_bakeShader, "metallicFactor");
            _bakeRoughnessFactorLoc = GL.GetUniformLocation(_bakeShader, "roughnessFactor");
            _bakeEmissiveFactorLoc = GL.GetUniformLocation(_bakeShader, "emissiveFactor");
            _bakeNormalScaleLoc = GL.GetUniformLocation(_bakeShader, "normalScale");
            _bakeOcclusionStrengthLoc = GL.GetUniformLocation(_bakeShader, "occlusionStrength");
            _bakeHasNormalTexLoc = GL.GetUniformLocation(_bakeShader, "hasNormalTexture");
            _bakeHasMetallicRoughnessTexLoc = GL.GetUniformLocation(_bakeShader, "hasMetallicRoughnessTexture");
            _bakeHasOcclusionTexLoc = GL.GetUniformLocation(_bakeShader, "hasOcclusionTexture");
            _bakeHasEmissiveTexLoc = GL.GetUniformLocation(_bakeShader, "hasEmissiveTexture");
            _bakeUseFogLoc = GL.GetUniformLocation(_bakeShader, "useFog");

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
            GL.RenderbufferStorage(Const.GL_RENDERBUFFER, (int)Const.GL_RGBA, ViewSize, ViewSize);
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

            // Use the coarsest available mesh for faster baking
            int bakeLOD = group.Lods.Keys.Max();
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

            // Allocate atlas pixel buffer
            byte[] atlasPixels = new byte[AtlasW * AtlasH * 4];
            Array.Fill<byte>(atlasPixels, 0);

            // Save current GL state so we can restore it
            _savedViewX = 0; _savedViewY = 0;
            _savedViewW = Glfw.WindowWidth;
            _savedViewH = Glfw.WindowHeight;
            // Note: no GL.GetIntegerv available in this wrapper — we use Glfw window dimensions
            // FBO binding: we save it via a different approach — just store it before change
            _savedFBO = 0; // We'll restore to screen (FBO 0)

            GL.Viewport(0, 0, ViewSize, ViewSize);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _bakeFBO);

            float orthoSize = MathF.Max(maxExtent * 0.7f, 0.01f);
            float nearP = -orthoSize * 2f;
            float farP = orthoSize * 2f;
            Matrix4x4 bakeProj = Matrix4x4.CreateOrthographic(orthoSize * 2f, orthoSize * 2f, nearP, farP);

            Vector3 bakeSunDir = Vector3.Normalize(new Vector3(0.707f, 0.707f, 0.0f));
            Vector3 bakeLightColor = new(0.95f, 0.93f, 0.88f);

            GL.Enable(Const.GL_DEPTH_TEST);
            GL.Enable(Const.GL_CULL_FACE);
            GL.CullFace(Const.GL_BACK);
            GL.FrontFace(Const.GL_CCW);

            GL.UseProgram(_bakeShader);

            // Set fixed bake uniforms
            GL.Uniform3f(_bakeSunDirLoc, bakeSunDir.X, bakeSunDir.Y, bakeSunDir.Z);
            if (_bakeRealSunDirLoc != -1)
                GL.Uniform3f(_bakeRealSunDirLoc, bakeSunDir.X, bakeSunDir.Y, bakeSunDir.Z);
            GL.Uniform3f(_bakeLightColorLoc, bakeLightColor.X, bakeLightColor.Y, bakeLightColor.Z);
            if (_bakeUseFogLoc != -1) GL.Uniform1i(_bakeUseFogLoc, 0);
            if (_bakeMetallicFactorLoc != -1) GL.Uniform1f(_bakeMetallicFactorLoc, 0.0f);
            if (_bakeRoughnessFactorLoc != -1) GL.Uniform1f(_bakeRoughnessFactorLoc, 1.0f);
            if (_bakeNormalScaleLoc != -1) GL.Uniform1f(_bakeNormalScaleLoc, 1.0f);
            if (_bakeOcclusionStrengthLoc != -1) GL.Uniform1f(_bakeOcclusionStrengthLoc, 1.0f);
            if (_bakeEmissiveFactorLoc != -1) GL.Uniform3f(_bakeEmissiveFactorLoc, 0f, 0f, 0f);

            // Upload ortho projection
            GL.UniformMatrix4fv(_bakeProjLoc, 1, false, &bakeProj.M11);

            for (int vi = 0; vi < NumViews; vi++)
            {
                float angleRad = vi * (MathF.PI * 2f / NumViews);
                float camDist = orthoSize * 2f;

                Vector3 camPos = new(
                    centerOffset.X + camDist * MathF.Sin(angleRad),
                    centerOffset.Y,
                    centerOffset.Z + camDist * MathF.Cos(angleRad)
                );

                Matrix4x4 bakeView = Matrix4x4.CreateLookAt(camPos, centerOffset, Vector3.UnitY);

                GL.ClearColor(0f, 0f, 0f, 0f);
                GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);

                GL.UniformMatrix4fv(_bakeViewLoc, 1, false, &bakeView.M11);
                GL.Uniform3f(_bakeViewPosLoc, camPos.X, camPos.Y, camPos.Z);

                // Render each mesh in the group
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

                    GL.UniformMatrix4fv(_bakeModelLoc, 1, false, &nodeMat.M11);

                    // Base color
                    GL.Uniform4f(_bakeBaseColorLoc, mesh.Material.BaseColorFactor.X,
                        mesh.Material.BaseColorFactor.Y, mesh.Material.BaseColorFactor.Z,
                        mesh.Material.BaseColorFactor.W);

                    if (mesh.Material.HasBaseColorTexture && mesh.Material.BaseColorTextureID != 0)
                    {
                        GL.ActiveTexture(Const.GL_TEXTURE0);
                        GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.BaseColorTextureID);
                        if (_bakeUseAlbedoLoc != -1) { GL.Uniform1i(_bakeUseAlbedoLoc, 1); }
                        if (_bakeAlbedoMapLoc != -1) { GL.Uniform1i(_bakeAlbedoMapLoc, 0); }
                    }
                    else
                    {
                        if (_bakeUseAlbedoLoc != -1) GL.Uniform1i(_bakeUseAlbedoLoc, 0);
                    }

                    // PBR flags
                    if (_bakeHasNormalTexLoc != -1)
                        GL.Uniform1i(_bakeHasNormalTexLoc, mesh.Material.HasNormalTexture ? 1 : 0);
                    if (_bakeHasMetallicRoughnessTexLoc != -1)
                        GL.Uniform1i(_bakeHasMetallicRoughnessTexLoc, mesh.Material.HasMetallicRoughnessTexture ? 1 : 0);
                    if (_bakeHasOcclusionTexLoc != -1)
                        GL.Uniform1i(_bakeHasOcclusionTexLoc, mesh.Material.HasOcclusionTexture ? 1 : 0);
                    if (_bakeHasEmissiveTexLoc != -1)
                        GL.Uniform1i(_bakeHasEmissiveTexLoc, mesh.Material.HasEmissiveTexture ? 1 : 0);

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

                // Read pixels for this tile into atlas at (tileX, 0)
                // GL.ReadPixels in this wrapper takes float* — we cast via void* intermediate
                int tileX = vi * ViewSize;
                fixed (byte* pRow = &atlasPixels[tileX * 4])
                {
                    GL.ReadPixels(0, 0, ViewSize, ViewSize, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (float*)pRow);
                }
            }

            // ── Upload atlas as OpenGL texture ──
            uint atlasTexture;
            GL.GenTextures(1, &atlasTexture);
            GL.BindTexture(Const.GL_TEXTURE_2D, atlasTexture);

            // Flip vertically (OpenGL reads bottom-up)
            byte[] flippedAtlas = new byte[AtlasW * AtlasH * 4];
            int rowBytes = AtlasW * 4;
            for (int row = 0; row < AtlasH; row++)
            {
                int srcRow = (AtlasH - 1 - row) * rowBytes;
                int dstRow = row * rowBytes;
                Buffer.BlockCopy(atlasPixels, srcRow, flippedAtlas, dstRow, rowBytes);
            }

            fixed (byte* pData = flippedAtlas)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                    AtlasW, AtlasH, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, pData);
            }

            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            // Store on group
            group.BillboardAtlasTexture = atlasTexture;
            group.BillboardRadius = radius;

            // Track for cleanup
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
        //  Per-frame: collect and render billboard instances
        // ───────────────────────────────────────────────────────────────────

        public void Collect(StaticObject obj, float opacity)
        {
            if (obj.Group == null || !obj.Group.HasBillboard) return;
            _instances.Add((obj, Math.Clamp(opacity, 0f, 1f)));
        }

        public void Flush(Camera camera, Lights light)
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

            GL.Uniform2f(_bbAtlasTilesLoc, NumViews, 1);
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
            float fadeStart = LOD2_Dist;
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
            GL.DeleteProgram(_bakeShader);
        }
    }
}
