using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Scene;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;

namespace DarkEngine3D_gl_csharp;

public unsafe class Program
{
    public static void Main()
    {
        // ═══════════════════════════════════════════════════
        // PHASE 1: ENGINE BOOTSTRAP (minimal initialization)
        // ═══════════════════════════════════════════════════

        //Glfw.WindowWidth = 2560;
        //Glfw.WindowHeight = 1440;
        Glfw.WindowWidth = 1920;
        Glfw.WindowHeight = 1080;

        // Init GLFW and Create Window
        Glfw.Init("My Native C# Engine", false);

        // Load Library GLFW
        IntPtr glfwLib = Glfw.GetglfwLib();
        IntPtr window = Glfw.GetWindow();

        // Load Library OpenGL
        OpenGL.Init();
        OpenGL.CacheGlfwFunctions(glfwLib);

        var glVersion = GL.GetString(Const.VERSION);
        Console.WriteLine($"Versi OpenGL aktif: {glVersion}");

        OpenGL.EnableDepthTest(true);
        OpenGL.EnableFaceCulling(true);

        // Init Shader
        Shader.Init();

        // Init Camera
        Camera camera = new(0, 0, 0, PlayerConfig.InitialHeading, 10,
            Glfw.WindowWidth / Glfw.WindowHeight, (float)Math.PI / 4, 0.1f, 500.0f);
        camera.CurrentMode = CameraMode.Orbit;

        Keyboard.IsFogActive = false;

        // Init Light
        Vector3 sunDirLoc = new(1.0f, 0.5f, 0.0f);
        Vector3 sunColorLoc = new(1.0f, 0.95f, 0.8f);
        Vector3 viewPosLoc = new(camera.Position.X, camera.Position.Y, camera.Position.Z);
        Lights light = new(sunDirLoc, sunColorLoc, viewPosLoc, "16:00");

        // Init Keyboard and Mouse
        Keyboard.Init(glfwLib);
        Mouse.Init(glfwLib, window);

        // Subscribe to resize event for camera aspect updates
        Glfw.OnWindowResized += (w, h) => camera.UpdateAspectRatio(w, h);
        Glfw.FireInitialResize();

        // ═══════════════════════════════════════════════════
        // PHASE 2: SCENE SYSTEM
        // ═══════════════════════════════════════════════════

        SceneManager sceneManager = new();
        
        // Create scenes — LoadingScene will load everything and switch to GameScene
        GameScene gameScene = new(camera, light);
        LoadingScene loadingScene = new(sceneManager, camera, light);

        // ═══════════════════════════════════════════════════
        // PHASE 3: MAIN LOOP (starts with LoadingScene)
        // ═══════════════════════════════════════════════════

        // LoadingScene.Enter() loads all assets synchronously (showing progress),
        // then switches to GameScene. SceneManager.Run() handles the game loop.
        sceneManager.Run(loadingScene);

        // Shutdown
        Console.WriteLine("Program Shutdown.");
    }
}