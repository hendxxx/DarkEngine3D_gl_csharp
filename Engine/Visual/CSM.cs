using DarkEngine3D_gl_csharp.Engine.Libs;
using System;
using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Terrains;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    public unsafe class CSM : IDisposable
    {
        public const int NumCascades = 3;
        private readonly int[] CascadeSizes = Config.ShadowConfig.CascadeSizes;
        
        // EVSM warp constant: higher = less light bleeding, but more precision issues
        public const float EVSM_Warp = 30.0f;

        public int ShadowSize { get; private set; }

        // ── EVSM Render FBOs (depth renderbuffer + RGBA32F color texture) ──
        public uint[] FBOs = new uint[NumCascades];
        public uint[] EVSMTextures = new uint[NumCascades];   // R=exp(c*z), G=exp(2c*z), B=z, A=1.0
        public uint[] DepthRBOs = new uint[NumCascades];       // depth renderbuffer for depth test

        // ── EVSM Blur Resources ──
        public uint[] BlurFBOs = new uint[NumCascades];            // temporary FBO for blur passes
        public uint[] BlurTempTextures = new uint[NumCascades];    // temp textures for ping-pong blur

        // ── Existing shadow mapping data ──
        public Matrix4x4[] LightSpaceMatrices = new Matrix4x4[NumCascades];
        public Vector3[][] OrthoCorners = new Vector3[NumCascades][];

        public float[] CascadeEnds = Config.ShadowConfig.CascadeLayer;

        // ── Blur Shader Programs ──
        private static uint _blurHProgram = 0;
        private static uint _blurVProgram = 0;
        private static bool _blurShadersLoaded = false;
        private static uint _blurVAO = 0; // shared attributeless VAO for blur

        public CSM(int shadowSize = 1024)
        {
            ShadowSize = shadowSize;
            LoadBlurShaders();
            CreateEVSMResources();
        }

        private static void LoadBlurShaders()
        {
            if (_blurShadersLoaded) return;
            _blurHProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/evsm_blur_vertex.glsl",
                "Artifacts/shaders/evsm_blur_h_fragment.glsl");
            _blurVProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/evsm_blur_vertex.glsl",
                "Artifacts/shaders/evsm_blur_v_fragment.glsl");

            // Create a single VAO for attributeless rendering
            fixed (uint* pVao = &_blurVAO)
                GL.GenVertexArrays(1, pVao);

            _blurShadersLoaded = true;
        }

        private unsafe void CreateEVSMResources()
        {
            // Generate render FBOs
            fixed (uint* pFbos = FBOs)
                GL.GenFramebuffers(NumCascades, pFbos);

            // Generate blur FBOs
            fixed (uint* pBlurFbos = BlurFBOs)
                GL.GenFramebuffers(NumCascades, pBlurFbos);

            // Generate all textures and RBOs
            fixed (uint* pTexs = EVSMTextures)
                GL.GenTextures(NumCascades, pTexs);

            fixed (uint* pTemp = BlurTempTextures)
                GL.GenTextures(NumCascades, pTemp);

            fixed (uint* pRbos = DepthRBOs)
                GL.GenRenderbuffers(NumCascades, pRbos);

            for (int i = 0; i < NumCascades; i++)
            {
                int size = CascadeSizes[i];

                // ── EVSM Color Texture (RGBA32F) ──
                GL.BindTexture(Const.GL_TEXTURE_2D, EVSMTextures[i]);
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA32F,
                              size, size, 0,
                              Const.GL_RGBA, Const.GL_FLOAT, (void*)0);

                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_BORDER);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_BORDER);

                float[] borderColor = { 1f, 1f, 1f, 1f };
                fixed (float* pBorder = borderColor)
                    GL.TexParameterfv(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_BORDER_COLOR, pBorder);

                // ── Blur Temp Texture (same format) ──
                GL.BindTexture(Const.GL_TEXTURE_2D, BlurTempTextures[i]);
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA32F,
                              size, size, 0,
                              Const.GL_RGBA, Const.GL_FLOAT, (void*)0);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_BORDER);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_BORDER);
                fixed (float* pBorder = borderColor)
                    GL.TexParameterfv(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_BORDER_COLOR, pBorder);

                // ── Depth Renderbuffer ──
                GL.BindRenderbuffer(Const.GL_RENDERBUFFER, DepthRBOs[i]);
                GL.RenderbufferStorage(Const.GL_RENDERBUFFER, (int)Const.GL_DEPTH_COMPONENT24, size, size);

                // ── Main Render FBO: depth RBO + EVSM color attachment ──
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, FBOs[i]);
                GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                        Const.GL_TEXTURE_2D, EVSMTextures[i], 0);
                GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_ATTACHMENT,
                                           Const.GL_RENDERBUFFER, DepthRBOs[i]);

                // Tell OpenGL we're writing to color attachment 0 (not depth anymore)
                uint colorAttach = Const.GL_COLOR_ATTACHMENT0;
                GL.DrawBuffers(1, &colorAttach);

                uint status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
                if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                    Console.WriteLine($"[CSM] Cascade {i} render FBO incomplete: 0x{status:X}");

                // ── Blur FBO: attached to BlurTempTexture[i] ──
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, BlurFBOs[i]);
                GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                        Const.GL_TEXTURE_2D, BlurTempTextures[i], 0);
                GL.DrawBuffers(1, &colorAttach);

                status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
                if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                    Console.WriteLine($"[CSM] Cascade {i} blur FBO incomplete: 0x{status:X}");
            }

            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
        }

        private static Matrix4x4 CreateOrthographicOffCenterOpenGL(
            float left, float right, float bottom, float top, float zNear, float zFar)
        {
            Matrix4x4 m = Matrix4x4.Identity;
            m.M11 = 2f / (right - left);
            m.M22 = 2f / (top - bottom);
            m.M33 = -2f / (zFar - zNear);
            m.M41 = -(right + left) / (right - left);
            m.M42 = -(top + bottom) / (top - bottom);
            m.M43 = -(zFar + zNear) / (zFar - zNear);
            return m;
        }

        public void UpdateMatrices(Camera camera, Vector3 lightDir)
        {
            lightDir = Vector3.Normalize(lightDir);

            CascadeEnds[NumCascades - 1] = camera.FarDist;

            float prevSplit = camera.NearDist;
            Matrix4x4 cameraView = camera.GetViewMatrix();
            float fovRad = Helpers.OGLMath.ToRadians(camera.FoV);

            for (int i = 0; i < NumCascades; i++)
            {
                float nextSplit = CascadeEnds[i];

                Matrix4x4 splitProj = Matrix4x4.CreatePerspectiveFieldOfView(
                    fovRad,
                    camera.GetAspect(),
                    prevSplit,
                    nextSplit);

                Vector3[] corners = TerrainChunk.GetFrustumCorners(cameraView, splitProj);

                Vector3 center = Vector3.Zero;
                for (int j = 0; j < 8; j++)
                    center += corners[j];
                center /= 8f;

                float radius = 0f;
                for (int j = 0; j < 8; j++)
                    radius = MathF.Max(radius, Vector3.Distance(corners[j], center));

                radius = MathF.Ceiling(radius * 32f) / 32f;

                Vector3 up = MathF.Abs(Vector3.Dot(lightDir, Vector3.UnitY)) > 0.99f
                    ? Vector3.UnitZ
                    : Vector3.UnitY;

                Vector3 lightPos = center + lightDir * radius;
                Matrix4x4 lightView = Matrix4x4.CreateLookAt(lightPos, center, up);

                // Transform frustum corners ke light space
                Vector3[] lsCorners = new Vector3[8];
                for (int j = 0; j < 8; j++)
                    lsCorners[j] = Vector3.Transform(corners[j], lightView);

                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;

                for (int j = 0; j < 8; j++)
                {
                    Vector3 lp = lsCorners[j];
                    minX = MathF.Min(minX, lp.X);
                    maxX = MathF.Max(maxX, lp.X);
                    minY = MathF.Min(minY, lp.Y);
                    maxY = MathF.Max(maxY, lp.Y);
                    minZ = MathF.Min(minZ, lp.Z);
                    maxZ = MathF.Max(maxZ, lp.Z);
                }

                float padXY = MathF.Max(radius * 0.25f, 5.0f);
                minX -= padXY;
                maxX += padXY;
                minY -= padXY;
                maxY += padXY;

                float padZFront = radius * 2.0f;
                float padZBack = radius * 4.0f;
                float zNear = minZ - padZBack;
                float zFar = maxZ + padZFront;

                if (zNear > zFar - 1.0f) zNear = zFar - 1.0f;

                // TEXEL SNAPPING
                float width = maxX - minX;
                float height = maxY - minY;

                float worldUnitsPerTexelX = width / CascadeSizes[i];
                float worldUnitsPerTexelY = height / CascadeSizes[i];

                if (worldUnitsPerTexelX > 0.0f)
                {
                    minX = MathF.Floor(minX / worldUnitsPerTexelX) * worldUnitsPerTexelX;
                    maxX = MathF.Floor(maxX / worldUnitsPerTexelX) * worldUnitsPerTexelX;
                }

                if (worldUnitsPerTexelY > 0.0f)
                {
                    minY = MathF.Floor(minY / worldUnitsPerTexelY) * worldUnitsPerTexelY;
                    maxY = MathF.Floor(maxY / worldUnitsPerTexelY) * worldUnitsPerTexelY;
                }

                // Simpan corners ortho di world space (buat debug / culling)
                Vector3[] ls =
                [
                    new(minX, minY, zNear),
                    new(maxX, minY, zNear),
                    new(maxX, maxY, zNear),
                    new(minX, maxY, zNear),

                    new(minX, minY, zFar),
                    new(maxX, minY, zFar),
                    new(maxX, maxY, zFar),
                    new(minX, maxY, zFar),
                ];

                Matrix4x4.Invert(lightView, out Matrix4x4 invView);
                for (int k = 0; k < 8; k++)
                    ls[k] = Vector3.Transform(ls[k], invView);

                OrthoCorners[i] = ls;

                Matrix4x4 lightProj = CreateOrthographicOffCenterOpenGL(
                    minX, maxX, minY, maxY, zNear, zFar);

                LightSpaceMatrices[i] = lightView * lightProj;

                prevSplit = nextSplit;
            }
        }

        public static Plane[]? BuildPlanesFromCorners(Vector3[] c)
        {
            if (c == null || c.Length < 8) return null;

            var planes = new Plane[6];

            static Plane MakePlane(Vector3 a, Vector3 b, Vector3 d, Vector3 insidePoint)
            {
                var n = Vector3.Normalize(Vector3.Cross(b - a, d - a));
                float D = -Vector3.Dot(n, a);

                Plane p = new Plane(n, D);

                float dist = Vector3.Dot(p.Normal, insidePoint) + p.D;
                if (dist < 0)
                {
                    p.Normal = -p.Normal;
                    p.D = -p.D;
                }

                return Plane.Normalize(p);
            }

            Vector3 center = (c[0] + c[6]) * 0.5f;

            planes[0] = MakePlane(c[1], c[2], c[6], center);
            planes[1] = MakePlane(c[3], c[0], c[4], center);
            planes[2] = MakePlane(c[0], c[1], c[5], center);
            planes[3] = MakePlane(c[2], c[3], c[7], center);
            planes[4] = MakePlane(c[0], c[3], c[2], center);
            planes[5] = MakePlane(c[5], c[6], c[7], center);

            return planes;
        }

        /// <summary>
        /// Bind the EVSM render FBO for cascade i.
        /// Shadow geometry will write depth to the RBO and EVSM values to the color texture.
        /// </summary>
        public void BindForEVSMWrite(int index)
        {
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, FBOs[index]);
            GL.Viewport(0, 0, CascadeSizes[index], CascadeSizes[index]);

            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthMask(true);

            GL.Enable(Const.GL_CULL_FACE);
            GL.CullFace(Const.GL_FRONT);
            GL.FrontFace(Const.GL_CCW);

            GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
        }

        /// <summary>
        /// Legacy: used by existing code. Calls BindForEVSMWrite internally.
        /// </summary>
        public void BindFramebuffer(int index)
        {
            BindForEVSMWrite(index);
        }

        /// <summary>
        /// Blur all EVSM textures using separable Gaussian blur (H+V) for smooth shadows.
        /// </summary>
        public unsafe void BlurEVSM()
        {
            GL.Disable(Const.GL_DEPTH_TEST);
            GL.DepthMask(false);
            GL.BindVertexArray(_blurVAO);

            for (int i = 0; i < NumCascades; i++)
            {
                int size = CascadeSizes[i];

                // ── Pass 1: Horizontal blur (EVSM → Temp) ──
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, BlurFBOs[i]);
                GL.Viewport(0, 0, size, size);

                GL.UseProgram(_blurHProgram);
                GL.Uniform1i(GL.GetUniformLocation(_blurHProgram, "evsmTex"), 0);
                GL.Uniform2f(GL.GetUniformLocation(_blurHProgram, "texelSize"), 1.0f / size, 1.0f / size);

                GL.ActiveTexture(Const.GL_TEXTURE0);
                GL.BindTexture(Const.GL_TEXTURE_2D, EVSMTextures[i]);

                GL.DrawArrays(Const.GL_TRIANGLES, 0, 3);

                // ── Pass 2: Vertical blur (Temp → EVSM) ──
                // Re-attach EVSMTextures[i] to the blur FBO
                uint colorAttach = Const.GL_COLOR_ATTACHMENT0;
                GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                        Const.GL_TEXTURE_2D, EVSMTextures[i], 0);
                GL.DrawBuffers(1, &colorAttach);

                GL.UseProgram(_blurVProgram);
                GL.Uniform1i(GL.GetUniformLocation(_blurVProgram, "evsmTex"), 0);
                GL.Uniform2f(GL.GetUniformLocation(_blurVProgram, "texelSize"), 1.0f / size, 1.0f / size);

                GL.ActiveTexture(Const.GL_TEXTURE0);
                GL.BindTexture(Const.GL_TEXTURE_2D, BlurTempTextures[i]);

                GL.DrawArrays(Const.GL_TRIANGLES, 0, 3);

                // Restore BlurFBO attachment to BlurTempTextures[i] for next frame
                GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                        Const.GL_TEXTURE_2D, BlurTempTextures[i], 0);
                GL.DrawBuffers(1, &colorAttach);
            }

            GL.BindVertexArray(0);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthMask(true);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
        }

        public void UnbindFramebuffer(int viewportWidth, int viewportHeight)
        {
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, viewportWidth, viewportHeight);

            GL.CullFace(Const.GL_BACK);
        }

        public unsafe void Dispose()
        {
            fixed (uint* pFbos = FBOs)
                GL.DeleteFramebuffers(NumCascades, pFbos);

            fixed (uint* pTexs = EVSMTextures)
                GL.DeleteTextures(NumCascades, pTexs);

            fixed (uint* pTemp = BlurTempTextures)
                GL.DeleteTextures(NumCascades, pTemp);

            fixed (uint* pRbos = DepthRBOs)
                GL.DeleteRenderbuffers(NumCascades, pRbos);

            fixed (uint* pBlurFbos = BlurFBOs)
                GL.DeleteFramebuffers(NumCascades, pBlurFbos);

            if (_blurVAO != 0)
            {
                fixed (uint* pVao = &_blurVAO)
                    GL.DeleteVertexArrays(1, pVao);
                _blurVAO = 0;
            }
        }
    }
}
