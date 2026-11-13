using System.Reflection;
using DarkEngine3D_gl_csharp.Engine.IDE.Panels;

// Guards the terrain LOD distance planner against the crash that took the
// editor down: Math.Clamp(x, previous + 1, cap) throws as soon as `previous`
// reaches `cap` ("'251' cannot be greater than 250."), which happened for a
// ~100-unit terrain with 2 chunks and a heightmap. The planner now scales all
// three rings instead, so this harness sweeps the whole input space and
// asserts the result is callable, ordered and capped.

var method = typeof(TerrainPanel).GetMethod("LodDistances", BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("TerrainPanel.LodDistances not found (renamed?)");

int checks = 0, failures = 0;

void Check(string what, float d0, float d1, float d2, float footprint)
{
    checks++;
    if (float.IsNaN(d0) || float.IsNaN(d1) || float.IsNaN(d2))
        Fail(what, $"NaN in {d0}/{d1}/{d2}");
    else if (d0 <= 0f || d0 >= d1 || d1 >= d2)
        Fail(what, $"not strictly increasing: {d0:F2} / {d1:F2} / {d2:F2}");
    else if (d2 > footprint * 3f + 1e-3f)
        Fail(what, $"past the cap: d2 {d2:F2} > {footprint * 3f:F2}");
}

void Fail(string what, string why)
{
    failures++;
    Console.WriteLine($"FAIL {what}: {why}");
}

(float D0, float D1, float D2) Plan(float footprint, int chunks, float relief, float scale)
{
    var result = method.Invoke(null, [footprint, chunks, relief, scale])
        ?? throw new InvalidOperationException("LodDistances returned null");
    return ((float, float, float))result;
}

float[] footprints = [1f, 5f, 20f, 50f, 100f, 125f, 250f, 500f, 1000f, 5000f];
float[] reliefs = [0f, 5f, 25f, 100f, 400f];
float[] scales = [0.7f, 1.0f, 1.6f];

foreach (var foot in footprints)
    foreach (var chunks in Enumerable.Range(1, 8))
        foreach (var relief in reliefs)
            foreach (var scale in scales)
            {
                string what = $"foot {foot} chunks {chunks} relief {relief} scale {scale}";
                try
                {
                    var (d0, d1, d2) = Plan(foot, chunks, relief, scale);
                    Check(what, d0, d1, d2, foot);
                }
                catch (TargetInvocationException ex)
                {
                    // The exact regression: these bounds used to reach Math.Clamp(251, 250).
                    Fail(what, ex.InnerException?.Message ?? ex.Message);
                }
            }

// The exact reported crash, spelled out so a regression names itself.
try
{
    var (d0, d1, d2) = Plan(100f, 2, 30f, 1.0f);
    Check("regression: 100-unit 2-chunk terrain with relief (was 251 > 250)", d0, d1, d2, 100f);
    Console.WriteLine($"     → {d0:F1} / {d1:F1} / {d2:F1} world (cap 300)");
}
catch (TargetInvocationException ex)
{
    Fail("regression: 100-unit 2-chunk terrain with relief", ex.InnerException?.Message ?? ex.Message);
}

Console.WriteLine(failures == 0
    ? $"ALL TERRAIN LOD PLAN CHECKS PASS ({checks} checks)"
    : $"{failures} FAILURE(S) of {checks} checks");
return failures == 0 ? 0 : 1;
