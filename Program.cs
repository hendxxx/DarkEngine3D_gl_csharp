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
    // add -load=game.ing untuk auto load a scene on startup
    public static void Main(string[] args)
    {
        // ═══════════════════════════════════════════════════
        // PHASE 0: LOAD SETTINGS (before window creation)
        // ═══════════════════════════════════════════════════

        // Parse command line arguments
        string? startupLoadPath = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("-load=", StringComparison.OrdinalIgnoreCase))
            {
                startupLoadPath = arg["-load=".Length..];
                Console.WriteLine($"[Program] Command-line: -load={startupLoadPath}");
            }
        }

        var settings = SettingsSave.Load();

        // Initialize save system directories
        SaveManager.Init();

        // 🛠️ FIX #9: Use shared ResolutionConfig instead of magic numbers.
        // 0=1920x1080, 1=1280x720, 2=2560x1440
        Glfw.WindowWidth = ResolutionConfig.GetWidth(settings.Resolution);
        Glfw.WindowHeight = ResolutionConfig.GetHeight(settings.Resolution);

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

        Keyboard.IsFogActive = false;

        // Init Keyboard and Mouse
        Keyboard.Init(glfwLib);
        Mouse.Init(glfwLib, window);

        // ═══════════════════════════════════════════════════
        // PHASE 2: SCENE SYSTEM
        // ═══════════════════════════════════════════════════

        // ── Initialize IDE ──
        IDE ide = new(window);

        // Restore persisted input lock state from settings
        ide.Bridge.InGameActive = settings.InGameActive;

        SceneManager sceneManager = new();
        sceneManager.AttachIde(ide);

        // If -load= was specified on command line, load the file and enter in-game mode immediately
        if (!string.IsNullOrEmpty(startupLoadPath))
        {
            ide.EnterInGameModeFromStartup(startupLoadPath);
        }

        // ═══════════════════════════════════════════════════
        // PHASE 3: MAIN LOOP (starts with no scene — UI Editor mode)
        // ═══════════════════════════════════════════════════

        // Start with no scene running — the IDE's UI Editor is the main interface.
        // User creates/edits scenes via Scene Manager panel.
        // When user wants to test/play, they can switch to a game scene.
        // MainMenuScene, LoadingScene, and GameScene are available as examples
        // via the Scene Manager panel's Load button.
        sceneManager.Run(/* startScene = null — blank UI Editor mode */);

        // Shutdown
        Console.WriteLine("Program Shutdown.");
    }
}