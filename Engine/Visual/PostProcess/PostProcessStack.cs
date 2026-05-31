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

        // dipanggil sebelum render scene
        public void BindSceneFBO()
        {
            if (_passes.Count == 0) return;  
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
            if (_passes.Count == 0) return;  

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, windowWidth, windowHeight);
            GL.Disable(Const.GL_DEPTH_TEST);

            uint currentTex = SceneColorTex;

            foreach (var pass in _passes)
            {
                // untuk sekarang: semua pass langsung render ke default framebuffer
                // kalau mau bener2 multi-buffer, nanti kita tambahin ping-pong FBO
                pass.Execute(currentTex, windowWidth, windowHeight, time);
            }

            GL.Enable(Const.GL_DEPTH_TEST); 
        }
    }


}
