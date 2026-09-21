using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Player Info panel — live monitor (and editor) of the player's status values:
/// HP, MP, Level, EXP, Fitness. Values are read from <see cref="Player2DStats"/>
/// which Player2DSystem keeps fresh during play; UI Bars bind to the same slots
/// (Inspector → Bar Properties → "Stat Binding") so HUD bars follow these numbers.
/// Each stat row: label, current/max sliders, fraction bar, quick-fill/reset buttons.
/// </summary>
public class PlayerInfoPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    public PlayerInfoPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Player Info", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;
        ImGui.Begin("Player Info", ref _visible);

        // ── Header: session state ──
        if (Player2DStats.SessionActive)
        {
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.4f, 1f), "● LIVE — values follow gameplay");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.65f, 1f), "○ Not playing — edit defaults here");
        }
        ImGui.Separator();

        DrawStatRow(PlayerStatNames.Health, "HP", new Vector4(0.9f, 0.3f, 0.3f, 1f));
        DrawStatRow(PlayerStatNames.Mana, "MP", new Vector4(0.3f, 0.5f, 0.95f, 1f));
        DrawStatRow(PlayerStatNames.Level, "Level", new Vector4(0.95f, 0.8f, 0.25f, 1f));
        DrawStatRow(PlayerStatNames.Experience, "EXP", new Vector4(0.4f, 0.85f, 0.4f, 1f));
        DrawStatRow(PlayerStatNames.Fitness, "Fitness", new Vector4(0.85f, 0.45f, 0.2f, 1f));

        ImGui.Separator();
        if (ImGui.Button("Reset to Defaults", new Vector2(-1, 0)))
        {
            Player2DStats.ResetToDefaults();
            Console.WriteLine("[PlayerInfo] Stats reset to defaults");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Restore all stats to their default values (also happens at each session start).");

        ImGui.End();
    }

    /// <summary>One stat row: colored label, Current/Max sliders, live fraction bar,
    /// and quick buttons (full / zero). Edits write straight into Player2DStats so a
    /// bound Bar updates the same frame — even mid-play.</summary>
    private static void DrawStatRow(string statName, string label, Vector4 color)
    {
        ImGui.PushID(statName);

        // ── Label + live fraction ──
        float frac = Player2DStats.GetFraction(statName);
        ImGui.TextColored(color, label);
        ImGui.SameLine(70);
        ImGui.Text($"{Player2DStats.GetCurrent(statName):F0} / {Player2DStats.GetMax(statName):F0}");
        ImGui.SameLine(ImGui.GetWindowWidth() - 80);
        ImGui.TextColored(new Vector4(color.X, color.Y, color.Z, 0.8f), $"{frac * 100f:F0}%");

        // ── Fraction bar (visual echo of what a bound Bar shows) ──
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        float w = ImGui.GetContentRegionAvail().X;
        float h = 10f;
        dl.AddRectFilled(p, new Vector2(p.X + w, p.Y + h), ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.18f, 1f)), 3f);
        dl.AddRectFilled(p, new Vector2(p.X + w * frac, p.Y + h), ImGui.ColorConvertFloat4ToU32(color), 3f);
        ImGui.Dummy(new Vector2(w, h + 4f));

        // ── Current / Max editors ──
        float cur = Player2DStats.GetCurrent(statName);
        float max = Player2DStats.GetMax(statName);
        ImGui.SetNextItemWidth(-110);
        if (ImGui.SliderFloat("##cur", ref cur, 0f, max, "%.0f"))
            Player2DStats.SetCurrent(statName, cur);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Current {label}");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-60);
        if (ImGui.DragFloat("##max", ref max, 1f, 1f, 9999f, "max %.0f"))
        {
            Player2DStats.SetMax(statName, max);
            Player2DStats.ClampAll();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Maximum {label} (bar denominator)");
        ImGui.SameLine();
        if (ImGui.SmallButton("Full"))
            Player2DStats.SetCurrent(statName, Player2DStats.GetMax(statName));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Set {label} to max");
        ImGui.SameLine();
        if (ImGui.SmallButton("0"))
            Player2DStats.SetCurrent(statName, 0f);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Set {label} to zero");

        ImGui.Spacing();
        ImGui.PopID();
    }
}
