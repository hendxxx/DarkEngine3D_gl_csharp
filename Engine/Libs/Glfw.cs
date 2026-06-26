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
        private delegate void glClearColorDelegate(float r, float g, float b, float a);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void glClearDelegate(uint mask);

        private static delegate* unmanaged[Cdecl]<double> glfwGetTime;

        private static delegate* unmanaged[Cdecl]<IntPtr, byte*, void> glfwSetWindowTitle;

        private static delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, void>, void> glfwSetWindowSizeCallback;
        private static delegate* unmanaged[Cdecl]<IntPtr, int> glfwWindowShouldClose;

        static nint window;
        static nint glfwLib;

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

        public static void Init(string title, bool fullscreen = true)
        {
            glfwLib = NativeLibrary.Load("glfw3.dll");

            // Ambil alamat fungsi (Get Function Pointers)
            var glfwInit = Marshal.GetDelegateForFunctionPointer<glfwInitDelegate>(NativeLibrary.GetExport(glfwLib, "glfwInit"));
            var glfwCreateWindow = Marshal.GetDelegateForFunctionPointer<glfwCreateWindowDelegate>(NativeLibrary.GetExport(glfwLib, "glfwCreateWindow"));
            var glfwMakeContextCurrent = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(glfwLib, "glfwMakeContextCurrent");
            var glfwSetWindowPos = (delegate* unmanaged[Cdecl]<IntPtr, int, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowPos");

            glfwSetWindowSizeCallback = (delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, void>, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowSizeCallback");

            glfwWindowShouldClose = (delegate* unmanaged[Cdecl]<IntPtr, int>)NativeLibrary.GetExport(glfwLib, "glfwWindowShouldClose");
            glfwGetTime = (delegate* unmanaged[Cdecl]<double>)NativeLibrary.GetExport(glfwLib, "glfwGetTime");
            glfwSetWindowTitle = (delegate* unmanaged[Cdecl]<IntPtr, byte*, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowTitle");

            // Tambahkan pointer fungsi untuk monitor utama
            var glfwGetPrimaryMonitor = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(glfwLib, "glfwGetPrimaryMonitor");

            // Init window
            if (glfwInit() == 0) return;

            // Atur rasio di sini jika tidak dalam mode fullscreen
            if (!fullscreen)
            {
                window = glfwCreateWindow(_windowWidth, _windowHeight, title, nint.Zero, nint.Zero);
                if (window == nint.Zero) return;

            }
            else
            {
                // Tentukan monitor jika fullscreen, atau nint.Zero jika windowed
                nint monitor = fullscreen ? glfwGetPrimaryMonitor() : nint.Zero;

                window = glfwCreateWindow(_windowWidth, _windowHeight, title, monitor, nint.Zero);
                if (window == nint.Zero) return;
            }
            glfwSetWindowPos(window, 0, 0);

            glfwMakeContextCurrent(window);
            glfwSetWindowSizeCallback(window, &OnGlfwWindowResized);

        }
        public static nint GetglfwLib()
        {
            return glfwLib;
        }
        public static nint GetWindow()
        {
            return window;
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
        // Fungsi yang akan dipanggil saat resize
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        public static void OnGlfwWindowResized(IntPtr window, int width, int height)
        {
            // 1. Update area gambar GPU
            GL.Viewport(0, 0, width, height);

            // 2. Update variabel ukuran window global
            _windowWidth = width;
            _windowHeight = height;

            // 3. Fire event so scenes can update camera aspect ratio
            OnWindowResized?.Invoke(width, height);
        }

        public static void ShowFPS(float deltaTime, int renderedTris, int totalMapTris, string gameTime)
        {
            //FPS
            timer += deltaTime;
            frameCount++;

            if (timer >= 1.0f) // Setiap 1 detik
            {
                lastFPS = frameCount;
                // Format HUD Premium: 🕒 [ 06:05 ]  |  ⚡ FPS: 60  |  📐 TRIS: 1.2M / 4.0M
                string title = $"🕒 [ {gameTime} ]    ⚡ FPS: {lastFPS}    📐 TRIS: {renderedTris:N0} / {totalMapTris:N0}";

                fixed (byte* pTitle = Encoding.UTF8.GetBytes(title + "\0"))
                {
                    glfwSetWindowTitle(window, pTitle);
                }
                frameCount = 0;
                timer = 0;
            }
        }


         
    }
}
