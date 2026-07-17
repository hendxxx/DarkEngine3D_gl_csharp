using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.Inputs
{
    public unsafe class Mouse
    {
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int, void> glfwSetInputMode;
        private static delegate* unmanaged[Cdecl]<IntPtr, double*, double*, void> glfwGetCursorPos;
        private static delegate* unmanaged[Cdecl]<IntPtr, double, double, void> glfwSetCursorPos; 
        private static delegate* unmanaged[Cdecl]<IntPtr, double, double, void> scrollCallback;
        private static delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, double, double, void>, void> glfwSetScrollCallback;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int> glfwGetMouseButton;


        static double lastX, lastY;
        private static double scrollX, scrollY;
        static bool firstMouse = true;
        public static float Sensitivity = 0.1f;
        private static nint window = 0;
        public static float ScrollY => (float)scrollY;
        public static float ScrollX => (float)scrollX;

        /// <summary>Get mouse position as screen coordinates.</summary>
        public static (float X, float Y) GetPosition()
        {
            double x = 0, y = 0;
            GetCursorPosition(out x, out y);
            return ((float)x, (float)y);
        }

        /// <summary>Check if a mouse button is currently down.</summary>
        public static bool IsButtonDown(int button)
        {
            if (window == 0) return false;
            return glfwGetMouseButton(window, button) == Const.GLFW_PRESS;
        }

        /// <summary>Get vertical scroll delta since last call, then reset.</summary>
        public static float GetScrollDeltaY()
        {
            float val = (float)scrollY;
            scrollY = 0;
            return val;
        }

        public static void ResetScroll()
        {
            scrollX = 0;
            scrollY = 0;
        }
        public static float DeltaX { get; private set; }
        public static float DeltaY { get; private set; }

        /// <summary>Reset the mouse state so the next Update call re-initializes without a delta jump.
        /// Call this when unpausing to discard menu mouse movements.</summary>
        public static void ResetState()
        {
            firstMouse = true;
            DeltaX = 0;
            DeltaY = 0;
        }
         


        /// <summary>Get the current cursor position in screen coordinates.</summary>
        public static unsafe void GetCursorPosition(out double x, out double y)
        {
            x = 0; y = 0;
            if (window == 0) return;
            double mx = 0, my = 0;
            glfwGetCursorPos(window, &mx, &my);
            x = mx;
            y = my;
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
            Sensitivity = _sensitivity;

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

            float offsetX = (float)(mouseX - lastX);
            float offsetY = (float)(mouseY - lastY);

            DeltaX = (float)(mouseX - lastX);
            DeltaY = (float)(mouseY - lastY);

            lastX = mouseX;
            lastY = mouseY;
            
            // Invert pitch for all modes so that moving mouse up looks up
            offsetY = -offsetY;

            // Invert yaw for all modes so that moving mouse right looks right
            offsetX = -offsetX;

            camera.Yaw += offsetX * Sensitivity;
            camera.Pitch += offsetY * Sensitivity;

            camera.UpdateVectors();
            
        }
        public static bool IsButtonPressed(int button)
        {
            if (window == 0) return false;
            return glfwGetMouseButton(window, button) == Const.GLFW_PRESS;
        }
        public static bool IsButtonDowm(int button)
        {
            if (window == 0) return false;
            return glfwGetMouseButton(window, button) == Const.GLFW_MOUSE_BUTTON_RIGHT;
        }


        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        public static void OnScroll(IntPtr window, double xoffset, double yoffset)
        {
            scrollX = xoffset;
            scrollY = yoffset;
        }

    }
}
