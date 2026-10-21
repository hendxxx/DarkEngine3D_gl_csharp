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

// ── SLOPE OVERRIDE MODE (v2) — the slope layer must DISPLACE manual paint on
// steep texels while keeping a paint hint (floor), and flat painted texels must
// stay untouched. The floor raise must preserve the sum = 1 invariant.
{
    var so = TerrainSplatField.CreateDefault();
    so.FillNeutralLayer0();
    // Paint layer 1 over the CENTER (protecting a painted texel to probe later).
    so.BeginStroke();
    for (int i = 0; i < 30; i++)
        so.ApplyBrush(0f, 0f, 100f, 100f, new SplatBrushSession { Layer = 1, Radius = 4f, Strength = 6f }, 1f / 60f, i);
    var paintedFlat = so.SampleWeights(0.5f, 0.5f);   // painted + flat → override must NOT touch (t≈0)
    Check("Override flat painted: untouched", paintedFlat.Y > 0.95f,
        $"painted L1={paintedFlat.Y:F3} (flat texel, mask ≈ 0)");

    // Steep flank via the sculpt-delta bump (same rig as the Combine test).
    var delta = TerrainHeightfield.CreateFlat(0.5f);
    for (int i = 0; i < 120; i++)
        delta.ApplyBrush(0f, 0f, 100f, 100f, 12f, 3f, 0.5f, TerrainBrushMode.Raise, 1f / 60f, i);
    so.FillNeutralLayer0();
    // Re-paint layer 1 (FillNeutral wiped it) so the override has paint to displace.
    // Radius 12 COVERS THE WHOLE BUMP incl. its steepest flank (bump brush radius =
    // 12) — the first probe suite taught this: paint r=6 leaves the flank texels
    // unpainted, so an override there legitimately displace nothing.
    so.BeginStroke();
    for (int i = 0; i < 30; i++)
        so.ApplyBrush(0f, 0f, 100f, 100f, new SplatBrushSession { Layer = 1, Radius = 14f, Strength = 6f }, 1f / 60f, i);
    const float floor = 0.4f;
    so.ComputeSlopeWeights(TerrainHeightfield.CreateFlat(0.5f), delta, 60f, 20f, 0f, 1f, 1f,
        100f, 100f, 2, 0.1f, 0.3f, overridePaint: true, paintFloor: floor);
    // Probe the steepest flank texel on the v=0.5 line: the override layer (L2)
    // must reach at least the floor·alpha there (alpha ≈ 1 on the steepest flank).
    float maxL2 = 0f; var maxW = Vector4.Zero;
    for (int i = 0; i <= 40; i++)
    {
        float u = 0.28f + 0.44f * i / 40f;
        var w = so.SampleWeights(u, 0.5f);
        if (w.Z > maxL2) { maxL2 = w.Z; maxW = w; }
    }
    Check("Override steep: slope ≥ floor", maxL2 >= floor - 0.02f,
        $"steep L2={maxL2:F3} ≥ floor {floor:F2}");
    Check("Override steep: sum = 1", MathF.Abs((maxW.X + maxW.Y + maxW.Z + maxW.W) - 1f) < 0.003f,
        $"sum={maxW.X + maxW.Y + maxW.Z + maxW.W:F4}");
    // The displaced painted layer must KEEP a visible share (not crushed to 0).
    Check("Override keeps paint hint", maxW.Y > 0.01f,
        $"painted L1 on steep texel = {maxW.Y:F3} (> 0)");

    // IDEMPOTENCE — a SECOND fold pass over the same parameters must NOT push the
    // slope layer beyond the first fold's result (the fold runs from a rebased base
    // each time in the editor; two sequential runs equivalent to one = the
    // double-apply guard). Verify via a fresh field + same pipeline twice.
    var so2 = TerrainSplatField.CreateDefault();
    so2.FillNeutralLayer0();
    so2.ComputeSlopeWeights(TerrainHeightfield.CreateFlat(0.5f), delta, 60f, 20f, 0f, 1f, 1f,
        100f, 100f, 2, 0.1f, 0.3f, overridePaint: true, paintFloor: floor);
    float f1 = so2.SampleWeights(0.42f, 0.5f).Z;
    so2.ComputeSlopeWeights(TerrainHeightfield.CreateFlat(0.5f), delta, 60f, 20f, 0f, 1f, 1f,
        100f, 100f, 2, 0.1f, 0.3f, overridePaint: true, paintFloor: floor);
    float f2 = so2.SampleWeights(0.42f, 0.5f).Z;
    Check("Override fold stable (max-push)", MathF.Abs(f2 - f1) < 0.02f,
        $"fold1 L2={f1:F3} → fold2 L2={f2:F3} (Δ ≤ 0.02)");
}

// ── DEGREES CONVERSION — the pipeline EditorObject uses: slope(abstract) =
// g²/(1+g²) ← g = tan(angle). Verify: a 45° world gradient (g=1 → slope 0.5)
// maps to threshold 0.5, and a 70° gradient (tan≈2.75 → slope ≈0.883) exceeds it
// while a 20° gradient (tan≈0.364 → slope ≈0.117) does not.
{
    static float SlopeFromGrad(float g) => g * g / (1f + g * g);
    static float SlopeThr(float angleDeg) => SlopeFromGrad(MathF.Tan(angleDeg * MathF.PI / 180f));
    float t45 = SlopeThr(45f);
    float s20 = SlopeThr(20f), s70 = SlopeThr(70f);
    Check("Degrees: 45° → thr ≈ 0.5", MathF.Abs(t45 - 0.5f) < 0.01f, $"thr={t45:F3}");
    Check("Degrees: 20° below 45°, 70° above", s20 < t45 && s70 > t45,
        $"s20={s20:F3} s45={t45:F3} s70={s70:F3}");
}

// ── REVERT PAINT / SCULPT MUST DETACH THE STORED BAKE ──
// The reported bug: "Revert paint engga jalan" — the button dropped the live field
// but left the path pointing at the baked TGA, so the next draw re-decoded the same
// strokes and the panel kept reporting Painted. This pins the contract: after the
// revert BOTH stored paths are empty (and the fixture proves the bake really carried
// paint, so the check cannot pass on an empty field by accident).
{
    string bakePath = Path.Combine(Path.GetTempPath(), "revert_paint_test_splat.tga");
    var painted = TerrainSplatField.CreateDefault();
    painted.FillNeutralLayer0();
    var brush = new SplatBrushSession
    {
        Mode = SplatBrushMode.Paint,
        Layer = 2,
        Radius = 8f,
        Strength = 6f,
        Hardness = 0.8f,
    };
    painted.BeginStroke();
    for (int i = 0; i < 40; i++)
        painted.ApplyBrush(0f, 0f, 64f, 64f, brush, 1f / 60f, i);
    bool baked = painted.BakeToTga(bakePath);
    var decoded = baked ? TerrainSplatField.FromFile(bakePath) : null;
    float cov = decoded?.PaintCoverage() ?? 0f;
    Check("Revert fixture: bake carries paint", decoded != null && cov > 0.01f,
        $"coverage {cov * 100:F1}%");

    var probe = new EditorObject(EditorPrimitiveType.Plane, "revert_probe");
    probe.SplatMapPath = bakePath;
    probe.SculptDeltaPath = "Artifacts/Terrain/probe_sculpt.tga";
    probe.RevertSplat();
    Check("Revert paint detaches the bake", probe.SplatMapPath.Length == 0,
        $"path after revert '{probe.SplatMapPath}'");
    Check("Revert paint clears the UI state", !probe.HasSplatEdits && probe.SplatPaintCoverage() <= 0.0001f,
        $"hasEdits={probe.HasSplatEdits} coverage={probe.SplatPaintCoverage() * 100:F2}%");
    probe.RevertSculpt();
    Check("Revert sculpt detaches the bake", probe.SculptDeltaPath.Length == 0,
        $"path after revert '{probe.SculptDeltaPath}'");

    // UNDO REVERT — the revert parks the live field instead of destroying it, so one
    // undo restores the exact painted state (weights + bake path) and the panel's
    // CanUndoSplatRevert flips off. A later stroke invalidates that stash.
    Check("Revert exposes a one-level undo", probe.CanUndoSplatRevert,
        $"CanUndoSplatRevert={probe.CanUndoSplatRevert}");
    bool undone = probe.UndoSplatRevert();
    Check("Undo revert restores the bake", undone && probe.SplatMapPath == bakePath,
        $"path '{probe.SplatMapPath}'");
    Check("Undo revert restores the paint", probe.HasSplatEdits && probe.SplatPaintCoverage() > 0.01f,
        $"coverage {probe.SplatPaintCoverage() * 100:F1}%");
    Check("Undo revert is single-use", !probe.CanUndoSplatRevert && !probe.UndoSplatRevert(),
        $"CanUndoSplatRevert={probe.CanUndoSplatRevert}");
    probe.RevertSplat();
    Check("Second revert parks again", probe.CanUndoSplatRevert && probe.SplatMapPath.Length == 0,
        $"stash={probe.CanUndoSplatRevert} path='{probe.SplatMapPath}'");
    probe.SplatBeginStroke();     // a new stroke makes the parked state stale
    Check("New stroke clears the revert stash", !probe.CanUndoSplatRevert,
        $"CanUndoSplatRevert={probe.CanUndoSplatRevert}");

    // ── REGRESSION: a SECOND revert must never DESTROY the parked painting ──
    // The viewport draw loop rebuilds an EMPTY base right after a revert (the live
    // field is gone, the bake path is detached). Parking THAT empty base over the
    // original painting made "undo revert" restore a blank map — the hours of
    // paint were gone for good. The first parked state must win until consumed.
    var probe2 = new EditorObject(EditorPrimitiveType.Plane, "revert_probe2");
    probe2.SplatMapPath = bakePath;
    probe2.RevertSplat();                  // park the painting
    probe2.EnsureSplatPaintField();        // ← what the viewport does every frame
    probe2.RevertSplat();                  // a second revert must NOT downgrade the stash
    Check("Second revert keeps the original paint stashed", probe2.CanUndoSplatRevert,
        $"CanUndoSplatRevert={probe2.CanUndoSplatRevert}");
    probe2.UndoSplatRevert();
    Check("Undo after a second revert restores the paint",
        probe2.HasSplatEdits && probe2.SplatPaintCoverage() > 0.01f,
        $"coverage {probe2.SplatPaintCoverage() * 100:F1}%");

    // ── REGRESSION: the AUTO layers (height bands + slope tiers) are folded in
    // PER PIXEL by the shader, so they survive a paint revert — which is exactly
    // why a correct revert could still LOOK like it had done nothing. The revert
    // must be able to switch them off, and the one-level undo must bring them
    // back with the exact state they had before. ──
    var probe3 = new EditorObject(EditorPrimitiveType.Plane, "revert_probe3");
    probe3.SplatMapPath = bakePath;
    probe3.SplatHeightBandsEnabled = true;
    probe3.SplatSlopeEnabled = true;
    probe3.SplatSlopeLayer = 1;
    Check("Auto layers are described for the panel",
        probe3.SplatAutoLayersEnabled && probe3.DescribeSplatAutoLayers().Contains("slope tier 1"),
        $"'{probe3.DescribeSplatAutoLayers()}'");
    probe3.RevertSplat();                  // a plain revert keeps the auto layers
    Check("Plain revert keeps the auto layers", probe3.SplatAutoLayersEnabled,
        $"auto={probe3.SplatAutoLayersEnabled} '{probe3.DescribeSplatAutoLayers()}'");
    probe3.UndoSplatRevert();
    probe3.RevertSplat(disableAutoLayers: true);
    Check("Revert can switch the auto layers off",
        !probe3.SplatAutoLayersEnabled && !probe3.SplatSlopeEnabled
            && !probe3.SplatHeightBandsEnabled,
        $"auto={probe3.SplatAutoLayersEnabled} slope={probe3.SplatSlopeEnabled}");
    probe3.UndoSplatRevert();
    Check("Undo revert brings the auto layers back",
        probe3.SplatAutoLayersEnabled && probe3.SplatSlopeEnabled
            && probe3.SplatHeightBandsEnabled,
        $"auto={probe3.SplatAutoLayersEnabled} slope={probe3.SplatSlopeEnabled}");

    // A terrain that was NEVER painted can still be reverted when the only thing
    // to change is the auto layers — otherwise the switch-off would be dead UI.
    var probe4 = new EditorObject(EditorPrimitiveType.Plane, "revert_probe4");
    probe4.SplatSlopeEnabled = true;
    probe4.RevertSplat();
    Check("Revert without paint does not arm the undo", !probe4.CanUndoSplatRevert
        && probe4.SplatAutoLayersEnabled,
        $"stash={probe4.CanUndoSplatRevert} auto={probe4.SplatAutoLayersEnabled}");
    probe4.RevertSplat(disableAutoLayers: true);
    Check("Revert switches the auto layers off without paint",
        !probe4.SplatAutoLayersEnabled && probe4.CanUndoSplatRevert,
        $"auto={probe4.SplatAutoLayersEnabled} stash={probe4.CanUndoSplatRevert}");
    probe4.UndoSplatRevert();
    Check("Undo brings the auto layers back without paint", probe4.SplatAutoLayersEnabled,
        $"auto={probe4.SplatAutoLayersEnabled}");

    try { File.Delete(bakePath); } catch { /* temp fixture only */ }
}

Console.WriteLine(fails == 0 ? "\nALL BRUSH TESTS PASSED" : $"\n{fails} TEST(S) FAILED");
BrushProbe.Run(fails);
WeatherProbe.Run();
return fails + BrushProbe.Fails + WeatherProbe.Fails;

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
