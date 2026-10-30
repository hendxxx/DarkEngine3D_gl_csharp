using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// DISPLAY-ONLY grouping helpers for animation clips. The auto-group name is derived
/// from the clip's own name (pattern exports are authored as `&lt;file name&gt;-&lt;action&gt;`,
/// so `Male Skin1-idle` belongs to `Male Skin1`), which means NO extra field is stored:
/// `AnimationClip2D.GroupPath` stays the only PERSISTED grouping and sprite-anim JSON
/// files are never rewritten by this. The Animation Editor tree and the Sprite Editor
/// clip list use it purely to render tidy one-level groups.
/// </summary>
public static class AnimationClipGroups
{
    /// <summary>Base sprite name: everything before the LAST '-' of the clip name
    /// (`Male Skin1-walk` → `Male Skin1`, `hero-walk-cycle` → `hero-walk`).
    /// A name without a usable prefix returns "" (no group derivable).</summary>
    public static string BaseNameOf(string? clipName)
    {
        if (string.IsNullOrWhiteSpace(clipName)) return "";
        int dash = clipName.LastIndexOf('-');
        if (dash <= 0) return "";
        return clipName[..dash].Trim();
    }

    /// <summary>Action label shown UNDER a derived group: everything after the LAST '-'.
    /// Falls back to the clip name itself when no prefix exists.</summary>
    public static string ActionLabelOf(string? clipName)
    {
        if (string.IsNullOrWhiteSpace(clipName)) return "";
        int dash = clipName.LastIndexOf('-');
        if (dash < 0 || dash >= clipName.Length - 1) return clipName;
        return clipName[(dash + 1)..].Trim();
    }

    /// <summary>PARENT segment of a group path: "Male Skin1/Idle" → "Male Skin1". The
    /// Inspector's Animation Group picker only ever shows parents; leaf paths remain
    /// visible in the Animation Editor tree. Empty stays empty.</summary>
    public static string RootGroupOf(string? groupPath)
    {
        if (string.IsNullOrWhiteSpace(groupPath)) return "";
        int slash = groupPath.IndexOf('/');
        return (slash > 0 ? groupPath[..slash] : groupPath).Trim();
    }

    /// <summary>The group a clip is DISPLAYED under. A manually assigned GroupPath always
    /// wins; with auto-group off (or no derivable base) the clip stays ungrouped ("").
    /// Never mutates the clip.</summary>
    public static string DisplayGroupOf(AnimationClip2D clip, bool autoGroupByBase)
    {
        if (clip == null) return "";
        if (!string.IsNullOrEmpty(clip.GroupPath)) return clip.GroupPath;
        return autoGroupByBase ? BaseNameOf(clip.Name) : "";
    }
}

/// <summary>
/// Animation clip for 2D sprites.
/// Defines a sequence of frames from a sprite sheet with timing and events.
/// </summary>
public class AnimationClip2D
{
    public string Name = "Idle";

    /// <summary>Reference to sprite sheet name (resolved at runtime).</summary>
    public string SpriteSheetName = "";

    /// <summary>Frame indices into the sprite sheet (ordered playback).</summary>
    public List<int> FrameIndices = new();

    /// <summary>Frames per second.</summary>
    public float FPS = 12f;

    /// <summary>Loop playback when reaching end.</summary>
    public bool Loop = true;

    /// <summary>Play in reverse.</summary>
    public bool Reverse;

    // ── Authoring (Phase 0/1 — ignored by renderers) ──
    /// <summary>Animation Library group path ("Player/Hero/Locomotion"; empty = ungrouped).</summary>
    public string GroupPath = "";
    /// <summary>Sprite (frame strip) or Skeleton (rig/timeline, baked). Metadata only at runtime.</summary>
    public AnimationClipMode Mode = AnimationClipMode.Sprite;
    /// <summary>Source sheet this clip was authored from. Empty on legacy baked clips.
    /// Kept so the clip can be re-edited and REBAKED after its atlas frames were written.</summary>
    public string SourceSheetName = "";
    /// <summary>Frame indices INTO <see cref="SourceSheetName"/>, in playback order. This is
    /// the authored frame strip; <see cref="FrameIndices"/> is the BAKED atlas result.
    /// Empty on legacy clips (timing edits only, no rebake).</summary>
    public List<int> SourceFrameIndices = new();
    /// <summary>Per-frame duration MULTIPLIER (1 = normal). Lets one frame hold longer in
    /// the timeline without changing FPS. Empty/short list → 1. Authoring + runtime timing.</summary>
    public List<float> FrameHolds = new();
    /// <summary>True when the authored strip differs from the last bake (timeline edits,
    /// holds, source change). The editor shows a dirty badge; rebake clears it.</summary>
    public bool BakeDirty;
    /// <summary>Stable clip id (guid). Generated when a clip is created or when an old
    /// file without ids loads. Editor-side linking only — gameplay still keys off Name.</summary>
    public string Id = Guid.NewGuid().ToString("N");
    /// <summary>Skeleton mode only: id of the <see cref="AnimationRig"/> this clip animates.
    /// Empty on sprite clips and on every legacy clip.</summary>
    public string RigId = "";
    /// <summary>Timeline tracks (Phase 0/2 authoring). Null/empty on legacy clips — the
    /// timeline then synthesizes a frame track from <see cref="EditFrameIndices"/> + FPS.</summary>
    public List<AnimationTrack>? Tracks;
    /// <summary>Bake settings + last-bake hash (Phase 0 field; the Phase 2 baker reads it,
    /// falling back to the clip's own FPS when null).</summary>
    public BakeSettings? Bake;

    /// <summary>Authored frames the editor should show/edit: source strip when known,
    /// else the baked atlas frames.</summary>
    public List<int> EditFrameIndices => SourceFrameIndices.Count > 0 ? SourceFrameIndices : FrameIndices;

    /// <summary>True when this clip can be rebaked from its source sheet.</summary>
    public bool CanRebake => !string.IsNullOrEmpty(SourceSheetName) && SourceFrameIndices.Count > 0;

    /// <summary>Frame hold multiplier (1 = normal).</summary>
    public float GetHold(int frameIndex)
        => frameIndex >= 0 && frameIndex < FrameHolds.Count
            ? MathF.Max(0.05f, FrameHolds[frameIndex]) : 1f;

    /// <summary>Duration in seconds including per-frame holds.</summary>
    public float DurationAt(float fps, float speed)
    {
        int n = FrameIndices.Count;
        if (n == 0) return 0f;
        float frame = 1f / MathF.Max(0.0001f, fps * MathF.Max(0.0001f, speed));
        float total = 0f;
        for (int i = 0; i < n; i++) total += frame * GetHold(i);
        return total;
    }

    /// <summary>Local frame index at <paramref name="time"/> honoring holds. Fast path when
    /// no holds are authored (identical to the old integer math).</summary>
    public int GetFrameAtTimeWithHolds(float time, bool loop)
    {
        int n = FrameIndices.Count;
        if (n == 0) return 0;
        float frameDur = 1f / MathF.Max(0.0001f, FPS * MathF.Max(0.0001f, SpeedMultiplier));

        if (FrameHolds.Count == 0)
        {
            int f = (int)(time / frameDur);
            return loop ? ((f % n) + n) % n : Math.Clamp(f, 0, n - 1);
        }

        float total = DurationAt(FPS, SpeedMultiplier);
        if (loop)
        {
            if (total <= 0f) return 0;
            time %= total;
            if (time < 0f) time += total;
        }
        else
        {
            time = Math.Clamp(time, 0f, MathF.Max(0f, total - 0.00001f));
        }

        float acc = 0f;
        for (int i = 0; i < n; i++)
        {
            acc += frameDur * GetHold(i);
            if (time < acc) return i;
        }
        return n - 1;
    }

    /// <summary>Speed multiplier (applied on top of FPS).</summary>
    public float SpeedMultiplier = 1f;

    /// <summary>Events triggered at specific frames. Key = frame index, Value = event name.</summary>
    public Dictionary<int, string> Events = new();

    // ── Master render box snapshot (copied from the sheet at Create Clip time) ──
    /// <summary>Master box the clip's frames render into (W × H px). Snapshot from the
    /// sheet's Render Normalization when the clip was created, so later sheet edits
    /// don't shift already-aligned clips. 0 = fall back to the sheet's live values.</summary>
    public float MasterWidth = 0f;
    public float MasterHeight = 0f;
    /// <summary>Sheet-level render offsets snapshotted with the master box.</summary>
    public float SpriteOffsetX = 0f;
    public float SpriteOffsetY = 0f;

    // ── Runtime ──
    public float Duration => DurationAt(FPS, SpeedMultiplier);

    /// <summary>
    /// Get the frame index at a given time (0-based into FrameIndices list).
    /// </summary>
    public int GetFrameAtTime(float time)
        => GetFrameAtTimeWithHolds(time, Loop);

    /// <summary>Loop-override variant (Change Sprite "Loop Animasi"): the caller's
    /// loop flag wins over the clip's own — Off = play once and hold the last frame.</summary>
    public int GetFrameAtTime(float time, bool loopOverride)
        => GetFrameAtTimeWithHolds(time, loopOverride);

    /// <summary>
    /// Get the sprite sheet frame index at a given time.
    /// </summary>
    public int GetSpriteFrameAtTime(float time)
    {
        int localFrame = GetFrameAtTime(time);
        if (localFrame < FrameIndices.Count)
            return FrameIndices[localFrame];
        return 0;
    }

    /// <summary>Loop-override variant — mirrors GetFrameAtTime(time, loopOverride)
    /// for the Change Sprite "Loop Animasi" off state (hold the last frame).</summary>
    public int GetSpriteFrameAtTime(float time, bool loopOverride)
    {
        int localFrame = GetFrameAtTime(time, loopOverride);
        if (localFrame < FrameIndices.Count)
            return FrameIndices[localFrame];
        return 0;
    }

    /// <summary>
    /// Check if an event should fire at the given time (compared to previous time).
    /// Returns event name if triggered, null otherwise.
    /// </summary>
    public string? CheckEvent(float previousTime, float currentTime)
    {
        if (Events.Count == 0 || FrameIndices.Count == 0) return null;

        int prevFrame = GetFrameAtTime(previousTime);
        int currFrame = GetFrameAtTime(currentTime);

        if (prevFrame == currFrame) return null;

        // Check if any event fires between prevFrame and currFrame
        for (int f = prevFrame + 1; f <= currFrame; f++)
        {
            int idx = Loop ? f % FrameIndices.Count : Math.Min(f, FrameIndices.Count - 1);
            if (Events.TryGetValue(idx, out string? evt) && evt != null)
                return evt;
        }
        return null;
    }

    /// <summary>
    /// Create a simple walk animation from consecutive frames.
    /// </summary>
    public static AnimationClip2D CreateWalk(string sheetName, int startFrame, int frameCount, float fps = 8f) => new()
    {
        Name = "Walk",
        SpriteSheetName = sheetName,
        FrameIndices = Enumerable.Range(startFrame, frameCount).ToList(),
        FPS = fps,
        Loop = true
    };

    /// <summary>
    /// Create a jump animation (plays once).
    /// </summary>
    public static AnimationClip2D CreateJump(string sheetName, int startFrame, int frameCount, float fps = 12f) => new()
    {
        Name = "Jump",
        SpriteSheetName = sheetName,
        FrameIndices = Enumerable.Range(startFrame, frameCount).ToList(),
        FPS = fps,
        Loop = false
    };

    /// <summary>
    /// Create an attack animation with hit event.
    /// </summary>
    public static AnimationClip2D CreateAttack(string sheetName, int startFrame, int frameCount,
        int hitFrame, float fps = 12f) => new()
    {
        Name = "Attack",
        SpriteSheetName = sheetName,
        FrameIndices = Enumerable.Range(startFrame, frameCount).ToList(),
        FPS = fps,
        Loop = false,
        Events = { [hitFrame] = "Hit" }
    };

    // ── Serialization ──

    public AnimationClip2DData ToData() => new()
    {
        Name = Name,
        SpriteSheetName = SpriteSheetName,
        FrameIndices = FrameIndices.ToList(),
        FPS = FPS,
        Loop = Loop,
        Reverse = Reverse,
        SpeedMultiplier = SpeedMultiplier,
        Events = Events.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        MasterWidth = MasterWidth,
        MasterHeight = MasterHeight,
        SpriteOffsetX = SpriteOffsetX,
        SpriteOffsetY = SpriteOffsetY,
        GroupPath = GroupPath,
        Mode = Mode,
        SourceSheetName = SourceSheetName,
        SourceFrameIndices = SourceFrameIndices.ToList(),
        FrameHolds = FrameHolds.ToList(),
        BakeDirty = BakeDirty,
        Id = Id,
        RigId = RigId,
        Tracks = Tracks?.Select(t => t.Clone()).ToList(),
        Bake = Bake?.Clone()
    };

    public static AnimationClip2D FromData(AnimationClip2DData data) => new()
    {
        Name = data.Name,
        SpriteSheetName = data.SpriteSheetName,
        FrameIndices = data.FrameIndices?.ToList() ?? new(),
        FPS = data.FPS,
        Loop = data.Loop,
        Reverse = data.Reverse,
        SpeedMultiplier = data.SpeedMultiplier,
        Events = data.Events?.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value) ?? new(),
        MasterWidth = data.MasterWidth,
        MasterHeight = data.MasterHeight,
        SpriteOffsetX = data.SpriteOffsetX,
        SpriteOffsetY = data.SpriteOffsetY,
        GroupPath = data.GroupPath ?? "",
        Mode = data.Mode,
        SourceSheetName = data.SourceSheetName ?? "",
        SourceFrameIndices = data.SourceFrameIndices?.ToList() ?? new(),
        FrameHolds = data.FrameHolds?.ToList() ?? new(),
        BakeDirty = data.BakeDirty,
        // Pre-Phase-0 files carry no id: mint one so every clip has a stable handle.
        Id = string.IsNullOrEmpty(data.Id) ? Guid.NewGuid().ToString("N") : data.Id,
        RigId = data.RigId ?? "",
        Tracks = data.Tracks?.Select(t => t.Clone()).ToList(),
        Bake = data.Bake?.Clone()
    };
}

/// <summary>
/// Runtime state for playing an animation clip.
/// </summary>
public class AnimationPlayer2D
{
    public AnimationClip2D? CurrentClip;
    public float CurrentTime;
    public bool IsPlaying;
    public bool IsFacingRight = true;

    /// <summary>Current state name (for state machine transitions).</summary>
    public string CurrentState = "Idle";

    /// <summary>All available clips indexed by name.</summary>
    public Dictionary<string, AnimationClip2D> Clips = new();

    /// <summary>State transition rules: from state -> (condition -> to state).</summary>
    public List<AnimationTransition> Transitions = new();

    /// <summary>Last event that was triggered (cleared each frame).</summary>
    public string? LastTriggeredEvent;

    public void Play(string clipName, bool restart = false)
    {
        if (!Clips.TryGetValue(clipName, out var clip)) return;
        if (CurrentClip == clip && IsPlaying && !restart) return;

        CurrentClip = clip;
        CurrentTime = 0f;
        IsPlaying = true;
        CurrentState = clipName;
    }

    public void Stop()
    {
        IsPlaying = false;
    }

    /// <summary>
    /// Update animation. Returns the current sprite sheet frame index (-1 if no clip).
    /// </summary>
    public int Update(float deltaTime, Dictionary<string, string>? conditions = null)
    {
        if (!IsPlaying || CurrentClip == null) return -1;

        float prevTime = CurrentTime;
        CurrentTime += deltaTime * (CurrentClip.Reverse ? -1f : 1f);

        // Check for loop end
        if (!CurrentClip.Loop && CurrentTime >= CurrentClip.Duration)
        {
            CurrentTime = CurrentClip.Duration;
            IsPlaying = false;
        }
        else if (CurrentClip.Loop)
        {
            CurrentTime = CurrentTime % CurrentClip.Duration;
            if (CurrentTime < 0) CurrentTime += CurrentClip.Duration;
        }

        // Check events
        LastTriggeredEvent = CurrentClip.CheckEvent(prevTime, CurrentTime);

        // Evaluate transitions
        if (conditions != null)
        {
            foreach (var t in Transitions)
            {
                if (t.FromState != CurrentState) continue;
                if (t.MeetsCondition(conditions, LastTriggeredEvent))
                {
                    Play(t.ToState);
                    break;
                }
            }
        }

        return CurrentClip.GetSpriteFrameAtTime(CurrentTime);
    }
}

/// <summary>
/// Transition rule between animation states.
/// </summary>
public class AnimationTransition
{
    public string FromState = "";
    public string ToState = "";

    /// <summary>Required conditions (key-value pairs that must match).</summary>
    public Dictionary<string, string> Conditions = new();

    /// <summary>If set, transition triggers on this animation event.</summary>
    public string? TriggerEvent;

    /// <summary>If set, transition triggers when animation finishes.</summary>
    public bool OnComplete;

    public bool MeetsCondition(Dictionary<string, string> state, string? triggeredEvent)
    {
        // Check event trigger
        if (TriggerEvent != null && triggeredEvent != TriggerEvent)
            return false;

        // Check all conditions
        foreach (var kv in Conditions)
        {
            if (!state.TryGetValue(kv.Key, out var val) || val != kv.Value)
                return false;
        }
        return true;
    }
}

// ── Serialization DTOs ──

public class AnimationClip2DData
{
    public string Name { get; set; } = "Idle";
    public string SpriteSheetName { get; set; } = "";
    public List<int>? FrameIndices { get; set; }
    public float FPS { get; set; } = 12f;
    public bool Loop { get; set; } = true;
    public bool Reverse { get; set; }
    public float SpeedMultiplier { get; set; } = 1f;
    public Dictionary<string, string>? Events { get; set; }
    // ── Master render box snapshot (per clip) ──
    public float MasterWidth { get; set; } = 0f;
    public float MasterHeight { get; set; } = 0f;
    public float SpriteOffsetX { get; set; } = 0f;
    public float SpriteOffsetY { get; set; } = 0f;

    // ── Authoring (Phase 0/1) ──
    public string GroupPath { get; set; } = "";
    public AnimationClipMode Mode { get; set; } = AnimationClipMode.Sprite;
    public string SourceSheetName { get; set; } = "";
    public List<int>? SourceFrameIndices { get; set; }
    public List<float>? FrameHolds { get; set; }
    public bool BakeDirty { get; set; }
    public string Id { get; set; } = "";
    public string? RigId { get; set; }
    public List<AnimationTrack>? Tracks { get; set; }
    public BakeSettings? Bake { get; set; }
}
