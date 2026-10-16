using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkEngine3D_gl_csharp.Engine.Visual;
using StbImageSharp;

// Headless harness for the REAL SpriteGridDetector (project reference — the engine
// assembly, not a copy):  1) a synthetic suite whose ground truth is known, because a
// sheet drawn with cell size C and gutter G must be read back exactly;
// 2) a sweep over real PNG sheets that reports the aggregate quality of the detection
// (standard cell sizes, square cells, exact tiling, pitch periodicity).
//
//   dotnet run --project Tools/SpriteGridTest                (synthetic suite)
//   dotnet run --project Tools/SpriteGridTest -- --sweep <folder> [-v]
static class Program
{
    private static int _fails;

    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--sweep")
        {
            Sweep(args[1], args.Contains("-v"));
            return;
        }
        if (args.Length > 0 && args[0] == "--dump")
        {
            Dump(args[1]);
            return;
        }
        Synthetic();
    }

    // ─────────────────────────── synthetic suite ───────────────────────────

    private static void Synthetic()
    {
        Console.WriteLine("== synthetic grids (cell @ ink width = cell-2) ==\n");

        // Power-of-two cells with a small gutter: the classic sheets the detector must nail.
        Check("8x8  cols4 rows2 pad4", BuildGrid(4, 2, 8, 8, 4, 4), new SpriteGridFit(4, 2, 8, 8, 0, 0, 4, 4));
        Check("16x16 cols6 rows3 pad2", BuildGrid(6, 3, 16, 16, 2, 2), new SpriteGridFit(6, 3, 16, 16, 0, 0, 2, 2));
        Check("32x32 cols8 rows4 pad4", BuildGrid(8, 4, 32, 32, 4, 4), new SpriteGridFit(8, 4, 32, 32, 0, 0, 4, 4));
        Check("64x64 cols5 rows2 pad0 (touching)", BuildGrid(5, 2, 64, 64, 0, 0), new SpriteGridFit(5, 2, 64, 64, 0, 0, 0, 0));
        Check("16x16 cols8 rows2 pad0 (touching)", BuildGrid(8, 2, 16, 16, 0, 0), new SpriteGridFit(8, 2, 16, 16, 0, 0, 0, 0));
        Check("32x32 cols12 rows8 pad0 (touching)", BuildGrid(12, 8, 32, 32, 0, 0), new SpriteGridFit(12, 8, 32, 32, 0, 0, 0, 0));
        // Touching + symmetric art: a half-pitch harmonic must NOT halve the cell.
        Check("32x32 cols4 rows1 pad0 symmetric art", BuildGrid(4, 1, 32, 32, 0, 0, symmetric: true),
              new SpriteGridFit(4, 1, 32, 32, 0, 0, 0, 0));
        // Offset/pad sheet (art placed inside a padded canvas).
        Check("16x16 cols3 rows2 pad2 off(3,5)", BuildGrid(3, 2, 16, 16, 2, 2, offX: 3, offY: 5),
              new SpriteGridFit(3, 2, 16, 16, 3, 5, 2, 2));
        // Anti-aliased cell edges: 30px ink in a 32px pitch. A single row has no gutter
        // partner to measure vertically, so PadY stays 0 (the cell already is the pitch).
        Check("32 pitch cols4, 30px ink", BuildSprites(4, 1, 32, 32, 30, 30, 2, 2),
              new SpriteGridFit(4, 1, 32, 32, 0, 0, 2, 0));
        // One sprite floating on a big canvas: ONE frame = the image (never a phantom 2x2
        // grid from divisibility, and never a crop that resizes the sprite).
        Check("single 30x40 sprite on 64x64", BuildSingle(64, 64, 1, 1, 30, 40), new SpriteGridFit(1, 1, 64, 64, 0, 0, 0, 0));
        // Full-bleed opaque background: one native frame.
        Check("opaque 128x64 full-bleed", BuildSingle(128, 64, 0, 0, 128, 64), new SpriteGridFit(1, 1, 128, 64, 0, 0, 0, 0));

        Console.WriteLine();
        var blank = new byte[16 * 16 * 4];
        var r = SpriteGridDetector.Detect(blank, 16, 16);
        Console.WriteLine($"{(r == null ? "OK  " : "FAIL")} blank image -> null" + (r == null ? "" : $" (got {r})"));
        if (r != null) _fails++;

        Console.WriteLine(_fails == 0 ? "\nALL SYNTHETIC CASES PASS" : $"\n{_fails} SYNTHETIC FAILURES");
        Environment.Exit(_fails == 0 ? 0 : 1);
    }

    private static void Check(string name, (byte[] px, int w, int h) sheet, SpriteGridFit want)
        => Check(name, sheet.px, sheet.w, sheet.h, want);

    private static void Check(string name, byte[] px, int w, int h, SpriteGridFit want)
    {
        var got = SpriteGridDetector.Detect(px, w, h);
        bool ok = got.HasValue && Same(got.Value, want);
        Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name} ({w}x{h}): "
                          + (got.HasValue ? got.Value.ToString() : "null")
                          + (ok ? "" : $"   WANT {want}"));
        if (!ok) _fails++;
    }

    private static bool Same(in SpriteGridFit a, in SpriteGridFit b) =>
        a.Cols == b.Cols && a.Rows == b.Rows && a.FrameW == b.FrameW && a.FrameH == b.FrameH &&
        a.OffX == b.OffX && a.OffY == b.OffY && a.PadX == b.PadX && a.PadY == b.PadY;

    // ─────────────────────────── synthetic sheet builders ───────────────────────────

    /// <summary>Uniform sheet: every cell holds a character-like silhouette whose widest
    /// row spans cell-2 px (so the cell is within the detector's standard-size snap) and
    /// whose rows are narrowed toward the top/bottom (a real ink profile, not a block).</summary>
    private static (byte[] px, int w, int h) BuildGrid(int cols, int rows, int cw, int ch,
        int padX, int padY, int offX = 0, int offY = 0, bool symmetric = false)
    {
        int w = offX + cols * (cw + padX) - padX;
        int h = offY + rows * (ch + padY) - padY;
        var px = new byte[w * h * 4];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                Paint(px, w, offX + c * (cw + padX), offY + r * (ch + padY), cw, ch, symmetric);
        return (px, w, h);
    }

    /// <summary>Explicit ink rectangles (tests the "30px ink in a 32px pitch" case).</summary>
    private static (byte[] px, int w, int h) BuildSprites(int cols, int rows, int cw, int ch,
        int inkW, int inkH, int padX, int padY)
    {
        int w = cols * (cw + padX) - padX;
        int h = rows * (ch + padY) - padY;
        var px = new byte[w * h * 4];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                Fill(px, w, c * (cw + padX), r * (ch + padY), inkW, inkH);
        return (px, w, h);
    }

    private static (byte[] px, int w, int h) BuildSingle(int w, int h, int x, int y, int sw, int sh)
    {
        var px = new byte[w * h * 4];
        Fill(px, w, x, y, sw, sh);
        return (px, w, h);
    }

    private static void Fill(byte[] px, int w, int x0, int y0, int cw, int ch)
    {
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int i = ((y0 + y) * w + x0 + x) * 4;
                px[i] = 200; px[i + 1] = 180; px[i + 2] = 160; px[i + 3] = 255;
            }
    }

    /// <summary>Character-like silhouette: widest row spans cw-2 px, tapering to ~1/6 at
    /// the top/bottom. `symmetric` keeps it mirror-symmetric (the half-pitch trap),
    /// otherwise a hip/leg tail makes the art asymmetric like a real character sprite.</summary>
    private static void Paint(byte[] px, int w, int x0, int y0, int cw, int ch, bool symmetric)
    {
        double halfMax = (cw - 2) / 2.0;
        for (int y = 0; y < ch; y++)
        {
            double t = ch <= 1 ? 0.5 : (double)y / (ch - 1);
            double half = (0.16 + 0.84 * Math.Sin(Math.PI * t)) * halfMax;
            // Ink centered in the cell: the widest row is exactly cw-2 px wide, inset 1.
            int s = (int)Math.Round((cw - 2 * half) / 2.0);
            int e = (int)Math.Round((cw + 2 * half) / 2.0) - 1;
            if (!symmetric && t > 0.62) s = Math.Max(0, s - Math.Max(1, cw / 8)); // tail
            s = Math.Clamp(s, 0, cw - 1);
            e = Math.Clamp(e, s, cw - 1);
            for (int x = s; x <= e; x++)
            {
                int i = ((y0 + y) * w + x0 + x) * 4;
                px[i] = 200; px[i + 1] = 180; px[i + 2] = 160; px[i + 3] = 255;
            }
        }
    }

    // ─────────────────────────── run dump (structure of ONE sheet) ───────────────────────────

    /// <summary>Print the raw ink runs of one sheet: the ground truth of its layout is
    /// readable straight from the run starts/widths.</summary>
    private static void Dump(string path)
    {
        var (px, w, h) = Load(path);
        Console.WriteLine($"{path}\n  {w}x{h}");
        var colRuns = Runs(px, w, h, vertical: false);
        var rowRuns = Runs(px, w, h, vertical: true);
        Console.WriteLine($"  col runs ({colRuns.Count}): " + Fmt(colRuns));
        Console.WriteLine($"  row runs ({rowRuns.Count}): " + Fmt(rowRuns));
        Console.WriteLine($"  col strides: " + FmtDeltas(colRuns));
        Console.WriteLine($"  row strides: " + FmtDeltas(rowRuns));
        var fit = SpriteGridDetector.Detect(px, w, h);
        Console.WriteLine($"  detect -> {(fit.HasValue ? fit.Value.ToString() : "null")}");
    }

    private static string Fmt(List<(int Start, int End)> runs) =>
        string.Join(", ", runs.Take(40).Select(r => $"{r.Start}-{r.End}({r.End - r.Start + 1})"))
        + (runs.Count > 40 ? $" … +{runs.Count - 40}" : "");

    private static string FmtDeltas(List<(int Start, int End)> runs)
    {
        var d = new List<int>();
        for (int i = 1; i < runs.Count; i++) d.Add(runs[i].Start - runs[i - 1].Start);
        return string.Join(", ", d.Take(40)) + (d.Count > 40 ? $" … +{d.Count - 40}" : "");
    }

    private static List<(int Start, int End)> Runs(byte[] px, int w, int h, bool vertical)
    {
        int n = vertical ? h : w;
        var ink = new bool[n];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[(y * w + x) * 4 + 3] >= 8) { if (vertical) ink[y] = true; else ink[x] = true; }
        var runs = new List<(int Start, int End)>();
        int s = -1;
        for (int i = 0; i < n; i++)
        {
            if (ink[i]) { if (s < 0) s = i; }
            else if (s >= 0) { runs.Add((s, i - 1)); s = -1; }
        }
        if (s >= 0) runs.Add((s, n - 1));
        return runs;
    }

    // ─────────────────────────── real-sheet sweep ───────────────────────────

    private static readonly int[] Preferred = [8, 16, 32, 64, 128, 256, 512];

    private static void Sweep(string dir, bool verbose)
    {
        var files = Directory.EnumerateFiles(Path.GetFullPath(dir), "*.png", SearchOption.AllDirectories)
                             .OrderBy(f => f).ToList();
        Console.WriteLine($"== sweep: {files.Count} PNG files under {Path.GetFullPath(dir)} ==\n");

        int ok = 0, blank = 0, standard = 0, square = 0, exact = 0;
        double corrSum = 0; int corrN = 0;
        var rows = new List<(string file, int w, int h, SpriteGridFit fit)>();

        foreach (var f in files)
        {
            byte[] px; int w, h;
            try { (px, w, h) = Load(f); } catch (Exception ex) { Console.WriteLine($"SKIP {f}: {ex.Message}"); continue; }

            var fitOpt = SpriteGridDetector.Detect(px, w, h);
            if (fitOpt == null) { blank++; if (verbose) Console.WriteLine($"{Rel(f, dir)} {w}x{h} -> blank"); continue; }
            var fit = fitOpt.Value;
            ok++;
            bool isStd = Preferred.Contains(fit.FrameW) && Preferred.Contains(fit.FrameH);
            bool isSquare = fit.PitchX == fit.PitchY;
            bool isExact = fit.OffX == 0 && fit.OffY == 0
                           && w % Math.Max(1, fit.PitchX) == 0 && h % Math.Max(1, fit.PitchY) == 0;
            if (isStd) standard++;
            if (isSquare) square++;
            if (isExact) exact++;

            double c = Periodicity(px, w, h, fit.PitchX, fit.PitchY);
            if (c >= 0) { corrSum += c; corrN++; }

            rows.Add((f, w, h, fit));
            if (verbose || !isStd || !isExact)
                Console.WriteLine($"{Rel(f, dir)} {w}x{h} -> {fit}  std={(isStd ? "y" : "n")} sq={(isSquare ? "y" : "n")} exact={(isExact ? "y" : "n")} corr={c:0.00}");
        }

        Console.WriteLine($"\n-- totals ----------------------------------------------------");
        Console.WriteLine($"detected          : {ok}/{files.Count}   (blank: {blank})");
        Console.WriteLine($"standard cell xy  : {standard}/{ok}  ({Pct(standard, ok)})");
        Console.WriteLine($"square cells      : {square}/{ok}  ({Pct(square, ok)})");
        Console.WriteLine($"exact tiling      : {exact}/{ok}  ({Pct(exact, ok)})");
        Console.WriteLine($"mean pitch corr   : {(corrN > 0 ? corrSum / corrN : 0):0.000}  (n={corrN})");
        Console.WriteLine($"tile sizes used   : {string.Join(", ", rows.GroupBy(r => r.fit.FrameW + "x" + r.fit.FrameH).OrderByDescending(g => g.Count()).Take(12).Select(g => $"{g.Key}:{g.Count()}"))}");
    }

    private static string Pct(int n, int d) => d == 0 ? "n/a" : $"{100.0 * n / d:0.0}%";

    private static string Rel(string f, string dir) => Path.GetRelativePath(Path.GetFullPath(dir), f);

    /// <summary>Mean cosine similarity of the ink profile against itself one cell pitch
    /// away — an independent check that the detected pitch really is the sheet's period.
    /// Returns -1 when undefined (single cell / off image).</summary>
    private static double Periodicity(byte[] px, int w, int h, int pitchX, int pitchY)
    {
        double a = ProfileCos(px, w, h, pitchX, vertical: false);
        double b = ProfileCos(px, w, h, pitchY, vertical: true);
        if (a < 0) return b;
        if (b < 0) return a;
        return (a + b) * 0.5;
    }

    private static double ProfileCos(byte[] px, int w, int h, int pitch, bool vertical)
    {
        int n = vertical ? h : w;
        if (pitch <= 0 || pitch >= n) return -1;
        var prof = new int[n];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[(y * w + x) * 4 + 3] >= 8) { if (vertical) prof[y]++; else prof[x]++; }
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i + pitch < n; i++)
        {
            dot += (double)prof[i] * prof[i + pitch];
            na += (double)prof[i] * prof[i];
            nb += (double)prof[i + pitch] * prof[i + pitch];
        }
        if (na <= 0 || nb <= 0) return -1;
        return dot / Math.Sqrt(na * nb);
    }

    private static (byte[] px, int w, int h) Load(string path)
    {
        using var s = File.OpenRead(path);
        var img = ImageResult.FromStream(s, ColorComponents.RedGreenBlueAlpha);
        return (img.Data, img.Width, img.Height);
    }
}
