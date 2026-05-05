using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine
{
    public unsafe class Keyboard
    {
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int> glfwGetKey;
         
        static bool isWireframe = false;
        static bool f1Pressed = false;
        static int lineVLoc = 0;
        static int linePLoc =0;     // Lokasi uniform view & projection untuk shader garis
        static uint lineShaderProgram; // ID shader program untuk rendering garis
        static Vector3[] frozenCorners = null; // Untuk menyimpan koordinat frustum yang di-freeze

        public static unsafe void Init(nint glfwLib, uint _lineShaderProgram )
        {
            lineShaderProgram = _lineShaderProgram;
            glfwGetKey = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)NativeLibrary.GetExport(glfwLib, "glfwGetKey");

            isWireframe = false;
            f1Pressed = false;

            lineVLoc = GL.GetUniformLocation(lineShaderProgram, "view");
            linePLoc = GL.GetUniformLocation(lineShaderProgram, "projection");

        }

        public static unsafe void Update(nint glfwLib, nint window, Camera camera, float deltaTime, float aspect, Terrain gameTerrain, uint lineShaderProgram)
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
            float speed = 10.0f * deltaTime;

            // Logic & Input di sini 
            if (glfwGetKey(window, Const.GLFW_KEY_W) == Const.GLFW_PRESS) camera.Position += camera.Front * speed;
            if (glfwGetKey(window, Const.GLFW_KEY_S) == Const.GLFW_PRESS) camera.Position -= camera.Front * speed;
            if (glfwGetKey(window, Const.GLFW_KEY_A) == Const.GLFW_PRESS) camera.Position -= Vector3.Normalize(Vector3.Cross(camera.Front, camera.Up)) * speed;
            if (glfwGetKey(window, Const.GLFW_KEY_D) == Const.GLFW_PRESS) camera.Position += Vector3.Normalize(Vector3.Cross(camera.Front, camera.Up)) * speed;

            if (glfwGetKey(window, 80) == 1) // 80 adalah GLFW_KEY_P
            { 
                Matrix4x4 view = camera.GetViewMatrix();
                Matrix4x4 proj = camera.GetProjectionMatrix(aspect);

                frozenCorners = gameTerrain.GetFrustumCorners(view,proj);
                
            }

            if (frozenCorners != null)
            {
                // Matikan Depth Test agar garis terlihat menembus tanah (X-ray mode)
                GL.Disable(0x0B71);
                GL.UseProgram(lineShaderProgram); // <--- WAJIB: Ganti ke shader garis
                GL.Enable(Const.GL_DEPTH_CLAMP);
                gameTerrain.RenderFrustumDebug(frozenCorners, lineShaderProgram, lineVLoc, linePLoc, camera, aspect);
                GL.Enable(0x0B71);
            }


        }
    } 
}
