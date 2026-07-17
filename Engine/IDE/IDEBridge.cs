using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Scene;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE;

/// <summary>
/// Provides engine data to IDE panels without coupling them to engine internals.
/// The engine (GameScene, MainMenuScene, etc.) populates this each frame.
/// </summary>
public class IDEBridge
{
    // ── Scene info ──
    public string SceneName { get; set; } = "GameScene";
    public int TotalObjects { get; set; }
    public int DrawnObjects { get; set; }
    public int TotalTriangles { get; set; }
    public int RenderedTriangles { get; set; }
    public int AnimatedObjectCount { get; set; }
    public int StaticObjectCount { get; set; }
    public float Fps { get; set; }
    public float FrameMs { get; set; }

    // ── Selected object for inspector ──
    public GltfObject? SelectedObject { get; set; }
    public CharacterAgent? SelectedAgent { get; set; }

    // ── UI element selection (for MainMenu/Loading scenes) ──
    /// <summary>Currently selected UI element in the hierarchy tree.</summary>
    public UIElement? SelectedUIElement { get; set; }
    /// <summary>Legacy: flat list of UI buttons (kept for backward compat).</summary>
    public IReadOnlyList<UIButtonData>? SceneUIButtons { get; set; }
    /// <summary>Root-level UI elements in the current scene, for hierarchy display.</summary>
    public IReadOnlyList<UIElement>? SceneRootElements { get; set; }

    // ── All agents (for hierarchy/selection) ──
    public IReadOnlyList<CharacterAgent>? AllAgents { get; set; }

    // ── Camera info ──
    public Camera? Camera { get; set; }
    public Vector3 CameraPosition { get; set; }
    public float CameraYaw { get; set; }
    public float CameraPitch { get; set; }

    // ── Object list (for hierarchy) ──
    public IReadOnlyList<GltfObject>? AllObjects { get; set; }

    // ── Scene texture (for Viewport panel) ──
    public uint SceneTextureID { get; set; }
    public int SceneTextureWidth { get; set; }
    public int SceneTextureHeight { get; set; }

    // ── Viewport focus state (set by ViewportPanel each frame) ──
    public bool IsViewportFocused { get; set; }

    // ── Viewport mouse/click state for click-to-select in IDE mode ──
    /// <summary>Set by ViewportPanel when the scene image is clicked.</summary>
    public bool IsViewportClicked { get; set; }
    /// <summary>Scene-space pixel X of the viewport click.</summary>
    public float ViewportClickX { get; set; }
    /// <summary>Scene-space pixel Y of the viewport click.</summary>
    public float ViewportClickY { get; set; }
    /// <summary>Scene-space pixel X of the current mouse position over the viewport (set each frame).</summary>
    public float ViewportMouseX { get; set; }
    /// <summary>Scene-space pixel Y of the current mouse position over the viewport (set each frame).</summary>
    public float ViewportMouseY { get; set; }

    // ── F9 toggle — when false, game input is blocked ──
    public bool InGameActive { get; set; }

    // ── Action to select object ──
    public Action<int>? SelectObjectByIndex { get; set; }
    public Action? FocusCameraOnSelected { get; set; }

    // ── Scene Manager (for SceneManagerPanel to switch scenes) ──
    public SceneManager? SceneManager { get; set; }

    /// <summary>Scene type — user picks this explicitly (no auto-detection).</summary>
    public enum SceneType
    {
        MainMenu,
        GameScene,
        Loading,
    }

    /// <summary>Available scene descriptors for the SceneManagerPanel.</summary>
    public record SceneEntry(string Name, string Description, bool HasInitializedEntry, SceneType Type);

    /// <summary>Human-readable labels for the SceneType enum (for combo box).</summary>
    public static readonly string[] SceneTypeLabels =
        ["Main Menu", "Game Scene", "Loading Screen"];

    /// <summary>List of all registered scenes and whether they have been initialized (current or next).</summary>
    public List<SceneEntry> AvailableScenes { get; } =
    [
        new("MainMenu",     "MainMenuScene — game title screen",            false, SceneType.MainMenu),
        new("GameScene",    "GameScene — main gameplay scene",               false, SceneType.GameScene),
        new("LoadingScene", "LoadingScene — resource loading screen",        false, SceneType.Loading),
    ];

    // ── Available Behaviors (for Inspector panel combo box) ──

    /// <summary>A behavior option with a UI label and a saved value string.</summary>
    public record BehaviorOption(string Label, string Value);

    /// <summary>All available behaviors a user can assign to a UI button via the Inspector.
    /// The Value is saved to .ing files and mapped to Action delegates at runtime by the scene.
    /// The Label is only for display in the combo box.</summary>
    public static readonly BehaviorOption[] AvailableBehaviors =
    [
        new("(none)",                        ""),
        new("New Game → Loading → Game",     "startgame"),
        new("Continue (latest save)",         "continue"),
        new("Open Load Game overlay",         "loadgame"),
        new("Open Settings panel",            "opensettings"),
        new("Show Exit Confirmation",         "showexitconfirm"),
        new("Confirm Exit (quit app)",        "confirmexit"),
        new("Cancel / Close dialog",          "cancel"),
        new("Apply & Save Settings",          "applysettings"),
        new("Cancel Settings",                "cancelsettings"),
        new("Discard Changes",                "discardchanges"),
        new("Keep Editing",                   "keepediting"),
    ];

    /// <summary>Mark a scene as initialized (has been entered at least once).</summary>
    public void MarkSceneInitialized(string name)
    {
        for (int i = 0; i < AvailableScenes.Count; i++)
        {
            if (AvailableScenes[i].Name == name)
            {
                AvailableScenes[i] = AvailableScenes[i] with { HasInitializedEntry = true };
                return;
            }
        }
    }
}
