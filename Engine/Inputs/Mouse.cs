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
        private static nint window = 0;
        public static unsafe void Init(nint glfwLib, nint _window ,float _sensitivity = 0.1f)
        {

            glfwSetCursorPos = (delegate* unmanaged[Cdecl]<IntPtr, double, double, void>)NativeLibrary.GetExport(glfwLib, "glfwSetCursorPos");

            glfwGetCursorPos = (delegate* unmanaged[Cdecl]<IntPtr, double*, double*, void>)NativeLibrary.GetExport(glfwLib, "glfwGetCursorPos");

            glfwSetInputMode = (delegate* unmanaged[Cdecl]<IntPtr, int, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetInputMode");

            window = _window;

            lastX = Glfw.WindowWidth / 2;
            lastY = Glfw.WindowHeight / 2;
            firstMouse = true;
            sensitivity = _sensitivity;

            glfwSetCursorPos(window, lastX, lastY);

        }
        public static void ShowMouse(bool show)
        {
            if (show)
                glfwSetInputMode(window, Const.GLFW_CURSOR, Const.GLFW_CURSOR_NORMAL);
            else
                glfwSetInputMode(window, Const.GLFW_CURSOR, Const.GLFW_CURSOR_DISABLED);

        }
        public static unsafe void Update(nint window, Camera camera)
        {
            double mouseX, mouseY;
            glfwGetCursorPos(window, &mouseX, &mouseY);

            if (firstMouse)
            {
                lastX = mouseX;
                lastY = mouseY;
                firstMouse = false;
            }

            //float offsetX = (float)(mouseX - lastX);
            //float offsetY = (float)(lastY - mouseY);
            //inverted kayak di game2 AAA
            float offsetX = (float)(lastX - mouseX);
            float offsetY = (float)(mouseY-lastY); 


            lastX = mouseX;
            lastY = mouseY;

            camera.Yaw += offsetX * sensitivity;
            camera.Pitch += offsetY * sensitivity;

            // Clamp pitch
            if (camera.Pitch > 89.0f) camera.Pitch = 89.0f;
            if (camera.Pitch < -89.0f) camera.Pitch = -89.0f;

            // **WAJIB**: update arah kamera
            camera.UpdateVectors();
        }

    }
}
