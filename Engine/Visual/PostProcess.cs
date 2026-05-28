using DarkEngine3D_gl_csharp.Engine.Libs;
using System;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    public class PostProcess
    {
        // Fullscreen quad
        static uint _quadVAO = 0;
        static uint _quadVBO = 0;

        // Scene FBO + textures (global fields)
        static uint _framebufferTexture = 0;
        static uint _sceneDepthTex = 0;
        public static uint _sceneFBO = 0;
        public static uint _sceneRBO = 0;

        public static uint GetSceneFBO() => _sceneFBO;
        public static uint GetSceneRBO() => _sceneRBO;
        public static uint GetframebufferTexture() => _framebufferTexture; 

        public static uint GetQuadVAO() => _quadVAO;

        // Shader program handle (assume Shader.GetGodRayShaderProgram exists)
        // public static uint godRayProgram; // optional cache

        public PostProcess() { }

        public unsafe static void Init(int width, int height)
        {
            // --- Fullscreen quad vertices in NDC (-1..1) with texcoords ---
            float[] vertices = new float[] {
                 // x, y,   u, v
                -1f,  1f,  0f, 0f,   // top-left
                -1f, -1f,  0f, 1f,   // bottom-left
                 1f, -1f,  1f, 1f,   // bottom-right

                -1f,  1f,  0f, 0f,   // top-left
                 1f, -1f,  1f, 1f,   // bottom-right
                 1f,  1f,  1f, 0f    // top-right
            };

            // --- VAO / VBO ---
            fixed (uint* quadVAO = &_quadVAO) GL.GenVertexArrays(1, quadVAO);
            fixed (uint* quadVBO = &_quadVBO) GL.GenBuffers(1, quadVBO);

            GL.BindVertexArray(_quadVAO);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _quadVBO);
            fixed (float* v = vertices)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(sizeof(float) * vertices.Length), v, Const.GL_STATIC_DRAW);
            }

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));

            GL.BindVertexArray(0);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, 0);

            // --- Framebuffer ---
            fixed (uint* sceneFBO = &_sceneFBO) GL.GenFramebuffers(1, sceneFBO);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _sceneFBO);

            // Color texture
            fixed (uint* framebufferTexture = &_framebufferTexture) GL.GenTextures(1, framebufferTexture);
            GL.BindTexture(Const.GL_TEXTURE_2D, _framebufferTexture);

            //// safe unpack alignment
            //GL.PixelStore(Const.GL_UNPACK_ALIGNMENT, 1);

            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA8, width, height, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (void*)0);
            uint err = GL.GetError();
            if (err != 0) Console.WriteLine("GL Error after TexImage2D (color): 0x" + err.ToString("X"));

            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR); 
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0, Const.GL_TEXTURE_2D, _framebufferTexture, 0);

            err = GL.GetError();
            if (err != 0) Console.WriteLine("GL Error after FramebufferTexture2D (color): 0x" + err.ToString("X"));

            // Depth+stencil renderbuffer
            fixed (uint* sceneRBO = &_sceneRBO) GL.GenRenderbuffers(1, sceneRBO);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, _sceneRBO);
            GL.RenderbufferStorage(Const.GL_RENDERBUFFER, Const.GL_DEPTH24_STENCIL8, width, height);
            err = GL.GetError();
            if (err != 0) Console.WriteLine("GL Error after RenderbufferStorage: 0x" + err.ToString("X"));

            GL.FramebufferRenderbuffer(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_STENCIL_ATTACHMENT, Const.GL_RENDERBUFFER, _sceneRBO);
            err = GL.GetError();
            if (err != 0) Console.WriteLine("GL Error after FramebufferRenderbuffer: 0x" + err.ToString("X"));

            // Set draw buffers
            uint[] bufs = new uint[] { Const.GL_COLOR_ATTACHMENT0 };
            fixed (uint* b = bufs) { GL.DrawBuffers(1, b); }
            err = GL.GetError();
            if (err != 0) Console.WriteLine("GL Error after DrawBuffers: 0x" + err.ToString("X"));

            // Check FBO
            uint status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                Console.WriteLine($"Framebuffer not complete: 0x{status:X}");
            else
                Console.WriteLine($"InitSceneFBO done: sceneFBO={_sceneFBO}, colorTex={_framebufferTexture}, rbo={_sceneRBO}");

            // cleanup binds
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.BindRenderbuffer(Const.GL_RENDERBUFFER, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);

            err = GL.GetError();
            if (err != 0) Console.WriteLine("GL Error after Init: 0x" + err.ToString("X"));
        }




        public static void BindSceneFBO(int width, int height)
        {
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _sceneFBO);
            GL.Viewport(0, 0, width, height);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthMask(true);
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);

        }


        public static void Draw(int windowWidth, int windowHeight)
        {
            OpenGL.EnableFaceCulling(false);

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, windowWidth, windowHeight);
            GL.Disable(Const.GL_DEPTH_TEST);

            uint postProg = Shader.GetPostProdShaderProgram();
            GL.UseProgram(postProg);

            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, _framebufferTexture);

            int loc = GL.GetUniformLocation(postProg, "sceneTex");
            GL.Uniform1i(loc, 0);

            GL.BindVertexArray(_quadVAO);
            GL.Disable(Const.GL_DEPTH_TEST);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            GL.BindVertexArray(0);

            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.Enable(Const.GL_DEPTH_TEST);

            OpenGL.EnableFaceCulling(true);
             
            // Unbind scene FBO and run postprocess to default framebuffer
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, windowWidth, windowHeight);
            GL.Disable(Const.GL_DEPTH_TEST);



        }

    }
}
