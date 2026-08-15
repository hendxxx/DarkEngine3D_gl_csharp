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

        // Apply all persisted shadow settings immediately (quality preset, cascade splits
        // and every bias/blend/filter value tuned in the IDE Shadow Settings panel).
        ShadowSettings.Apply(settings);

        // Write-through: persist the (possibly default) shadow values back to settings.json
        // so the file always contains the full shadow field set from the very first run.
        // (Previously the fields only appeared after the first change in the Shadow panel.)
        ShadowSettings.Persist();

        // Restore the global fog settings (Inspector "Fog" section).
        FogSettings.Apply(settings);

        // Restore the post-processing settings (bloom / tonemapping / gamma) and
        // write them through so settings.json always carries the full field set.
        PostFxSettings.Apply(settings);
        PostFxSettings.Persist();

        // Restore the unified quality preset (MSAA + shadow quality + shadow filter).
        // Runs after ShadowSettings so the preset's shadow level wins, and writes
        // through so settings.json always carries the field.
        QualitySettings.Apply(settings);
        QualitySettings.Persist();

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

        // Restore viewport grid/snap prefs so the grid on/off + snap values
        // stay consistent across restarts (previously reset to defaults).
        ide.Bridge.ShowDebugGrid = settings.ShowDebugGrid;
        ide.Bridge.ShowShadows = settings.ShowShadows;
        ide.SetViewportSnap(settings.SnapEnabled, settings.SnapGridSize);

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