using DarkEngine3D_gl_csharp.Engine.Libs;
using System;

namespace DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing
{

    public unsafe class PostProcessStack
    {
        public uint SceneFBO;
        public uint SceneColorTex;
        public uint SceneDepthRBO;

        private int _width, _height;
        private uint _simpleVAO, _simpleVBO;

        private readonly List<IPostProcessPass> _passes = new();

        public PostProcessStack(int width, int height)
        {
            _width = width;
            _height = height;
            CreateSceneFBO();
        }

        void CreateSceneFBO()
        {
            uint fbo = 0, color = 0, rbo = 0;

            GL.GenFramebuffers(1, &fbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, fbo);

            GL.GenTextures(1, &color);
            GL.BindTexture(Const.GL_TEXTURE_2D, color);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA8,
                          _width, _height, 0,
                          Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, color, 0);

            GL.GenRenderbuffers(1, &rbo);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, rbo);
            GL.RenderbufferStorage(Const.GL_RENDERBUFFER, Const.GL_DEPTH24_STENCIL8, _width, _height);
            GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_STENCIL_ATTACHMENT,
                                       Const.GL_RENDERBUFFER, rbo);

            uint status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                Console.WriteLine($"PostProcessStack FBO incomplete: 0x{status:X}");

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);

            SceneFBO = fbo;
            SceneColorTex = color;
            SceneDepthRBO = rbo;
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
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, windowWidth, windowHeight);
            GL.Disable(Const.GL_DEPTH_TEST);

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
