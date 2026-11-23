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
            _cursorShown = null;   // a (re)created window starts in an unknown cursor mode

            lastX = Glfw.WindowWidth / 2;
            lastY = Glfw.WindowHeight / 2;
            firstMouse = true;
            Sensitivity = _sensitivity;

            glfwSetCursorPos(window, lastX, lastY);


            scrollCallback = &OnScroll;
            glfwSetScrollCallback(window, scrollCallback);

        } 

        /// <summary>Last state applied to GLFW (null = never applied). Several per-frame paths
        /// call ShowMouse, so it MUST be idempotent: re-applying it every frame would re-centre
        /// the pointer and zero the delta stream, which silently kills mouse-look.</summary>
        private static bool? _cursorShown;

        /// <summary>True when the OS cursor is currently visible (last mode applied was
        /// GLFW_CURSOR_NORMAL). Read by cursor owners that must restore a state they did
        /// not set themselves.</summary>
        public static bool CursorShown => _cursorShown ?? true;

        /// <summary>Show/hide the OS cursor. BOTH states park the pointer in the CENTRE of the
        /// window: while hidden that is where GLFW keeps its virtual cursor (so the delta stream
        /// starts at zero), and showing it again makes the cursor appear in the middle of the
        /// screen instead of at whatever spot it was last left.</summary>
        public static void ShowMouse(bool show)
        {
            if (window == 0) return;
            if (_cursorShown == show) return;
            _cursorShown = show;

            glfwSetInputMode(window, Const.GLFW_CURSOR,
                show ? Const.GLFW_CURSOR_NORMAL : Const.GLFW_CURSOR_DISABLED);
            CentreCursor();
        }

        /// <summary>Park the pointer in the middle of the window and restart the delta stream
        /// from there. Used by the camera-preview look: it re-centres EVERY frame, so the
        /// captured cursor never drifts and the ImGui mouse position stays on screen.</summary>
        public static void CentreCursor()
        {
            double cx = Glfw.WindowWidth * 0.5;
            double cy = Glfw.WindowHeight * 0.5;
            if (cx <= 0 || cy <= 0) return;
            glfwSetCursorPos(window, cx, cy);
            lastX = cx;
            lastY = cy;
            firstMouse = true;
            DeltaX = 0;
            DeltaY = 0;
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
