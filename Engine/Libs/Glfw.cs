using DarkEngine3D_gl_csharp.Engine.Scene;
using System.Runtime.InteropServices;
using System.Text;

namespace DarkEngine3D_gl_csharp.Engine.Libs
{

    public unsafe class Glfw
    {
        // Delegasi untuk fungsi GLFW
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte glfwInitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint glfwCreateWindowDelegate(int width, int height, string title, nint monitor, nint share);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] 
        private delegate nint glfwMaximizeWindowDelegate(IntPtr window, int maximized);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void glClearColorDelegate(float r, float g, float b, float a);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void glClearDelegate(uint mask);

        private static delegate* unmanaged[Cdecl]<double> glfwGetTime;

        private static delegate* unmanaged[Cdecl]<IntPtr, byte*, void> glfwSetWindowTitle;

        private static delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, void>, void> glfwSetWindowSizeCallback;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int, void> glfwSetWindowSize;
        private static delegate* unmanaged[Cdecl]<int, void> glfwSwapInterval;
        private static delegate* unmanaged[Cdecl]<IntPtr, nint, int, int, int, int, int, void> glfwSetWindowMonitor;
        private static delegate* unmanaged[Cdecl]<IntPtr, int> glfwWindowShouldClose;
        private static delegate* unmanaged[Cdecl]<IntPtr, out int, out int, void> glfwGetFramebufferSize;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int, void> glfwSetWindowPos;

        private static delegate* unmanaged[Cdecl]<nint> glfwGetPrimaryMonitor;
        private static delegate* unmanaged[Cdecl]<nint, out int, out int, out int, out int, void> glfwGetMonitorWorkarea;
        private static delegate* unmanaged[Cdecl]<int, int, void> glfwWindowHint;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int, void> glfwSetWindowAttrib;

        static nint window;
        static nint glfwLib;

        public static bool IsFullscreen { get; private set; } = true;
        public static bool IsBorderless { get; private set; } = false;

        // ── Resize event (scenes subscribe to update camera aspect ratio) ──
        public static event Action<int, int>? OnWindowResized;

        static float timer = 0;
        static int frameCount = 0;
        static int lastFPS = 0;
        public static int GetLastFPS() => lastFPS;

        static float deltaTime = 0.0f;
        static float lastFrame = 0.0f;

        private static int _windowWidth;
        private static int _windowHeight;

        public static int WindowWidth
        {
            get => _windowWidth;
            set => _windowWidth = value;
        }

        public static int WindowHeight
        {
            get => _windowHeight;
            set => _windowHeight = value;
        }

        /// <summary>Return 0 if the window should keep running, non-zero if close requested.</summary>
        public static int GetWindowShouldClose(nint window)
        {
            return glfwWindowShouldClose(window);
        }

        public static void Init(string title, bool fullscreen = true, bool borderless = false)
        {
            glfwLib = NativeLibrary.Load("glfw3.dll");

            // Ambil alamat fungsi (Get Function Pointers)
            var glfwInit = Marshal.GetDelegateForFunctionPointer<glfwInitDelegate>(NativeLibrary.GetExport(glfwLib, "glfwInit"));
            var glfwCreateWindow = Marshal.GetDelegateForFunctionPointer<glfwCreateWindowDelegate>(NativeLibrary.GetExport(glfwLib, "glfwCreateWindow"));
            var glfwMaximizeWindow = Marshal.GetDelegateForFunctionPointer<glfwMaximizeWindowDelegate>(NativeLibrary.GetExport(glfwLib, "glfwMaximizeWindow"));
            var glfwMakeContextCurrent = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(glfwLib, "glfwMakeContextCurrent");

            glfwSetWindowSizeCallback = (delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, void>, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowSizeCallback");
            glfwSetWindowSize = (delegate* unmanaged[Cdecl]<IntPtr, int, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowSize");
            glfwSwapInterval = (delegate* unmanaged[Cdecl]<int, void>)NativeLibrary.GetExport(glfwLib, "glfwSwapInterval");
            glfwSetWindowMonitor = (delegate* unmanaged[Cdecl]<IntPtr, nint, int, int, int, int, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowMonitor");
            glfwGetFramebufferSize = (delegate* unmanaged[Cdecl]<IntPtr, out int, out int, void>)NativeLibrary.GetExport(glfwLib, "glfwGetFramebufferSize");
            glfwSetWindowPos = (delegate* unmanaged[Cdecl]<IntPtr, int, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowPos");

            glfwWindowShouldClose = (delegate* unmanaged[Cdecl]<IntPtr, int>)NativeLibrary.GetExport(glfwLib, "glfwWindowShouldClose");
            glfwGetTime = (delegate* unmanaged[Cdecl]<double>)NativeLibrary.GetExport(glfwLib, "glfwGetTime");
            glfwSetWindowTitle = (delegate* unmanaged[Cdecl]<IntPtr, byte*, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowTitle");

            // Pointer fungsi untuk monitor utama, work area, & window hints
            glfwGetPrimaryMonitor = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(glfwLib, "glfwGetPrimaryMonitor");
            glfwGetMonitorWorkarea = (delegate* unmanaged[Cdecl]<nint, out int, out int, out int, out int, void>)NativeLibrary.GetExport(glfwLib, "glfwGetMonitorWorkarea");
            glfwWindowHint = (delegate* unmanaged[Cdecl]<int, int, void>)NativeLibrary.GetExport(glfwLib, "glfwWindowHint");
            glfwSetWindowAttrib = (delegate* unmanaged[Cdecl]<IntPtr, int, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowAttrib");

            // Init window
            if (glfwInit() == 0) return;

            glfwWindowHint(Const.GLFW_MAXIMIZED, Const.GLFW_TRUE);
            // ── Borderless fullscreen windowed: size to monitor work area, no decorations ──
            if (borderless && !fullscreen)
            {
                glfwWindowHint(Const.GLFW_DECORATED, Const.GLFW_FALSE);

                var wa = GetMonitorWorkArea();
                _windowWidth = wa.Width;
                _windowHeight = wa.Height;

                window = glfwCreateWindow(_windowWidth, _windowHeight, title, nint.Zero, nint.Zero);
                if (window == nint.Zero) return;

                glfwSetWindowPos(window, wa.X, wa.Y);
                IsBorderless = true;
            }
            else if (!fullscreen)
            {
                // Normal windowed mode with decorations
                window = glfwCreateWindow(_windowWidth, _windowHeight, title, nint.Zero, nint.Zero);
                if (window == nint.Zero) return;
                glfwSetWindowPos(window, 0, 0);
                IsBorderless = false;
            }
            else
            {
                // Exclusive fullscreen — window hints don't matter (GLFW removes decorations automatically)
                nint monitor = glfwGetPrimaryMonitor();
                window = glfwCreateWindow(_windowWidth, _windowHeight, title, monitor, nint.Zero);
                if (window == nint.Zero) return;
                glfwSetWindowPos(window, 0, 0);
                IsBorderless = false;
            }

            glfwMakeContextCurrent(window);
            glfwSetWindowSizeCallback(window, &OnGlfwWindowResized);

            // Query actual framebuffer size — GLFW may choose a different resolution
            // than requested (especially in fullscreen on some monitors).
            // Note: we cannot call GL.Viewport here because OpenGL function pointers
            // have not been loaded yet (OpenGL.Init() is called after Glfw.Init()).
            // Just update the size variables; the viewport will be set explicitly
            // in Program.cs after OpenGL.Init().
            glfwGetFramebufferSize(window, out int fbWidth, out int fbHeight);
            if (fbWidth > 0 && fbHeight > 0)
            {
                _windowWidth = fbWidth;
                _windowHeight = fbHeight;
            }

            IsFullscreen = fullscreen;
            Console.WriteLine($"[Glfw] Init: {_windowWidth}x{_windowHeight}, fullscreen={fullscreen}, borderless={IsBorderless}");
        }
        public static nint GetglfwLib()
        {
            return glfwLib;
        }
        public static nint GetWindow()
        {
            return window;
        }

        /// <summary>Get the primary monitor's work area (screen area minus taskbar/docks).</summary>
        public static (int X, int Y, int Width, int Height) GetMonitorWorkArea()
        {
            nint monitor = glfwGetPrimaryMonitor();
            glfwGetMonitorWorkarea(monitor, out int x, out int y, out int w, out int h);
            return (x, y, w, h);
        }

        /// <summary>
        /// Switch to borderless fullscreen windowed mode using the monitor work area
        /// (excludes the taskbar so content isn't cut off).
        /// </summary>
        public static void SetBorderlessFullscreen()
        {
            var wa = GetMonitorWorkArea();
            _windowWidth = wa.Width;
            _windowHeight = wa.Height;

            // Remove decorations at runtime via glfwSetWindowAttrib (GLFW 3.3+)
            glfwSetWindowAttrib(window, Const.GLFW_DECORATED, Const.GLFW_FALSE);

            // Remove from fullscreen monitor (if in exclusive fullscreen) and reposition
            glfwSetWindowMonitor(window, nint.Zero, wa.X, wa.Y, wa.Width, wa.Height, Const.GLFW_DONT_CARE);
            IsFullscreen = false;
            IsBorderless = true;

            Console.WriteLine($"[Glfw] Borderless fullscreen windowed: {wa.Width}x{wa.Height} at ({wa.X},{wa.Y})");
        }

        public static void SetWindowPosition(int x, int y)
        {
            glfwSetWindowPos(window, x, y);
        }
        public static void SetWindowSize(int width, int height)
        {
            glfwSetWindowSize(window, width, height);
        }

        public static void SetSwapInterval(int interval)
        {
            glfwSwapInterval(interval);
        }

        /// <summary>Toggle between fullscreen and windowed mode at the current resolution.</summary>
        public static void SetFullscreen(bool fullscreen)
        {
            if (fullscreen)
            {
                nint monitor = glfwGetPrimaryMonitor();
                glfwSetWindowMonitor(window, monitor, 0, 0, _windowWidth, _windowHeight, Const.GLFW_DONT_CARE);
                IsBorderless = false;
            }
            else
            {
                // Switch to windowed — position at (100, 100) with current resolution
                glfwSetWindowMonitor(window, nint.Zero, 100, 100, _windowWidth, _windowHeight, Const.GLFW_DONT_CARE);
                IsBorderless = false;
            }
            IsFullscreen = fullscreen;
        }

        /// <summary>Quick-toggle fullscreen — useful for Alt+Enter shortcut.</summary>
        public static void ToggleFullscreen()
        {
            SetFullscreen(!IsFullscreen);
        }

        /// <summary>
        /// Fire the resize event so subscribers (scenes) can update camera aspect ratio.
        /// Called by external code after camera is created.
        /// </summary>
        public static void FireInitialResize()
        {
            OnWindowResized?.Invoke(_windowWidth, _windowHeight);
        }
        public static float GetDeltaTime()
        {

            float currentFrame = (float)glfwGetTime();


            deltaTime = currentFrame - lastFrame;
            lastFrame = currentFrame;

            return deltaTime;
        }
        /// <summary>Internal helper: update GL viewport, window size vars, and fire resize event.</summary>
        private static void UpdateWindowSize(int width, int height)
        {
            GL.Viewport(0, 0, width, height);
            _windowWidth = width;
            _windowHeight = height;
            OnWindowResized?.Invoke(width, height);
        }

        /// <summary>GLFW resize callback. Called by GLFW when the window is resized.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        public static void OnGlfwWindowResized(IntPtr window, int width, int height)
        {
            UpdateWindowSize(width, height);
        }

        public static void ShowFPS(float deltaTime, int renderedTris, int totalMapTris, string gameTime)
        {
            //FPS
            timer += deltaTime;
            frameCount++;

            if (timer >= 1.0f) // Setiap 1 detik
            {
                lastFPS = frameCount;
                 
                frameCount = 0;
                timer = 0;
            }
        }


         
    }
}
