using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace DarkEngine3D_gl_csharp.Engine
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

        private static delegate* unmanaged[Cdecl]<IntPtr, int, int> glfwGetKey;
         
        private static delegate* unmanaged[Cdecl]<double> glfwGetTime;

        private static delegate* unmanaged[Cdecl]<IntPtr, byte*, void> glfwSetWindowTitle;
        private static delegate* unmanaged[Cdecl]<IntPtr, double*, double*, void> glfwSetCursorPos;

        private static delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, void>, void> glfwSetWindowSizeCallback;

        static nint window;
        static nint glfwLib;
        private static delegate* unmanaged[Cdecl]<IntPtr, int> glfwWindow;

        static float timer = 0;
        static int frameCount = 0;
         
        static float deltaTime = 0.0f;
        static float lastFrame = 0.0f;

        private static int _windowWidth;
        private static int _windowHeight; 
        
        public static int WindowWidth
        {
            get => _windowWidth;
            private set => _windowWidth = value;
        }

        public static int WindowHeight
        {
            get => _windowHeight;
            private set => _windowHeight = value;
        }
        public static float aspect = 1;
        public static void Init(int width, int height, string title)
        {
            glfwLib = NativeLibrary.Load("glfw3.dll");

            // Ambil alamat fungsi (Get Function Pointers)
            var glfwInit = Marshal.GetDelegateForFunctionPointer<glfwInitDelegate>(NativeLibrary.GetExport(glfwLib, "glfwInit"));
            var glfwCreateWindow = Marshal.GetDelegateForFunctionPointer<glfwCreateWindowDelegate>(NativeLibrary.GetExport(glfwLib, "glfwCreateWindow"));
            var glfwMakeContextCurrent = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(glfwLib, "glfwMakeContextCurrent");
            var glfwWindowShouldClose = (delegate* unmanaged[Cdecl]<IntPtr, int>)NativeLibrary.GetExport(glfwLib, "glfwWindowShouldClose");
            glfwGetKey = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)NativeLibrary.GetExport(glfwLib, "glfwGetKey");

            glfwSetWindowSizeCallback = (delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, void>, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowSizeCallback");
               
            glfwWindow = glfwWindowShouldClose;

            glfwGetTime = (delegate* unmanaged[Cdecl]<double>)NativeLibrary.GetExport(glfwLib, "glfwGetTime");
            
            glfwSetWindowTitle = (delegate* unmanaged[Cdecl]<IntPtr, byte*, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowTitle");

            glfwSetCursorPos= (delegate* unmanaged[Cdecl]<IntPtr, double*, double*, void>)NativeLibrary.GetExport(glfwLib, "glfwSetCursorPos");

            // Init window
            if (glfwInit() == 0) return;
            
            WindowWidth = width;
            WindowHeight = height;

            window = glfwCreateWindow(width, height, title, nint.Zero, nint.Zero);
            if (window == nint.Zero) return;

            glfwMakeContextCurrent(window);

            

            glfwSetWindowSizeCallback(window, &OnWindowResized);


        }
        public static nint GetglfwLib()
        {
            return glfwLib;
        }
        public static nint GetWindow()
        {
            return window;
        }

        // New accessor so Keyboard class can query key state
        public static int GetKey(nint windowHandle, int key)
        {
            return glfwGetKey((IntPtr)windowHandle, key);
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
        public static void OnWindowResized(IntPtr window, int width, int height)
        {
            // 1. Update area gambar GPU
            GL.Viewport(0, 0, width, height);

            // 2. Update variabel aspect ratio global (agar matriks proyeksi tidak gepeng)
            // Pastikan variabel 'aspect' bisa diakses dari sini
            aspect = (float)width / (float)height;
        }

        public static void ShowFPS(int mapSize, int chunkSize,float deltaTime,int renderedTris)
        {
            //FPS
            timer += deltaTime;
            frameCount++;

            if (timer >= 1.0f) // Setiap 1 detik
            {
                // Hitung total segitiga jika seluruh map dirender (untuk perbandingan)
                int totalMapTris = (mapSize / chunkSize) * (mapSize / chunkSize) * (chunkSize * chunkSize * 2);

                string title = $"DarkEngine3D | FPS: {frameCount} | Tris: {renderedTris:N0} / {totalMapTris:N0}";
                fixed (byte* pTitle = Encoding.UTF8.GetBytes(title + "\0"))
                {
                    glfwSetWindowTitle(window, pTitle);
                }
                frameCount = 0;
                timer = 0;
            }
        } 

        public static void Loop(nint glfwLib , Camera camera, Lights light, Object3D objTriangle, TerrainChunk gameTerrainChunk)
        {
            uint shaderProgram = Shader.GetShaderProgram();

            // Pastikan nama string "view" dan "projection" sama persis dengan yang ada di kode GLSL Anda
            int viewLocation = Shader.GetView();
            int projectionLocation = Shader.GetProjection();

            // Game Loop (Zero-GC)
            Console.WriteLine("Engine Running...");
            while (glfwWindow(window) == 0)
            { 
                GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
                GL.ClearColor(0.6f, 0.6f, 1.0f, 1.0f);

                deltaTime = Glfw.GetDeltaTime();

                camera.UpdateVectors();
                GL.UseProgram(shaderProgram);
                GL.BindVertexArray(0);

                int renderedTris = gameTerrainChunk.Render(camera, WindowWidth / WindowHeight, gameTerrainChunk.GetFrozenPlanes());
                  
                camera.SetViewAndProjection(viewLocation, projectionLocation);
           
                Keyboard.Update(glfwLib, window, camera, deltaTime, gameTerrainChunk);
                Mouse.Update(window, camera);


                objTriangle.Draw(deltaTime, window, 5.0f);

                light.Update();

                Glfw.ShowFPS(TerrainChunk.GetMapSize(), TerrainChunk.GetChunkSize(), deltaTime, renderedTris);

                Shader.Cleanup();

                OpenGL.SwapBuffer(glfwLib, window);
                OpenGL.PoolEvents(glfwLib);
            }

            Console.WriteLine("Engine Shutdown.");


        }
    }
}
