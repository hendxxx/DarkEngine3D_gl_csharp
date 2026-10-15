using System;
using System.Collections.Generic;

// USER-PROVIDED detector (copied verbatim logic; System.Drawing replaced with a raw
// pixel loader so the test works headless): ink profiles per axis + segment count,
// frame = width/cols, height/rows.
static class UserDetector
{
    public struct GridInfo
    {
        public int Rows;
        public int Columns;
        public int ApproximateFrameWidth;
        public int ApproximateFrameHeight;
        public override string ToString() => $"{Columns}x{Rows} frames @ ~{ApproximateFrameWidth}x{ApproximateFrameHeight}px";
    }

    public static GridInfo DetectGrid(byte[] px, int width, int height)
    {
        var horizontalProfile = new int[height];
        var verticalProfile = new int[width];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (px[(y * width + x) * 4 + 3] > 5)
                {
                    horizontalProfile[y]++;
                    verticalProfile[x]++;
                }

        int rows = CountSegments(horizontalProfile);
        int cols = CountSegments(verticalProfile);

        return new GridInfo
        {
            Rows = rows,
            Columns = cols,
            ApproximateFrameWidth = cols > 0 ? width / cols : width,
            ApproximateFrameHeight = rows > 0 ? height / rows : height
        };
    }

    static int CountSegments(int[] profile)
    {
        int segments = 0;
        bool inside = false;
        for (int i = 0; i < profile.Length; i++)
        {
            bool hasContent = profile[i] > 0;
            if (hasContent && !inside) { inside = true; segments++; }
            else if (!hasContent && inside) { inside = false; }
        }
        return segments;
    }
}

// PANEL autofit (copy of AutoFitFromImage helper chain incl. standard-size snap).
static class PanelDetector
{
    static readonly int[] StandardFrameSizes = [512, 256, 128, 64, 32, 16, 96, 48, 24, 8];

    public static (int Cols, int Rows, int FrameW, int FrameH, int OffX, int OffY, int PadX, int PadY)? AutoFitFromImage(byte[] pxIn, int width, int height, bool debug = false)
    {
        if (pxIn == null || width <= 0 || height <= 0) return null;
        var colInk = new bool[width];
        var rowInk = new bool[height];
        int stride = width * 4;
        for (int y = 0; y < height; y++)
        {
            int rowBase = y * stride;
            for (int x = 0; x < width; x++)
                if (pxIn[rowBase + x * 4 + 3] >= 8) { colInk[x] = true; rowInk[y] = true; }
        }
        var colSegs = InkRuns(colInk);
        var rowSegs = InkRuns(rowInk);
        if (debug)
        {
            Console.WriteLine("   col runs (start,end): " + string.Join(", ", colSegs));
            Console.WriteLine("   row runs (start,end): " + string.Join(", ", rowSegs));
        }
        if (colSegs.Count == 0 || rowSegs.Count == 0) return null;

        if (colSegs.Count == 1 && rowSegs.Count == 1)
        {
            bool hasTransparency = false;
            for (int i = 3; i < pxIn.Length && !hasTransparency; i += 4)
                hasTransparency = pxIn[i] < 8;
            if (!hasTransparency)
                return (1, 1, width, height, 0, 0, 0, 0);
            foreach (int size in StandardFrameSizes)
                if (width % size == 0 && height % size == 0 && size < width && size < height)
                    return (width / size, height / size, size, size, 0, 0, 0, 0);
            return (1, 1, width, height, 0, 0, 0, 0);
        }

        var cx = colSegs.Count >= 2 ? FitAxis(colSegs) : SnapSingleRun(colSegs[0], width);
        var rx = rowSegs.Count >= 2 ? FitAxis(rowSegs) : SnapSingleRun(rowSegs[0], height);
        int syncX = cx.Count == 1 ? 0 : cx.Frame + cx.Pad;
        int syncY = rx.Count == 1 ? 0 : rx.Frame + rx.Pad;
        if (cx.Count == 1) cx = SplitMergedAxis(cx, width, syncY);
        if (rx.Count == 1) rx = SplitMergedAxis(rx, height, syncX);
        return (cx.Count, rx.Count, cx.Frame, rx.Frame, cx.Start, rx.Start, cx.Pad, rx.Pad);
    }

    static (int Start, int Frame, int Pad, int Count) SplitMergedAxis(
        (int Start, int Frame, int Pad, int Count) axis, int extent, int otherPitch)
    {
        if (axis.Frame != extent) return axis;
        if (otherPitch <= 1) return axis;
        int pitch = otherPitch;
        if (Array.IndexOf(StandardFrameSizes, pitch) < 0)
        {
            int nearest = -1, bestGap = 3;
            foreach (int s in StandardFrameSizes)
            {
                int gap = Math.Abs(pitch - s);
                if (gap < bestGap) { nearest = s; bestGap = gap; }
            }
            if (nearest <= 0) return axis;
            pitch = nearest;
        }
        if (extent % pitch != 0) return axis;
        int count = extent / pitch;
        if (count < 2) return axis;
        return (0, pitch, 0, count);
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
        if (Array.IndexOf(StandardFrameSizes, stride) >= 0)
        {
            frame = stride;
            pad = 0;
        }
        return (segs[0].Start, frame, pad, n);
    }

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
}

static class Loader
{
    // Minimal PNG decoder via stb-free path: use the engine's own image loading to
    // avoid new deps — but the test must stay standalone, so parse simple ALPHA-
    // only approach with SixLabors? No — keep deps zero: decode with the built-in
    // .NET PNG support via System.Drawing.Common on Windows.
    public static (byte[] px, int w, int h) Load(string path, System.Drawing.Imaging.ImageFormat raw)
    {
        using var bmp = new System.Drawing.Bitmap(path);
        int w = bmp.Width, h = bmp.Height;
        var px = new byte[w * h * 4];
        var rect = new System.Drawing.Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        unsafe
        {
            byte* ptr = (byte*)data.Scan0;
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)(ptr + y * data.Stride), px, y * w * 4, w * 4);
        }
        bmp.UnlockBits(data);
        return (px, w, h);
    }
}

static class Program
{
    static void Main(string[] args)
    {
        string dir = @"C:\Users\hend_\source\repos\DarkEngine3D_gl_csharp\Dark Projects\Sample game 2D Forest Adventures\Artifacts\images\FX";
        string[] files =
        {
            "GandalfHardcore Water Tiles sheet.png",
            "Campfire sheet.png",
            "GandalfHardcore Animated Water Tiles.png",
        };

        foreach (string f in files)
        {
            string path = System.IO.Path.Combine(dir, f);
            if (!System.IO.File.Exists(path)) { Console.WriteLine($"MISSING {f}"); continue; }
            var (px, w, h) = Loader.Load(path, null);
            Console.WriteLine($"== {f} ({w}x{h}px) ==");

            var user = UserDetector.DetectGrid(px, w, h);
            Console.WriteLine($"   user : {user}");

            var panel = PanelDetector.AutoFitFromImage(px, w, h, debug: true);
            if (panel == null) Console.WriteLine("   panel: null");
            else
            {
                var p = panel.Value;
                Console.WriteLine($"   panel: {p.Cols}x{p.Rows} @ {p.FrameW}x{p.FrameH}px off({p.OffX},{p.OffY}) pad({p.PadX},{p.PadY})");
            }
            Console.WriteLine();
        }
    }
}
