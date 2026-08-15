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
            GL.TexImage3DPtr = GetProcAddress(glLib, "glTexImage3D");
            GL.TexSubImage3DPtr = GetProcAddress(glLib, "glTexSubImage3D");
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
            GL.GetShaderInfoLogPtr = GetProcAddress(glLib, "glGetShaderInfoLog");
            GL.GetProgramivPtr = GetProcAddress(glLib, "glGetProgramiv");
            GL.GetProgramInfoLogPtr = GetProcAddress(glLib, "glGetProgramInfoLog");
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

            GL.StencilMaskPtr = GetProcAddress(glLib, "glStencilMask");
            GL.StencilFuncPtr = GetProcAddress(glLib, "glStencilFunc");
            GL.StencilOpPtr = GetProcAddress(glLib, "glStencilOp");

            return glLib;
        }

        /// <summary>
        /// Enable/disable face culling. When active, uses CullFace(GL_BACK)
        /// and FrontFace(CCW ? GL_CCW : GL_CW) — standard 3D rendering state.
        /// When disabling, only calls GL.Disable (no FrontFace modification).
        /// </summary>
        /// <param name="active">True to enable culling, false to disable.</param>
        /// <param name="CCW">When true, FrontFace=GL_CCW (standard). When false, FrontFace=GL_CW.</param>
        public static unsafe void EnableFaceCulling(bool active, bool CCW = true)
        {
            if (active)
            {
                GL.Enable(Const.GL_CULL_FACE);
                GL.CullFace(Const.GL_BACK);
                GL.FrontFace(CCW ? Const.GL_CCW : Const.GL_CW);
            }
            else
            {
                GL.Disable(Const.GL_CULL_FACE);
            }
        } 

        /// <summary>
        /// Apply per-scene OpenGL render state properties (background color, face culling,
        /// wireframe mode, depth test, blending, etc.) at the start of a scene's Render().
        /// This replaces scattered GL state calls inside each scene's render method.
        /// </summary>
        /// <param name="props">The scene's render properties to apply.</param>
        /// <param name="vsyncCurrent">The current VSync state. Pass by ref so it's only set when changed.</param>
        public static void ApplySceneProperties(Scene.SceneRenderProperties props, ref bool vsyncCurrent)
        {
            if (props == null) return;

            // Apply all properties via the SceneRenderProperties.Apply() method
            props.Apply();

            // VSync is a GLFW call, applied once per scene switch (not per frame)
            if (props.VSync != vsyncCurrent)
            {
                vsyncCurrent = props.VSync;
                Glfw.SetSwapInterval(props.VSync ? 1 : 0);
                Console.WriteLine($"[OpenGL] VSync set to {(props.VSync ? "ON" : "OFF")}");
            }
        }

        /// <summary>
        /// Query GL_FRONT_FACE and GL_CULL_FACE state, log a warning if winding is not GL_CCW.
        /// Call once per frame at the start of the render loop to detect state corruption.
        /// Only logs on state change (not every frame) to avoid spam.
        /// </summary>
        private static int _lastFrontFaceWarn = -1;

        public static unsafe void CheckFrontFaceState()
        {
            int frontFace = 0;
            GL.GetIntegerv(Const.GL_FRONT_FACE, &frontFace);

            if (frontFace != Const.GL_CCW && frontFace != _lastFrontFaceWarn)
            {
                _lastFrontFaceWarn = frontFace;
                string windingName = frontFace switch
                {
                    0x0900 => "GL_CW (Clockwise)",
                    0x0901 => "GL_CCW (Counter-Clockwise)",
                    _ => $"0x{frontFace:X}",
                };
                string expected = Const.GL_CCW == 0x0901 ? "GL_CCW (0x0901)" : "?";
                Console.WriteLine($"[OpenGL] ⚠️ FrontFace state corruption detected! Current={windingName}, Expected={expected}");
                Console.WriteLine($"[OpenGL]   Call stack hint: something set FrontFace(GL_CW) without restoring to GL_CCW.");
            }
            else if (frontFace == Const.GL_CCW)
            {
                // Reset warning so a subsequent corruption re-triggers the log
                _lastFrontFaceWarn = -1;
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
