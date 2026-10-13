using System.Diagnostics;

namespace DarkEngine3D_gl_csharp.Engine.Helpers;

/// <summary>
/// Zero-allocation frame-section profiler for the EDITOR frame. Timing sites
/// wrap their code in <c>using (FrameProfiler.Scope(ref FrameProfiler.XxxMs))</c>
/// scopes (Stopwatch timestamp ≈ 20 ns — negligible even when the panel is
/// closed); <see cref="NextFrame"/> (called at the top of IDE.Render) pushes the
/// collected values into ring-buffer history for the Frame Profiler UI
/// (Render Time panel) and clears the slots for the frame being measured.
/// </summary>
public static class FrameProfiler
{
    public const int History = 240;

    // ── Section slots (ms of the frame currently being measured) ──
    /// <summary>Whole IDE.Render call (menu + panels + popups; in-game path included).</summary>
    public static double FrameMs;
    /// <summary>The engine's own 3D stage timing (copied from IDEBridge.RenderTotalMs —
    /// measured inside GameScene, not by a scope here).</summary>
    public static double SceneMs;
    /// <summary>Viewport panel Render() — scene texture display, gizmos, brushes, overlays.</summary>
    public static double ViewportMs;
    /// <summary>All OTHER editor panels combined (Scope ACCUMULATES — safe to nest
    // multiple panels inside one using block; each panel contributes additively).</summary>
    public static double PanelsMs;
    /// <summary>ImGui NewFrame path: pending-font processing, input polling (mouse+keyboard
    /// every frame) and io setup + ImGui.NewFrame() itself.</summary>
    public static double ImguiNewFrameMs;
    /// <summary>ImGui.Render() + RenderDrawData: building ImGui's draw lists is included
    /// in the section timers; this slot measures only the GPU-backed vertex/index buffer
    /// upload + draw call submission + font-atlas texture bind.</summary>
    public static double ImguiRenderMs;
    /// <summary>Main menu bar (BeginMainMenuBar → EndMainMenuBar) — menu items, font menus,
    /// panel toggles and the Help menu are redrawn EVERY frame.</summary>
    public static double MenuBarMs;
    /// <summary>Project popups (New/Open/Save-As Project file pickers) + the shared
    /// GLB reference dialog rendered every frame in the editor path.</summary>
    public static double PopupsMs;
    /// <summary>Sprite-sheet/clip registry sync (Sprite Editor → IDEBridge): the
    /// per-frame refresh that keeps Player2D/Sprite2D resolving their art. Throttled
    /// + signature-gated, so in steady state this should be ~0 most frames.</summary>
    public static double SpriteRegistryMs;
    /// <summary>Transition overlay + TrackAndRestorePanelFocus + undo/redo routing
    /// (the small bookkeeping tail of the editor frame).</summary>
    public static double EditorMiscMs;
    /// <summary>Terrain sculpt block in the viewport (pick + stamp + overlay gizmos).</summary>
    public static double SculptMs;
    /// <summary>Sculpt sub-section: CPU ray-march picking.</summary>
    public static double SculptPickMs;
    /// <summary>Sculpt sub-section: brush stamp + per-frame R8 flush + stroke-end bake.</summary>
    public static double SculptStampMs;
    /// <summary>Sculpt sub-section: brush ring + paintable-region outline drawing.</summary>
    public static double SculptOverlayMs;

    // ── Ring history (pushed by NextFrame) ──
    private static readonly double[] _histFrame = new double[History];
    private static readonly double[] _histScene = new double[History];
    private static readonly double[] _histViewport = new double[History];
    private static readonly double[] _histPanels = new double[History];
    private static readonly double[] _histSculpt = new double[History];
    private static readonly double[] _histImguiNewFrame = new double[History];
    private static readonly double[] _histImguiRender = new double[History];
    private static readonly double[] _histMenuBar = new double[History];
    private static readonly double[] _histPopups = new double[History];
    private static readonly double[] _histSpriteReg = new double[History];
    private static readonly double[] _histMisc = new double[History];
    private static int _head;
    private static int _count;

    public static double[] HistFrame => _histFrame;
    public static double[] HistScene => _histScene;
    public static double[] HistViewport => _histViewport;
    public static double[] HistPanels => _histPanels;
    public static double[] HistSculpt => _histSculpt;
    public static double[] HistImguiNewFrame => _histImguiNewFrame;
    public static double[] HistImguiRender => _histImguiRender;
    public static double[] HistMenuBar => _histMenuBar;
    public static double[] HistPopups => _histPopups;
    public static double[] HistSpriteRegistry => _histSpriteReg;
    public static double[] HistMisc => _histMisc;
    /// <summary>Valid entries in the history buffers (grows to <see cref="History"/>).</summary>
    public static int HistCount => _count;

    /// <summary>Push the current slots into history and zero them for the next frame.</summary>
    public static void NextFrame()
    {
        _histFrame[_head] = FrameMs;
        _histScene[_head] = SceneMs;
        _histViewport[_head] = ViewportMs;
        _histPanels[_head] = PanelsMs;
        _histSculpt[_head] = SculptMs;
        _histImguiNewFrame[_head] = ImguiNewFrameMs;
        _histImguiRender[_head] = ImguiRenderMs;
        _histMenuBar[_head] = MenuBarMs;
        _histPopups[_head] = PopupsMs;
        _histSpriteReg[_head] = SpriteRegistryMs;
        _histMisc[_head] = EditorMiscMs;
        _head = (_head + 1) % History;
        if (_count < History) _count++;
        FrameMs = SceneMs = ViewportMs = PanelsMs = 0;
        ImguiNewFrameMs = ImguiRenderMs = MenuBarMs = PopupsMs = EditorMiscMs = 0;
        SpriteRegistryMs = 0;
        SculptMs = SculptPickMs = SculptStampMs = SculptOverlayMs = 0;
    }

    /// <summary>The i-th OLDEST valid sample of a history buffer (i = 0 → oldest,
    /// i = HistCount-1 → newest) — safe indexing across the ring wrap.</summary>
    public static double Sample(double[] hist, int i)
    {
        int idx = (_head - _count + i + History * 2) % History;
        return hist[idx];
    }

    /// <summary>Average of a history buffer over its valid entries (oldest→newest
    /// indexing: sample i = <c>buf[(head - count + i + 2·History) % History]</c>).</summary>
    public static double Average(double[] hist)
    {
        if (_count == 0) return 0;
        double sum = 0;
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head - _count + i + History * 2) % History;
            sum += hist[idx];
        }
        return sum / _count;
    }

    /// <summary>Start a timing scope — dispose it to add the elapsed ms to the slot:
    /// <c>using (FrameProfiler.Scope(ref FrameProfiler.ViewportMs)) { … }</c></summary>
    public static ProfScope Scope(ref double slot) => new(ref slot);

    /// <summary>Accumulate into a slot from a manually captured timestamp — for
    /// regions where a using-block would require re-indenting hundreds of lines
    /// (e.g. the whole main-menu-bar body).
    /// <c>long t0 = Stopwatch.GetTimestamp(); … FrameProfiler.AddMs(ref slot, t0);</c></summary>
    public static void AddMs(ref double slot, long startTimestamp)
        => slot += (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

    /// <summary>Max of the last <paramref name="n"/> pushed samples of a history buffer,
    /// youngest→oldest. For SPARSE-SPIKE sections (work that runs every-N-frames, like the
    /// throttled sprite registry sync): a per-frame slot reads 0.00 on the N-1 empty frames
    /// and a one-off number on the work frame — sampling it instantly shows 0 almost
    /// always. The peak over the throttle window is the section's true per-burst cost and
    /// stays stable between bursts. The CURRENT (not-yet-pushed) slot value is passed via
    /// <paramref name="current"/> so the frame still being collected is included.
    /// <c>FrameProfiler.MaxRecent(FrameProfiler.HistX, N, FrameProfiler.X)</c></summary>
    public static double MaxRecent(double[] hist, int n, double current)
    {
        double max = current;
        int valid = n < _count ? n : _count;
        for (int i = 0; i < valid; i++)
        {
            int idx = (_head - 1 - i + History * 2) % History;
            double v = hist[idx];
            if (v > max) max = v;
        }
        return max;
    }
}

/// <summary>Disposable timing scope for <see cref="FrameProfiler.Scope"/> — a ref
/// struct (stack-only, zero allocation) holding a REF to the target slot, so
/// disposal accumulates into the exact static field that was passed in.</summary>
public ref struct ProfScope
{
    private readonly ref double _slot;
    private readonly long _start;

    public ProfScope(ref double slot)
    {
        _slot = ref slot;
        _start = Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        _slot += (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
    }
}
