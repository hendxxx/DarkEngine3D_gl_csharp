using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

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

    /// <summary>Speed multiplier (applied on top of FPS).</summary>
    public float SpeedMultiplier = 1f;

    /// <summary>Events triggered at specific frames. Key = frame index, Value = event name.</summary>
    public Dictionary<int, string> Events = new();

    // ── Runtime ──
    public float Duration => FrameIndices.Count > 0 ? FrameIndices.Count / (FPS * SpeedMultiplier) : 0f;

    /// <summary>
    /// Get the frame index at a given time (0-based into FrameIndices list).
    /// </summary>
    public int GetFrameAtTime(float time)
    {
        if (FrameIndices.Count == 0) return 0;

        float frameDuration = 1f / (FPS * SpeedMultiplier);
        int frame = (int)(time / frameDuration);

        if (Loop)
            frame = ((frame % FrameIndices.Count) + FrameIndices.Count) % FrameIndices.Count;
        else
            frame = Math.Clamp(frame, 0, FrameIndices.Count - 1);

        return frame;
    }

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
        Events = Events.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)
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
        Events = data.Events?.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value) ?? new()
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
}
