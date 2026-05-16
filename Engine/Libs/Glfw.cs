using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;
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

        static nint window;
        static nint glfwLib;
        private static delegate* unmanaged[Cdecl]<IntPtr, int> glfwWindow;

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

        public static void Init(string title)
        {
            glfwLib = NativeLibrary.Load("glfw3.dll");

            // Ambil alamat fungsi (Get Function Pointers)
            var glfwInit = Marshal.GetDelegateForFunctionPointer<glfwInitDelegate>(NativeLibrary.GetExport(glfwLib, "glfwInit"));
            var glfwCreateWindow = Marshal.GetDelegateForFunctionPointer<glfwCreateWindowDelegate>(NativeLibrary.GetExport(glfwLib, "glfwCreateWindow"));
            var glfwMakeContextCurrent = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(glfwLib, "glfwMakeContextCurrent");
            var glfwWindowShouldClose = (delegate* unmanaged[Cdecl]<IntPtr, int>)NativeLibrary.GetExport(glfwLib, "glfwWindowShouldClose");

            glfwSetWindowSizeCallback = (delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, void>, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowSizeCallback");

            glfwWindow = glfwWindowShouldClose;

            glfwGetTime = (delegate* unmanaged[Cdecl]<double>)NativeLibrary.GetExport(glfwLib, "glfwGetTime");
            glfwSetWindowTitle = (delegate* unmanaged[Cdecl]<IntPtr, byte*, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowTitle");

            // Init window
            if (glfwInit() == 0) return;

            window = glfwCreateWindow(_windowWidth, _windowHeight, title, nint.Zero, nint.Zero);
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

        public static void Loop(Texture[] skyTextures, Camera camera, Lights light, Object3D objTriangle, TerrainChunk gameTerrainChunk, Skybox skybox, HUD hud)
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
                GL.ClearColor(1.0f, 0.0f, 0.0f, 1.0f); // Paksa seluruh layar jadi MERAH

                deltaTime = Glfw.GetDeltaTime();

                // Draw Skybox first
                skybox.Draw(camera, light, deltaTime, skyTextures);

                GL.UseProgram(shaderProgram);
                GL.BindVertexArray(0);

                int renderedTris = gameTerrainChunk.Render(camera, deltaTime, WindowWidth / WindowHeight, gameTerrainChunk.GetFrozenPlanes());

                camera.SetViewAndProjection(viewLocation, projectionLocation);

                Keyboard.Update(window, camera, deltaTime, gameTerrainChunk);

                Mouse.Update(window, camera);

                objTriangle.Draw(deltaTime, window, 5.0f);

                // --- TIME SYSTEM ---
                float baseSpeed = 0.0043f; // Normal: 1 real second = 1 game minute
                float manualMultiplier = 60.0f; // Fast Forward: 1 real second = 1 game hour
                
                // Always progress time slowly
                light.WorldTime += deltaTime * baseSpeed;

                // Manual Override / Fast Forward
                if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_EQUAL)) 
                    light.WorldTime += deltaTime * baseSpeed * manualMultiplier;
                if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_MINUS)) 
                    light.WorldTime -= deltaTime * baseSpeed * manualMultiplier;
                
                light.Update(camera.Position);
                // --------------------

                camera.UpdateVectors();

                // --- HUD SYSTEM ---
                int totalMapTris = TerrainChunk.GetTotalMapTriangles();
                string gTime = light.GetFormattedTime();
                Glfw.ShowFPS(deltaTime, renderedTris, totalMapTris, gTime);


                // 1. Gambar Background (Box)
                //hud.DrawBox(0, 0, WindowWidth, 200, new Vector3(0, 0, 0)); // Kotak Hitam

                // 2. Gambar Tulisan di atasnya
                //hud.DrawText("Halo Dunia", 20, 30, new Vector3(1, 1, 1)); // Teks Putih

                hud.DrawText("TIME: " + gTime + "| FPS: " + Glfw.GetLastFPS() + " | POS: " + camera.Position , 10, 60, new Vector3(1, 0, 0));
                hud.DrawText("a brown fox quickly jump over the lazy dog", 10, 90, new Vector3(0, 0, 0), new Vector3(1, 1, 1));
                hud.DrawText("`1234567890-=~!@#$%^&*()_+[]\\{}|;':\",./<>?", 10, 120, new Vector3(0, 0, 0), new Vector3(1, 0, 1));



                // Static Console HUD
                Console.Write("\x1b[H"); 
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("========================================================================");
                Console.WriteLine($"  [ DARK ENGINE 3D ]   TIME: {gTime}   FPS: {Glfw.GetLastFPS()}   TRIS: {renderedTris:N0}  ");
                Console.WriteLine("========================================================================");
                Console.ResetColor();

                OpenGL.SwapBuffer(window);
                OpenGL.PollEvents();
            }

            Console.WriteLine("Engine Shutdown.");
        }
    }
}
