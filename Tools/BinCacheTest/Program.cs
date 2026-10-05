using System.Text.Json;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Scene;

// ═══════════════════════════════════════════════════════════════════════
// BinaryObjectCache CPU verification — exit code 1 on ANY failure.
// 1. Round-trip binary == JSON parse (real SceneManifest shape).
// 2. Stale detection (JSON edited after cache write → null → parse).
// 3. Corrupt cache fallback (garbage sidecar never breaks the load).
// 4. Type guard (SceneAsset sidecar rejected when reading SceneManifest).
// 5. Collections/enums/nullable/Vectors round-trip.
// ═══════════════════════════════════════════════════════════════════════

int fails = 0;
void Check(bool ok, string name, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? $" — {detail}" : "")}");
    if (!ok) fails++;
}

string dir = Path.Combine(Path.GetTempPath(), "bincache_test_" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(dir);

// ── Build a realistic SceneManifest graph (the game.ing DTO shape) ──
var asset = new SceneAsset
{
    SceneName = "TestLevel",
};
asset.Elements.Add(new SceneElementData
{
    Type = "Group",
    Name = "Root",
    Children = new List<SceneElementData>
    {
        new() { Type = "Text", Name = "T1", Text = "Halo dunia — unicode ok" },
        new() { Type = "Image", Name = "I1", ImagePath = "img.png", Opacity = 0.75f },
    },
});
var manifest = new SceneManifest();
manifest.Scenes.Add(asset);

string jsonPath = Path.Combine(dir, "game.ing");
File.WriteAllText(jsonPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

// ── 1. Write sidecar, then load it back and compare with the JSON parse ──
BinaryObjectCache.TryWrite(jsonPath, manifest);
string cachePath = Path.Combine(dir, ".cache", "game.ing.bin");
Check(File.Exists(cachePath), "sidecar written next to JSON");

var viaJson = JsonSerializer.Deserialize<SceneManifest>(File.ReadAllText(jsonPath));
var viaBin = BinaryObjectCache.TryLoad<SceneManifest>(jsonPath);
Check(viaBin != null && BinaryObjectCache.LastLoadWasBinary, "binary load consumed the sidecar");
string jsonNorm = JsonSerializer.Serialize(viaJson);
string binNorm = JsonSerializer.Serialize(viaBin);
Check(jsonNorm == binNorm, "binary round-trip == JSON parse", jsonNorm == binNorm ? "" : "graphs differ");

// ── 2. Staleness: edit the JSON after the cache write → cache must be rejected ──
File.WriteAllText(jsonPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
var stale = BinaryObjectCache.TryLoad<SceneManifest>(jsonPath); // TryLoad resets the telemetry flag itself
Check(!BinaryObjectCache.LastLoadWasBinary, "stale JSON → sidecar rejected (hash/len/mtime mismatch)");
Check(stale is null || JsonSerializer.Serialize(stale) == jsonNorm, "stale fallback parses cleanly");

// ── 3. Corruption: garbage bytes in the sidecar → never throws, falls back ──
BinaryObjectCache.TryWrite(jsonPath, manifest);
byte[] garbage = new byte[2048];
new Random(7).NextBytes(garbage);
File.WriteAllBytes(cachePath, garbage);
bool threw = false;
SceneManifest? corrupt = null;
try { corrupt = BinaryObjectCache.TryLoad<SceneManifest>(jsonPath); }
catch (Exception ex) { threw = true; Console.WriteLine($"      unexpected throw: {ex.Message}"); }
Check(!threw, "corrupt sidecar never throws");
// Fallback contract: TryLoad signals "parse the JSON yourself" by returning null —
// the callers wire `TryLoad(...) ?? JsonSerializer.Deserialize(json)`. So after a
// corrupt sidecar the correct result is null + telemetry says not-binary.
Check(!BinaryObjectCache.LastLoadWasBinary && corrupt == null, "corrupt sidecar → clean fallback (null → caller parses JSON)");

// ── 4. Root-type guard: a SceneAsset sidecar must not load as SceneManifest ──
BinaryObjectCache.TryWrite(jsonPath, asset);
var wrongType = BinaryObjectCache.TryLoad<SceneManifest>(jsonPath);
Check(!BinaryObjectCache.LastLoadWasBinary, "type guard rejects foreign root type");

// ── 5. Collection/enum/nullable/Vector round-trips ──
string jsonPath2 = Path.Combine(dir, "mix.json");
var mix = new MixPayload
{
    Items = new List<MixItem>
    {
        new() { Id = "a", Kind = MixKind.Beta, Weight = 0.5f, Tags = new List<string> { "x", "y" } },
        new() { Id = "b", Kind = MixKind.Alpha, Weight = null, Tags = new List<string>() },
    },
    Lookup = new Dictionary<string, int> { ["k1"] = 11, ["k2"] = -3 },
    Points = new List<System.Numerics.Vector3> { new(1, 2, 3), new(-0.5f, 0.25f, 9.75f) },
    Flags = new[] { true, false, true },
};
File.WriteAllText(jsonPath2, JsonSerializer.Serialize(mix));
BinaryObjectCache.TryWrite(jsonPath2, mix);
var mixBack = BinaryObjectCache.TryLoad<MixPayload>(jsonPath2);
Check(mixBack != null && JsonSerializer.Serialize(mixBack) == JsonSerializer.Serialize(mix),
    "List/Dictionary/array/enum/nullable/Vector round-trip");

// ── 6. Benchmark (INFO, non-fatal): JSON read+parse vs binary cache on a big manifest ──
string bigPath = Path.Combine(dir, "big.ing");
var big = new SceneManifest();
var bigAsset = new SceneAsset { SceneName = "BigLevel" };
for (int i = 0; i < 1500; i++)
{
    bigAsset.Elements.Add(new SceneElementData
    {
        Type = i % 3 == 0 ? "Image" : "Text",
        Name = $"el_{i}",
        Text = i % 3 == 0 ? "" : $"Label {i} — some longer description text to inflate the payload size",
        ImagePath = $"Assets/Sprites/tex_{i % 40}.png",
        Opacity = 0.25f + (i % 50) * 0.01f,
        Children = new List<SceneElementData>
        {
            new() { Type = "Text", Name = $"child_{i}_a", Text = "nested child content for the tree walk" },
            new() { Type = "Image", Name = $"child_{i}_b", ImagePath = "Assets/images/icon.png", Opacity = 0.9f },
        },
    });
}
big.Scenes.Add(bigAsset);
File.WriteAllText(bigPath, JsonSerializer.Serialize(big, new JsonSerializerOptions { WriteIndented = true }));
BinaryObjectCache.TryWrite(bigPath, big);
string bigCachePath = Path.Combine(dir, ".cache", "big.ing.bin");
_ = JsonSerializer.Deserialize<SceneManifest>(File.ReadAllText(bigPath)); // JIT warmup
_ = BinaryObjectCache.TryLoad<SceneManifest>(bigPath);
const int benchIters = 40;
var sw = System.Diagnostics.Stopwatch.StartNew();
for (int i = 0; i < benchIters; i++) _ = JsonSerializer.Deserialize<SceneManifest>(File.ReadAllText(bigPath));
sw.Stop();
double jsonMs = sw.Elapsed.TotalMilliseconds / benchIters;
sw.Restart();
for (int i = 0; i < benchIters; i++) _ = BinaryObjectCache.TryLoad<SceneManifest>(bigPath);
sw.Stop();
double binMs = sw.Elapsed.TotalMilliseconds / benchIters;
Console.WriteLine($"INFO  benchmark: JSON {jsonMs:F2} ms vs binary {binMs:F2} ms per load = {jsonMs / Math.Max(binMs, 0.0001):F1}x faster | {new FileInfo(bigPath).Length / 1024.0:F0} KB json -> {new FileInfo(bigCachePath).Length / 1024.0:F0} KB sidecar");

Console.WriteLine(fails == 0 ? "== ALL PASS ==" : $"== {fails} FAILURES ==");
return fails == 0 ? 0 : 1;

// ── Local DTO shapes (mirror engine-style graphs) ──
public enum MixKind { Alpha, Beta }

public class MixItem
{
    public string Id { get; set; } = "";
    public MixKind Kind { get; set; }
    public float? Weight { get; set; }
    public List<string>? Tags { get; set; }
}

public class MixPayload
{
    public List<MixItem> Items { get; set; } = new();
    public Dictionary<string, int> Lookup { get; set; } = new();
    public List<System.Numerics.Vector3> Points { get; set; } = new();
    public bool[] Flags { get; set; } = Array.Empty<bool>();
}
