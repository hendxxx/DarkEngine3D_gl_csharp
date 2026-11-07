namespace DarkEngine3D_gl_csharp.Engine.Visual.Skeleton;

/// <summary>
/// Curve evaluation for skeleton tracks. Mirrors <see cref="AnimationTrack.Evaluate"/>
/// (which stays untouched so sprite clips keep their exact timing) and adds the
/// <see cref="AnimationKeyInterp.Bezier"/> case the pose evaluator needs.
///
/// Bezier contract (Phase 2, minimal): in a key's segment the two control points are
/// <c>(prev.BezierOut, 0)</c> and <c>(next.BezierIn, 1)</c> in normalized
/// (time, value) space — i.e. the handles only shape WHEN the value moves, not overshoot.
/// The defaults (BezierOut = 1, BezierIn = 0) give a symmetric ease-in-out that passes
/// through the segment midpoint at t = 0.5. Full two-axis handles belong to the Phase 2
/// curve editor, which needs a UI to author them.
/// </summary>
public static class AnimationCurve
{
    public static float Evaluate(AnimationTrack track, float time)
    {
        if (track == null || track.Keys.Count == 0) return 0f;
        var keys = track.Keys;
        if (time <= keys[0].Time) return keys[0].Value;
        var last = keys[^1];
        if (time >= last.Time) return last.Value;

        for (int i = 1; i < keys.Count; i++)
        {
            var prev = keys[i - 1];
            var next = keys[i];
            if (time > next.Time) continue;
            float span = next.Time - prev.Time;
            if (span <= 0f) return next.Value;
            float u = (time - prev.Time) / span;
            float eased = prev.Interp switch
            {
                AnimationKeyInterp.Stepped => 0f,
                AnimationKeyInterp.Bezier => BezierEase(u, prev.BezierOut, next.BezierIn),
                _ => u,
            };
            return prev.Value + (next.Value - prev.Value) * eased;
        }
        return last.Value;
    }

    /// <summary>Normalized cubic Bezier easing. Bisection (16 steps) instead of Newton:
    /// deterministic, no division-by-zero edge cases, and the bake cost is irrelevant
    /// next to rasterization.</summary>
    public static float BezierEase(float u, float outHandle, float inHandle)
    {
        if (u <= 0f) return 0f;
        if (u >= 1f) return 1f;
        float x1 = Math.Clamp(outHandle, 0f, 1f);
        float x2 = Math.Clamp(inHandle, 0f, 1f);
        if (MathF.Abs(x1 - 1f) < 1e-5f && MathF.Abs(x2) < 1e-5f) return u; // handles == diagonal

        float lo = 0f, hi = 1f;
        for (int i = 0; i < 16; i++)
        {
            float t = (lo + hi) * 0.5f;
            if (CubicX(t, x1, x2) < u) lo = t; else hi = t;
        }
        float tt = (lo + hi) * 0.5f;
        return CubicY(tt);
    }

    private static float CubicX(float t, float x1, float x2)
    {
        float mt = 1f - t;
        return 3f * mt * mt * t * x1 + 3f * mt * t * t * x2 + t * t * t;
    }

    private static float CubicY(float t)
    {
        float mt = 1f - t;
        return 3f * mt * t * t + t * t * t;
    }
}

/// <summary>One slot resolved at a point in time: which attachment it shows and the world
/// transform that places it (the attachment's own offset/rotation/scale already applied,
/// pivot-relative corners are the rasterizer's job because only it knows the frame size).</summary>
public sealed class PoseSlot
{
    public string SlotName = "";
    public string AttachmentName = "";
    public Attachment2D? Attachment;
    public Transform2D World;
    public float R, G, B, A;
}

/// <summary>An evaluated rig at one time: world transform per bone plus the slots to draw,
/// in draw order (back to front — exactly the rig's slot order).</summary>
public sealed class SkeletonPose
{
    public Dictionary<string, Transform2D> Bones { get; } = new(StringComparer.Ordinal);
    public List<PoseSlot> Slots { get; } = new();
}

/// <summary>
/// Phase 2 pose evaluator: rig setup pose + clip tracks at a time → drawable slots.
/// Pure and deterministic (no state, no allocation of hidden caches) so a bake is
/// reproducible: the same rig + tracks + time always produce the same pixels.
///
/// Track conventions (v1):
///   • BoneRotate    — <c>Value</c> is DEGREES ADDED to the bone's setup rotation.
///   • BoneTranslate — <c>Value</c> = local X, <c>Value2</c> = local Y, both ADDED to setup.
///   • BoneScale     — <c>Value</c> = X, <c>Value2</c> = Y, MULTIPLIES the setup scale.
///   • SlotAttachment— <c>Value</c> = index into that slot's attachments (rig order);
///                     -1 hides the slot. The index is used instead of a name because a
///                     key carries numbers; the slot's attachment list is sorted by name
///                     so reordering the rig's attachment list cannot silently re-target
///                     a key.
///   • SlotColor     — <c>Value</c> = alpha multiplier (0..1) applied to the slot alpha.
///   • DrawOrder     — accepted but ignored in v1 (rig slot order wins).
/// </summary>
public static class SkeletonPoseEvaluator
{
    public static SkeletonPose Evaluate(AnimationRig rig, IReadOnlyList<AnimationTrack>? tracks,
        float time, string? skinName = null)
    {
        var pose = new SkeletonPose();
        if (rig == null) return pose;

        // ── animated channel values ──
        var rotate = new Dictionary<string, float>(StringComparer.Ordinal);
        var translateX = new Dictionary<string, float>(StringComparer.Ordinal);
        var translateY = new Dictionary<string, float>(StringComparer.Ordinal);
        var scaleX = new Dictionary<string, float>(StringComparer.Ordinal);
        var scaleY = new Dictionary<string, float>(StringComparer.Ordinal);
        var slotAlpha = new Dictionary<string, float>(StringComparer.Ordinal);
        var slotAttach = new Dictionary<string, int>(StringComparer.Ordinal);

        if (tracks != null)
        {
            foreach (var track in tracks)
            {
                if (track == null || string.IsNullOrEmpty(track.Target) || track.Keys.Count == 0) continue;
                switch (track.Kind)
                {
                    case AnimationTrackKind.BoneRotate:
                        Add(rotate, track.Target, AnimationCurve.Evaluate(track, time));
                        break;
                    case AnimationTrackKind.BoneTranslate:
                        // Same keys, two channels: X on Value, Y on Value2.
                        Add(translateX, track.Target, AnimationCurve.Evaluate(track, time));
                        Add(translateY, track.Target, EvaluateChannelTwo(track, time));
                        break;
                    case AnimationTrackKind.BoneScale:
                        Add(scaleX, track.Target, AnimationCurve.Evaluate(track, time));
                        Add(scaleY, track.Target, EvaluateChannelTwo(track, time));
                        break;
                    case AnimationTrackKind.SlotColor:
                        slotAlpha[track.Target] = AnimationCurve.Evaluate(track, time);
                        break;
                    case AnimationTrackKind.SlotAttachment:
                        slotAttach[track.Target] = (int)MathF.Round(AnimationCurve.Evaluate(track, time));
                        break;
                }
            }
        }

        // ── bone world transforms (memoized, cycle-safe) ──
        var byName = new Dictionary<string, Bone2D>(StringComparer.Ordinal);
        foreach (var b in rig.Bones)
            if (!string.IsNullOrEmpty(b.Name)) byName[b.Name] = b;

        var visiting = new HashSet<string>(StringComparer.Ordinal);

        Transform2D WorldOf(string name)
        {
            if (pose.Bones.TryGetValue(name, out var cached)) return cached;
            if (!byName.TryGetValue(name, out var bone)) return Transform2D.Identity;
            // A parent loop (bad data) silently falls back to the setup pose instead of
            // recursing forever — a bake must never hang the editor.
            if (!visiting.Add(name))
            {
                var flat = Transform2D.Trss(bone.X, bone.Y, bone.Rotation, bone.ScaleX,
                    bone.ScaleY, bone.ShearX, bone.ShearY);
                pose.Bones[name] = flat;
                return flat;
            }

            float tx = bone.X + (translateX.TryGetValue(name, out var dx) ? dx : 0f);
            float ty = bone.Y + (translateY.TryGetValue(name, out var dy) ? dy : 0f);
            float rot = bone.Rotation + (rotate.TryGetValue(name, out var dr) ? dr : 0f);
            float sx = bone.ScaleX * (scaleX.TryGetValue(name, out var fsx) ? fsx : 1f);
            float sy = bone.ScaleY * (scaleY.TryGetValue(name, out var fsy) ? fsy : 1f);

            var local = Transform2D.Trss(tx, ty, rot, sx, sy, bone.ShearX, bone.ShearY);
            var world = local;
            if (!string.IsNullOrEmpty(bone.Parent) && byName.ContainsKey(bone.Parent))
                world = WorldOf(bone.Parent) * local;

            visiting.Remove(name);
            pose.Bones[name] = world;
            return world;
        }

        foreach (var b in rig.Bones)
            if (!string.IsNullOrEmpty(b.Name)) WorldOf(b.Name);

        // ── slots in rig order (back to front) ──
        var skin = FindSkin(rig, skinName);
        var slotAttachments = new Dictionary<string, List<Attachment2D>>(StringComparer.Ordinal);
        foreach (var a in rig.Attachments)
        {
            if (a == null || string.IsNullOrEmpty(a.Slot)) continue;
            if (!slotAttachments.TryGetValue(a.Slot, out var list))
                slotAttachments[a.Slot] = list = new List<Attachment2D>();
            list.Add(a);
        }
        foreach (var list in slotAttachments.Values)
            list.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));

        foreach (var slot in rig.Slots)
        {
            if (slot == null || string.IsNullOrEmpty(slot.Name)) continue;
            string attachmentName = ResolveAttachmentName(slot, skin);
            if (slotAttachments.TryGetValue(slot.Name, out var options) && options.Count > 0)
            {
                // An animated index overrides the skin/setup choice; out-of-range values are
                // ignored (a stale key must not blank a whole character).
                if (slotAttach.TryGetValue(slot.Name, out var idx) && idx < options.Count)
                {
                    attachmentName = idx < 0 ? "" : options[idx].Name;
                }
            }

            if (!slot.Visible || string.IsNullOrEmpty(attachmentName)) continue;
            Attachment2D? attachment = null;
            if (slotAttachments.TryGetValue(slot.Name, out var list))
                attachment = list.FirstOrDefault(a => string.Equals(a.Name, attachmentName, StringComparison.Ordinal));
            attachment ??= rig.Attachments.FirstOrDefault(a =>
                string.Equals(a.Name, attachmentName, StringComparison.Ordinal));
            if (attachment == null) continue;

            var boneWorld = pose.Bones.TryGetValue(slot.Bone, out var bw) ? bw : Transform2D.Identity;
            var place = Transform2D.Trss(attachment.OffsetX, attachment.OffsetY, attachment.Rotation,
                attachment.ScaleX, attachment.ScaleY, 0f, 0f);

            float alpha = slot.A * (slotAlpha.TryGetValue(slot.Name, out var sa) ? Math.Clamp(sa, 0f, 1f) : 1f);
            pose.Slots.Add(new PoseSlot
            {
                SlotName = slot.Name,
                AttachmentName = attachment.Name,
                Attachment = attachment,
                World = boneWorld * place,
                R = slot.R, G = slot.G, B = slot.B, A = alpha,
            });
        }

        return pose;
    }

    private static void Add(Dictionary<string, float> map, string key, float value)
        => map[key] = map.TryGetValue(key, out var existing) ? existing + value : value;

    /// <summary>Evaluate the secondary channel (<c>Value2</c>) of a translate/scale track
    /// with the same keys and interpolation as the primary value.</summary>
    private static float EvaluateChannelTwo(AnimationTrack track, float time)
    {
        if (time <= track.Keys[0].Time) return track.Keys[0].Value2;
        var last = track.Keys[^1];
        if (time >= last.Time) return last.Value2;
        for (int i = 1; i < track.Keys.Count; i++)
        {
            var prev = track.Keys[i - 1];
            var next = track.Keys[i];
            if (time > next.Time) continue;
            float span = next.Time - prev.Time;
            if (span <= 0f) return next.Value2;
            float u = (time - prev.Time) / span;
            float eased = prev.Interp switch
            {
                AnimationKeyInterp.Stepped => 0f,
                AnimationKeyInterp.Bezier => AnimationCurve.BezierEase(u, prev.BezierOut, next.BezierIn),
                _ => u,
            };
            return prev.Value2 + (next.Value2 - prev.Value2) * eased;
        }
        return last.Value2;
    }

    private static string ResolveAttachmentName(Slot2D slot, Skin2D? skin)
    {
        if (skin != null && skin.AttachmentBySlot.TryGetValue(slot.Name, out var over) && over != null)
            return over;
        return slot.Attachment ?? "";
    }

    private static Skin2D? FindSkin(AnimationRig rig, string? skinName)
    {
        if (rig.Skins.Count == 0) return null;
        string wanted = string.IsNullOrWhiteSpace(skinName) ? rig.DefaultSkin : skinName!;
        return rig.Skins.FirstOrDefault(s => string.Equals(s.Name, wanted, StringComparison.Ordinal))
            ?? rig.Skins.FirstOrDefault(s => string.Equals(s.Name, rig.DefaultSkin, StringComparison.Ordinal));
    }
}
