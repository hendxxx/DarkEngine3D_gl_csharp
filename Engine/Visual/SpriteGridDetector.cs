using System;
using System.Collections.Generic;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>
    /// Result of a sprite-sheet grid detection: how many columns/rows the sheet holds
    /// and where the first cell starts inside the image (Cols, Rows, FrameW, FrameH,
    /// OffX, OffY, PadX, PadY) — the exact fields a SpriteSheet needs.
    /// </summary>
    public readonly struct SpriteGridFit
    {
        public readonly int Cols, Rows, FrameW, FrameH, OffX, OffY, PadX, PadY;

        public SpriteGridFit(int cols, int rows, int frameW, int frameH,
                             int offX, int offY, int padX, int padY)
        {
            Cols = cols; Rows = rows; FrameW = frameW; FrameH = frameH;
            OffX = offX; OffY = offY; PadX = padX; PadY = padY;
        }

        /// <summary>Cell stride per axis (frame + gutter).</summary>
        public int PitchX => FrameW + PadX;
        public int PitchY => FrameH + PadY;

        public override string ToString() =>
            $"{Cols}x{Rows} @ {FrameW}x{FrameH}px off({OffX},{OffY}) pad({PadX},{PadY})";
    }

    /// <summary>
    /// Content-aware sprite-sheet grid detection straight from the pixels. Pure and
    /// headless (no GL, no ImGui), so the Sprite Editor and the harness in
    /// Tools/SpriteGridTest run the SAME code.
    ///
    /// Per axis the ink profile gives the runs of ink, and every plausible grid becomes a
    /// candidate: the MEASURED stride (±2 px), a STANDARD lattice on an exact divisor of
    /// the image, a PERIODIC pitch recovered where the sprites touch, and a single native
    /// cell as the last resort. Each candidate is scored on the ink itself —
    ///   cut   : cell boundaries must land on the thinnest columns (a half-pitch grid
    ///           runs its boundaries through the middle of every sprite and dies here);
    ///   fit   : every ink run must sit inside a single cell;
    ///   holes : a grid whose filled cells alternate with empty ones is over-fine —
    /// and the best one wins, with the classic power-of-two cells (8/16/32/64/128…)
    /// preferred whenever the pixel evidence leaves a choice.
    ///
    /// The CELL of the winning grid is the largest standard size that still contains the
    /// widest ink run and fits inside the pitch — so a sheet whose art varies in width
    /// (7 px wheat above 23 px) still reports the artist's frame instead of the widest
    /// thing that happened to be drawn.
    /// </summary>
    public static class SpriteGridDetector
    {
        /// <summary>Standard pixel-art frame sizes. Order is a PREFERENCE: the classic
        /// power-of-two tiles (8/16/32/64/128...) come first and win whenever the pixel
        /// evidence ties; odd legacy sizes (96/48/24) come last.</summary>
        public static readonly int[] StandardFrameSizes = [8, 16, 32, 64, 128, 256, 512, 24, 48, 96, 192, 384];

        /// <summary>Alpha value from which a pixel counts as ink (sprite art).</summary>
        private const int InkAlpha = 8;

        /// <summary>A transparent gap this wide is a hairline seam inside one sprite
        /// (anti-aliased overlap), not a gutter, and is bridged. Real gutters are 2 px and
        /// wider — the old 2 px bridge swallowed 2 px gutters, which collapsed whole
        /// sheets (6x3 @16 with a 2 px gutter) to a single 1x1 frame.</summary>
        private const int BridgePx = 1;

        /// <summary>Minimum normalized self-correlation of the ink profile one pitch away
        /// before a pitch is trusted on a gutter-less (touching) axis.</summary>
        private const double MinPeriodicity = 0.55;

        /// <summary>Minimum boundary emptiness for a periodic pitch on a gutter-less axis:
        /// a true cell boundary in touching art is twice as thin as the average column.</summary>
        private const double MinBoundaryCut = 0.5;

        /// <summary>Ink runs closer than this belong to the same sprite burst when the
        /// stride is measured (fragmented outlines, detached cuffs, dust bands).</summary>
        private const int ClusterGap = 5;

        /// <summary>Detect the grid of an RGBA image (top-first rows, 4 bytes/pixel).
        /// Null = blank/undetectable (no ink at all).</summary>
        public static SpriteGridFit? Detect(byte[] px, int width, int height)
        {
            if (px == null || width <= 0 || height <= 0) return null;
            var colInk = new bool[width];
            var rowInk = new bool[height];
            var colMass = new int[width];
            var rowMass = new int[height];
            int strideBytes = width * 4; // RGBA
            bool hasTransparency = false;

            for (int y = 0; y < height; y++)
            {
                int rowBase = y * strideBytes;
                for (int x = 0; x < width; x++)
                {
                    if (px[rowBase + x * 4 + 3] >= InkAlpha)
                    {
                        colInk[x] = true;
                        rowInk[y] = true;
                        colMass[x]++;
                        rowMass[y]++;
                    }
                    else hasTransparency = true;
                }
            }

            // A fully opaque image is a background/effect canvas: one native frame.
            if (!hasTransparency) return new SpriteGridFit(1, 1, width, height, 0, 0, 0, 0);

            var colRuns = InkRuns(colInk);
            var rowRuns = InkRuns(rowInk);
            if (colRuns.Count == 0 || rowRuns.Count == 0) return null; // blank image

            var cx = SolveAxis(colRuns, colMass, width);
            var rx = SolveAxis(rowRuns, rowMass, height);

            // NOTHING is measurable on either axis: two flat, fully merged blobs (solid
            // blocks or JPEG-alpha soup). Keep the classic divisible-size guess so such a
            // sheet still gets a plausible grid instead of one giant frame.
            if (cx.Count == 1 && rx.Count == 1 && cx.Merged && rx.Merged)
            {
                foreach (int size in StandardFrameSizes)
                    if (width % size == 0 && height % size == 0 && size < width && size < height)
                        return new SpriteGridFit(width / size, height / size, size, size, 0, 0, 0, 0);
            }

            // MERGED-AXIS INFERENCE: sprites that TOUCH along one axis leave that axis a
            // single full-extent run while the other axis resolves normally. When the
            // resolved pitch divides the merged extent exactly, the touching sprites sit on
            // the same grid — split it (160 px of touching columns + a 32 px row pitch ->
            // 5 cells @32). Exact division only, so full-bleed art is never cut into cells.
            if (cx.Count == 1) cx = SplitMergedAxis(cx, width, rx.Pitch);
            if (rx.Count == 1) rx = SplitMergedAxis(rx, height, cx.Pitch);

            return new SpriteGridFit(cx.Count, rx.Count, cx.Frame, rx.Frame,
                                     cx.Start, rx.Start, cx.Pad, rx.Pad);
        }

        // ─────────────────────────── per-axis solve ───────────────────────────

        private readonly struct Axis
        {
            public readonly int Start, Frame, Pad, Count;
            /// <summary>This axis is a single run spanning (nearly) the whole image: its
            /// ink is continuous, so its cell count can only come from elsewhere.</summary>
            public readonly bool Merged;

            public Axis(int start, int frame, int pad, int count, bool merged)
            {
                Start = start; Frame = frame; Pad = pad; Count = count; Merged = merged;
            }

            public int Pitch => Frame + Pad;
        }

        /// <summary>Score every plausible grid for one axis and return the winner.</summary>
        private static Axis SolveAxis(List<(int Start, int End)> runs, int[] prof, int extent)
        {
            var pool = new List<(Axis a, double score)>();
            int inkStart = runs[0].Start;
            int inkEnd = runs[^1].End;
            int widest = MedianWidth(runs);

            // (1) MEASURED grids: every stride the ink actually repeats with, ±2 px (a 1 px
            //     measurement jitter must not lose to a neighbour), anchored at the image
            //     origin when the pitch tiles it, otherwise at the first ink run.
            if (runs.Count >= 2)
            {
                var seen = new HashSet<(int, int)>();
                foreach (int stride in StrideCandidates(runs))
                {
                    for (int delta = 0; delta <= 2; delta++)
                    {
                        for (int k = 0; k < (delta == 0 ? 1 : 2); k++)
                        {
                            int pitch = stride + (delta == 0 ? 0 : (k == 0 ? -delta : delta));
                            if (pitch <= 1) continue;
                            foreach (int start in Anchors(inkStart, pitch, extent))
                            {
                                int count = Math.Max(1, (inkEnd - start) / pitch + 1);
                                int frame = ChooseFrame(widest, pitch);
                                // The cell may be wider than the ink (a 6 px sprite in an
                                // 8 px cell): centre the ink in its cell by pulling the
                                // origin back half the difference, so the frame lands on
                                // the artist's grid instead of 1 px off it.
                                int origin = Math.Max(0, start - Math.Max(0, frame - widest) / 2);
                                if (!seen.Add((origin, pitch))) continue;
                                Add(pool, new Axis(origin, frame, pitch - frame, count, false),
                                    prof, extent, runs, bonus: 0.05 - 0.02 * delta);
                            }
                        }
                    }
                }
            }

            // (2) STANDARD lattices on exact divisors of the image (8/16/32/64/128...).
            //     With gutters they must explain every run (each run inside one cell);
            //     without gutters the profile must actually repeat with that pitch and its
            //     boundaries must be thin, so a symmetric sprite is never halved.
            foreach (var (start, pitch) in PitchCandidates(runs, extent))
            {
                int count = (extent - start) / pitch;
                if (count < 2) continue;
                var a = new Axis(start, pitch, 0, count, false);
                // With run structure the SCORE decides: a straddling run only costs fit
                // score, so two touching sprites (one merged ink run across two cells)
                // still let the true pitch win. With continuous ink there is nothing to
                // measure, so the profile itself must repeat AND its boundaries must be
                // thin — otherwise a plain wide sprite would be cut into phantom cells.
                if (runs.Count < 2)
                {
                    if (!PeriodicEnough(prof, extent, start, pitch)) continue;
                    if (BoundaryCut(a, prof, extent) < MinBoundaryCut) continue;
                }
                Add(pool, a, prof, extent, runs, bonus: 0.0);
            }

            // (3) ONE native cell (a lone sprite, or a blob with no grid information at
            //     all). Fixed low score: any supported grid beats it.
            var single = SingleAxis(runs, extent);
            pool.Add((single, 0.30));

            double best = double.MinValue;
            foreach (var p in pool) best = Math.Max(best, p.score);
            Axis chosen = single;
            double chosenKey = double.MinValue;
            foreach (var p in pool)
            {
                if (p.score < best - 0.06) continue;              // close enough to compete
                double key = p.score + 0.06 * Priority(p.a.Pitch);  // 8/16/32/64 first
                if (key > chosenKey) { chosenKey = key; chosen = p.a; }
            }
            return chosen;
        }

        private static void Add(List<(Axis a, double score)> pool, Axis a,
                                int[] prof, int extent, List<(int Start, int End)> runs, double bonus)
        {
            double s = ScoreGrid(a, prof, extent, runs);
            if (s < 0) return;                                    // grid would cut the art off
            // A STANDARD grid that starts at the canvas edge and ends on it is the artist's
            // own layout far more often than a near-miss pitch that leaves a ragged margin
            // (a measured 97 px pitch on a 672 px sheet is really the standard 96 x 7).
            if (a.Start == 0 && a.Count * a.Pitch == extent
                && Array.IndexOf(StandardFrameSizes, a.Pitch) >= 0) s += 0.05;
            pool.Add((a, s + bonus));
        }

        /// <summary>Anchor candidates for a measured pitch: the image origin when the pitch
        /// divides the image exactly (the artist's grid usually starts at the canvas edge),
        /// then the first ink run. Origin first, so an exact tie prefers it.</summary>
        private static IEnumerable<int> Anchors(int inkStart, int pitch, int extent)
        {
            if (extent % pitch == 0) yield return 0;
            if ((inkStart != 0 || extent % pitch != 0) && extent - inkStart >= pitch)
                yield return inkStart;
        }

        /// <summary>Every stride the ink repeats with, most frequent first: the sprites'
        /// ink is inset differently in every cell, so individual strides jitter by ±2 px
        /// (a 256 px sheet whose true pitch is 32 px measures strides 28..33) and the
        /// MEDIAN can land on a jitter value. A stride that repeats is real evidence, so
        /// all of them become candidates and the pixel score picks between them.
        /// Art whose outline is broken into fragments (a cuff 5 px from the hand) would
        /// report invented strides, so the runs are also re-measured with the fragments
        /// clustered into one sprite.</summary>
        private static List<int> StrideCandidates(List<(int Start, int End)> runs)
        {
            var repeated = new List<int>();
            var raw = Repeats(runs);
            repeated.AddRange(raw);
            var clustered = Repeats(Cluster(runs));
            foreach (int c in clustered)
                if (!repeated.Contains(c)) repeated.Add(c);

            var result = new List<int>();
            foreach (int r in repeated)
                if (r > 1 && !result.Contains(r)) result.Add(r);
            if (result.Count == 0)
            {
                var all = new List<int>();
                for (int i = 1; i < runs.Count; i++) all.Add(runs[i].Start - runs[i - 1].Start);
                if (all.Count == 0) return [1];
                all.Sort();
                result.Add(Math.Max(1, all[all.Count / 2]));
            }
            return result;
        }

        /// <summary>Strides that occur more than once, most frequent first (ties: the
        /// longer stride first — a 5 px sliver gap repeats as often as the real pitch).</summary>
        private static List<int> Repeats(List<(int Start, int End)> runs)
        {
            var counts = new Dictionary<int, int>();
            for (int i = 1; i < runs.Count; i++)
            {
                int d = runs[i].Start - runs[i - 1].Start;
                if (d <= 0) continue;
                counts.TryGetValue(d, out int c);
                counts[d] = c + 1;
            }
            var list = new List<int>();
            foreach (var kv in counts) if (kv.Value > 1) list.Add(kv.Key);
            list.Sort((a, b) =>
            {
                int byCount = counts[b].CompareTo(counts[a]);
                return byCount != 0 ? byCount : b.CompareTo(a);
            });
            if (list.Count > 3) list.RemoveRange(3, list.Count - 3);
            return list;
        }

        /// <summary>Merge ink runs separated by a hairline gap (<= 5 px) into one sprite
        /// fragment chain, so the stride is measured between SPRITES, not between the
        /// pieces of one (a broken outline would otherwise invent a 6 px "pitch").</summary>
        private static List<(int Start, int End)> Cluster(List<(int Start, int End)> runs)
        {
            var merged = new List<(int Start, int End)>();
            foreach (var r in runs)
            {
                if (merged.Count > 0 && r.Start - merged[^1].End <= ClusterGap)
                    merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, r.End));
                else merged.Add(r);
            }
            return merged;
        }

        /// <summary>Typical ink run width (median). Using the median instead of the WIDEST
        /// run keeps one merged pair of sprites, or one oversized pose, from inflating the
        /// cell size for the whole sheet.</summary>
        private static int MedianWidth(List<(int Start, int End)> runs)
        {
            var w = new List<int>(runs.Count);
            foreach (var r in runs) w.Add(r.End - r.Start + 1);
            w.Sort();
            return w[w.Count / 2];
        }

        /// <summary>The frame of a cell: the LARGEST standard size that still contains the
        /// widest ink run and fits inside the pitch — a 34 px stride with 30 px ink is a
        /// 32 px cell + 2 px gutter, and a 44 px character on an 80 px stride is a 64 px
        /// cell + 16 px gutter instead of a 44 px cell + 36 px gutter. Falls back to the
        /// measured ink width when no standard size fits.</summary>
        private static int ChooseFrame(int widest, int pitch)
        {
            int best = 0;
            foreach (int s in StandardFrameSizes)
                if (s >= widest && s <= pitch && s > best) best = s;
            return best > 0 ? best : Math.Min(widest, pitch);
        }

        /// <summary>One cell over the WHOLE axis: the image is one frame. A lone sprite, a
        /// background, a border-to-border asset — cropping the frame to the art's bounding
        /// box would silently resize the sprite (and lose its canvas margins), so the safe,
        /// predictable reading is the image itself.</summary>
        private static Axis SingleAxis(List<(int Start, int End)> runs, int extent)
        {
            int inkStart = runs[0].Start;
            int inkEnd = runs[^1].End;
            bool merged = inkStart <= extent / 20 && inkEnd >= extent - extent / 20;
            return new Axis(0, extent, 0, 1, merged);
        }

        /// <summary>Standard pitches that divide the image (or the image minus the first
        /// run's offset) into 2+ cells — the touching-sheet and exact-sheet lattices.</summary>
        private static IEnumerable<(int Start, int Pitch)> PitchCandidates(
            List<(int Start, int End)> runs, int extent)
        {
            foreach (int p in StandardFrameSizes)
            {
                if (p < 4 || p * 2 > extent) continue;
                if (extent % p == 0) yield return (0, p);
                int anchor = runs[0].Start;
                if (anchor > 0 && (extent - anchor) % p == 0 && (extent - anchor) / p >= 2)
                    yield return (anchor, p);
            }
        }

        /// <summary>Grid quality from the ink profile, in [0, 1] (negative = unusable grid):
        /// cutScore — cell boundaries must fall on the thinnest columns (a half-pitch grid
        /// puts them through the middle of every sprite and dies here);
        /// fitScore — every ink run must sit inside one cell;
        /// holes — a grid whose filled cells alternate with empty ones is over-fine.</summary>
        private static double ScoreGrid(Axis a, int[] prof, int extent, List<(int Start, int End)> runs)
        {
            int pitch = a.Pitch;
            if (pitch <= 0 || a.Count < 1) return -1;
            int covered = a.Count * pitch;
            if (a.Start > runs[0].Start || a.Start + covered < runs[^1].End) return -1; // cuts art off

            double cutScore = BoundaryCut(a, prof, extent);

            bool merged = runs.Count == 1;
            double fitScore;
            double holeRatio = 0;
            if (merged) fitScore = 0.5;                  // continuous ink: no run evidence
            else
            {
                int inside = 0, minCell = int.MaxValue, maxCell = -1;
                var used = new HashSet<int>();
                foreach (var r in runs)
                {
                    int c0 = (r.Start - a.Start) / pitch;
                    int c1 = (r.End - a.Start) / pitch;
                    if (c0 == c1) inside++;
                    // EVERY cell the run touches counts as filled: a run that spans a
                    // boundary (two touching sprites bridged into one) must not leave the
                    // cell it covers looking empty, or the grid would be called over-fine.
                    int from = Math.Clamp(c0, 0, a.Count - 1);
                    int to = Math.Clamp(c1, 0, a.Count - 1);
                    for (int c = from; c <= to; c++) used.Add(c);
                    minCell = Math.Min(minCell, from);
                    maxCell = Math.Max(maxCell, to);
                }
                fitScore = (double)inside / runs.Count;
                int span = maxCell - minCell + 1;
                if (span > 0) holeRatio = 1.0 - (double)used.Count / span;
            }

            return 0.55 * cutScore + 0.25 * fitScore + 0.10 - 0.75 * holeRatio;
        }

        /// <summary>How empty this grid's cell boundaries are: 1 = every boundary sits on a
        /// transparent column, 0 = boundaries carry the average amount of ink. The ink taken
        /// at each boundary is the MINIMUM of the 3 columns around it, so a 1 px
        /// anti-aliasing spill never disqualifies a correct grid.</summary>
        private static double BoundaryCut(Axis a, int[] prof, int extent)
        {
            int pitch = a.Pitch;
            if (pitch <= 0) return 0;
            double boundaryInk = 0; int boundaries = 0;
            for (int k = 1; k < a.Count; k++)
            {
                int b = a.Start + k * pitch;
                if (b < 1 || b >= extent) continue;
                int m = prof[b];
                if (b - 1 >= 0) m = Math.Min(m, prof[b - 1]);
                if (b + 1 < extent) m = Math.Min(m, prof[b + 1]);
                boundaryInk += m; boundaries++;
            }
            if (boundaries == 0) return 1.0;
            int last = Math.Min(extent, a.Start + a.Count * pitch);
            double mass = 0;
            for (int x = a.Start; x < last; x++) mass += prof[x];
            double avg = mass / Math.Max(1, last - a.Start);
            return Math.Clamp(1.0 - (boundaryInk / boundaries) / Math.Max(1.0, avg), 0.0, 1.0);
        }

        /// <summary>Normalized self-correlation of the ink profile one pitch away: a
        /// touching sheet (no gutters) still repeats with its cell pitch.</summary>
        private static bool PeriodicEnough(int[] prof, int extent, int start, int pitch)
        {
            double dot = 0, na = 0, nb = 0;
            for (int i = start; i + pitch < extent; i++)
            {
                dot += (double)prof[i] * prof[i + pitch];
                na += (double)prof[i] * prof[i];
                nb += (double)prof[i + pitch] * prof[i + pitch];
            }
            if (na <= 0 || nb <= 0) return false;
            return dot / Math.Sqrt(na * nb) >= MinPeriodicity;
        }

        /// <summary>Preference of a cell size: 1.0 for the most-wanted standard (8x8 first,
        /// then 16/32/64/...), lower for the legacy odd sizes, 0 for anything else. Used as
        /// a small bias and as the tie-break between equally supported grids.</summary>
        private static double Priority(int pitch)
        {
            if (Array.IndexOf(StandardFrameSizes, pitch) < 0) return 0;
            if (pitch <= 512 && (pitch & (pitch - 1)) == 0)         // power of two
            {
                int bit = 0, v = pitch;
                while (v > 1) { v >>= 1; bit++; }                    // 8 -> 3, 16 -> 4, 32 -> 5
                return Math.Max(0.55, 1.0 - 0.11 * (bit - 3));
            }
            return 0.40;                                            // 24/48/96/192/384
        }

        /// <summary>Split a merged axis (one run across the whole image) into exact cells
        /// when the other axis's pitch is a standard size (or within 2 px of one) and
        /// divides the extent exactly. Count only — cell placement is untouched, and the
        /// merged ink is continuous so nothing can be cut.</summary>
        private static Axis SplitMergedAxis(Axis axis, int extent, int otherPitch)
        {
            if (!axis.Merged) return axis;
            if (otherPitch <= 1) return axis;
            int pitch = otherPitch;
            if (Array.IndexOf(StandardFrameSizes, pitch) < 0)
            {
                int nearest = -1, bestGap = 3;                 // within 2 px of a standard
                foreach (int s in StandardFrameSizes)
                {
                    int gap = Math.Abs(pitch - s);
                    if (gap < bestGap) { nearest = s; bestGap = gap; }
                }
                if (nearest <= 0) return axis;
                pitch = nearest;
            }
            if (extent % pitch != 0) return axis;              // uneven — no exact grid
            int count = extent / pitch;
            if (count < 2) return axis;
            return new Axis(0, pitch, 0, count, merged: true);
        }

        /// <summary>Runs of true values in the flag array. A transparent gap of at most
        /// BridgePx (1 px) is bridged into the surrounding run — that is anti-aliasing
        /// inside one sprite; every wider gap is a real gutter and ends the run.</summary>
        private static List<(int Start, int End)> InkRuns(bool[] flags)
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
                if (runStart < 0) continue; // not inside a run
                int gap = 0;
                while (x + gap < flags.Length && !flags[x + gap]) gap++;
                if (x + gap >= flags.Length)
                {
                    runs.Add((runStart, lastInk)); // trailing blanks — close the run
                    runStart = -1;
                    break;
                }
                if (gap <= BridgePx) { x += gap - 1; continue; } // bridge a hairline seam
                runs.Add((runStart, lastInk));           // real gutter ends the run
                runStart = -1;
            }
            if (runStart >= 0) runs.Add((runStart, lastInk));
            return runs;
        }
    }
}
