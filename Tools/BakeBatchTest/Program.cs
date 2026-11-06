using DarkEngine3D_gl_csharp.Engine.Visual;
using StbImageSharp;

// ═══════════════════════════════════════════════════════════════════════
// TEMPORARY check for the "Bake All without freezing" path.
// Bake All now composites every clip on the UI thread (cheap) and hands the frames to
// SpriteAtlasBaker.RepackAllAsync, which packs them in ONE worker pass. Clips that were
// not rebaked arrive as existing atlas references. This exercises that mixed input and
// verifies the staged atlas really contains both kinds of frame, in clip order.
// ═══════════════════════════════════════════════════════════════════════

int fails = 0;
void Check(bool ok, string name, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
    if (!ok) fails++;
}

static BakedSpriteFrame Frame(int w, int h, byte r, byte g, byte b) => new()
{
    Pixels = Fill(w, h, r, g, b),
    Width = w,
    Height = h,
    Metadata = new SpriteFrame { X = 0, Y = 0, Width = w, Height = h, Name = $"synth-{r}-{g}-{b}" }
};

static byte[] Fill(int w, int h, byte r, byte g, byte b)
{
    var px = new byte[w * h * 4];
    for (int p = 0; p < w * h; p++)
    {
        px[p * 4] = r;
        px[p * 4 + 1] = g;
        px[p * 4 + 2] = b;
        px[p * 4 + 3] = 255;
    }
    return px;
}

string dir = Path.Combine(Path.GetTempPath(), "bakebatch-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    SpriteAtlasBaker.EnsureDefaultAtlas(dir);

    // 1) A clip that already lives in the atlas (the "preserved" part of a bake batch).
    var preservedFrames = Enumerable.Range(0, 4)
        .Select(i => Frame(32, 32, (byte)(40 * i + 10), 200, 30)).ToList();
    var atlasSheets = new List<SpriteSheet>();
    var firstBake = SpriteAtlasBaker.BakeClip(dir, "preserved", preservedFrames, atlasSheets, []);
    Check(firstBake != null && firstBake.FrameIndices.Count == 4, "existing clip baked the old way");
    if (firstBake == null) return 1;
    atlasSheets.Add(firstBake.Atlas);

    // 2) Freshly composited frames — exactly what Bake All now hands the worker.
    var bakedFrames = Enumerable.Range(0, 5)
        .Select(i => Frame(64, 48, (byte)(200 - i * 20), (byte)(i * 40), 255)).ToList();

    var sources = new List<SpriteAtlasRepackSource>
    {
        new("mainsprite1",
            Path.Combine(dir, "mainsprite1.png"),
            Path.Combine(dir, "mainsprite1Normal.png"),
            firstBake.Atlas.CustomFrames!.Select(f => new SpriteFrame
            {
                X = f.X, Y = f.Y, Width = f.Width, Height = f.Height, Name = f.Name,
                AnchorX = f.AnchorX, AnchorY = f.AnchorY,
                RenderOffsetX = f.RenderOffsetX, RenderOffsetY = f.RenderOffsetY,
                HitboxX = f.HitboxX, HitboxY = f.HitboxY, HitboxW = f.HitboxW, HitboxH = f.HitboxH,
                Tags = new Dictionary<string, string>(f.Tags)
            }).ToList())
    };

    var preservedInputs = firstBake.FrameIndices.Select(idx =>
    {
        var meta = firstBake.Atlas.CustomFrames![idx];
        return new SpriteAtlasRepackFrameInput("mainsprite1", idx, meta.Width, meta.Height, meta);
    }).ToList();

    var clips = new List<SpriteAtlasRepackClipInput>
    {
        new("preserved", preservedInputs),
        new("baked-clip", bakedFrames.Select((f, i) => new SpriteAtlasRepackFrameInput(
            "Male Skin1", i, f.Width, f.Height, f.Metadata, f)).ToList())
    };

    var staged = await SpriteAtlasBaker.RepackAllAsync(dir, sources, clips);
    Check(staged.Atlases.Count >= 1, "staged atlas set written", $"{staged.Atlases.Count} atlas(es)");
    Check(staged.Clips.Count == 2, "every clip survived the pack", $"{staged.Clips.Count} clip placement(s)");

    foreach (var atlas in staged.Atlases)
    {
        Check(File.Exists(Path.Combine(staged.StagingDirectory, atlas.Name + ".png")),
            $"staged color atlas '{atlas.Name}' exists");
        Check(File.Exists(Path.Combine(staged.StagingDirectory, atlas.Name + "Normal.png")),
            $"staged normal atlas '{atlas.Name}' exists");
    }

    var bakedPlacement = staged.Clips.FirstOrDefault(c => c.ClipName == "baked-clip");
    var preservedPlacement = staged.Clips.FirstOrDefault(c => c.ClipName == "preserved");
    Check(bakedPlacement?.FramePlacements.Count == 5, "in-memory clip kept all 5 frames");
    Check(preservedPlacement?.FramePlacements.Count == 4, "preserved clip kept all 4 frames");

    // Atlas frame indices must be one consecutive run per clip (no holes / duplicates) so the
    // clip's FrameIndices list stays a simple slice of the packed atlas.
    foreach (var clip in staged.Clips)
    {
        var indices = clip.FramePlacements.Select(p => p.AtlasFrameIndex).OrderBy(i => i).ToList();
        bool consecutive = indices.Count > 0
            && indices.Distinct().Count() == indices.Count
            && indices[^1] - indices[0] + 1 == indices.Count;
        Check(consecutive, $"clip '{clip.ClipName}' frame indices are one consecutive run",
            string.Join(",", indices));
    }

    // Row-major: each clip's frames flow left→right, wrapping to a new row at x < previous x.
    if (bakedPlacement != null)
    {
        var p = bakedPlacement.FramePlacements;
        bool ordered = true;
        for (int i = 1; i < p.Count; i++)
        {
            bool sameRow = p[i].Y == p[i - 1].Y && p[i].X > p[i - 1].X;
            bool nextRow = p[i].Y > p[i - 1].Y && p[i].X < p[i - 1].X;
            if (!sameRow && !nextRow) ordered = false;
        }
        Check(ordered, "frames stay in authored order (left→right, wrap to the next row)");
    }

    // Pixels: the in-memory art and the preserved art must both be in the staged PNG.
    if (bakedPlacement != null && preservedPlacement != null)
    {
        var colorFiles = staged.Atlases.ToDictionary(a => a.Name,
            a => ImageResult.FromStream(File.OpenRead(Path.Combine(staged.StagingDirectory, a.Name + ".png")),
                ColorComponents.RedGreenBlueAlpha));
        bool memoryOk = true, preservedOk = true;
        for (int i = 0; i < bakedPlacement.FramePlacements.Count; i++)
        {
            var placement = bakedPlacement.FramePlacements[i];
            var atlas = staged.Atlases.First(a => a.Name == $"mainsprite{bakedPlacement.AtlasNumber}");
            var px = colorFiles[atlas.Name].Data;
            int center = ((placement.Y + placement.Height / 2) * SpriteAtlasBaker.AtlasSize
                + placement.X + placement.Width / 2) * 4;
            var expected = bakedFrames[i].Pixels[0];
            if (px[center] != expected || px[center + 1] != bakedFrames[i].Pixels[1]
                || px[center + 3] != 255) memoryOk = false;
        }
        for (int i = 0; i < preservedPlacement.FramePlacements.Count; i++)
        {
            var placement = preservedPlacement.FramePlacements[i];
            var atlas = staged.Atlases.First(a => a.Name == $"mainsprite{preservedPlacement.AtlasNumber}");
            var px = colorFiles[atlas.Name].Data;
            int center = ((placement.Y + placement.Height / 2) * SpriteAtlasBaker.AtlasSize
                + placement.X + placement.Width / 2) * 4;
            if (px[center] != preservedFrames[i].Pixels[0]
                || px[center + 1] != preservedFrames[i].Pixels[1]) preservedOk = false;
        }
        Check(memoryOk, "freshly baked (in-memory) pixels landed in the staged atlas");
        Check(preservedOk, "preserved clip pixels landed in the staged atlas");
    }

    // ── Cost comparison: one write for all clips vs one write per clip ──
    // The old Bake All wrote the 4096² color+normal PNG once PER CLIP on the UI thread.
    string bigDir = Path.Combine(dir, "big");
    Directory.CreateDirectory(bigDir);
    SpriteAtlasBaker.EnsureDefaultAtlas(bigDir);

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    SpriteAtlasBaker.BakeClip(bigDir, "single",
        Enumerable.Range(0, 10).Select(i => Frame(64, 64, (byte)(i * 20), 90, 120)).ToList(), [], []);
    stopwatch.Stop();
    var singleWrite = stopwatch.Elapsed;

    var bigClips = Enumerable.Range(0, 28).Select(c => new SpriteAtlasRepackClipInput($"clip{c}",
        Enumerable.Range(0, 10).Select(i =>
        {
            var f = Frame(64, 64, (byte)(c * 8), (byte)(i * 20), 140);
            return new SpriteAtlasRepackFrameInput("Male Skin1", i, f.Width, f.Height, f.Metadata, f);
        }).ToList())).ToList();
    stopwatch.Restart();
    var bigStaged = await SpriteAtlasBaker.RepackAllAsync(bigDir, [], bigClips);
    stopwatch.Stop();
    var batchWrite = stopwatch.Elapsed;
    Console.WriteLine($"INFO  one atlas write (old path, per clip): {singleWrite.TotalMilliseconds:0} ms"
        + $" → 28 clips ≈ {singleWrite.TotalMilliseconds * 28:0} ms of blocked UI");
    Console.WriteLine($"INFO  batched pack of 28 clips x 10 frames: {batchWrite.TotalMilliseconds:0} ms on the worker");
    Check(bigStaged.Clips.Count == 28, "28-clip batch packed every clip");
    Check(batchWrite < singleWrite * 28, "batched pack is cheaper than one write per clip",
        $"{batchWrite.TotalMilliseconds:0} ms vs {singleWrite.TotalMilliseconds * 28:0} ms");

    Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILURE(S)");
    return fails == 0 ? 0 : 1;
}
finally
{
    try { Directory.Delete(dir, recursive: true); } catch { }
}
