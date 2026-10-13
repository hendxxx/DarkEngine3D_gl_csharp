using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Render Time panel — shows a detailed breakdown of how long each object took
/// to render in the last frame, plus the overall render-stage timings
/// (objects / post-process / total). Useful for finding perf hotspots.
/// </summary>
public class RenderTimePanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    // Sort order: 0 = time desc, 1 = name asc, 2 = triangles desc
    private int _sortMode = 0;
    // Reused sort buffer for the per-object table (avoids a List + ToList copy
    // allocation every frame while the panel is open).
    private readonly List<RenderTimingSample> _sortScratch = new();

    // ── CSM on/off performance sampling — measures the real cost of the shadow
    // pass by accumulating frame ms separately while shadows are ON vs OFF, and
    // resets both buckets whenever the toggle changes (Shadow panel OR viewport
    // toolbar — both write the same IDEBridge.ShowShadows flag). ──
    private bool? _perfLastShadows = null;   // last sampled ShowShadows state (edge detect)
    private float _perfSumOn = 0f;            // accumulated frame ms with shadows ON
    private float _perfSumOff = 0f;           // accumulated frame ms with shadows OFF
    private int _perfFramesOn = 0;            // frame count in the ON bucket
    private int _perfFramesOff = 0;           // frame count in the OFF bucket

    public RenderTimePanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Render Time", null, ref _visible);

    public void Render()
    {
        // Only ask GameScene to capture per-object timings while this panel is open,
        // so the main render path stays allocation-free when profiling is off.
        _bridge.CaptureRenderTimings = _visible;

        // Sample the CSM on/off frame-time buckets EVERY frame (even when the panel is
        // closed) so the averages are already populated when the panel is opened.
        SamplePerformance();

        if (!_visible) return;

        ImGui.Begin("Render Time", ref _visible);
        IDE.PanelFocus.Notify("Render Time");
        // Sync the capture flag in case the user closed the window via the X button
        _bridge.CaptureRenderTimings = _visible;

        // ── Overall render-stage breakdown ──
        if (ImGui.CollapsingHeader("Stage Breakdown", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float objects = _bridge.RenderObjectsMs;
            float total = _bridge.RenderTotalMs;
            if (total < 0.0001f) total = objects;

            DrawStageRow("Objects", objects, total);
            ImGui.Separator();
            DrawStageRow("Total", total, total, bold: true);

            ImGui.TextDisabled($"Frame: {_bridge.FrameMs:F1} ms  ({_bridge.Fps:F0} FPS)");

            // ── CSM shadow pass cost (measured, not estimated) — toggle the shadow
            // pass off for a few seconds then on; the buckets average each state and
            // the delta below shows exactly what shadows cost. ──
            bool on = _bridge.ShowShadows;
            float onMs = _perfFramesOn > 0 ? _perfSumOn / _perfFramesOn : 0f;
            float offMs = _perfFramesOff > 0 ? _perfSumOff / _perfFramesOff : 0f;

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.8f, 1f, 1f), "CSM Shadows (A/B):");
            if (_perfFramesOn > 0)
                ImGui.Text($"  ON  : {onMs:F1} ms  ({_perfFramesOn} frames)");
            else
                ImGui.TextDisabled("  ON  : — (no frames sampled yet)");
            if (_perfFramesOff > 0)
                ImGui.Text($"  OFF : {offMs:F1} ms  ({_perfFramesOff} frames)");
            else
                ImGui.TextDisabled("  OFF : — (no frames sampled yet)");

            if (_perfFramesOn > 0 && _perfFramesOff > 0)
            {
                float saved = onMs - offMs;
                if (saved > 0.05f)
                {
                    float pct = 100f * saved / onMs;
                    ImGui.TextColored(new Vector4(0.45f, 0.9f, 0.5f, 1f),
                        $"Shadow pass cost: {saved:F1} ms (~{pct:F0}% of the ON frame) → toggle OFF gains ~{MathF.Round(1000f / MathF.Max(offMs, 0.01f) - 1000f / MathF.Max(onMs, 0.01f))} fps");
                }
                else if (saved < -0.05f)
                {
                    ImGui.TextColored(new Vector4(0.9f, 0.6f, 0.3f, 1f),
                        $"OFF is {-saved:F1} ms SLOWER than ON (variance — samples are noisy at low frame times)");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f),
                        $"No measurable difference ({MathF.Abs(saved):F1} ms) — shadow pass cost is negligible here.");
                }
            }
            else
            {
                ImGui.TextDisabled("Tip: toggle shadows OFF for a few seconds, then back ON to measure the difference.");
            }
            ImGui.TextDisabled("Averages reset when the CSM toggle changes (Shadow panel or viewport toolbar).");
        }

        // ── FRAME PROFILER — editor-frame section breakdown + history graph ──
        if (ImGui.CollapsingHeader("Frame Profiler (editor frame)", ImGuiTreeNodeFlags.DefaultOpen))
            DrawFrameProfiler();

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

                // Sort in a reused buffer (no per-frame allocation)
                _sortScratch.Clear();
                foreach (var t in timings) _sortScratch.Add(t);
                var sorted = _sortScratch;
                switch (_sortMode)
                {
                    case 1: sorted.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name)); break;
                    case 2: sorted.Sort((a, b) => b.Triangles.CompareTo(a.Triangles)); break;
                    default: sorted.Sort((a, b) => b.TimeMs.CompareTo(a.TimeMs)); break;
                }

                // Total computed once (the footer used to call Sum() again over all rows).
                float totalObjMs = 0f; long totalTris = 0;
                foreach (var t in timings) { totalObjMs += t.TimeMs; totalTris += t.Triangles; }

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
                    ImGui.Text($"{totalTris:N0}");

                    ImGui.EndTable();
                }

                ImGui.TextDisabled($"{timings.Count} entries · {totalObjMs:F2} ms total object render");
            }
        }

        ImGui.End();
    }

    /// <summary>Accumulate the current frame ms into the ON or OFF bucket, matching
    /// the live CSM state. Detects state changes (Shadow panel OR viewport toolbar)
    /// and resets both buckets so the averages compare clean runs of each state.
    /// Called every frame from Render(), even when the panel is closed.</summary>
    private void SamplePerformance()
    {
        bool shadowsOn = _bridge.ShowShadows;

        // State changed (or first frame) → reset both buckets so each average
        // reflects a continuous run of that state only.
        if (_perfLastShadows != shadowsOn)
        {
            _perfLastShadows = shadowsOn;
            _perfSumOn = 0f;
            _perfSumOff = 0f;
            _perfFramesOn = 0;
            _perfFramesOff = 0;
        }

        float dtMs = Glfw.PeekDeltaTime() * 1000f; // peek: read-only, doesn't slice the shared clock
        // Guard against pathological frame spikes so one hitch doesn't poison the average.
        if (dtMs <= 0f || dtMs > 250f) return;

        if (shadowsOn)
        {
            _perfSumOn += dtMs;
            _perfFramesOn++;
        }
        else
        {
            _perfSumOff += dtMs;
            _perfFramesOff++;
        }
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

    // ── Frame Profiler (editor frame sections, FrameProfiler ring history) ──

    private void DrawFrameProfiler()
    {
        double frame = FrameProfiler.FrameMs;
        if (frame < 0.0001) frame = FrameProfiler.Average(FrameProfiler.HistFrame);

        // Sparkline: the last FrameProfiler.History editor-frame totals, 16-bit-graph
        // style. Scale = max(20 ms, worst sample) so 60 fps sits at ~mid-height.
        float width = ImGui.GetContentRegionAvail().X;
        float height = 56f;
        var dl = ImGui.GetWindowDrawList();
        var p0 = ImGui.GetCursorScreenPos();
        double maxMs = 20.0;
        int n = FrameProfiler.HistCount;
        double worst = 0;
        for (int i = 0; i < n; i++)
            if (FrameProfiler.Sample(FrameProfiler.HistFrame, i) > worst)
                worst = FrameProfiler.Sample(FrameProfiler.HistFrame, i);
        if (worst > maxMs) maxMs = worst;
        Vector2 prev = default;
        for (int i = 0; i < n; i++)
        {
            float x = p0.X + (i + 1) / (float)FrameProfiler.History * width;
            float y = p0.Y + height - (float)(FrameProfiler.Sample(FrameProfiler.HistFrame, i) / maxMs) * height;
            if (i > 0) dl.AddLine(prev, new Vector2(x, y), 0xFF40B0FF, 1.5f); // ABGR: orange
            prev = new Vector2(x, y);
        }
        // 60 fps reference line (16.7 ms).
        float refY = p0.Y + height - (float)(16.7 / maxMs) * height;
        dl.AddLine(new Vector2(p0.X, refY), new Vector2(p0.X + width, refY), 0x6030C030, 1f);
        dl.AddRectFilled(p0, p0 + new Vector2(width, height), 0x3A20201F, 3f);
        ImGui.Dummy(new Vector2(width, height));
        ImGui.TextDisabled($"last {FrameProfiler.History} editor frames · scale {maxMs:F0} ms · green line = 60 fps");

        ImGui.Separator();

        // Section rows: value, share of frame, colored bar.
        DrawSection("Editor frame (total)", FrameProfiler.FrameMs, frame, frame, new Vector4(1f, 1f, 1f, 1f));
        DrawSection("Scene render (engine 3D)", FrameProfiler.SceneMs, frame, frame, new Vector4(0.4f, 0.75f, 1f, 1f));

        // ── Expandable sub-groups: the editor pipeline beyond Scene ──
        if (ImGui.TreeNode("Editor breakdown (expand detail)"))
        {
            DrawSection("Viewport panel (UI/gizmo/brush)", FrameProfiler.ViewportMs, frame, frame, new Vector4(1f, 0.75f, 0.3f, 1f));
            DrawSection("All other panels", FrameProfiler.PanelsMs, frame, frame, new Vector4(0.65f, 0.85f, 0.55f, 1f));
            // Animated sprites: registry sync cost. The sync runs every 8th frame
            // (throttled + signature-gated), so the instantaneous slot reads 0.00 on the
            // other 7 — showing it raw looked "always 0". Instead show the PEAK of the
            // last 8 pushed samples + the current slot: stable between bursts, and a
            // real rebuild shows up as a jump. Watch the HISTORY average below for a
            // sustained every-frame value (rebuild-per-frame regression).
            DrawSection("Sprite registry sync",
                FrameProfiler.MaxRecent(FrameProfiler.HistSpriteRegistry, 8, FrameProfiler.SpriteRegistryMs),
                frame, frame, new Vector4(0.5f, 0.9f, 0.9f, 1f));

            // ImGui costs, grouped and expandable one level further.
            if (ImGui.TreeNode("ImGui (UI framework)"))
            {
                DrawSection("NewFrame (fonts + input poll)", FrameProfiler.ImguiNewFrameMs, frame, frame, new Vector4(0.75f, 0.6f, 1f, 1f));
                DrawSection("Render (vtx/idx upload + draw)", FrameProfiler.ImguiRenderMs, frame, frame, new Vector4(0.6f, 0.7f, 1f, 1f));
                DrawSection("Menu bar redraw", FrameProfiler.MenuBarMs, frame, frame, new Vector4(0.9f, 0.7f, 0.95f, 1f));
                DrawSection("Popups + file dialogs", FrameProfiler.PopupsMs, frame, frame, new Vector4(0.85f, 0.85f, 0.6f, 1f));
                DrawSection("Focus + undo/redo tail", FrameProfiler.EditorMiscMs, frame, frame, new Vector4(0.7f, 0.85f, 0.9f, 1f));

                double imguiTotal = FrameProfiler.ImguiNewFrameMs + FrameProfiler.ImguiRenderMs
                    + FrameProfiler.MenuBarMs + FrameProfiler.PopupsMs + FrameProfiler.EditorMiscMs;
                double ioPct = frame > 0.0001 ? (imguiTotal + FrameProfiler.PanelsMs + FrameProfiler.ViewportMs) / frame * 100.0 : 0.0;
                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1f),
                    $"ImGui total: {imguiTotal:F2} ms · editor UI overhead {ioPct:F0}% of frame");
                ImGui.TreePop();
            }

            // Per-group frame history averages over the ring buffer (slow trend view).
            if (ImGui.TreeNode("History averages (last 240 frames)"))
            {
                DrawHistRow("Frame", FrameProfiler.HistFrame);
                DrawHistRow("Scene", FrameProfiler.HistScene);
                DrawHistRow("Viewport", FrameProfiler.HistViewport);
                DrawHistRow("Panels", FrameProfiler.HistPanels);
                DrawHistRow("ImGui NewFrame", FrameProfiler.HistImguiNewFrame);
                DrawHistRow("ImGui Render", FrameProfiler.HistImguiRender);
                DrawHistRow("Menu bar", FrameProfiler.HistMenuBar);
                DrawHistRow("Popups", FrameProfiler.HistPopups);
                DrawHistRow("Misc tail", FrameProfiler.HistMisc);
                DrawHistRow("Sprite registry sync", FrameProfiler.HistSpriteRegistry);
                ImGui.TreePop();
            }

            ImGui.TreePop();
        }

        ImGui.Separator();
        ImGui.TextDisabled("Terrain sculpt (while a session is ON):");
        double sculptTotal = FrameProfiler.SculptPickMs + FrameProfiler.SculptStampMs + FrameProfiler.SculptOverlayMs;
        DrawSection("· Pick (CPU ray-march)", FrameProfiler.SculptPickMs, frame, frame, new Vector4(0.95f, 0.55f, 0.9f, 1f));
        DrawSection("· Stamp + upload", FrameProfiler.SculptStampMs, frame, frame, new Vector4(0.95f, 0.4f, 0.4f, 1f));
        DrawSection("· Ring + outline", FrameProfiler.SculptOverlayMs, frame, frame, new Vector4(0.9f, 0.9f, 0.4f, 1f));
        DrawSection("· Sculpt total", sculptTotal, frame, frame, new Vector4(1f, 0.6f, 0.2f, 1f));

        if (sculptTotal > 0.5)
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f),
                $"Sculpt is eating {sculptTotal / Math.Max(frame, 0.01) * 100f:F0}% of the frame!");
    }

    /// <summary>One compact history row: section name + ring average + last sample.</summary>
    private void DrawHistRow(string label, double[] hist)
    {
        ImGui.Text(label);
        ImGui.SameLine(230);
        ImGui.Text($"{FrameProfiler.Average(hist),7:F2} ms");
        ImGui.SameLine(330);
        ImGui.Text($"{FrameProfiler.Sample(hist, Math.Max(0, FrameProfiler.HistCount - 1)),7:F2} last");
    }

    /// <summary>One labeled row: ms value + % of frame + proportional bar.</summary>
    private void DrawSection(string label, double value, double frame, double totalForBar, Vector4 col)
    {
        double pct = totalForBar > 0.0001 ? value / totalForBar * 100.0 : 0.0;
        ImGui.Text(label);
        ImGui.SameLine(230);
        ImGui.Text($"{value,7:F2} ms");
        ImGui.SameLine(330);
        ImGui.Text($"{pct,5:F1}%");
        ImGui.SameLine(385);
        float barW = MathF.Min(160f, MathF.Max(0f, ImGui.GetContentRegionAvail().X));
        var p0 = ImGui.GetCursorScreenPos();
        float frac = frame > 0.0001 ? (float)(value / frame) : 0f;
        frac = Math.Clamp(frac, 0f, 1f);
        ImGui.GetWindowDrawList().AddRectFilled(p0, p0 + new Vector2(barW, 10f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.25f, 1f)), 2f);
        if (frac > 0.001f)
            ImGui.GetWindowDrawList().AddRectFilled(p0, p0 + new Vector2(barW * frac, 10f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(col.X, col.Y, col.Z, 0.9f)), 2f);
        ImGui.Dummy(new Vector2(barW, 10f));
    }
}
