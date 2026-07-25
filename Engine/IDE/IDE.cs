using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.IDE.Panels;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
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
        if (ImGui.BeginMainMenuBar())
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
                // ── IDE Mode / Preview Mode (radio-like, mutually exclusive) ──
                bool isPreview = _viewport.PreviewMode;
                if (ImGui.MenuItem("IDE Mode", null, !isPreview, true))
                    _viewport.PreviewMode = false;
                if (ImGui.MenuItem("Preview Mode", "F5", isPreview, true))
                    _viewport.PreviewMode = true;

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
