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

    /// <summary>Portal button-mode key probe: given the portal's key name (e.g. "E",
    /// "F"), returns true on the frame the key is PRESSED (edge). Wired by the IDE-side
    /// player system; when null the default enter-key edge passed to Update is used.</summary>
    public static Func<string, bool>? PortalKeyProbe { get; set; }

    /// <summary>Optional additional maps the Portal destination lookup may search
    /// (e.g. offscreen Map2D objects kept loaded for multi-map levels). Refreshed by
    /// the IDE side when the scene changes.</summary>
    public static List<Tilemap2D>? ExtraMaps { get; set; }

    /// <summary>Live player horizontal velocity (world units/s), refreshed by Update
    /// so the Portal action can spawn the arrival point in FRONT of the destination
    /// portal relative to the player's travel direction (right-moving → right side).
    /// ZERO frames are IGNORED (last non-zero wins): a button-mode player stands still
    /// while pressing the key — the arrival side must remember the direction they
    /// WALKED in with, not the idle velocity of the fire frame.</summary>
    private static float _lastPlayerVelX;

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
    /// <paramref name="enterKeyPressed"/>: true on the frame the player presses the
    /// interact key (portal button mode) — edges are computed by the caller so held
    /// keys don't retrigger. Advances the portal 4-state animation clocks as well.
    /// </summary>
    public static void Update(Tilemap2D? map, Vector3 playerFeetPos, float radius, float height, float dt,
        float playerVelX = 0f, float capsuleOffsetX = 0f, float capsuleOffsetY = 0f,
        bool enterKeyPressed = false)
    {
        // Keep save-slot checkpoint loading able to ground-snap against the live map.
        _activeMap = map;
        RuntimeMap ??= map; // adopt the first map seen as the runtime level (session start)
        _liveCapsuleOffsetX = capsuleOffsetX;
        _liveCapsuleOffsetY = capsuleOffsetY;
        // Keep the last NON-ZERO horizontal velocity: button-mode portals fire while
        // the player stands still (velX = 0) — the arrival side must come from the
        // direction they entered with.
        if (playerVelX != 0f)
            _lastPlayerVelX = playerVelX;

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
            if (trigger == null || trigger.RuntimeHidden) continue;
            if (trigger.RuntimeCooldown > 0f)
                trigger.RuntimeCooldown = MathF.Max(0f, trigger.RuntimeCooldown - dt);

            bool wasInside = trigger.RuntimePlayerInside;
            bool inside = false;

            if (trigger.IsEnabled && !trigger.RuntimeHidden && trigger.WidthPx > 0f && trigger.HeightPx > 0f)
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

            // Interact-key mode: the key EDGE while the player stands INSIDE — probed
            // per trigger so each zone can bind its own key (default E).
            bool interactPressed = trigger.RequireInteractKey && inside
                ? (PortalKeyProbe?.Invoke(string.IsNullOrWhiteSpace(trigger.InteractKey) ? "E" : trigger.InteractKey) ?? false)
                : false;

            bool fired = false;

            if (wasInside && !inside)
            {
                // ── On Exit ──
                trigger.RuntimePlayerInside = false;
                trigger.RuntimeInteractArmed = false;
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
                // Portal button mode: entering just ARMS the portal (plays the Enter
                // animation); the actual fire happens on the interact key press.
                bool isPortalArmed = false;
                if (trigger.IsEnabled && trigger.OnEnter && trigger.RuntimeCooldown <= 0f)
                {
                    bool hasPortalAction = trigger.Actions.Any(a =>
                        a != null && (a.Type == TriggerActionTypes.Portal || a.Type == TriggerActionTypes.PortalOneWay));
                    if (hasPortalAction && !trigger.PortalAutoEnter)
                    {
                        isPortalArmed = true;
                        trigger.RuntimePortalWasInside = true;
                        if (!string.IsNullOrEmpty(trigger.PortalAnimEnter))
                        {
                            trigger.RuntimePortalPhase = "entering";
                            trigger.RuntimePortalClock = 0f;
                        }
                    }
                    else
                    {
                        // RequireMovingRight gate: door-style triggers only fire when the
                        // player is actually heading INTO the area. Backtracking ignores it.
                        bool gateOk = !trigger.RequireMovingRight || playerVelX > 0.1f;
                        if (gateOk)
                        {
                            if (trigger.RequireInteractKey)
                            {
                                // Interact-key mode: OnEnter only ARMS the zone — the
                                // actual fire happens below on the key press (chest /
                                // NPC-style interaction, no auto-fire on walk-through).
                                trigger.RuntimeInteractArmed = true;
                            }
                            else
                            {
                                Fire(trigger, "OnEnter");
                                fired = true;
                            }
                        }
                    }
                }
                if (isPortalArmed) fired = true; // mark the boundary event (cooldown guard)
            }
            else if (wasInside && !inside && trigger.RuntimePortalWasInside && !trigger.PortalAutoEnter)
            {
                // Button-mode portal: player left without pressing the key → disarm.
                trigger.RuntimePortalWasInside = false;
                if (trigger.RuntimePortalPhase == "entering")
                    trigger.RuntimePortalPhase = "idle";
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

            // ── Interact-key fire: armed (player inside) + key EDGE → execute the
            // action list. Independent of the portal machinery (RuntimeApplied guard
            // still applies: an open chest does not re-fire its post-swap actions).
            if (interactPressed && trigger.RuntimeInteractArmed
                && trigger.IsEnabled && trigger.RuntimeCooldown <= 0f)
            {
                trigger.RuntimeInteractArmed = false;
                Fire(trigger, $"Interact ({trigger.InteractKey})");
                fired = true;
            }

            // ── Portal state machine: button-mode fire + animation clock advance ──
            TickPortalState(trigger, inside, enterKeyPressed, dt);

            if (fired)
            {
                trigger.RuntimeCooldown = 0.1f; // tiny guard so one boundary event can't double-fire
                // Journal the post-fire state so re-entering the map doesn't reset it
                // (chest already looted stays looted — see WorldStateJournal).
                WorldStateJournal.RecordTrigger(_activeMap?.Name, trigger.Name, trigger.RuntimeApplied, trigger.RuntimeHidden);
            }
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
            t.RuntimeHidden = false; // one-way portals reappear on session/map reload
            t.RuntimePortalPhase = "idle";
            t.RuntimePortalClock = 0f;
            t.RuntimePortalWasInside = false;
            t.RuntimeApplied = false; // chest guard: fresh session starts closed
            t.RuntimeTargetName = "";
            t.RuntimeInteractArmed = false; // interact-key zones re-arm on next entry
        }
        // World journal: a map reload must not un-loot a chest / resurrect a used
        // one-way portal — re-apply whatever the player already did there.
        WorldStateJournal.ApplyToMap(map);
    }

    // ── Portal runtime ──

    /// <summary>Advance one portal trigger's state machine: plays the 4-state
    /// animation (NotActive → Active → Enter → Out), fires button-mode portals on the
    /// interact key press, and finishes the "out" phase back to the correct idle
    /// state. Call once per frame per portal with the player overlap result.</summary>
    private static void TickPortalState(TilemapTriggerArea trigger, bool inside, bool enterKeyPressed, float dt)
    {
        // 1) Button-mode fire: armed (player inside) + interact key edge → portal fires
        //    (the OnEnter fire already ran, so re-fire now executes the portal action).
        //    The portal's own key (PortalEnterKey) wins when a probe is wired; the
        //    passed-in edge (default E) is the fallback.
        bool keyEdge = enterKeyPressed;
        if (PortalKeyProbe != null)
            keyEdge = PortalKeyProbe(trigger.PortalEnterKey);
        if (inside && trigger.IsEnabled && !trigger.RuntimeHidden && trigger.RuntimePortalWasInside
            && keyEdge && trigger.RuntimePortalPhase != "out"
            && trigger.RuntimeCooldown <= 0f)
        {
            trigger.RuntimePortalWasInside = false; // one shot per entry
            Fire(trigger, "OnEnter (button)");
            trigger.RuntimeCooldown = 0.1f;
        }

        // 2) "Out" phase: play PortalAnimOut once, then return to the right idle state.
        if (trigger.RuntimePortalPhase == "out")
        {
            if (TryGetPortalClip(trigger, trigger.PortalAnimOut, out var outClip) && outClip != null)
            {
                float dur = outClip.FrameIndices.Count / MathF.Max(0.01f, outClip.FPS * MathF.Max(0.01f, outClip.SpeedMultiplier));
                if (trigger.RuntimePortalClock >= dur)
                {
                    trigger.RuntimePortalPhase = "idle";
                    trigger.RuntimePortalClock = 0f;
                }
            }
            else
            {
                // No Out clip authored → the fire itself completed the transition.
                trigger.RuntimePortalPhase = "idle";
                trigger.RuntimePortalClock = 0f;
            }
        }

        // 3) "Entering" phase (button mode, pre-fire Enter animation): hold on the LAST
        //    frame until the key press fires the portal.
        if (trigger.RuntimePortalPhase == "entering")
        {
            if (TryGetPortalClip(trigger, trigger.PortalAnimEnter, out var enterClip) && enterClip != null)
            {
                float dur = enterClip.FrameIndices.Count / MathF.Max(0.01f, enterClip.FPS * MathF.Max(0.01f, enterClip.SpeedMultiplier));
                if (trigger.RuntimePortalClock < dur)
                {
                    // Still playing the Enter animation — advance the clock HERE and
                    // bail. (The old code returned BEFORE the advance at the bottom,
                    // so the clock froze at ~0 and the Enter/Active clip never moved —
                    // the reported "portal animasinya tidak jalan".)
                    trigger.RuntimePortalClock += dt;
                    return;
                }
            }
        }

        // 4) Advance the animation clock (idle states + phase timers share it).
        trigger.RuntimePortalClock += dt;
    }

    /// <summary>Resolve the portal's animation clip by name on its sheet (Sprite
    /// Editor registry). Returns false when the sheet/clip isn't authored or loaded.</summary>
    private static bool TryGetPortalClip(TilemapTriggerArea trigger, string clipName,
        out AnimationClip2D? clip)
    {
        clip = null;
        if (trigger == null || string.IsNullOrWhiteSpace(trigger.PortalSheet) || string.IsNullOrWhiteSpace(clipName))
            return false;
        return DarkEngine3D_gl_csharp.Engine.IDE.IDEBridge.TryGetSpriteClip(trigger.PortalSheet, clipName, out var _, out clip) && clip != null;
    }

    /// <summary>The clip the portal should render right now + whether it is playing
    /// once (true) or looping (false). Called by the viewport renderer every frame.
    /// Priority: Out (once) → Enter (button-mode pre-fire) → Active/NotActive loop.
    /// The clock is held/clamped per phase so one-shot clips don't loop.</summary>
    public static (string clipName, bool once, float clock) GetPortalDisplayState(TilemapTriggerArea trigger)
    {
        switch (trigger.RuntimePortalPhase)
        {
            case "out":
                if (TryGetPortalClip(trigger, trigger.PortalAnimOut, out var outC) && outC != null)
                {
                    float outDur = outC.FrameIndices.Count / MathF.Max(0.01f, outC.FPS * MathF.Max(0.01f, outC.SpeedMultiplier));
                    return (trigger.PortalAnimOut, true, MathF.Min(trigger.RuntimePortalClock, outDur * 0.999f));
                }
                break;
            case "entering":
                if (TryGetPortalClip(trigger, trigger.PortalAnimEnter, out var enterC) && enterC != null)
                {
                    float enterDur = enterC.FrameIndices.Count / MathF.Max(0.01f, enterC.FPS * MathF.Max(0.01f, enterC.SpeedMultiplier));
                    return (trigger.PortalAnimEnter, true, MathF.Min(trigger.RuntimePortalClock, enterDur * 0.999f));
                }
                break;
        }
        // Idle states: Active while enabled, NotActive while disabled/hidden.
        bool active = trigger.IsEnabled && !trigger.RuntimeHidden;
        string idleClipName = active ? trigger.PortalAnimActive : trigger.PortalAnimNotActive;
        // Fallback when NO idle clip is authored: reuse the sheet's own first registered
        // clip (e.g. "Greyscale Portal-Spinning" with one spinning clip). When the sheet
        // has NO clips at all, return a sentinel name so the renderer still draws the
        // portal — it falls back to the sheet's implicit frame grid. An empty name would
        // make DrawPortalSprites skip the portal entirely ("animasinya tidak jalan").
        if (string.IsNullOrEmpty(idleClipName) && !string.IsNullOrWhiteSpace(trigger.PortalSheet))
        {
            if (DarkEngine3D_gl_csharp.Engine.IDE.IDEBridge.TryGetFirstSpriteClipName(trigger.PortalSheet, out string firstClip))
                idleClipName = firstClip;
            else
                idleClipName = "(sheet)"; // sentinel: renderer animates the implicit grid
        }
        return (idleClipName, false, trigger.RuntimePortalClock);

    }

    /// <summary>Execute one trigger's action list with the SIMPLE SKIP GUARD: the
    /// first Change Sprite action is always applied (idempotent), and while the
    /// trigger's "open" state stands (SkipWhenDone = true), every action AFTER it is
    /// skipped — the chest pattern: an open chest plays no open-anim again and does
    /// NOT drop its item a second time. Delayed actions keep queuing normally (the
    /// guard re-checks at execution time, after the delay elapses).</summary>
    private static void Fire(TilemapTriggerArea trigger, string condition)
    {
        // ── Item-key lock (kondisi AWAL): player must carry the required item or
        // NOTHING happens — every fire path (auto-enter, interact key, stay) is
        // rejected here before any action runs. The chest stays closed and the
        // "locked" flash tells the player why.
        if (!string.IsNullOrWhiteSpace(trigger.RequireItemId))
        {
            string needId = trigger.RequireItemId.Trim();
            if (InventorySystem.Count(needId) <= 0)
            {
                if (!string.IsNullOrWhiteSpace(trigger.RequireItemMessage))
                    InventoryHud.PushFlash(trigger.RequireItemMessage);
                Console.WriteLine($"[Trigger] '{trigger.Name}' → BLOCKED by item lock: '{needId}' not in inventory");
                return;
            }
        }
        Console.WriteLine($"[Trigger] '{trigger.Name}' fired ({condition}) — {trigger.Actions.Count} action(s)");
        bool skipping = trigger.SkipWhenDone && trigger.RuntimeApplied;
        foreach (var action in trigger.Actions)
        {
            if (action == null) continue;
            if (action.Type == TriggerActionTypes.ChangeSprite)
            {
                // Apply unconditionally (the swap itself is idempotent), then arm the
                // guard so the remaining actions of THIS fire are skipped.
                ExecuteAction(action, trigger.Name, trigger);
                trigger.RuntimeApplied = true;
                skipping = trigger.SkipWhenDone; // first fire: keep executing this list
                continue;
            }
            if (skipping)
            {
                Console.WriteLine($"[Trigger] '{trigger.Name}' → skipped '{action.Type}' (state already applied)");
                continue;
                // Actions BEFORE the first Change Sprite always run (dialogue, sound…).
            }
            if (action.Delay > 0f)
            {
                // Delayed actions queue onto the runtime state; executed by TickDelayed.
                State.QueueDelayed(action, trigger.Name, condition, trigger);
            }
            else
            {
                ExecuteAction(action, trigger.Name, trigger);
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
        public TilemapTriggerArea? Source; // one-way portals need their trigger to vanish
    }

    /// <summary>Runtime-only holder for delayed action queue. Kept separate from the
    /// map data so nothing runtime leaks into save files.</summary>
    public sealed class TriggerRuntimeState
    {
        private readonly List<PendingAction> _pending = new();

        public void QueueDelayed(TilemapTriggerAction action, string triggerName, string condition,
            TilemapTriggerArea? source = null)
        {
            _pending.Add(new PendingAction { Action = action, TriggerName = triggerName, Remaining = action.Delay, Source = source });
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
                    ExecuteAction(p.Action, p.TriggerName, p.Source);
                }
            }
        }

        public void Clear() => _pending.Clear();
    }

    /// <summary>Dispatch one trigger action. Wired actions call real engine systems;
    /// the rest log once so the designer knows the trigger fired but the action has
    /// no gameplay implementation yet. <paramref name="trigger"/> is the firing area
    /// (null for dialogue-driven actions) — the one-way portal uses it to vanish.</summary>
    public static void ExecuteAction(TilemapTriggerAction action, string triggerName,
        TilemapTriggerArea? trigger = null)
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
                        // Snap against the RUNTIME level (portals can move the session to
                        // another scene map — the checkpoint must land on THAT collision).
                        float feetX = CheckpointPosition.Value.X + _liveCapsuleOffsetX;
                        float feetY = SnapFeetToGround(RuntimeMap ?? _activeMap, feetX,
                            CheckpointPosition.Value.Y + _liveCapsuleOffsetY);
                        target = new Vector2(feetX, feetY);
                        z = CheckpointZ;
                        source = "(session checkpoint)";
                    }
                    else if (TryLoadCheckpointFromSaves(RuntimeMap ?? _activeMap, _liveCapsuleOffsetX, _liveCapsuleOffsetY, out var saved, out float savedZ))
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
                    OnTeleportPlayer(target, z, null); // checkpoint: same runtime level
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

            case TriggerActionTypes.Portal:
            case TriggerActionTypes.PortalOneWay:
            {
                bool oneWay = action.Type == TriggerActionTypes.PortalOneWay;
                string dest = action.Param?.Trim() ?? "";
                if (string.IsNullOrEmpty(dest))
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Portal FAILED: destination not set in the action's Param (format 'x,y' world units, or trigger/portal name)");
                    break;
                }

                // ── Resolve the destination (object-space convention: OnTeleportPlayer
                // subtracts the capsule offsets exactly once) ──
                //   1) "x,y"  → direct world coordinates (ground-snapped below).
                //   2) name   → center of the trigger/portal with that name, searched on
                //      every loaded map (the portal's own map first). For a two-way portal
                //      pair this lands the player INSIDE the twin portal so stepping back
                //      through it returns to where they came from.
                Vector2 target;
                float z = CheckpointZ;
                Tilemap2D? destMap = null;
                TilemapTriggerArea? destArea = null;

                if (TryParseDestination(dest, out var direct))
                {
                    target = direct;
                }
                else
                {
                    foreach (var m in EnumerateCandidateMaps(_activeMap))
                    {
                        var hit = FindTriggerArea(m, dest, out var owner);
                        if (hit == null || owner == null) continue;
                        destArea = hit;
                        destMap = owner;
                        break;
                    }
                    if (destArea == null)
                    {
                        Console.WriteLine($"[Trigger] '{triggerName}' → Portal FAILED: no portal/trigger named '{dest}' on any loaded map");
                        break;
                    }
                    // Center of the destination portal (world space).
                    float cellD = destMap!.TileSize * Tilemap2D.WorldScale;
                    float cxD = (destArea.LeftPx + destArea.WidthPx * 0.5f) * Tilemap2D.WorldScale;
                    float cyD = (destMap.Height * cellD) - (destArea.TopPx + destArea.HeightPx * 0.5f) * Tilemap2D.WorldScale;
                    // Spawn IN FRONT of the destination portal: center ± one portal
                    // width (the DESTINATION portal's width), side picked by the
                    // player's travel direction. Arriving outside the trigger area
                    // prevents the arrival frame from re-entering the twin portal
                    // (two-way pairs would otherwise ping-pong forever).
                    float portalW = MathF.Max(destArea.WidthPx, 1f) * Tilemap2D.WorldScale;
                    float spawnX = cxD + (_lastPlayerVelX >= 0f ? portalW : -portalW);
                    target = new Vector2(spawnX, cyD);
                }

                // ── Teleport ──
                if (OnTeleportPlayer == null)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Portal skipped: no teleport handler registered");
                    break;
                }

                // Snap the feet down onto collision so the player never materializes
                // inside/under a solid tile. Ground-snap searches DOWN; for portals
                // placed under the floor this still finds the floor directly below.
                var snapMap = destMap;
                if (snapMap == null && trigger != null)
                {
                    // Direct coordinates: snap against the portal's OWN map.
                    foreach (var m in EnumerateCandidateMaps(_activeMap))
                    {
                        if (FindTriggerArea(m, trigger.Name, out var owner) != null)
                        { snapMap = owner; break; }
                    }
                }
                snapMap ??= _activeMap;
                if (snapMap != null)
                {
                    float feetX = target.X + _liveCapsuleOffsetX;
                    float feetY = SnapFeetToGround(snapMap, feetX, target.Y + _liveCapsuleOffsetY);
                    target = new Vector2(feetX, feetY);
                }                    OnTeleportPlayer(target, z, destMap); // portal: destination map becomes the runtime level
                    Console.WriteLine($"[Trigger] '{triggerName}' → {(oneWay ? "Portal One Way" : "Portal")} → ({target.X:F1}, {target.Y:F1}){(directCoordsHint(dest) ? " (coords)" : $" (portal '{dest}' + width offset)")}{(destMap != null ? $" [map '{destMap.Name}']" : "")}");

                // ── Arrival loop guard (AUTO portals only): the player materializes
                // with the capsule edge overlapping the destination area, so the next
                // overlap frame would read wasInside=false + inside=true and re-fire
                // its OnEnter (auto pairs would ping-pong forever) — mark it already-
                // inside so only a genuine walk-out/walk-in refires it. BUTTON-mode
                // destinations must NOT be marked: they fire on the interact key, so
                // there is no loop — and marking inside would swallow the enter
                // boundary that ARMS the key prompt (walking in would never enable E).
                if (destArea != null && destArea.PortalAutoEnter
                    && destArea.Actions.Any(a => a != null &&
                        (a.Type == TriggerActionTypes.Portal || a.Type == TriggerActionTypes.PortalOneWay)))
                {
                    destArea.RuntimePlayerInside = true;
                    destArea.RuntimePortalWasInside = false;
                }

                // ── One-way portal: "bisa masuk saja, portal akan menghilang" ──
                // Hide + disable it until ResetRuntime (session/map reload) so it can't
                // refire; the two-way portal stays for the trip back.
                if (oneWay && trigger != null)
                {
                    trigger.RuntimeHidden = true;
                    trigger.IsEnabled = false;
                    trigger.RuntimePlayerInside = false;
                    trigger.RuntimePortalWasInside = false;
                    // Journal the vanish (recorded AFTER the fired-block wrote Applied,
                    // so this supersedes it with Hidden = true).
                    WorldStateJournal.RecordTrigger(_activeMap?.Name, trigger.Name, trigger.RuntimeApplied, trigger.RuntimeHidden);
                    Console.WriteLine($"[Trigger] '{triggerName}' → one-way portal vanished (returns on session/map reload)");
                }

                // Play the portal's Out animation (one-shot) when authored; the state
                // machine returns to Active/NotActive (or stays NotActive while hidden).
                if (trigger != null && !string.IsNullOrEmpty(trigger.PortalAnimOut))
                {
                    trigger.RuntimePortalPhase = "out";
                    trigger.RuntimePortalClock = 0f;
                }
                break;
            }

            case TriggerActionTypes.EnablePortal:
            case TriggerActionTypes.DisablePortal:
            {
                // Event-driven portal control: Param = portal/trigger NAME (searched on
                // every loaded map — same-named portals all switch together, which makes
                // linked twin portals trivial to gate). DisablePortal deactivates the
                // portal (shows NotActive animation, ignores the player); EnablePortal
                // brings it back (a vanished one-way portal STAYS hidden until reload).
                string portalName = action.Param?.Trim() ?? "";
                if (string.IsNullOrEmpty(portalName))
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → {(action.Type == TriggerActionTypes.EnablePortal ? "Enable" : "Disable")} Portal FAILED: no portal name set in the action's Param");
                    break;
                }
                bool enable = action.Type == TriggerActionTypes.EnablePortal;
                int changed = 0;
                foreach (var m in EnumerateCandidateMaps(_activeMap))
                {
                    var area = FindTriggerArea(m, portalName, out _);
                    if (area == null) continue;
                    area.IsEnabled = enable;
                    if (!enable)
                    {
                        area.RuntimePortalWasInside = false;
                        if (area.RuntimePortalPhase != "out") area.RuntimePortalPhase = "idle";
                    }
                    changed++;
                }
                Console.WriteLine($"[Trigger] '{triggerName}' → {(enable ? "Enable" : "Disable")} Portal '{portalName}' → {changed} portal(s) {(enable ? "enabled" : "disabled")}");
                break;
            }

            case TriggerActionTypes.ModifyStat:
            {
                // Simple gameplay script: Param = stat name (Health/Mana/Level/...
                // or HP/MP shorthands), Param2 = delta like "-1", "+5", "-10.5".
                // Positive adds, negative subtracts; result clamps into [0, Max].
                string stat = (action.Param ?? "").Trim();
                string deltaStr = (action.Param2 ?? "").Trim();
                if (stat.Length == 0 || deltaStr.Length == 0)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Modify Stat FAILED: set Param (stat name) and Param2 (delta, e.g. -1)");
                    break;
                }
                // Currency: "Gold" tweaks the inventory wallet (quest rewards,
                // vendor payouts) instead of a body stat.
                if (stat.Equals("Gold", StringComparison.OrdinalIgnoreCase) ||
                    stat.Equals("Coin", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(deltaStr, out int goldDelta))
                    {
                        Console.WriteLine($"[Trigger] '{triggerName}' → Modify Stat Gold FAILED: '{deltaStr}' is not a number");
                        break;
                    }
                    int beforeGold = InventorySystem.Gold;
                    InventorySystem.Gold = Math.Max(0, beforeGold + goldDelta);
                    int applied = InventorySystem.Gold - beforeGold;
                    if (applied != 0)
                        InventoryHud.PushFlash($"{(applied > 0 ? "+" : "")}{applied} Gold");
                    Console.WriteLine($"[Trigger] '{triggerName}' → Modify Stat Gold {beforeGold} {(applied >= 0 ? "+" : "−")}{MathF.Abs(applied)} → {InventorySystem.Gold}");
                    break;
                }
                // Friendly shorthands → canonical stat names.
                string canonical = stat.ToUpperInvariant() switch
                {
                    "HP" or "HEALTH" or "LIFE" => PlayerStatNames.Health,
                    "MP" or "MANA" or "SP" => PlayerStatNames.Mana,
                    "LV" or "LVL" or "LEVEL" => PlayerStatNames.Level,
                    "EXP" or "XP" => PlayerStatNames.Experience,
                    "ST" or "STA" or "FITNESS" => PlayerStatNames.Fitness,
                    _ => stat,
                };
                if (!float.TryParse(deltaStr, out float delta))
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Modify Stat FAILED: '{deltaStr}' is not a number");
                    break;
                }
                float before = Player2DStats.GetCurrent(canonical);
                float after = MathF.Max(0f, before + delta);
                Player2DStats.SetCurrent(canonical, after);
                Player2DStats.ClampAll();
                Console.WriteLine($"[Trigger] '{triggerName}' → Modify Stat {canonical} {before:F0} {(delta >= 0 ? "+" : "−")}{MathF.Abs(delta):F0} → {Player2DStats.GetCurrent(canonical):F0}");
                break;
            }

            case TriggerActionTypes.Rain:
            {
                // Weather: Param = "on"/"off"/"toggle" (empty = toggle), Param2 =
                // intensity 0..1 (optional, persists while rain stays on).
                string mode = (action.Param ?? "").Trim().ToLowerInvariant();
                bool nowOn = mode switch
                {
                    "on" or "start" or "1" or "true" => true,
                    "off" or "stop" or "0" or "false" => false,
                    _ => !Effect2DSystem.RainEnabled,
                };
                Effect2DSystem.RainEnabled = nowOn;
                if (!string.IsNullOrWhiteSpace(action.Param2)
                    && float.TryParse(action.Param2.Trim(), out float inten))
                    Effect2DSystem.RainIntensity = Math.Clamp(inten, 0f, 1f);
                Console.WriteLine($"[Trigger] '{triggerName}' → Rain {(nowOn ? "ON" : "OFF")} (intensity {Effect2DSystem.RainIntensity:F2})");
                break;
            }

            case TriggerActionTypes.SetWind:
            {
                // Weather: Param = wind speed in world units/second (+ right, − left).
                if (!float.TryParse((action.Param ?? "").Trim(), out float wind))
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Set Wind FAILED: Param must be a number (world units/s, negative = left)");
                    break;
                }
                Effect2DSystem.WindX = wind;
                Console.WriteLine($"[Trigger] '{triggerName}' → Set Wind → {wind:F2} u/s");
                break;
            }

            case TriggerActionTypes.SpawnEffect:
            {
                // One-shot particle burst: Param = preset name (Explosion, Coin, Sparks,
                // Fire, Fireball, Dust, Smoke, Snow), Param2 = scale multiplier (optional).
                string preset = (action.Param ?? "").Trim();
                if (preset.Length == 0)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Spawn Effect FAILED: set Param to a preset ({string.Join(", ", Effect2DSystem.Presets)})");
                    break;
                }
                Vector3 pos = _lastPlayerPos ?? Vector3.Zero;
                float scale = 1f;
                if (!string.IsNullOrWhiteSpace(action.Param2))
                    float.TryParse(action.Param2.Trim(), out scale);
                if (scale <= 0f) scale = 1f;
                Effect2DSystem.SpawnBurst(preset, pos, scale);
                Console.WriteLine($"[Trigger] '{triggerName}' → Spawn Effect '{preset}' ×{scale:F1} at ({pos.X:F1}, {pos.Y:F1})");
                break;
            }

            case TriggerActionTypes.SpawnProjectile:
            {
                // Sprite projectile launcher: Param = "Sheet|Clip" (Sprite Editor
                // registry), Param2 = "speed,maxDist,gravity" (optional). Flies toward
                // the player's side (trap/turret). Damage fields come from the XML
                // defaults (10 HP) — phase 2 exposes them per-trigger in the editor.
                string raw = action.Param ?? "";
                var parts = raw.Split('|');
                string sheet = parts.Length > 0 ? parts[0].Trim() : "";
                string clip = parts.Length > 1 ? parts[1].Trim() : "";
                if (sheet.Length == 0 || clip.Length == 0)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Spawn Projectile FAILED: Param must be 'Sheet|Clip' (Sprite Editor names)");
                    break;
                }
                var cfg = new Projectile2DSystem.Config { Sheet = sheet, Clip = clip };
                if (!string.IsNullOrWhiteSpace(action.Param2))
                {
                    var nums = action.Param2.Split(',');
                    if (nums.Length > 0 && float.TryParse(nums[0].Trim(), out float spd)) cfg.Speed = spd;
                    if (nums.Length > 1 && float.TryParse(nums[1].Trim(), out float dist)) cfg.MaxDistance = dist;
                    if (nums.Length > 2 && float.TryParse(nums[2].Trim(), out float grav)) cfg.Gravity = grav;
                }
                // Origin: the firing trigger's center (falls back to the player position).
                Vector3 origin = _lastPlayerPos ?? Vector3.Zero;
                if (trigger != null && _activeMap != null)
                {
                    float cellD = _activeMap.TileSize * Tilemap2D.WorldScale;
                    origin = new Vector3(
                        (trigger.LeftPx + trigger.WidthPx * 0.5f) * Tilemap2D.WorldScale,
                        _activeMap.Height * cellD - (trigger.TopPx + trigger.HeightPx * 0.5f) * Tilemap2D.WorldScale,
                        origin.Z);
                }
                Projectile2DSystem.SpawnFromTrigger(cfg, origin, towardPlayer: true);
                Console.WriteLine($"[Trigger] '{triggerName}' → Spawn Projectile '{sheet}|{clip}' speed {cfg.Speed:F1}");
                break;
            }

            case TriggerActionTypes.ChangeSprite:
            {
                // Sprite state swap (chest closed → open): Param = object NAME (empty =
                // nearest Player2D), Param2 = "Sheet|Clip" (Sprite Editor registry),
                // Param3 = "Swap" (default) / "Revert". The state lives ON the object;
                // the trigger's RuntimeApplied flag (set by Fire) gates the skip guard.
                string objName = (action.Param ?? "").Trim();
                string swapRaw = (action.Param2 ?? "").Trim();
                bool revert = (action.Param3 ?? "").Trim().Equals("Revert", StringComparison.OrdinalIgnoreCase);
                var target = FindTriggerTarget(LastEditorObjectManager, objName, trigger);
                if (target == null)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Change Sprite FAILED: object '{objName}' not found (use the Hierarchy object name, empty = nearest player)");
                    break;
                }
                if (revert)
                {
                    target.ClearSpriteStateOverride();
                    WorldStateJournal.ClearSprite(target.Name); // journal forgets the swap
                    Console.WriteLine($"[Trigger] '{triggerName}' → Change Sprite REVERT on '{target.Name}'");
                    break;
                }
                var parts = swapRaw.Split('|');
                string swSheet = parts.Length > 0 ? parts[0].Trim() : "";
                string swClip = parts.Length > 1 ? parts[1].Trim() : "";
                if (swSheet.Length == 0 || swClip.Length == 0)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Change Sprite FAILED: Param2 must be 'Sheet|Clip' (Sprite Editor names)");
                    break;
                }
                // Loop mode from the "Loop Animasi" checkbox: ON = keep looping the
                // swapped clip, OFF = play it once and hold the last frame (chest-open,
                // levers) so the art does not re-open forever.
                target.SetSpriteStateOverride(swSheet, swClip, triggerName, action.LoopAnim);
                // Journal the END-STATE so leaving and returning to the map keeps the
                // chest open (re-applied after every map reload / save load).
                WorldStateJournal.RecordSprite(target.Name, swSheet, swClip, action.LoopAnim, triggerName);
                if (trigger != null) trigger.RuntimeTargetName = target.Name;
                Console.WriteLine($"[Trigger] '{triggerName}' → Change Sprite '{target.Name}' → '{swSheet}|{swClip}' (loop {(action.LoopAnim ? "on" : "off")})");
                break;
            }

            case TriggerActionTypes.GiveItem:
            {
                // Inventory phase 1: Param = item id, Param2 = amount (default 1),
                // Param3 = delivery mode: empty/"Direct" → straight into the inventory
                // (legacy), "Drop" → spawn a world loot drop that magnet-vacuums into
                // the player (chest smash / enemy drop feel). Equips nothing.
                string itemId = (action.Param ?? "").Trim();
                if (itemId.Length == 0)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Give Item FAILED: no item id in Param");
                    break;
                }
                int amount = 1;
                if (!string.IsNullOrWhiteSpace(action.Param2))
                    int.TryParse(action.Param2.Trim(), out amount);
                if (amount <= 0) amount = 1;
                string mode = (action.Param3 ?? "").Trim();
                if (mode.Equals("Drop", StringComparison.OrdinalIgnoreCase))
                {
                    // World drop at the player's feet: the magnet tick (Player2DSystem
                    // → InventorySystem.TickDrops) pulls it in with the vacuum animation.
                    InventorySystem.SpawnDrop(itemId, amount,
                        InventorySystem.PlayerFeetX, InventorySystem.PlayerFeetY + 0.6f);
                    Console.WriteLine($"[Trigger] '{triggerName}' → Give Item '{itemId}' ×{amount} → world drop (magnet pickup)");
                    break;
                }
                int added = InventorySystem.AddItem(itemId, amount);
                Console.WriteLine($"[Trigger] '{triggerName}' → Give Item '{itemId}' ×{amount} → added {added}");
                if (added > 0)
                    Effect2DSystem.SpawnBurst("Sparks",
                        new Vector3(InventorySystem.PlayerFeetX, InventorySystem.PlayerFeetY + 1f, 0f), 0.5f);
                break;
            }
            case TriggerActionTypes.RemoveItem:
            {
                // Fetch-quest hand-in: take the offered goods back (3 Ember → reward).
                // Removed even when the count is short — callers gate the branch with
                // an "item:x>=N" dialogue condition so it only runs when it can pay.
                string itemId = (action.Param ?? "").Trim();
                if (itemId.Length == 0)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Remove Item FAILED: no item id in Param");
                    break;
                }
                int amount = 1;
                if (!string.IsNullOrWhiteSpace(action.Param2))
                    int.TryParse(action.Param2.Trim(), out amount);
                if (amount <= 0) amount = 1;
                int removed = InventorySystem.RemoveItem(itemId, amount);
                if (removed > 0)
                InventoryHud.PushFlash($"-{removed} {(InventorySystem.Find(itemId)?.Name ?? itemId)}");
                Console.WriteLine($"[Trigger] '{triggerName}' → Remove Item '{itemId}' ×{amount} → removed {removed}");
                break;
            }
            case TriggerActionTypes.BuyItem:
            {
                // Shop transaction: Param = item id, Param2 = amount (default 1),
                // Param3 = "" = priced at ItemDef.Price each; "N" = fixed price override
                // per item (0 = free). Payment + delivery are ALL-OR-NOTHING: without
                // enough gold (or a full grid with Direct mode) nothing is paid/taken.
                string itemId = (action.Param ?? "").Trim();
                if (itemId.Length == 0)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Buy Item FAILED: no item id in Param");
                    break;
                }
                int amount = 1;
                if (!string.IsNullOrWhiteSpace(action.Param2))
                    int.TryParse(action.Param2.Trim(), out amount);
                if (amount <= 0) amount = 1;
                int unitPrice;
                string priceRaw = (action.Param3 ?? "").Trim();
                var def = InventorySystem.Find(itemId);
                if (priceRaw.Length > 0 && int.TryParse(priceRaw, out int fixedPrice))
                    unitPrice = Math.Max(0, fixedPrice); // "0" = gratis, "N" = harga override
                else
                    unitPrice = Math.Max(0, def?.Price ?? 0); // tak terdaftar = gratis (log saja)
                int total = unitPrice * amount;
                if (InventorySystem.Gold < total)
                {
                    InventoryHud.PushFlash($"Gold kurang! Butuh {total} Gold.");
                    Console.WriteLine($"[Trigger] '{triggerName}' → Buy Item '{itemId}' ×{amount} FAILED: {InventorySystem.Gold}/{total} gold");
                    break;
                }
                int added = InventorySystem.AddItem(itemId, amount);
                if (added <= 0)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Buy Item FAILED: inventory grid penuh (Direct mode)");
                    break;
                }
                int charged = unitPrice * added; // grid penuh sebagian → bayar proporsional
                InventorySystem.Gold -= charged;
                if (def != null)
                    InventoryHud.PushFlash($"-{charged} Gold → +{added} {def.Name}");
                Effect2DSystem.SpawnBurst("Coin",

                    new Vector3(InventorySystem.PlayerFeetX, InventorySystem.PlayerFeetY + 1f, 0f), 0.5f);
                Console.WriteLine($"[Trigger] '{triggerName}' → Buy Item '{itemId}' ×{added} @ {unitPrice} → -{charged} gold (sisa {InventorySystem.Gold})");
                break;
            }
            case TriggerActionTypes.SellItem:
            {
                // Vendor: Param = item id, Param2 = amount (default 1), Param3 = "" =
                // payout ItemDef.Price each; "N" = fixed payout override. All-or-nothing
                // on stock: without the goods nothing is paid out.
                string itemId = (action.Param ?? "").Trim();
                if (itemId.Length == 0)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Sell Item FAILED: no item id in Param");
                    break;
                }
                int amount = 1;
                if (!string.IsNullOrWhiteSpace(action.Param2))
                    int.TryParse(action.Param2.Trim(), out amount);
                if (amount <= 0) amount = 1;
                int unitPrice;
                string payoutRaw = (action.Param3 ?? "").Trim();
                var def = InventorySystem.Find(itemId);
                if (payoutRaw.Length > 0 && int.TryParse(payoutRaw, out int fixedPayout))
                    unitPrice = Math.Max(0, fixedPayout);
                else
                    unitPrice = Math.Max(0, def?.Price ?? 0);
                if (InventorySystem.Count(itemId) < amount)
                
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Sell Item FAILED: '{itemId}' ×{amount} tidak tersedia");
                    break;
                }
                int sold = InventorySystem.RemoveItem(itemId, amount);
                InventorySystem.Gold += unitPrice * sold;
                if (sold > 0)
                    InventoryHud.PushFlash($"+{unitPrice * sold} Gold ← -{sold} {(def?.Name ?? itemId)}");
                Effect2DSystem.SpawnBurst("Coin",
                    new Vector3(InventorySystem.PlayerFeetX, InventorySystem.PlayerFeetY + 1f, 0f), 0.5f);
                Console.WriteLine($"[Trigger] '{triggerName}' → Sell Item '{itemId}' ×{sold} @ {unitPrice} → +{unitPrice * sold} gold (sisa {InventorySystem.Gold})");
                break;
            }
            case TriggerActionTypes.OpenShop:
            {
                // Shop panel: Param = shop id (Shop Editor / shops.json). Opens the
                // classic buy/sell panel (ShopHud) ON TOP of the conversation — the
                // dialogue keeps running underneath and regains the window on close.
                string shopId = (action.Param ?? "").Trim();
                if (shopId.Length == 0)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Open Shop FAILED: no shop id in Param");
                    break;
                }
                ShopHud.TryOpen(shopId);
                break;
            }
            case TriggerActionTypes.ActivateQuest:
            {
                // Quest bookkeeping via dialogue flags: quest_<id>_active (and _done
                // cleared so a quest can be re-offered). The dialogue conditions
                // quest:/questdone: read these same flags — see DialogueSystem.
                string questId = (action.Param ?? "").Trim();
                if (questId.Length == 0)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Activate Quest FAILED: no quest id in Param");
                    break;
                }
                DialogueSystem.SetFlag($"quest_{questId}_active");
                DialogueSystem.ClearFlag($"quest_{questId}_done");
                QuestSystem.OnActivated(questId); // HUD tracker flash + auto-placeholder
                Console.WriteLine($"[Trigger] '{triggerName}' → Activate Quest '{questId}'");
                break;
            }
            case TriggerActionTypes.CompleteQuest:
            {
                // Hand-in endpoint: flips quest_<id>_done — the notflag/questdone
                // dialogue conditions use it to hide the turn-in option afterwards.
                string questId = (action.Param ?? "").Trim();
                if (questId.Length == 0)
                {
                    Console.WriteLine($"[Trigger] '{triggerName}' → Complete Quest FAILED: no quest id in Param");
                    break;
                }
                DialogueSystem.SetFlag($"quest_{questId}_done");
                DialogueSystem.ClearFlag($"quest_{questId}_active");
                QuestSystem.OnCompleted(questId); // HUD flash "✓ Quest selesai"
                Console.WriteLine($"[Trigger] '{triggerName}' → Complete Quest '{questId}'");
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

    /// <summary>Resolve a Change Sprite target object: exact visible match by NAME
    /// first (Hierarchy name), otherwise the NEAREST visible Player2D/Sprite2D to the
    /// firing trigger's center (chest art usually sits inside its trigger volume).</summary>
    private static DarkEngine3D_gl_csharp.Engine.Objects.EditorObject? FindTriggerTarget(
        DarkEngine3D_gl_csharp.Engine.Objects.EditorObjectManager? mgr, string objName,
        TilemapTriggerArea? trigger)
    {
        if (mgr == null) return null;
        if (objName.Length > 0)
        {
            foreach (var o in mgr.Objects)
                if (o is { IsVisible: true } && string.Equals(o.Name, objName, StringComparison.OrdinalIgnoreCase))
                    return o;
            return null;
        }
        // Nearest swap-capable object to the trigger center (world units).
        float cx = 0f, cy = 0f; bool hasCenter = false;
        if (trigger != null && _activeMap != null)
        {
            float cell = _activeMap.TileSize * Tilemap2D.WorldScale;
            cx = (trigger.LeftPx + trigger.WidthPx * 0.5f) * Tilemap2D.WorldScale;
            cy = _activeMap.Height * cell - (trigger.TopPx + trigger.HeightPx * 0.5f) * Tilemap2D.WorldScale;
            hasCenter = true;
        }
        DarkEngine3D_gl_csharp.Engine.Objects.EditorObject? best = null;
        float bestDist = float.MaxValue;
        foreach (var o in mgr.Objects)
        {
            if (o is not { IsVisible: true } ||
                (o.PrimitiveType != DarkEngine3D_gl_csharp.Engine.Objects.EditorPrimitiveType.Player2D
                 && o.PrimitiveType != DarkEngine3D_gl_csharp.Engine.Objects.EditorPrimitiveType.Sprite2D))
                continue;
            float d = hasCenter
                ? (o.Position.X - cx) * (o.Position.X - cx) + (o.Position.Y - cy) * (o.Position.Y - cy)
                : 0f; // no center known: take the first candidate (player-first order below)
            // Prefer Player2D on distance ties (the legacy "change the player" case).
            if (o.PrimitiveType == DarkEngine3D_gl_csharp.Engine.Objects.EditorPrimitiveType.Player2D)
                d -= 0.01f;
            if (best == null || d < bestDist) { best = o; bestDist = d; }
            if (!hasCenter) break;
        }
        return best;
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
    public static Action<Vector2, float, Tilemap2D?>? OnTeleportPlayer { get; set; }

    /// <summary>The RUNTIME level the session currently plays on. The active map at
    /// session start, then swapped by portals whose destination lives on ANOTHER
    /// scene map (or by the Change Map action's file load). Checkpoint save/load and
    /// camera world-bounds consult this instead of the editor's selected map, so
    /// changing map levels also moves the camera framing and ground snapping.</summary>
    public static Tilemap2D? RuntimeMap { get; set; }

    /// <summary>Clear per-session runtime state (delayed queue + warned set + saved
    /// checkpoint). Call when entering in-game/preview so a new session starts from
    /// the start point again instead of inheriting the last session's checkpoint.</summary>
    public static void BeginSession()
    {
        State.Clear();
        _warned.Clear();
        CheckpointPosition = null;
        WorldStateJournal.Clear(); // fresh run → the world resets (chest closed again)
        Player2DStats.ResetToDefaults(); // fresh session → default HP/MP/Level/EXP/Fitness
        DialogueSystem.ResetSession();   // fresh session → no flags/vars/progress/bubbles
        InventorySystem.ResetSession();  // fresh session → empty grid/bag/gold
        InventorySystem.SnapshotBaseStats(); // base maxima BEFORE equipment bonuses
        // Fresh session → clear weather + particles (rain/wind don't leak between runs).
        Effect2DSystem.RainEnabled = false;
        Effect2DSystem.WindX = 0f;
        Effect2DSystem.Clear();
        Projectile2DSystem.Clear();   // fresh session → no leftover projectiles / enemy HP
        InventorySystem.ClearDrops(); // fresh session → no leftover world loot
        // Fresh session → chest/lever sprite overrides reset (new session = closed).
        if (LastEditorObjectManager != null)
            foreach (var o in LastEditorObjectManager.Objects)
                o?.ClearSpriteStateOverride();
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

    // ── Portal helpers ──

    /// <summary>Parse a portal destination in "x,y" world units (space or comma
    /// separated). Returns false for anything else so it can be treated as a portal
    /// / trigger NAME instead.</summary>
    private static bool TryParseDestination(string dest, out Vector2 position)
    {
        position = default;
        var parts = dest.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        if (!float.TryParse(parts[0], out float x)) return false;
        if (!float.TryParse(parts[1], out float y)) return false;
        position = new Vector2(x, y);
        return true;
    }

    /// <summary>Enumerate every BUTTON-mode portal (has a Portal action, PortalAutoEnter
    /// = false) that is enabled and visible right now — the interact-key badge ("[E]")
    /// renderers iterate this so the hint above the portal shows during gameplay.
    /// Portal-less triggers and auto-enter portals are skipped (no key needed).</summary>
    public static IEnumerable<(TilemapTriggerArea Area, Tilemap2D Map)> ButtonModePortals(
        DarkEngine3D_gl_csharp.Engine.Objects.EditorObjectManager? manager)
    {
        if (manager == null) yield break;
        var maps = manager.Objects
            .Where(o => o is { IsVisible: true, PrimitiveType: DarkEngine3D_gl_csharp.Engine.Objects.EditorPrimitiveType.Map2D })
            .Select(o => o.Map2dTilemap)
            .OfType<Tilemap2D>()
            .Distinct();
        foreach (var map in maps)
        {
            foreach (var t in map.TriggerAreas)
            {
                if (t == null || !t.IsEnabled || t.RuntimeHidden) continue;
                if (t.PortalAutoEnter) continue;
                bool hasPortal = t.Actions.Any(a => a != null &&
                    (a.Type == TriggerActionTypes.Portal || a.Type == TriggerActionTypes.PortalOneWay));
                if (!hasPortal) continue;
                yield return (t, map);
            }
        }
    }

    /// <summary>Every ARMED interact-key zone (player inside, waiting for the key
    /// press) — the "[E]" badge renderers iterate this so the hint shows above the    /// zone during gameplay. Interact-key mode is the chest/NPC-style trigger.</summary>
    public static IEnumerable<(TilemapTriggerArea Area, Tilemap2D Map)> InteractKeyZones(
        DarkEngine3D_gl_csharp.Engine.Objects.EditorObjectManager? manager)
    {
        if (manager == null) yield break;
        var maps = manager.Objects
            .Where(o => o is { IsVisible: true, PrimitiveType: DarkEngine3D_gl_csharp.Engine.Objects.EditorPrimitiveType.Map2D })
            .Select(o => o.Map2dTilemap)
            .OfType<Tilemap2D>()
            .Distinct();
        foreach (var map in maps)
        {
            foreach (var t in map.TriggerAreas)
            {
                if (t == null || !t.IsEnabled || t.RuntimeHidden) continue;
                if (!t.RequireInteractKey || !t.RuntimeInteractArmed) continue;
                yield return (t, map);
            }
        }
    }

    /// <summary>Case-insensitive trigger-area lookup by name. Returns the area and
    /// the map that owns it (useful for multi-map scenes).</summary>
    private static TilemapTriggerArea? FindTriggerArea(Tilemap2D? map, string name, out Tilemap2D? owner)
    {
        owner = map;
        if (map == null || string.IsNullOrWhiteSpace(name)) return null;
        foreach (var t in map.TriggerAreas)
        {
            if (t != null && string.Equals(t.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                return t;
        }
        return null;
    }

    /// <summary>Every tilemap visible in the scene via Map2D objects: the ACTIVE map
    /// first (the usual single-map case), then all other maps. Physics + trigger
    /// evaluation enumerate this so multi-map 2D levels apply everywhere — a solid
    /// tile or portal painted on ANY map level works no matter which map is active.</summary>
    public static IEnumerable<Tilemap2D> EnumerateSceneMaps(
        DarkEngine3D_gl_csharp.Engine.Objects.EditorObjectManager? manager, Tilemap2D? activeMap)
    {
        if (activeMap != null) yield return activeMap;
        if (manager != null)
        {
            foreach (var o in manager.Objects)
            {
                if (o is not { IsVisible: true, PrimitiveType: DarkEngine3D_gl_csharp.Engine.Objects.EditorPrimitiveType.Map2D }) continue;
                var m = o.Map2dTilemap;
                if (m == null || ReferenceEquals(m, activeMap)) continue;
                yield return m;
            }
        }
    }

    /// <summary>Re-pin the checkpoint/save-slot snapping map after a multi-map
    /// trigger pass (Update overwrites _activeMap on every call; the ACTIVE map is
    /// the authoritative one for checkpoints).</summary>
    public static void PinActiveMap(Tilemap2D? map) => _activeMap = map;

    /// <summary>Maps the portal destination search may consult: every Map2D object in
    /// the scene (the ACTIVE map first — the usual single-map case) plus any map
    /// advertised by the runtime (extra maps). Route names are not resolved here:
    /// the loading pipeline swaps the whole active scene, so portals resolve against
    /// what is actually loaded.</summary>
    private static IEnumerable<Tilemap2D> EnumerateCandidateMaps(Tilemap2D? activeMap)
    {
        if (activeMap != null)
            yield return activeMap;
        var mgr = LastEditorObjectManager;
        if (mgr != null)
        {
            foreach (var o in mgr.Objects)
            {
                if (o is not { IsVisible: true, PrimitiveType: DarkEngine3D_gl_csharp.Engine.Objects.EditorPrimitiveType.Map2D }) continue;
                var m = o.Map2dTilemap;
                if (m == null || ReferenceEquals(m, activeMap)) continue;
                yield return m;
            }
        }
        if (ExtraMaps != null)
        {
            foreach (var m in ExtraMaps)
            {
                if (m == null || ReferenceEquals(m, activeMap)) continue;
                yield return m;
            }
        }
    }

    private static bool directCoordsHint(string dest) =>
        dest.Contains(',') || char.IsDigit(dest.Trim()[0]);
}
