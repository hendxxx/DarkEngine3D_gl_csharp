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

    private bool _showDemoWindow = false;

    public bool IsHealthy { get; private set; } = false;

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
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Exit IDE", "F2"))
                    ToggleRequested = false;
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("View"))
            {
                ImGui.MenuItem("Demo Window", "", ref _showDemoWindow);
                ImGui.Separator();
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
            if (ImGui.BeginMenu("Help"))
            {
                ImGui.Text("DarkEngine IDE v0.1");
                ImGui.Text("F2 to toggle IDE overlay");
                ImGui.EndMenu();
            }

            // ── Input lock toggle button (right side of menu bar) ──
            ImGui.SameLine(ImGui.GetWindowWidth() - 150f);
            bool isInGameActive = Bridge.InGameActive;
            Vector4 btnColor = isInGameActive
                ? new Vector4(0.20f, 0.65f, 0.25f, 1f) // red = active
                : new Vector4(0.85f, 0.25f, 0.20f, 1f); // green = inactive
            ImGui.PushStyleColor(ImGuiCol.Button, btnColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, btnColor * 1.2f);
            string label = isInGameActive ? "[INGAME ACTIVE YES] (F9)" : "[INGAME ACTIVE NO] (F9)";
            if (ImGui.Button(label))
            {
                Bridge.InGameActive = !isInGameActive;
                // Persist to settings.json
                var settings = Config.SettingsSave.Load();
                settings.InGameActive = Bridge.InGameActive;
                Config.SettingsSave.Save(settings);
                Console.WriteLine($"[IDE] Toggle InGameActive: {Bridge.InGameActive}");
            }
            ImGui.PopStyleColor(2);

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

        // ── Demo window ──
        if (_showDemoWindow)
            ImGui.ShowDemoWindow(ref _showDemoWindow);

        // ── Render ImGui draw data ──
        _imgui.Render();
    }

    /// <summary>Set by Program.cs when F2 is pressed.</summary>
    public bool ToggleRequested { get; set; } = true;
    public bool IsActive { get; set; } = true;

    public void Dispose()
    {
        if (IsHealthy)
            _imgui.Dispose();
    }
}
