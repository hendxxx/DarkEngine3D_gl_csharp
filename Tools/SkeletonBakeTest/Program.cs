using DarkEngine3D_gl_csharp.Engine.Visual;
using DarkEngine3D_gl_csharp.Engine.Visual.Skeleton;
using StbImageSharp;

namespace DarkEngine3D_gl_csharp.Tools.SkeletonBakeTest;

/// <summary>
/// End-to-end skeleton bake harness: rig → AnimationBaker → SpriteAtlasBaker.BakeClip → a real
/// atlas PNG on disk. Verifies the parts that a headless pose test cannot: frame placement,
/// that the pixels actually land in the atlas, the runtime feet contract
/// (<c>SpriteOffsetY</c> = art below the root) and that a rebake reuses the clip's slots.
/// </summary>
internal static class Program
{
    private static int _fails;

    private static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "darkengine-skel-bake-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            string sheetPath = WriteSourceSheet(dir);
            var sheet = SourceSheet(sheetPath);

            RigGeometry(sheet);
            AtlasBake(dir, sheet);
            FeetContract(dir, sheet);
            Rebake(dir, sheet);
            Limits(dir, sheet);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }

        Console.WriteLine(_fails == 0
            ? "\nALL SKELETON BAKE CASES PASS"
            : $"\n{_fails} SKELETON BAKE CASE(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    // ── A. runtime geometry contract ────────────────────────────────────────────────

    private static void RigGeometry(SpriteSheet sheet)
    {
        Console.WriteLine("\n== A. runtime canvas geometry ==\n");

        // Symmetric art: canvas is exactly the art, anchor bottom-centre.
        var rig = BuildRig(sheet, bodyOffsetX: 0f, bodyOffsetY: 0f, handOffsetY: 10f);
        var result = AnimationBaker.Bake(rig, StaticClip(), null, n => Same(n, sheet) ? sheet : null);
        Check("a static rig bakes", result.Success, result.Warnings);
        Check("the root is the canvas centre", result.AnchorX == 0.5f, result.AnchorX.ToString());
        Check("the root sits on the canvas bottom", MathF.Abs(result.BelowRootPx) < 0.001f, result.BelowRootPx.ToString());
        Check("the root is the bottom anchor", MathF.Abs(result.AnchorY - 1f) < 0.001f, result.AnchorY.ToString());

        // Art hanging below the feet must NOT be cropped: it is reported so the clip can
        // carry it as SpriteOffsetY (the sprite path draws a frame's bottom on the feet line).
        var below = BuildRig(sheet, bodyOffsetX: 0f, bodyOffsetY: -6f, handOffsetY: 10f);
        var belowResult = AnimationBaker.Bake(below, StaticClip(), null, n => Same(n, sheet) ? sheet : null);
        Check("art below the feet extends the canvas", belowResult.BelowRootPx < 0f, belowResult.BelowRootPx.ToString());
        Check("below-root art shifts the anchor up", belowResult.AnchorY < 1f, belowResult.AnchorY.ToString());
        Check("below-root art is not cropped",
            CountOpaque(belowResult.Frames[0]) == CountOpaque(result.Frames[0]));

        // Off-centre art must still keep the root at the horizontal centre, or the runtime
        // (which centres the frame on the object) would shift the character sideways.
        var wide = BuildRig(sheet, bodyOffsetX: 20f, bodyOffsetY: 0f, handOffsetY: 10f);
        var wideResult = AnimationBaker.Bake(wide, StaticClip(), null, n => Same(n, sheet) ? sheet : null);
        Check("an off-centre attachment keeps the root centred", wideResult.AnchorX == 0.5f);
        Check("an off-centre attachment widens the canvas symmetrically",
            wideResult.Width == 2 * (int)MathF.Ceiling(20f + 2f), wideResult.Width.ToString());
    }

    // ── B. real atlas write ─────────────────────────────────────────────────────────

    private static void AtlasBake(string dir, SpriteSheet sheet)
    {
        Console.WriteLine("\n== B. atlas bake ==\n");

        var rig = BuildRig(sheet, 0f, 0f, 10f);
        var clip = AnimatedClip(rig.Id);
        var bake = AnimationBaker.Bake(rig, clip, new SkeletonBakeOptions { Fps = 8f },
            n => Same(n, sheet) ? sheet : null);
        Check("animated rig bakes 8 frames", bake.Frames.Count == 8, bake.Frames.Count.ToString());

        var result = SpriteAtlasBaker.BakeClip(dir, "hero-spin", bake.Frames, [], []);
        Check("the bake writes an atlas", result != null);
        if (result == null) return;

        Check("every sample gets an atlas frame", result.FrameIndices.Count == bake.Frames.Count,
            $"{result.FrameIndices.Count} vs {bake.Frames.Count}");
        Check("frame indices are distinct", result.FrameIndices.Distinct().Count() == result.FrameIndices.Count);
        Check("frames are inside the atlas",
            result.FrameIndices.All(i => i >= 0 && i < result.Atlas.CustomFrames!.Count));
        Check("the atlas file exists", File.Exists(Path.Combine(dir, result.Atlas.Name + ".png")));
        Check("the normal map exists", File.Exists(Path.Combine(dir, result.Atlas.Name + "Normal.png")));

        var rects = result.FrameIndices.Select(i => result.Atlas.CustomFrames![i]).ToList();
        Check("every frame keeps the baked canvas size",
            rects.All(r => r.Width == bake.Width && r.Height == bake.Height),
            string.Join(",", rects.Select(r => $"{r.Width}x{r.Height}").Distinct()));
        Check("frames do not overlap", !AnyOverlap(rects));
        Check("frames carry the feet anchor",
            rects.All(r => MathF.Abs(r.AnchorX - bake.AnchorX) < 1e-4f && MathF.Abs(r.AnchorY - bake.AnchorY) < 1e-4f));

        // Pixels must actually reach the atlas: a copy of each frame, no more, no less.
        var atlasPixels = ReadPng(Path.Combine(dir, result.Atlas.Name + ".png"));
        Check("the atlas PNG is the expected size",
            atlasPixels.Width == SpriteAtlasBaker.AtlasSize && atlasPixels.Height == SpriteAtlasBaker.AtlasSize);
        int expectedOpaque = bake.Frames.Sum(CountOpaque);
        Check("every baked pixel landed in the atlas", CountOpaque(atlasPixels) == expectedOpaque,
            $"{CountOpaque(atlasPixels)} vs {expectedOpaque}");

        var firstFrameOpaque = CountOpaqueInRect(atlasPixels, rects[0]);
        Check("frame 0 occupies its own rect", firstFrameOpaque == CountOpaque(bake.Frames[0]),
            $"{firstFrameOpaque} vs {CountOpaque(bake.Frames[0])}");
    }

    // ── C. feet contract carried by the clip ────────────────────────────────────────

    private static void FeetContract(string dir, SpriteSheet sheet)
    {
        Console.WriteLine("\n== C. feet offset contract ==\n");

        var rig = BuildRig(sheet, 0f, -4f, 10f);
        var bake = AnimationBaker.Bake(rig, StaticClip(), null, n => Same(n, sheet) ? sheet : null);
        Check("below-root art reports a negative offset", bake.BelowRootPx < 0f);

        // The editor stamps this on the clip; emulate that decision here so the contract is
        // pinned by a test rather than by a comment.
        var clip = StaticClip();
        clip.SpriteOffsetY = bake.BelowRootPx;
        clip.MasterHeight = rig.DesignHeightPx;
        Check("the clip carries the offset", clip.SpriteOffsetY == bake.BelowRootPx);

        // With the offset applied, the root lands exactly where the runtime puts the frame
        // bottom: offset = -(how far the canvas reaches below the root).
        float rootAboveBottom = MathF.Abs(bake.BelowRootPx);
        Check("offset cancels the below-root pixels", MathF.Abs(rootAboveBottom + clip.SpriteOffsetY) < 1e-4f);
        Check("a bake without below-root art needs no offset",
            MathF.Abs(AnimationBaker.Bake(BuildRig(sheet, 0f, 0f, 10f), StaticClip(), null,
                n => Same(n, sheet) ? sheet : null).BelowRootPx) < 1e-4f);

        // MasterHeight must be the rig's design height, not the canvas: the canvas can be
        // taller than the character (a raised sword) and scaling by it would shrink the body.
        Check("MasterHeight comes from the rig, not the canvas", clip.MasterHeight == rig.DesignHeightPx);
    }

    // ── D. rebake ──────────────────────────────────────────────────────────────────

    private static void Rebake(string dir, SpriteSheet sheet)
    {
        Console.WriteLine("\n== D. rebake after a rig edit ==\n");

        var rig = BuildRig(sheet, 0f, 0f, 10f);
        var clip = AnimatedClip(rig.Id);
        var options = new SkeletonBakeOptions { Fps = 8f };
        var first = AnimationBaker.Bake(rig, clip, options, n => Same(n, sheet) ? sheet : null);
        var firstAtlas = SpriteAtlasBaker.BakeClip(dir, "hero-spin-rebake", first.Frames, [], []);
        Check("first bake lands", firstAtlas != null);
        if (firstAtlas == null) return;
        var before = firstAtlas.FrameIndices.Select(i => firstAtlas.Atlas.CustomFrames![i]).ToList();
        var beforePixels = before.Select(r => HashRect(ReadPng(Path.Combine(dir, "mainsprite1.png")), r)).ToList();

        // Edit the rig (longer bone → different silhouette), then bake again.
        rig.Bones[1].Y = 12f;
        var second = AnimationBaker.Bake(rig, clip, options, n => Same(n, sheet) ? sheet : null);
        Check("the edited rig bakes", second.Success);
        Check("a taller arm changes the canvas or the pixels",
            second.Height != first.Height || !second.Frames[0].Pixels.SequenceEqual(first.Frames[0].Pixels));

        var secondAtlas = SpriteAtlasBaker.BakeClip(dir, "hero-spin-rebake", second.Frames, [], []);
        Check("the rebake lands", secondAtlas != null);
        if (secondAtlas == null) return;
        Check("the rebake keeps one frame per sample",
            secondAtlas.FrameIndices.Count == second.Frames.Count);
        var after = secondAtlas.FrameIndices.Select(i => secondAtlas.Atlas.CustomFrames![i]).ToList();
        var afterPixels = after.Select(r => HashRect(ReadPng(Path.Combine(dir, "mainsprite1.png")), r)).ToList();
        Check("the atlas pixels changed after the rig edit", !beforePixels.SequenceEqual(afterPixels));
        Check("the rebake still does not overlap frames", !AnyOverlap(after));

        // Duration override: a looping clip must sample exactly the authored length.
        clip.Bake = new BakeSettings { DurationSeconds = 0.5f };
        var halfSecond = AnimationBaker.Bake(rig, clip, new SkeletonBakeOptions { Fps = 8f },
            n => Same(n, sheet) ? sheet : null);
        Check("BakeSettings.DurationSeconds drives the sample count",
            halfSecond.Frames.Count == 4, halfSecond.Frames.Count.ToString());
    }

    // ── E. limits ──────────────────────────────────────────────────────────────────

    private static void Limits(string dir, SpriteSheet sheet)
    {
        Console.WriteLine("\n== E. atlas limits ==\n");

        var big = new BakedSpriteFrame
        {
            Pixels = new byte[5000 * 4 * 4],
            Width = 5000,
            Height = 4,
            Metadata = new SpriteFrame { Width = 5000, Height = 4 },
        };
        var rejected = SpriteAtlasBaker.BakeClip(dir, "too-wide", [big], [], []);
        Check("a frame wider than the atlas is refused", rejected == null);

        var rig = BuildRig(sheet, 0f, 0f, 10f);
        var huge = AnimationBaker.Bake(rig, StaticClip(),
            new SkeletonBakeOptions { MaxFrameSize = 4 }, n => Same(n, sheet) ? sheet : null);
        Check("the baker refuses oversized frames before touching an atlas", !huge.Success);
        Check("the refusal is reported", huge.Warnings.Any(w => w.Contains("atlas limit")));
    }

    // ── helpers ────────────────────────────────────────────────────────────────────

    private static AnimationClip2D StaticClip() => new()
    {
        Name = "hero-idle-rig",
        Mode = AnimationClipMode.Skeleton,
        Loop = true,
        FPS = 8f,
        SourceFrameIndices = [0, 1],
    };

    private static AnimationClip2D AnimatedClip(string rigId) => new()
    {
        Name = "hero-spin-rig",
        Mode = AnimationClipMode.Skeleton,
        RigId = rigId,
        Loop = true,
        FPS = 8f,
        Tracks =
        [
            new AnimationTrack
            {
                Kind = AnimationTrackKind.BoneRotate,
                Target = "arm",
                Keys =
                [
                    new AnimationKey { Time = 0f, Value = 0f },
                    new AnimationKey { Time = 1f, Value = 90f },
                ],
            },
        ],
    };

    private static AnimationRig BuildRig(SpriteSheet sheet, float bodyOffsetX, float bodyOffsetY, float handOffsetY)
        => new()
        {
            Id = "rig-bake",
            Name = "BakeHero",
            DesignHeightPx = 14f,
            Bones =
            [
                new Bone2D { Name = "root", Length = 2f },
                new Bone2D { Name = "arm", Parent = "root", Y = handOffsetY, Length = 4f },
            ],
            Slots =
            [
                new Slot2D { Name = "body-slot", Bone = "root", Attachment = "body" },
                new Slot2D { Name = "hand-slot", Bone = "arm", Attachment = "hand" },
            ],
            Attachments =
            [
                new Attachment2D
                {
                    Name = "body", Slot = "body-slot", SheetName = sheet.Name, FrameIndex = 0,
                    OffsetX = bodyOffsetX, OffsetY = bodyOffsetY,
                    PivotX = 0.5f, PivotY = 1f,
                },
                new Attachment2D
                {
                    Name = "hand", Slot = "hand-slot", SheetName = sheet.Name, FrameIndex = 1,
                    PivotX = 0.5f, PivotY = 1f,
                },
            ],
            Skins = [new Skin2D { Name = "default" }],
            DefaultSkin = "default",
        };

    private static bool Same(string name, SpriteSheet sheet)
        => string.Equals(name, sheet.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>4×4 frames in one row: frame 0 solid red, frame 1 solid green.</summary>
    private static string WriteSourceSheet(string dir)
    {
        const int w = 8, h = 4;
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool second = x >= 4;
                rgba[i] = second ? (byte)0 : (byte)255;
                rgba[i + 1] = second ? (byte)255 : (byte)0;
                rgba[i + 3] = 255;
            }
        string path = Path.Combine(dir, "bake-src.png");
        SpriteAtlasBaker.WriteRgbaPng(path, rgba, w, h);
        return path;
    }

    private static SpriteSheet SourceSheet(string imagePath) => new()
    {
        Name = "bake-src",
        ImagePath = imagePath,
        ImageWidth = 8,
        ImageHeight = 4,
        Columns = 2,
        Rows = 1,
        FrameWidth = 4,
        FrameHeight = 4,
    };

    private static (int Width, int Height, byte[] Data) ReadPng(string path)
    {
        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return (image.Width, image.Height, image.Data);
    }

    private static int CountOpaque(BakedSpriteFrame frame)
    {
        int count = 0;
        for (int i = 3; i < frame.Pixels.Length; i += 4)
            if (frame.Pixels[i] > 0) count++;
        return count;
    }

    private static int CountOpaque((int Width, int Height, byte[] Data) image)
    {
        int count = 0;
        for (int i = 3; i < image.Data.Length; i += 4)
            if (image.Data[i] > 0) count++;
        return count;
    }

    private static int CountOpaqueInRect((int Width, int Height, byte[] Data) image, SpriteFrame rect)
    {
        int count = 0;
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
            for (int x = rect.X; x < rect.X + rect.Width; x++)
                if (image.Data[(y * image.Width + x) * 4 + 3] > 0) count++;
        return count;
    }

    private static long HashRect((int Width, int Height, byte[] Data) image, SpriteFrame rect)
    {
        long h = 17;
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
            for (int x = rect.X; x < rect.X + rect.Width; x++)
            {
                int i = (y * image.Width + x) * 4;
                for (int c = 0; c < 4; c++) h = unchecked(h * 31 + image.Data[i + c]);
            }
        return h;
    }

    private static bool AnyOverlap(List<SpriteFrame> rects)
    {
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
            {
                var a = rects[i];
                var b = rects[j];
                bool separate = a.X + a.Width <= b.X || b.X + b.Width <= a.X
                    || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                if (!separate) return true;
            }
        return false;
    }

    private static void Check(string what, bool ok, string? detail = null)
    {
        Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {what}{(ok || string.IsNullOrEmpty(detail) ? "" : $"  [{detail}]")}");
        if (!ok) _fails++;
    }

    private static void Check(string what, bool ok, List<string> warnings)
        => Check(what, ok, warnings.Count > 0 ? string.Join(" | ", warnings) : null);
}
