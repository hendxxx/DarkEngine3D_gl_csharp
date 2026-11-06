using DarkEngine3D_gl_csharp.Engine.Visual;

// ═══════════════════════════════════════════════════════════════════════
// TEMPORARY verification of SpriteAtlasBaker.CreateFullRepackPlan's row-major
// shelf layout (frames LEFT→RIGHT, animation order preserved, no interior holes).
// Exit code 1 on ANY failure.
// ═══════════════════════════════════════════════════════════════════════

int fails = 0;
void Check(bool ok, string name, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
    if (!ok) fails++;
}

SpriteAtlasRepackClipInput Clip(string name, int count, int w, int h) =>
    new(name, Enumerable.Range(0, count).Select(i =>
        new SpriteAtlasRepackFrameInput("Male Hair1", i, w, h, new SpriteFrame { Width = w, Height = h })).ToList());

var clips = new List<SpriteAtlasRepackClipInput>
{
    Clip("Male-Hair1-idle", 7, 64, 54),
    Clip("Male-Hair1-walk", 8, 64, 54),
    Clip("Male-Hair1-run", 9, 64, 54),
    Clip("Male-Hair1-attack", 6, 64, 54),
    Clip("Fire", 5, 128, 96)
};

var plan = SpriteAtlasBaker.CreateFullRepackPlan(clips);
int totalFrames = clips.Sum(c => c.Frames.Count);
int placed = plan.Clips.Sum(c => c.FramePlacements.Count);

Check(placed == totalFrames, "every frame placed", $"{placed}/{totalFrames}");
Check(plan.Clips.All(c => c.AtlasNumber == 1), "all clips on one page", $"atlas count {plan.AtlasCount}");

// No overlapping rects (gutter-padded) and everything inside the page.
const int pad = 1; // SpriteAtlasBaker.Gutter (private)
var boxes = plan.Clips.SelectMany(c => c.FramePlacements)
    .Select(f => (X: f.X - pad, Y: f.Y - pad, W: f.Width + pad * 2, H: f.Height + pad * 2)).ToList();
Check(boxes.All(b => b.X >= 0 && b.Y >= 0 && b.X + b.W <= SpriteAtlasBaker.AtlasSize
    && b.Y + b.H <= SpriteAtlasBaker.AtlasSize), "all frames inside the atlas");
int overlaps = 0;
for (int i = 0; i < boxes.Count; i++)
    for (int j = i + 1; j < boxes.Count; j++)
    {
        var a = boxes[i]; var b = boxes[j];
        if (a.X < b.X + b.W && a.X + a.W > b.X && a.Y < b.Y + b.H && a.Y + a.H > b.Y) overlaps++;
    }
Check(overlaps == 0, "no overlapping frames", $"{overlaps} overlap(s)");

// Every clip: frames keep animation order and run LEFT→RIGHT, wrapping to the next row only.
bool orderOk = true, rowMajorOk = true;
string orderDetail = "";
foreach (var c in plan.Clips)
{
    var f = c.FramePlacements;
    for (int i = 0; i < f.Count; i++)
    {
        if (f[i].AtlasFrameIndex != f[0].AtlasFrameIndex + i) { orderOk = false; orderDetail = $"{c.ClipName}[{i}] index {f[i].AtlasFrameIndex}"; }
        if (i == 0) continue;
        bool sameRow = f[i].Y == f[i - 1].Y && f[i].X > f[i - 1].X;
        bool nextRow = f[i].Y > f[i - 1].Y && f[i].X == 0;
        if (!sameRow && !nextRow)
        {
            rowMajorOk = false;
            orderDetail = $"{c.ClipName}[{i}] {f[i - 1].X},{f[i - 1].Y} → {f[i].X},{f[i].Y}";
        }
    }
}
Check(orderOk, "atlas frame index follows animation order (contiguous per clip)", orderDetail);
Check(rowMajorOk, "frames flow left→right, wrapping to x=0 of a lower row", orderDetail);

// Interior holes: with one uniform cell size the layout must be a filled grid — every row
// (except the last) holds the SAME number of frames as the atlas width allows.
int perRow = SpriteAtlasBaker.AtlasSize / (64 + pad * 2);
var uniformRows = plan.Clips.SelectMany(c => c.FramePlacements)
    .Where(f => f.Width == 64 && f.Height == 54)
    .GroupBy(f => f.Y).OrderBy(g => g.Key).ToList();
bool gridOk = true;
string gridDetail = "";
for (int r = 0; r < uniformRows.Count; r++)
{
    bool last = r == uniformRows.Count - 1;
    if (!last && uniformRows[r].Count() != perRow)
    {
        gridOk = false;
        gridDetail = $"row y={uniformRows[r].Key} has {uniformRows[r].Count()} of {perRow}";
    }
}
Check(gridOk, "uniform rows are completely filled (no gap in the middle)", gridDetail);
Console.WriteLine($"INFO  uniform rows: {string.Join(", ", uniformRows.Select(g => $"y={g.Key}:{g.Count()}"))}");
Console.WriteLine($"INFO  atlas count {plan.AtlasCount}, largest Y {boxes.Max(b => b.Y + b.H)}");

Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILURE(S)");
return fails == 0 ? 0 : 1;
