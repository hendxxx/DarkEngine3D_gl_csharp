using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Libs;
using System;

namespace DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing
{

    public unsafe class PostProcessStack
    {
        // ── MSAA scene target: the scene renders into SceneFBO (multisampled).
        // After rendering, Resolve() blits it into the single-sample SceneColorTex,
        // which the post-process passes and the IDE Viewport panel sample. ──
        public uint SceneFBO;
        public uint SceneColorTex;
        public uint SceneDepthRBO;

        private uint _msaaColorRBO = 0;
        private uint _resolveFBO = 0;
        private int _samples;

        // PostFX removed.

        private int _width, _height;
        private uint _simpleVAO, _simpleVBO;

        private readonly List<IPostProcessPass> _passes = new();
#pragma warning disable CS0414
        private int _ppDebugCount = 0;

        public PostProcessStack(int width, int height)
        {
            _width = width;
            _height = height;

            // MSAA sample count from the unified quality preset, clamped to the driver's max.
            UpdateSamples();

            CreateSceneFBO();
        }

        /// <summary>Pull the requested MSAA sample count from <see cref="Config.QualitySettings"/>
        /// and clamp it to what the driver actually supports (1 = MSAA off).</summary>
        private void UpdateSamples()
        {
            _samples = Math.Max(1, Config.QualitySettings.MsaaSamples);
            int maxSamples = 0;
            GL.GetIntegerv(Const.GL_MAX_SAMPLES, &maxSamples);
            if (maxSamples > 0) _samples = Math.Min(_samples, maxSamples);
        }

        void CreateSceneFBO()
        {
            uint fbo = 0, color = 0, rbo = 0, resolveFbo = 0;

            GL.GenFramebuffers(1, &fbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, fbo);

            // Multisampled color attachment (renderbuffer) + multisampled depth-stencil.
            GL.GenRenderbuffers(1, &rbo);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, rbo);
            bool msaaOk = GL.RenderbufferStorageMultisample(Const.GL_RENDERBUFFER, _samples, Const.GL_RGBA8, _width, _height);
            GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                       Const.GL_RENDERBUFFER, rbo);

            uint depthRbo = 0;
            GL.GenRenderbuffers(1, &depthRbo);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, depthRbo);
            GL.RenderbufferStorageMultisample(Const.GL_RENDERBUFFER, _samples, Const.GL_DEPTH24_STENCIL8, _width, _height);
            GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_STENCIL_ATTACHMENT,
                                       Const.GL_RENDERBUFFER, depthRbo);

            uint msaaStatus = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            if (msaaStatus != Const.GL_FRAMEBUFFER_COMPLETE)
                Console.WriteLine($"PostProcessStack MSAA FBO incomplete: 0x{msaaStatus:X} (samples={_samples})");

            // Single-sample resolve target — the texture everything downstream samples.
            GL.GenTextures(1, &color);
            GL.BindTexture(Const.GL_TEXTURE_2D, color);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA8,
                          _width, _height, 0,
                          Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);

            GL.GenFramebuffers(1, &resolveFbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, resolveFbo);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, color, 0);

            // MSAA unavailable or rejected → fall back to a plain single-sample target
            // (render straight into the resolve FBO; the code path stays identical).
            bool msaaUsable = msaaOk && _samples > 1 && msaaStatus == Const.GL_FRAMEBUFFER_COMPLETE;
            if (!msaaUsable)
            {
                // Reallocate the existing depth renderbuffer as a plain single-sample one
                // (RenderbufferStorageMultisample was a no-op, so it has no storage yet).
                GL.BindRenderbuffer(Const.GL_RENDERBUFFER, depthRbo);
                GL.RenderbufferStorage(Const.GL_RENDERBUFFER, Const.GL_DEPTH24_STENCIL8, _width, _height);
                GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_STENCIL_ATTACHMENT,
                                           Const.GL_RENDERBUFFER, depthRbo);
            }

            uint resolveStatus = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            if (resolveStatus != Const.GL_FRAMEBUFFER_COMPLETE)
                Console.WriteLine($"PostProcessStack resolve FBO incomplete: 0x{resolveStatus:X}");

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);

            SceneFBO = msaaUsable ? fbo : resolveFbo;
            SceneColorTex = color;
            SceneDepthRBO = depthRbo;
            _msaaColorRBO = rbo;
            _resolveFBO = resolveFbo;

            if (!msaaUsable)
                Console.WriteLine($"[PostProcessStack] MSAA {_samples}x unavailable — falling back to single-sample ({_width}x{_height})");
        }

        /// <summary>Recreate the scene FBOs at a new size (window resize). Keeps the
        /// registered post-process passes untouched.</summary>
        public void Resize(int width, int height)
        {
            bool samplesChanged = _samples != Math.Max(1, Config.QualitySettings.MsaaSamples);
            if (width == _width && height == _height && !samplesChanged) return;
            _width = width;
            _height = height;
            UpdateSamples();
            DestroySceneFBO();
            CreateSceneFBO();
        }

        /// <summary>Recreate the scene FBOs with the current quality-preset MSAA sample
        /// count. Call after <see cref="Config.QualitySettings.Apply(int)"/> so a live
        /// quality change takes effect immediately.</summary>
        public void ApplyQuality()
        {
            Resize(_width, _height);
        }

        private void DestroySceneFBO()
        {
            if (SceneFBO != 0) { fixed (uint* p = &SceneFBO) GL.DeleteFramebuffers(1, p); SceneFBO = 0; }
            if (_resolveFBO != 0) { fixed (uint* p = &_resolveFBO) GL.DeleteFramebuffers(1, p); _resolveFBO = 0; }
            if (SceneColorTex != 0) { fixed (uint* p = &SceneColorTex) GL.DeleteTextures(1, p); SceneColorTex = 0; }
            if (_msaaColorRBO != 0) { fixed (uint* p = &_msaaColorRBO) GL.DeleteRenderbuffers(1, p); _msaaColorRBO = 0; }
            if (SceneDepthRBO != 0) { fixed (uint* p = &SceneDepthRBO) GL.DeleteRenderbuffers(1, p); SceneDepthRBO = 0; }
        }

        /// <summary>Resolve the multisampled scene FBO into the single-sample SceneColorTex
        /// that the post-process and Viewport panel sample. Call after the scene has been
        /// rendered into SceneFBO and before reading SceneColorTex.</summary>
        public void Resolve()
        {
            if (_resolveFBO == 0 || SceneFBO == _resolveFBO) return;

            GL.BindFramebuffer(Const.GL_READ_FRAMEBUFFER, SceneFBO);
            GL.BindFramebuffer(Const.GL_DRAW_FRAMEBUFFER, _resolveFBO);
            GL.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height,
                               Const.GL_COLOR_BUFFER_BIT, Const.GL_NEAREST);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
        }

        public void AddPass(IPostProcessPass pass)
        {
            pass.Init();
            _passes.Add(pass);
        }

        // dipanggil sebelum render scene — always binds to offscreen FBO
        public void BindSceneFBO()
        {
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, SceneFBO);
            GL.Viewport(0, 0, _width, _height);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthMask(true);
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
        }

        // dipanggil setelah scene selesai dirender
        public void RunStack(int windowWidth, int windowHeight, float time)
        {
            // MSAA resolve: SceneFBO (multisampled) → SceneColorTex (single-sample).
            Resolve();

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, windowWidth, windowHeight);
            GL.Disable(Const.GL_DEPTH_TEST);

            // AAA post-FX chain (bloom → ACES tonemap → gamma). When enabled it replaces
            // PostFX removed.

            // If no passes, just render the scene texture as-is
            if (_passes.Count == 0)
            {
                RenderTextureToScreen(SceneColorTex, Shader.GetBlurPassShaderProgram(), windowWidth, windowHeight, 0f);
                GL.Enable(Const.GL_DEPTH_TEST);
                return;
            }

            uint currentTex = SceneColorTex;

            foreach (var pass in _passes)
            {
                // untuk sekarang: semua pass langsung render ke default framebuffer
                // kalau mau bener2 multi-buffer, nanti kita tambahin ping-pong FBO
                
                pass.Execute(currentTex, windowWidth, windowHeight, time);
 
            }

            GL.Enable(Const.GL_DEPTH_TEST); 
        }

        /// <summary>Render the scene color texture to the screen using the blur shader.
        /// Called instead of the normal passthrough when the pause menu is active.</summary>
        public void RenderBlurred(int windowWidth, int windowHeight, float blurRadius = 3f, float blurStrength = 1.0f)
        {
            // MSAA resolve first so the blur samples the resolved frame.
            Resolve();

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, windowWidth, windowHeight);
            GL.Disable(Const.GL_DEPTH_TEST);

            RenderTextureToScreen(SceneColorTex, Shader.GetBlurPassShaderProgram(), windowWidth, windowHeight, blurRadius, blurStrength);

            GL.Enable(Const.GL_DEPTH_TEST);
        }

        /// <summary>Render a texture to the full screen using the given shader.
        /// Creates a simple full-screen quad if not already created.</summary>
        private void RenderTextureToScreen(uint texture, uint shader, int w, int h,
                                            float blurRadius = 0f, float blurStrength = 1.0f)
        {
            if (_simpleVAO == 0)
                CreateSimpleQuad();

            GL.UseProgram(shader);
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, texture);
            GL.Uniform1i(GL.GetUniformLocation(shader, "sceneTex"), 0);

            // Set texel size for sampling offsets
            int texelLoc = GL.GetUniformLocation(shader, "texelSize");
            if (texelLoc >= 0)
                GL.Uniform2f(texelLoc, 1f / w, 1f / h);

            // Set blur parameters
            int radiusLoc = GL.GetUniformLocation(shader, "blurRadius");
            if (radiusLoc >= 0)
                GL.Uniform1f(radiusLoc, blurRadius);

            int strengthLoc = GL.GetUniformLocation(shader, "blurStrength");
            if (strengthLoc >= 0)
                GL.Uniform1f(strengthLoc, blurStrength);

            GL.BindVertexArray(_simpleVAO);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            GL.BindVertexArray(0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        }

        private void CreateSimpleQuad()
        {
            // Full-screen quad with CCW winding (FrontFace = GL_CCW):
            //   Tri 1: bottom-left → bottom-right → top-right
            //   Tri 2: bottom-left → top-right → top-left
            // NOTE: FBO texture (SceneColorTex) uses OpenGL convention where origin
            // is BOTTOM-LEFT (uv 0,0). So we flip UV.y: bottom = 0, top = 1.
            float[] quad =
            [
                // Triangle 1: BL → BR → TR  (CCW)
                -1f, -1f,  0f, 0f,    // bottom-left:  uv(0,0)
                 1f, -1f,  1f, 0f,    // bottom-right: uv(1,0)
                 1f,  1f,  1f, 1f,    // top-right:    uv(1,1)

                // Triangle 2: BL → TR → TL  (CCW)
                -1f, -1f,  0f, 0f,    // bottom-left:  uv(0,0)
                 1f,  1f,  1f, 1f,    // top-right:    uv(1,1)
                -1f,  1f,  0f, 1f,    // top-left:     uv(0,1)
            ];

            uint vao = 0, vbo = 0;
            GL.GenVertexArrays(1, &vao);
            GL.GenBuffers(1, &vbo);

            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

            fixed (float* v = quad)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(quad.Length * sizeof(float)), v, Const.GL_STATIC_DRAW);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));

            GL.BindVertexArray(0);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, 0);

            _simpleVAO = vao;
            _simpleVBO = vbo;
        }
    }
}
