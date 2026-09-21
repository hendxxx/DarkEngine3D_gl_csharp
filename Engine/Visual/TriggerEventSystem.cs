using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Scene;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Project;
using System.IO;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Gameplay trigger-area runtime for 2D levels. Unlike collision tiles (which block
/// the player), trigger areas are PASS-THROUGH volumes: the player walks through them
/// and crossing their boundary fires designer-authored actions.
///
/// Detection runs inside Player2DSystem.Update (same cadence as physics) against the
/// player's capsule AABB. Conditions:
///   • OnEnter — fires once the moment the capsule starts overlapping.
///   • OnStay  — fires every OnStayIntervalSeconds while overlapping (0 = disabled).
///   • OnExit  — fires once the moment the capsule stops overlapping.
///
/// Actions dispatch through ExecuteAction; Save Game / Load Checkpoint / Change Map /
/// Camera Shake are wired to real engine systems, the remaining catalog entries log
/// a single "not wired" line so authored triggers never fail silently.
/// </summary>
public static class TriggerEventSystem
{
    /// <summary>Singleton runtime state holder — the IDE creates one and hands it to
    /// Player2DSystem each update so editor and gameplay share the same instance.</summary>
    public static TriggerRuntimeState State { get; } = new();

    /// <summary>Set by the IDE so the "Change Map" action can load another .tilemap.json
    /// from the project's Assets/Maps folder (and swap the scene's Map2D object).</summary>
    public static Func<string, bool>? OnChangeMap { get; set; }

    /// <summary>Set by the IDE so "Camera Shake" can reach the active camera without
    /// the trigger runtime knowing camera internals. Parameter = duration seconds.</summary>
    public static Action<float>? OnCameraShake { get; set; }

    /// <summary>Sets the intensity (jolt size multiplier) of the NEXT/active camera
    /// shake. Kept separate from OnCameraShake so the camera API stays simple.</summary>
    public static Action<float>? RequestShakeIntensity { get; set; }

    /// <summary>Remembered checkpoint world position (feet anchor). Set by the
    /// "Save Checkpoint" action; consumed by the "Load Checkpoint" action and by the
    /// pit-respawn path. Null = no checkpoint yet → respawn/teleport falls back to
    /// the level's Start2D marker (start point).</summary>
    public static Vector2? CheckpointPosition { get; set; }
    /// <summary>Checkpoint world Z (players live on the map plane at z≈0, but keep it
    /// exact for the teleport).</summary>
    public static float CheckpointZ { get; set; }

    /// <summary>Read the checkpoint from the most recent save slot. "Save Game" stores
    /// the player position at save time — that IS the checkpoint the designer wants
    /// "Load Checkpoint" to restore after a restart. Returns false when no valid
    /// save exists (then the caller falls back to the Start2D start point).
    /// The saved object position is converted to CAPSULE space (feet/capsule-center)
    /// with the live offsets, then ground-snapped so the restored feet ALWAYS rest on
    /// top of a collision tile — never embedded in one, never floating.</summary>
    private static bool TryLoadCheckpointFromSaves(Tilemap2D? map, float capOffX, float capOffY,
        out Vector2 position, out float z)
    {
        position = default;
        z = 0f;
        int slot = SaveManager.GetLatestSlot();
        if (slot < 0) return false;
        var data = SaveManager.Load(slot);
        if (data == null) return false;

        // Save Game writes the raw OBJECT position → convert to capsule space.
        float capsuleX = data.PlayerX + capOffX;
        float feetY = SnapFeetToGround(map, capsuleX, data.PlayerY + capOffY);
        position = new Vector2(capsuleX, feetY);
        z = data.PlayerZ;
        return true;
    }

    /// <summary>Drop a capsule-space feet Y onto the collision surface below it: walk
    /// the collision tiles of every layer down from the feet row and land the feet on
    /// the TOP edge of the first solid tile (plus a hair above it, matching the
    /// physics SkinWidth). Uses the same grid→world mapping as the physics (row ty
    /// spans world Y [(mapH-1-ty)*cell, (mapH-ty)*cell], top edge = (mapH-ty)*cell).
    /// Feet resting exactly on a surface stay put; feet inside a tile pop to its top.
    /// Public: the pit-respawn path in Player2DSystem uses the same snap so a respawned
    /// player always lands ON TOP of collision (never below the map again).</summary>
    public static float SnapFeetToGroundPublic(Tilemap2D? map, float worldX, float feetY)
        => SnapFeetToGround(map, worldX, feetY);

    private static float SnapFeetToGround(Tilemap2D? map, float worldX, float feetY)
    {
        if (map == null || map.Layers.Count == 0) return feetY;
        float cell = map.TileSize * Tilemap2D.WorldScale;
        if (cell <= 0f) return feetY;

        int gy = (int)MathF.Floor((map.Height * cell - feetY) / cell); // feet row (WorldRowFloor)
        int gx = (int)MathF.Floor(worldX / cell);
        if (gx < 0 || gx >= map.Width) return feetY;
        if (gy < 0) gy = 0; // above the map: scan from the top row down

        for (; gy < map.Height; gy++)
        {
            foreach (var layer in map.Layers)
            {
                if (layer == null) continue;
                int tile = layer.GetTile(gx, gy);
                if (tile < 0 || !layer.TileHasCollision(tile)) continue;
                // Solid tile at/below the feet: land the feet on its TOP edge + ε so
                // the capsule sits just above the collision surface (never inside it).
                return (map.Height - gy) * cell + 0.001f; // TileWorldMaxY(map, gy, cell)
            }
        }
        return feetY; // nothing solid below — keep the height (airborne checkpoint)
    }

    /// <summary>Track which objects already logged a "not wired" message so a Stay
    /// trigger can't spam the console every frame.</summary>
    private static readonly HashSet<string> _warned = new();

    /// <summary>
    /// Evaluate all trigger areas of the map against one player. Call once per frame
    /// per player (from Player2DSystem.Update, preview/in-game only).
    /// </summary>
    public static void Update(Tilemap2D? map, Vector3 playerFeetPos, float radius, float height, float dt,
        float playerVelX = 0f, float capsuleOffsetX = 0f, float capsuleOffsetY = 0f)
    {
        // Keep save-slot checkpoint loading able to ground-snap against the live map.
        _activeMap = map;
        _liveCapsuleOffsetX = capsuleOffsetX;
        _liveCapsuleOffsetY = capsuleOffsetY;

        if (map == null || map.TriggerAreas.Count == 0) return;

        // Player capsule AABB in world space (feet-anchored, same convention as physics).
        // A thin vertical pad (≈ the capsule radius) is added BELOW the feet: a feet-anchored
        // box test otherwise has zero depth at the ground line, so a capsule standing ON a
        // trigger box (or a box resized to exactly the player's height) reads "not inside".
        // The pad keeps box-height ↔ capsule-height alignment forgiving at the ground seam.
        float pMinX = playerFeetPos.X - radius;
        float pMaxX = playerFeetPos.X + radius;
        float pMinY = playerFeetPos.Y - radius * 0.5f;
        float pMaxY = playerFeetPos.Y + height;

        float cell = map.TileSize * Tilemap2D.WorldScale;
        if (cell <= 0f) return;

        foreach (var trigger in map.TriggerAreas)
        {
            if (trigger == null) continue;
            if (trigger.RuntimeCooldown > 0f)
                trigger.RuntimeCooldown = MathF.Max(0f, trigger.RuntimeCooldown - dt);

            bool wasInside = trigger.RuntimePlayerInside;
            bool inside = false;

            if (trigger.IsEnabled && trigger.WidthPx > 0f && trigger.HeightPx > 0f)
            {
                // Pixel rect → world AABB. TopPx counts DOWN from the map's top edge
                // (same convention as tile row 0 = top), so:
                //   world top Y    = mapHeight*cell - TopPx
                //   world bottom Y = mapHeight*cell - (TopPx + HeightPx)
                float worldLeft = trigger.LeftPx * Tilemap2D.WorldScale;
                float worldRight = (trigger.LeftPx + trigger.WidthPx) * Tilemap2D.WorldScale;
                float mapHWorld = map.Height * cell;
                float worldTop = mapHWorld - trigger.TopPx * Tilemap2D.WorldScale;
                float worldBottom = mapHWorld - (trigger.TopPx + trigger.HeightPx) * Tilemap2D.WorldScale;

                inside = pMaxX > worldLeft && pMinX < worldRight
                      && pMaxY > worldBottom && pMinY < worldTop;
            }

            bool fired = false;

            if (wasInside && !inside)
            {
                // ── On Exit ──
                trigger.RuntimePlayerInside = false;
                if (trigger.IsEnabled && trigger.OnExit && trigger.RuntimeCooldown <= 0f)
                {
                    Fire(trigger, "OnExit");
                    fired = true;
                }
            }
            else if (!wasInside && inside)
            {
                // ── On Enter ──
                trigger.RuntimePlayerInside = true;
                trigger.RuntimeStayTimer = trigger.OnStayIntervalSeconds;
                // RequireMovingRight gate: door-style triggers only fire when the player
                // is actually heading INTO the area (rightward). Backtracking ignores it.
                bool gateOk = !trigger.RequireMovingRight || playerVelX > 0.1f;
                if (trigger.IsEnabled && trigger.OnEnter && gateOk && trigger.RuntimeCooldown <= 0f)
                {
                    Fire(trigger, "OnEnter");
                    fired = true;
                }
            }
            else if (inside)
            {
                // ── On Stay: repeating interval while overlapping ──
                if (trigger.IsEnabled && trigger.OnStayIntervalSeconds > 0f)
                {
                    trigger.RuntimeStayTimer -= dt;
                    if (trigger.RuntimeStayTimer <= 0f)
                    {
                        trigger.RuntimeStayTimer = trigger.OnStayIntervalSeconds;
                        if (trigger.RuntimeCooldown <= 0f)
                        {
                            Fire(trigger, "OnStay");
                            fired = true;
                        }
                    }
                }
            }

            if (fired)
                trigger.RuntimeCooldown = 0.1f; // tiny guard so one boundary event can't double-fire
        }
    }

    /// <summary>Reset runtime state for all triggers (called on session start / map
    /// load so OnEnter fires again the first time through).</summary>
    public static void ResetRuntime(Tilemap2D? map)
    {
        if (map == null) return;
        foreach (var t in map.TriggerAreas)
        {
            if (t == null) continue;
            t.RuntimePlayerInside = false;
            t.RuntimeStayTimer = 0f;
            t.RuntimeCooldown = 0f;
        }
    }

    private static void Fire(TilemapTriggerArea trigger, string condition)
    {
        Console.WriteLine($"[Trigger] '{trigger.Name}' fired ({condition}) — {trigger.Actions.Count} action(s)");
        foreach (var action in trigger.Actions)
        {
            if (action == null) continue;
            if (action.Delay > 0f)
            {
                // Delayed actions queue onto the runtime state; executed by TickDelayed.
                State.QueueDelayed(action, trigger.Name, condition);
            }
            else
            {
                ExecuteAction(action, trigger.Name);
            }
        }
    }

    /// <summary>Execute queued delayed actions whose timers elapsed. Call once per frame.</summary>
    public static void TickDelayed(float dt)
    {
        State.Tick(dt);
    }

    private sealed class PendingAction
    {
        public TilemapTriggerAction Action = null!;
        public string TriggerName = "";
        public float Remaining;
    }

    /// <summary>Runtime-only holder for delayed action queue. Kept separate from the
    /// map data so nothing runtime leaks into save files.</summary>
    public sealed class TriggerRuntimeState
    {
        private readonly List<PendingAction> _pending = new();

        public void QueueDelayed(TilemapTriggerAction action, string triggerName, string condition)
        {
            _pending.Add(new PendingAction { Action = action, TriggerName = triggerName, Remaining = action.Delay });
        }

        public void Tick(float dt)
        {
            if (_pending.Count == 0) return;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                p.Remaining -= dt;
                if (p.Remaining <= 0f)
                {
                    _pending.RemoveAt(i);
                    ExecuteAction(p.Action, p.TriggerName);
                }
            }
        }

        public void Clear() => _pending.Clear();
    }

    /// <summary>Dispatch one trigger action. Wired actions call real engine systems;
    /// the rest log once so the designer knows the trigger fired but the action has
    /// no gameplay implementation yet.</summary>
    public static void ExecuteAction(TilemapTriggerAction action, string triggerName)
    {
        switch (action.Type)
        {
            case TriggerActionTypes.SaveGame:
            {
                // Save into the next empty slot (falls back to slot 0 when full) and
                // reuse the engine's screenshot capture path via SaveManager directly.
                int slot = SaveManager.GetNextEmptySlot();
                if (slot < 0) slot = 0;
                SaveData data = new()
                {
                    PlayerX = _lastPlayerPos?.X ?? 0f,
                    PlayerY = _lastPlayerPos?.Y ?? 0f,
                    PlayerZ = _lastPlayerPos?.Z ?? 0f,
                    SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                SaveManager.Save(slot, data);
                Console.WriteLine($"[Trigger] '{triggerName}' → Save Game → slot {slot + 1} at ({data.PlayerX:F1}, {data.PlayerY:F1})");
                break;
            }

            case TriggerActionTypes.SaveCheckpoint:
            {
                // Record the player's position as the checkpoint anchor (OBJECT space,
                // same convention as the pit-respawn and teleport paths — capsule
                // offsets are applied exactly once by whoever consumes the value).
                if (_lastPlayerPos.HasValue)
                {
                    CheckpointPosition = new Vector2(_lastPlayerPos.Value.X, _lastPlayerPos.Value.Y);
                    CheckpointZ = _lastPlayerPos.Value.Z;
                    Console.WriteLine($"[Trigger] '{triggerName}' → Save Checkpoint at ({CheckpointPosition.Value.X:F1}, {CheckpointPosition.Value.Y:F1})");
                }
                else
                    Console.WriteLine($"[Trigger] '{triggerName}' → Save Checkpoint FAILED: no player position known");
                break;
            }

            case TriggerActionTypes.LoadCheckpoint:
            {
                // Teleport the player to the checkpoint: session-saved → latest save
                // slot → Start2D fallback (always meaningful, even on a fresh project).
                // EVERYTHING here resolves to FEET space and OnTeleportPlayer subtracts
                // the capsule offsets once — passing object-space values through the same
                // handler double-applies the offset (dropped the player below the map).
                if (OnTeleportPlayer != null)
                {
                    // 1) Fresh checkpoint saved this session → teleport there (re-snapped
                    //    to the CURRENT collision in case the level changed since).
                    // 2) No session checkpoint → load from the LATEST save slot (Save
                    //    Game persists it), capsule-converted + ground-snapped.
                    // 3) Nothing anywhere → fall back to the level's Start2D marker.
                    Vector2 target;
                    float z;
                    string source;
                    if (CheckpointPosition.HasValue)
                    {
                        // CheckpointPosition is OBJECT space → convert to feet space once.
                        float feetX = CheckpointPosition.Value.X + _liveCapsuleOffsetX;
                        float feetY = SnapFeetToGround(_activeMap, feetX,
                            CheckpointPosition.Value.Y + _liveCapsuleOffsetY);
                        target = new Vector2(feetX, feetY);
                        z = CheckpointZ;
                        source = "(session checkpoint)";
                    }
                    else if (TryLoadCheckpointFromSaves(_activeMap, _liveCapsuleOffsetX, _liveCapsuleOffsetY, out var saved, out float savedZ))
                    {
                        target = saved; // already capsule-space feet + ground-snapped
                        z = savedZ;
                        source = "(last saved checkpoint — from save slot)";
                        CheckpointPosition = saved; // keep in memory for pit respawn too
                        CheckpointZ = savedZ;
                    }
                    else
                    {
                        target = _fallbackSpawn; // Start2D marker is feet-anchored already
                        z = CheckpointZ;
                        source = "(start point — no checkpoint saved yet)";
                    }
                    OnTeleportPlayer(target, z);
                    Console.WriteLine($"[Trigger] '{triggerName}' → Load Checkpoint → teleport to ({target.X:F1}, {target.Y:F1}) {source}");
                }
                else
                    Console.WriteLine($"[Trigger] '{triggerName}' → Load Checkpoint skipped: no teleport handler registered");
                break;
            }

            case TriggerActionTypes.ChangeMap:
            {
                string mapName = action.Param?.Trim() ?? "";
                if (string.IsNullOrEmpty(mapName))
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Change Map FAILED: no map name set in the action's Param");
                    break;
                }
                if (OnChangeMap != null)
                {
                    bool ok = OnChangeMap(mapName);
                    Console.WriteLine($"[Trigger] '{triggerName}' → Change Map → '{mapName}' {(ok ? "OK" : "FAILED (file not found or load error)")}");
                }
                else
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Change Map skipped: no OnChangeMap handler registered");
                }
                break;
            }

            case TriggerActionTypes.CameraShake:
            {
                float.TryParse(action.Param, out float intensity);
                float.TryParse(action.Param2, out float duration);
                if (duration <= 0f) duration = 0.4f;
                if (intensity <= 0f) intensity = 1f;
                // Earthquake shake: duration sets how long, intensity scales the jolt
                // size (the camera scales the offset to the current view size, so even
                // a mild intensity reads big on screen).
                OnCameraShake?.Invoke(duration);
                RequestShakeIntensity?.Invoke(intensity);
                Console.WriteLine($"[Trigger] '{triggerName}' → Camera Shake (intensity {intensity}, duration {duration:F2}s)");
                break;
            }

            case TriggerActionTypes.StartDialogue:
            {
                // Dialogue asset id/name in Param; optional start node in Param2 (node id
                // or numeric node index — e.g. "0" = first node). Opens the RPG
                // conversation window (drawn through the HUD — works in-game and preview).
                string assetId = action.Param?.Trim() ?? "";
                if (string.IsNullOrEmpty(assetId))
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Start Dialogue FAILED: no dialogue asset id set in the action's Param");
                    break;
                }
                string node = action.Param2?.Trim() ?? "";
                bool ok = !string.IsNullOrEmpty(node)
                    ? DialogueSystem.StartConversationAt(assetId, node)
                    : DialogueSystem.StartConversation(assetId);
                if (ok)
                    Console.WriteLine($"[Trigger] '{triggerName}' → Start Dialogue → '{assetId}'{(node.Length > 0 ? $" @ node '{node}'" : "")}");
                break;
            }

            case TriggerActionTypes.ShowBubble:
            {
                // Param = text, Param2 = "type|duration" (optional). Bubble follows the
                // player (trigger areas have no attached NPC).
                string text = action.Param?.Trim() ?? "";
                if (string.IsNullOrEmpty(text)) break;
                string type = DialogueBubbleTypes.Speech;
                float duration = 3f;
                if (!string.IsNullOrEmpty(action.Param2))
                {
                    var parts = action.Param2.Split('|');
                    if (!string.IsNullOrWhiteSpace(parts[0])) type = parts[0].Trim();
                    if (parts.Length > 1 && float.TryParse(parts[1], out float d)) duration = d;
                }
                var player = FindPlayerObject();
                if (player != null)
                    DialogueSystem.ShowBubble(text, player, type, duration);
                else if (LastPlayerPosition is Vector3 pp)
                    DialogueSystem.ShowWorldBubble(text, new Vector2(pp.X, pp.Y + 2f), type, duration);
                Console.WriteLine($"[Trigger] '{triggerName}' → Show Bubble → '{text}' ({type}, {duration:F1}s)");
                break;
            }

            case TriggerActionTypes.HideBubble:
            {
                DialogueSystem.HideAllBubbles();
                Console.WriteLine($"[Trigger] '{triggerName}' → Hide Bubble (all)");
                break;
            }

            default:
            {
                string key = action.Type;
                if (_warned.Add(key))
                    Console.WriteLine($"[Trigger] '{triggerName}' → '{action.Type}' fired but has no runtime implementation yet (Param: '{action.Param}')");
                break;
            }
        }
    }

    /// <summary>Last known player position, refreshed each Update so action handlers
    /// (save/checkpoint) can capture where the trigger fired. IMPORTANT: this is the
    /// player's raw OBJECT position — NOT capsule space. All consumers (Save Checkpoint,
    /// Save Game) must store it as-is, and every place that converts to feet space
    /// adds CapsuleOffset exactly ONCE.</summary>
    public static Vector3? LastPlayerPosition
    {
        get => _lastPlayerPos;
        set => _lastPlayerPos = value;
    }
    private static Vector3? _lastPlayerPos;

    /// <summary>Active map + live player capsule offsets — refreshed by Player2DSystem
    /// every frame so save-slot checkpoint loading can ground-snap the restored feet
    /// position against the CURRENT level (box heights may have changed since the save).</summary>
    private static Tilemap2D? _activeMap;
    private static float _liveCapsuleOffsetX;
    private static float _liveCapsuleOffsetY;

    /// <summary>Start-point fallback (Start2D marker world pos, feet-anchored). The
    /// player2D system refreshes it every frame so Load Checkpoint can use it when no
    /// checkpoint has been saved.</summary>
    public static Vector2 _fallbackSpawn = Vector2.Zero;

    /// <summary>Set by the IDE-side player system: teleports the player's capsule to
    /// the given feet position and zeroes velocity (Load Checkpoint execution).</summary>
    public static Action<Vector2, float>? OnTeleportPlayer { get; set; }

    /// <summary>Clear per-session runtime state (delayed queue + warned set + saved
    /// checkpoint). Call when entering in-game/preview so a new session starts from
    /// the start point again instead of inheriting the last session's checkpoint.</summary>
    public static void BeginSession()
    {
        State.Clear();
        _warned.Clear();
        CheckpointPosition = null;
        Player2DStats.ResetToDefaults(); // fresh session → default HP/MP/Level/EXP/Fitness
        DialogueSystem.ResetSession();   // fresh session → no flags/vars/progress/bubbles
    }

    /// <summary>Find the first visible Player2D object in the editor scene (used to
    /// anchor player-following bubbles). The manager reference is refreshed by
    /// Player2DSystem every frame (bubble Show actions fire from trigger detection
    /// which runs inside that same update). Returns null when no player exists.</summary>
    public static DarkEngine3D_gl_csharp.Engine.Objects.EditorObjectManager? LastEditorObjectManager { get; set; }

    private static DarkEngine3D_gl_csharp.Engine.Objects.EditorObject? FindPlayerObject()
    {
        var mgr = LastEditorObjectManager;
        if (mgr == null) return null;
        foreach (var o in mgr.Objects)
            if (o is { IsVisible: true, PrimitiveType: DarkEngine3D_gl_csharp.Engine.Objects.EditorPrimitiveType.Player2D })
                return o;
        return null;
    }
}
