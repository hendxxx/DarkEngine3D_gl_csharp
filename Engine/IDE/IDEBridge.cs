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
    /// <summary>Currently selected UI element in the hierarchy tree (primary/last-clicked).</summary>
    public UIElement? SelectedUIElement { get; set; }
    /// <summary>All currently selected UI elements (for multi-select). Keep in sync with SelectedUIElement.</summary>
    public HashSet<UIElement> SelectedUIElements { get; set; } = [];
    /// <summary>Legacy: flat list of UI buttons (kept for backward compat).</summary>
    public IReadOnlyList<UIButtonData>? SceneUIButtons { get; set; }
    /// <summary>The invisible scene-root container that holds all top-level UI elements.</summary>
    public UIElement? SceneRoot { get; set; }
    /// <summary>Root-level UI elements in the current scene, for hierarchy display (children of SceneRoot).</summary>
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

    // ── Undo/Redo integration ──
    // Called by ViewportPanel when a drag operation ends to record undo for position/size change.
    // Parameters: (element, oldX, oldY, oldW, oldH, newX, newY, newW, newH)
    public Action<UIElement, float, float, float, float, float, float, float, float>? RecordTransformUndo { get; set; }

    // ── Save integration (HierarchyPanel → SceneManagerPanel) ──
    /// <summary>Called by HierarchyPanel to save ALL editor scenes (triggers SceneManager's Save All).</summary>
    public Action? SaveAllScenes { get; set; }
    /// <summary>Called by HierarchyPanel to open the Save As file dialog (first save or redirect).</summary>
    public Action? RequestSaveAsDialog { get; set; }

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

    /// <summary>List of all registered scenes and whether they have been initialized (current or next).
    /// Starts empty — user adds scenes via the Scene Manager panel.</summary>
    public IReadOnlyList<SceneEntry> AvailableScenes => _availableScenes;
    internal List<SceneEntry> _availableScenes = [];
    /// <summary>Internal mutable list for adding/removing scenes (SceneManagerPanel uses this).</summary>
    public List<SceneEntry> AvailableScenesInternal => _availableScenes;

    // ── UI Editor scene data (managed by IDE, not by game scenes) ──

    /// <summary>An editor scene is a named container with its own UIElement tree root.</summary>
    public record EditorScene(string Name, SceneType Type, UIElement Root);

    /// <summary>All scenes created/managed by the UI Editor. Keyed by scene name.</summary>
    public Dictionary<string, EditorScene> EditorScenes { get; } = [];

    /// <summary>Name of the currently selected editor scene (displayed in SceneDetail).</summary>
    public string? SelectedEditorScene { get; set; }

    /// <summary>Get the currently selected editor scene's root element, or null.</summary>
    public UIElement? GetSelectedEditorRoot()
    {
        if (SelectedEditorScene == null) return null;
        if (EditorScenes.TryGetValue(SelectedEditorScene, out var editorScene))
            return editorScene.Root;
        return null;
    }

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
        for (int i = 0; i < _availableScenes.Count; i++)
        {
            if (_availableScenes[i].Name == name)
            {
                _availableScenes[i] = _availableScenes[i] with { HasInitializedEntry = true };
                return;
            }
        }
    }
}
