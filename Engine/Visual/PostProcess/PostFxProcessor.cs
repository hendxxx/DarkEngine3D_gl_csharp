using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Libs;
using System;

namespace DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing
{
    /// <summary>
    /// AAA post-FX chain: bloom + ACES tonemapping + gamma correction.
    /// Uses a dedicated luminance texture for auto-exposure so the viewport
    /// texture never needs mipmap-compatible filters.
    /// </summary>
    public unsafe class PostFxProcessor
    {
        // ── Bloom ping-pong FBOs (half res) ──
        private uint _bloomFBO_A = 0, _bloomTex_A = 0;
        private uint _bloomFBO_B = 0, _bloomTex_B = 0;
        // ── Composite FBO (full res) ──
        private uint _compositeFBO = 0, _compositeTex = 0;
        // ── Dedicated luminance texture for auto-exposure ──
        private uint _lumaFBO = 0, _lumaTex = 0;
        private int _lumaW = 0, _lumaH = 0;

        private int _w = 0, _h = 0;
        private int _bloomW = 0, _bloomH = 0;

        private uint _vao = 0, _vbo = 0;

        // ── Auto-exposure state ──
        private float _smoothedExposure = 1f;
        private readonly System.Diagnostics.Stopwatch _aeClock = new();
        private double _lastAeTime = 0;

        public PostFxProcessor(int width, int height)
        {
            Resize(width, height);
        }

        public void Resize(int width, int height)
        {
            if (width == _w && height == _h) return;
            _w = width;
            _h = height;
            DestroyTargets();

            _bloomW = Math.Max(1, width / 2);
            _bloomH = Math.Max(1, height / 2);

            (_bloomFBO_A, _bloomTex_A) = CreateColorTarget(_bloomW, _bloomH, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE);
            (_bloomFBO_B, _bloomTex_B) = CreateColorTarget(_bloomW, _bloomH, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE);
            (_compositeFBO, _compositeTex) = CreateColorTarget(width, height, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE);

            // Luminance texture: small, RGBA8, mipmapped for average luminance
            _lumaW = Math.Max(1, width / 16);
            _lumaH = Math.Max(1, height / 16);
            (_lumaFBO, _lumaTex) = CreateColorTarget(_lumaW, _lumaH, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE);
            // Enable mipmaps on the luminance texture for auto-exposure
            GL.BindTexture(Const.GL_TEXTURE_2D, _lumaTex);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR_MIPMAP_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.GenerateMipmap(Const.GL_TEXTURE_2D);
        }

        public void Run(uint inputTexture, uint outputFBO, int width, int height)
        {
            Resize(width, height);
            if (_compositeFBO == 0) return;
            if (_vao == 0) CreateQuad();

            // ── Auto-exposure ──
            float effectiveExposure = PostFxSettings.Exposure;
            if (PostFxSettings.AutoExposure)
            {
                effectiveExposure = ComputeAutoExposure(inputTexture, width, height);
                PostFxSettings.CurrentAutoExposure = effectiveExposure;
            }

            float bInvW = 1f / _bloomW;
            float bInvH = 1f / _bloomH;

            // ── 1. Bright pass → bloom A (half res) ──
            BindTarget(_bloomFBO_A, _bloomW, _bloomH);
            uint brightShader = Shader.GetPostFxBrightShaderProgram();
            GL.UseProgram(brightShader);
            BindTexUnit0(inputTexture);
            GL.Uniform1i(GL.GetUniformLocation(brightShader, "sceneTex"), 0);
            GL.Uniform1f(GL.GetUniformLocation(brightShader, "u_Threshold"), PostFxSettings.BloomThreshold);
            GL.Uniform1f(GL.GetUniformLocation(brightShader, "u_SoftKnee"), PostFxSettings.BloomSoftKnee);
            DrawQuad();

            // ── 2. Separable blur, 2 iterations (H then V, ping-pong A ⇄ B) ──
            uint blurShader = Shader.GetPostFxBlurShaderProgram();
            for (int i = 0; i < 2; i++)
            {
                // Horizontal: A → B
                BindTarget(_bloomFBO_B, _bloomW, _bloomH);
                GL.UseProgram(blurShader);
                BindTexUnit0(_bloomTex_A);
                GL.Uniform1i(GL.GetUniformLocation(blurShader, "sceneTex"), 0);
                GL.Uniform2f(GL.GetUniformLocation(blurShader, "texelSize"), bInvW, bInvH);
                GL.Uniform2f(GL.GetUniformLocation(blurShader, "u_Direction"), 1f, 0f);
                DrawQuad();

                // Vertical: B → A
                BindTarget(_bloomFBO_A, _bloomW, _bloomH);
                GL.UseProgram(blurShader);
                BindTexUnit0(_bloomTex_B);
                GL.Uniform1i(GL.GetUniformLocation(blurShader, "sceneTex"), 0);
                GL.Uniform2f(GL.GetUniformLocation(blurShader, "texelSize"), bInvW, bInvH);
                GL.Uniform2f(GL.GetUniformLocation(blurShader, "u_Direction"), 0f, 1f);
                DrawQuad();
            }

            // ── 3. Composite + tonemap + gamma → composite FBO ──
            BindTarget(_compositeFBO, _w, _h);
            uint compositeShader = Shader.GetPostFxCompositeShaderProgram();
            GL.UseProgram(compositeShader);
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, inputTexture);
            GL.Uniform1i(GL.GetUniformLocation(compositeShader, "sceneTex"), 0);
            GL.ActiveTexture(Const.GL_TEXTURE1);
            GL.BindTexture(Const.GL_TEXTURE_2D, _bloomTex_A);
            GL.Uniform1i(GL.GetUniformLocation(compositeShader, "bloomTex"), 1);
            GL.Uniform1f(GL.GetUniformLocation(compositeShader, "u_BloomIntensity"), PostFxSettings.BloomIntensity);
            GL.Uniform1f(GL.GetUniformLocation(compositeShader, "u_Exposure"), effectiveExposure);
            GL.Uniform1f(GL.GetUniformLocation(compositeShader, "u_Gamma"), PostFxSettings.Gamma);
            DrawQuad();

            // ── 4. Copy graded result into outputFBO (passthrough) ──
            BindTarget(outputFBO, _w, _h);
            uint passthrough = Shader.GetBlurPassShaderProgram();
            GL.UseProgram(passthrough);
            BindTexUnit0(_compositeTex);
            GL.Uniform1i(GL.GetUniformLocation(passthrough, "sceneTex"), 0);
            GL.Uniform2f(GL.GetUniformLocation(passthrough, "texelSize"), 1f / _w, 1f / _h);
            GL.Uniform1f(GL.GetUniformLocation(passthrough, "blurRadius"), 0f);
            GL.Uniform1f(GL.GetUniformLocation(passthrough, "blurStrength"), 1f);
            DrawQuad();

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, _w, _h);
        }

        /// <summary>
        /// Compute auto-exposure using a dedicated luminance texture.
        /// The scene is downscaled to a small texture, mipmaps generate the
        /// average, then we read back the luminance.
        /// </summary>
        private float ComputeAutoExposure(uint inputTexture, int width, int height)
        {
            // 1. Downscale scene into the dedicated luminance texture
            BindTarget(_lumaFBO, _lumaW, _lumaH);
            uint brightShader = Shader.GetPostFxBrightShaderProgram();
            GL.UseProgram(brightShader);
            BindTexUnit0(inputTexture);
            GL.Uniform1i(GL.GetUniformLocation(brightShader, "sceneTex"), 0);
            GL.Uniform1f(GL.GetUniformLocation(brightShader, "u_Threshold"), 0f);
            GL.Uniform1f(GL.GetUniformLocation(brightShader, "u_SoftKnee"), 0f);
            DrawQuad();

            // 2. Generate mipmaps on the luminance texture (has mipmap filter)
            GL.BindTexture(Const.GL_TEXTURE_2D, _lumaTex);
            GL.GenerateMipmap(Const.GL_TEXTURE_2D);

            // 3. Find the smallest mip level
            int maxDim = Math.Max(_lumaW, _lumaH);
            int maxLevel = 0;
            while ((maxDim >> maxLevel) > 1) maxLevel++;

            // 4. Attach the smallest mip to our FBO and read back
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _lumaFBO);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, _lumaTex, maxLevel);

            float[] px = new float[4];
            fixed (float* p = px)
                GL.ReadPixels(0, 0, 1, 1, Const.GL_RGBA, Const.GL_FLOAT, p);

            // Restore full-res attachment
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, _lumaTex, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            // 5. Compute exposure from average luminance
            float luma = 0.2126f * px[0] + 0.7152f * px[1] + 0.0722f * px[2];
            float target = PostFxSettings.AutoExposureTargetLuminance / Math.Max(luma, 1e-4f);
            float lo = Math.Min(PostFxSettings.AutoExposureMinExposure, PostFxSettings.AutoExposureMaxExposure);
            float hi = Math.Max(PostFxSettings.AutoExposureMinExposure, PostFxSettings.AutoExposureMaxExposure);
            target = Math.Clamp(target, lo, hi);

            // 6. Exponential smoothing
            if (!_aeClock.IsRunning) _aeClock.Start();
            double now = _aeClock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(now - _lastAeTime, 0.1);
            _lastAeTime = now;

            float t = 1f - MathF.Exp(-PostFxSettings.AutoExposureSpeed * Math.Max(dt, 0f));
            _smoothedExposure += (target - _smoothedExposure) * t;
            return _smoothedExposure;
        }

        public void Destroy()
        {
            DestroyTargets();
            if (_vao != 0) { fixed (uint* p = &_vao) GL.DeleteVertexArrays(1, p); _vao = 0; }
            if (_vbo != 0) { fixed (uint* p = &_vbo) GL.DeleteBuffers(1, p); _vbo = 0; }
            _w = 0;
            _h = 0;
        }

        private void DestroyTargets()
        {
            if (_bloomFBO_A != 0) { fixed (uint* p = &_bloomFBO_A) GL.DeleteFramebuffers(1, p); _bloomFBO_A = 0; }
            if (_bloomTex_A != 0) { fixed (uint* p = &_bloomTex_A) GL.DeleteTextures(1, p); _bloomTex_A = 0; }
            if (_bloomFBO_B != 0) { fixed (uint* p = &_bloomFBO_B) GL.DeleteFramebuffers(1, p); _bloomFBO_B = 0; }
            if (_bloomTex_B != 0) { fixed (uint* p = &_bloomTex_B) GL.DeleteTextures(1, p); _bloomTex_B = 0; }
            if (_compositeFBO != 0) { fixed (uint* p = &_compositeFBO) GL.DeleteFramebuffers(1, p); _compositeFBO = 0; }
            if (_compositeTex != 0) { fixed (uint* p = &_compositeTex) GL.DeleteTextures(1, p); _compositeTex = 0; }
            if (_lumaFBO != 0) { fixed (uint* p = &_lumaFBO) GL.DeleteFramebuffers(1, p); _lumaFBO = 0; }
            if (_lumaTex != 0) { fixed (uint* p = &_lumaTex) GL.DeleteTextures(1, p); _lumaTex = 0; }
        }

        private static (uint fbo, uint tex) CreateColorTarget(int w, int h, uint internalFormat, uint pixelType)
        {
            uint fbo = 0, tex = 0;

            GL.GenFramebuffers(1, &fbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, fbo);

            GL.GenTextures(1, &tex);
            GL.BindTexture(Const.GL_TEXTURE_2D, tex);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)internalFormat, w, h, 0,
                          Const.GL_RGBA, pixelType, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, tex, 0);

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            return (fbo, tex);
        }

        private static void BindTarget(uint fbo, int w, int h)
        {
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, fbo);
            GL.Viewport(0, 0, w, h);
            GL.Disable(Const.GL_DEPTH_TEST);
        }

        private static void BindTexUnit0(uint tex)
        {
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, tex);
        }

        private void DrawQuad()
        {
            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            GL.BindVertexArray(0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        }

        private void CreateQuad()
        {
            float[] quad =
            [
                -1f, -1f,  0f, 0f,
                 1f, -1f,  1f, 0f,
                 1f,  1f,  1f, 1f,
                -1f, -1f,  0f, 0f,
                 1f,  1f,  1f, 1f,
                -1f,  1f,  0f, 1f,
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

            _vao = vao;
            _vbo = vbo;
        }
    }
}
