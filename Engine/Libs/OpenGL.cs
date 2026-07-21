using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.Libs
{
    public class OpenGL
    {
        // Cached GLFW function pointers (hot path — called every frame)
        private static unsafe delegate* unmanaged[Cdecl]<IntPtr, void> _glfwSwapBuffers;
        private static unsafe delegate* unmanaged[Cdecl]<void> _glfwPollEvents;
        public static unsafe nint Init()
        {
            nint glLib = NativeLibrary.Load("opengl32.dll");

            // Mengisi secara manual satu per satu jauh lebih aman dari crash
            GL.DeleteProgramPtr = GetProcAddress(glLib, "glDeleteProgram");
            GL.DrawBufferPtr = GetProcAddress(glLib, "glDrawBuffer");
            GL.ReadBufferPtr = GetProcAddress(glLib, "glReadBuffer");
            GL.ClearColorPtr = GetProcAddress(glLib, "glClearColor");
            GL.ClearPtr = GetProcAddress(glLib, "glClear");
            GL.GenBuffersPtr = GetProcAddress(glLib, "glGenBuffers"); 
            GL.BindBufferPtr = GetProcAddress(glLib, "glBindBuffer");
            GL.BufferDataPtr = GetProcAddress(glLib, "glBufferData");
            GL.CreateShaderPtr = GetProcAddress(glLib, "glCreateShader");
            GL.ShaderSourcePtr = GetProcAddress(glLib, "glShaderSource");
            GL.CompileShaderPtr = GetProcAddress(glLib, "glCompileShader");
            GL.CreateProgramPtr = GetProcAddress(glLib, "glCreateProgram");
            GL.AttachShaderPtr = GetProcAddress(glLib, "glAttachShader");
            GL.LinkProgramPtr = GetProcAddress(glLib, "glLinkProgram");
            GL.UseProgramPtr = GetProcAddress(glLib, "glUseProgram");
            GL.DeleteShaderPtr = GetProcAddress(glLib, "glDeleteShader");
            GL.GetUniformLocationPtr = GetProcAddress(glLib, "glGetUniformLocation");
            GL.GetUniformfvPtr = GetProcAddress(glLib, "glGetUniformfv");
            GL.ReadPixelsPtr = GetProcAddress(glLib, "glReadPixels");
            GL.UniformMatrix4fvPtr = GetProcAddress(glLib, "glUniformMatrix4fv");
            GL.UniformMatrix3fvPtr = GetProcAddress(glLib, "glUniformMatrix3fv");

            GL.VertexAttribPointerPtr = GetProcAddress(glLib, "glVertexAttribPointer");
            GL.VertexAttribIPointerPtr = GetProcAddress(glLib, "glVertexAttribIPointer");
            GL.EnableVertexAttribArrayPtr = GetProcAddress(glLib, "glEnableVertexAttribArray");
            GL.DisableVertexAttribArrayPtr = GetProcAddress(glLib, "glDisableVertexAttribArray");

            GL.DrawArraysPtr = GetProcAddress(glLib, "glDrawArrays"); 
            GL.GenVertexArraysPtr = GetProcAddress(glLib, "glGenVertexArrays");
            GL.BindVertexArrayPtr = GetProcAddress(glLib, "glBindVertexArray");
            GL.EnablePtr = GetProcAddress(glLib, "glEnable");
            GL.DisablePtr = GetProcAddress(glLib, "glDisable");

            GL.PolygonModePtr = GetProcAddress(glLib, "glPolygonMode");

            GL.DeleteVertexArraysPtr = GetProcAddress(glLib, "glDeleteVertexArrays");
            GL.DeleteBuffersPtr = GetProcAddress(glLib, "glDeleteBuffers");

            GL.CullFacePtr = GetProcAddress(glLib, "glCullFace");
            GL.FrontFacePtr = GetProcAddress(glLib, "glFrontFace");
            GL.Uniform3fPtr = GetProcAddress(glLib, "glUniform3f");

            GL.ViewportPtr = GetProcAddress(glLib, "glViewport");

            GL.GenTexturesPtr = GetProcAddress(glLib,"glGenTextures");
            GL.DeleteTexturesPtr = GetProcAddress(glLib, "glDeleteTextures");
            GL.BindTexturePtr = GetProcAddress(glLib, "glBindTexture");
            GL.TexImage2DPtr = GetProcAddress(glLib, "glTexImage2D");
            GL.TexParameteriPtr = GetProcAddress(glLib, "glTexParameteri");
            GL.GenerateMipmapPtr = GetProcAddress(glLib, "glGenerateMipmap");
            GL.ActiveTexturePtr = GetProcAddress(glLib, "glActiveTexture");
            GL.Uniform1iPtr = GetProcAddress(glLib, "glUniform1i");
            GL.Uniform1fPtr = GetProcAddress(glLib, "glUniform1f");
            GL.Uniform2fPtr = GetProcAddress(glLib, "glUniform2f");
            GL.Uniform3fPtr = GetProcAddress(glLib, "glUniform3f");
            GL.Uniform4fPtr = GetProcAddress(glLib, "glUniform4f");

            GL.DrawElementsPtr = GetProcAddress(glLib, "glDrawElements");
            GL.TexParameterfPtr = GetProcAddress(glLib, "glTexParameterf");
            GL.TexParameterfvPtr = GetProcAddress(glLib, "glTexParameterfv");
            GL.BlendFuncPtr = GetProcAddress(glLib, "glBlendFunc");
            GL.PixelStorePtr = GetProcAddress(glLib, "glPixelStorei");
            GL.BufferSubDataPtr = GetProcAddress(glLib, "glBufferSubData");
            GL.PolygonOffsetPtr = GetProcAddress(glLib, "glPolygonOffset");
             
            GL.GenFramebuffersPtr = GetProcAddress(glLib, "glGenFramebuffers");
            GL.BindFramebufferPtr = GetProcAddress(glLib, "glBindFramebuffer");
            GL.FramebufferTexture2DPtr = GetProcAddress(glLib, "glFramebufferTexture2D");
            GL.DrawBuffersPtr = GetProcAddress(glLib, "glDrawBuffers");
            GL.CheckFramebufferStatusPtr = GetProcAddress(glLib, "glCheckFramebufferStatus");
            GL.DepthMaskPtr = GetProcAddress(glLib, "glDepthMask");
            GL.DeleteFramebuffersPtr = GetProcAddress(glLib, "glDeleteFramebuffers");
            GL.GenRenderbuffersPtr = GetProcAddress(glLib, "glGenRenderbuffers");
            GL.DeleteRenderbuffersPtr = GetProcAddress(glLib, "glDeleteRenderbuffers");
            GL.BindRenderbufferPtr = GetProcAddress(glLib, "glBindRenderbuffer");
            GL.RenderbufferStoragePtr = GetProcAddress(glLib, "glRenderbufferStorage");
            GL.FramebufferRenderbufferPtr = GetProcAddress(glLib, "glFramebufferRenderbuffer");
            GL.GetShaderivPtr = GetProcAddress(glLib, "glGetShaderiv");
            GL.GetProgramivPtr = GetProcAddress(glLib, "glGetProgramiv");
            GL.DepthFuncPtr = GetProcAddress(glLib, "glDepthFunc");
            GL.GetStringPtr = GetProcAddress(glLib, "glGetString");
            GL.VertexAttribDivisorPtr = GetProcAddress(glLib, "glVertexAttribDivisor");
            GL.DrawArraysInstancedPtr = GetProcAddress(glLib, "glDrawArraysInstanced");
            GL.DrawElementsInstancedPtr = GetProcAddress(glLib, "glDrawElementsInstanced");

            GL.GenQueriesPtr = GetProcAddress(glLib, "glGenQueries");
            GL.DeleteQueriesPtr = GetProcAddress(glLib, "glDeleteQueries");
            GL.BeginQueryPtr = GetProcAddress(glLib, "glBeginQuery");
            GL.EndQueryPtr = GetProcAddress(glLib, "glEndQuery");
            GL.GetQueryObjectivPtr = GetProcAddress(glLib, "glGetQueryObjectiv");
            GL.GetQueryObjectuivPtr = GetProcAddress(glLib, "glGetQueryObjectuiv");
            GL.ColorMaskPtr = GetProcAddress(glLib, "glColorMask");
            GL.ScissorPtr = GetProcAddress(glLib, "glScissor");
            GL.GetIntegervPtr = GetProcAddress(glLib, "glGetIntegerv");
            GL.IsEnabledPtr = GetProcAddress(glLib, "glIsEnabled");
            GL.DrawElementsBaseVertexPtr = GetProcAddress(glLib, "glDrawElementsBaseVertex");

            return glLib;
        }

        public static unsafe void EnableFaceCulling(bool active, bool CCW = true)
        {
            if (active)
            {
                GL.Enable(Const.GL_CULL_FACE);
                GL.CullFace(Const.GL_FRONT);
                GL.FrontFace(CCW ? Const.GL_CW : Const.GL_CCW);
            }
            else
            {
                // 🛠️ FIX #6: Do NOT call GL.FrontFace when disabling culling.
                // The old code set FrontFace(GL_CW) on every disable, which
                // corrupted the OpenGL state machine — subsequent 3D rendering
                // that calls EnableFaceCulling(true) with CCW=false would get
                // the wrong winding, causing back-face culling issues.
                GL.Disable(Const.GL_CULL_FACE);
            }
        } 

        public static unsafe void EnableDepthTest(bool active)
        {
            if (active)
            {
                GL.Enable(Const.GL_DEPTH_TEST);
                GL.DepthFunc(Const.GL_LEQUAL);
                GL.DepthMask( true );
            }
                
        }
        public static nint GetProcAddress(nint glLib, string name)
        {
            // 1. Coba ambil dari wglGetProcAddress (untuk fungsi modern/ext)
            var wglAddr = Marshal.GetDelegateForFunctionPointer<wglGetProcAddressDelegate>(
                NativeLibrary.GetExport(glLib, "wglGetProcAddress")
            );

            nint addr = wglAddr(name);

            // 2. Jika gagal (kembali 0 atau -1), coba ambil langsung dari DLL (untuk fungsi kuno/1.1)
            if (addr == nint.Zero || addr == 1 || addr == 2 || addr == -1)
            {
                NativeLibrary.TryGetExport(glLib, name, out addr);
            }

            return addr;
        }

        /// <summary>
        /// Call once after OpenGL.Init() to cache GLFW function pointers used every frame.
        /// </summary>
        public static unsafe void CacheGlfwFunctions(nint glfwLib)
        {
            _glfwSwapBuffers = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(glfwLib, "glfwSwapBuffers");
            _glfwPollEvents = (delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(glfwLib, "glfwPollEvents");
        }

        public static unsafe void SwapBuffer(nint window)
        {
            _glfwSwapBuffers(window);
        }

        public static unsafe void PollEvents()
        {
            _glfwPollEvents();
        }
    }
}
