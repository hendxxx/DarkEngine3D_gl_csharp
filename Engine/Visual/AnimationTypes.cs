using System.Text.Json.Serialization;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// How a clip is authored:
///   • Sprite   — the existing frame-strip animation (FrameIndices into a baked atlas).
///   • Skeleton — a rig/timeline clip whose frames are baked from the rig (Phase 2).
///
/// The runtime only ever sees the baked result; Mode is authoring metadata.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnimationClipMode
{
    Sprite,
    Skeleton,
}

/// <summary>
/// One node of the Animation Library tree. Paths are display/organisation only
/// ("Player/Hero/Locomotion") — a clip points at its group by <see cref="AnimationClip2D.GroupPath"/>,
/// so lookup by (sheet, clip name) is untouched and old files simply have no groups.
/// </summary>
public class AnimationGroup
{
    /// <summary>Full path, '/'-separated. Never empty for a real group.</summary>
    public string Path { get; set; } = "";

    /// <summary>Sort order among siblings (lower first).</summary>
    public int SortOrder { get; set; }

    /// <summary>Drag & drop dress-up state: which items/sheets sit on the preview
    /// character for this group (editor-only — the game equips through the inventory,
    /// so this never reaches the runtime).</summary>
    public List<PreviewLoadoutEntry> PreviewLoadout { get; set; } = new();

    /// <summary>Editor-only expand state — never serialized.</summary>
    [JsonIgnore]
    public bool IsExpanded { get; set; } = true;

    public AnimationGroup() { }

    public AnimationGroup(string path, int sortOrder = 0)
    {
        Path = path;
        SortOrder = sortOrder;
    }

    public AnimationGroup Clone() => new(Path, SortOrder)
    {
        IsExpanded = IsExpanded,
        PreviewLoadout = PreviewLoadout.Select(e => e.Clone()).ToList(),
    };
}

/// <summary>One layer of a group's Preview Loadout — something dragged from the palette
/// onto the preview character.
/// Item entries store ONLY the item id and resolve their art live from
/// <see cref="InventorySystem.ItemDef"/> (EquipSheet/EquipClip/EquipLayer/offsets/sync),
/// so editing the item in the Item Editor updates the preview with no re-link.
/// Direct sheet entries carry their own values (there is no item to read from).</summary>
public sealed class PreviewLoadoutEntry
{
    /// <summary>Inventory item id; empty = direct sheet layer.</summary>
    public string ItemId { get; set; } = "";
    /// <summary>Overlay sheet for a direct layer (ignored when ItemId is set).</summary>
    public string Sheet { get; set; } = "";
    /// <summary>Optional explicit overlay clip ("" = auto-match by action token, the
    /// same FindAnimationClipForBase rule the runtime uses).</summary>
    public string Clip { get; set; } = "";
    public int Layer { get; set; } = 1;
    /// <summary>World-unit nudge from the character's bottom-center.</summary>
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    /// <summary>Overlay height in world units (0 = same height as the character).</summary>
    public float WorldHeight { get; set; }
    /// <summary>Sample the overlay at the base clip's position-in-clip (clothing);
    /// false = animate on the overlay's own FPS (auras/fire).</summary>
    public bool SyncFrame { get; set; } = true;
    public bool Visible { get; set; } = true;

    public PreviewLoadoutEntry Clone() => (PreviewLoadoutEntry)MemberwiseClone();
}

/// <summary>
/// Default timing for one action of a standard animation set (Idle/Walk/Run/Attack/Dead…).
/// Used by the New Animation Set dialog; the dialog rows are editable before creation.
/// </summary>
public sealed class AnimationSetPreset
{
    // Properties (not fields): System.Text.Json only serializes properties, and these
    // are persisted as AnimationTemplates in sprites.sheets.json.
    public string Action { get; set; } = "idle";

    /// <summary>Display name (title-cased) shown in the dialog.</summary>
    public string Label { get; set; } = "Idle";

    public float Fps { get; set; } = 8f;
    public bool Loop { get; set; } = true;

    /// <summary>Play once and hold the last frame (Dead) — implemented as Loop=false.</summary>
    public bool HoldLast { get; set; }

    /// <summary>Optional event authored at <see cref="EventAtPercent"/> of the range.</summary>
    public string? EventName { get; set; }
    public float EventAtPercent { get; set; } = 0.6f;

    public AnimationSetPreset() { }

    public AnimationSetPreset(string action, string label, float fps, bool loop,
        bool holdLast = false, string? eventName = null)
    {
        Action = action;
        Label = label;
        Fps = fps;
        Loop = loop;
        HoldLast = holdLast;
        EventName = eventName;
    }

    public AnimationSetPreset Clone() => (AnimationSetPreset)MemberwiseClone();

    /// <summary>The default standard set. Actions follow the existing Save As Pattern
    /// naming so `<sheet>-<action>` clips keep matching equipment layers.</summary>
    public static readonly AnimationSetPreset[] Defaults =
    [
        new("idle",       "Idle",       8f,  true),
        new("walk",       "Walk",       8f,  true),
        new("run",        "Run",       12f,  true),
        new("attack",     "Attack",    12f, false, false, "Hit"),
        new("dead",       "Dead",      10f, false, true),
        new("jump start", "Jump Start",12f, false),
        new("jump end",   "Jump End",  12f, false),
        new("hurt",       "Hurt",      10f, false),
        new("cast",       "Cast",      12f, false),
    ];
}

/// <summary>What one timeline track drives. Sprite clips only ever use
/// <see cref="SpriteFrame"/> (synthesized from FrameIndices + FPS); the rest belong to
/// skeleton clips (Phase 2) but live in the model now so the file format is settled.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnimationTrackKind
{
    SpriteFrame,
    BoneRotate,
    BoneTranslate,
    BoneScale,
    SlotAttachment,
    SlotColor,
    DrawOrder,
    Event,
}

/// <summary>Key interpolation. Bezier carries two control offsets for the curve editor;
/// evaluation of Bezier lands in Phase 2 next to the skeleton pose evaluator.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnimationKeyInterp
{
    Linear,
    Stepped,
    Bezier,
}

/// <summary>One keyframe. Time is in SECONDS (frame-rate independent — the sprite
/// timeline converts through FPS for display only).</summary>
public sealed class AnimationKey
{
    public float Time { get; set; }
    /// <summary>Primary value (rotation degrees, frame index, event payload …).</summary>
    public float Value { get; set; }
    /// <summary>Secondary channel (translation Y, scale Y, color …).</summary>
    public float Value2 { get; set; }
    public AnimationKeyInterp Interp { get; set; } = AnimationKeyInterp.Linear;
    /// <summary>Bezier control offsets (relative to the key) — Phase 2 curve editor.</summary>
    public float BezierIn { get; set; }
    public float BezierOut { get; set; } = 1f;

    public AnimationKey Clone() => (AnimationKey)MemberwiseClone();
}

/// <summary>A track of keyframes over one target (a bone, a slot, the frame strip, the
/// event channel). Track list order is the timeline's display order.</summary>
public sealed class AnimationTrack
{
    public AnimationTrackKind Kind { get; set; } = AnimationTrackKind.SpriteFrame;
    /// <summary>Target name: bone / slot name, or "frame" / "event" for the sprite rows.</summary>
    public string Target { get; set; } = "";
    public List<AnimationKey> Keys { get; set; } = new();

    public AnimationTrack Clone() => new()
    {
        Kind = Kind,
        Target = Target,
        Keys = Keys.Select(k => k.Clone()).ToList(),
    };

    /// <summary>Value at <paramref name="time"/> seconds (holds the last key
    /// outside the range). A key's <c>Interp</c> governs the segment from that key to
    /// the NEXT one (Spine convention): Stepped holds the previous value across its
    /// segment. Bezier falls back to Linear until the Phase 2 curve evaluator lands —
    /// never silently wrong, just unsmoothed.</summary>
    public float Evaluate(float time)
    {
        if (Keys.Count == 0) return 0f;
        if (time <= Keys[0].Time) return Keys[0].Value;
        var last = Keys[^1];
        if (time >= last.Time) return last.Value;

        for (int i = 1; i < Keys.Count; i++)
        {
            var prev = Keys[i - 1];
            var next = Keys[i];
            if (time > next.Time) continue;
            if (prev.Interp == AnimationKeyInterp.Stepped) return prev.Value;
            float span = next.Time - prev.Time;
            if (span <= 0f) return next.Value;
            float t = (time - prev.Time) / span;
            return prev.Value + (next.Value - prev.Value) * t;
        }
        return last.Value;
    }
}

/// <summary>How a clip is baked (Phase 0 field, used by the bake buttons in Phase 2).
/// Missing on legacy clips — the baker then falls back to the clip's own FPS.</summary>
public sealed class BakeSettings
{
    /// <summary>Bake sample rate. 0 = follow <c>AnimationClip2D.FPS</c>.</summary>
    public float Fps { get; set; }
    /// <summary>Preferred atlas number (0 = let the baker pick the first fit).</summary>
    public int TargetAtlas { get; set; }
    /// <summary>Hash of rig + tracks + settings at the last bake — dirty badge source.</summary>
    public string LastBakeHash { get; set; } = "";

    public BakeSettings Clone() => (BakeSettings)MemberwiseClone();
}
