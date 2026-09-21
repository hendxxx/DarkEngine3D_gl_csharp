using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Libs;
using System;

namespace DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing
{
    /// <summary>
    /// AAA post-FX chain: reactive bloom + ACES tonemapping + gamma correction.
    /// Bloom is a multi-mip chain (bright pass → 5 half-res downscales, each blurred,
    /// additively combined on the way back up) so bright spots bloom wide AND tight —
    /// the classic "reactive" firefly glow. Uses a dedicated luminance texture for
    /// auto-exposure so the viewport texture never needs mipmap-compatible filters.
    /// </summary>
    public unsafe class PostFxProcessor
    {
        // ── Shared instance: GameScene's PostProcessStack AND the editor shared-FBO
        // path (SceneManager) use the SAME processor so auto-exposure keeps one
        // continuous adaptation state across render paths. ──
        private static PostFxProcessor? _shared;
        public static PostFxProcessor Shared => _shared ??= new PostFxProcessor(1, 1);

        // ── Reactive bloom mip chain: mip[i] is half the size of mip[i-1]. ──
        // Downscale blurs each level; the upscale pass combines level+1 into level
        // additively (BloomDownsample/BloomUpsample fragment shaders) so the final
        // bloom texture holds BOTH wide halos and tight hot cores.
        private const int BloomMipCount = 5;
        private readonly uint[] _mipFBOs = new uint[BloomMipCount];
        private readonly uint[] _mipTexs = new uint[BloomMipCount];
        private readonly int[] _mipWs = new int[BloomMipCount];
        private readonly int[] _mipHs = new int[BloomMipCount];
        // Ping-pong scratch per mip — blur passes need a second target the same size
        // (sampling and writing the same texture is a feedback loop = undefined).
        private readonly uint[] _blurFBOs = new uint[BloomMipCount];
        private readonly uint[] _blurTexs = new uint[BloomMipCount];
        // ── Composite FBO (full res) ──
        private uint _compositeFBO = 0, _compositeTex = 0;
        // ── Output FBO (full res, separate from input to avoid read/write feedback loop) ──
        private uint _outputFBO = 0, _outputTex = 0;
        // ── Dedicated luminance texture for auto-exposure ──
        private uint _lumaFBO = 0, _lumaTex = 0;
        private int _lumaW = 0, _lumaH = 0;

        private int _w = 0, _h = 0;

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

            // Reactive bloom mip chain: starts at half res, halves each level down.
            int mw = Math.Max(1, width / 2);
            int mh = Math.Max(1, height / 2);
            for (int i = 0; i < BloomMipCount; i++)
            {
                _mipWs[i] = Math.Max(1, mw);
                _mipHs[i] = Math.Max(1, mh);
                (_mipFBOs[i], _mipTexs[i]) = CreateColorTarget(_mipWs[i], _mipHs[i]);
                (_blurFBOs[i], _blurTexs[i]) = CreateColorTarget(_mipWs[i], _mipHs[i]);
                mw = Math.Max(1, mw / 2);
                mh = Math.Max(1, mh / 2);
            }

            (_compositeFBO, _compositeTex) = CreateColorTarget(width, height);
            (_outputFBO, _outputTex) = CreateColorTarget(width, height);

            // Luminance texture: small, RGBA8, mipmapped for average luminance
            _lumaW = Math.Max(1, width / 16);
            _lumaH = Math.Max(1, height / 16);
            (_lumaFBO, _lumaTex) = CreateColorTarget(_lumaW, _lumaH);
            // Enable mipmaps on the luminance texture for auto-exposure
            GL.BindTexture(Const.GL_TEXTURE_2D, _lumaTex);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR_MIPMAP_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.GenerateMipmap(Const.GL_TEXTURE_2D);


        }

        /// <summary>Run the chain in-place: read <paramref name="colorTex"/>, grade it
        /// (reactive bloom + auto-exposure + tonemap) and blit the result back into the
        /// same texture via <paramref name="colorFbo"/>. Returns true when executed.
        /// Used by the editor shared-FBO path (SceneManager) for a live viewport.</summary>
        public bool ApplyInPlace(uint colorTex, uint colorFbo, int width, int height)
        {
            if (!PostFxSettings.Enabled) return false;
            if (colorTex == 0 || colorFbo == 0 || width <= 0 || height <= 0) return false;
            Run(colorTex, colorFbo, width, height);
            return true;
        }

        // ── Debug surface — the FrameBuffer Debug panel renders every intermediate
        // target as a live thumbnail so each FX stage can be eyeballed. ──
        /// <summary>True once the chain allocated its GPU targets (first Run).</summary>
        public bool IsAllocated => _compositeTex != 0;
        /// <summary>Final graded result (bloom + tonemap + gamma) before the output copy.</summary>
        public uint CompositeTex => _compositeTex;
        /// <summary>Graded copy that gets blitted to the display target (no feedback loop).</summary>
        public uint OutputTex => _outputTex;
        /// <summary>Size of the composite/output targets in pixels.</summary>
        public (int W, int H) OutputSize => (_w, _h);
        /// <summary>Downscaled scene luminance used by auto-exposure (mip 0).</summary>
        public uint LumaTex => _lumaTex;
        /// <summary>Size of the luminance target in pixels.</summary>
        public (int W, int H) LumaSize => (_lumaW, _lumaH);
        /// <summary>Bloom chain length (the max "Radius (Mips)" value).</summary>
        public int DebugMipCount => BloomMipCount;
        /// <summary>Thresheld bright-pass result at mip <paramref name="i"/> (0 = half res).</summary>
        public uint GetMipTex(int i) => _mipTexs[i];
        /// <summary>Size of bloom mip <paramref name="i"/> in pixels.</summary>
        public (int W, int H) GetMipSize(int i) => (_mipWs[i], _mipHs[i]);
        /// <summary>Blur scratch target of mip <paramref name="i"/> (last softened round trip).</summary>
        public uint GetBlurTex(int i) => _blurTexs[i];

        public void Run(uint inputTexture, uint outputFBO, int width, int height)
        {
            Resize(width, height);
            if (_compositeFBO == 0) return;
            if (_vao == 0) CreateQuad();

            uint brightS = Shader.GetPostFxBrightShaderProgram();
            uint downS = Shader.GetPostFxBloomDownsampleShaderProgram();
            uint upS = Shader.GetPostFxBloomUpsampleShaderProgram();
            uint compS = Shader.GetPostFxCompositeShaderProgram();
            uint passS = Shader.GetBlurPassShaderProgram();

            if (brightS == 0 || downS == 0 || upS == 0 || compS == 0 || passS == 0) return;
            if (_mipFBOs[0] == 0 || _compositeFBO == 0 || _outputFBO == 0) return;

            // ── Auto-exposure ──
            float effectiveExposure = PostFxSettings.Exposure;
            if (PostFxSettings.AutoExposure)
            {
                effectiveExposure = ComputeAutoExposure(inputTexture, width, height);
                PostFxSettings.CurrentAutoExposure = effectiveExposure;
            }

            // ── 1. Bright pass: full scene → mip 0 (half res), thresholded ──
            BindTarget(_mipFBOs[0], _mipWs[0], _mipHs[0]);
            GL.UseProgram(brightS);
            BindTexUnit0(inputTexture);
            GL.Uniform1i(GL.GetUniformLocation(brightS, "sceneTex"), 0);
            GL.Uniform1f(GL.GetUniformLocation(brightS, "u_Threshold"), PostFxSettings.BloomThreshold);
            GL.Uniform1f(GL.GetUniformLocation(brightS, "u_SoftKnee"), PostFxSettings.BloomSoftKnee);
            DrawQuad();

            // ── 2. Reactive chain: 13-tap downsample mip[i-1] → mip[i], then soften each
            // level with ping-pong 13-tap blurs (level A → scratch B → level A). Lower
            // mips = wide soft halos, higher mips = tight hot cores — combined below.
            // BloomMips (1..BloomMipCount, persisted) controls the chain length —
            // fewer mips = tighter glow, more mips = wide cinematic halos.
            int mips = Math.Clamp((int)PostFxSettings.BloomMips, 1, BloomMipCount);
            for (int i = 1; i < mips; i++)
            {
                BindTarget(_mipFBOs[i], _mipWs[i], _mipHs[i]);
                GL.UseProgram(downS);
                BindTexUnit0(_mipTexs[i - 1]);
                GL.Uniform1i(GL.GetUniformLocation(downS, "sceneTex"), 0);
                GL.Uniform2f(GL.GetUniformLocation(downS, "texelSize"), 1f / _mipWs[i - 1], 1f / _mipHs[i - 1]);
                DrawQuad();
            }
            for (int i = mips - 1; i >= 0; i--)
            {
                // Ping-pong soften: level → scratch → level. Each round trip is a
                // double 13-tap box blur — 2 round trips give a smooth wide halo.
                for (int it = 0; it < 2; it++)
                {
                    BindTarget(_blurFBOs[i], _mipWs[i], _mipHs[i]);
                    GL.UseProgram(downS);
                    BindTexUnit0(_mipTexs[i]);
                    GL.Uniform1i(GL.GetUniformLocation(downS, "sceneTex"), 0);
                    GL.Uniform2f(GL.GetUniformLocation(downS, "texelSize"), 1f / _mipWs[i], 1f / _mipHs[i]);
                    DrawQuad();

                    BindTarget(_mipFBOs[i], _mipWs[i], _mipHs[i]);
                    BindTexUnit0(_blurTexs[i]);
                    DrawQuad();
                }
            }

            // ── 3. Upscale: combine additively, small mips → mip 0. Each level adds its
            // tight core into the wider level below, producing the reactive glow. ──
            GL.Enable(Const.GL_BLEND);
            GL.BlendFunc(Const.GL_ONE, Const.GL_ONE); // additive
            for (int i = mips - 1; i > 0; i--)
            {
                BindTarget(_mipFBOs[i - 1], _mipWs[i - 1], _mipHs[i - 1]);
                GL.UseProgram(upS);
                BindTexUnit0(_mipTexs[i]);
                GL.Uniform1i(GL.GetUniformLocation(upS, "sceneTex"), 0);
                GL.Uniform2f(GL.GetUniformLocation(upS, "texelSize"), 1f / _mipWs[i - 1], 1f / _mipHs[i - 1]);
                GL.Uniform1f(GL.GetUniformLocation(upS, "u_MipScale"), (i <= 2) ? 0.8f : 0.4f);
                DrawQuad();
            }
            GL.Disable(Const.GL_BLEND);

            // ── 4. Composite + tonemap + gamma → composite FBO ──
            BindTarget(_compositeFBO, _w, _h);
            GL.UseProgram(compS);
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, inputTexture);
            GL.Uniform1i(GL.GetUniformLocation(compS, "sceneTex"), 0);
            GL.ActiveTexture(Const.GL_TEXTURE1);
            GL.BindTexture(Const.GL_TEXTURE_2D, _mipTexs[0]);
            GL.Uniform1i(GL.GetUniformLocation(compS, "bloomTex"), 1);
            GL.Uniform1f(GL.GetUniformLocation(compS, "u_BloomIntensity"), PostFxSettings.BloomIntensity);
            GL.Uniform1f(GL.GetUniformLocation(compS, "u_Exposure"), effectiveExposure);
            GL.Uniform1f(GL.GetUniformLocation(compS, "u_Gamma"), PostFxSettings.Gamma);
            DrawQuad();

            // ── 5. Copy graded result to separate output texture (avoid feedback loop) ──
            BindTarget(_outputFBO, _w, _h);
            GL.UseProgram(passS);
            BindTexUnit0(_compositeTex);
            GL.Uniform1i(GL.GetUniformLocation(passS, "sceneTex"), 0);
            GL.Uniform2f(GL.GetUniformLocation(passS, "texelSize"), 1f / _w, 1f / _h);
            GL.Uniform1f(GL.GetUniformLocation(passS, "blurRadius"), 0f);
            GL.Uniform1f(GL.GetUniformLocation(passS, "blurStrength"), 1f);
            DrawQuad();

            // ── 6. Copy graded result into the caller's FBO — via a quad draw, NOT
            // glBlitFramebuffer. The blit wrapper silently NO-OPS when the wgl pointer
            // fails to load (same bug the DoF composite hit: effect ran in the debug
            // thumbnails but never reached the texture the viewport samples). A quad
            // draw goes through the normal draw path and always lands. ──
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, outputFBO);
            GL.Viewport(0, 0, _w, _h);
            GL.UseProgram(passS);
            BindTexUnit0(_outputTex);
            GL.Uniform1i(GL.GetUniformLocation(passS, "sceneTex"), 0);
            GL.Uniform2f(GL.GetUniformLocation(passS, "texelSize"), 1f / _w, 1f / _h);
            GL.Uniform1f(GL.GetUniformLocation(passS, "blurRadius"), 0f);
            GL.Uniform1f(GL.GetUniformLocation(passS, "blurStrength"), 1f);
            DrawQuad();
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
        }

        /// <summary>
        /// Compute auto-exposure using a dedicated luminance texture.
        /// Uses GL_UNSIGNED_BYTE for ReadPixels (compatible with RGBA8 textures on all drivers).
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

            // 4. Attach the smallest mip to our FBO and read back as UNSIGNED_BYTE
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _lumaFBO);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, _lumaTex, maxLevel);

            float[] pxF = new float[4];
            fixed (float* p = pxF)
                GL.ReadPixels(0, 0, 1, 1, Const.GL_RGBA, Const.GL_FLOAT, p);

            // Restore full-res attachment
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, _lumaTex, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            // 5. Compute exposure from average luminance
            float luma = 0.2126f * pxF[0] + 0.7152f * pxF[1] + 0.0722f * pxF[2];
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
            for (int i = 0; i < BloomMipCount; i++)
            {
                if (_mipFBOs[i] != 0) { fixed (uint* p = &_mipFBOs[i]) GL.DeleteFramebuffers(1, p); _mipFBOs[i] = 0; }
                if (_mipTexs[i] != 0) { fixed (uint* p = &_mipTexs[i]) GL.DeleteTextures(1, p); _mipTexs[i] = 0; }
                if (_blurFBOs[i] != 0) { fixed (uint* p = &_blurFBOs[i]) GL.DeleteFramebuffers(1, p); _blurFBOs[i] = 0; }
                if (_blurTexs[i] != 0) { fixed (uint* p = &_blurTexs[i]) GL.DeleteTextures(1, p); _blurTexs[i] = 0; }
            }
            if (_compositeFBO != 0) { fixed (uint* p = &_compositeFBO) GL.DeleteFramebuffers(1, p); _compositeFBO = 0; }
            if (_compositeTex != 0) { fixed (uint* p = &_compositeTex) GL.DeleteTextures(1, p); _compositeTex = 0; }
            if (_outputFBO != 0) { fixed (uint* p = &_outputFBO) GL.DeleteFramebuffers(1, p); _outputFBO = 0; }
            if (_outputTex != 0) { fixed (uint* p = &_outputTex) GL.DeleteTextures(1, p); _outputTex = 0; }
            if (_lumaFBO != 0) { fixed (uint* p = &_lumaFBO) GL.DeleteFramebuffers(1, p); _lumaFBO = 0; }
            if (_lumaTex != 0) { fixed (uint* p = &_lumaTex) GL.DeleteTextures(1, p); _lumaTex = 0; }
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

            uint status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                Console.WriteLine($"[PostFx] FBO incomplete: 0x{status:X} ({w}x{h})");

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
