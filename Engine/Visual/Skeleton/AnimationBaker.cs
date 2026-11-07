namespace DarkEngine3D_gl_csharp.Engine.Visual.Skeleton;

/// <summary>Knobs for one skeleton bake.</summary>
public sealed class SkeletonBakeOptions
{
    /// <summary>Sample rate. 0 = the clip's <see cref="BakeSettings.Fps"/>, else its FPS.</summary>
    public float Fps { get; set; }

    /// <summary>Skin to composite (null/empty = the rig's default skin).</summary>
    public string? Skin { get; set; }

    /// <summary>Largest frame the atlas accepts (4096 − 2 × 1px gutter).</summary>
    public int MaxFrameSize { get; set; } = SpriteAtlasBaker.AtlasSize - 2;

    public SkeletonBakeOptions Clone() => (SkeletonBakeOptions)MemberwiseClone();
}

/// <summary>Result of a skeleton bake: ready-to-atlas frames plus the geometry every frame
/// shares. Frames are NOT written to an atlas here — that is
/// <see cref="SpriteAtlasBaker.BakeClip"/>'s job, so the baker stays testable headless.</summary>
public sealed class SkeletonBakeResult
{
    public List<BakedSpriteFrame> Frames { get; } = new();
    public List<string> Warnings { get; } = new();
    public int Width;
    public int Height;
    public float Fps;
    public float Duration;
    /// <summary>Feet/root anchor inside the frame, in <see cref="SpriteFrame"/> semantics
    /// (0 = top/left, 1 = bottom/right). Identical on every frame.</summary>
    public float AnchorX = 0.5f;
    public float AnchorY = 1f;
    public bool Success => Frames.Count > 0;
}

/// <summary>
/// Phase 2 baker: sample a rig + skeleton clip into atlas-ready frames, entirely on the CPU.
/// The runtime contract does not change — the game still loads a plain frame strip.
///
/// Geometry rule: the WHOLE clip shares ONE canvas and ONE anchor, computed from the union
/// of every sampled pose plus the rig root at (0,0). That is what keeps feet aligned across
/// frames (a per-frame bounding box would make the character bob as limbs move) and it makes
/// <see cref="SpriteFrame.RenderOffsetX"/> unnecessary for baked rigs.
/// </summary>
public static class AnimationBaker
{
    /// <summary>Authored length in seconds: the last key of any track, falling back to the
    /// source frame count at the clip's FPS for a rig clip with no keys.</summary>
    public static float ResolveDuration(AnimationClip2D clip)
    {
        float duration = 0f;
        if (clip?.Tracks != null)
        {
            foreach (var t in clip.Tracks)
            {
                if (t?.Keys == null || t.Keys.Count == 0) continue;
                float last = t.Keys[^1].Time;
                if (last > duration) duration = last;
            }
        }
        if (duration <= 0f && clip != null)
        {
            int frames = clip.SourceFrameIndices.Count > 0
                ? clip.SourceFrameIndices.Count
                : clip.FrameIndices.Count;
            float fps = clip.FPS > 0f ? clip.FPS : 12f;
            if (frames > 1) duration = frames / fps;
        }
        return MathF.Max(0f, duration);
    }

    /// <summary>Sample times for a clip: a looping clip drops the duplicate end frame so the
    /// seam does not stutter, a one-shot clip keeps its final pose.</summary>
    public static List<float> SampleTimes(AnimationClip2D clip, float fps, float duration)
    {
        var times = new List<float>();
        if (fps <= 0f) fps = 12f;
        if (duration <= 0f)
        {
            times.Add(0f);
            return times;
        }
        if (clip?.Loop == true)
        {
            int count = Math.Max(1, (int)MathF.Round(duration * fps));
            for (int i = 0; i < count; i++) times.Add(i / fps);
        }
        else
        {
            int count = Math.Max(1, (int)MathF.Round(duration * fps));
            for (int i = 0; i <= count; i++) times.Add(i / fps);
        }
        return times;
    }

    public static SkeletonBakeResult Bake(AnimationRig rig, AnimationClip2D clip,
        SkeletonBakeOptions? options, Func<string, SpriteSheet?> sheetLookup)
    {
        var result = new SkeletonBakeResult();
        if (rig == null || clip == null)
        {
            result.Warnings.Add("No rig or clip to bake.");
            return result;
        }

        var opts = options ?? new SkeletonBakeOptions();
        float fps = opts.Fps > 0f ? opts.Fps
            : clip.Bake != null && clip.Bake.Fps > 0f ? clip.Bake.Fps
            : clip.FPS > 0f ? clip.FPS : 12f;
        fps = Math.Clamp(fps, 1f, 240f);
        float duration = ResolveDuration(clip);
        result.Fps = fps;
        result.Duration = duration;

        var times = SampleTimes(clip, fps, duration);
        var rasterizer = new SpriteRasterizer(sheetLookup);
        var regions = new Dictionary<Attachment2D, RegionImage?>();
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── pass 1: bounds + cached regions ──
        var poses = new List<SkeletonPose>(times.Count);
        float left = float.MaxValue, bottom = float.MaxValue, right = float.MinValue, top = float.MinValue;
        bool anySlot = false;

        foreach (float time in times)
        {
            var pose = SkeletonPoseEvaluator.Evaluate(rig, clip.Tracks, time, opts.Skin);
            poses.Add(pose);
            foreach (var slot in pose.Slots)
            {
                var region = GetRegion(rasterizer, regions, slot.Attachment!, missing);
                if (region == null) continue;
                var (l, b, r, t) = rasterizer.MeasureSlot(slot, region);
                if (l < left) left = l;
                if (b < bottom) bottom = b;
                if (r > right) right = r;
                if (t > top) top = t;
                anySlot = true;
            }
        }

        foreach (var name in missing)
            result.Warnings.Add($"Attachment sheet '{name}' could not be read; its slots were skipped.");

        if (!anySlot)
        {
            result.Warnings.Add($"Clip '{clip.Name}' produced no drawable slots.");
            return result;
        }

        // The root (0,0) is always inside the canvas so the anchor is meaningful even when
        // every attachment sits off to one side.
        left = MathF.Min(left, 0f);
        bottom = MathF.Min(bottom, 0f);
        right = MathF.Max(right, 0f);
        top = MathF.Max(top, 0f);

        int canvasLeft = (int)MathF.Floor(left);
        int canvasBottom = (int)MathF.Floor(bottom);
        int canvasRight = (int)MathF.Ceiling(right);
        int canvasTop = (int)MathF.Ceiling(top);
        int width = Math.Max(1, canvasRight - canvasLeft);
        int height = Math.Max(1, canvasTop - canvasBottom);

        if (width > opts.MaxFrameSize || height > opts.MaxFrameSize)
        {
            result.Warnings.Add($"Baked frame {width}x{height} exceeds the {opts.MaxFrameSize}px atlas limit.");
            return result;
        }

        result.Width = width;
        result.Height = height;
        result.AnchorX = (0f - canvasLeft) / width;
        result.AnchorY = (canvasTop - 0f) / height;

        // ── pass 2: rasterize ──
        for (int i = 0; i < poses.Count; i++)
        {
            var pixels = new byte[width * height * 4];
            foreach (var slot in poses[i].Slots)
            {
                var region = GetRegion(rasterizer, regions, slot.Attachment!, missing);
                if (region == null) continue;
                rasterizer.DrawSlot(slot, region, pixels, width, height, canvasLeft, canvasTop);
            }

            var meta = new SpriteFrame
            {
                X = 0,
                Y = 0,
                Width = width,
                Height = height,
                Name = $"{clip.Name}#{i}",
                AnchorX = result.AnchorX,
                AnchorY = result.AnchorY,
            };
            result.Frames.Add(new BakedSpriteFrame
            {
                Pixels = pixels,
                Width = width,
                Height = height,
                Metadata = meta,
            });
        }

        return result;
    }

    private static RegionImage? GetRegion(SpriteRasterizer rasterizer,
        Dictionary<Attachment2D, RegionImage?> cache, Attachment2D attachment, HashSet<string> missing)
    {
        if (attachment == null) return null;
        if (!cache.TryGetValue(attachment, out var region))
        {
            region = rasterizer.GetRegion(attachment);
            cache[attachment] = region;
            if (region == null && !string.IsNullOrEmpty(attachment.SheetName)) missing.Add(attachment.SheetName);
        }
        return region;
    }
}
