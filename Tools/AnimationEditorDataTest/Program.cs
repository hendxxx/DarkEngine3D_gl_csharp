using System.Text.Json;
using DarkEngine3D_gl_csharp.Engine.IDE.Panels;
using DarkEngine3D_gl_csharp.Engine.Visual;

// Harness for the Animation Editor's DATA CONTRACT (Phase 0/1). No GL, no window:
// everything here is what the panel reads/writes through sprites.sheets.json plus the
// timeline timing math the preview and the runtime share.
//
//   A. Clip authoring fields survive a save/load round-trip (group, mode, source strip,
//      holds, dirty flag) — without these a re-save silently turns clips into legacy ones.
//   B. Library groups survive the round-trip, in order, with paths intact — plus the
//      Preview Loadout entries (item id vs direct sheet values, deep-cloned).
//   C. Old files (pre-Phase-0) load safely: Sprite mode, empty source strip, no groups,
//      and the editor reports the clip as non-rebakable instead of crashing.
//   D. Timeline holds change duration + frame lookup exactly as the preview expects.
//   E. The standard set presets are the ones the New Set dialog wants, with the same
//      "<sheet>-<action>" naming the existing equipment layers match on.
//   F. Phase 0 rig / track / bake-settings model round-trips (deep copies, string enums).
//   G. Pre-Phase-0 files leave the new fields at their defaults and mint a clip id.
//   H. Data-driven set templates survive the save/load cycle unchanged.
//   I. Key evaluation: linear interpolation, stepped hold, end clamping.

namespace DarkEngine3D_gl_csharp.Tools.AnimationEditorDataTest;

public static class Program
{
    private static int _fails;

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static int Main()
    {
        try
        {
            AuthoringRoundTrip();
            GroupsRoundTrip();
            LegacyFilesLoad();
            HoldsDriveTiming();
            StandardSetPresets();
            RigsTracksBakeRoundTrip();
            LegacyPhase0Defaults();
            TemplatesRoundTrip();
            TrackEvaluation();
            LayeredClipRoundTrip();
            SkeletonClipSemantics();
        }
        catch (Exception ex)
        {
            Check($"harness crashed: {ex}", false);
        }

        Console.WriteLine(_fails == 0
            ? "ALL ANIMATION EDITOR DATA CASES PASS"
            : $"{_fails} ANIMATION EDITOR DATA FAILURES");
        return _fails == 0 ? 0 : 1;
    }

    // ── A ────────────────────────────────────────────────────────────────────────────

    private static void AuthoringRoundTrip()
    {
        Console.WriteLine("== A. clip authoring fields survive save/load ==\n");

        var clip = new AnimationClip2D
        {
            Name = "Hero-Walk",
            SpriteSheetName = "Hero-atlas-walk",
            FrameIndices = [0, 1, 2],
            FPS = 8f,
            Loop = true,
            GroupPath = "Player/Hero/Locomotion",
            Mode = AnimationClipMode.Sprite,
            SourceSheetName = "Hero",
            SourceFrameIndices = [4, 5, 6],
            FrameHolds = [1f, 2.5f, 1f],
            BakeDirty = true,
            MasterWidth = 128f,
            MasterHeight = 128f,
            Events = { [2] = "Footstep" },
        };

        var save = new SpriteSheetsSaveData { AnimationClips = [clip.ToData()] };
        var loaded = JsonSerializer.Deserialize<SpriteSheetsSaveData>(
            JsonSerializer.Serialize(save, Opts))!;
        var back = AnimationClip2D.FromData(loaded.AnimationClips[0]);

        Check("group path survives", back.GroupPath == "Player/Hero/Locomotion");
        Check("mode survives", back.Mode == AnimationClipMode.Sprite);
        Check("source sheet survives", back.SourceSheetName == "Hero");
        Check("authored strip survives, in order",
            back.SourceFrameIndices.SequenceEqual([4, 5, 6]));
        Check("per-frame holds survive", back.FrameHolds.SequenceEqual([1f, 2.5f, 1f]));
        Check("dirty flag survives", back.BakeDirty);
        Check("baked strip survives", back.FrameIndices.SequenceEqual([0, 1, 2]));
        Check("events survive", back.Events.TryGetValue(2, out var ev) && ev == "Footstep");
        Check("clip is rebakable after reload", back.CanRebake);
        Check("editor frame list is the authored strip, not the baked one",
            back.EditFrameIndices.SequenceEqual([4, 5, 6]));
    }

    // ── B ────────────────────────────────────────────────────────────────────────────

    private static void GroupsRoundTrip()
    {
        Console.WriteLine("\n== B. library groups + preview loadout survive save/load ==\n");

        var save = new SpriteSheetsSaveData
        {
            AnimationGroups =
            [
                new AnimationGroup("Player", 0),
                new AnimationGroup("Player/Hero", 1),
                new AnimationGroup("Player/Hero/Locomotion", 2),
            ],
        };
        // Loadout: one item layer (only the id is stored — art resolves LIVE from the
        // item) and one direct sheet layer with its own values.
        save.AnimationGroups[1].PreviewLoadout.Add(new PreviewLoadoutEntry
        {
            ItemId = "sword_iron",
            Visible = true,
        });
        save.AnimationGroups[1].PreviewLoadout.Add(new PreviewLoadoutEntry
        {
            Sheet = "hair_spiky",
            Clip = "hair-idle",
            Layer = -1,
            OffsetX = 0.05f,
            OffsetY = 0.1f,
            WorldHeight = 0.4f,
            SyncFrame = false,
            Visible = false,
        });

        var loaded = JsonSerializer.Deserialize<SpriteSheetsSaveData>(
            JsonSerializer.Serialize(save, Opts))!;

        Check("all groups survive", loaded.AnimationGroups.Count == 3);
        Check("group order survives",
            loaded.AnimationGroups.Select(g => g.Path)
                .SequenceEqual(["Player", "Player/Hero", "Player/Hero/Locomotion"]));
        Check("sort order survives", loaded.AnimationGroups[2].SortOrder == 2);
        Check("editor-only expand state is not serialized",
            !JsonSerializer.Serialize(save, Opts).Contains("IsExpanded", StringComparison.OrdinalIgnoreCase));

        var lo = loaded.AnimationGroups[1].PreviewLoadout;
        Check("loadout entries survive", lo.Count == 2);
        Check("item layer keeps its id (art stays live, no snapshot)",
            lo[0].ItemId == "sword_iron" && lo[0].Layer == 1 && lo[0].Sheet == "");
        Check("direct sheet layer keeps its values",
            lo[1].Sheet == "hair_spiky" && lo[1].Clip == "hair-idle" && lo[1].Layer == -1
            && lo[1].OffsetX == 0.05f && lo[1].OffsetY == 0.1f && lo[1].WorldHeight == 0.4f
            && !lo[1].SyncFrame && !lo[1].Visible);
        Check("other groups have no loadout",
            loaded.AnimationGroups[0].PreviewLoadout.Count == 0
            && loaded.AnimationGroups[2].PreviewLoadout.Count == 0);

        // Clone must deep-copy entries (group snapshots feed the undo stack) — a shared
        // entry would let an undo restore mutate the live loadout.
        var cloned = save.AnimationGroups[1].Clone();
        cloned.PreviewLoadout[0].Visible = false;
        cloned.PreviewLoadout.Add(new PreviewLoadoutEntry { Sheet = "extra" });
        Check("Clone deep-copies the loadout",
            save.AnimationGroups[1].PreviewLoadout.Count == 2
            && save.AnimationGroups[1].PreviewLoadout[0].Visible);
    }

    // ── C ────────────────────────────────────────────────────────────────────────────

    private static void LegacyFilesLoad()
    {
        Console.WriteLine("\n== C. pre-Phase-0 files load safely ==\n");

        const string legacyJson = """
        {
          "AnimationClips": [
            { "Name": "Idle", "SpriteSheetName": "Hero-atlas", "FrameIndices": [0, 1], "FPS": 12, "Loop": true }
          ]
        }
        """;

        var loaded = JsonSerializer.Deserialize<SpriteSheetsSaveData>(legacyJson)!;
        Check("legacy file loads", loaded.AnimationClips.Count == 1);
        Check("no groups invented", loaded.AnimationGroups.Count == 0);

        var clip = AnimationClip2D.FromData(loaded.AnimationClips[0]);
        Check("legacy clip defaults to Sprite mode", clip.Mode == AnimationClipMode.Sprite);
        Check("legacy clip has no source sheet", string.IsNullOrEmpty(clip.SourceSheetName));
        Check("legacy clip has no authored strip", clip.SourceFrameIndices.Count == 0);
        Check("legacy clip is not rebakable", !clip.CanRebake);
        Check("legacy clip still plays its baked frames",
            clip.GetSpriteFrameAtTime(0.0f) == 0 && clip.GetSpriteFrameAtTime(0.1f) == 1);
    }

    // ── D ────────────────────────────────────────────────────────────────────────────

    private static void HoldsDriveTiming()
    {
        Console.WriteLine("\n== D. timeline holds drive duration + frame lookup ==\n");

        var clip = new AnimationClip2D
        {
            Name = "Step",
            FrameIndices = [0, 1, 2],
            FPS = 10f,
            Loop = true,
            FrameHolds = [1f, 2f, 1f],
        };

        // 3 frames at 10 FPS = 0.1s each; frame 1 is held twice → 0.4s total.
        Check("duration includes holds", Math.Abs(clip.Duration - 0.4f) < 1e-4f);
        Check("frame 0 during its slot", clip.GetFrameAtTime(0.05f) == 0);
        Check("frame 1 spans its double slot", clip.GetFrameAtTime(0.15f) == 1 && clip.GetFrameAtTime(0.25f) == 1);
        Check("frame 2 after the held frame", clip.GetFrameAtTime(0.35f) == 2);
        Check("loops back to frame 0", clip.GetFrameAtTime(0.41f) == 0);

        clip.FrameHolds[1] = 1f;
        Check("removing the hold shortens the clip", Math.Abs(clip.Duration - 0.3f) < 1e-4f);

        clip.FrameHolds = [1f, 1f, 3f];
        Check("a hold on the last frame stretches the tail", Math.Abs(clip.Duration - 0.5f) < 1e-4f);
        Check("last frame is still reached", clip.GetFrameAtTime(0.48f) == 2);
    }

    // ── E ────────────────────────────────────────────────────────────────────────────

    private static void StandardSetPresets()
    {
        Console.WriteLine("\n== E. standard set presets ==\n");

        var actions = AnimationSetPreset.Defaults.Select(p => p.Action).ToList();
        Check("has the core actions",
            new[] { "idle", "walk", "run", "attack", "dead" }.All(actions.Contains));
        Check("attack is one-shot", AnimationSetPreset.Defaults
            .Single(p => p.Action == "attack").Loop == false);
        Check("dead is one-shot", AnimationSetPreset.Defaults
            .Single(p => p.Action == "dead").Loop == false);
        Check("attack authors a Hit event", AnimationSetPreset.Defaults
            .Single(p => p.Action == "attack").EventName == "Hit");
        Check("all actions are lowercase (equipment layer naming)",
            actions.All(a => a == a.ToLowerInvariant()));
        Check("event position is inside the clip",
            AnimationSetPreset.Defaults.Where(p => p.EventName != null)
                .All(p => p.EventAtPercent >= 0f && p.EventAtPercent <= 1f));

        string clipName = $"Hero-{AnimationSetPreset.Defaults[0].Action}";
        Check("clip naming stays '<sheet>-<action>'", clipName == "Hero-idle");
    }

    // ── F ────────────────────────────────────────────────────────────────────────────

    private static void RigsTracksBakeRoundTrip()
    {
        Console.WriteLine("\n== F. rig, tracks and bake settings survive save/load ==\n");

        var rig = new AnimationRig { Id = "rig_hero", Name = "Hero", DesignHeightPx = 64f };
        rig.Bones.Add(new Bone2D { Name = "root" });
        rig.Bones.Add(new Bone2D { Name = "torso", Parent = "root", Rotation = 10f, ScaleY = 1.2f });
        rig.Slots.Add(new Slot2D { Name = "body", Bone = "torso", Attachment = "body" });
        rig.Attachments.Add(new Attachment2D
        {
            Name = "body", Slot = "body", SheetName = "hero", FrameIndex = 3,
            PivotX = 0.5f, PivotY = 1f,
        });
        rig.Skins.Add(new Skin2D { Name = "knight" });
        rig.Skins[0].AttachmentBySlot["body"] = "armor";

        var clip = new AnimationClip2D
        {
            Name = "hero-idle",
            Mode = AnimationClipMode.Skeleton,
            RigId = "rig_hero",
            Tracks =
            [
                new AnimationTrack
                {
                    Kind = AnimationTrackKind.BoneRotate,
                    Target = "torso",
                    Keys =
                    [
                        new AnimationKey { Time = 0f, Value = 0f },
                        new AnimationKey { Time = 0.5f, Value = 45f, Interp = AnimationKeyInterp.Stepped },
                    ],
                },
            ],
            Bake = new BakeSettings { Fps = 12f, TargetAtlas = 2, LastBakeHash = "hash123" },
        };
        string authoredId = clip.Id;

        var save = new SpriteSheetsSaveData { Rigs = [rig], AnimationClips = [clip.ToData()] };
        string json = JsonSerializer.Serialize(save, Opts);
        var loaded = JsonSerializer.Deserialize<SpriteSheetsSaveData>(json)!;

        Check("rig survives", loaded.Rigs.Count == 1);
        var r = loaded.Rigs[0];
        Check("rig id/name/height survive", r.Id == "rig_hero" && r.Name == "Hero" && r.DesignHeightPx == 64f);
        Check("bone hierarchy survives",
            r.Bones.Count == 2 && r.Bones[1].Parent == "root" && r.Bones[1].Rotation == 10f && r.Bones[1].ScaleY == 1.2f);
        Check("slot survives", r.Slots.Count == 1 && r.Slots[0].Bone == "torso" && r.Slots[0].Visible);
        Check("region attachment survives",
            r.Attachments.Count == 1 && r.Attachments[0].SheetName == "hero" && r.Attachments[0].FrameIndex == 3
            && r.Attachments[0].PivotX == 0.5f && r.Attachments[0].PivotY == 1f);
        Check("skin overrides survive", r.Skins.Count == 1 && r.Skins[0].AttachmentBySlot["body"] == "armor");

        var back = AnimationClip2D.FromData(loaded.AnimationClips[0]);
        Check("clip id survives", back.Id == authoredId);
        Check("skeleton mode + rig link survive", back.Mode == AnimationClipMode.Skeleton && back.RigId == "rig_hero");
        Check("track survives", back.Tracks is { Count: 1 });
        Check("track kind/target survive",
            back.Tracks![0].Kind == AnimationTrackKind.BoneRotate && back.Tracks[0].Target == "torso");
        Check("keys survive (time, value, interp)",
            back.Tracks[0].Keys.Count == 2 && back.Tracks[0].Keys[1].Time == 0.5f
            && back.Tracks[0].Keys[1].Value == 45f && back.Tracks[0].Keys[1].Interp == AnimationKeyInterp.Stepped);
        Check("bake settings survive", back.Bake != null && back.Bake.Fps == 12f
            && back.Bake.TargetAtlas == 2 && back.Bake.LastBakeHash == "hash123");
        Check("enums serialize as strings (readable diffs)",
            json.Contains("\"Skeleton\"") && json.Contains("\"BoneRotate\"") && json.Contains("\"Stepped\""));

        // Mutating the loaded copy must not touch the original (deep clone, not aliasing).
        r.Bones[1].Rotation = 90f;
        back.Tracks[0].Keys[1].Value = 100f;
        Check("loaded rig/track are deep copies",
            rig.Bones[1].Rotation == 10f && clip.Tracks![0].Keys[1].Value == 45f);
    }

    // ── G ────────────────────────────────────────────────────────────────────────────

    private static void LegacyPhase0Defaults()
    {
        Console.WriteLine("\n== G. legacy files leave Phase 0 fields at their defaults ==\n");

        const string legacyJson = """
        {
          "AnimationClips": [
            { "Name": "Idle", "SpriteSheetName": "Hero-atlas", "FrameIndices": [0, 1], "FPS": 12 }
          ]
        }
        """;

        var loaded = JsonSerializer.Deserialize<SpriteSheetsSaveData>(legacyJson)!;
        Check("no rigs invented", loaded.Rigs.Count == 0);
        Check("no templates invented", loaded.AnimationTemplates.Count == 0);

        var clip = AnimationClip2D.FromData(loaded.AnimationClips[0]);
        Check("legacy clip gets a minted id", !string.IsNullOrEmpty(clip.Id));
        Check("legacy clip has no rig", string.IsNullOrEmpty(clip.RigId));
        Check("legacy clip has no tracks", clip.Tracks == null);
        Check("legacy clip has no bake settings", clip.Bake == null);

        // The minted id must be stable from here on (it is what the editor links by).
        var save = new SpriteSheetsSaveData { AnimationClips = [clip.ToData()] };
        var reloaded = JsonSerializer.Deserialize<SpriteSheetsSaveData>(
            JsonSerializer.Serialize(save, Opts))!;
        Check("minted id survives the next save", AnimationClip2D.FromData(reloaded.AnimationClips[0]).Id == clip.Id);
    }

    // ── H ────────────────────────────────────────────────────────────────────────────

    private static void TemplatesRoundTrip()
    {
        Console.WriteLine("\n== H. animation set templates round-trip ==\n");

        var save = new SpriteSheetsSaveData
        {
            AnimationTemplates = AnimationSetPreset.Defaults.Select(t => t.Clone()).ToList(),
        };
        string json = JsonSerializer.Serialize(save, Opts);
        var loaded = JsonSerializer.Deserialize<SpriteSheetsSaveData>(json)!;

        Check("all templates survive", loaded.AnimationTemplates.Count == AnimationSetPreset.Defaults.Length);
        for (int i = 0; i < AnimationSetPreset.Defaults.Length; i++)
        {
            var want = AnimationSetPreset.Defaults[i];
            var got = loaded.AnimationTemplates[i];
            if (want.Action != got.Action || want.Fps != got.Fps || want.Loop != got.Loop
                || want.EventName != got.EventName || want.Label != got.Label)
            {
                Check($"template '{want.Action}' survives unchanged", false);
                return;
            }
        }
        Check("every template survives unchanged", true);
        Check("attack keeps its Hit event",
            loaded.AnimationTemplates.Single(t => t.Action == "attack").EventName == "Hit");
    }

    // ── I ────────────────────────────────────────────────────────────────────────────

    private static void TrackEvaluation()
    {
        Console.WriteLine("\n== I. track key evaluation ==\n");

        var track = new AnimationTrack
        {
            Kind = AnimationTrackKind.BoneRotate,
            Target = "arm",
            Keys =
            [
                new AnimationKey { Time = 0f, Value = 0f },
                new AnimationKey { Time = 1f, Value = 90f },
                // Interp lives on the START key of a segment (Spine convention): this
                // stepped key makes [2s → 3s] hold at 90 instead of ramping to 180.
                new AnimationKey { Time = 2f, Value = 90f, Interp = AnimationKeyInterp.Stepped },
                new AnimationKey { Time = 3f, Value = 180f },
            ],
        };

        Check("before the first key clamps", track.Evaluate(-1f) == 0f);
        Check("mid-key is linear", Math.Abs(track.Evaluate(0.5f) - 45f) < 1e-4f);
        Check("key time hits the key", Math.Abs(track.Evaluate(1f) - 90f) < 1e-4f);
        Check("flat segment stays flat", Math.Abs(track.Evaluate(1.5f) - 90f) < 1e-4f);
        Check("stepped holds the previous value", Math.Abs(track.Evaluate(2.5f) - 90f) < 1e-4f);
        Check("after the last key clamps", track.Evaluate(99f) == 180f);
        Check("empty track evaluates to 0", new AnimationTrack().Evaluate(0.5f) == 0f);
    }

    // ── J ────────────────────────────────────────────────────────────────────────────

    private static void LayeredClipRoundTrip()
    {
        Console.WriteLine("\n== J. Phase 3 layered-bake clip list survives save/load ==\n");

        var body = new AnimationClip2D
        {
            Name = "Hero-run",
            SpriteSheetName = "mainsprite1",
            FrameIndices = [0, 1, 2, 3],
            SourceSheetName = "Hero",
            SourceFrameIndices = [0, 1, 2, 3],
            // back→front: hair behind the weapon
            LayerClipNames = ["Hero-hair-run", "Hero-sword-run"],
        };
        Check("a clip reports its layers", body.HasLayerClips);
        Check("layer list keeps its order (draw order)",
            body.LayerClipNames.SequenceEqual(["Hero-hair-run", "Hero-sword-run"]));

        var save = new SpriteSheetsSaveData { AnimationClips = [body.ToData()] };
        var loaded = JsonSerializer.Deserialize<SpriteSheetsSaveData>(
            JsonSerializer.Serialize(save, Opts))!;
        var back = AnimationClip2D.FromData(loaded.AnimationClips[0]);
        Check("layer clip names survive the round-trip, in order",
            back.LayerClipNames.SequenceEqual(["Hero-hair-run", "Hero-sword-run"]));
        Check("layers do not disturb the baked strip", back.FrameIndices.SequenceEqual([0, 1, 2, 3]));
        Check("a layered clip still rebakes from its source strip", back.CanRebake);

        // The DTO list must be a COPY: editing the reloaded clip must not mutate the
        // source data (undo snapshots serialize the same DTO instances).
        loaded.AnimationClips[0].LayerClipNames!.Clear();
        Check("the reloaded list is an independent copy",
            back.LayerClipNames.Count == 2);

        // Legacy clips carry no layers and must stay valid single-layer clips.
        var legacy = AnimationClip2D.FromData(new AnimationClip2DData
        {
            Name = "Old-idle",
            SpriteSheetName = "mainsprite1",
            FrameIndices = [0],
        });
        Check("a legacy clip loads with no layers", legacy.LayerClipNames.Count == 0);
        Check("a legacy clip is a valid single-layer clip", !legacy.HasLayerClips);
    }

    // ── K ────────────────────────────────────────────────────────────────────────────

    private static void SkeletonClipSemantics()
    {
        Console.WriteLine("\n== K. skeleton clip semantics ==\n");

        // A rig clip has no source strip, so CanRebake stays false — but it IS bakeable.
        var rigClip = new AnimationClip2D
        {
            Name = "hero-run-rig",
            Mode = AnimationClipMode.Skeleton,
            RigId = "rig-1",
            FrameIndices = [3, 4, 5, 6],
            FPS = 12f,
        };
        Check("a rig clip is a skeleton clip", rigClip.IsSkeleton);
        Check("a rig clip cannot rebake from a strip", !rigClip.CanRebake);
        Check("a rig clip can still be baked", rigClip.CanBake);
        Check("a skeleton clip without a rig id is not a rig clip",
            !new AnimationClip2D { Mode = AnimationClipMode.Skeleton }.IsSkeleton);
        Check("a sprite clip keeps its old bake rule",
            new AnimationClip2D { SourceSheetName = "s", SourceFrameIndices = [0] }.CanBake
            && !new AnimationClip2D { SourceSheetName = "s", SourceFrameIndices = [0] }.IsSkeleton);

        // Events are frame-keyed: a bake that changes the sample count must move them
        // proportionally instead of letting them drift or fall off the strip.
        var events = new AnimationClip2D { FrameIndices = Enumerable.Range(0, 10).ToList() };
        events.Events[0] = "Start";
        events.Events[6] = "Hit";
        events.Events[9] = "End";
        events.RemapEventsToFrameCount(21);
        Check("the first event stays at the start", events.Events.TryGetValue(0, out var start) && start == "Start");
        Check("60% stays at 60%", events.Events.TryGetValue(13, out var hit) && hit == "Hit");
        Check("the last event lands on the last frame",
            events.Events.TryGetValue(20, out var end) && end == "End");
        Check("no event is silently dropped", events.Events.Count == 3);

        var shrunk = new AnimationClip2D { FrameIndices = Enumerable.Range(0, 21).ToList() };
        shrunk.Events[20] = "End";
        shrunk.RemapEventsToFrameCount(4);
        Check("shrinking clamps into the new strip",
            shrunk.Events.Count == 1 && shrunk.Events.ContainsKey(3));
        Check("a single-frame strip maps everything to frame 0",
            SingleFrameEventTarget());

        // BakeSettings grew two skeleton fields — they must round-trip like the rest.
        var save = new SpriteSheetsSaveData
        {
            AnimationClips =
            [
                new AnimationClip2D
                {
                    Name = "hero-run-rig",
                    Mode = AnimationClipMode.Skeleton,
                    RigId = "rig-1",
                    Bake = new BakeSettings { Fps = 12f, Skin = "knight", DurationSeconds = 0.75f },
                }.ToData(),
            ],
        };
        var loaded = JsonSerializer.Deserialize<SpriteSheetsSaveData>(
            JsonSerializer.Serialize(save, Opts))!;
        var back = AnimationClip2D.FromData(loaded.AnimationClips[0]);
        Check("the bake skin survives save/load", back.Bake?.Skin == "knight");
        Check("the cycle length survives save/load", Math.Abs((back.Bake?.DurationSeconds ?? 0f) - 0.75f) < 1e-4f);
        Check("the rig link survives save/load", back.RigId == "rig-1" && back.IsSkeleton);
    }

    private static bool SingleFrameEventTarget()
    {
        var clip = new AnimationClip2D { FrameIndices = [0] };
        clip.Events[0] = "Only";
        clip.RemapEventsToFrameCount(1);
        return clip.Events.Count == 1 && clip.Events.ContainsKey(0);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────

    private static void Check(string what, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {what}");
        if (!ok) _fails++;
    }
}
