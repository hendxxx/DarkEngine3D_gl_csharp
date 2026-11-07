using DarkEngine3D_gl_csharp.Engine.Visual;
using DarkEngine3D_gl_csharp.Engine.Visual.Skeleton;

namespace DarkEngine3D_gl_csharp.Tools.AnimationEvalTest;

/// <summary>
/// Headless harness for the Phase 2 skeleton pipeline: transform math, curve evaluation,
/// pose evaluation (hierarchy, tracks, skins, draw order), the CPU rasterizer and the baker.
/// Every case is deterministic — a real PNG sheet is generated in a temp folder, so no
/// project art is needed.
/// </summary>
internal static class Program
{
    private static int _fails;
    private static string _sheetPath = "";

    private static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "darkengine-anim-eval-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            _sheetPath = WriteTestSheet(dir);
            var sheet = TestSheet();

            Transforms();
            Curves();
            PoseBasics(sheet);
            PoseTracks(sheet);
            PoseSkinsAndSlots(sheet);
            FrameRect(sheet);
            Rasterizer(sheet);
            Bake(sheet);
            BakeLimits(sheet);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp folder */ }
        }

        Console.WriteLine(_fails == 0
            ? "\nALL ANIMATION EVAL CASES PASS"
            : $"\n{_fails} ANIMATION EVAL CASE(S) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    // ── A. transform math ───────────────────────────────────────────────────────────

    private static void Transforms()
    {
        Console.WriteLine("\n== A. 2D transform math ==\n");

        var identity = Transform2D.Identity;
        var p = identity.TransformPoint(3f, 4f);
        Check("identity keeps the point", Near(p.X, 3f) && Near(p.Y, 4f));

        var rot = Transform2D.RotationDegrees(90f);
        var rp = rot.TransformPoint(1f, 0f);
        Check("+90° turns +X into +Y (y-up)", Near(rp.X, 0f) && Near(rp.Y, 1f));

        var parent = Transform2D.Translation(0f, 10f);
        var child = Transform2D.Translation(0f, 5f);
        var composed = (parent * child).TransformPoint(0f, 0f);
        Check("child translation stacks on the parent", Near(composed.X, 0f) && Near(composed.Y, 15f));

        var composed2 = (Transform2D.RotationDegrees(90f) * Transform2D.Translation(1f, 0f))
            .TransformPoint(0f, 0f);
        Check("parent rotation rotates the child's offset", Near(composed2.X, 0f) && Near(composed2.Y, 1f));

        var trss = Transform2D.Trss(0f, 0f, 0f, 2f, 3f, 0f, 0f);
        var sp = trss.TransformPoint(1f, 1f);
        Check("scale multiplies per axis", Near(sp.X, 2f) && Near(sp.Y, 3f));

        var world = Transform2D.Trss(5f, -2f, 33f, 1.4f, 0.7f, 0f, 0f);
        Check("inverse exists", world.TryInvert(out var inv));
        var back = inv.TransformPoint(world.TransformPoint(2.5f, -1.25f).X,
            world.TransformPoint(2.5f, -1.25f).Y);
        Check("world → local round-trips", Near(back.X, 2.5f) && Near(back.Y, -1.25f));

        Check("degenerate transform has no inverse",
            !Transform2D.Scale(0f, 0f).TryInvert(out _));
    }

    // ── B. curves ───────────────────────────────────────────────────────────────────

    private static void Curves()
    {
        Console.WriteLine("\n== B. curve evaluation (linear / stepped / bezier) ==\n");

        var linear = new AnimationTrack
        {
            Kind = AnimationTrackKind.BoneRotate,
            Target = "arm",
            Keys = [new AnimationKey { Time = 0f, Value = 0f }, new AnimationKey { Time = 1f, Value = 10f }],
        };
        Check("linear midpoint", Near(AnimationCurve.Evaluate(linear, 0.5f), 5f));
        Check("clamps before the first key", Near(AnimationCurve.Evaluate(linear, -3f), 0f));
        Check("clamps after the last key", Near(AnimationCurve.Evaluate(linear, 9f), 10f));

        var stepped = new AnimationTrack
        {
            Keys =
            [
                new AnimationKey { Time = 0f, Value = 0f, Interp = AnimationKeyInterp.Stepped },
                new AnimationKey { Time = 1f, Value = 10f },
            ],
        };
        Check("stepped holds until the next key", Near(AnimationCurve.Evaluate(stepped, 0.99f), 0f));

        var bezier = new AnimationTrack
        {
            Keys =
            [
                new AnimationKey { Time = 0f, Value = 0f, Interp = AnimationKeyInterp.Bezier },
                new AnimationKey { Time = 1f, Value = 10f },
            ],
        };
        Check("bezier hits its endpoints",
            Near(AnimationCurve.Evaluate(bezier, 0f), 0f) && Near(AnimationCurve.Evaluate(bezier, 1f), 10f));
        Check("default bezier handles are symmetric (mid = 0.5)",
            Near(AnimationCurve.Evaluate(bezier, 0.5f), 5f, 0.02f));
        bool monotonic = true;
        float prev = -1f;
        for (int i = 0; i <= 20; i++)
        {
            float v = AnimationCurve.Evaluate(bezier, i / 20f);
            if (v < prev - 1e-4f) monotonic = false;
            prev = v;
        }
        Check("bezier never overshoots backwards", monotonic);
        Check("ease with the diagonal handles is linear",
            Near(AnimationCurve.BezierEase(0.25f, 1f, 0f), 0.25f, 1e-3f));
        Check("empty track evaluates to zero", Near(AnimationCurve.Evaluate(new AnimationTrack(), 1f), 0f));
    }

    // ── C. pose: hierarchy + draw order ─────────────────────────────────────────────

    private static void PoseBasics(SpriteSheet sheet)
    {
        Console.WriteLine("\n== C. pose hierarchy and draw order ==\n");

        var rig = BuildRig(sheet);
        var pose = SkeletonPoseEvaluator.Evaluate(rig, null, 0f);

        Check("both slots resolve", pose.Slots.Count == 2);
        Check("draw order follows the rig slot list",
            pose.Slots.Count == 2 && pose.Slots[0].SlotName == "body-slot" && pose.Slots[1].SlotName == "hand-slot");
        Check("root bone sits at the feet", Near(pose.Bones["root"].TransformPoint(0f, 0f).Y, 0f));
        var arm = pose.Bones["arm"].TransformPoint(0f, 0f);
        Check("child bone inherits the parent offset", Near(arm.X, 0f) && Near(arm.Y, 10f));

        var again = SkeletonPoseEvaluator.Evaluate(rig, null, 0f);
        Check("evaluation is deterministic", SamePose(pose, again));

        var hidden = BuildRig(sheet);
        hidden.Slots[1].Visible = false;
        Check("an invisible slot is not drawn",
            SkeletonPoseEvaluator.Evaluate(hidden, null, 0f).Slots.Count == 1);

        var noAttachment = BuildRig(sheet);
        noAttachment.Slots[0].Attachment = "";
        Check("a slot without an attachment is skipped",
            SkeletonPoseEvaluator.Evaluate(noAttachment, null, 0f).Slots.Count == 1);

        var orphan = BuildRig(sheet);
        orphan.Slots[0].Bone = "does-not-exist";
        var orphanPose = SkeletonPoseEvaluator.Evaluate(orphan, null, 0f);
        Check("a slot on an unknown bone still draws at the rig origin",
            orphanPose.Slots.Count == 2 && Near(orphanPose.Slots[0].World.TransformPoint(0f, 0f).X, 0f));

        var cyclic = BuildRig(sheet);
        cyclic.Bones[0].Parent = "arm"; // root ⇄ arm loop: bad data must not hang
        Check("a bone cycle terminates", SkeletonPoseEvaluator.Evaluate(cyclic, null, 0f).Slots.Count == 2);
    }

    // ── D. pose: tracks ─────────────────────────────────────────────────────────────

    private static void PoseTracks(SpriteSheet sheet)
    {
        Console.WriteLine("\n== D. track application ==\n");

        var rig = BuildRig(sheet);
        var rotate = new AnimationTrack
        {
            Kind = AnimationTrackKind.BoneRotate,
            Target = "arm",
            Keys = [new AnimationKey { Time = 0f, Value = 0f }, new AnimationKey { Time = 1f, Value = 90f }],
        };
        var pose = SkeletonPoseEvaluator.Evaluate(rig, [rotate], 1f);
        var tip = pose.Bones["arm"].TransformPoint(0f, 1f);
        Check("bone rotation adds to the setup pose", Near(tip.X, -1f) && Near(tip.Y, 10f, 1e-3f));

        var translate = new AnimationTrack
        {
            Kind = AnimationTrackKind.BoneTranslate,
            Target = "arm",
            Keys = [new AnimationKey { Time = 0f, Value = 0f, Value2 = 0f }, new AnimationKey { Time = 1f, Value = 5f, Value2 = -2f }],
        };
        var moved = SkeletonPoseEvaluator.Evaluate(rig, [translate], 0.5f).Bones["arm"].TransformPoint(0f, 0f);
        Check("bone translation moves X and Y", Near(moved.X, 2.5f) && Near(moved.Y, 9f));

        var scale = new AnimationTrack
        {
            Kind = AnimationTrackKind.BoneScale,
            Target = "root",
            Keys = [new AnimationKey { Time = 0f, Value = 2f, Value2 = 2f }],
        };
        var scaled = SkeletonPoseEvaluator.Evaluate(rig, [scale], 0f).Bones["arm"].TransformPoint(0f, 0f);
        Check("bone scale multiplies the setup scale", Near(scaled.Y, 20f));

        var alpha = new AnimationTrack
        {
            Kind = AnimationTrackKind.SlotColor,
            Target = "hand-slot",
            Keys = [new AnimationKey { Time = 0f, Value = 0.25f }],
        };
        var faded = SkeletonPoseEvaluator.Evaluate(rig, [alpha], 0f);
        Check("slot alpha track multiplies the slot alpha",
            Near(faded.Slots[1].A, 0.25f) && Near(faded.Slots[0].A, 1f));

        var hide = new AnimationTrack
        {
            Kind = AnimationTrackKind.SlotAttachment,
            Target = "hand-slot",
            Keys = [new AnimationKey { Time = 0f, Value = -1f }],
        };
        Check("attachment index -1 hides the slot",
            SkeletonPoseEvaluator.Evaluate(rig, [hide], 0f).Slots.Count == 1);

        var stale = new AnimationTrack
        {
            Kind = AnimationTrackKind.SlotAttachment,
            Target = "hand-slot",
            Keys = [new AnimationKey { Time = 0f, Value = 7f }],
        };
        Check("an out-of-range attachment index is ignored, not blanked",
            SkeletonPoseEvaluator.Evaluate(rig, [stale], 0f).Slots.Count == 2);
    }

    // ── E. pose: skins + per-slot attachment lists ──────────────────────────────────

    private static void PoseSkinsAndSlots(SpriteSheet sheet)
    {
        Console.WriteLine("\n== E. skins and per-slot attachments ==\n");

        var rig = BuildRig(sheet);
        rig.Attachments.Add(new Attachment2D
        {
            Name = "alt",
            Slot = "body-slot",
            SheetName = sheet.Name,
            FrameIndex = 1,
            PivotX = 0.5f,
            PivotY = 1f,
        });
        rig.Skins.Add(new Skin2D { Name = "alt-skin", AttachmentBySlot = { ["body-slot"] = "alt" } });

        var skinPose = SkeletonPoseEvaluator.Evaluate(rig, null, 0f, "alt-skin");
        Check("a skin overrides the slot attachment", skinPose.Slots[0].AttachmentName == "alt");

        var defaultPose = SkeletonPoseEvaluator.Evaluate(rig, null, 0f);
        Check("the setup attachment wins without a skin", defaultPose.Slots[0].AttachmentName == "body");

        // Per-slot attachment lists are name-sorted: ["alt", "body"] → index 0 = alt.
        var indexTrack = new AnimationTrack
        {
            Kind = AnimationTrackKind.SlotAttachment,
            Target = "body-slot",
            Keys = [new AnimationKey { Time = 0f, Value = 0f }],
        };
        Check("attachment index follows the sorted per-slot list",
            SkeletonPoseEvaluator.Evaluate(rig, [indexTrack], 0f).Slots[0].AttachmentName == "alt");

        var unknownSkin = SkeletonPoseEvaluator.Evaluate(rig, null, 0f, "nope");
        Check("an unknown skin falls back to the rig default", unknownSkin.Slots.Count == 2);
    }

    // ── F. frame rect math ──────────────────────────────────────────────────────────

    private static void FrameRect(SpriteSheet sheet)
    {
        Console.WriteLine("\n== F. source frame rects ==\n");

        Check("frame 0 rect", SpriteRasterizer.TryGetFrameRect(sheet, 0, out int x0, out int y0, out int w0, out int h0)
            && x0 == 0 && y0 == 0 && w0 == 4 && h0 == 4);
        Check("frame 1 rect follows the grid",
            SpriteRasterizer.TryGetFrameRect(sheet, 1, out int x1, out _, out _, out _) && x1 == 4);
        Check("an out-of-range index still maps onto the grid",
            SpriteRasterizer.TryGetFrameRect(sheet, 5, out int x5, out _, out _, out _) && x5 >= 0);
        Check("a negative index is rejected", !SpriteRasterizer.TryGetFrameRect(sheet, -1, out _, out _, out _, out _));
    }

    // ── G. rasterizer ───────────────────────────────────────────────────────────────

    private static void Rasterizer(SpriteSheet sheet)
    {
        Console.WriteLine("\n== G. CPU rasterizer ==\n");

        var rig = BuildRig(sheet);
        var result = AnimationBaker.Bake(rig, StaticClip(), null, name => Lookup(name, sheet));
        Check("a static rig bakes", result.Success, result.Warnings);
        Check("the canvas covers both regions plus the root",
            result.Width == 4 && result.Height == 14, $"{result.Width}x{result.Height}");
        Check("the anchor puts the root at bottom-center",
            Near(result.AnchorX, 0.5f) && Near(result.AnchorY, 1f));

        var frame = result.Frames[0];
        Check("every slot pixel landed", CountOpaque(frame) == 32, $"{CountOpaque(frame)} opaque px");

        var body = PixelAt(frame, 2, 12);   // world (0, 2): body region
        var hand = PixelAt(frame, 2, 2);    // world (0, 12): hand region
        Check("body slot draws its own colour", body == (255, 0, 0, 255), body.ToString());
        Check("hand slot draws its own colour", hand == (0, 255, 0, 255), hand.ToString());

        var empty = PixelAt(frame, 0, 7);   // world y 6.5: between body and hand
        Check("untouched pixels stay transparent", empty == (0, 0, 0, 0), empty.ToString());

        // Tint: half alpha red over nothing → alpha 128, colour preserved.
        var tinted = BuildRig(sheet);
        tinted.Slots[0].A = 0.5f;
        var tintFrame = AnimationBaker.Bake(tinted, StaticClip(), null, name => Lookup(name, sheet)).Frames[0];
        var blended = PixelAt(tintFrame, 2, 12);
        Check("slot alpha scales the baked alpha", blended.A is >= 127 and <= 128, blended.ToString());

        // A second slot over the first blends source-over.
        var stacked = BuildRig(sheet);
        stacked.Slots[1].Bone = "root";
        stacked.Slots[1].Attachment = "body";
        stacked.Slots[0].A = 0.5f;
        var stackFrame = AnimationBaker.Bake(stacked, StaticClip(), null, name => Lookup(name, sheet)).Frames[0];
        var stackedPixel = PixelAt(stackFrame, 2, 2);
        Check("a later slot composites over an earlier one", stackedPixel.A == 255, stackedPixel.ToString());

        // Big scale still lands inside the canvas (bounds grow with the pose).
        var big = BuildRig(sheet);
        big.Attachments[0].ScaleX = 3f;
        big.Attachments[0].ScaleY = 3f;
        var bigResult = AnimationBaker.Bake(big, StaticClip(), null, name => Lookup(name, sheet));
        Check("scaled attachments widen the canvas", bigResult.Width > 4, $"{bigResult.Width}x{bigResult.Height}");
        Check("scaled bake keeps the root anchored", Near(bigResult.AnchorX, 0.5f));
    }

    // ── H. bake: timing, loop seam, warnings ────────────────────────────────────────

    private static void Bake(SpriteSheet sheet)
    {
        Console.WriteLine("\n== H. bake timing, loop seam and warnings ==\n");

        var rig = BuildRig(sheet);
        var spin = new AnimationTrack
        {
            Kind = AnimationTrackKind.BoneRotate,
            Target = "arm",
            Keys = [new AnimationKey { Time = 0f, Value = 0f }, new AnimationKey { Time = 1f, Value = 90f }],
        };
        var clip = new AnimationClip2D
        {
            Name = "hero-spin",
            Mode = AnimationClipMode.Skeleton,
            RigId = rig.Id,
            Loop = true,
            FPS = 8f,
            Tracks = [spin],
        };

        Check("duration comes from the last key", Near(AnimationBaker.ResolveDuration(clip), 1f));

        var looped = AnimationBaker.Bake(rig, clip, null, name => Lookup(name, sheet));
        Check("a looping clip drops the duplicate end frame", looped.Frames.Count == 8, $"{looped.Frames.Count} frames");
        Check("the loop seam moves", !looped.Frames[0].Pixels.SequenceEqual(looped.Frames[^1].Pixels));
        Check("every frame shares the canvas",
            looped.Frames.All(f => f.Width == looped.Width && f.Height == looped.Height));
        Check("every frame shares the anchor",
            looped.Frames.All(f => Near(f.Metadata.AnchorX, looped.AnchorX) && Near(f.Metadata.AnchorY, looped.AnchorY)));
        Check("a rotated arm widens the union canvas", looped.Width > 4, $"{looped.Width}x{looped.Height}");

        var oneShot = new AnimationClip2D
        {
            Name = "hero-spin-once",
            Mode = AnimationClipMode.Skeleton,
            Loop = false,
            FPS = 8f,
            Tracks = [spin],
        };
        var once = AnimationBaker.Bake(rig, oneShot, null, name => Lookup(name, sheet));
        Check("a one-shot clip keeps its final pose", once.Frames.Count == 9, $"{once.Frames.Count} frames");

        var fpsOverride = AnimationBaker.Bake(rig, clip, new SkeletonBakeOptions { Fps = 16f }, name => Lookup(name, sheet));
        Check("the bake fps override wins", fpsOverride.Frames.Count == 16, $"{fpsOverride.Frames.Count} frames");

        var bakeFps = new AnimationClip2D
        {
            Name = "hero-bakefps",
            Loop = true,
            FPS = 8f,
            Tracks = [spin],
            Bake = new BakeSettings { Fps = 4f },
        };
        Check("BakeSettings.Fps is used when no override is given",
            AnimationBaker.Bake(rig, bakeFps, null, name => Lookup(name, sheet)).Frames.Count == 4);

        var missingSheet = BuildRig(sheet);
        missingSheet.Attachments[1].SheetName = "no-such-sheet";
        var missing = AnimationBaker.Bake(missingSheet, StaticClip(), null, name => Lookup(name, sheet));
        Check("a missing attachment sheet still bakes the rest", missing.Success);
        Check("a missing sheet is reported", missing.Warnings.Any(w => w.Contains("no-such-sheet")));

        var emptyRig = new AnimationRig { Id = "empty", Name = "empty" };
        var empty = AnimationBaker.Bake(emptyRig, StaticClip(), null, name => Lookup(name, sheet));
        Check("an empty rig fails cleanly", !empty.Success && empty.Warnings.Count > 0);

        var noTracksClip = new AnimationClip2D
        {
            Name = "strip-only",
            FPS = 10f,
            SourceFrameIndices = [0, 1, 2],
            Loop = true,
        };
        Check("a keyless clip falls back to its strip length",
            Near(AnimationBaker.ResolveDuration(noTracksClip), 0.3f));
    }

    // ── I. bake limits ──────────────────────────────────────────────────────────────

    private static void BakeLimits(SpriteSheet sheet)
    {
        Console.WriteLine("\n== I. bake limits ==\n");

        var rig = BuildRig(sheet);
        var blocked = AnimationBaker.Bake(rig, StaticClip(),
            new SkeletonBakeOptions { MaxFrameSize = 8 }, name => Lookup(name, sheet));
        Check("an oversized frame is rejected", !blocked.Success);
        Check("the rejection explains itself", blocked.Warnings.Any(w => w.Contains("atlas limit")));

        var huge = BuildRig(sheet);
        huge.Attachments[0].ScaleX = 4000f;
        huge.Attachments[0].ScaleY = 4000f;
        var hugeResult = AnimationBaker.Bake(huge, StaticClip(), null, name => Lookup(name, sheet));
        Check("a frame wider than the atlas is rejected", !hugeResult.Success);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────

    private static SpriteSheet? Lookup(string name, SpriteSheet sheet)
        => string.Equals(name, sheet.Name, StringComparison.OrdinalIgnoreCase) ? sheet : null;

    private static AnimationClip2D StaticClip() => new()
    {
        Name = "hero-idle",
        Mode = AnimationClipMode.Skeleton,
        Loop = true,
        FPS = 8f,
        SourceFrameIndices = [0, 1],
    };

    private static AnimationRig BuildRig(SpriteSheet sheet)
    {
        return new AnimationRig
        {
            Id = "rig-hero",
            Name = "Hero",
            DesignHeightPx = 14f,
            Bones =
            [
                new Bone2D { Name = "root", Length = 2f },
                new Bone2D { Name = "arm", Parent = "root", Y = 10f, Length = 4f },
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
                    Name = "body", Slot = "body-slot",
                    SheetName = sheet.Name, FrameIndex = 0,
                    PivotX = 0.5f, PivotY = 1f,
                },
                new Attachment2D
                {
                    Name = "hand", Slot = "hand-slot",
                    SheetName = sheet.Name, FrameIndex = 1,
                    PivotX = 0.5f, PivotY = 1f,
                },
            ],
            Skins = [new Skin2D { Name = "default" }],
            DefaultSkin = "default",
        };
    }

    /// <summary>4×4 frames in one row: frame 0 solid red, frame 1 solid green.</summary>
    private static string WriteTestSheet(string dir)
    {
        const int w = 8, h = 4;
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool second = x >= 4;
                rgba[i] = second ? (byte)0 : (byte)255;
                rgba[i + 1] = second ? (byte)255 : (byte)0;
                rgba[i + 2] = 0;
                rgba[i + 3] = 255;
            }
        }
        string path = Path.Combine(dir, "eval-src.png");
        SpriteAtlasBaker.WriteRgbaPng(path, rgba, w, h);
        return path;
    }

    private static SpriteSheet TestSheet() => new()
    {
        Name = "eval-src",
        ImagePath = _sheetPath,
        ImageWidth = 8,
        ImageHeight = 4,
        Columns = 2,
        Rows = 1,
        FrameWidth = 4,
        FrameHeight = 4,
        PaddingX = 0,
        PaddingY = 0,
        OffsetX = 0,
        OffsetY = 0,
    };

    private static int CountOpaque(BakedSpriteFrame frame)
    {
        int count = 0;
        for (int i = 3; i < frame.Pixels.Length; i += 4)
            if (frame.Pixels[i] > 0) count++;
        return count;
    }

    private static (byte R, byte G, byte B, byte A) PixelAt(BakedSpriteFrame frame, int x, int y)
    {
        int i = (y * frame.Width + x) * 4;
        return (frame.Pixels[i], frame.Pixels[i + 1], frame.Pixels[i + 2], frame.Pixels[i + 3]);
    }

    private static bool SamePose(SkeletonPose a, SkeletonPose b)
    {
        if (a.Slots.Count != b.Slots.Count) return false;
        for (int i = 0; i < a.Slots.Count; i++)
        {
            var pa = a.Slots[i].World.TransformPoint(0f, 0f);
            var pb = b.Slots[i].World.TransformPoint(0f, 0f);
            if (!Near(pa.X, pb.X) || !Near(pa.Y, pb.Y)) return false;
        }
        return true;
    }

    private static bool Near(float a, float b, float eps = 1e-4f) => MathF.Abs(a - b) <= eps;

    private static void Check(string what, bool ok, string? detail = null)
    {
        Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {what}{(ok || string.IsNullOrEmpty(detail) ? "" : $"  [{detail}]")}");
        if (!ok) _fails++;
    }

    private static void Check(string what, bool ok, List<string> warnings)
        => Check(what, ok, warnings.Count > 0 ? string.Join(" | ", warnings) : null);
}
