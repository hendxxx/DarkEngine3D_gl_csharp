using System;
using System.Collections.Generic;

// Pure copies of AutoFitFromImage helpers (SpriteEditorPanel.cs) for smoke testing.
static class AutoFitTest
{
    struct ImageResult { public int Width, Height; public byte[] Data; }

    static (int Cols, int Rows, int FrameW, int FrameH, int OffX, int OffY, int PadX, int PadY)? AutoFitFromImage(ImageResult img)
    {
        if (img.Data == null || img.Width <= 0 || img.Height <= 0) return null;
        var colInk = new bool[img.Width];
        var rowInk = new bool[img.Height];
        var px = img.Data;
        int stride = img.Width * 4;
        for (int y = 0; y < img.Height; y++)
        {
            int rowBase = y * stride;
            for (int x = 0; x < img.Width; x++)
                if (px[rowBase + x * 4 + 3] >= 8) { colInk[x] = true; rowInk[y] = true; }
        }
        var colSegs = InkRuns(colInk);
        var rowSegs = InkRuns(rowInk);
        if (colSegs.Count == 0 || rowSegs.Count == 0) return null;

        if (colSegs.Count == 1 && rowSegs.Count == 1)
        {
            bool hasTransparency = false;
            for (int i = 3; i < px.Length && !hasTransparency; i += 4)
                hasTransparency = px[i] < 8;
            if (!hasTransparency)
                return (1, 1, img.Width, img.Height, 0, 0, 0, 0);
            foreach (int size in new[] { 256, 128, 96, 64, 48, 32, 24, 16 })
                if (img.Width % size == 0 && img.Height % size == 0 && size < img.Width && size < img.Height)
                    return (img.Width / size, img.Height / size, size, size, 0, 0, 0, 0);
            return (1, 1, img.Width, img.Height, 0, 0, 0, 0);
        }

        var cx = colSegs.Count >= 2 ? FitAxis(colSegs) : SnapSingleRun(colSegs[0], img.Width);
        var rx = rowSegs.Count >= 2 ? FitAxis(rowSegs) : SnapSingleRun(rowSegs[0], img.Height);
        return (cx.Count, rx.Count, cx.Frame, rx.Frame, cx.Start, rx.Start, cx.Pad, rx.Pad);
    }

    static (int Start, int Frame, int Pad, int Count) FitAxis(List<(int Start, int End)> segs)
    {
        int n = segs.Count;
        int widest = 0;
        foreach (var s in segs) widest = Math.Max(widest, s.End - s.Start + 1);
        var strides = new List<int>(n - 1);
        for (int i = 1; i < n; i++) strides.Add(segs[i].Start - segs[i - 1].Start);
        strides.Sort();
        int stride = strides[strides.Count / 2];
        int frame = widest, pad = stride - widest;
        if (pad < 0) { frame = stride; pad = 0; }
        SnapToStandard(ref frame, ref pad, stride, maxGrow: 2);
        return (segs[0].Start, frame, pad, n);
    }

    static readonly int[] StandardFrameSizes = [256, 128, 64, 32, 16, 96, 48, 24];

    static void SnapToStandard(ref int frame, ref int pad, int stride, int maxGrow)
    {
        int best = -1, bestGap = int.MaxValue;
        foreach (int s in StandardFrameSizes)
        {
            int gap = Math.Abs(frame - s);
            if (gap > 2 || gap >= bestGap) continue;
            int newPad = stride - s;
            if (newPad < 0) continue;
            if (s - frame > maxGrow) continue;
            best = s;
            bestGap = gap;
        }
        if (best > 0) { frame = best; pad = stride - best; }
    }

    static (int Start, int Frame, int Pad, int Count) SnapSingleRun((int Start, int End) seg, int axisExtent)
    {
        int frame = seg.End - seg.Start + 1;
        foreach (int s in StandardFrameSizes)
        {
            if (s >= frame && s - frame <= 2 && s <= axisExtent) { frame = s; break; }
        }
        return (seg.Start, frame, 0, 1);
    }

    static List<(int Start, int End)> InkRuns(bool[] flags)
    {
        var runs = new List<(int Start, int End)>();
        int runStart = -1, lastInk = -1;
        for (int x = 0; x < flags.Length; x++)
        {
            if (flags[x])
            {
                if (runStart < 0) runStart = x;
                lastInk = x;
                continue;
            }
            if (runStart < 0) continue;
            int gap = 0;
            while (x + gap < flags.Length && !flags[x + gap]) gap++;
            if (x + gap >= flags.Length) { runs.Add((runStart, lastInk)); runStart = -1; break; }
            if (gap <= 2) { x += gap - 1; continue; }
            runs.Add((runStart, lastInk));
            runStart = -1;
        }
        if (runStart >= 0) runs.Add((runStart, lastInk));
        return runs;
    }

    static ImageResult Make(int w, int h, params (int x0, int x1, int y0, int y1)[] sprites)
    {
        var d = new byte[w * h * 4];
        foreach (var s in sprites)
        {
            for (int y = s.y0; y <= s.y1; y++)
                for (int x = s.x0; x <= s.x1; x++)
                {
                    int i = (y * w + x) * 4;
                    d[i + 3] = 255;
                }
        }
        return new ImageResult { Width = w, Height = h, Data = d };
    }

    static int fails = 0;
    static void Check(string name, ImageResult img, (int Cols, int Rows, int FrameW, int FrameH, int OffX, int OffY, int PadX, int PadY) want)
    {
        var gotOpt = AutoFitFromImage(img);
        if (gotOpt == null) { Console.WriteLine($"FAIL {name}: null"); fails++; return; }
        var got = gotOpt.Value;
        bool ok = got.Cols == want.Cols && got.Rows == want.Rows && got.FrameW == want.FrameW &&
                  got.FrameH == want.FrameH && got.OffX == want.OffX && got.OffY == want.OffY &&
                  got.PadX == want.PadX && got.PadY == want.PadY;
        Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}: got {got.Cols}x{got.Rows} @ {got.FrameW}x{got.FrameH} off {got.OffX},{got.OffY} pad {got.PadX},{got.PadY}" + (ok ? "" : $"  (want {want.Cols}x{want.Rows} @ {want.FrameW}x{want.FrameH} off {want.OffX},{want.OffY} pad {want.PadX},{want.PadY})"));
        if (!ok) fails++;
    }

    static void Main()
    {
        int fails0;
        // Touching sprites that cover the WHOLE canvas with no transparent gap:
        // pixel-wise identical to a full-bleed image → correctly 1x1 native
        // (no grid info exists in the pixels; user sets the grid manually).
        var img = Make(96, 64,
            (0, 31, 0, 31), (32, 63, 0, 31), (64, 95, 0, 31),
            (0, 31, 32, 63), (32, 63, 32, 63), (64, 95, 32, 63));
        Check("touching full-cover blob -> 1x1 native", img, (1, 1, 96, 64, 0, 0, 0, 0));
        fails0 = fails;

        // Touching 3x2 grid WITH transparency inside the canvas: divisible fallback
        // must recover 3x2 @32.
        var imgT = Make(96, 64,
            (0, 31, 0, 31), (32, 63, 0, 31), (64, 95, 0, 31),
            (0, 31, 32, 63), (32, 63, 32, 63), (64, 95, 32, 63));
        imgT.Data[(63 * 96 + 42) * 4 + 3] = 0; // one transparent pixel INSIDE the blob
        Check("touching 3x2 grid w/ transparency (fallback)", imgT, (3, 2, 32, 32, 0, 0, 0, 0));

        // 3x2 with 4px gutters: sprites 0-27, 32-59, 64-91 (28px sprite), gutter 4px → but gap must exceed 2px bridge!
        var img2 = Make(96, 64,
            (0, 27, 0, 27), (32, 59, 0, 27), (64, 91, 0, 27),
            (0, 27, 32, 59), (32, 59, 32, 59), (64, 91, 32, 59));
        // col runs: 0-27 (28px), gap 28..31 =4 -> real gutter -> next 32-59, gap 4, next 64-91 => three runs stride 32, widest 28, pad 4
        Check("gutted 3x2 @28 pad4", img2, (3, 2, 28, 28, 0, 0, 4, 4));

        // offset 3px, 2x1 small sprite, gutter 8
        var img3 = Make(32, 16, (3, 12, 2, 13), (21, 30, 2, 13));
        // runs: 3-12 (10px), gap 13-20 =8 → real → 21-30. stride 18, widest 10, pad 8.
        // Single row: PadY stays 0 (nothing to measure vertically).
        Check("2x1 off(3,2) pad8", img3, (2, 1, 10, 12, 3, 2, 8, 0));

        // blank
        var blank = new ImageResult { Width = 16, Height = 16, Data = new byte[16 * 16 * 4] };
        var r = AutoFitFromImage(blank);
        Console.WriteLine($"{(r == null ? "OK  " : "FAIL")} blank image -> null" + (r == null ? "" : $" got {r}"));
        if (r != null) fails++;

        // full-bleed single sprite
        var full = Make(64, 48, (0, 63, 0, 47));
        var rf = AutoFitFromImage(full);
        if (rf != null && rf.Value.Cols == 1 && rf.Value.Rows == 1 && rf.Value.FrameW == 64 && rf.Value.FrameH == 48) Console.WriteLine("OK   full-bleed -> 1x1 native");
        else { Console.WriteLine($"FAIL full-bleed: {(rf.HasValue ? rf.Value.ToString() : "null")}"); fails++; }

        // Near-standard snap: two 30px sprites with a real 4px gutter (gap > 2px
        // bridge) in a true 32px cell → frame snaps to 32, pad 0.
        var snap = Make(64, 32, (0, 25, 0, 29), (30, 59, 0, 29)); // ink 26?? no: (0..25)=26px, gap 26..29=4 → runs 0-25 & 30-59(30px)
        var rs = AutoFitFromImage(snap);
        // runs: 0-25 (26px), 30-59 (30px); stride=30, widest=30 → pad0; snap |30-32|=2 grow<=2 → 32? newPad=30-32=-2 <0 → reject → stays 30... stride must be >=32 for cell32.
        // Use stride 32 with ink 30: sprites (0,29) and (34,63): runs 0-29(30px), 34-63(30px), gap 4
        var snapOk = Make(64, 32, (0, 29, 0, 29), (34, 63, 0, 29));
        var rsOk = AutoFitFromImage(snapOk);
        if (rsOk != null && rsOk.Value.Cols == 2 && rsOk.Value.FrameW == 32 && rsOk.Value.PadX == 2)
            Console.WriteLine("OK   30px ink stride34 -> snapped to standard 32 pad2");
        else { Console.WriteLine($"FAIL snappedOk: {(rsOk.HasValue ? rsOk.Value.ToString() : "null")} (want 2x1 @32 pad2)"); fails++; }
        // (kept for record: unsnappable stride30 stays @30)
        _ = rs;

        // Snap with padding: 30px ink, stride 36 → frame snaps to 32, pad 4
        var snap2 = Make(144, 32, (0, 29, 0, 29), (36, 65, 0, 29), (72, 101, 0, 29), (108, 137, 0, 29));
        var rs2 = AutoFitFromImage(snap2);
        if (rs2 != null && rs2.Value.Cols == 4 && rs2.Value.FrameW == 32 && rs2.Value.PadX == 4)
            Console.WriteLine("OK   snapped stride36 -> 32 + pad4");
        else { Console.WriteLine($"FAIL snapped2: {(rs2.HasValue ? rs2.Value.ToString() : "null")} (want 4x1 @32 pad4)"); fails++; }

        Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILURES");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
