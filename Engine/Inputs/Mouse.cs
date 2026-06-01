using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace DarkEngine3D_gl_csharp.Engine.Inputs
{
    public unsafe class Mouse
    {
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int, void> glfwSetInputMode;
        private static delegate* unmanaged[Cdecl]<IntPtr, double*, double*, void> glfwGetCursorPos;
        private static delegate* unmanaged[Cdecl]<IntPtr, double, double, void> glfwSetCursorPos;
        private static delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, double, double, void>, void> SetScrollCallback;
        private static delegate* unmanaged[Cdecl]<IntPtr, double, double, void> scrollCallback;
        private static delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, double, double, void>, void> glfwSetScrollCallback;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int> glfwGetMouseButton;


        static double lastX, lastY;
        private static double scrollX, scrollY;
        static bool firstMouse = true;
        static float sensitivity = 0.1f;
        private static nint window = 0;
        public static float ScrollY => (float)scrollY;
        public static float ScrollX => (float)scrollX;

        public static void ResetScroll()
        {
            scrollX = 0;
            scrollY = 0;
        }


        public static unsafe void Init(nint glfwLib, nint _window ,float _sensitivity = 0.1f)
        {

            glfwSetCursorPos = (delegate* unmanaged[Cdecl]<IntPtr, double, double, void>)NativeLibrary.GetExport(glfwLib, "glfwSetCursorPos");

            glfwGetCursorPos = (delegate* unmanaged[Cdecl]<IntPtr, double*, double*, void>)NativeLibrary.GetExport(glfwLib, "glfwGetCursorPos");

            glfwSetInputMode = (delegate* unmanaged[Cdecl]<IntPtr, int, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetInputMode");
            glfwSetScrollCallback = (delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, double, double, void>, void>) NativeLibrary.GetExport(glfwLib, "glfwSetScrollCallback");
            glfwGetMouseButton = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)NativeLibrary.GetExport(glfwLib, "glfwGetMouseButton");


            window = _window;

            lastX = Glfw.WindowWidth / 2;
            lastY = Glfw.WindowHeight / 2;
            firstMouse = true;
            sensitivity = _sensitivity;

            glfwSetCursorPos(window, lastX, lastY);


            scrollCallback = &OnScroll;
            glfwSetScrollCallback(window, scrollCallback);

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

            float offsetX = (float)(lastX - mouseX);
            float offsetY = (float)(mouseY - lastY);

            lastX = mouseX;
            lastY = mouseY;
             
          
            camera.Yaw += offsetX * sensitivity;
            camera.Pitch += offsetY * sensitivity;

            camera.UpdateVectors();
            
        }
        public static bool IsButtonPressed(int button)
        {
            if (window == 0) return false;
            return glfwGetMouseButton(window, button) == Const.GLFW_PRESS;
        }


        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        public static void OnScroll(IntPtr window, double xoffset, double yoffset)
        {
            scrollX = xoffset;
            scrollY = yoffset;
        }

    }
}
