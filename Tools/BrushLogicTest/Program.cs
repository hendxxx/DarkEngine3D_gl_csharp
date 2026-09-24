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

Console.WriteLine(fails == 0 ? "\nALL BRUSH TESTS PASSED" : $"\n{fails} TEST(S) FAILED");
BrushProbe.Run(fails);
return BrushProbe.Fails;

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
