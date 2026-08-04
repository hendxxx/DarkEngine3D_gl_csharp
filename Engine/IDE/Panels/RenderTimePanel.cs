using DarkEngine3D_gl_csharp.Engine.Objects;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Render Time panel — shows a detailed breakdown of how long each object took
/// to render in the last frame, plus the overall render-stage timings
/// (terrain / objects / post-process / total). Useful for finding perf hotspots.
/// </summary>
public class RenderTimePanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    // Sort order: 0 = time desc, 1 = name asc, 2 = triangles desc
    private int _sortMode = 0;

    public RenderTimePanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Render Time", null, ref _visible);

    public void Render()
    {
        // Only ask GameScene to capture per-object timings while this panel is open,
        // so the main render path stays allocation-free when profiling is off.
        _bridge.CaptureRenderTimings = _visible;
        if (!_visible) return;

        ImGui.Begin("Render Time", ref _visible);
        // Sync the capture flag in case the user closed the window via the X button
        _bridge.CaptureRenderTimings = _visible;

        // ── Overall render-stage breakdown ──
        if (ImGui.CollapsingHeader("Stage Breakdown", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float terrain = _bridge.RenderTerrainMs;
            float objects = _bridge.RenderObjectsMs;
            float postfx = _bridge.RenderPostFxMs;
            float total = _bridge.RenderTotalMs;
            if (total < 0.0001f) total = terrain + objects + postfx;

            DrawStageRow("Terrain", terrain, total);
            DrawStageRow("Objects", objects, total);
            DrawStageRow("PostFX", postfx, total);
            ImGui.Separator();
            DrawStageRow("Total", total, total, bold: true);

            ImGui.TextDisabled($"Frame: {_bridge.FrameMs:F1} ms  ({_bridge.Fps:F0} FPS)");
        }

        // ── Per-object breakdown ──
        if (ImGui.CollapsingHeader("Per-Object", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var timings = _bridge.ObjectRenderTimings;
            if (timings == null || timings.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f),
                    "No object timing data. Run the GameScene to populate.");
            }
            else
            {
                // Sort controls
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4f, 3f));
                ImGui.Text("Sort:");
                ImGui.SameLine();
                if (ImGui.RadioButton("Time", _sortMode == 0)) _sortMode = 0;
                ImGui.SameLine();
                if (ImGui.RadioButton("Name", _sortMode == 1)) _sortMode = 1;
                ImGui.SameLine();
                if (ImGui.RadioButton("Tris", _sortMode == 2)) _sortMode = 2;
                ImGui.PopStyleVar();

                // Build sorted copy
                var sorted = timings.ToList();
                switch (_sortMode)
                {
                    case 1: sorted.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name)); break;
                    case 2: sorted.Sort((a, b) => b.Triangles.CompareTo(a.Triangles)); break;
                    default: sorted.Sort((a, b) => b.TimeMs.CompareTo(a.TimeMs)); break;
                }

                float totalObjMs = timings.Count > 0 ? timings.Sum(t => t.TimeMs) : 0f;

                if (ImGui.BeginTable("render_time_table", 4,
                        ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
                        new Vector2(0f, 300f)))
                {
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1f);
                    ImGui.TableSetupColumn("ms", ImGuiTableColumnFlags.WidthFixed, 70f);
                    ImGui.TableSetupColumn("%", ImGuiTableColumnFlags.WidthFixed, 55f);
                    ImGui.TableSetupColumn("Tris", ImGuiTableColumnFlags.WidthFixed, 80f);
                    ImGui.TableHeadersRow();

                    for (int i = 0; i < sorted.Count; i++)
                    {
                        var t = sorted[i];
                        float pct = totalObjMs > 0.0001f ? t.TimeMs / totalObjMs * 100f : 0f;

                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextColored(t.IsAnimated
                            ? new Vector4(0.5f, 0.8f, 1f, 1f)
                            : new Vector4(0.8f, 0.8f, 0.55f, 1f),
                            t.IsAnimated ? "▸ " + t.Name : "▣ " + t.Name);
                        ImGui.TableNextColumn();
                        // Color code by cost
                        var msCol = t.TimeMs >= 1f ? new Vector4(0.9f, 0.3f, 0.2f, 1f)
                            : t.TimeMs >= 0.3f ? new Vector4(0.9f, 0.8f, 0.2f, 1f)
                            : new Vector4(0.6f, 0.9f, 0.6f, 1f);
                        ImGui.TextColored(msCol, $"{t.TimeMs:F2}");
                        ImGui.TableNextColumn();
                        ImGui.TextColored(pct >= 10f ? new Vector4(1f, 0.7f, 0.3f, 1f) : new Vector4(0.8f, 0.8f, 0.8f, 1f), $"{pct:F1}%");
                        ImGui.TableNextColumn();
                        ImGui.Text($"{t.Triangles:N0}");
                    }

                    // Footer total
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(1f, 1f, 1f, 0.9f), "Σ Total");
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(1f, 0.9f, 0.5f, 1f), $"{totalObjMs:F2}");
                    ImGui.TableNextColumn();
                    ImGui.TextDisabled("100%");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{timings.Sum(t => t.Triangles):N0}");

                    ImGui.EndTable();
                }

                ImGui.TextDisabled($"{timings.Count} entries · {totalObjMs:F2} ms total object render");
            }
        }

        ImGui.End();
    }

    /// <summary>Draw one stage-breakdown row with an inline bar proportional to total time.</summary>
    private static void DrawStageRow(string label, float value, float total, bool bold = false)
    {
        float frac = total > 0.0001f ? Math.Clamp(value / total, 0f, 1f) : 0f;
        var col = bold ? new Vector4(1f, 1f, 1f, 1f)
            : value >= 1f ? new Vector4(0.9f, 0.4f, 0.3f, 1f)
            : value >= 0.3f ? new Vector4(0.9f, 0.8f, 0.3f, 1f)
            : new Vector4(0.6f, 0.9f, 0.6f, 1f);

        if (bold)
            ImGui.TextColored(col, label);
        else
            ImGui.Text(label);

        ImGui.SameLine();
        float avail = ImGui.GetContentRegionAvail().X;
        float barW = avail - 90f;
        if (barW > 20f)
        {
            var p0 = ImGui.GetCursorScreenPos();
            p0.Y += 3f;
            var p1 = p0 + new Vector2(barW, 10f);
            ImGui.GetWindowDrawList().AddRectFilled(p0, p1,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.25f, 1f)), 2f);
            if (frac > 0.001f)
                ImGui.GetWindowDrawList().AddRectFilled(p0, p0 + new Vector2(barW * frac, 10f),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(col.X, col.Y, col.Z, 0.85f)), 2f);
            ImGui.SameLine();
            ImGui.TextColored(col, $"{value:F2} ms");
        }
        else
        {
            ImGui.SameLine();
            ImGui.TextColored(col, $"{value:F2} ms");
        }
    }
}
