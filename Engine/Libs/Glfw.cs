using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;

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

        private static Camera? MainCamera;

        public static void Init(string title, bool fullscreen = true)
        {
            glfwLib = NativeLibrary.Load("glfw3.dll");

            // Ambil alamat fungsi (Get Function Pointers)
            var glfwInit = Marshal.GetDelegateForFunctionPointer<glfwInitDelegate>(NativeLibrary.GetExport(glfwLib, "glfwInit"));
            var glfwCreateWindow = Marshal.GetDelegateForFunctionPointer<glfwCreateWindowDelegate>(NativeLibrary.GetExport(glfwLib, "glfwCreateWindow"));
            var glfwMakeContextCurrent = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(glfwLib, "glfwMakeContextCurrent");
            var glfwWindowShouldClose = (delegate* unmanaged[Cdecl]<IntPtr, int>)NativeLibrary.GetExport(glfwLib, "glfwWindowShouldClose");
            var glfwSetWindowPos = (delegate* unmanaged[Cdecl]<IntPtr, int, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowPos");

            glfwSetWindowSizeCallback = (delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, void>, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowSizeCallback");

            glfwWindow = glfwWindowShouldClose;
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


        public static void SetMainCamera(Camera camera)
        {
            MainCamera = camera;
            MainCamera.UpdateAspectRatio((float)_windowWidth, (float)_windowHeight);
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

            // 2. Update aspek rasio kamera global jika sudah diinisialisasi
            MainCamera?.UpdateAspectRatio((float)width, (float)height);

            // 3. Update variabel ukuran window global Anda (opsional)
            _windowWidth = width;
            _windowHeight = height;

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

        public static void Loop(Texture[] skyTextures, Camera camera, Lights light, Object3D objTriangle, TerrainChunk gameTerrainChunk, Skybox skybox, HUD hud, RainManager rainManager, ObjectManager? objectManager = null)
        {
            uint shaderProgram = Shader.GetShaderProgram();
            int projectionLocation = GL.GetUniformLocation(shaderProgram, "projection");
            int viewLocation = GL.GetUniformLocation(shaderProgram, "view");

            PostProcessStack ppStack = new PostProcessStack(_windowWidth, _windowHeight);
            var rainOverlayPass = new RainOverlayPass(Shader.GetRainOverlayShaderProgram());
            var invertPass = new InvertPass(Shader.GetInvertPassShaderProgram()); 

            //ppStack.AddPass(rainOverlayPass);
            //ppStack.AddPass(invertPass);

            // Game Loop (Zero-GC)
            Console.WriteLine("Engine Running...");
            float time = 0f;
            while (glfwWindow(window) == 0)
            {
                deltaTime = Glfw.GetDeltaTime();
                time += deltaTime;

                ppStack.BindSceneFBO();
                GL.ClearColor(0.07f, 0.13f, 0.17f, 1.0f);


                GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);

                // 1. Update inputs (mouse + keyboard) BEFORE any rendering so camera is stable
                Mouse.Update(window, camera);
                Keyboard.Update(window, light, camera, deltaTime, gameTerrainChunk);

                // 2. Clamp camera height to terrain once per-frame (centralized)
                try
                {
                    var ml = gameTerrainChunk.GetMapLoader();
                    if (ml != null) camera.ClampToTerrain(ml, deltaTime);
                }
                catch { }

                // 3. Draw Skybox
                skybox.Draw(camera, light, deltaTime, skyTextures, gameTerrainChunk);

                //// 4. Ensure terrain shader has current view/projection uniforms bound 
                camera.SetViewAndProjection(viewLocation, projectionLocation);
                int renderedTris = 0;
                if (gameTerrainChunk != null)
                    renderedTris = gameTerrainChunk.Render(camera, gameTerrainChunk.GetFrozenPlanes());

                light.Update(deltaTime, camera.Position);

                if (objTriangle != null)
                    objTriangle.Draw(deltaTime, window, 5.0f);

                // ---- glTF Object Manager (autonomous wandering agents) ----
                if (objectManager != null)
                {

                    // Each character decides on its own whether to idle/walk/run, picks
                    // a direction, moves at a gait-matched speed, avoids the others, and
                    // stays on the terrain. Updated before drawing to avoid a 1-frame lag.
                    objectManager.UpdateAgents(deltaTime, gameTerrainChunk);
                    objectManager.Draw(camera, light);
                    objectManager.DrawHealthBars(camera, hud);   // health bars above heads

                }
                //float currentweatherMode = 1;// Keyboard.GetCurrentWeather(); // 0 = cerah, 1 = badai
                //rainOverlayPass.RainAmount = Helpers.ShaderHelpers.SmoothStep(0.7f, 1.0f, currentweatherMode);
                //uint sceneTexture = ppStack.SceneColorTex;
                //rainManager.UpdateAndDraw(camera, currentweatherMode, time, sceneTexture);
               
                // 2. jalankan semua postprocess pass
                ppStack.RunStack(_windowWidth, _windowHeight, time);
                 

                //hud.DrawBox(0, 0, WindowWidth, 200, new Vector3(0, 0, 0)); // Kotak Hitam
                // --- HUD SYSTEM ---
                int totalMapTris = TerrainChunk.GetTotalMapTriangles();
                string gTime = light.GetFormattedTime();
                Glfw.ShowFPS(deltaTime, renderedTris, totalMapTris, gTime);

                string title = $"🕒 [ {gTime} ]    ⚡ FPS: {lastFPS}    📐 TRIS: {renderedTris:N0} / {totalMapTris:N0}";

                hud.DrawText(title + " POS: " + camera.Position, 10, 60, new Vector3(1, 0, 0));
                hud.DrawText("a brown fox quickly jump over the lazy dog", 10, 90, new Vector3(0, 0, 0), new Vector3(1, 1, 1));
                hud.DrawText("`1234567890-=~!@#$%^&*()_+[]\\{}|;':\",./<>?", 10, 120, new Vector3(0, 0, 0), new Vector3(1, 0, 1));


                OpenGL.SwapBuffer(window);
                OpenGL.PollEvents();
            }

            Console.WriteLine("Engine Shutdown.");
        }


    }
}
