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

        // Set initial viewport to actual framebuffer size (Glfw.Init updated the
        // size variables but couldn't call GL.Viewport because OpenGL wasn't loaded yet)
        GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);

        var glVersion = GL.GetString(Const.VERSION);
        Console.WriteLine($"Versi OpenGL aktif: {glVersion}");

        OpenGL.EnableDepthTest(true);
        OpenGL.EnableFaceCulling(true);

        // Init Shader
        Shader.Init();

        // Init Camera
        Camera camera = new(0, 0, 0, PlayerConfig.InitialHeading, 10,
            (float)Glfw.WindowWidth / Glfw.WindowHeight, (float)Math.PI / 4, 0.1f, 500.0f);
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
        
        // Create MainMenuScene first — user chooses Start Game to load the game
        MainMenuScene mainMenu = new(sceneManager, camera, light);

        // ═══════════════════════════════════════════════════
        // PHASE 3: MAIN LOOP (starts with MainMenuScene)
        // ═══════════════════════════════════════════════════

        // MainMenuScene renders the menu. "Start Game" switches to LoadingScene,
        // which loads assets and then switches to GameScene.
        sceneManager.Run(mainMenu);

        // Shutdown
        Console.WriteLine("Program Shutdown.");
    }
}