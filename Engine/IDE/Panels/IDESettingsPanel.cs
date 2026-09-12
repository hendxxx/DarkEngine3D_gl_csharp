using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Libs;
using ImGuiNET;
using System.Numerics;
using static DarkEngine3D_gl_csharp.Engine.Config.SettingsSave;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// IDE Settings panel — VSync, Antialiasing (MSAA), font sizes, and editor preferences.
/// </summary>
public class IDESettingsPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = false;

    // ── Cached settings (read from SettingsData on open) ──
    private bool _vsync;
    private int _msaaSamples; // 0=Off(1x), 1=2x, 2=4x, 3=8x
    private bool _showCursorInGame;
    private bool _showDebugGrid;
    private bool _showShadows;
    private float _ideFontSize;
    private float _viewportFontSize;
    private float _toolbarFontSize;

    private static readonly string[] MSAAOptions = ["Off (1x)", "2x", "4x", "8x"];
    private static readonly int[] MSAASamples = [1, 2, 4, 8];

    public IDESettingsPanel(IDEBridge bridge)
    {
        _bridge = bridge;
    }

    public void ShowInMenu() => ImGui.MenuItem("IDE Settings", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;

        ImGui.SetNextWindowSize(new Vector2(400, 500), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("IDE Settings", ref _visible))
        {
            IDE.PanelFocus.Notify("IDE Settings");
            // Load from current settings each frame (in case external changes)
            LoadFromSettings();

            // ── Rendering ──
            if (ImGui.CollapsingHeader("Rendering", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // VSync
                bool vsync = _vsync;
                if (ImGui.Checkbox("VSync", ref vsync))
                {
                    _vsync = vsync;
                    Glfw.SetSwapInterval(vsync ? 1 : 0);
                    SaveSetting(s => s.VSync = vsync);
                    Console.WriteLine($"[IDESettings] VSync: {(vsync ? "ON" : "OFF")}");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Synchronizes frame rate to monitor refresh rate.\nON = smooth but capped to monitor Hz.\nOFF = uncapped FPS (higher CPU/GPU usage).");

                // MSAA
                int msaaIdx = GetMSAAIndex(_msaaSamples);
                ImGui.PushItemWidth(150);
                if (ImGui.Combo("Antialiasing (MSAA)", ref msaaIdx, MSAAOptions, MSAAOptions.Length))
                {
                    _msaaSamples = MSAASamples[msaaIdx];
                    QualitySettings.MsaaSamples = _msaaSamples;
                    SaveSetting(s => s.QualityPreset = msaaIdx);
                    Console.WriteLine($"[IDESettings] MSAA: {_msaaSamples}x");
                }
                ImGui.PopItemWidth();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Multi-Sample Anti-Aliasing.\nHigher = smoother edges but more GPU usage.\nRequires restart to take full effect.");

                // Show Shadows
                bool shadows = _showShadows;
                if (ImGui.Checkbox("Editor Shadows", ref shadows))
                {
                    _showShadows = shadows;
                    SaveSetting(s => s.ShowShadows = shadows);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Toggle shadow rendering in the editor viewport.");

                // Show Debug Grid
                bool grid = _showDebugGrid;
                if (ImGui.Checkbox("Debug Grid", ref grid))
                {
                    _showDebugGrid = grid;
                    SaveSetting(s => s.ShowDebugGrid = grid);
                }
            }

            ImGui.Separator();

            // ── Interface ──
            if (ImGui.CollapsingHeader("Interface", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // IDE Font Size
                float ideFont = _ideFontSize;
                ImGui.PushItemWidth(200);
                if (ImGui.SliderFloat("IDE Font Size", ref ideFont, 10f, 40f, "%.0f px"))
                {
                    _ideFontSize = ideFont;
                    SaveSetting(s => s.IDEFontSize = ideFont);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Font size for editor panels (Inspector, Hierarchy, etc.).\nRequires restart to apply.");

                // Viewport Font Size
                float vpFont = _viewportFontSize;
                if (ImGui.SliderFloat("Viewport Font Size", ref vpFont, 8f, 30f, "%.0f px"))
                {
                    _viewportFontSize = vpFont;
                    SaveSetting(s => s.ViewportFontSize = vpFont);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Font size for the viewport toolbar and overlays.\nRequires restart to apply.");

                // Toolbar Font Size
                float tbFont = _toolbarFontSize;
                if (ImGui.SliderFloat("Toolbar Font Size", ref tbFont, 8f, 24f, "%.0f px"))
                {
                    _toolbarFontSize = tbFont;
                    SaveSetting(s => s.ToolbarFontSize = tbFont);
                }
                ImGui.PopItemWidth();
            }

            ImGui.Separator();

            // ── Gameplay ──
            if (ImGui.CollapsingHeader("Gameplay"))
            {
                bool cursor = _showCursorInGame;
                if (ImGui.Checkbox("Show Cursor In-Game", ref cursor))
                {
                    _showCursorInGame = cursor;
                    SaveSetting(s => s.ShowCursorInGame = cursor);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Keep mouse cursor visible during in-game mode (F9).");

                // FOV
                var settings = Load();
                int fov = settings.Fov;
                ImGui.PushItemWidth(200);
                if (ImGui.SliderInt("FOV", ref fov, 30, 120, "%d°"))
                {
                    settings.Fov = fov;
                    Save(settings);
                }
                ImGui.PopItemWidth();
            }

            ImGui.Separator();

            // ── Camera Zoom (per project) ──
            if (ImGui.CollapsingHeader("Camera Zoom"))
            {
                var settingsZ = Load();
                ImGui.PushItemWidth(200);
                float zMin = settingsZ.OrthoZoomMin;
                if (ImGui.DragFloat("Ortho Zoom Min", ref zMin, 0.5f, 0.5f, 100f, "%.1f"))
                {
                    settingsZ.OrthoZoomMin = zMin;
                    Save(settingsZ);
                    Engine.Visual.Camera.ApplyZoomLimits(zMin, settingsZ.OrthoZoomMax);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Closest ortho zoom (smallest view half-height).\nPixel-art levels: raise this (e.g. 5) so players can't zoom in past crisp pixel scale.\nApplies to scroll wheel + Ortho Zoom slider. Per project, saved in settings.json.");

                float zMax = settingsZ.OrthoZoomMax;
                if (ImGui.DragFloat("Ortho Zoom Max", ref zMax, 5f, 1f, 2000f, "%.0f"))
                {
                    settingsZ.OrthoZoomMax = zMax;
                    Save(settingsZ);
                    Engine.Visual.Camera.ApplyZoomLimits(settingsZ.OrthoZoomMin, zMax);
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Farthest ortho zoom (largest view half-height).\nLarge worlds: raise it; pixel-art levels: lower it (e.g. 60).\nApplies to scroll wheel + Ortho Zoom slider. Per project, saved in settings.json.");
                ImGui.PopItemWidth();

                ImGui.TextDisabled($"current range: {Engine.Visual.Camera.OrthoZoomMin:0.##} – {Engine.Visual.Camera.OrthoZoomMax:0.#}");

                if (ImGui.SmallButton("Reset Zoom Limits"))
                {
                    var s = Load();
                    s.OrthoZoomMin = 2f;
                    s.OrthoZoomMax = 200f;
                    Save(s);
                    Engine.Visual.Camera.ApplyZoomLimits(2f, 200f);
                }
                ImGui.SameLine();
                ImGui.TextDisabled("default 2 – 200");
            }

            ImGui.Separator();

            // ── Info ──
            if (ImGui.CollapsingHeader("Info"))
            {
                unsafe
                {
                    int maxSamples = 0;
                    GL.GetIntegerv(Const.GL_MAX_SAMPLES, &maxSamples);
                    ImGui.Text($"Max MSAA Samples: {maxSamples}");
                }
                ImGui.Text($"Current MSAA: {QualitySettings.MsaaSamples}x");
                ImGui.Text($"Quality Preset: {QualitySettings.PresetName(QualitySettings.Current)}");
            }

            ImGui.Separator();

            // ── Reset ──
            if (ImGui.Button("Reset to Defaults"))
            {
                _vsync = false;
                _msaaSamples = 1;
                _showCursorInGame = false;
                _showDebugGrid = false;
                _showShadows = true;
                _ideFontSize = 20f;
                _viewportFontSize = 16f;
                _toolbarFontSize = 14f;
                Glfw.SetSwapInterval(0);
                QualitySettings.MsaaSamples = 1;
                var s = Load();
                s.VSync = false;
                s.QualityPreset = 0;
                s.ShowCursorInGame = false;
                s.ShowDebugGrid = false;
                s.ShowShadows = true;
                s.IDEFontSize = 20f;
                s.ViewportFontSize = 16f;
                s.ToolbarFontSize = 14f;
                                    Save(s);
                Console.WriteLine("[IDESettings] Reset to defaults");
            }
            ImGui.SameLine();
            ImGui.TextDisabled("VSync + MSAA may require restart");
        }
        ImGui.End();
    }

    private void LoadFromSettings()
    {
        var s = Load();
        _vsync = s.VSync;
        _msaaSamples = QualitySettings.MsaaSamples;
        _showCursorInGame = s.ShowCursorInGame;
        _showDebugGrid = s.ShowDebugGrid;
        _showShadows = s.ShowShadows;
        _ideFontSize = s.IDEFontSize;
        _viewportFontSize = s.ViewportFontSize;
        _toolbarFontSize = s.ToolbarFontSize;
    }

    private static int GetMSAAIndex(int samples) => samples switch
    {
        2 => 1,
        4 => 2,
        8 => 3,
        _ => 0
    };

    private static void SaveSetting(Action<SettingsData> apply)
    {
        var s = Load();
        apply(s);
        Save(s);
    }
}
