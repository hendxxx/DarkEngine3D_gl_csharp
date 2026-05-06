using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine
{
    public unsafe class Keyboard
    {
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int> glfwGetKey;
         
        static bool isWireframe = false;
        static bool f1Pressed = false;
        static float speedCam = 1.0f;
        static int lineVLoc = 0;
        static int linePLoc = 0;     // Lokasi uniform view & projection untuk shader garis
        static uint lineShaderProgram; // ID shader program untuk rendering garis
        static Vector3[]?frozenCorners = null; // Untuk menyimpan koordinat frustum yang di-freeze

        // state untuk tombol P (edge detection)
        static int prevPState = 0;
        static bool frozenMode = false;

        public static unsafe void Init(nint glfwLib, float _speedCam)
        {
            lineShaderProgram = Shader.GetLineShaderProgram();
            glfwGetKey = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)NativeLibrary.GetExport(glfwLib, "glfwGetKey");

            isWireframe = false;
            f1Pressed = false;

            speedCam = _speedCam;

            lineVLoc = GL.GetUniformLocation(lineShaderProgram, "view");
            linePLoc = GL.GetUniformLocation(lineShaderProgram, "projection");
        }

        public static unsafe void Update(nint glfwLib, nint window, Camera camera, float deltaTime, Terrain gameTerrain)
        { 
            // Tombol ESC untuk Keluar
            if (glfwGetKey(window, Const.GLFW_KEY_ESCAPE) == Const.GLFW_PRESS)
            {
                // Beritahu GLFW untuk menutup jendela
                var glfwSetWindowShouldClose = (delegate* unmanaged[Cdecl]<IntPtr, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowShouldClose");
                glfwSetWindowShouldClose(window, 1);
            }

            // F1 toggle wireframe (rising edge)
            int f1State = glfwGetKey(window, Const.GLFW_KEY_F1);
            if (f1State == Const.GLFW_PRESS)
            {
                if (!f1Pressed)
                {
                    isWireframe = !isWireframe;
                    GL.PolygonMode(Const.GL_FRONT_AND_BACK, isWireframe ? Const.GL_LINE : Const.GL_FILL);
                    f1Pressed = true;
                    Console.WriteLine(isWireframe ? "Wireframe Mode: ON" : "Wireframe Mode: OFF");
                }
            }
            else
            {
                f1Pressed = false;
            }

            // Movement
            float speed = speedCam * deltaTime;
            if (glfwGetKey(window, Const.GLFW_KEY_W) == Const.GLFW_PRESS) camera.Position += camera.Front * speed;
            if (glfwGetKey(window, Const.GLFW_KEY_S) == Const.GLFW_PRESS) camera.Position -= camera.Front * speed;
            if (glfwGetKey(window, Const.GLFW_KEY_A) == Const.GLFW_PRESS) camera.Position -= Vector3.Normalize(Vector3.Cross(camera.Front, camera.Up)) * speed;
            if (glfwGetKey(window, Const.GLFW_KEY_D) == Const.GLFW_PRESS) camera.Position += Vector3.Normalize(Vector3.Cross(camera.Front, camera.Up)) * speed;
             
            // P: toggle freeze frustum and set it into Terrain (rising edge)
            int pState = glfwGetKey(window, Const.GLFW_KEY_P);
            if (pState == Const.GLFW_PRESS && prevPState != Const.GLFW_PRESS)
            {
                // toggle frozen mode
                frozenMode = !frozenMode;
                if (frozenMode)
                {
                    Matrix4x4 view = camera.GetViewMatrix();
                    Matrix4x4 proj = Camera.GetProjectionMatrix(camera.GetAspect(), camera.foV, camera.nearDist, camera.farDist);
                    frozenCorners = Terrain.GetFrustumCorners(view, proj);
                    gameTerrain.SetFrozenFrustumCorners(frozenCorners); // PASS frozen corners to Terrain
                    gameTerrain.SetHighlightFrustumMatches(true);
                }
                else
                {
                    frozenCorners = null;
                    gameTerrain.ClearFrozenFrustumCorners();
                    gameTerrain.SetHighlightFrustumMatches(false);
                }
            }
            prevPState = pState;

            // NOTE: drawing of the frozen frustum lines is handled inside Terrain.Render now.
        }
    } 
}
