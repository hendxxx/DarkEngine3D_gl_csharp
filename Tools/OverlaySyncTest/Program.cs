using System.Text.Json;
using StbImageSharp;

// ═══════════════════════════════════════════════════════════════════════
// TEMPORARY check for the bake-time loadout frame mapping.
// The bake used to sample a synced layer at the STRIP position (0..N-1) while the
// editor preview and the runtime sample the BASE FRAME'S SHEET INDEX (60..69 for
// '-dead'). This reads the real project art and measures how well the hair cell
// lines up with the body cell for both candidates. Exit 1 on any failure.
// Usage: dotnet run -- "<project Artifacts/Sprites dir>"
// ═══════════════════════════════════════════════════════════════════════

int fails = 0;
void Check(bool ok, string name, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
    if (!ok) fails++;
}

string dir = args.Length > 0 ? args[0] : "Dark Projects/Sample game 2D Forest Adventures/Artifacts/Sprites";
string jsonPath = Path.Combine(dir, "sprites.sheets.json");
Check(File.Exists(jsonPath), "project manifest found", jsonPath);
if (!File.Exists(jsonPath)) return 1;

using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
var root = doc.RootElement;

(byte[] Pixels, int ImgW, List<(int X, int Y, int W, int H)> Frames) LoadSheet(string name)
{
    foreach (var s in root.GetProperty("SourceSheets").EnumerateArray())
    {
        if (!string.Equals(s.GetProperty("Name").GetString(), name, StringComparison.OrdinalIgnoreCase)) continue;
        string path = s.GetProperty("ImagePath").GetString() ?? "";
        using var stream = File.OpenRead(path);
        var img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        var frames = new List<(int, int, int, int)>();
        foreach (var f in s.GetProperty("CustomFrames").EnumerateArray())
            frames.Add((f.GetProperty("X").GetInt32(), f.GetProperty("Y").GetInt32(),
                f.GetProperty("Width").GetInt32(), f.GetProperty("Height").GetInt32()));
        return (img.Data, img.Width, frames);
    }
    throw new InvalidDataException($"sheet '{name}' missing from {jsonPath}");
}

var skin = LoadSheet("Male Skin1");
var hair = LoadSheet("Male Hair1");
Check(skin.Frames.Count == hair.Frames.Count, "both sheets share the same grid",
    $"{skin.Frames.Count} vs {hair.Frames.Count} frames");

static byte[] Cell(byte[] data, int imgW, (int X, int Y, int W, int H) f)
{
    var px = new byte[f.W * f.H * 4];
    for (int y = 0; y < f.H; y++)
        Buffer.BlockCopy(data, ((f.Y + y) * imgW + f.X) * 4, px, y * f.W * 4, f.W * 4);
    return px;
}

static (float Cx, float Top, int Count) Ink(byte[] px, int w, int h)
{
    int minX = w, maxX = -1, minY = h, n = 0;
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            if (px[(y * w + x) * 4 + 3] > 8)
            {
                n++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
            }
    return n == 0 ? (0, 0, 0) : ((minX + maxX) * 0.5f, minY, n);
}

static float Overlap(byte[] a, byte[] b, int w, int h)
{
    int hit = 0, total = 0;
    for (int p = 0; p < w * h; p++)
    {
        if (a[p * 4 + 3] <= 8) continue;
        total++;
        if (b[p * 4 + 3] > 8) hit++;
    }
    return total == 0 ? 0f : hit / (float)total;
}

// Base clips with their real SOURCE sheet frame indices (from the manifest).
var clips = new (string Name, int First, int Count)[]
{
    ("Male Skin1-walk", 10, 8),
    ("Male Skin1-attack", 50, 6),
    ("Male Skin1-dead", 60, 10),
};

bool allBetter = true;
foreach (var (name, first, count) in clips)
{
    float correctMis = 0f, oldMis = 0f, correctOv = 0f, oldOv = 0f;
    for (int i = 0; i < count; i++)
    {
        int baseIdx = first + i;
        var baseCell = Cell(skin.Pixels, skin.ImgW, skin.Frames[baseIdx]);
        var hairSame = Cell(hair.Pixels, hair.ImgW, hair.Frames[baseIdx]); // fixed: base sheet index
        var hairOld = Cell(hair.Pixels, hair.ImgW, hair.Frames[i]);        // bug: strip position
        var b = Ink(baseCell, 64, 64);
        var hs = Ink(hairSame, 64, 64);
        var ho = Ink(hairOld, 64, 64);
        correctMis += MathF.Abs(hs.Cx - b.Cx) + MathF.Abs(hs.Top - b.Top);
        oldMis += MathF.Abs(ho.Cx - b.Cx) + MathF.Abs(ho.Top - b.Top);
        correctOv += Overlap(hairSame, baseCell, 64, 64);
        oldOv += Overlap(hairOld, baseCell, 64, 64);
    }
    correctMis /= count; oldMis /= count; correctOv /= count; oldOv /= count;
    bool better = correctOv > oldOv;
    allBetter &= better;
    Console.WriteLine($"INFO  {name}: misalign base-sheet {correctMis:F2}px / strip-pos {oldMis:F2}px "
        + $"| hair-on-body overlap {correctOv:P0} / {oldOv:P0}");
}

Check(allBetter, "synced layers align better at the base frame's sheet index than at the strip position");
Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILURE(S)");
return fails == 0 ? 0 : 1;
