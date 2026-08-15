using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Libs;
using System;

namespace DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing
{
    /// <summary>
    /// Self-contained AAA post-FX chain (bloom + ACES tonemapping + gamma correction)
    /// that can be run on ANY full-res texture — GameScene's PostProcessStack and the
    /// editor's shared viewport FBO both use it, so the IDE "Post FX" panel sliders are
    /// live in the editor viewport too.
    ///
    /// <c>Run(inputTexture, outputFBO, w, h)</c> extracts a half-res bright-pass, blurs it
    /// (2× separable Gaussian ping-pong), composites scene + bloom with ACES tonemap and
    /// gamma, then writes the graded result into <c>outputFBO</c>. All values come live
    /// from <see cref="PostFxSettings"/> (no rebuild needed to retune).
    /// </summary>
    public unsafe class PostFxProcessor
    {
        private uint _bloomFBO_A = 0, _bloomTex_A = 0;
        private uint _bloomFBO_B = 0, _bloomTex_B = 0;
        private uint _compositeFBO = 0, _compositeTex = 0;
        private int _w = 0, _h = 0;
        private int _bloomW = 0, _bloomH = 0;

        private uint _vao = 0, _vbo = 0;

        // ── Auto-exposure: reads the 1×1 mip of the input (box-filtered average)
        // luminance, adapts the exposure over time so the frame is never blown out
        // nor pitch black. ──
        private uint _lumaFBO = 0;
        private float _smoothedExposure = 1f;
        private readonly System.Diagnostics.Stopwatch _aeClock = new();
        private double _lastAeTime = 0;

        public PostFxProcessor(int width, int height)
        {
            Resize(width, height);
        }

        /// <summary>(Re)create the bloom + composite targets for the given size.
        /// No-op when the size is unchanged, so calling it every frame is cheap.</summary>
        public void Resize(int width, int height)
        {
            if (width == _w && height == _h) return;
            _w = width;
            _h = height;
            DestroyTargets();

            _bloomW = Math.Max(1, width / 2);
            _bloomH = Math.Max(1, height / 2);

            (_bloomFBO_A, _bloomTex_A) = CreateColorTarget(_bloomW, _bloomH);
            (_bloomFBO_B, _bloomTex_B) = CreateColorTarget(_bloomW, _bloomH);
            (_compositeFBO, _compositeTex) = CreateColorTarget(width, height);
        }

        /// <summary>Run the full chain: inputTexture → bright-pass → blur → composite
        /// (scene + bloom, ACES tonemap, gamma) → outputFBO.</summary>
        public void Run(uint inputTexture, uint outputFBO, int width, int height)
        {
            Resize(width, height);
            if (_compositeFBO == 0) return;

            if (_vao == 0) CreateQuad();

            // ── Auto-exposure: measure the scene luminance BEFORE any grading and pick
            // the exposure used by the composite pass below. ──
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

            // ── 3. Composite + tonemap + gamma → full-res composite FBO ──
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

        /// <summary>Measure the input's average luminance (via its 1×1 mip level — each
        /// mip is a box-filtered average of the level below, so the last mip is the
        /// scene average) and return the adaptively smoothed exposure.</summary>
        private float ComputeAutoExposure(uint inputTexture, int width, int height)
        {
            // 1. Generate the mip chain (full-res box-filtered averages down to 1×1).
            GL.BindTexture(Const.GL_TEXTURE_2D, inputTexture);
            GL.GenerateMipmap(Const.GL_TEXTURE_2D);

            int maxDim = Math.Max(width, height);
            int maxLevel = 0;
            while ((maxDim >> maxLevel) > 1) maxLevel++;

            // 2. Attach the 1×1 mip to a tiny FBO and read back the average color.
            if (_lumaFBO == 0)
            {
                uint fbo = 0;
                GL.GenFramebuffers(1, &fbo);
                _lumaFBO = fbo;
            }
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _lumaFBO);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, inputTexture, maxLevel);

            float[] px = new float[4];
            fixed (float* p = px)
                GL.ReadPixels(0, 0, 1, 1, Const.GL_RGBA, Const.GL_FLOAT, p);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            // 3. Target exposure from the average luminance (Rec. 709 luma).
            float luma = 0.2126f * px[0] + 0.7152f * px[1] + 0.0722f * px[2];
            float target = PostFxSettings.AutoExposureTargetLuminance / Math.Max(luma, 1e-4f);
            // Clamp with normalized bounds — Math.Clamp throws if min > max, which the
            // panel sliders can produce when the user drags Min above Max.
            float lo = Math.Min(PostFxSettings.AutoExposureMinExposure, PostFxSettings.AutoExposureMaxExposure);
            float hi = Math.Max(PostFxSettings.AutoExposureMinExposure, PostFxSettings.AutoExposureMaxExposure);
            target = Math.Clamp(target, lo, hi);

            // 4. Exponential smoothing over time — exposure adapts smoothly instead of
            // jumping when the camera pans between bright and dark areas.
            if (!_aeClock.IsRunning) _aeClock.Start();
            double now = _aeClock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(now - _lastAeTime, 0.1);
            _lastAeTime = now;

            float t = 1f - MathF.Exp(-PostFxSettings.AutoExposureSpeed * Math.Max(dt, 0f));
            _smoothedExposure += (target - _smoothedExposure) * t;
            return _smoothedExposure;
        }

        /// <summary>Delete all GL resources (also called on resize before recreating).</summary>
        public void Destroy()
        {
            DestroyTargets();
            if (_vao != 0) { fixed (uint* p = &_vao) GL.DeleteVertexArrays(1, p); _vao = 0; }
            if (_vbo != 0) { fixed (uint* p = &_vbo) GL.DeleteBuffers(1, p); _vbo = 0; }
            if (_lumaFBO != 0) { fixed (uint* p = &_lumaFBO) GL.DeleteFramebuffers(1, p); _lumaFBO = 0; }
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
        }

        private static (uint fbo, uint tex) CreateColorTarget(int w, int h)
        {
            uint fbo = 0, tex = 0;

            GL.GenFramebuffers(1, &fbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, fbo);

            GL.GenTextures(1, &tex);
            GL.BindTexture(Const.GL_TEXTURE_2D, tex);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA8, w, h, 0,
                          Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (void*)0);
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
            // Full-screen quad, CCW, FBO convention: bottom-left = uv(0,0).
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
