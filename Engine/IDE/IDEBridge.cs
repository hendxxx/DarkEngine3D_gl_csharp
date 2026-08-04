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
    /// <summary>Terrain render time in ms (last frame). Set by GameScene, shown in the in-game overlay.</summary>
    public float RenderTerrainMs { get; set; }
    /// <summary>Objects render time in ms (last frame). Set by GameScene, shown in the in-game overlay.</summary>
    public float RenderObjectsMs { get; set; }
    /// <summary>Post-process pass time in ms (last frame). Set by GameScene, shown in the in-game overlay.</summary>
    public float RenderPostFxMs { get; set; }
    /// <summary>Total frame render time in ms (last frame). Set by GameScene, shown in the in-game overlay.</summary>
    public float RenderTotalMs { get; set; }
    /// <summary>Per-object render timings from the last frame (animated characters + static groups).
    /// Set by GameScene each frame; consumed by the Render Time panel.</summary>
    public IReadOnlyList<RenderTimingSample>? ObjectRenderTimings { get; set; }
    /// <summary>True while the Render Time panel is open. When false, GameScene skips
    /// per-object timing capture so the main render path stays allocation-free.</summary>
    public bool CaptureRenderTimings { get; set; }

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

    // ── Editor debug grid toggle (shown in the viewport while editing) ──
    public bool ShowDebugGrid { get; set; } = true;

    // ── Action to select object ──
    public Action<int>? SelectObjectByIndex { get; set; }
    public Action? FocusCameraOnSelected { get; set; }

    // ── Undo/Redo integration ──
    // Called by ViewportPanel when a drag operation ends to record undo for position/size change.
    // Parameters: (element, oldX, oldY, oldW, oldH, newX, newY, newW, newH)
    public Action<UIElement, float, float, float, float, float, float, float, float>? RecordTransformUndo { get; set; }
    /// <summary>Called by InspectorPanel when a color property changes.
    /// Parameters: (element, propertyName, oldColor, newColor)</summary>
    public Action<UIElement, string, Vector3, Vector3>? RecordColorUndo { get; set; }

    // ── Save integration (HierarchyPanel → SceneManagerPanel) ──
    /// <summary>Called by HierarchyPanel to save ALL editor scenes (triggers SceneManager's Save All).</summary>
    public Action? SaveAllScenes { get; set; }
    /// <summary>Called by HierarchyPanel to open the Save As file dialog (first save or redirect).</summary>
    public Action? RequestSaveAsDialog { get; set; }

    // ── Scene Manager (for SceneManagerPanel to switch scenes) ──
    public SceneManager? SceneManager { get; set; }

    // ── Editor Object Manager ──
    private EditorObjectManager? _editorObjectManager;
    /// <summary>Manages editor-placed 3D primitives (Plane, Box, Sphere, glb references).
    /// Auto-wires OnObjectSelected to keep SelectedEditorObject in sync.</summary>
    public EditorObjectManager? EditorObjectManager
    {
        get => _editorObjectManager;
        set
        {
            // Unwire old manager
            if (_editorObjectManager != null)
            {
                _editorObjectManager.OnObjectSelected -= OnManagerSelectionChanged;
                _editorObjectManager.OnObjectRemoved -= OnManagerObjectRemoved;
            }

            // If the manager instance changes (e.g. scene switch), the previous selection
            // belongs to the old manager's objects — clear it so no stale refs linger.
            if (_editorObjectManager != value)
            {
                _selectedEditorObject = null;
                SelectedEditorObjects.Clear();
            }

            _editorObjectManager = value;

            // Wire new manager
            if (_editorObjectManager != null)
            {
                _editorObjectManager.OnObjectSelected += OnManagerSelectionChanged;
                _editorObjectManager.OnObjectRemoved += OnManagerObjectRemoved;
            }
        }
    }

    /// <summary>Prune the multi-selection set when any object is removed from the manager
    /// (prevents stale references to disposed objects from lingering in the set).</summary>
    private void OnManagerObjectRemoved(EditorObject obj)
    {
        DeselectEditorObject(obj);
    }

    private void OnManagerSelectionChanged(EditorObject? obj)
    {
        _selectedEditorObject = obj;
        // Keep the multi-selection set in sync with the manager's primary selection
        if (obj == null)
            SelectedEditorObjects.Clear();
        else if (!SelectedEditorObjects.Contains(obj))
            SelectedEditorObjects.Add(obj);
    }

    private EditorObject? _selectedEditorObject;
    /// <summary>All currently selected editor-placed 3D objects (multi-select).
    /// The primary (last-clicked / gizmo-driven) selection is <see cref="SelectedEditorObject"/>.</summary>
    public HashSet<EditorObject> SelectedEditorObjects { get; set; } = [];

    /// <summary>Currently selected editor-placed 3D object (primary — drives the Inspector + gizmo).
    /// Setting this also syncs to EditorObjectManager.SelectedObject and keeps the multi-set in sync.
    /// Use <see cref="SelectEditorObject"/> for ctrl-additive selection instead of this setter.</summary>
    public EditorObject? SelectedEditorObject
    {
        get => _selectedEditorObject;
        set
        {
            _selectedEditorObject = value;
            // Keep the multi-set in sync: primary is always a member
            if (value == null)
                SelectedEditorObjects.Clear();
            else if (!SelectedEditorObjects.Contains(value))
                SelectedEditorObjects.Add(value);
            // Keep EditorObjectManager.SelectedObject in sync
            if (_editorObjectManager != null && _editorObjectManager.SelectedObject != value)
                _editorObjectManager.SelectedObject = value;
        }
    }

    /// <summary>True when Ctrl is held over the viewport (set each frame by ViewportPanel).
    /// Used by GameScene's click-to-select so ctrl-click adds to the multi-selection.</summary>
    public bool ViewportCtrlHeld { get; set; }
    /// <summary>True when Shift is held over the viewport (set each frame by ViewportPanel).
    /// Used by GameScene's click-to-select so shift-click also adds to the multi-selection.</summary>
    public bool ViewportShiftHeld { get; set; }

    /// <summary>Compute the single gizmo anchor position for the current selection.
    /// One object → its pivot override (or position). Multi-select → the average of all
    /// selected positions (group center), so ONE gizmo drives the whole group.
    /// Returns null when nothing is selected.</summary>
    public Vector3? GetEditorGizmoCenter()
    {
        if (SelectedEditorObjects.Count == 0) return null;
        if (SelectedEditorObjects.Count == 1)
        {
            var sole = SelectedEditorObjects.First();
            if (sole == null) return null;
            return sole.GizmoPivotOverride ?? sole.Position;
        }
        Vector3 sum = Vector3.Zero;
        int n = 0;
        foreach (var s in SelectedEditorObjects)
        {
            if (s == null) continue;
            sum += s.Position;
            n++;
        }
        return n > 0 ? sum / n : null;
    }

    /// <summary>Set the primary editor object selection. When <paramref name="additive"/> is true
    /// (Ctrl+Click) the object is added to the multi-selection set and becomes primary;
    /// otherwise the set is replaced with just this object.</summary>
    public void SelectEditorObject(EditorObject? obj, bool additive = false)
    {
        if (obj == null)
        {
            SelectedEditorObject = null; // clears set + manager sync
            return;
        }
        if (!additive)
        {
            SelectedEditorObjects.Clear();
            _selectedEditorObject = obj;
            SelectedEditorObjects.Add(obj);
            if (_editorObjectManager != null) _editorObjectManager.SelectedObject = obj;
        }
        else
        {
            if (!SelectedEditorObjects.Contains(obj)) SelectedEditorObjects.Add(obj);
            _selectedEditorObject = obj;
            if (_editorObjectManager != null) _editorObjectManager.SelectedObject = obj;
        }
    }

    /// <summary>Toggle an object in the multi-selection set (Ctrl+Click). If the removed object
    /// was the primary, the last remaining member becomes the new primary.</summary>
    public void ToggleEditorObjectSelection(EditorObject obj)
    {
        if (SelectedEditorObjects.Contains(obj))
        {
            SelectedEditorObjects.Remove(obj);
            if (_selectedEditorObject == obj)
            {
                _selectedEditorObject = SelectedEditorObjects.Count > 0 ? SelectedEditorObjects.Last() : null;
                if (_editorObjectManager != null) _editorObjectManager.SelectedObject = _selectedEditorObject;
            }
        }
        else
        {
            SelectedEditorObjects.Add(obj);
            _selectedEditorObject = obj;
            if (_editorObjectManager != null) _editorObjectManager.SelectedObject = obj;
        }
    }

    /// <summary>Remove a (deleted) object from the multi-selection set, updating the primary.
    /// Safe to call on objects that were never in the set.</summary>
    public void DeselectEditorObject(EditorObject obj)
    {
        if (!SelectedEditorObjects.Remove(obj)) return;
        if (_selectedEditorObject == obj)
        {
            _selectedEditorObject = SelectedEditorObjects.Count > 0 ? SelectedEditorObjects.Last() : null;
            if (_editorObjectManager != null) _editorObjectManager.SelectedObject = _selectedEditorObject;
        }
    }

/// <summary>Gizmo interaction mode: 0=Translate, 1=Rotate, 2=Scale.</summary>
    public int GizmoMode { get; set; } = 0;

    /// <summary>Shared TransformGizmo instance for viewport interaction.</summary>
    public TransformGizmo? EditorGizmo { get; set; }

    /// <summary>Compute a spawn position aligned to the editor grid for newly created objects.
    /// Z is forced to 0 and X is snapped to the nearest 1-unit grid column so primitives
    /// line up with the editor grid squares. Y is set so the object rests on the ground
    /// (0 for a plane, 0.5 for box/sphere so they sit on the grid plane, 1.5 for cameras at
    /// eye height, 3 for lights so they float above, 0 for sky markers).</summary>
    public static Vector3 GetGridSpawnPosition(Camera? cam, EditorPrimitiveType type)
    {
        Vector3 spawn = cam != null ? cam.Position + cam.Front * 5f : new Vector3(0f, 1f, -5f);
        spawn.X = MathF.Round(spawn.X);
        spawn.Y = type switch
        {
            EditorPrimitiveType.Plane => 0f,
            EditorPrimitiveType.Camera => 1.5f,
            EditorPrimitiveType.Light => 3f,
            EditorPrimitiveType.Sky => 0f,
            _ => 0.5f,
        };
        spawn.Z = 0f;
        return spawn;
    }

    /// <summary>Called by ViewportPanel when a gizmo drag ends (for undo support).
    /// Passes ALL EditorObjects that were actually dragged (multi-select aware,
    /// primary first) — not whatever is currently selected — so undo stays correct
    /// even if selection changed mid-drag.</summary>
    public Action<IReadOnlyList<EditorObject>>? OnGizmoDragEnded { get; set; }

    /// <summary>Called by ViewportPanel when a gizmo pivot is placed via middle-click
    /// (for undo support). Passes the object, the previous pivot override (may be null),
    /// and the new pivot override — consistent with OnGizmoDragEnded.
    /// HierarchyPanel records this as an undo/redo action so Ctrl+Z can revert pivot placement.</summary>
    public Action<EditorObject, Vector3?, Vector3?>? OnGizmoPivotChanged { get; set; }

    // ── Viewport mouse state (tracked per frame for 3D gizmo interaction) ──
    /// <summary>True when the left mouse button is held down over the viewport.</summary>
    public bool IsViewportMouseDown { get; set; }
    /// <summary>True on the frame the left mouse button is released over the viewport.</summary>
    public bool IsViewportMouseReleased { get; set; }
    /// <summary>Mouse NDC X coordinate (-1 to 1) for ray casting.</summary>
    public float ViewportMouseNDCX { get; set; }
    /// <summary>Mouse NDC Y coordinate (-1 to 1) for ray casting.</summary>
    public float ViewportMouseNDCY { get; set; }

    // ── Selection highlight colors (configurable via Inspector) ──
    /// <summary>Contains the two configurable wireframe colors: GltfObject (gold) and EditorObject (cyan).</summary>
    public SelectionHighlightColors SelectionHighlights = SelectionHighlightColors.Default;
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

    /// <summary>An editor scene with its own UIElement tree root and optional 3D objects.</summary>
    public record EditorScene(string Name, SceneType Type, UIElement Root)
    {
        /// <summary>3D editor objects specific to this scene (Plane, Box, Sphere, etc.).</summary>
        public EditorObjectManager? ObjectManager { get; set; }

        /// <summary>Per-scene render properties (background color, VSync, face culling, wireframe, etc.).</summary>
        public SceneRenderProperties? RenderProperties { get; set; }
    }

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

    /// <summary>Get overlay (Dialog/Container) names from the current scene root.</summary>
    public static string[] GetOverlayNamesFromScene(UIElement? sceneRoot)
    {
        if (sceneRoot == null) return [];
        var names = new List<string>();
        foreach (var child in sceneRoot.Children)
        {
            if (child.Type == UIElementType.Container)
                names.Add(child.Name);
        }
        return [.. names];
    }

    // ── Available Behaviors (for Inspector panel combo box) ──

    /// <summary>A behavior option with display label and stored value.</summary>
    public record BehaviorOption(string Label, string Value);

    /// <summary>Available behavior types for UI elements.</summary>
    public static readonly BehaviorOption[] AvailableBehaviors =
    [
        new BehaviorOption("(none)", ""),
        new BehaviorOption("Open / Close Overlay", "overlay"),
        new BehaviorOption("Close Current Overlay", "closeoverlay"),
        new BehaviorOption("Go to Scene", "scene"),
        new BehaviorOption("Exit", "exit"),
    ];

    /// <summary>Available scene names for scene action type.</summary>
    public string[] AvailableSceneNames =>
        _availableScenes.Select(s => s.Name).ToArray();

    /// <summary>Parse a ClickBehaviorLabel into (typeValue, param).
    /// e.g. "overlay:ExitConfirm" → ("overlay", "ExitConfirm")
    ///      "scene:MainMenu" → ("scene", "MainMenu")
    ///      "exit" → ("exit", "")
    ///      "" → ("", "")</summary>
    public static (string type, string param) ParseBehavior(string clickBehaviorLabel)
    {
        if (string.IsNullOrEmpty(clickBehaviorLabel))
            return ("", "");

        int colon = clickBehaviorLabel.IndexOf(':');
        if (colon >= 0)
        {
            string type = clickBehaviorLabel[..colon];
            string param = clickBehaviorLabel[(colon + 1)..];
            return (type.ToLowerInvariant(), param);
        }
        return (clickBehaviorLabel.ToLowerInvariant(), "");
    }

    /// <summary>Build a ClickBehaviorLabel from type and param.</summary>
    public static string BuildBehavior(string type, string param)
    {
        if (string.IsNullOrEmpty(type))
            return "";
        return string.IsNullOrEmpty(param) ? type : $"{type}:{param}";
    }

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
