using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.IDE;
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
        // PHASE 0: LOAD SETTINGS (before window creation)
        // ═══════════════════════════════════════════════════
        var settings = SettingsSave.Load();

        // Initialize save system directories
        SaveManager.Init();

        // Apply resolution to window size
        // 0=1920x1080, 1=1280x720, 2=2560x1440
        Glfw.WindowWidth = settings.Resolution switch
        {
            1 => 1280,
            2 => 2560,
            _ => 1920,
        };
        Glfw.WindowHeight = settings.Resolution switch
        {
            1 => 720,
            2 => 1440,
            _ => 1080,
        };

        // Apply Shadow Quality to config immediately
        // 0=Low, 1=Medium, 2=High, 3=Ultra
        int sq = Math.Clamp(settings.ShadowQuality, 0, ShadowPresets.CascadeSizes.Length - 1);
        ShadowConfig.CascadeSizes = ShadowPresets.CascadeSizes[sq];

        // Apply Occlusion Mode to config immediately
        switch (settings.OcclusionMode)
        {
            case 0:
                OcclusionConfig.Mode = OcclusionMode.Software;
                OcclusionConfig.UseOcclusion = true;
                break;
            case 1:
                OcclusionConfig.Mode = OcclusionMode.HiZ;
                OcclusionConfig.UseOcclusion = true;
                break;
            default:
                OcclusionConfig.UseOcclusion = false;
                break;
        }

        // ═══════════════════════════════════════════════════
        // PHASE 1: ENGINE BOOTSTRAP (minimal initialization)
        // ═══════════════════════════════════════════════════

        // Init GLFW and Create Window (exclusive fullscreen or borderless fullscreen windowed from settings)
        // If BorderlessFullscreen is active, use borderless windowed mode sized to monitor work area
        // (work area excludes the taskbar so content isn't cut off).
        bool useBorderless = !settings.Fullscreen && settings.BorderlessFullscreen;
        Glfw.Init("My Native C# Engine", settings.Fullscreen, useBorderless);

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
        // Apply VSync after window creation
        Glfw.SetSwapInterval(settings.VSync ? 1 : 0);

        // Init Shader
        Shader.Init();

        // Init Camera
        Camera camera = new(0, 0, 0, PlayerConfig.InitialHeading, 10,
            (float)Glfw.WindowWidth / Glfw.WindowHeight, (float)Math.PI / 4, CameraConfig.CameraNearDist, CameraConfig.CameraFarDist);
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

        // ── Initialize IDE ──
        IDE ide = new(window);

        // Restore persisted input lock state from settings
        ide.Bridge.InGameActive = settings.InGameActive;

        SceneManager sceneManager = new();
        sceneManager.AttachIde(ide);

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