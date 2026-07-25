using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.IDE.Panels;
using DarkEngine3D_gl_csharp.Engine.Libs;
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

    public bool IsHealthy { get; private set; }

    // ── Mode toggles ──
    private bool _inGameMode = false;
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
                    // Save all editor scenes to game.ing first, then reload for in-game mode
                    Console.WriteLine("[IDE] Saving editor scenes before entering in-game mode...");
                    Bridge.SaveAllScenes?.Invoke();
                    LoadDefaultGameIng();
                    _viewport.SetFullscreen(true);
                }
                else
                {
                    // Reset fullscreen mode when exiting in-game mode
                    _viewport.SetFullscreen(false);
                }
                Console.WriteLine($"[IDE] In-Game Mode: {_inGameMode}");
            }
        }
    }
    /// <summary>Toggle in-game mode on/off. F8 shortcut.</summary>
    public void ToggleInGameMode() => InGameMode = !_inGameMode;

    /// <summary>Load game.ing from disk every time in-game mode is entered.
    /// Replaces any existing editor scenes with the freshly loaded data.</summary>
    private void LoadDefaultGameIng()
    {
        string gameIngPath = SceneAssetSerializer.GameIngPath;
        if (!File.Exists(gameIngPath))
        {
            Console.WriteLine($"[IDE] game.ing not found at: {gameIngPath}");
            return;
        }

        Console.WriteLine($"[IDE] Loading game.ing for in-game mode...");
        _sceneManagerPanel.LoadGameIngScenes();
        Console.WriteLine($"[IDE] Loaded game.ing ({Bridge.EditorScenes.Count} scenes)");
    }

    public IDE(nint window)
    {
        try
        {
            Console.WriteLine("[IDE] Creating ImGuiController...");
            _imgui = new ImGuiController(window);
            Console.WriteLine("[IDE] ImGuiController OK");

            _viewport = new ViewportPanel(Bridge);
            _sceneView = new SceneViewPanel(Bridge);
            _inspector = new InspectorPanel(Bridge);
            _assetBrowser = new AssetBrowserPanel(Bridge);
            _console = new ConsolePanel(Bridge);
            _sceneManagerPanel = new SceneManagerPanel(Bridge);
            _hierarchy = new HierarchyPanel(Bridge);

            // Wire save integration: HierarchyPanel can trigger SceneManager's Save All / Save As
            Bridge.SaveAllScenes = () => _sceneManagerPanel.SaveAllEditorScenesPublic();
            Bridge.RequestSaveAsDialog = () => _sceneManagerPanel.OpenSaveAsDialog();

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

        // ── In-Game Mode: render full-screen viewport with no ImGui chrome ──
        if (_inGameMode)
        {
            RenderInGameMode();
            return;
        }

        // ── Restore editor scene root (game scenes overwrite bridge.SceneRoot each frame) ──
        // Without this, ViewportPanel, HierarchyPanel, and InspectorPanel would see the
        // game scene's empty _sceneRoot instead of the loaded editor scene's UI elements.
        if (Bridge.SelectedEditorScene != null &&
            Bridge.EditorScenes.TryGetValue(Bridge.SelectedEditorScene, out var activeEditorScene))
        {
            Bridge.SceneRoot = activeEditorScene.Root;
            Bridge.SceneRootElements = new List<UIElement> { activeEditorScene.Root }.AsReadOnly();
        }

        // ── Build main menu bar ──
        ImGui.BeginMainMenuBar();
        {
            // ════════════════════════════════════════════════════
            //  File Menu
            // ════════════════════════════════════════════════════
            if (ImGui.BeginMenu("File"))
            {
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
                    if (ImGui.BeginMenu("Open Recent"))
                    {
                        for (int ri = 0; ri < recentFiles.Count; ri++)
                        {
                            string path = recentFiles[ri];
                            string label = System.IO.Path.GetFileName(path);
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

                // Save (Save All)
                bool hasEditorScenes = Bridge.EditorScenes.Count > 0;
                ImGui.BeginDisabled(!hasEditorScenes);
                if (ImGui.MenuItem("Save", "Ctrl+S"))
                    _sceneManagerPanel.SaveAllScenes();
                ImGui.EndDisabled();

                // Save As...
                if (ImGui.MenuItem("Save As...", "Ctrl+Shift+S"))
                    _sceneManagerPanel.OpenSaveAsDialog();

                ImGui.Separator();

                if (ImGui.MenuItem("Exit IDE", "F2"))
                    IsHealthy = false;
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
                    _viewport.SnapEnabled = !snap;

                ImGui.Separator();

                // ── Panel visibility toggles ──
                _viewport.ShowInMenu();
                _sceneView.ShowInMenu();
                _inspector.ShowInMenu();
                _assetBrowser.ShowInMenu();
                _console.ShowInMenu();
                _hierarchy.ShowInMenu();
                ImGui.Separator();
                _sceneManagerPanel.ShowInMenu();
                ImGui.EndMenu();
            }

            // ════════════════════════════════════════════════════
            //  Help Menu
            // ════════════════════════════════════════════════════
            if (ImGui.BeginMenu("Help"))
            {
                ImGui.Text("DarkEngine IDE v0.1");
                ImGui.Text("F2 to toggle IDE overlay");
                ImGui.Separator();
                ImGui.TextDisabled("Ctrl+Z  Undo");
                ImGui.TextDisabled("Ctrl+Y  Redo");
                ImGui.TextDisabled("Ctrl+C  Copy");
                ImGui.TextDisabled("Ctrl+V  Paste");
                ImGui.TextDisabled("Ctrl+D  Duplicate");
                ImGui.TextDisabled("Del     Delete");
                ImGui.TextDisabled("F5      Preview Mode");
                ImGui.TextDisabled("F8      In-Game Mode");
                ImGui.TextDisabled("F9      In-Game Input");
                ImGui.EndMenu();
            }

            // ── Input lock toggle button (right side of menu bar) ──
            ImGui.SameLine(ImGui.GetWindowWidth() - 200f);
            bool isInGameActive = Bridge.InGameActive;
            Vector4 btnColor = isInGameActive
                ? new Vector4(0.20f, 0.65f, 0.25f, 1f) // green = active (in-game input on)
                : new Vector4(0.85f, 0.25f, 0.20f, 1f); // red = inactive (editor mode)
            ImGui.PushStyleColor(ImGuiCol.Button, btnColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, btnColor * 1.2f);
            if (ImGui.Button(isInGameActive ? "In-Game ON" : "In-Game OFF", new Vector2(120, 0)))
            {
                Bridge.InGameActive = !isInGameActive;
            }
            ImGui.PopStyleColor(2);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(isInGameActive
                    ? "In-game input is active (F9 to toggle)"
                    : "In-game input is blocked (F9 to toggle)");

            ImGui.EndMainMenuBar();
        }

        // ── Docking space ──
        ImGui.DockSpaceOverViewport();

        // ── Render panels ──
        _viewport.Render();
        _sceneView.Render();
        _inspector.Render();
        _assetBrowser.Render();
        _hierarchy.Render();
        _console.Render();
        _sceneManagerPanel.Render();

        // ── Render ImGui draw data ──
        _imgui.Render();
    }

    /// <summary>Render a full-screen game viewport with no ImGui chrome.
    /// All editor panels and menu bars are hidden; the game scene fills the entire screen.
    /// A small overlay button allows returning to IDE mode.</summary>
    /// <summary>In-Game Mode: NO ImGui windows at all.
    /// Renders scene texture + UI elements directly to foreground draw list,
    /// then calls _imgui.Render() to flush. Exit via F8 only.</summary>
    private void RenderInGameMode()
    {
        var io = ImGui.GetIO();
        float screenW = io.DisplaySize.X;
        float screenH = io.DisplaySize.Y;

        // Render directly to foreground draw list — no ImGui windows!
        var drawList = ImGui.GetForegroundDrawList();

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
        if (Bridge.EditorScenes.Count > 0 &&
            Bridge.SelectedEditorScene != null &&
            Bridge.EditorScenes.TryGetValue(Bridge.SelectedEditorScene, out var activeEditScene) &&
            activeEditScene.Root.Children.Count > 0)
        {
            // Use scene texture dimensions as the virtual coordinate space
            // (elements' X/Y/W/H are stored relative to this resolution)
            float virtualW = Bridge.SceneTextureWidth > 0 ? Bridge.SceneTextureWidth : 1920f;
            float virtualH = Bridge.SceneTextureHeight > 0 ? Bridge.SceneTextureHeight : 1080f;
            float virtualAspect = virtualW / virtualH;
            float panelAspect = screenW / screenH;

            // Calculate aspect-ratio-corrected canvas centered on screen
            float canvasW, canvasH, ox, oy;
            if (panelAspect > virtualAspect)
            {
                // Screen is wider than texture — use full height, centered horizontally
                canvasH = screenH;
                canvasW = screenH * virtualAspect;
                ox = (screenW - canvasW) * 0.5f;
                oy = 0f;
            }
            else
            {
                // Screen is taller than texture — use full width, centered vertically
                canvasW = screenW;
                canvasH = screenW / virtualAspect;
                ox = 0f;
                oy = (screenH - canvasH) * 0.5f;
            }

            _viewport.RenderUIElements(
                drawList,
                new Vector2(ox, oy), new Vector2(ox + canvasW, oy + canvasH),
                virtualW, virtualH,
                activeEditScene.Root.Children,
                ImGui.GetMousePos(),
                ImGui.IsMouseClicked(ImGuiMouseButton.Left),
                isPreview: true,
                isMouseDown: ImGui.IsMouseDown(ImGuiMouseButton.Left));
        }

        // No ImGui windows at all — just flush the draw list
        _imgui.Render();
    }

    /// <summary>Whether the IDE overlay is currently active. Always true since F2 toggle is disabled.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Set by Program.cs when F2 is pressed (kept for backward compat, currently unused).</summary>
    public bool ToggleRequested { get; set; } = true;

    public void Dispose()
    {
        if (IsHealthy)
            _imgui.Dispose();
    }
}
