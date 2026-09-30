using System;
using System.Collections.Generic;
using System.Linq;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// WORLD-STATE JOURNAL — remembers what the player already did to the world so
/// leaving and re-entering a map does not reset it:
///   • trigger areas: "Skip When Done" applied (chest looted), one-way portal vanished
///   • sprite swaps: the Change Sprite end-state (chest stays OPEN) with the loop mode
/// Keyed by map name + trigger name (triggers) and object name (sprites), so the
/// journal survives map switches (Change Map) and is re-applied by
/// <see cref="TriggerEventSystem.ResetRuntime"/> after every reload.
/// A fresh session (<see cref="TriggerEventSystem.BeginSession"/>) clears it — new
/// run, world resets. Full game saves capture it via CaptureState/RestoreState
/// (SaveData.WorldTriggerStates / WorldSpriteStates), so loading a save restores the
/// exact world state too.
/// </summary>
public static class WorldStateJournal
{
    /// <summary>One trigger's persisted runtime bits.</summary>
    public sealed class TriggerState
    {
        public bool Applied;  // Skip-When-Done guard: the fire already happened
        public bool Hidden;   // one-way portal vanished after use
    }

    /// <summary>One object's persisted Change Sprite end-state.</summary>
    public sealed class SpriteState
    {
        public string Sheet = "";
        public string Clip = "";
        public bool Loop = true;
        public string Source = ""; // original trigger name (keeps idempotency stable)
    }

    private static readonly Dictionary<string, TriggerState> _triggers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, SpriteState> _sprites = new(StringComparer.OrdinalIgnoreCase);

    // ── Recording ────────────────────────────────

    private static string TriggerKey(string mapName, string triggerName) => $"{mapName}|{triggerName}";

    /// <summary>Record a trigger's applied/hidden flags (called when they change).</summary>
    public static void RecordTrigger(string? mapName, string triggerName, bool applied, bool hidden)
    {
        if (string.IsNullOrWhiteSpace(mapName) || string.IsNullOrWhiteSpace(triggerName)) return;
        _triggers[TriggerKey(mapName, triggerName)] = new TriggerState { Applied = applied, Hidden = hidden };
    }

    /// <summary>Record a Change Sprite end-state for an object (called after every swap).</summary>
    public static void RecordSprite(string objName, string sheet, string clip, bool loop, string source)
    {
        if (string.IsNullOrWhiteSpace(objName) || string.IsNullOrWhiteSpace(sheet) || string.IsNullOrWhiteSpace(clip)) return;
        _sprites[objName] = new SpriteState { Sheet = sheet, Clip = clip, Loop = loop, Source = source };
    }

    /// <summary>A Revert dropped the object's swap — forget its journal entry.</summary>
    public static void ClearSprite(string objName) => _sprites.Remove(objName);

    /// <summary>Fresh run: the world resets (called from BeginSession).</summary>
    public static void Clear()
    {
        _triggers.Clear();
        _sprites.Clear();
    }

    // ── Re-applying (after every ResetRuntime / load) ────────────────────────────────

    /// <summary>Restore trigger runtime flags for ONE map from the journal (call after
    /// ResetRuntime zeroed them — map reload must not un-loot a chest).</summary>
    public static void ApplyToMap(Tilemap2D? map)
    {
        if (map == null) return;
        foreach (var t in map.TriggerAreas)
        {
            if (t == null) continue;
            if (!_triggers.TryGetValue(TriggerKey(map.Name, t.Name), out var st)) continue;
            if (st.Applied) t.RuntimeApplied = true;
            if (st.Hidden) t.RuntimeHidden = true;
        }
    }

    /// <summary>Restore every journaled sprite end-state onto the live objects
    /// (idempotent — same sheet/clip/source keeps the anim clock running).</summary>
    public static void ApplySprites(DarkEngine3D_gl_csharp.Engine.Objects.EditorObjectManager? mgr)
    {
        if (mgr == null || _sprites.Count == 0) return;
        foreach (var o in mgr.Objects)
        {
            if (o == null || !_sprites.TryGetValue(o.Name, out var st)) continue;
            o.SetSpriteStateOverride(st.Sheet, st.Clip, st.Source, st.Loop);
        }
    }

    // ── Save / load (SaveData lists) ────────────────────────────────

    /// <summary>Serialize to save-slot friendly strings:
    /// triggers "map|trigger|applied|hidden", sprites "name|sheet|clip|loop|source".</summary>
    public static (List<string> triggers, List<string> sprites) CaptureState()
    {
        var trig = _triggers.Select(kv =>
            $"{kv.Key}|{(kv.Value.Applied ? 1 : 0)}|{(kv.Value.Hidden ? 1 : 0)}").ToList();
        var spr = _sprites.Select(kv =>
            $"{kv.Key}|{kv.Value.Sheet}|{kv.Value.Clip}|{(kv.Value.Loop ? 1 : 0)}|{kv.Value.Source}").ToList();
        return (trig, spr);
    }

    /// <summary>Restore from save data (call BEFORE ApplyToMap/ApplySprites).</summary>
    public static void RestoreState(List<string>? triggers, List<string>? sprites)
    {
        _triggers.Clear();
        _sprites.Clear();
        if (triggers != null)
        {
            foreach (var s in triggers)
            {
                var p = s.Split('|');
                if (p.Length < 4) continue;
                _triggers[p[0] + "|" + p[1]] = new TriggerState
                { Applied = p[2] == "1", Hidden = p[3] == "1" };
            }
        }
        if (sprites != null)
        {
            foreach (var s in sprites)
            {
                var p = s.Split('|');
                if (p.Length < 5) continue;
                _sprites[p[0]] = new SpriteState
                { Sheet = p[1], Clip = p[2], Loop = p[3] == "1", Source = p[4] };
            }
        }
    }

    /// <summary>Diagnostic dump.</summary>
    public static void LogSummary()
    {
        Console.WriteLine($"[WorldState] journal: {_triggers.Count} trigger(s), {_sprites.Count} sprite state(s) — " +
            string.Join(", ", _triggers.Keys.Select(k => k + ( _triggers[k].Applied ? "✓" : "" )).Take(6)));
    }
}
