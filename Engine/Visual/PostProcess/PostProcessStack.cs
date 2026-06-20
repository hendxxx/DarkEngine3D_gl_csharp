using DarkEngine3D_gl_csharp.Engine.Libs;
using System;

namespace DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing
{

    public unsafe class PostProcessStack
    {
        public uint SceneFBO;
        public uint SceneColorTex;
        public uint SceneDepthTex;  // Texture instead of RBO so SSAO can sample it

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
            uint fbo = 0, color = 0, depthTex = 0;

            GL.GenFramebuffers(1, &fbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, fbo);

            // Color attachment
            GL.GenTextures(1, &color);
            GL.BindTexture(Const.GL_TEXTURE_2D, color);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA8,
                          _width, _height, 0,
                          Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, color, 0);

            // Depth texture (instead of RBO) — for SSAO sampling
            GL.GenTextures(1, &depthTex);
            GL.BindTexture(Const.GL_TEXTURE_2D, depthTex);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_DEPTH24_STENCIL8,
                          _width, _height, 0,
                          Const.GL_DEPTH_STENCIL, Const.GL_UNSIGNED_INT_24_8, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_STENCIL_ATTACHMENT,
                                    Const.GL_TEXTURE_2D, depthTex, 0);

            uint status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                Console.WriteLine($"[PostProcessStack] FBO incomplete: 0x{status:X}");

            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);

            SceneFBO = fbo;
            SceneColorTex = color;
            SceneDepthTex = depthTex;
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
                pass.Execute(currentTex, windowWidth, windowHeight, time);
            }

            GL.Enable(Const.GL_DEPTH_TEST); 
        }

        public void Resize(int width, int height)
        {
            if (width == _width && height == _height) return;
            _width = width;
            _height = height;

            // Delete old resources
            uint fbo = SceneFBO, color = SceneColorTex, depth = SceneDepthTex;
            GL.DeleteFramebuffers(1, &fbo);
            GL.DeleteTextures(1, &color);
            GL.DeleteTextures(1, &depth);

            CreateSceneFBO();
        }
    }


}
