using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.Inputs
{
    public unsafe class Mouse
    {
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int, void> glfwSetInputMode;
        private static delegate* unmanaged[Cdecl]<IntPtr, double*, double*, void> glfwGetCursorPos;
        private static delegate* unmanaged[Cdecl]<IntPtr, double, double, void> glfwSetCursorPos;

        static double lastX, lastY;
        static bool firstMouse = true;
        static float sensitivity = 0.1f;

        public static unsafe void Init(nint glfwLib, nint window ,float _sensitivity = 0.1f)
        {

            glfwSetCursorPos = (delegate* unmanaged[Cdecl]<IntPtr, double, double, void>)NativeLibrary.GetExport(glfwLib, "glfwSetCursorPos");

            glfwGetCursorPos = (delegate* unmanaged[Cdecl]<IntPtr, double*, double*, void>)NativeLibrary.GetExport(glfwLib, "glfwGetCursorPos");

            glfwSetInputMode = (delegate* unmanaged[Cdecl]<IntPtr, int, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetInputMode");

            glfwSetInputMode(window, Const.GLFW_CURSOR, Const.GLFW_CURSOR_DISABLED);
             

            lastX = Glfw.WindowWidth / 2;
            lastY = Glfw.WindowHeight / 2;
            firstMouse = true;
            sensitivity = _sensitivity;

            glfwSetCursorPos(window, lastX, lastY);

        }

        public static unsafe void Update(nint window, Camera camera)
        {

            double mouseX, mouseY;
            glfwGetCursorPos(window, &mouseX, &mouseY);

            if (firstMouse)
            {
                lastX = mouseX; lastY = mouseY;
                firstMouse = false;
            }

            float offsetX = (float)(mouseX - lastX);
            float offsetY = (float)(lastY - mouseY); // Terbalik karena koordinat Y GLFW dari atas ke bawah
            lastX = mouseX; lastY = mouseY;

            camera.Yaw += offsetX * sensitivity;
            camera.Pitch += offsetY * sensitivity;

            // Batasi agar tidak bisa menoleh ke belakang (salto)
            if (camera.Pitch > 89.0f) camera.Pitch = 89.0f;
            if (camera.Pitch < -89.0f) camera.Pitch = -89.0f;


        }
    }
}
