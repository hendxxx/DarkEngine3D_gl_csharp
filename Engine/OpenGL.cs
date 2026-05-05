using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine
{
    public class OpenGL
    {
        public static unsafe nint Init()
        {
            nint glLib = NativeLibrary.Load("opengl32.dll");

            // Mengisi secara manual satu per satu jauh lebih aman dari crash
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
            GL.UniformMatrix4fvPtr = GetProcAddress(glLib, "glUniformMatrix4fv");

            GL.VertexAttribPointerPtr = GetProcAddress(glLib, "glVertexAttribPointer");
            GL.EnableVertexAttribArrayPtr = GetProcAddress(glLib, "glEnableVertexAttribArray");
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

            return glLib;
        }
        public static unsafe void EnableDepthTest(bool active)
        {
            if (active)
            {
                GL.Enable(Const.GL_DEPTH_TEST);
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

        public static unsafe void SwapBuffer(nint glfwLib, nint window)
        {
            var glfwSwapBuffers = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(glfwLib, "glfwSwapBuffers");
            glfwSwapBuffers(window);

        }

        public static unsafe void PoolEvents(nint glfwLib)
        {
            var glfwPollEvents = (delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(glfwLib, "glfwPollEvents");
            glfwPollEvents();

        }
    }
}
