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

        static nint window;
        static nint glfwLib;
        private static delegate* unmanaged[Cdecl]<IntPtr, int> glfwWindow;

        static float timer = 0;
        static int frameCount = 0;
         
        static float deltaTime = 0.0f;
        static float lastFrame = 0.0f;

        private static int WindowWidth;
        private static int WindowHeight;

        public static void Init(int width, int height, string title)
        {
            glfwLib = NativeLibrary.Load("glfw3.dll");

            // Ambil alamat fungsi (Get Function Pointers)
            var glfwInit = Marshal.GetDelegateForFunctionPointer<glfwInitDelegate>(NativeLibrary.GetExport(glfwLib, "glfwInit"));
            var glfwCreateWindow = Marshal.GetDelegateForFunctionPointer<glfwCreateWindowDelegate>(NativeLibrary.GetExport(glfwLib, "glfwCreateWindow"));
            var glfwMakeContextCurrent = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(glfwLib, "glfwMakeContextCurrent");
            var glfwWindowShouldClose = (delegate* unmanaged[Cdecl]<IntPtr, int>)NativeLibrary.GetExport(glfwLib, "glfwWindowShouldClose");
            glfwGetKey = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)NativeLibrary.GetExport(glfwLib, "glfwGetKey");

            glfwWindow = glfwWindowShouldClose;

            glfwGetTime = (delegate* unmanaged[Cdecl]<double>)NativeLibrary.GetExport(glfwLib, "glfwGetTime");
            
            glfwSetWindowTitle = (delegate* unmanaged[Cdecl]<IntPtr, byte*, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowTitle");

            // Init window
            if (glfwInit() == 0) return;
            
            WindowWidth = width;
            WindowHeight = height;

            window = glfwCreateWindow(width, height, title, nint.Zero, nint.Zero);
            if (window == nint.Zero) return;

            glfwMakeContextCurrent(window);
             
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

        
        public static void ShowFPS(float deltaTime,int renderedTris)
        {
            //FPS
            timer += deltaTime;
            frameCount++;

            if (timer >= 1.0f) // Setiap 1 detik
            {
                // Hitung total segitiga jika seluruh map dirender (untuk perbandingan)
                int totalMapTris = (256 / 16) * (256 / 16) * (16 * 16 * 2);

                string title = $"DarkEngine3D | FPS: {frameCount} | Tris: {renderedTris:N0} / {totalMapTris:N0}";
                fixed (byte* pTitle = Encoding.UTF8.GetBytes(title + "\0"))
                {
                    glfwSetWindowTitle(window, pTitle);
                }
                frameCount = 0;
                timer = 0;
            }
        } 

        public static void Loop(nint glfwLib , Camera camera, Object3D objTriangle, uint shaderProgram, int viewLocation, int projectionLocation, TerrainManager terrainMan, Terrain gameTerrain)
        {


            OpenGL.EnableDepthTest(true);

            // Game Loop (Zero-GC)
            Console.WriteLine("Engine Running...");
            while (glfwWindow(window) == 0)
            {     
                deltaTime = Glfw.GetDeltaTime();

                Keyboard.Update(glfwLib, window, camera, deltaTime);
                Mouse.Update(window, camera);

                camera.UpdateVectors();

                GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
                GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);

                GL.UseProgram(shaderProgram);

                //terrain.Draw();
                int renderedTris = gameTerrain.Render(camera, 800f / 600f);
                //terrainMan.Update(shaderProgram,camera.Position);


                objTriangle.Draw(deltaTime, window);

                camera.SetViewAndProjection(WindowWidth, WindowHeight, viewLocation, projectionLocation);
                 
                OpenGL.SwapBuffer(glfwLib, window);
                OpenGL.PoolEvents(glfwLib);

                Glfw.ShowFPS(deltaTime, renderedTris);

                Shader.Cleanup();
            }

            Console.WriteLine("Engine Shutdown.");


        }
    }
}
