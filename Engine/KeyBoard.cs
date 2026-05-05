using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine
{
    public unsafe class KeyBoard
    {
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int> glfwGetKey;
         
        static bool isWireframe = false;
        static bool f1Pressed = false;

        public static unsafe void Init(nint glfwLib )
        {
            glfwGetKey = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)NativeLibrary.GetExport(glfwLib, "glfwGetKey");

            isWireframe = false;
            f1Pressed = false;
             
        }

        public static unsafe void Update(nint glfwLib, nint window, Camera camera, float deltaTime)
        { 

            // Tombol ESC untuk Keluar
            if (glfwGetKey(window, Const.GLFW_KEY_ESCAPE) == Const.GLFW_PRESS)
            {
                // Beritahu GLFW untuk menutup jendela
                var glfwSetWindowShouldClose = (delegate* unmanaged[Cdecl]<IntPtr, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowShouldClose");
                glfwSetWindowShouldClose(window, 1);
            }

            // Di dalam loop while
            if (glfwGetKey(window, Const.GLFW_KEY_F1) == 1) // 1 = GLFW_PRESS
            {
                if (!f1Pressed) // Hanya eksekusi sekali saat tombol mulai ditekan
                {
                    isWireframe = !isWireframe;
                    GL.PolygonMode(Const.GL_FRONT_AND_BACK, isWireframe ? Const.GL_LINE : Const.GL_FILL);
                    f1Pressed = true;
                    Console.WriteLine(isWireframe ? "Wireframe Mode: ON" : "Wireframe Mode: OFF");
                }
            }
            else
            {
                f1Pressed = false; // Reset saat tombol dilepas
            }

            // Kecepatan sekarang dikalikan deltaTime (misal: 2.5 unit per detik)
            float speed = 2.5f * deltaTime;

            // Logic & Input di sini 
            if (glfwGetKey(window, Const.GLFW_KEY_W) == Const.GLFW_PRESS) camera.Position += camera.Front * speed;
            if (glfwGetKey(window, Const.GLFW_KEY_S) == Const.GLFW_PRESS) camera.Position -= camera.Front * speed;
            if (glfwGetKey(window, Const.GLFW_KEY_A) == Const.GLFW_PRESS) camera.Position -= Vector3.Normalize(Vector3.Cross(camera.Front, camera.Up)) * speed;
            if (glfwGetKey(window, Const.GLFW_KEY_D) == Const.GLFW_PRESS) camera.Position += Vector3.Normalize(Vector3.Cross(camera.Front, camera.Up)) * speed;


        }
    } 
}
