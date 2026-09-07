using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.IDE.Panels;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Scene;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.IO;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE;

/// <summary>
/// Main IDE orchestrator. Manages all panels, the ImGui controller,
/// and provides the Update/Render entry points.
/// </summary>
public class IDE : IDisposable
{
    private readonly ImGuiController _imgui;
    public readonly IDEBridge Bridge = new();

    // Panels
    private readonly ViewportPanel _viewport;
    private readonly SceneViewPanel _sceneView;
    private readonly InspectorPanel _inspector;
    private readonly AssetBrowserPanel _assetBrowser;
    private readonly ConsolePanel _console;
    private readonly SceneManagerPanel _sceneManagerPanel;
    private readonly HierarchyPanel _hierarchy;
    private readonly TransitionPanel _transitionPanel;
    private readonly RenderTimePanel _renderTime = null!;
    private readonly ShadowPanel _shadowPanel = null!;
    // PostFX removed
    private readonly PbrPanel _pbrPanel = null!;
    // ── 2D Sidescroller Panels ──
    private readonly SpriteEditorPanel _spriteEditor = null!;
    private readonly MapEditorPanel _mapEditor = null!;
    private readonly CollisionEditorPanel _collisionEditor = null!;
    private readonly IDESettingsPanel _ideSettings = null!;
    /// <summary>File picker for Model > Add GLB Reference... (.glb models).</summary>
    private readonly ImGuiFileDialog _glbDialog = new();

    // ── Transform Gizmo ──
    private readonly TransformGizmo _gizmo = new();

    public bool IsHealthy { get; private set; }

    // ── Mode toggles ──
    private bool _inGameMode = false;
    // ── IDE font scan cache ──
    private string[]? _ideFontNames;
    private string[]? _ideFontPaths;
    private float _savedIDEFontSize = 20f;
    // ── Keyboard navigation in in-game mode ──
    private UIElement? _focusedInGameElement = null;
    private int _focusedInGameIndex = -1;
    // ── Project dialogs ──
    private bool _showNewProjectDialog = false;
    private bool _showOpenProjectDialog = false;
    private byte[] _newProjectNameBuf = new byte[256];
    private byte[] _newProjectPathBuf = new byte[512];
    private readonly ImGuiFileDialog _projectFolderDialog = new();
    private readonly ImGuiFileDialog _projingFileDialog = new();
    /// <summary>When true, all ImGui panels are hidden and the game scene fills the entire screen.</summary>
    public bool InGameMode
    {
        get => _inGameMode;
        set
        {
            if (_inGameMode != value)
            {
                _inGameMode = value;
                if (_inGameMode)
                {
                    // Need at least one scene to enter in-game mode
                    if (Bridge.EditorScenes.Count == 0)
                    {
                        Console.WriteLine("[IDE] Cannot enter In-Game Mode: no scenes loaded.");
                        _inGameMode = false;
                        return;
                    }
                    // Save all editor scenes to game.ing first, then reload for in-game mode
                    Console.WriteLine($"[IDE] Saving editor scenes to {(Bridge.ActiveSaveFile ?? "game.ing")} before entering in-game mode...");
                    string previousScene = Bridge.SelectedEditorScene ?? "";
                    Bridge.SaveToGameIng?.Invoke();
                    LoadCurrentSaveFile();
                    // Restore the scene the user had selected (LoadFromIngFile picks first by default)
                    if (!string.IsNullOrEmpty(previousScene) &&
                        Bridge.EditorScenes.ContainsKey(previousScene))
                    {
                        _sceneManagerPanel.SelectEditorScenePublic(previousScene);
                    }
                    Bridge.InGameActive = true;
                    _viewport.SetFullscreen(true);
                    _focusedInGameElement = null;
                    _focusedInGameIndex = -1;

                    // ── Enable freefly + preview mode for in-game navigation ──
                    // (2D level scenes keep freefly OFF — see SyncLevelCamera.)
                    _viewport.PreviewMode = true;
                    if (Bridge.Camera != null && Bridge.ActiveTilemap == null)
                        Bridge.Camera.FlyMouseLook = true;

                    // ── Switch editor camera to the first Camera object in the scene ──
                    _lastInGameSceneName = Bridge.SelectedEditorScene;
                    SwitchToGameCamera();

                    // 2D level scenes stay in ortho FRONT view (anchored to the map's
                    // bottom-left) even in-game — re-anchor over any Camera marker.
                    _levelCameraReframePending = Bridge.ActiveTilemap != null;
                }
                else
                {
                    // Reset fullscreen mode when exiting in-game mode
                    Bridge.InGameActive = false;
                    _viewport.SetFullscreen(false);
                    _viewport.PreviewMode = false;
                    if (Bridge.Camera != null)
                        Bridge.Camera.FlyMouseLook = false;
                    _focusedInGameElement = null;
                    _focusedInGameIndex = -1;
                    _lastInGameSceneName = null;
                }
                Console.WriteLine($"[IDE] In-Game Mode: {_inGameMode}");
            }
        }
    }
    /// <summary>Toggle in-game mode on/off. F8 shortcut.</summary>
    public void ToggleInGameMode() => InGameMode = !_inGameMode;

    /// <summary>Enter in-game mode directly at startup with a specified .ing file.
    /// Skips the save step (nothing to save yet) and loads the given file path.
    /// Used by -load= command line argument in Program.cs.</summary>
    public void EnterInGameModeFromStartup(string loadPath)
    {
        if (!File.Exists(loadPath))
        {
            Console.WriteLine($"[IDE] Startup load FAILED: file not found at '{loadPath}'");
            return;
        }

        Console.WriteLine($"[IDE] Startup: loading '{loadPath}' and entering in-game mode...");

        // Load the file into editor scenes (replaces any existing scenes)
        _sceneManagerPanel.LoadFromFilePath(loadPath);

        // Set in-game mode directly — skip save, just set flags
        _inGameMode = true;
        _viewport.SetFullscreen(true);
        _focusedInGameElement = null;
        _focusedInGameIndex = -1;

        // ── Enable freefly + preview mode for in-game navigation ──
        _viewport.PreviewMode = true;
        if (Bridge.Camera != null)
            Bridge.Camera.FlyMouseLook = true;

        // ── Switch editor camera to the first Camera object in the scene ──
        _lastInGameSceneName = Bridge.SelectedEditorScene;
        SwitchToGameCamera();

        Console.WriteLine($"[IDE] Startup in-game mode active ({Bridge.EditorScenes.Count} scene(s) from '{loadPath}')");
    }

    /// <summary>Reload scenes from the current save file for in-game mode.
    /// Uses _currentSaveFile via LoadGameIngScenes() — never touches game.ing directly.</summary>
    private void LoadCurrentSaveFile()
    {
        Console.WriteLine($"[IDE] Reloading current save file for in-game mode...");
        _sceneManagerPanel.LoadGameIngScenes();
        Console.WriteLine($"[IDE] Reloaded ({Bridge.EditorScenes.Count} scenes)");
    }

    /// <summary>Auto-load game.ing when a project is opened or closed.</summary>
    private void OnProjectChanged()
    {
        Console.WriteLine($"[IDE] OnProjectChanged: IsProjectLoaded={Engine.Project.ProjectManager.IsProjectLoaded}, ProjectRoot='{Engine.Project.ProjectManager.ProjectRoot}'");
        if (Engine.Project.ProjectManager.IsProjectLoaded)
        {
            string gameIng = Engine.Project.ProjectManager.GameIngPath;
            Console.WriteLine($"[IDE] Looking for game.ing at: {gameIng}, exists={File.Exists(gameIng)}");
            if (File.Exists(gameIng))
            {
                Console.WriteLine($"[IDE] Auto-loading {gameIng}...");
                _sceneManagerPanel?.LoadFromFilePath(gameIng);
            }
            else
            {
                // Fallback: scan for any .ing files in the project
                string scenesDir = Engine.Project.ProjectManager.ScenesDir;
                string projectRoot = Engine.Project.ProjectManager.ProjectRoot ?? "";
                Console.WriteLine($"[IDE] No game.ing. ScenesDir='{scenesDir}' exists={Directory.Exists(scenesDir)}");
                string[] searchDirs = [projectRoot, scenesDir];
                string? foundIng = null;
                foreach (var dir in searchDirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    var files = Directory.GetFiles(dir, "*.ing");
                    if (files.Length > 0) { foundIng = files[0]; break; }
                }
                if (foundIng != null)
                {
                    Console.WriteLine($"[IDE] Found fallback .ing file: {foundIng}");
                    _sceneManagerPanel?.LoadFromFilePath(foundIng);
                }
                else
                {
                    Console.WriteLine($"[IDE] No .ing files found in project, starting fresh.");
                }
            }
            // Update Asset Browser to project root
            _assetBrowser?.SetProjectRoot(Engine.Project.ProjectManager.ProjectRoot);
            // Auto-load sprite sheets + Map Editor grid/palette prefs
            _spriteEditor?.OnProjectChanged(Engine.Project.ProjectManager.ProjectRoot);
            _mapEditor?.OnProjectChanged(Engine.Project.ProjectManager.ProjectRoot);
            // 2D level maps live INSIDE each scene's .ing (Map2D object payload saved/restored
            // by SceneManagerPanel). A scene only shows its level when the file contains one,
            // so there is no project-wide map auto-load anymore.
        }
        else
        {
            // Project closed — reset all editor state (same as New Project)
            Console.WriteLine("[IDE] Project closed, clearing all editor state.");
            Bridge.EditorScenes.Clear();
            Bridge.AvailableScenesInternal.Clear();
            Bridge.SceneRoot = null;
            Bridge.SceneRootElements = null;
            Bridge.SelectedEditorScene = null;
            Bridge.SelectedUIElement = null;
            Bridge.SelectedUIElements.Clear();
            Bridge.SelectedEditorObjects.Clear();
            Bridge.ActiveTilemap = null;
            // Reset Asset Browser to default
            _assetBrowser?.SetProjectRoot(null);
            // Clear sprite sheets + map editor state
            _spriteEditor?.OnProjectChanged(null);
            _mapEditor?.AutoLoadMap(null);
        }
    }

    /// <summary>
    /// When a level (visible Map2D object bound to the active tilemap) is shown in the
    /// editor, switch the camera once to an orthographic FRONT view that is reset so the
    /// map's bottom-left corner sits at the bottom-left of the viewport — a natural 2D
    /// tile-editor framing. The camera is only moved on level transitions; afterwards the
    /// user can pan/zoom freely. When the level is removed / a non-level scene is selected,
    /// the previous perspective camera is restored.
    /// </summary>
    private void SyncLevelCamera()
    {
        var cam = Bridge.Camera;
        if (cam == null) return;

        // Find the level currently shown: a VISIBLE Map2D object in the active scene's
        // manager that is bound to the active tilemap.
        Visual.Tilemap2D? level = IsLevelShown() ? Bridge.ActiveTilemap : null;

        if (level == null)
        {
            // No level shown — undo level mode if it was active.
            if (_levelCameraApplied)
            {
                cam.IsOrthographic = _levelCameraSavedOrtho;
                cam.OrthoSize = _levelCameraSavedOrthoSize;
                cam.IsFlyMode = _levelCameraSavedFly;
                cam.FlyMouseLook = _levelCameraSavedFlyLook;
                cam.SetEditorViewTransform(
                    _levelCameraSavedPos, _levelCameraSavedYaw, _levelCameraSavedPitch, cam.FoV);
                _levelCameraApplied = false;
                _levelCameraMap = null;
                Console.WriteLine("[IDE] Level camera: restored previous view");
            }
            _levelCameraReframePending = false;
            return;
        }

        // Already framed for this exact level — leave the user's pan/zoom alone,
        // unless a reframe was requested (e.g. entering in-game mode over the level).
        if (_levelCameraApplied && ReferenceEquals(_levelCameraMap, level) && !_levelCameraReframePending)
        {
            // Keep fly mode off every frame while the 2D view is active.
            cam.IsFlyMode = false;
            cam.FlyMouseLook = false;
            return;
        }

        if (!_levelCameraApplied)
        {
            // Remember the current view so we can come back to it later.
            _levelCameraSavedPos = cam.Position;
            _levelCameraSavedYaw = cam.Yaw;
            _levelCameraSavedPitch = cam.Pitch;
            _levelCameraSavedOrtho = cam.IsOrthographic;
            _levelCameraSavedOrthoSize = cam.OrthoSize;
            _levelCameraSavedFly = cam.IsFlyMode;
            _levelCameraSavedFlyLook = cam.FlyMouseLook;
        }
        _levelCameraReframePending = false;

        // Frame the whole map in ortho, anchored so world (0,0) — the map's bottom-left
        // corner — is at the bottom-left of the viewport. The map plane is upright at
        // z = layer index, spanning x/y in [0, W*cell] × [0, H*cell].
        float cell = level.TileSize * Visual.Tilemap2D.WorldScale;
        float extentW = level.Width * cell;
        float extentH = level.Height * cell;
        int vw = Bridge.SceneTextureWidth;
        int vh = Bridge.SceneTextureHeight;
        float aspect = vw > 0 && vh > 0 ? (float)vw / vh : 16f / 9f;

        // Half-height chosen so the whole map fits the viewport at the current aspect.
        float margin = cell * 0.5f;
        float halfH = MathF.Max(extentH, extentW / aspect) * 0.5f + margin;
        float halfW = halfH * aspect;
        float lookZ = MathF.Max(60f, extentW + extentH + halfW);

        cam.IsOrthographic = true;
        cam.OrthoSize = halfH;
        cam.SetEditorViewTransform(new Vector3(halfW, halfH, lookZ), 180f, 0f, cam.FoV);

        // 2D level mode has no fly navigation — yaw/pitch stay locked on the front view;
        // users pan/zoom the ortho camera instead of flying around the map.
        cam.IsFlyMode = false;
        cam.FlyMouseLook = false;

        _levelCameraApplied = true;
        _levelCameraMap = level;
        Console.WriteLine($"[IDE] Level camera: ortho front view for '{level.Name}' " +
            $"({extentW:F0}x{extentH:F0} units, origin at bottom-left)");
    }

    /// <summary>True while a level is shown: a VISIBLE Map2D object bound to the active
    /// tilemap exists in the current scene's object manager. Used to keep the in-game
    /// camera in ortho/front and disable freefly for 2D scenes.</summary>
    private bool IsLevelShown()
    {
        if (Bridge.ActiveTilemap == null || Bridge.EditorObjectManager == null) return false;
        foreach (var o in Bridge.EditorObjectManager.Objects)
        {
            if (o != null && o.PrimitiveType == EditorPrimitiveType.Map2D
                && o.IsVisible && ReferenceEquals(o.Map2dTilemap, Bridge.ActiveTilemap))
                return true;
        }
        return false;
    }

    /// <summary>Persist all current editor data before the project is closed or the app
    /// exits: sprite sheets + animation clips (Assets/Sprites), the active level's map
    /// file (Assets/Maps) and the editor scenes (.ing, which also carries the level).
    /// Ran BEFORE ProjectManager clears the project root.</summary>
    private void PersistEditorData()
    {
        if (!Engine.Project.ProjectManager.IsProjectLoaded) return;
        try
        {
            _spriteEditor?.SaveAllSheets();
            _mapEditor?.SaveMap();
            _sceneManagerPanel?.SaveAllScenes();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IDE] PersistEditorData failed: {ex.Message}");
        }
    }

    /// <summary>Warning text shown when no Camera object is found in the scene during in-game mode.</summary>
    private string? _inGameCameraWarning = null;
    private float _inGameCameraWarningTimer = 0f;

    /// <summary>Tracks the last in-game scene so we can switch camera when the user
    /// navigates to a different scene via in-game UI buttons.</summary>
    private string? _lastInGameSceneName = null;

    // ── Auto ortho + Front camera for the 2D level editor ──
    /// <summary>Whether the level camera (orthographic front view anchored to the map's
    /// bottom-left corner) is currently applied.</summary>
    private bool _levelCameraApplied;
    /// <summary>Set when entering in-game mode over a level so the camera is re-anchored to
    /// the ortho front/bottom-left view (SwitchToGameCamera may have moved it).</summary>
    private bool _levelCameraReframePending;
    /// <summary>The level the camera was framed for (re-frames when a different map loads).</summary>
    private Visual.Tilemap2D? _levelCameraMap;
    // Camera state saved when level mode started, so leaving the level restores the view.
    private System.Numerics.Vector3 _levelCameraSavedPos;
    private float _levelCameraSavedYaw, _levelCameraSavedPitch, _levelCameraSavedOrthoSize;
    private bool _levelCameraSavedOrtho;
    private bool _levelCameraSavedFly, _levelCameraSavedFlyLook;

    /// <summary>Switch the editor freefly camera to the first placed Camera object in the current scene.
    /// If no Camera object exists, shows a warning overlay for 5 seconds.
    /// Called when entering in-game mode (F8 or startup).</summary>
    private void SwitchToGameCamera()
    {
        _inGameCameraWarning = null;
        _inGameCameraWarningTimer = 0f;

        // Only GameScene type requires camera switch + warning
        // MainMenu and Loading are pure UI — no camera needed
        if (Bridge.SelectedEditorScene != null &&
            Bridge.EditorScenes.TryGetValue(Bridge.SelectedEditorScene, out var checkScene))
        {
            if (checkScene.Type != IDEBridge.SceneType.GameScene)
            {
                Console.WriteLine($"[IDE] In-Game Mode: {checkScene.Type} scene — skipping camera switch");
                return;
            }
        }

        if (Bridge.EditorObjectManager == null)
        {
            _inGameCameraWarning = "No Camera found in scene — add a Camera object (Model → Camera) for in-game view.";
            _inGameCameraWarningTimer = 5f;
            Console.WriteLine("[IDE] In-Game Mode: no EditorObjectManager, cannot find Camera");
            return;
        }

        // Find the first visible Camera object in the current scene
        EditorObject? gameCamera = null;
        foreach (var obj in Bridge.EditorObjectManager.Objects)
        {
            if (obj.PrimitiveType == EditorPrimitiveType.Camera && obj.IsVisible)
            {
                gameCamera = obj;
                break;
            }
        }

        if (gameCamera == null)
        {
            _inGameCameraWarning = "No Camera found in scene — add a Camera object (Model → Camera) for in-game view.";
            _inGameCameraWarningTimer = 5f;
            Console.WriteLine("[IDE] In-Game Mode: no Camera object found in scene");
            return;
        }

        // Position the editor camera at the placed Camera's position and look direction
        var cam = Bridge.Camera;
        if (cam == null)
        {
            Console.WriteLine("[IDE] In-Game Mode: editor camera not available");
            return;
        }

        cam.Position = gameCamera.Position;

        // Convert the Camera object's Euler rotation to editor fly-camera Yaw/Pitch.
        // Both use CreateFromYawPitchRoll convention: Y = yaw, X = pitch (positive = look up).
        cam.Yaw = gameCamera.RotationEuler.Y;
        cam.Pitch = gameCamera.RotationEuler.X;
        cam.UpdateVectors();
        cam.SyncSmoothVectors();

        Console.WriteLine($"[IDE] In-Game Mode: switched to Camera '{gameCamera.Name}' at {gameCamera.Position}"
            + $" (yaw={cam.Yaw:F1}°, pitch={cam.Pitch:F1}°)");
    }

    /// <summary>Render a warning overlay in the center of the screen (used when no game camera found).</summary>
    private void RenderInGameWarning(float dt)
    {
        if (string.IsNullOrEmpty(_inGameCameraWarning) || _inGameCameraWarningTimer <= 0f) return;
        _inGameCameraWarningTimer -= dt;
        if (_inGameCameraWarningTimer <= 0f) { _inGameCameraWarning = null; return; }

        var drawList = ImGui.GetForegroundDrawList();
        var io = ImGui.GetIO();
        float alpha = Math.Clamp(_inGameCameraWarningTimer, 0f, 1f);
        var font = ImGui.GetFont();
        float fontSize = Bridge.ViewportFontSize > 0f ? Bridge.ViewportFontSize : 18f;
        var textSize = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, _inGameCameraWarning);
        float cx = io.DisplaySize.X * 0.5f;
        float cy = io.DisplaySize.Y * 0.15f;
        var bgMin = new Vector2(cx - textSize.X * 0.5f - 12f, cy - textSize.Y * 0.5f - 6f);
        var bgMax = new Vector2(cx + textSize.X * 0.5f + 12f, cy + textSize.Y * 0.5f + 6f);
        drawList.AddRectFilled(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0.6f, 0.2f, 0.1f, 0.85f * alpha)), 6f);
        drawList.AddRect(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.4f, 0.3f, alpha)), 6f, ImDrawFlags.None, 1.5f);
        drawList.AddText(font, fontSize, new Vector2(cx - textSize.X * 0.5f, cy - textSize.Y * 0.5f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.9f, 0.8f, alpha)), _inGameCameraWarning);
    }

    public IDE(nint window)
    {
        try
        {
            Console.WriteLine("[IDE] Creating ImGuiController...");
            _imgui = new ImGuiController(window);
            Bridge.ImGuiCtrl = _imgui;
            Console.WriteLine("[IDE] ImGuiController OK");

            _viewport = new ViewportPanel(Bridge);

            _sceneView = new SceneViewPanel(Bridge);
            _inspector = new InspectorPanel(Bridge);
            _assetBrowser = new AssetBrowserPanel(Bridge);
            _console = new ConsolePanel(Bridge);
            _sceneManagerPanel = new SceneManagerPanel(Bridge);
            _hierarchy = new HierarchyPanel(Bridge);
            _transitionPanel = new TransitionPanel(Bridge);
            _renderTime = new RenderTimePanel(Bridge);
            _shadowPanel = new ShadowPanel(Bridge);
            _pbrPanel = new PbrPanel(Bridge);
            _spriteEditor = new SpriteEditorPanel(Bridge);
            _mapEditor = new MapEditorPanel(Bridge);
            _collisionEditor = new CollisionEditorPanel(Bridge);
            _ideSettings = new IDESettingsPanel(Bridge);

            // Wire tilemap painting: viewport raycasts → panel paint/fill/pick handlers.
            // (Declared on the bridge but this connection was never made — without it,
            // clicking/dragging tiles in the viewport did nothing.)
            Bridge.MapPaintAt = pos => _mapEditor.PaintAtWorldPosition(pos);
            Bridge.MapFillAt = pos => _mapEditor.FillAtWorldPosition(pos);
            Bridge.MapPickAt = pos => _mapEditor.PickAtWorldPosition(pos);

            // Assign shared gizmo to bridge
            Bridge.EditorGizmo = _gizmo;

            // Wire save integration: HierarchyPanel can trigger SceneManager's Save All / Save As
            Bridge.SaveAllScenes = () => _sceneManagerPanel.SaveAllEditorScenesPublic();
            Bridge.SaveToGameIng = () => _sceneManagerPanel.SaveToGameIng();            Bridge.RequestSaveAsDialog = () => _sceneManagerPanel.OpenSaveAsDialog();

            // Auto-load scenes when project is opened
            Engine.Project.ProjectManager.OnProjectChanged += OnProjectChanged;

            IsHealthy = true;
            Console.WriteLine("[IDE] Initialized.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IDE] INIT FAILED: {ex.GetType().Name}: {ex.Message}");
            // Create placeholder null-safe panels so the engine doesn't crash
            _imgui = null!;
            _viewport = null!;
            _sceneView = null!;
            _inspector = null!;
            _assetBrowser = null!;
            _console = null!;
            _sceneManagerPanel = null!;
            _hierarchy = null!;
        }
    }

    /// <summary>Called from engine's update loop.</summary>
    public void Update(float deltaTime)
    {
        if (!IsHealthy) return;
        _imgui.NewFrame(deltaTime);
    }

    /// <summary>Called from engine's render loop, after all game rendering.</summary>
    public void Render()
    {
        if (!IsHealthy) return;

        // ── F8 shortcut: toggle between IDE Mode and In-Game Mode ──
        if (ImGui.IsKeyReleased(ImGuiKey.F8))
            ToggleInGameMode();

        // A level (Map2D) keeps the camera in orthographic FRONT view even while in-game:
        // this must run for both editor frames and in-game frames (before the early return).
        SyncLevelCamera();

        // ── In-Game Mode: render full-screen viewport with no ImGui chrome ──
        if (_inGameMode)
        {
            RenderInGameMode();
            return;
        }

        // ── Restore editor scene root and 3D objects for the current scene ──
        if (Bridge.SelectedEditorScene == null && Bridge.EditorScenes.Count > 0)
        {
            // No scene is selected but there are editor scenes — auto-select the first one.
            // Use SceneManagerPanel.SelectEditorScene for full initialization.
            foreach (var kvp in Bridge.EditorScenes)
            {
                _sceneManagerPanel.SelectEditorScenePublic(kvp.Key);
                break;
            }
            Console.WriteLine($"[IDE] Auto-selected scene: {Bridge.SelectedEditorScene}");
        }

        if (Bridge.SelectedEditorScene != null &&
            Bridge.EditorScenes.TryGetValue(Bridge.SelectedEditorScene, out var activeEditorScene))
        {
            Bridge.SceneRoot = activeEditorScene.Root;
            Bridge.SceneRootElements = new List<UIElement> { activeEditorScene.Root }.AsReadOnly();

            // Sync the editor object manager to match the current scene
            if (Bridge.EditorObjectManager != activeEditorScene.ObjectManager)
            {
                var mgr = activeEditorScene.ObjectManager;
                if (mgr == null)
                {
                    mgr = new EditorObjectManager();
                    activeEditorScene.ObjectManager = mgr;
                }
                Bridge.EditorObjectManager = mgr;
                Bridge.SelectedEditorObject = null;
            }
        }

        // Map2D / tilemap renderers only belong in GameScene — rebind the active map to
        // the current scene's object manager (or strip it from menu/loading scenes).
        Bridge.EnforceMapObjectSceneRule();

        // ── Model > Add GLB Reference... file dialog + placement ──
        _glbDialog.Render();
        if (_glbDialog.IsConfirmed && _glbDialog.SelectedPath != null)
        {
            string path = _glbDialog.SelectedPath;
            _glbDialog.Close();
            if (File.Exists(path))
            {
                var glbObj = Bridge.EditorObjectManager?.AddGlbReference(path, GetSpawnPosition(EditorPrimitiveType.GlbReference));
                if (glbObj != null) Bridge.SelectEditorObject(glbObj);
            }
        }



        // ── Build main menu bar ──
        ImGui.BeginMainMenuBar();
        {
            // ════════════════════════════════════════════════════
            //  File Menu
            // ════════════════════════════════════════════════════
            if (ImGui.BeginMenu("File"))
            {
                // ═══ Project (top) ═══
                if (ImGui.MenuItem("New Project..."))
                {
                    _projectFolderDialog.OpenForPickFolder("Select Project Location");
                }
                if (ImGui.MenuItem("Open Project..."))
                {
                    _projingFileDialog.OpenForLoad("*.projing", "Open Project (.projing)");
                }

                // ── Recent Projects ──
                var recentProjects = RecentProjectsManager.GetRecentProjects();
                if (recentProjects.Count > 0)
                {
                    if (ImGui.BeginMenu("Recent Projects"))
                    {
                        for (int ri = 0; ri < recentProjects.Count; ri++)
                        {
                            string projPath = recentProjects[ri];
                            string projName = Path.GetFileName(projPath);
                            if (string.IsNullOrEmpty(projName)) projName = projPath;
                            bool opened = ImGui.MenuItem(projName);
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip(projPath);
                            if (opened)
                            {
                                if (Engine.Project.ProjectManager.OpenProject(projPath))
                                {
                                    RecentProjectsManager.AddRecentProject(projPath);
                                    Console.WriteLine($"[IDE] Opened recent project: {projPath}");
                                }
                            }
                        }
                        ImGui.Separator();
                        if (ImGui.MenuItem("Clear Recent Projects"))
                        {
                            RecentProjectsManager.ClearRecentProjects();
                        }
                        ImGui.EndMenu();
                    }
                }

                // Show current project info + close
                if (Engine.Project.ProjectManager.IsProjectLoaded)
                {
                    ImGui.Separator();
                    ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.5f, 1f), $"  {Path.GetFileName(Engine.Project.ProjectManager.ProjectRoot)}");
                    ImGui.TextDisabled($"  {Engine.Project.ProjectManager.ProjectRoot}");
                    if (ImGui.MenuItem("Close Project"))
                    {
                        // Persist everything before the project root is cleared.
                        PersistEditorData();
                        Engine.Project.ProjectManager.CloseProject();
                        Console.WriteLine("[IDE] Project closed");
                    }
                }

                ImGui.Separator();

                // ═══ Scenes ═══
                // New Scene — opens SceneManager's Add Scene popup
                if (ImGui.MenuItem("New Scene", "Ctrl+N"))
                    _sceneManagerPanel.OpenAddScenePopup();

                // Open Scene — opens file dialog to load .ing
                if (ImGui.MenuItem("Open Scene...", "Ctrl+O"))
                    _sceneManagerPanel.OpenLoadFileDialog();

                // ── Recent Files ──
                var recentFiles = RecentFilesManager.GetRecentFiles();
                if (recentFiles.Count > 0)
                {
                    if (ImGui.BeginMenu("Open Recent Scene"))
                    {
                        for (int ri = 0; ri < recentFiles.Count; ri++)
                        {
                            string path = recentFiles[ri];
                            string label = Path.GetFileName(path);
                            bool loaded = ImGui.MenuItem(label, $"Ctrl+F{ri + 1}");
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip(path);
                            if (loaded)
                                _sceneManagerPanel.LoadFromFilePath(path);
                        }
                        ImGui.Separator();
                        if (ImGui.MenuItem("Clear Recent Files"))
                        {
                            RecentFilesManager.ClearRecentFiles();
                        }
                        ImGui.EndMenu();
                    }
                }

                ImGui.Separator();

                // Save (Save All) — scenes (.ing incl. level) + sprite sheets/anim data
                bool hasEditorScenes = Bridge.EditorScenes.Count > 0;
                ImGui.BeginDisabled(!hasEditorScenes);
                if (ImGui.MenuItem("Save", "Ctrl+S"))
                {
                    _spriteEditor?.SaveAllSheets();
                    _mapEditor?.SaveMap();
                    _sceneManagerPanel.SaveAllScenes();
                }
                ImGui.EndDisabled();

                // Save As...
                if (ImGui.MenuItem("Save As...", "Ctrl+Shift+S"))
                    _sceneManagerPanel.OpenSaveAsDialog();

                ImGui.Separator();

                if (ImGui.MenuItem("Exit"))
                {
                    Console.WriteLine("Exiting via File > Exit");
                    PersistEditorData();
                    nint exitWindow = Glfw.GetWindow();
                    if (exitWindow != nint.Zero)
                        Glfw.SetWindowShouldClose(exitWindow, 1);
                }
                ImGui.EndMenu();
            }

            // ════════════════════════════════════════════════════
            //  Model Menu
            // ════════════════════════════════════════════════════
            if (ImGui.BeginMenu("Model"))
            {
                if (ImGui.MenuItem("Add Plane", "Ctrl+1"))
                {
                    var obj = Bridge.EditorObjectManager?.AddPrimitive(EditorPrimitiveType.Plane, GetSpawnPosition(EditorPrimitiveType.Plane));
                    if (obj != null) Bridge.SelectEditorObject(obj);
                }
                if (ImGui.MenuItem("Add Box", "Ctrl+2"))
                {
                    var obj = Bridge.EditorObjectManager?.AddPrimitive(EditorPrimitiveType.Box, GetSpawnPosition(EditorPrimitiveType.Box));
                    if (obj != null) Bridge.SelectEditorObject(obj);
                }
                if (ImGui.MenuItem("Add Sphere", "Ctrl+3"))
                {
                    var obj = Bridge.EditorObjectManager?.AddPrimitive(EditorPrimitiveType.Sphere, GetSpawnPosition(EditorPrimitiveType.Sphere));
                    if (obj != null) Bridge.SelectEditorObject(obj);
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Add Camera", "Ctrl+4"))
                {
                    var obj = Bridge.EditorObjectManager?.AddPrimitive(EditorPrimitiveType.Camera, GetSpawnPosition(EditorPrimitiveType.Camera));
                    if (obj != null) Bridge.SelectEditorObject(obj);
                }
                if (ImGui.MenuItem("Add Light", "Ctrl+5"))
                {
                    var obj = Bridge.EditorObjectManager?.AddPrimitive(EditorPrimitiveType.Light, GetSpawnPosition(EditorPrimitiveType.Light));
                    if (obj != null) Bridge.SelectEditorObject(obj);
                }
                if (ImGui.MenuItem("Add Sky", "Ctrl+6"))
                {
                    var obj = Bridge.EditorObjectManager?.AddPrimitive(EditorPrimitiveType.Sky, GetSpawnPosition(EditorPrimitiveType.Sky));
                    if (obj != null)
                    {
                        // Sky automatically drives a DIRECT light — reuse or create one (bug #7).
                        Bridge.EditorObjectManager?.EnsureDirectLightForSky(obj);
                        Bridge.SelectEditorObject(obj);
                    }
                }
                if (ImGui.MenuItem("Add GLB Reference..."))
                {
                    _glbDialog.OpenForLoad("*.glb", "Open GLB model");
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Gizmo: Translate", null, Bridge.GizmoMode == 0))
                    Bridge.GizmoMode = 0;
                // Sky markers can't be rotated/scaled — disable those gizmo modes while
                // a Sky object is selected (the gizmo stays locked to Translate).
                bool skyLocked = Bridge.SelectionHasSky;
                if (skyLocked) ImGui.BeginDisabled();
                if (ImGui.MenuItem("Gizmo: Rotate", null, Bridge.GizmoMode == 1))
                    Bridge.GizmoMode = 1;
                if (ImGui.MenuItem("Gizmo: Scale", null, Bridge.GizmoMode == 2))
                    Bridge.GizmoMode = 2;
                if (skyLocked) ImGui.EndDisabled();
                ImGui.EndMenu();
            }

            // ════════════════════════════════════════════════════
            //  2D Menu
            // ════════════════════════════════════════════════════
            if (ImGui.BeginMenu("2D"))
            {
                if (ImGui.MenuItem("New Tilemap"))
                    _mapEditor.CreateNewMap();
                if (ImGui.MenuItem("Add Parallax Layer"))
                    _mapEditor.AddParallaxLayer();
                ImGui.EndMenu();
            }

            // ════════════════════════════════════════════════════
            //  Edit Menu
            // ════════════════════════════════════════════════════
            if (ImGui.BeginMenu("Edit"))
            {
                // Undo / Redo
                ImGui.BeginDisabled(!_hierarchy.CanUndo);
                if (ImGui.MenuItem("Undo", "Ctrl+Z"))
                    _hierarchy.Undo();
                ImGui.EndDisabled();

                ImGui.BeginDisabled(!_hierarchy.CanRedo);
                if (ImGui.MenuItem("Redo", "Ctrl+Y"))
                    _hierarchy.Redo();
                ImGui.EndDisabled();

                ImGui.Separator();

                // Cut / Copy / Paste / Duplicate
                ImGui.BeginDisabled(!_hierarchy.HasSelection);
                if (ImGui.MenuItem("Cut", "Ctrl+X"))
                    _hierarchy.CutSelection();

                if (ImGui.MenuItem("Copy", "Ctrl+C"))
                    _hierarchy.CopySelection();

                if (ImGui.MenuItem("Paste", "Ctrl+V"))
                    _hierarchy.PasteClipboard();

                // Paste to specific scene
                ImGui.BeginDisabled(!_hierarchy.HasClipboard);
                if (ImGui.BeginMenu("Paste to Scene..."))
                {
                    foreach (var sceneName in Bridge.AvailableSceneNames)
                    {
                        if (ImGui.MenuItem(sceneName))
                        {
                            // Switch to target scene, then paste
                            string prevScene = Bridge.SelectedEditorScene ?? "";
                            if (sceneName != prevScene)
                            {
                                _sceneManagerPanel.SelectEditorScenePublic(sceneName);
                            }
                            _hierarchy.PasteClipboard();
                        }
                    }
                    ImGui.EndMenu();
                }
                ImGui.EndDisabled();

                if (ImGui.MenuItem("Duplicate", "Ctrl+D"))
                    _hierarchy.Duplicate();
                ImGui.EndDisabled();

                ImGui.Separator();

                // Delete
                ImGui.BeginDisabled(!_hierarchy.HasSelection);
                if (ImGui.MenuItem("Delete", "Del"))
                    _hierarchy.DeleteSelection();
                ImGui.EndDisabled();

                ImGui.EndMenu();
            }

            // ════════════════════════════════════════════════════
            //  View Menu
            // ════════════════════════════════════════════════════
            if (ImGui.BeginMenu("View"))
            {
                // ── IDE Mode / In-Game Mode (radio-like, mutually exclusive) ──
                if (ImGui.MenuItem("IDE Mode", null, !_inGameMode, true))
                    InGameMode = false;
                if (ImGui.MenuItem("In-Game Mode", "F8", _inGameMode, true))
                    InGameMode = true;

                ImGui.Separator();

                // ── Preview Mode (within IDE Mode, hides editor helpers) ──
                ImGui.BeginDisabled(_inGameMode);
                bool isPreview = _viewport.PreviewMode;
                if (ImGui.MenuItem("Preview Mode", "F5", isPreview, !_inGameMode))
                    _viewport.PreviewMode = !isPreview;
                ImGui.EndDisabled();

                ImGui.Separator();

                // ── Snap to Grid toggle ──
                bool snap = _viewport.SnapEnabled;
                if (ImGui.MenuItem("Snap to Grid", null, snap))
                {
                    _viewport.SnapEnabled = !snap;
                    _viewport.PersistViewportPrefs();
                }

                // ── Viewport shadows (CSM) toggle ──
                bool shadows = Bridge.ShowShadows;
                if (ImGui.MenuItem("Viewport Shadows", null, shadows))
                {
                    Bridge.ShowShadows = !shadows;
                    _viewport.PersistViewportPrefs();
                    Console.WriteLine($"[IDE] Viewport shadows {(shadows ? "disabled" : "enabled")}");
                }

                ImGui.Separator();

                // ── Panel visibility toggles ──
                _viewport.ShowInMenu();
                _sceneView.ShowInMenu();
                _inspector.ShowInMenu();
                _assetBrowser.ShowInMenu();
                _console.ShowInMenu();
                _hierarchy.ShowInMenu();
                _renderTime.ShowInMenu();
                _shadowPanel.ShowInMenu();
                _pbrPanel.ShowInMenu();
                ImGui.Separator();
                _sceneManagerPanel.ShowInMenu();
                _transitionPanel.ShowInMenu();
                ImGui.Separator();
                // ── 2D Sidescroller Panels ──
                _spriteEditor.ShowInMenu();
                _mapEditor.ShowInMenu();
                _collisionEditor.ShowInMenu();
                ImGui.Separator();
                _ideSettings.ShowInMenu();

                // ── IDE Font selector ──
                ImGui.Separator();
                if (ImGui.BeginMenu("IDE Font"))
                {
                    ScanIDEFontsMenu();
                    ImGui.Separator();
                    float fontSize = _savedIDEFontSize;
                    if (ImGui.DragFloat("Font Size", ref fontSize, 0.5f, 8f, 28f, "%.0f"))
                    {
                        _savedIDEFontSize = Math.Clamp(fontSize, 8f, 28f);
                    }
                    // Apply font change only when mouse released (no flicker)
                    if (ImGui.IsItemDeactivatedAfterEdit())
                    {
                        _imgui.ChangeIDEFont(_cachedIDEFontPath, _savedIDEFontSize);
                        var s = Config.SettingsSave.Load(); s.IDEFontSize = _savedIDEFontSize; s.IDEFontPath = _cachedIDEFontPath; Config.SettingsSave.Save(s);
                    }
                    ImGui.EndMenu();
                }

                // ── Viewport Font Size ──
                if (ImGui.BeginMenu("Viewport Font"))
                {
                    float vpFontSize = Bridge.ViewportFontSize;
                    if (ImGui.DragFloat("Font Size", ref vpFontSize, 0.5f, 8f, 32f, "%.0f"))
                    {
                        Bridge.ViewportFontSize = Math.Clamp(vpFontSize, 8f, 32f);
                    }
                    if (ImGui.IsItemDeactivatedAfterEdit())
                    {
                        var s = Config.SettingsSave.Load(); s.ViewportFontSize = Bridge.ViewportFontSize; Config.SettingsSave.Save(s);
                    }
                    ImGui.EndMenu();
                }
                ImGui.EndMenu();
            }

            // ════════════════════════════════════════════════════
            //  Help Menu
            // ════════════════════════════════════════════════════
            if (ImGui.BeginMenu("Help"))
            {
                ImGui.Text("DarkEngine IDE v0.1");
                ImGui.Separator();
                ImGui.TextDisabled("Ctrl+Z  Undo");
                ImGui.TextDisabled("Ctrl+Y  Redo");
                ImGui.TextDisabled("Ctrl+C  Copy");
                ImGui.TextDisabled("Ctrl+V  Paste");
                ImGui.TextDisabled("Ctrl+D  Duplicate");
                ImGui.TextDisabled("Del     Delete");
                ImGui.TextDisabled("F5      Preview Mode");
                ImGui.TextDisabled("F8      In-Game Mode"); 
                ImGui.EndMenu();
            }  
             

            ImGui.EndMainMenuBar();
        }



        // ── Docking space ──
        ImGui.DockSpaceOverViewport();



        // ── Render panels ──
        _viewport.Render();
        _sceneView.Render();
        _inspector.Render();
        _renderTime.Render();
        _shadowPanel.Render();
        _pbrPanel.Render();
        _assetBrowser.Render();
        _hierarchy.Render();
        _console.Render();
        _sceneManagerPanel.Render();
        _transitionPanel.Render();
        // ── 2D Sidescroller Panels ──
        _spriteEditor.Render();
        _mapEditor.Render();
        _collisionEditor.Render();
        _ideSettings.Render();

        // ── Project popups (rendered after panels, so window context exists) ──
        RenderProjectPopups();

        // ── Render ImGui draw data ──
        // Render transition overlay (if any) while ImGui frame is active
        try
        {
            Bridge.SceneManager?.RenderTransitionOverlay();
        }
        catch { }

        _imgui.Render();
    }

    /// <summary>Render project management popups (New/Open Project, Folder Picker).
    /// Called after panels render so ImGui window context exists for OpenPopup.</summary>
    private void RenderProjectPopups()
    {
        // ── Folder picker for New Project ──
        _projectFolderDialog.Render();
        if (_projectFolderDialog.IsConfirmed && _projectFolderDialog.SelectedPath != null)
        {
            string selectedFolder = _projectFolderDialog.SelectedPath;
            Array.Clear(_newProjectNameBuf);
            Array.Clear(_newProjectPathBuf);
            System.Text.Encoding.UTF8.GetBytes(selectedFolder, _newProjectPathBuf);
            _showNewProjectDialog = true;
            _projectFolderDialog.Close();
        }

        // ── New Project name dialog ──
        if (_showNewProjectDialog)
        {
            ImGui.OpenPopup("New Project");
            _showNewProjectDialog = false;
        }
        bool newProjOpen = true;
        if (ImGui.BeginPopupModal("New Project", ref newProjOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Location:");
            ImGui.TextColored(new Vector4(0.5f, 0.7f, 1f, 1f), $"  {System.Text.Encoding.UTF8.GetString(_newProjectPathBuf).TrimEnd('\0')}");
            ImGui.Spacing();
            ImGui.Text("Project Name:");
            ImGui.SetNextItemWidth(300);
            ImGui.InputText("##proj_name", _newProjectNameBuf, (uint)_newProjectNameBuf.Length);

            ImGui.Separator();
            if (ImGui.Button("Create", new Vector2(120, 0)))
            {
                string name = System.Text.Encoding.UTF8.GetString(_newProjectNameBuf).TrimEnd('\0');
                string dir = System.Text.Encoding.UTF8.GetString(_newProjectPathBuf).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(dir))
                {
                    try
                    {
                        string rootPath = Path.Combine(dir, name);
                        Engine.Project.ProjectManager.CreateProject(rootPath, name);
                        Config.RecentProjectsManager.AddRecentProject(rootPath);
                        Console.WriteLine($"[IDE] Created project '{name}' ({name}.projing) at {rootPath}");
                        ImGui.CloseCurrentPopup();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[IDE] Failed to create project: {ex.Message}");
                    }
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        // ── Open Project file browser ──
        _projingFileDialog.Render();
        if (_projingFileDialog.IsConfirmed && _projingFileDialog.SelectedPath != null)
        {
            string projingPath = _projingFileDialog.SelectedPath;
            if (Engine.Project.ProjectManager.OpenProject(projingPath))
            {
                Config.RecentProjectsManager.AddRecentProject(Path.GetFullPath(Path.GetDirectoryName(projingPath)!));
                Console.WriteLine($"[IDE] Opened project from {projingPath}");
            }
            else
            {
                Console.WriteLine($"[IDE] Failed to open project at {projingPath}");
            }
            _projingFileDialog.Close();
        }
    }

    /// <summary>Render a full-screen game viewport with no ImGui chrome.
    /// All editor panels and menu bars are hidden; the game scene fills the entire screen.
    /// A small overlay button allows returning to IDE mode.</summary>
    /// <summary>In-Game Mode: NO ImGui windows at all.
    /// Renders scene texture + UI elements directly to foreground draw list,
    /// then calls _imgui.Render() to flush. Exit via F8 only.</summary>
    /// <summary>Scan for IDE fonts (project + common Windows fonts) and render as menu items.</summary>
    private string _cachedIDEFontPath = "";
#pragma warning disable CS0414
    private bool _fontCacheLoaded = false;
#pragma warning restore CS0414

#pragma warning disable CS0414
    private void ScanIDEFontsMenu()
    {
        if (_ideFontNames == null)
        {
            var paths = new List<string>();
            var names = new List<string>();

            // Project fonts
            try
            {
                string fontsDir = Engine.Project.ProjectManager.IsProjectLoaded
                    ? Path.Combine(Engine.Project.ProjectManager.AssetsDir, "fonts")
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Artifacts", "fonts");
                if (Directory.Exists(fontsDir))
                {
                    foreach (var f in Directory.GetFiles(fontsDir, "*.ttf"))
                    {
                        paths.Add(f);
                        names.Add($"[Project] {Path.GetFileNameWithoutExtension(f)}");
                    }
                }
            }
            catch { }

            // Common Windows fonts
            try
            {
                string winFonts = @"C:\Windows\Fonts";
                string[] commonFonts = [
                    "arial.ttf", "arialbd.ttf", "times.ttf", "timesbd.ttf",
                    "cour.ttf", "courbd.ttf", "verdana.ttf", "verdanab.ttf",
                    "tahoma.ttf", "tahomabd.ttf", "calibri.ttf", "calibrib.ttf",
                    "segoeui.ttf", "segoeuib.ttf", "segoeuil.ttf",
                    "CascadiaCode.ttf", "CascadiaMono.ttf",
                    "Consola.ttf", "consolab.ttf",
                ];
                foreach (var fn in commonFonts)
                {
                    string fullPath = Path.Combine(winFonts, fn);
                    if (File.Exists(fullPath))
                    {
                        paths.Add(fullPath);
                        names.Add(Path.GetFileNameWithoutExtension(fn));
                    }
                }
            }
            catch { }

            _ideFontNames = names.ToArray();
            _ideFontPaths = paths.ToArray();

            // Load saved font settings ONCE
            var saved = Config.SettingsSave.Load();
            _savedIDEFontSize = saved.IDEFontSize;
            _cachedIDEFontPath = saved.IDEFontPath ?? "";
            Bridge.ViewportFontSize = saved.ViewportFontSize > 0f ? saved.ViewportFontSize : 16f;
            _fontCacheLoaded = true;
        }

        // Use cached font path — no file I/O per frame!
        string currentPath = _cachedIDEFontPath;

        // Default option
        bool isDefault = string.IsNullOrEmpty(currentPath);
        if (ImGui.MenuItem("Default", null, isDefault))
        {
            _cachedIDEFontPath = "";
            _imgui.ChangeIDEFont("", _savedIDEFontSize);
            var s = Config.SettingsSave.Load(); s.IDEFontPath = ""; Config.SettingsSave.Save(s);
        }

        for (int i = 0; i < _ideFontPaths.Length; i++)
        {
            bool isActive = string.Equals(_ideFontPaths[i], currentPath, StringComparison.OrdinalIgnoreCase);
            if (ImGui.MenuItem(_ideFontNames[i], null, isActive))
            {
                _cachedIDEFontPath = _ideFontPaths[i];
                _imgui.ChangeIDEFont(_ideFontPaths[i], _savedIDEFontSize);
                var s = Config.SettingsSave.Load(); s.IDEFontPath = _ideFontPaths[i]; Config.SettingsSave.Save(s);
            }
        }
    }

    private void RenderInGameMode()
    {
        // In fullscreen mode the viewport IS the entire screen — always report focused
        // so the free-fly camera (WASD + mouse look) processes keyboard/mouse input.
        Bridge.IsViewportFocused = true;

        var io = ImGui.GetIO();
        float screenW = io.DisplaySize.X;
        float screenH = io.DisplaySize.Y;

        // Render directly to foreground draw list — no ImGui windows!
        var drawList = ImGui.GetForegroundDrawList();

        // ── ESC key: toggle menu visibility in in-game mode ──
        // Must run BEFORE hasVisibleMenu check so the state is current.
        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            // Find Containers with TriggeredByKeyboardButton == "Escape" and toggle them.
            // Also checks root itself (single Container element = root).
            // ESC only works for GameScene type — MainMenu/Loading scenes skip ESC.
            if (Bridge.EditorScenes.Count > 0 &&
                Bridge.SelectedEditorScene != null &&
                Bridge.EditorScenes.TryGetValue(Bridge.SelectedEditorScene, out var escScene)
                && escScene.Type == IDEBridge.SceneType.GameScene)
            {
                UIElement? fallbackMenu = null;
                bool foundTriggered = false;

                // Helper: check and toggle a container + restore children visibility
                void ToggleContainer(UIElement c)
                {
                    if (fallbackMenu == null && c.Type == UIElementType.Container)
                        fallbackMenu = c;
                    if (c.Type == UIElementType.Container &&
                        string.Equals(c.TriggeredByKeyboardButton, "Escape", StringComparison.OrdinalIgnoreCase))
                    {
                        c.IsVisible = !c.IsVisible;
                        foundTriggered = true;
                        // When showing container, also restore all children visibility
                        if (c.IsVisible)
                        {
                            foreach (var child in c.Children)
                                child.IsVisible = true;
                        }
                        Console.WriteLine($"[IDE] ESC: container '{c.Name}' {(c.IsVisible ? "shown" : "hidden")}");
                    }
                }

                // 1) Check root itself (single Container element scenario)
                ToggleContainer(escScene.Root);
                // 2) Check root's children (multi-element scene)
                foreach (var rootChild in escScene.Root.Children)
                    ToggleContainer(rootChild);

                // Fallback: toggle first container if none has TriggeredByKeyboardButton
                if (!foundTriggered && fallbackMenu != null)
                {
                    fallbackMenu.IsVisible = !fallbackMenu.IsVisible;
                    if (fallbackMenu.IsVisible)
                    {
                        foreach (var child in fallbackMenu.Children)
                            child.IsVisible = true;
                    }
                    Console.WriteLine($"[IDE] ESC: fallback toggle '{fallbackMenu.Name}' {(fallbackMenu.IsVisible ? "shown" : "hidden")}");
                }
            }
        }

        // ── Auto mouse/fly mode based on visible menu (Container) ──
        // Recompute AFTER ESC toggle so the state reflects the latest visibility.
        bool hasVisibleMenu = false;
        if (Bridge.EditorScenes.Count > 0 &&
            Bridge.SelectedEditorScene != null &&
            Bridge.EditorScenes.TryGetValue(Bridge.SelectedEditorScene, out var _menuScene))
        {
            // Check root itself (single Container element = root)
            if (_menuScene.Root.IsVisible && _menuScene.Root.Type == UIElementType.Container)
                hasVisibleMenu = true;
            // Check root's children (multi-element scene)
            if (!hasVisibleMenu)
            {
                foreach (var rootChild in _menuScene.Root.Children)
                {
                    if (rootChild.IsVisible && rootChild.Type == UIElementType.Container)
                    {
                        hasVisibleMenu = true;
                        break;
                    }
                }
            }
        }

        // Apply mouse/fly toggle (only when state changes to avoid per-frame noise)
        var cam = Bridge.Camera;
        if (cam != null)
        {
            // 2D level scenes match edit mode: ortho/front, freefly OFF, cursor visible.
            bool levelMode = IsLevelShown();
            if (levelMode)
            {
                if (cam.FlyMouseLook)
                {
                    cam.FlyMouseLook = false;
                    Mouse.ShowMouse(true);
                }
            }
            // ShowCursorInGame option overrides: always show cursor in-game mode
            else if (Bridge.ShowCursorInGame)
            {
                if (cam.FlyMouseLook)
                {
                    cam.FlyMouseLook = false;
                    Mouse.ShowMouse(true);
                }
            }
            else if (hasVisibleMenu && cam.FlyMouseLook)
            {
                cam.FlyMouseLook = false;
                Mouse.ShowMouse(true);
            }
            else if (!hasVisibleMenu && !cam.FlyMouseLook)
            {
                cam.FlyMouseLook = true;
                Mouse.ShowMouse(false);
            }
        }

        // 1) Scene texture from running game scene (render first, behind UI)
        if (Bridge.SceneTextureID != 0)
        {
            float texW = Bridge.SceneTextureWidth > 0 ? Bridge.SceneTextureWidth : 1f;
            float texH = Bridge.SceneTextureHeight > 0 ? Bridge.SceneTextureHeight : 1f;
            float panelAspect = screenW / screenH;
            float texAspect = texW / texH;

            Vector2 imgSize;
            if (panelAspect > texAspect)
                imgSize = new Vector2(screenH * texAspect, screenH);
            else
                imgSize = new Vector2(screenW, screenW / texAspect);

            float ox = (screenW - imgSize.X) * 0.5f;
            float oy = (screenH - imgSize.Y) * 0.5f;

            drawList.AddImage((nint)Bridge.SceneTextureID,
                new Vector2(ox, oy), new Vector2(ox + imgSize.X, oy + imgSize.Y),
                new Vector2(0, 1), new Vector2(1, 0));
        }
        else
        {
            // Dark background when no scene texture
            drawList.AddRectFilled(new Vector2(0, 0), new Vector2(screenW, screenH),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.05f, 0.08f, 1f)));
        }

        // 2) Editor scene UI elements (from loaded game.ing) — ALWAYS rendered on top
        // Also render when root itself is a Container (single element scenario)
        if (Bridge.EditorScenes.Count > 0 &&
            Bridge.SelectedEditorScene != null &&
            Bridge.EditorScenes.TryGetValue(Bridge.SelectedEditorScene, out var activeEditScene) &&
            (activeEditScene.Root.Children.Count > 0 || activeEditScene.Root.Type == UIElementType.Container))
        {
            // Use scene texture dimensions as the virtual coordinate space
            // (elements' X/Y/W/H are stored relative to this resolution)
            float virtualW = Bridge.SceneTextureWidth > 0 ? Bridge.SceneTextureWidth : 1920f;
            float virtualH = Bridge.SceneTextureHeight > 0 ? Bridge.SceneTextureHeight : 1080f;
            float virtualAspect = virtualW / virtualH;
            float panelAspect = screenW / screenH;

            float canvasW, canvasH, ox, oy;
            if (panelAspect > virtualAspect)
            {
                canvasH = screenH;
                canvasW = screenH * virtualAspect;
                ox = (screenW - canvasW) * 0.5f;
                oy = 0f;
            }
            else
            {
                canvasW = screenW;
                canvasH = screenW / virtualAspect;
                ox = 0f;
                oy = (screenH - canvasH) * 0.5f;
            }

            // ── Keyboard navigation ──
            // Collect all visible interactive elements (depth-first, flattened)
            var navElements = new List<UIElement>();

            // Check if there's an active overlay (first visible Container at root level)
            // When an overlay is open, only elements INSIDE it are navigable (modal behavior).
            // Elements behind the overlay are blocked from navigation, just like they're
            // blocked from mouse clicks by IsBlockedByOverlay in ViewportPanel.
            UIElement? activeOverlay = null;
            // Check root itself (single Container element = root IS the overlay)
            if (activeEditScene.Root.IsVisible && activeEditScene.Root.Type == UIElementType.Container)
                activeOverlay = activeEditScene.Root;
            // Check root's children (multi-element scene)
            if (activeOverlay == null)
            {
                foreach (var rootChild in activeEditScene.Root.Children)
                {
                    if (rootChild.IsVisible && rootChild.Type == UIElementType.Container)
                    {
                        activeOverlay = rootChild;
                        break;
                    }
                }
            }

            if (activeOverlay != null)
            {
                // Modal: only flatten elements inside the overlay (background not navigable)
                FlattenVisibleInteractive(activeOverlay.Children, navElements);
                if (navElements.Count > 0)
                    Console.WriteLine($"[IDE] Overlay '{activeOverlay.Name}' — {navElements.Count} navigable elements inside");
            }
            else
            {
                // No overlay: flatten all visible interactive elements as usual
                FlattenVisibleInteractive(activeEditScene.Root.Children, navElements);
            }

            // Tab / Shift+Tab to navigate forward/backward
            bool tabPressed = ImGui.IsKeyPressed(ImGuiKey.Tab, false);
            bool shiftTab = tabPressed && io.KeyShift;
            if (tabPressed)
            {
                if (navElements.Count > 0)
                {
                    if (shiftTab)
                    {
                        _focusedInGameIndex--;
                        if (_focusedInGameIndex < 0)
                            _focusedInGameIndex = navElements.Count - 1;
                    }
                    else
                    {
                        _focusedInGameIndex++;
                        if (_focusedInGameIndex >= navElements.Count)
                            _focusedInGameIndex = 0;
                    }
                    _focusedInGameElement = navElements[_focusedInGameIndex];
                }
            }

            // Arrow keys:
            // - Up/Down always navigate focus (previous/next element)
            // - Left/Right also navigate focus UNLESS the focused element is a SliderNumber or SliderText
            //   (in which case they adjust the value: Left=decrease, Right=increase)
            if (navElements.Count > 0 && _focusedInGameIndex >= 0 && _focusedInGameElement != null)
            {
                // ── Slider value adjustment (Left/Right when focused on a slider) ──
                bool isSlider = _focusedInGameElement.Type == UIElementType.SliderNumber ||
                                _focusedInGameElement.Type == UIElementType.SliderText;
                bool leftPressed = ImGui.IsKeyPressed(ImGuiKey.LeftArrow, false);
                bool rightPressed = ImGui.IsKeyPressed(ImGuiKey.RightArrow, false);

                if (isSlider && (leftPressed || rightPressed))
                {
                    if (_focusedInGameElement.Type == UIElementType.SliderNumber)
                    {
                        float step = Math.Max(0.001f, _focusedInGameElement.Step);
                        float delta = rightPressed ? step : -step;
                        float newVal = _focusedInGameElement.CurrentValue + delta;
                        newVal = Math.Clamp(newVal, _focusedInGameElement.MinValue, _focusedInGameElement.MaxValue);
                        if (step >= 1f)
                            newVal = MathF.Round(newVal / step, MidpointRounding.AwayFromZero) * step;
                        _focusedInGameElement.CurrentValue = newVal;
                        Console.WriteLine($"[IDE] Slider '{_focusedInGameElement.Name}' = {newVal:F2}");
                    }
                    else // SliderText
                    {
                        int maxIdx = _focusedInGameElement.TextOptions.Count - 1;
                        if (maxIdx >= 0)
                        {
                            int newIdx = _focusedInGameElement.SelectedTextIndex + (rightPressed ? 1 : -1);
                            newIdx = Math.Clamp(newIdx, 0, maxIdx);
                            _focusedInGameElement.SelectedTextIndex = newIdx;
                            Console.WriteLine($"[IDE] SliderText '{_focusedInGameElement.Name}' = idx {newIdx} ('{_focusedInGameElement.TextOptions[newIdx]}')");
                        }
                    }
                }
                else
                {
                    // ── Focus navigation (Up/Down, or Left/Right when NOT on a slider) ──
                    bool arrowNext = ImGui.IsKeyPressed(ImGuiKey.DownArrow, false) ||
                                     (ImGui.IsKeyPressed(ImGuiKey.RightArrow, false) && !isSlider);
                    bool arrowPrev = ImGui.IsKeyPressed(ImGuiKey.UpArrow, false) ||
                                     (ImGui.IsKeyPressed(ImGuiKey.LeftArrow, false) && !isSlider);

                    if (arrowNext)
                    {
                        _focusedInGameIndex = (_focusedInGameIndex + 1) % navElements.Count;
                        _focusedInGameElement = navElements[_focusedInGameIndex];
                    }
                    else if (arrowPrev)
                    {
                        _focusedInGameIndex--;
                        if (_focusedInGameIndex < 0)
                            _focusedInGameIndex = navElements.Count - 1;
                        _focusedInGameElement = navElements[_focusedInGameIndex];
                    }
                }
            }

            // Ensure valid focused element
            if (_focusedInGameIndex >= 0 && _focusedInGameIndex < navElements.Count)
                _focusedInGameElement = navElements[_focusedInGameIndex];
            else if (navElements.Count > 0)
            {
                _focusedInGameIndex = 0;
                _focusedInGameElement = navElements[0];
            }
            else
            {
                _focusedInGameIndex = -1;
                _focusedInGameElement = null;
            }

            // Enter / Space to activate focused element (simulate click)
            bool activatePressed = ImGui.IsKeyPressed(ImGuiKey.Enter, false) ||
                                    ImGui.IsKeyPressed(ImGuiKey.Space, false);

            // If root IS a Container, render it alone — recursion handles its children.
            // Otherwise render root's children directly (multi-element scene).
            var renderElements = activeEditScene.Root.Type == UIElementType.Container
                ? (IReadOnlyList<UIElement>)[activeEditScene.Root]
                : activeEditScene.Root.Children;

            _viewport.RenderUIElements(
                drawList,
                new Vector2(ox, oy), new Vector2(ox + canvasW, oy + canvasH),
                virtualW, virtualH,
                renderElements,
                ImGui.GetMousePos(),
                ImGui.IsMouseClicked(ImGuiMouseButton.Left),
                isPreview: true,
                isMouseDown: ImGui.IsMouseDown(ImGuiMouseButton.Left),
                focusedElement: _focusedInGameElement,
                keyboardActivate: activatePressed);

            // ── Sync mouse click to keyboard focus ──
            // If the user clicked an element with the mouse, update keyboard focus to match.
            if (_viewport.LastInGameClickedElement != null)
            {
                var clicked = _viewport.LastInGameClickedElement;
                _viewport.LastInGameClickedElement = null; // consume

                int clickIdx = navElements.IndexOf(clicked);
                if (clickIdx >= 0)
                {
                    _focusedInGameIndex = clickIdx;
                    _focusedInGameElement = clicked;
                    Console.WriteLine($"[IDE] Mouse click synced keyboard focus to '{clicked.Name}'");
                }
            }
        }

        // ── Detect scene change and switch camera to new scene's Camera object ──
        string? currentScene = Bridge.SelectedEditorScene;
        if (!string.IsNullOrEmpty(currentScene) && currentScene != _lastInGameSceneName)
        {
            _lastInGameSceneName = currentScene;
            SwitchToGameCamera();
        }

        // ── Stats overlay (in-game mode) — always visible, drawn last so it sits on top ──
        // Mirrors the GameScene debug HUD: FPS/frame-time, triangles, object counts,
        // and camera position. Reads the bridge values SceneManager refreshes every frame.
        {
            float fps = Bridge.Fps;
            float frameMs = Bridge.FrameMs;
            // Prefer the live camera object when the bridge position is stale (e.g. scenes
            // that only set Bridge.Camera and not Bridge.CameraPosition).
            var camPos = Bridge.Camera?.Position ?? Bridge.CameraPosition;

            var fpsCol = fps switch
            {
                >= 55f => new Vector4(0.3f, 0.9f, 0.3f, 1f),
                >= 30f => new Vector4(0.9f, 0.8f, 0.2f, 1f),
                _ => new Vector4(0.9f, 0.3f, 0.2f, 1f)
            };
            var objCol = new Vector4(0.3f, 0.8f, 1.0f, 1f);
            var trisCol = new Vector4(1f, 0.7f, 0.3f, 1f);
            var timeCol = new Vector4(0.6f, 1f, 0.5f, 1f);
            var posCol = new Vector4(0.9f, 0.9f, 0.9f, 1f);
            var dimCol = new Vector4(0.7f, 0.7f, 0.8f, 1f);

            string[] lines =
            [
                $"FPS: {fps:F0}  ({frameMs:F1} ms)",
                $"TRIS: {Bridge.RenderedTriangles:N0} / {Bridge.TotalTriangles:N0}",
                $"Objects: {Bridge.DrawnObjects:N0} / {Bridge.TotalObjects:N0}  (anim {Bridge.AnimatedObjectCount:N0} | static {Bridge.StaticObjectCount:N0})",
                $"Render: t={Bridge.RenderTerrainMs:N1}ms  o={Bridge.RenderObjectsMs:N1}ms  tot={Bridge.RenderTotalMs:N1}ms",
                $"POS: X={camPos.X:N2}  Y={camPos.Y:N2}  Z={camPos.Z:N2}",
                $"Yaw: {Bridge.CameraYaw:F1}°  Pitch: {Bridge.CameraPitch:F1}°",
            ];

            var font = ImGui.GetFont();
            float fontSize = 18f;
            float lineHeight = 20f;
            float pad = 6f;

            // Measure the widest line for the backing panel
            float panelW = 0f;
            for (int li = 0; li < lines.Length; li++)
            {
                float w = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, lines[li]).X;
                if (w > panelW) panelW = w;
            }
            float panelH = lineHeight * lines.Length + pad * 2f;

            var bgMin = new Vector2(10f, 10f);
            var bgMax = new Vector2(10f + panelW + pad * 2f, 10f + panelH);
            drawList.AddRectFilled(bgMin, bgMax,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.45f)), 6f);
            drawList.AddRect(bgMin, bgMax,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.12f)), 6f, ImDrawFlags.None, 1f);

            // Symmetric padding: first line starts at bgMin + pad, spacing is lineHeight
            float ty = bgMin.Y + pad;
            for (int li = 0; li < lines.Length; li++)
            {
                var col = li switch
                {
                    0 => fpsCol,
                    1 => trisCol,
                    2 => objCol,
                    3 => timeCol,
                    4 => posCol,
                    _ => dimCol,
                };
                drawList.AddText(font, fontSize, new Vector2(bgMin.X + pad, ty),
                    ImGui.ColorConvertFloat4ToU32(col), lines[li]);
                ty += lineHeight;
            }
        }

        // ── Camera warning overlay (shown when no Camera object found) ──
        float dt = io.DeltaTime;
        RenderInGameWarning(dt);

        // ── Render transition overlay (if any) while ImGui frame is active ──
        try
        {
            Bridge.SceneManager?.RenderTransitionOverlay();
        }
        catch { }

        // No ImGui windows at all — just flush the draw list
        _imgui.Render();
    }

    /// <summary>Depth-first flatten visible interactive elements for keyboard navigation.</summary>
    private static void FlattenVisibleInteractive(IReadOnlyList<UIElement> elements, List<UIElement> result)
    {
        foreach (var elem in elements)
        {
            if (!elem.IsVisible) continue;
            // Interactive types only: Button, Checkbox, Dropdown, SliderNumber, SliderText, TextBox
            if (elem.Type == UIElementType.Button ||
                elem.Type == UIElementType.Checkbox ||
                elem.Type == UIElementType.Dropdown ||
                elem.Type == UIElementType.SliderNumber ||
                elem.Type == UIElementType.SliderText ||
                elem.Type == UIElementType.TextBox ||
                elem.Type == UIElementType.RadioButton)
            {
                result.Add(elem);
            }
            if (elem.Children.Count > 0)
                FlattenVisibleInteractive(elem.Children, result);
        }
    }

    /// <summary>
    /// Get a spawn position aligned to the editor grid (Z=0, X snapped to grid squares),
    /// slightly in front of the camera, or a default offset if no camera is set.
    /// </summary>
    private Vector3 GetSpawnPosition(EditorPrimitiveType type)
    {
        return IDEBridge.GetGridSpawnPosition(Bridge.Camera, type);
    }

    /// <summary>Apply persisted viewport snap/grid prefs at startup (called by Program.cs).</summary>
    public void SetViewportSnap(bool enabled, float gridSize)
    {
        _viewport.SnapEnabled = enabled;
        _viewport.SnapGridSize = gridSize;
    }

    /// <summary>Whether the IDE overlay is currently active.</summary>
    public bool IsActive { get; set; } = true;

    public void Dispose()
    {
        if (IsHealthy)
            _imgui.Dispose();
    }
}
