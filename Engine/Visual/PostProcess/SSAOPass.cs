using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing
{
    public unsafe class SSAOPass : IPostProcessPass
    {
        // Shaders
        private uint _ssaoShader;
        private uint _blurHShader;
        private uint _blurVShader;
        private uint _compositeShader;

        // Fullscreen quad VAO/VBO (shared)
        private uint _vao, _vbo;

        // Ping-pong FBOs for AO computation + blur + temporal
        private uint _aoFBO, _aoTex;
        private uint _blurFBO, _blurTex;
        private uint _tempFBO, _tempTex;  // temporary for 2-pass separable blur
        private int _fboWidth, _fboHeight; // track FBO size

        // Depth texture reference (from PostProcessStack)
        private uint _depthTex;

        // Projection matrix for view-space reconstruction
        private Matrix4x4 _projection;

        // Hemisphere sample kernel (32 samples using spherical Fibonacci)
        private Vector3[] _samples = new Vector3[32];

        private int _width, _height;
        private bool _initialized = false;

        public SSAOPass(uint ssaoShader, uint blurHShader, uint blurVShader, uint compositeShader, uint depthTex, ref Matrix4x4 projection)
        {
            _ssaoShader = ssaoShader;
            _blurHShader = blurHShader;
            _blurVShader = blurVShader;
            _compositeShader = compositeShader;
            _depthTex = depthTex;
            _projection = projection;
            _fboWidth = 0; _fboHeight = 0;

            GenerateSampleKernel();
        }

        public void UpdateDepthTexture(uint depthTex)
        {
            _depthTex = depthTex;
        }

        public void UpdateProjection(ref Matrix4x4 projection)
        {
            _projection = projection;
        }

        private Matrix4x4 GetInvProjection()
        {
            Matrix4x4.Invert(_projection, out Matrix4x4 inv);
            return inv;
        }

        private void GenerateSampleKernel()
        {
            // Hemisphere samples (z > 0) used with the NEGATED surface normal.
            // The negated normal points AWAY from camera (deeper into scene),
            // so hemisphere samples follow the surface orientation but never
            // go behind the camera. This gives correct AO at all angles.
            float goldenRatio = (1.0f + MathF.Sqrt(5.0f)) / 2.0f;
            for (int i = 0; i < 32; i++)
            {
                float r = MathF.Sqrt((float)(i + 0.5f) / 32.0f);
                float theta = 2.0f * MathF.PI * (float)i * goldenRatio;

                float x = r * MathF.Cos(theta);
                float y = r * MathF.Sin(theta);
                float z = MathF.Sqrt(MathF.Max(0.0f, 1.0f - x * x - y * y)); // hemisphere: z > 0

                // Scale so samples cluster near origin
                float scale = 0.1f + 0.9f * (float)(i / 32.0f) * (float)(i / 32.0f);
                _samples[i] = new Vector3(x, y, z) * scale;
            }
        }

        // Noise texture no longer needed — using gl_FragCoord hash instead

        private void CreateAOAndBlurFBOs(int width, int height)
        {
            // Only create if size changed or not yet created
            if (_aoFBO != 0 && _fboWidth == width && _fboHeight == height)
                return;

            // Delete old FBOs if they exist
            if (_aoFBO != 0)
            {
                uint fbo = _aoFBO, tex = _aoTex;
                GL.DeleteFramebuffers(1, &fbo);
                GL.DeleteTextures(1, &tex);
            }
            if (_blurFBO != 0)
            {
                uint fbo = _blurFBO, tex = _blurTex;
                GL.DeleteFramebuffers(1, &fbo);
                GL.DeleteTextures(1, &tex);
            }
            if (_tempFBO != 0)
            {
                uint fbo = _tempFBO, tex = _tempTex;
                GL.DeleteFramebuffers(1, &fbo);
                GL.DeleteTextures(1, &tex);
            }
            // AO FBO
            uint aoFbo = 0, aoTex = 0;
            GL.GenFramebuffers(1, &aoFbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, aoFbo);
            GL.GenTextures(1, &aoTex);
            GL.BindTexture(Const.GL_TEXTURE_2D, aoTex);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_R8,
                          width, height, 0,
                          Const.GL_RED, Const.GL_UNSIGNED_BYTE, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, aoTex, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            _aoFBO = aoFbo;
            _aoTex = aoTex;

            // Blur FBO (final output after two-pass separable blur)
            uint blurFbo = 0, blurTex = 0;
            GL.GenFramebuffers(1, &blurFbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, blurFbo);
            GL.GenTextures(1, &blurTex);
            GL.BindTexture(Const.GL_TEXTURE_2D, blurTex);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_R8,
                          width, height, 0,
                          Const.GL_RED, Const.GL_UNSIGNED_BYTE, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, blurTex, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            _blurFBO = blurFbo;
            _blurTex = blurTex;

            // Temp FBO for intermediate blur pass (horizontal → temp → vertical → blur)
            uint tempFbo = 0, tempTex = 0;
            GL.GenFramebuffers(1, &tempFbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, tempFbo);
            GL.GenTextures(1, &tempTex);
            GL.BindTexture(Const.GL_TEXTURE_2D, tempTex);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_R8,
                          width, height, 0,
                          Const.GL_RED, Const.GL_UNSIGNED_BYTE, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, tempTex, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            _tempFBO = tempFbo;
            _tempTex = tempTex;

            _fboWidth = width;
            _fboHeight = height;
        }

        public void Init()
        {
            if (_initialized) return;

            // Create fullscreen quad
            float[] quad =
            [
                -1f,  1f,  0f, 0f,   // top-left
                -1f, -1f,  0f, 1f,   // bottom-left
                 1f, -1f,  1f, 1f,   // bottom-right

                -1f,  1f,  0f, 0f,   // top-left
                 1f, -1f,  1f, 1f,   // bottom-right
                 1f,  1f,  1f, 0f    // top-right
            ];


            uint vao = 0, vbo = 0;
            GL.GenVertexArrays(1, &vao);
            GL.GenBuffers(1, &vbo);

            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

            fixed (float* v = quad)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER,
                              (nuint)(quad.Length * sizeof(float)),
                              v,
                              Const.GL_STATIC_DRAW);
            }

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));

            GL.BindVertexArray(0);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, 0);

            _vao = vao;
            _vbo = vbo;


            _initialized = true;
        }

        public void Execute(uint inputTexture, int width, int height, float time)
        {
            if (!Keyboard.GetIsSSAOActive())
            {
                // SSAO disabled: pass through scene color directly by rendering to the current FBO (0)
                RenderPassThrough(inputTexture, width, height);
                return;
            }

            _width = width;
            _height = height;

            // Ensure AO FBOs are the right size (only recreates on resize)
            CreateAOAndBlurFBOs(width, height);

            // --- PASS 1: Compute raw SSAO ---
            GL.UseProgram(_ssaoShader);
            
            int depthTexLoc = GL.GetUniformLocation(_ssaoShader, "depthTex");
            int projLoc = GL.GetUniformLocation(_ssaoShader, "projection");
            int invProjLoc = GL.GetUniformLocation(_ssaoShader, "invProjection");
            int radiusLoc = GL.GetUniformLocation(_ssaoShader, "radius");
            int biasLoc = GL.GetUniformLocation(_ssaoShader, "bias");
            int powerLoc = GL.GetUniformLocation(_ssaoShader, "power");

            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, _depthTex);
            GL.Uniform1i(depthTexLoc, 0);

            Matrix4x4 invProj = GetInvProjection();
            fixed (float* pProj = &_projection.M11)
                GL.UniformMatrix4fv(projLoc, 1, false, pProj);
            GL.UniformMatrix4fv(invProjLoc, 1, false, (float*)&invProj.M11);

            GL.Uniform1f(radiusLoc, 1.5f);
            GL.Uniform1f(biasLoc, 0.025f);
            GL.Uniform1f(powerLoc, 2.0f);

            int samplesLoc = GL.GetUniformLocation(_ssaoShader, "samples[0]");
            if (samplesLoc != -1)
            {
                for (int i = 0; i < 32; i++)
                    GL.Uniform3f(samplesLoc + i, _samples[i].X, _samples[i].Y, _samples[i].Z);
            }

            // Render raw AO to AO FBO
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _aoFBO);
            GL.Viewport(0, 0, width, height);
            GL.Disable(Const.GL_DEPTH_TEST);
            GL.ClearColor(1.0f, 1.0f, 1.0f, 1.0f);
            GL.Clear(Const.GL_COLOR_BUFFER_BIT);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            GL.BindVertexArray(0);

            // --- PASS 2: Horizontal blur (AO → temp) ---
            GL.UseProgram(_blurHShader);
            int texLocH = GL.GetUniformLocation(_blurHShader, "ssaoTex");
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, _aoTex);
            GL.Uniform1i(texLocH, 0);

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _tempFBO);
            GL.Clear(Const.GL_COLOR_BUFFER_BIT);
            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            GL.BindVertexArray(0);

            // --- PASS 3: Vertical blur (temp → blur) ---
            GL.UseProgram(_blurVShader);
            int texLocV = GL.GetUniformLocation(_blurVShader, "ssaoTex");
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, _tempTex);
            GL.Uniform1i(texLocV, 0);

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _blurFBO);
            GL.Clear(Const.GL_COLOR_BUFFER_BIT);
            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            GL.BindVertexArray(0);

            // --- PASS 4: Composite AO onto scene color (using blurred AO directly) ---
            RenderComposite(inputTexture, _blurTex, width, height);
        }



        // Cached uniform locations for composite shader
        private int _compositeColorTexLoc = -1;
        private int _compositeAoTexLoc = -1;
        private int _compositeUseAOLoc = -1;
        private int _compositeStrengthLoc = -1;

        private void RenderComposite(uint colorTex, uint aoTex, int width, int height)
        {
            // ⚠️ CRITICAL: Bind default framebuffer so result appears on screen
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, width, height);
            GL.Disable(Const.GL_DEPTH_TEST);

            GL.UseProgram(_compositeShader);

            // Cache uniform locations on first use
            if (_compositeColorTexLoc < 0)
            {
                _compositeColorTexLoc = GL.GetUniformLocation(_compositeShader, "sceneTex");
                _compositeAoTexLoc = GL.GetUniformLocation(_compositeShader, "ssaoTex");
                _compositeUseAOLoc = GL.GetUniformLocation(_compositeShader, "useSSAO");
                _compositeStrengthLoc = GL.GetUniformLocation(_compositeShader, "ssaoStrength");
            }

            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, colorTex);
            GL.Uniform1i(_compositeColorTexLoc, 0);

            GL.ActiveTexture(Const.GL_TEXTURE1);
            GL.BindTexture(Const.GL_TEXTURE_2D, aoTex);
            GL.Uniform1i(_compositeAoTexLoc, 1);

            GL.Uniform1i(_compositeUseAOLoc, 1);
            GL.Uniform1f(_compositeStrengthLoc, 0.35f);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            GL.BindVertexArray(0);

            GL.ActiveTexture(Const.GL_TEXTURE1);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            
            // Re-enable depth test for next frame's scene rendering
            GL.Enable(Const.GL_DEPTH_TEST);
        }

        private void RenderPassThrough(uint colorTex, int width, int height)
        {
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, width, height);
            GL.Disable(Const.GL_DEPTH_TEST);

            GL.UseProgram(_compositeShader);

            if (_compositeColorTexLoc < 0)
                _compositeColorTexLoc = GL.GetUniformLocation(_compositeShader, "sceneTex");
            if (_compositeUseAOLoc < 0)
                _compositeUseAOLoc = GL.GetUniformLocation(_compositeShader, "useSSAO");

            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, colorTex);
            GL.Uniform1i(_compositeColorTexLoc, 0);
            GL.Uniform1i(_compositeUseAOLoc, 0);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            GL.BindVertexArray(0);

            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.Enable(Const.GL_DEPTH_TEST);
        }
    }
}
