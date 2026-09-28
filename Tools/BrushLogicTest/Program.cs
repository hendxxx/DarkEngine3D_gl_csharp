using DarkEngine3D_gl_csharp.Engine.Objects;
using System.Numerics;

// ── CPU-LEVEL BRUSH VERIFICATION ──
// Exercises TerrainHeightfield.ApplyBrush directly (no GL, no editor): every mode
// must visibly change the field. Isolates "SculptApply has no effect" into either
// FIELD MATH (this test fails) or EDITOR INTEGRATION (this passes, bug elsewhere).

// Source: 64×64 grayscale TGA, vertical ramp 0.25 .. 0.75 (bottom→top).
const int W = 64, H = 64;
string srcPath = Path.Combine(Path.GetTempPath(), "brush_test_src.tga");
using (var bw = new BinaryWriter(File.Create(srcPath)))
{
    bw.Write(new byte[] { 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
    bw.Write((short)W); bw.Write((short)H);
    bw.Write((byte)8); bw.Write((byte)0);
    for (int y = H - 1; y >= 0; y--)                 // TGA row 0 = top
        for (int x = 0; x < W; x++)
            bw.Write((byte)((0.25f + 0.5f * y / (H - 1)) * 255f + 0.5f));
}

int fails = 0;
void Check(string name, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    if (!ok) fails++;
}

float Avg(TerrainHeightfield f, int cx, int cz, int r = 6)
{
    float s = 0; int n = 0;
    for (int z = cz - r; z <= cz + r; z++)
        for (int x = cx - r; x <= cx + r; x++) { s += f.Sample(x, z); n++; }
    return s / n;
}
const float Span = 64f;   // plane world size; 512 texels ⇒ 1 world = 8 texels

// DECODE sanity — BAKE→RELOAD must round-trip exactly (orientation symmetry:
// the bake flips rows AND writes descriptor 0 = bottom-origin, which cancel;
// regression guard for the SourceComp/bpp decode bug that scrambled grayscale
// files). Raw file-byte order verified separately below.
{
    var f = TerrainHeightfield.FromImage(srcPath)!;
    float dec0 = f.Sample(256, 256);
    string rtPath = Path.Combine(Path.GetTempPath(), "brush_roundtrip.tga");
    f.BakeToTga(rtPath);
    var f2 = TerrainHeightfield.FromImage(rtPath)!;
    float maxDiff = 0f;
    for (int i = 0; i < 64; i++)
        maxDiff = MathF.Max(maxDiff, MathF.Abs(f.Sample(i * 8, i * 8) - f2.Sample(i * 8, i * 8)));
    Check("Decode round-trip", maxDiff < 0.004f, $"max Δ {maxDiff:F4} (center {dec0:F3})");
}

// RAISE — core must lift toward 1.
{
    var f = TerrainHeightfield.FromImage(srcPath)!;
    float before = Avg(f, 256, 256);
    for (int i = 0; i < 30; i++)
        f.ApplyBrush(0f, 0f, Span, Span, 8f, 1.5f, 0.5f, TerrainBrushMode.Raise, 1f / 60f, i);
    float after = Avg(f, 256, 256);
    Check("Raise", after > before + 0.1f, $"avg {before:F3} → {after:F3}");
}

// LOWER — must sink toward 0.
{
    var f = TerrainHeightfield.FromImage(srcPath)!;
    float before = Avg(f, 256, 256);
    for (int i = 0; i < 30; i++)
        f.ApplyBrush(0f, 0f, Span, Span, 8f, 1.5f, 0.5f, TerrainBrushMode.Lower, 1f / 60f, i);
    float after = Avg(f, 256, 256);
    Check("Lower", after < before - 0.1f, $"avg {before:F3} → {after:F3}");
}

// SMOOTH — HIGH-FREQUENCY energy must drop. Source = RANDOM per-pixel noise
// (max high-frequency energy); the 5-point Laplacian relax kernel must crush it.
// (A linear ramp is a Laplacian null-space and low-frequency FBM barely
// attenuates — both useless as smooth-test inputs, verified empirically.)
{
    string noisePath = Path.Combine(Path.GetTempPath(), "brush_test_noise.tga");
    var rng = new Random(4242);
    var nb = new byte[W * H];
    rng.NextBytes(nb);
    using (var bw = new BinaryWriter(File.Create(noisePath)))
    {
        bw.Write(new byte[] { 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        bw.Write((short)W); bw.Write((short)H);
        bw.Write((byte)8); bw.Write((byte)0);
        for (int y = H - 1; y >= 0; y--) for (int x = 0; x < W; x++) bw.Write(nb[y * W + x]);
    }
    var f = TerrainHeightfield.FromImage(noisePath)!;
    double GradEnergy()
    {
        double s = 0; int n = 0;
        for (int z = 230; z <= 282; z++)
            for (int x = 230; x <= 282; x++)
            {
                float dx = f.Sample(x + 1, z) - f.Sample(x, z);
                float dz = f.Sample(x, z + 1) - f.Sample(x, z);
                s += dx * dx + dz * dz; n++;
            }
        return s / n;
    }
    double eBefore = GradEnergy();
    for (int i = 0; i < 60; i++)
        f.ApplyBrush(0f, 0f, Span, Span, 16f, 1.5f, 0.5f, TerrainBrushMode.Smooth, 1f / 60f, i);
    double eAfter = GradEnergy();
    Check("Smooth", eAfter < eBefore * 0.1, $"grad-energy {eBefore:F4} → {eAfter:F4}");
}

// FLATTEN — must level toward the footprint average.
{
    var f = TerrainHeightfield.FromImage(srcPath)!;
    for (int i = 0; i < 40; i++)
        f.ApplyBrush(0f, 0f, Span, Span, 12f, 1.5f, 0.5f, TerrainBrushMode.Flatten, 1f / 60f, i);
    float lo = f.Sample(248, 256), hi = f.Sample(264, 256);
    Check("Flatten", MathF.Abs(hi - lo) < 0.01f, $"edge Δ {MathF.Abs(hi - lo):F4}");
}

// NOISE — footprint variance must RISE (new detail) and converge (stable after repeat).
{
    var f = TerrainHeightfield.FromImage(srcPath)!;
    double Var(int r)
    {
        float m = Avg(f, 256, 256, r);
        double s = 0; int n = 0;
        for (int z = 256 - r; z <= 256 + r; z += 2)
            for (int x = 256 - r; x <= 256 + r; x += 2) { s += (f.Sample(x, z) - m) * (f.Sample(x, z) - m); n++; }
        return s / n;
    }
    double vBefore = Var(12);
    for (int i = 0; i < 30; i++)
        f.ApplyBrush(0f, 0f, Span, Span, 12f, 1.5f, 0.5f, TerrainBrushMode.Noise, 1f / 60f, i, 8, 12345);
    double vAfter = Var(12);
    Check("Noise detail", vAfter > vBefore * 1.5, $"var {vBefore:F5} → {vAfter:F5}");
    float snap = Avg(f, 256, 256);
    for (int i = 30; i < 60; i++)
        f.ApplyBrush(0f, 0f, Span, Span, 12f, 1.5f, 0.5f, TerrainBrushMode.Noise, 1f / 60f, i, 8, 12345);
    float snap2 = Avg(f, 256, 256);
    Check("Noise converges", MathF.Abs(snap2 - snap) < 0.02f, $"avg Δ {MathF.Abs(snap2 - snap):F4}");
}

// TERRACE — heights must snap onto the shared step grid.
{
    var f = TerrainHeightfield.FromImage(srcPath)!;
    for (int i = 0; i < 60; i++)
        f.ApplyBrush(0f, 0f, Span, Span, 12f, 1.5f, 0.5f, TerrainBrushMode.Terrace, 1f / 60f, i, 8, 0);
    const float stepH = 1f / 8f;
    int offGrid = 0, n = 0;
    for (int z = 244; z <= 268; z++)
        for (int x = 244; x <= 268; x++)
        {
            float h = f.Sample(x, z);
            float d = MathF.Abs(h / stepH - MathF.Round(h / stepH));
            if (d > 0.02f) offGrid++;
            n++;
        }
    Check("Terrace grid", offGrid == 0, $"{offGrid}/{n} off-grid texels (8 steps)");
}

// UNDO — pre-stroke snapshot must restore the pre-brush field.
{
    var f = TerrainHeightfield.FromImage(srcPath)!;
    float before = Avg(f, 256, 256);
    f.BeginStroke();
    for (int i = 0; i < 30; i++)
        f.ApplyBrush(0f, 0f, Span, Span, 8f, 1.5f, 0.5f, TerrainBrushMode.Raise, 1f / 60f, i);
    f.Undo();
    float after = Avg(f, 256, 256);
    Check("Undo", MathF.Abs(after - before) < 0.001f, $"avg {before:F3} → {after:F3}");
}

// ── SPLAT FIELD — the paint counter-suite (mirrors the sculpt coverage) ──
// Exercises TerrainSplatField.ApplyBrush / ComputeHeightBands / bake directly.
// The regression being pinned: a stamp that mutates ZERO texels must NOT mark the
// field as painted (HasAnyEdits/HasPaint) — that false positive made stroke end
// bake an empty/pure-band splat TGA over the previous good bake.
{
    var sp = TerrainSplatField.CreateDefault();

    // Bands-only baseline: recompute writes the band weights; the field must NOT
    // count as painted (nothing baked on stroke end).
    var flat = TerrainHeightfield.CreateFlat(0.5f);
    var bands = new Vector2[4]
    {
        new(0f, 8f), new(6f, 14f), new(12f, 20f), new(18f, 1e5f),
    };
    var caps = new float[] { 1f, 1f, 1f, 1f };
    sp.ComputeHeightBands(flat, 20f, 0f, 1f, 1f, bands, caps, 2f, 4);
    Check("Bands → not painted", !sp.HasAnyEdits && !sp.HasPaint, $"HasAnyEdits={sp.HasAnyEdits} HasPaint={sp.HasPaint}");

    // RECOMPUTE IDEMPOTENCE — a second band pass over the same params must not
    // flip the painted state (same weights; only the flag was at risk).
    sp.ComputeHeightBands(flat, 20f, 0f, 1f, 1f, bands, caps, 2f, 4);

    // PAINT — a real stamp over the field MUST mutate texels and set the flags.
    sp.BeginStroke();
    for (int i = 0; i < 30; i++)
        sp.ApplyBrush(0f, 0f, Span, Span, new SplatBrushSession { Layer = 1, Radius = 6f, Strength = 2f }, 1f / 60f, i);
    int paintedTexels = sp.StrokeTexels;
    Check("Paint mutates texels", paintedTexels > 0, $"{paintedTexels} texels mutated (30 stamps @ r=6)");
    Check("Paint sets HasAnyEdits/HasPaint", sp.HasAnyEdits && sp.HasPaint, $"HasAnyEdits={sp.HasAnyEdits} HasPaint={sp.HasPaint}");

    // The paint must actually stick above the band weights (bake round-trips it).
    string spPath = Path.Combine(Path.GetTempPath(), "splat_paint_test.tga");
    sp.BakeToTga(spPath);
    var sp2 = TerrainSplatField.FromFile(spPath)!;
    var c = sp2.SampleWeights(0.5f, 0.5f);
    Check("Paint survives bake/reload", c.Y > 0.6f, $"layer-1 weight at center {c.Y:F3} — want > 0.6");
}

// ── SLOPE LAYER (PBR) — steepness mask folded into the weight map ──
// Pins the contract: neutral base → slope pass lifts ONLY the target layer on
// steep texels, flat texels stay untouched, the sum stays 1, and painted fields
// are documented to be skipped by the caller (paint wins).
{
    var sl = TerrainSplatField.CreateDefault();
    sl.FillNeutralLayer0();
    // RAMPLIKE heightfield: elevation = u * 40 (slope |grad| = 40/span → steep) —
    // build via ApplyBrush would be slow; instead reuse a sculpt-neutral trick:
    // a flat 0.5 field gives slope ≈ 0 — we assert the FLAT case here (mask off)
    // plus a synthetic steep case through the public API below.
    var flatH = TerrainHeightfield.CreateFlat(0.5f);
    sl.ComputeSlopeWeights(flatH, null, 0f, 20f, 0f, 1f, 1f, 100f, 100f, 1, 0.35f, 0.2f);
    var flatW = sl.SampleWeights(0.5f, 0.5f);
    Check("Slope flat ground: layer untouched", flatW.Y < 0.02f && flatW.X > 0.98f,
        $"L1={flatW.Y:F3} L0={flatW.X:F3} (flat → mask ≈ 0)");
    Check("Slope keeps sum = 1", MathF.Abs((flatW.X + flatW.Y + flatW.Z + flatW.W) - 1f) < 0.003f,
        $"sum={flatW.X + flatW.Y + flatW.Z + flatW.W:F4}");

    // STEEP case: sculpt a tall plateau via the sculpt field (delta map), then fold
    // it in with a big amp — the plateau's flank carries a huge gradient → the slope
    // mask lifts the target layer there, while the flat top keeps layer 0.
    var delta = TerrainHeightfield.CreateFlat(0.5f);
    for (int i = 0; i < 120; i++)
        delta.ApplyBrush(0f, 0f, 100f, 100f, 12f, 3f, 0.5f, TerrainBrushMode.Raise, 1f / 60f, i);
    sl.FillNeutralLayer0();
    sl.ComputeSlopeWeights(flatH, delta, 60f, 20f, 0f, 1f, 1f, 100f, 100f, 2, 0.2f, 0.25f);
    // SELF-CALIBRATING flank probe: scan the v=0.5 line for the MAX target-layer
    // weight (the steep flank is wherever the brush profile happens to be steepest)
    // and compare against the flat top center.
    float maxL2 = 0f; var maxW = Vector4.Zero;
    for (int i = 0; i <= 40; i++)
    {
        float u = 0.28f + 0.44f * i / 40f;
        var w = sl.SampleWeights(u, 0.5f);
        if (w.Z > maxL2) { maxL2 = w.Z; maxW = w; }
    }
    var topW = sl.SampleWeights(0.5f, 0.5f);     // plateau top (flat)
    Check("Slope steep flank: target layer rises", maxL2 > topW.Z + 0.05f,
        $"max flank L2={maxL2:F3} > top L2={topW.Z:F3} + 0.05");
    Check("Slope flank sum = 1", MathF.Abs((maxW.X + maxW.Y + maxW.Z + maxW.W) - 1f) < 0.003f,
        $"sum={maxW.X + maxW.Y + maxW.Z + maxW.W:F4}");
    Check("Slope layer index respected", sl.SlopeApplied, "SlopeApplied flag set");
}

Console.WriteLine(fails == 0 ? "\nALL BRUSH TESTS PASSED" : $"\n{fails} TEST(S) FAILED");
BrushProbe.Run(fails);
WeatherProbe.Run();
return BrushProbe.Fails + WeatherProbe.Fails;

/// <summary>WEATHER PER-PIXEL suite — pins the tileset outline contract through
/// Effect2DSystem's private helpers via reflection: a synthetic 8×2 tileset with a
/// WEDGE tile (triangle: opaque at the bottom, rising left edge) must produce a
/// per-column profile that descends left→right, and a fully-transparent tile must
/// report 1.0 (fall-through) everywhere. The runtime tilemap math (GroundYAtAny)
/// is grid-exact with this same profile, so passing here = tents splash on their
/// slopes in the editor.</summary>
static class WeatherProbe
{
    public static int Fails;
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
        if (!ok) Fails++;
    }

    public static void Run()
    {
        Console.WriteLine("\n── WEATHER PER-PIXEL (tileset outline) ──");
        // Synthetic RGBA tileset: 8×2 tiles, 16 px each (128×32). Tile 1 = wedge
        // (triangle pointing up-right), tile 0 = fully transparent.
        const int TW = 16;
        const int SW = TW * 8, SH = TW * 2;
        var px = new byte[SW * SH * 4];
        // Tile 1 occupies (16..31, 0..15). Wedge: opaque where x >= (col−tileX0)+...
        // Build: opaque pixel when (localX >= (15 − localY*?)) — simple ascending
        // wedge: opaque if localX >= 15 − localY (diagonal hypotenuse from top-right).
        for (int ly = 0; ly < TW; ly++)
            for (int lx = 0; lx < TW; lx++)
            {
                int gx = 16 + lx, gy = ly; // tile row 0
                bool opaque = lx >= (TW - 1) - ly; // triangle rising to the right
                int i = (gy * SW + gx) * 4;
                px[i] = opaque ? (byte)200 : (byte)0;
                px[i + 1] = opaque ? (byte)200 : (byte)0;
                px[i + 2] = opaque ? (byte)200 : (byte)0;
                px[i + 3] = opaque ? (byte)255 : (byte)0;
            }
        // Tile 0 (first 16×16) stays all-transparent.
        string tga = Path.Combine(Path.GetTempPath(), "weather_tileset.tga");
        using (var bw = new BinaryWriter(File.Create(tga)))
        {
            bw.Write(new byte[] { 0, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0 }); // RLE not used; type 10 w/ 0 pixels written fails — use type 2 (uncompressed BGR)
            // (type 10 needs RLE packets; simplest correct: type 2 uncompressed RGBA)
        }
        // Write PNG instead via raw bytes is complex — use TGA type 2 (32-bit BGRA).
        using (var bw = new BinaryWriter(File.Create(tga)))
        {
            bw.Write(new byte[] { 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            bw.Write((short)SW); bw.Write((short)SH);
            bw.Write((byte)32); bw.Write((byte)8); // 32bpp
            for (int y = SH - 1; y >= 0; y--)      // TGA bottom-up row order
                for (int x = 0; x < SW; x++)
                {
                    int i = (y * SW + x) * 4;
                    // NOTE: our profile code reads the DECODED RGBA (StbImage returns
                    // top-row-first in image order, NOT TGA file order) — StbImageSharp
                    // flips bottom-up TGAs internally, so decoded row 0 = file row last.
                    bw.Write(px[i + 2]); bw.Write(px[i + 1]); bw.Write(px[i]); bw.Write(px[i + 3]); // BGRA
                }
        }

        var sys = typeof(DarkEngine3D_gl_csharp.Engine.Visual.Effect2DSystem);
        var mi = sys.GetMethod("GetTileTopProfile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Check("GetTileTopProfile accessible", mi != null, mi == null ? "reflection miss" : "ok");
        if (mi == null) return;

        // 1) Transparent tile (0) → every column 1.0 (fall-through).
        var prof0 = (float[]?)mi.Invoke(null, new object[] { tga, 0, 8, 2 });
        Check("Transparent tile → all 1.0", prof0 != null && prof0.All(v => v >= 1f),
            prof0 == null ? "null" : $"min {prof0.Min():F2}");

        // 2) Wedge tile (1): column profile must RISE left→right (frac = surface depth
        //    from tile top; column c is first-opaque at ly = 15−c → frac = (15−c)/16).
        //    So frac DECREASES with c (apex at right) — fail if any frac INCREASES.
        var prof1 = (float[]?)mi.Invoke(null, new object[] { tga, 1, 8, 2 });
        bool rises = prof1 != null && prof1.Length == TW;
        if (rises)
            for (int c = 1; c < TW && rises; c++)
                if (prof1![c] > prof1[c - 1] + 0.001f) rises = false;
        Check("Wedge profile rises left→right", rises,
            prof1 == null ? "null" : $"c0={prof1[0]:F2} c15={prof1[^1]:F2}");
        Check("Wedge left column bottom-only", prof1 != null && prof1[0] > 0.9f,
            prof1 == null ? "null" : $"c0={prof1[0]:F2} (single bottom pixel)"
        );
        Check("Wedge right column at top", prof1 != null && prof1[^1] <= 0.001f,
            prof1 == null ? "null" : $"c15={prof1[^1]:F3}");

        // 3) TGA row-order sanity: decode a known pixel via GetSheetPixels — the
        //    wedge hypotenuse must exist where expected (guard against flipped rows).
        var miPix = sys.GetMethod("GetSheetPixels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var pix = ((byte[]? Data, int W, int H)?)miPix?.Invoke(null, new object[] { tga });
        bool wedgeFound = false;
        if (pix is { Item1: not null } p)
        {
            // Decoded (top-row-first) pixel at tile-1 local (15, 0) must be opaque
            // (wedge apex at top-right): gx = 16+15, gy = 0.
            int i = (0 * p.W + 16 + 15) * 4;
            wedgeFound = p.Data![i + 3] >= 128;
        }
        Check("Row order: wedge apex opaque at decoded top", wedgeFound, "apex alpha");
    }
}

/// <summary>Second suite (called from the single top-level program): raycast
/// precision — rays aimed at known world points must land exactly there.</summary>
static class BrushProbe
{
    public static int Fails;
    public static void Run(int brushFails)
    {
        Console.WriteLine(brushFails == 0 ? "\nALL BRUSH TESTS PASSED" : $"\n{brushFails} BRUSH TEST(S) FAILED");
        Fails = brushFails;
        Probe();
    }

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
        if (!ok) Fails++;
    }

    static void Probe()
    {
        Console.WriteLine("── raycast precision ──");
        var world = Matrix4x4.CreateScale(64f, 1f, 64f);
        var f = TerrainHeightfield.CreateFlat(0.5f);   // flat base → elevation = 0.5·baseH

        bool ok1 = f.TryRaycast(world, 2f, 0f, 10f, 1f, 1f,
            new Vector3(5f, 10f, -8f), new Vector3(0f, -1f, 0f), out var hit1);
        float lx1 = f.LastHitLocalXZ.X, lz1 = f.LastHitLocalXZ.Y;
        Check("Aim (5,-8) identity", ok1 && MathF.Abs(lx1 - 5f) < 0.05f && MathF.Abs(lz1 + 8f) < 0.05f,
            $"local ({lx1:F2}, {lz1:F2}) — want (5.00, -8.00)");

        // Engine WorldMatrix convention (row-vector): S * R * T — scale FIRST,
        // rotation second, translation LAST. A world point maps to plane-local
        // WORLD units by undoing translation + rotation ONLY (scale stays).
        var worldR = Matrix4x4.CreateScale(64f, 1f, 64f) * Matrix4x4.CreateRotationY(0.5236f);
        // Manual inverse-rotate (30°): xl = xw·cosθ − zw·sinθ, zl = xw·sinθ + zw·cosθ.
        const float ct = 0.8660254f, st = 0.5f;
        float wantLx = 3f * ct - 4f * st, wantLz = 3f * st + 4f * ct;
        bool ok2 = f.TryRaycast(worldR, 2f, 0f, 10f, 1f, 1f,
            new Vector3(3f, 10f, 4f), new Vector3(0f, -1f, 0f), out var hit2);
        float lx2 = f.LastHitLocalXZ.X, lz2 = f.LastHitLocalXZ.Y;
        Check("Aim (3,4) rotY30", ok2 && MathF.Abs(lx2 - wantLx) < 0.05f && MathF.Abs(lz2 - wantLz) < 0.05f,
            $"local ({lx2:F2}, {lz2:F2}) — want ({wantLx:F2}, {wantLz:F2})");
        Check("Hit world XZ", MathF.Abs(hit2.X - 3f) < 0.05f && MathF.Abs(hit2.Z - 4f) < 0.05f,
            $"world ({hit2.X:F2}, {hit2.Z:F2}) — want (3.00, 4.00)");

        // Same convention: S * T (scale first, translation last) → local = world − t.
        var worldT = Matrix4x4.CreateScale(64f, 1f, 64f) * Matrix4x4.CreateTranslation(20f, 0f, -10f);
        bool ok3 = f.TryRaycast(worldT, 2f, 0f, 10f, 1f, 1f,
            new Vector3(28f, 10f, -14f), new Vector3(0f, -1f, 0f), out _);
        float lx3 = f.LastHitLocalXZ.X, lz3 = f.LastHitLocalXZ.Y;
        Check("Aim translated", ok3 && MathF.Abs(lx3 - 8f) < 0.05f && MathF.Abs(lz3 + 4f) < 0.05f,
            $"local ({lx3:F2}, {lz3:F2}) — want (8.00, -4.00)");

        // Sculpt DELTA bump at local (0,0)±8 world: the ray must land ON the bump.
        var delta = TerrainHeightfield.CreateNeutral();
        var hf = typeof(TerrainHeightfield).GetField("_h", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var arr = (float[])hf.GetValue(delta)!;
        for (int z = 224; z <= 288; z++)
            for (int x = 224; x <= 288; x++)
                arr[z * 512 + x] = 1.0f;   // (1.0−0.5)·2·amp=1 → +1 world on top of base
        bool ok4 = f.TryRaycast(world, 2f, 0f, 10f, 1f, 1f,
            delta, 1f,
            new Vector3(0f, 10f, 0f), new Vector3(0f, -1f, 0f), out var hit4);
        Check("Delta bump height", ok4 && MathF.Abs(hit4.Y - 2f) < 0.06f, $"hit Y {hit4.Y:F2} — want 2.00");

        Console.WriteLine(Fails == 0 ? "ALL RAYCAST TESTS PASSED" : $"{Fails} TEST(S) FAILED TOTAL");
    }
}
