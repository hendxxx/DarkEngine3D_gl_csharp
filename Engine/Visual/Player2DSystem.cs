using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.IDE;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Minimal capsule-vs-tile physics for Player2D objects in preview/in-game.
/// Applies gravity to every Player2D EditorObject, then resolves the player's
/// feet-anchored capsule AABB against the collision tiles (CollisionTileIds) of
/// EVERY visible Map2D tilemap in the scene (the active map first — multi-map 2D
/// levels: a solid tile painted on ANY map level stops the capsule) using
/// swept-axis resolution: move X, resolve X (walls),
/// then move Y, resolve Y (ground/ceiling). Visible Box objects in the scene
/// also act as solid AABB colliders (capsule vs box): land on top, bump the
/// ceiling, get pushed out sideways. Runs ONLY in preview/in-game — Update is
/// called exclusively from those modes.
///
/// Grid mapping: the renderer bakes grid row `ty` at world Y
/// [(mapH-1-ty)*cell, (mapH-ty)*cell] — row 0 is the TOP of the map. All tile
/// lookups here therefore convert world Y → grid row with the same flip:
/// ty = floor((mapH*cell - worldY - ε) / cell), so a player standing on a tile
/// painted as solid is supported by exactly that tile.
/// </summary>
public static class Player2DSystem
{
    /// <summary>Sub-1 emission accumulator for action-bound follow FX (carry-over
    /// between frames so low rates still emit deterministically).</summary>
    private static float _actionFxEmit;

    // ── Hot-path scratch buffers ──    // Update runs EVERY frame (in-game + preview). The per-frame collection below
    // (boxes, collision maps, layers) used to allocate fresh Lists 60×/s — enqueue
    // pressure the GC pays for with stutter spikes. These buffers are reused; the
    // content is rebuilt each frame (single-threaded update — no aliasing risk).
    private static readonly List<(Vector3 min, Vector3 max)> _scratchBoxes = new();
    private static readonly List<(Tilemap2D map, float cell, List<(TileLayer layer, int idx)> layers)> _scratchMapData = new();
    // CRITICAL BUG + FIX ("physics dead / player falls through everything with 2+ maps"):
    // the first scratch version reused ONE _scratchLayers list for EVERY map in
    // mapData. TryAddCollisionMap(map2) Clear()ed that shared list — and because the
    // tuples in mapData hold the SAME list reference, every ALREADY-ADDED map's
    // layer set became empty instantly → IsSolid() false everywhere → walls and
    // floors vanished mid-physics. A per-map layer list MUST be its own instance:
    // each map takes a pool slot (Clear per-slot per-frame, single-threaded update
    // = no aliasing), referencing each map's OWN TileLayer objects (TileLayer is
    // never mutated here — only the tuple list is).
    private static readonly List<List<(TileLayer layer, int idx)>> _scratchLayerPool = new();
    private static int _scratchLayerPoolIdx;

    // ── 1 Hz collision telemetry (FPS-dip diagnosis) ──
    // Counters accumulate across frames (zero alloc: plain int/float fields), and
    // once per second a single Console.WriteLine flushes+resets them. Kept as
    // STATIC fields so physics stays allocation-free; toggled OFF by default —
    // bridge sets Player2DTelemetryEnabled=true (IDE Settings → Gameplay) to turn
    // the log on.
    public static bool TelemetryEnabled;
    private static float _telemetryTimer;
    private static int _teleFrames;
    private static int _telePlayers;
    private static int _teleMaps;
    private static int _teleCollisionLayers;
    private static long _teleRowsProbed;
    private static long _teleTilesProbed;
    private static long _teleSolidsProbed;
    private static long _teleXPushes;
    private static long _teleLandings;

    /// <summary>Row-window probe counter — call ONCE per row-scanning loop with
    /// the loop's row count (X-push window, landing sweep, MapTopAt, respawn snap).</summary>
    private static void TelRow(int rows)
    {
        if (TelemetryEnabled && rows > 0) _teleRowsProbed += rows;
    }

    /// <summary>Per-frame aggregates → one Console line per second when enabled.
    /// Format: [P2D-Tel] fps~N players M maps K layers L rows R tiles T solid S
    /// xp X land C — rows/tiles per SECOND are the diagnosis signal: a rising line
    /// over time (map grew, more collision layers, multi-map stacking) shows up
    /// BEFORE the FPS actually falls.</summary>
    private static void TelemetryTick(float dt)
    {
        if (!TelemetryEnabled) return;
        _teleFrames++;
        _telemetryTimer += dt;
        if (_telemetryTimer < 1f) return;
        float fps = _teleFrames / MathF.Max(0.001f, _telemetryTimer);
        Console.WriteLine(
            $"[P2D-Tel] fps~{fps:F0} players={_telePlayers}/f maps={_teleMaps} collLayers={_teleCollisionLayers} " +
            $"rows/s={_teleRowsProbed} tiles/s={_teleTilesProbed} solid/s={_teleSolidsProbed} " +
            $"xp/s={_teleXPushes} land/s={_teleLandings}");
        // Reset — one clean 1 s window.
        _telemetryTimer = 0f; _teleFrames = 0;
        _telePlayers = 0; _teleMaps = 0; _teleCollisionLayers = 0;
        _teleRowsProbed = 0; _teleTilesProbed = 0; _teleSolidsProbed = 0;
        _teleXPushes = 0; _teleLandings = 0;
    }

    /// <summary>Advance animation clocks + run physics for all Player2D objects.
    /// Call once per frame from GameScene/SceneManager update when in-game or
    /// preview is active (not in pure edit mode).</summary>
    public static void Update(Objects.EditorObjectManager? manager, Tilemap2D? activeMap, float dt, IDEBridge? bridge)
    {
        if (manager == null) return;
        TelemetryTick(dt);

        // Refresh the trigger runtime's manager reference (bubble actions anchor to
        // the player object found here). Also drives dialogue NPC interaction input.
        TriggerEventSystem.LastEditorObjectManager = manager;
        DialogueSystem.UpdateInteraction(manager, dt);

        // Deferred spawn: in-game entry sets Player2DSpawnPending because the .ing
        // reload re-creates objects AFTER the setter runs. On the first update frame
        // the re-created objects exist — teleport them to the Start2D marker now.
        // LAZY Start2D: the full-object FirstOrDefault scan used to run EVERY frame
        // (only the pit-death path actually consumes the marker on temple frames).
        Objects.EditorObject? start2d = null;
        if (Objects.EditorObject.Player2DSpawnPending)
        {
            foreach (var o in manager.Objects)
                if (o is { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Start2D }) { start2d = o; break; }
            Objects.EditorObject.Player2DSpawnPending = false;
            // New play session: the follow camera must re-snap to its start point.
            Objects.EditorObject.CameraFollowInitialized = false;
            if (start2d != null)
            {
                foreach (var p in manager.Objects)
                {
                    if (p is not { PrimitiveType: Objects.EditorPrimitiveType.Player2D }) continue;
                    // Spawn is CAPSULE-AWARE: Start2D marks where the COLLIDER stands,
                    // so subtract the capsule offset (physics feet = Position.Y + offset).
                    // The sprite anchor follows via its own rendering — collider lands
                    // exactly on the marker regardless of the capsule nudge.
                    p.Position = new System.Numerics.Vector3(
                        start2d.Position.X - p.Player2DCapsuleOffsetX,
                        start2d.Position.Y - p.Player2DCapsuleOffsetY,
                        start2d.Position.Z);
                    p.Player2DVelocityY = 0f;
                    p.Player2DAnimTime = 0f;
                    p.Player2DJumpCutDone = true; // fresh session: no height cut pending
                    // Fresh session: no action should carry over a frozen StopOnFrameEnd
                    // hold from a previous run — start clean in idle.
                    p.Player2DCurrentAction = "";
                    p.Player2DActionHoldingEnd = false;
            // Fresh run: clear any stale walk/facing state carried over from
            // a previous in-game session.
            p.Player2DMoving = false;
            p.Player2DRunning = false;
            p.Player2DFacingRight = true;
                    Console.WriteLine($"[Player2D] Spawned at Start ({p.Position.X:F1}, {p.Position.Y:F1}) (capsule offset {p.Player2DCapsuleOffsetX:F2},{p.Player2DCapsuleOffsetY:F2})");
                    // Fresh session: trigger areas start clean so OnEnter fires the
                    // first time the player crosses each zone. Also resets any saved
                    // checkpoint so Load Checkpoint falls back to this start point.
                    TriggerEventSystem.BeginSession();
                    // Seed the session paperdoll with the player's scene-authored
                    // equipment — BeginSession above just RESET the session state, so
                    // this runs after it. Empty slots only: an in-game Load restores
                    // its own paperdoll via InventorySystem.RestoreState, but that
                    // happens mid-session (spawn already consumed), so in practice the
                    // paperdoll is empty here and the scene load-out applies whole.
                    foreach (var seed in p.Equipment.Slots)
                        if (!InventorySystem.PlayerEquipment.Slots.ContainsKey(seed.Key))
                            InventorySystem.PlayerEquipment.Slots[seed.Key] = seed.Value;
                    if (p.Equipment.Slots.Count > 0) InventorySystem.ApplyEquipmentBonuses();
                    TriggerEventSystem.ResetRuntime(activeMap);
                    // Multi-map: reset the trigger runtime on EVERY scene map so
                    // portals/doors on other levels start the session clean too.
                    foreach (var o in manager.Objects)
                        if (o is { PrimitiveType: Objects.EditorPrimitiveType.Map2D, Map2dTilemap: { } om })
                            TriggerEventSystem.ResetRuntime(om);
                    // Fresh session: the runtime level restarts on the active map.
                    TriggerEventSystem.RuntimeMap = activeMap;
                    // World journal fresh-run: BeginSession cleared it, so nothing to
                    // re-apply here — sprites are clean (chest closed). (Save-load path
                    // re-fills the journal BEFORE spawning and applies below.)
                }
            }
        }

        // ── Scene Box objects = solid AABB colliders (capsule vs box) ──
        // Axis-aligned from Position/Scale (rotation ignored — 2D sidescroller boxes
        // are unrotated). Feet-anchored capsule spans Z: Position.Z ± radius.
        // Buffer-reused (no per-frame List allocation — GC pressure showed as FPS dips).
        var boxes = _scratchBoxes; boxes.Clear();
        foreach (var b in manager.Objects)
        {
            if (b is not { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Box }) continue;
            var half = b.Scale * 0.5f;
            if (half.X <= 0f || half.Y <= 0f || half.Z <= 0f) continue;
            boxes.Add((b.Position - half, b.Position + half));
        }

        // ── Multi-map collision collection (buffer-reused, ONE manager pass) ──
        // Collision applies from EVERY visible Map2D tilemap in the scene (the
        // active map first), each against ALL of its collision-carrying layers.
        // Maps all bake at the world origin — one shared world↔grid mapping — so a
        // collision tile painted on ANY map level stops the capsule wherever it
        // stands (player on map 1, wall on map 3 → still solid).
        // PERF: the maps+layers collection is built ONCE per frame (not per player)
        // into scratch lists; CollectCollisionMaps (which re-enumerated the manager
        // AGAIN plus allocates) folded in here — one pass over manager.Objects covers
        // the active-map pick, collision-map filter, Box colliders and the layer scan.
        var mapData = _scratchMapData; mapData.Clear();
        _scratchLayerPoolIdx = 0;   // pool round-trips per frame (undo the pool index)
        if (activeMap != null) TryAddCollisionMap(activeMap, mapData);
        foreach (var o in manager.Objects)
        {
            if (o is not { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Map2D, Map2dTilemap: { } m }) continue;
            if (ReferenceEquals(m, activeMap)) continue;
            TryAddCollisionMap(m, mapData);
        }
        if (TelemetryEnabled)
        {
            _teleMaps = Math.Max(_teleMaps, mapData.Count);
            int cl = 0; foreach (var md in mapData) cl += md.layers.Count;
            _teleCollisionLayers = Math.Max(_teleCollisionLayers, cl);
        }

        // Pit-death threshold: the largest cell among collision maps (any level);
        // fall back to the active map's cell when nothing carries collision.
        float pitCell = 0f;
        foreach (var md in mapData) pitCell = MathF.Max(pitCell, md.cell);
        if (pitCell <= 0f && activeMap != null)
            pitCell = activeMap.TileSize * Tilemap2D.WorldScale;

        foreach (var player in manager.Objects)
        {
            if (player is not { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Player2D })
                continue;
            if (TelemetryEnabled) _telePlayers++;

            // NOTE: the animation clock advances inside DrawPlayer2D (single source of
            // truth) — updating it here too would double the playback speed.

            // ── Input-driven movement + jump (preview/in-game only) ──
            // A/D or Left/Right move horizontally (Walk by default, Shift = Run);
            // Space/Up jumps when grounded. W/S fly-style vertical movement was
            // REMOVED — W no longer moves the player up (pure platformer controls).
            // Conversation active → freeze ALL player input (movement, run, jump)
            // but keep gravity/physics so the character stays planted mid-dialogue.
            // Same while the EDITOR's RMB-hold freefly owns the camera: A/D would
            // drive the player AND the camera at once (the camera moves, the player
            // must not). RMB only takes the camera in perspective (3D) view — 2D
            // levels keep right-drag = pan, so player input stays live there.
            // (RmbFreeflyActive is only ever set in perspective — the static flag is
            // the single source of truth; no ortho check needed here.)
            bool conversationActive = DialogueSystem.IsConversationActive || ShopHud.IsOpen;
            bool rmbLookOwnsInput = Camera.RmbFreeflyActive;
            float walkSpeed = conversationActive ? 0.001f : MathF.Max(0.1f, player.Player2DMoveSpeed);
            float runSpeed = conversationActive ? 0.001f : MathF.Max(walkSpeed, player.Player2DRunSpeed);
            bool inputFrozen = conversationActive || rmbLookOwnsInput;
            bool left = !inputFrozen && (ImGui.IsKeyDown(ImGuiKey.A) || ImGui.IsKeyDown(ImGuiKey.LeftArrow));
            bool right = !inputFrozen && (ImGui.IsKeyDown(ImGuiKey.D) || ImGui.IsKeyDown(ImGuiKey.RightArrow));
            bool runHeld = !inputFrozen && (ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift));

            // ── Jump trigger resolution (Inspector-aware) ──
            // The built-in jump honors the "Jump" action's Trigger setting, using the
            // same semantics as custom actions (EditorObject.KeyTriggered):
            //   KeyDown      = jump on press  (hold for variable height)
            //   KeyDownOnce  = jump on press, must release before next jump
            //   KeyUp        = jump on release (press-impulse → full arc)
            //   KeyUpOnce    = jump on release, must press before next jump
            // Keys default to Space/Up; the Jump action's own KeyBinding overrides
            // them when set.
            // Physical press is EDGE-detected (wasUp → isDown): ImGui.IsKeyPressed
            // auto-repeats while held (OS key-repeat) which caused jump-spam and an
            // endless jump-action loop. One physical press = one jump.
            // jumpAction lookup — cached on the player (re-faced only when the
            // action list identity changes); the old per-frame FirstOrDefault ran
            // on EVERY physics frame for EVERY player.
            var jumpAction = player.GetJumpActionCached();
            bool jumpOnRelease = jumpAction is { IsKeyUpTrigger: true };
            ImGuiKey jumpBoundKey = ImGuiKey.None;
            if (jumpAction != null && !string.IsNullOrEmpty(jumpAction.KeyBinding)
                && jumpAction.KeyBinding != "None"
                && Enum.TryParse<ImGuiKey>(jumpAction.KeyBinding, out var jumpBk)
                && jumpBk != ImGuiKey.None)
                jumpBoundKey = jumpBk;

            // Unified physical-state probe for whichever key drives the jump.
            bool jkDown, jkPressed, jkReleased;
            if (jumpBoundKey != ImGuiKey.None)
            {
                jkDown = ImGui.IsKeyDown(jumpBoundKey);
                jkPressed = ImGui.IsKeyPressed(jumpBoundKey, false);
                jkReleased = ImGui.IsKeyReleased(jumpBoundKey);
            }
            else
            {
                jkDown = ImGui.IsKeyDown(ImGuiKey.Space) || ImGui.IsKeyDown(ImGuiKey.UpArrow);
                jkPressed = ImGui.IsKeyPressed(ImGuiKey.Space, false) || ImGui.IsKeyPressed(ImGuiKey.UpArrow, false);
                jkReleased = ImGui.IsKeyReleased(ImGuiKey.Space) || ImGui.IsKeyReleased(ImGuiKey.UpArrow);
            }
            bool jumpEdge = !player.Player2DJumpKeyWasDown && jkDown; // physical press edge (repeat-proof)
            player.Player2DJumpKeyWasDown = jkDown;

            bool jumpPressed;
            if (jumpAction is { IsKeyDownOnceTrigger: true })
                jumpPressed = jumpEdge;
            else if (jumpAction is { IsKeyUpOnceTrigger: true })
                jumpPressed = jkReleased; // release edge is already one-shot by nature
            else if (jumpOnRelease)
                jumpPressed = jkReleased;
            else
                jumpPressed = jumpEdge; // KeyDown (default): press edge, hold = variable height
            jkDown &= !inputFrozen;
            jumpPressed &= !inputFrozen;
            bool jump = jumpPressed;
            float targetVx = 0f;

            static bool PortalKeyProbeCached(string? keyName)
            {
                return Enum.TryParse<ImGuiKey>(keyName, out var k) && k != ImGuiKey.None
                    && ImGui.IsKeyPressed(k, false);
            }
            if (left && !right) targetVx = -(runHeld ? runSpeed : walkSpeed);
            else if (right && !left) targetVx = runHeld ? runSpeed : walkSpeed;
            // Walk vs Run is a STATE (speed modifier held + actually moving), consumed by
            // ComputeLocomotionDesired for the animation action choice. Shift while still
            // (or mid-air with no horizontal input) is NOT a run.
            player.Player2DRunning = runHeld && targetVx != 0f;

            // Accelerate/decelerate toward the target (air control scales acceleration).
            float accel = player.Player2DAcceleration;
            if (!player.Player2DGrounded) accel *= Math.Clamp(player.Player2DAirControl, 0f, 1f);
            if (targetVx == 0f) accel = player.Player2DDeceleration;
            float velX = player.Player2DVelocityX;
            if (velX < targetVx) velX = MathF.Min(targetVx, velX + accel * dt);
            else if (velX > targetVx) velX = MathF.Max(targetVx, velX - accel * dt);
            player.Player2DVelocityX = velX;
            if (velX > 0.05f) player.Player2DFacing = 1f;
            else if (velX < -0.05f) player.Player2DFacing = -1f;

            // Legacy anim flags (HEAD side): the simple walk/idle clip resolver keys off
            // Player2DMoving + Player2DFacingRight, so keep them in sync with velocity.
            player.Player2DMoving = MathF.Abs(velX) > 0.05f;
            if (velX > 0f) player.Player2DFacingRight = true;
            else if (velX < 0f) player.Player2DFacingRight = false;
            // ── Platformer jump feel: coyote time + jump buffer + variable height ──
            // Timers advance per-frame against the live params (both 0 = classic strict
            // grounded-jump behavior, so the feature never fights the old tuning).
            // Physical key hold drives variable jump height (works for both trigger modes;
            // for KeyUp triggers the hold phase was the press, so hold-state is unused).
            bool jumpHeld = jkDown;
            float coyoteMax = MathF.Max(0f, player.Player2DCoyoteTime);
            float bufferMax = MathF.Max(0f, player.Player2DJumpBuffer);
            player.Player2DCoyoteTimer = player.Player2DGrounded ? coyoteMax : MathF.Max(0f, player.Player2DCoyoteTimer - dt);
            if (jump) player.Player2DJumpBufferTimer = bufferMax;
            else player.Player2DJumpBufferTimer = MathF.Max(0f, player.Player2DJumpBufferTimer - dt);

            bool canCoyoteJump = player.Player2DGrounded || player.Player2DCoyoteTimer > 0f;
            if (player.Player2DJumpBufferTimer > 0f && canCoyoteJump)
            {
                // Consume the buffered press + burn coyote so one press can't double-fire.
                player.Player2DJumpBufferTimer = 0f;
                player.Player2DCoyoteTimer = 0f;
                player.Player2DVelocityY = MathF.Max(1f, player.Player2DJumpForce);
                player.Player2DGrounded = false;
                // Re-arm the release cut for this arc — EXCEPT release-triggered jumps:
                // the key is already UP when the impulse fires, so the cut would halve
                // every hop. Release-jumps get a full arc (hold phase was the press).
                player.Player2DJumpCutDone = jumpOnRelease;
                // Jump action fires automatically (priority-gated).
                player.TryStartAction("Jump");
            }
            // Variable jump height: releasing the key mid-rise cuts upward velocity ONCE
            // (short tap = short hop, hold = full height; multiplier 1 = fixed arc).
            if (!jumpHeld && !player.Player2DJumpCutDone && player.Player2DVelocityY > 0f)
            {
                float cut = Math.Clamp(player.Player2DJumpCutMultiplier, 0.05f, 1f);
                player.Player2DVelocityY *= cut;
                player.Player2DJumpCutDone = true;
            }

            // ── User-bound action keys (Inspector: Attack = J, Block = K, ...) ──
            // Track whether the bound key for the currently active action is still held —                // used by ResolveLocomotionAction to release the action when the key goes up.
            bool currentActionKeyHeld = false;
            foreach (var act in player.Actions)
            {
                // "Jump" is driven entirely by the physics block above (coyote/buffer).
                // Processing it here would double-fire TryStartAction and corrupt
                // currentActionKeyHeld, preventing locomotion from resolving.
                if (string.Equals(act.Name, "Jump", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(act.KeyBinding) || act.KeyBinding == "None") continue;
                if (Enum.TryParse<ImGuiKey>(act.KeyBinding, out var k) && k != ImGuiKey.None)
                {
                // Trigger mode: KeyDown fires on press; KeyUp fires on RELEASE —
                // a press-impulse action (charge up, release to swing/spin).
                if (act.KeyTriggered(k))
                {
                    bool started = player.TryStartAction(act.Name);
                    // Action-bound projectile (opt-in via the Inspector "Projectile →
                    // Sheet ≠ None"): launch when the action starts (K → fireball).
                    if (started && act.ProjectileEnabled)
                        Projectile2DSystem.SpawnFromAction(player, act, manager,
                            act.ProjectileOffsetX, act.ProjectileOffsetY,
                            act.ProjectileSpeed, act.ProjectileMaxDistance,
                            act.ProjectileGravity, act.ProjectileVelY,
                            act.ProjectileWorldHeight, act.ProjectileDamageHP, act.ProjectileDamageMP,
                            act.ProjectileHitSheet, act.ProjectileHitClip, act.ProjectileHitScale,
                            act.ProjectileHitFx, act.ProjectileHitFxScale,
                            act.ProjectileRotateToVelocity, act.ProjectilePiercing);
                }
                    if (act.Name == player.Player2DCurrentAction)
                        // Hold logic per trigger mode:
                        // - KeyDown: held while key is down (hold = keep playing)
                        // - KeyDownOnce: one-shot on press, never held (fire once then done, e.g. Jump)
                        // - KeyUp: held while key is released (persists until re-press)
                        // - KeyUpOnce: one-shot on release, never held (fire once then done)
                        if (act.IsKeyUpOnceTrigger || act.IsKeyDownOnceTrigger)
                            currentActionKeyHeld = false; // one-shot: don't persist as held
                        else if (act.IsKeyUpTrigger)
                            currentActionKeyHeld = !ImGui.IsKeyDown(k);
                        else
                            currentActionKeyHeld = ImGui.IsKeyDown(k);
                }
            }

            // Auto-resolve locomotion action (idle/walk/run) to match current state.
            // Idle when grounded+still, Walk/Run by velocity. Pass whether the currently
            // active key-bound action's key is still held — if released, locomotion takes
            // over (e.g. Run bound to J: hold = Run, release = back to Idle/Walk).
            player.ResolveLocomotionAction(currentActionKeyHeld);

            // ── Action-bound follow FX (fireball trail, magic aura…): stream particles
            // from the character while the action is active. One-shot FX burst inside
            // TryStartAction; this handles the Follow style via the runtime anchor.
            // PERF: plain foreach — FirstOrDefault allocated a closure + enumerator
            // per frame per player; the name rarely repeats frame-to-frame so a plain
            // loop with early exit is allocation-free and just as fast.
            Player2DAction? fxCur = null;
            foreach (var fa in player.Actions)
            {
                if (fa.Name == player.Player2DCurrentAction) { fxCur = fa; break; }
            }
            if (fxCur != null && !string.IsNullOrWhiteSpace(fxCur.FxPreset) && fxCur.FxFollow)
            {
                var anchor = new Vector3(player.Position.X,
                    player.Position.Y + player.Player2DHeight * 0.5f, player.Position.Z + 0.05f);
                player.Effect2DRuntimeAnchor = anchor;
                var fxCfg = Effect2DSystem.WithSizeScale(
                    Effect2DSystem.PresetConfig(fxCur.FxPreset.Trim()),
                    MathF.Max(0.01f, fxCur.FxScale));
                fxCfg.Pos = anchor;
                _actionFxEmit += fxCfg.Rate * dt;
                while (_actionFxEmit >= 1f) { _actionFxEmit -= 1f; Effect2DSystem.Spawn(fxCfg); }
            }
            else
                _actionFxEmit = 0f;

            var pos = player.Position;
            float r = player.Player2DCapsuleRadius;
            float height = player.Player2DCapsuleHeight;
            float capZMin = pos.Z - r, capZMax = pos.Z + r;
            // Capsule offset: the physics body can be shifted relative to the object
            // position (same values the capsule gizmo draws with). capX is the capsule's
            // horizontal CENTER; feet sit at pos.Y + capOffY. All probes/resolves below
            // use these, and results are converted back into pos.X/pos.Y.
            float capOffX = player.Player2DCapsuleOffsetX;
            float capOffY = player.Player2DCapsuleOffsetY;
            float capX = pos.X + capOffX;

            // ════════════════════════════════════════════════════════════
            // STEP 1 — Move X, resolve X (walls) via swept AABB.
            // ════════════════════════════════════════════════════════════
            float newX = pos.X + velX * dt;

            // Vertical span covered during the X sweep (Y is unchanged here).
            float yBot = pos.Y + capOffY;
            float yTop = pos.Y + capOffY + height;

            // X sweep against EVERY map's collision tiles. The push is Y-aware —
            // only tiles whose world band overlaps the capsule [yBot, yTop] block —
            // so sweeping other maps with all their rows can't phantom-block from a
            // solid tile far above the head in the same column.
            // STEP-UP (user: "tanjakan sedikit langsung naik saja") — walking INTO a
            // collision tile whose (possibly interpolated) top is only ≤ StepHeight
            // above the feet LIFTS the feet onto that top instead of stopping the
            // character; the Y-resolve below keeps grounding. Only while grounded and
            // moving — a jump passes over anything taller anyway.
            float stepH = MathF.Max(0f, player.Player2DStepHeight);
            bool steppedUp = false;
            if (stepH > 0f && player.Player2DGrounded && velX != 0f)
            {
                float probeX = (velX > 0f ? newX + capOffX + r + 0.05f : newX + capOffX - r - 0.05f);
                float aheadTop = MapTopAt(mapData, probeX, yBot + stepH);
                // aheadTop within [yBot, yBot+stepH] → rise the feet onto it.
                if (aheadTop > yBot + 0.005f && aheadTop <= yBot + stepH && aheadTop < yTop)
                {
                    float dy = aheadTop - yBot;
                    yBot += dy; yTop += dy;
                    steppedUp = true;
                }
            }

            if (velX != 0f)
            {
                float dir = MathF.Sign(velX);

                // ── SWEPT WALL SWEEP (multi-column tunneling fix) ──
                // The old check tested ONLY the destination column (gxEdge from newX):
                // at run/jump speed the capsule can skip 2+ columns in ONE frame, so a
                // wall standing in a skipped MIDDLE column was never tested → the
                // player phased through it ("works walking, fails running/jumping" —
                // walking never skips a column, so it was the ONLY safe case).
                // Fix: walk the leading edge column by column from the START column to
                // the end column, pushing back at the FIRST solid wall. Each map is
                // swept independently (different cell sizes = different column counts).
                // Cap the iterations (32) as a runaway guard — even a maxed run speed on
                // a tiny-cell map stays well below it.
                foreach (var (cmap, ccell, clayers) in mapData)
                {
                    const int maxCols = 32;
                    // Leading-edge column BEFORE and AFTER the move (per-map cell size).
                    int gxOldEdge = (int)MathF.Floor((capX + dir * r) / ccell);
                    int gxNewEdge = (int)MathF.Floor((newX + capOffX + dir * r) / ccell);
                    int span = Math.Abs(gxNewEdge - gxOldEdge);

                    // Walk the NEWLY-ENTERED columns in order. Slow move → span==0
                    // (edge stays in the same column): the DESTINATION-EDGE column
                    // MUST still be tested — `TryResolveXPush` only blocks when feet
                    // are BELOW the (ramp-lowered) top, so retesting the edge's own
                    // column never double-blocks a legal stance and keeps the old
                    // behavior for the slow case (step-up/launch frames where the
                    // edge partially dips into a wall column still push back).
                    int cols = Math.Clamp(span, 1, maxCols);
                    for (int c = 1; c <= cols; c++)
                    {
                        int gxC = span > 0
                            ? (dir > 0f ? gxOldEdge + c : gxOldEdge - c)
                            : gxNewEdge;             // span 0: test the edge's own column
                        TryResolveXPush(cmap, ccell, clayers, gxC, yBot, yTop, dir, r, capOffX, ref newX);
                        if (TelemetryEnabled) _teleXPushes++;

                        // A wall push rewrites newX — if that pulled the leading edge
                        // back to/before the column we just tested, the sweep is done
                        // (further columns are behind the wall now).
                        int gxNow = (int)MathF.Floor((newX + capOffX + dir * r) / ccell);
                        int remain = dir > 0f ? gxNewEdge - gxNow : gxNow - gxNewEdge;
                        if (remain <= 0)
                            break;
                    }
                }
            }

            // Box walls along X (same pass, least-penetration style push kept from
            // the original behavior but applied to the swept position).
            // TUNNELING FIX for boxes too: the overlap test used only the END position
            // — a thin box could be crossed entirely in one fast frame (start edge left
            // of the box, end edge right of it = both tests false = pass-through).
            // Correct test = the interval [old edge, new edge] overlaps the box span.
            foreach (var (bmin, bmax) in boxes)
            {
                bool overlapZ = capZMax > bmin.Z && capZMin < bmax.Z;
                bool overlapY = yTop > bmin.Y + 0.001f && yBot < bmax.Y - 0.001f;
                if (!overlapZ || !overlapY) continue;

                float capOldL = capX - r, capOldR = capX + r;      // pre-move span
                float capNewL = newX + capOffX - r, capNewR = newX + capOffX + r;
                // Swept X span = [min(oldL,newL), max(oldR,newR)] — overlap with the
                // box = the interval intersection test.
                bool sweptHit = MathF.Min(capOldL, capNewL) < bmax.X
                             && MathF.Max(capOldR, capNewR) > bmin.X;
                if (!sweptHit) continue;
                if (capNewR > bmin.X && capNewL < bmax.X)
                {
                    float pushLeft = bmin.X - capNewR;    // negative: eject left
                    float pushRight = bmax.X - capNewL;   // positive: eject right
                    newX += MathF.Abs(pushLeft) < MathF.Abs(pushRight) ? pushLeft : pushRight;
                }
            }

            pos.X = newX;

            // ════════════════════════════════════════════════════════
            // STEP 2 — Gravity + move Y, resolve Y (ground/ceiling).
            // ════════════════════════════════════════════════════════
            player.Player2DVelocityY -= player.Player2DGravity * MathF.Max(0f, player.Player2DGravityScale) * dt;
            // Fall action while airborne and descending (auto-released on landing by action loop rules).
            // PERF: FirstOrDefault over Actions every airborne frame replaced by an
            // edge-flag (actions don't change per frame; only the cache flag needs refresh).
            bool hasFallAction = player.Player2DHasFallAction;
            if (!hasFallAction)
            {
                hasFallAction = player.Actions.Any(a => a.Name == "Fall");
                player.Player2DHasFallAction = hasFallAction;
            }
            if (player.Player2DVelocityY < -0.5f && !player.Player2DGrounded && hasFallAction
                && player.Player2DCurrentAction == "")
                player.TryStartAction("Fall");
            float newY = pos.Y + player.Player2DVelocityY * dt;
            // STEP-UP carry: yBot/yTop were already RAISED next to the step — persist
            // that lift as a floor so gravity doesn't instantly re-pull through it; the
            // falling probe below re-lands the feet exactly on the ramp top.
            float stepFloor = steppedUp ? yBot - capOffY : float.MinValue;
            if (newY < stepFloor) newY = stepFloor;

            bool grounded = steppedUp; // a step-up frame is grounded (feet on the suffered ramp top)

            if (player.Player2DVelocityY <= 0f)
            {
                // ── Falling: swept feet probe (per map) from old feet to new feet ──
                // Check every row the feet cross (topmost solid wins) plus the row
                // the new feet rest in, so tunneling can't slip through a tile.
                // Across maps the HIGHEST qualifying surface wins (a bridge tile on
                // map 3 lands the player even with map 1 open below).
                float? landTop = null;
                // Probe columns use the RESOLVED X (newX, cap center) — a step-up frame
                // moved the capsule INTO the new column, so landing on the ramp top must
                // consider where the capsule ended up, not where it started.
                float landCapX = newX + capOffX;
                foreach (var (cmap, ccell, clayers) in mapData)
                {
                    int gx0 = WorldColFloor(cmap, landCapX - r + 0.001f, ccell);
                    int gx1 = WorldColFloor(cmap, landCapX + r - 0.001f, ccell);
                    int rowFeetNew = WorldRowFloor(cmap, newY + capOffY, ccell);
                    int rowFeetOld = WorldRowFloor(cmap, pos.Y + capOffY, ccell);
                    int rowTop = Math.Min(rowFeetOld, rowFeetNew); // smallest index = highest band
                    int rowBottom = Math.Max(rowFeetOld, rowFeetNew);
                    TelRow(rowBottom - rowTop + 1);              // landing sweep rows
                    // Cross rows in the order the feet pass them: highest band first.
                    for (int gy = rowTop; gy <= rowBottom; gy++)
                    {
                        bool solid = false;
                        foreach (var (collisionLayer, layerIdx) in clayers)
                        {
                            for (int gx = gx0; gx <= gx1; gx++)
                            {
                                if (IsSolid(cmap, collisionLayer, layerIdx, gx, gy))
                                {
                                    solid = true;
                                    break;
                                }
                            }
                            if (solid) break;
                        }
                        if (!solid) continue;

                        // RAMP-AWARE top: sample the interpolated tile top at the
                        // capsule edge columns (the ramp slope spans the cell); the
                        // higher edge is this frame's landing surface candidate.
                        // BUGFIX ("stuck at the ramp base"): only edge columns that
                        // actually carry a COLLISION tile in this row may contribute.
                        // TileWorldTopAt falls back to the row's full-height baseTop
                        // for a missing/empty tile — while one capsule edge stands on
                        // a lowered RAMP top, the other edge sitting over an EMPTY
                        // column reported that phantom full top, outranked the real
                        // ramp surface, failed the "came from above" test, and the
                        // Y-resolve then snapped the feet back onto the flat ground
                        // row below → the player jittered in place and never climbed.
                        // An empty edge now contributes NOTHING (−∞).
                        float tileTop = float.NegativeInfinity;
                        foreach (var (collisionLayer, layerIdx) in clayers)
                        {
                            // NOTE: gx1 MAY equal gx0 (capsule narrower than a cell) — the two
                            // SAMPLE X positions still differ, and inside one ramp cell the left
                            // sample can be the wedge's LOW side: both must be sampled so the
                            // resting height is the HIGHEST surface point under the capsule.
                            if (IsSolid(cmap, collisionLayer, layerIdx, gx0, gy))
                                tileTop = MathF.Max(tileTop,
                                    TileWorldTopAt(cmap, collisionLayer, layerIdx, gx0, gy, ccell, landCapX - r + 0.001f));
                            if (IsSolid(cmap, collisionLayer, layerIdx, gx1, gy))
                                tileTop = MathF.Max(tileTop,
                                    TileWorldTopAt(cmap, collisionLayer, layerIdx, gx1, gy, ccell, landCapX + r - 0.001f));
                        }
                        if (tileTop == float.NegativeInfinity) continue; // no edge column solid here
                        // STEP-UP frame: feet were lifted by the X pass this frame — the
                        // landing check must compare against the LIFTED feet (yBot), not
                        // the frame-start feet (pos.Y + capOffY), or the ramp top reads
                        // as "came from below" and the step never lands → stuck.
                        if (yBot >= tileTop - 0.01f)
                        {
                            // Came from above → candidate landing on this tile's top.
                            if (!landTop.HasValue || tileTop > landTop.Value) landTop = tileTop;
                            break; // topmost qualifying band of THIS map
                        }
                        // Feet started inside/below this band's top edge → not a landing
                        // surface; keep checking lower rows.
                    }
                }
                if (landTop.HasValue)
                {
                    newY = landTop.Value - capOffY;
                    player.Player2DVelocityY = 0f;
                    grounded = true;
                    if (TelemetryEnabled) _teleLandings++;
                }
            }
            else
            {
                // ── Rising: swept head probe (per map) from old head to new head ──
                // Across maps the LOWEST qualifying ceiling wins.
                float? ceilBottom = null;
                foreach (var (cmap, ccell, clayers) in mapData)
                {
                    int gx0 = WorldColFloor(cmap, capX - r + 0.001f, ccell);
                    int gx1 = WorldColFloor(cmap, capX + r - 0.001f, ccell);
                    int rowHeadNew = WorldRowFloor(cmap, newY + capOffY + height, ccell);
                    int rowHeadOld = WorldRowFloor(cmap, pos.Y + capOffY + height, ccell);
                    int rowTop = Math.Min(rowHeadOld, rowHeadNew);
                    int rowBottom = Math.Max(rowHeadOld, rowHeadNew);
                    // Cross rows in the order the head passes them: lowest band first.
                    for (int gy = rowBottom; gy >= rowTop; gy--)
                    {
                        bool solid = false;
                        foreach (var (collisionLayer, layerIdx) in clayers)
                        {
                            for (int gx = gx0; gx <= gx1; gx++)
                            {
                                if (IsSolid(cmap, collisionLayer, layerIdx, gx, gy))
                                {
                                    solid = true;
                                    break;
                                }
                            }
                            if (solid) break;
                        }
                        if (!solid) continue;

                        float tileBottom = TileWorldMinY(cmap, gy, ccell);
                        if (pos.Y + capOffY + height <= tileBottom + 0.01f)
                        {
                            // Came from below → candidate ceiling on this tile's bottom.
                            if (!ceilBottom.HasValue || tileBottom < ceilBottom.Value) ceilBottom = tileBottom;
                            break; // lowest qualifying band of THIS map
                        }
                        // Head started above this band's bottom edge → not a ceiling here;
                        // keep checking higher rows.
                    }
                }
                if (ceilBottom.HasValue)
                {
                    newY = ceilBottom.Value - height - capOffY;
                    player.Player2DVelocityY = 0f;
                }
            }

            // ── Box vertical resolve: land on top / bump ceiling ──
            // Same swept logic as tiles but against scene Box AABBs. Requires the
            // capsule to overlap the box in X/Z, and the previous position to have
            // been OUTSIDE the box along the movement axis (so walking into a side
            // doesn't teleport the player onto the top — the X pass handles that).
            foreach (var (bmin, bmax) in boxes)
            {
                bool overlapXZ = capX + r > bmin.X && capX - r < bmax.X
                    && capZMax > bmin.Z && capZMin < bmax.Z;
                if (!overlapXZ) continue;

                if (player.Player2DVelocityY <= 0f)
                {
                    // Falling: feet crossed the box top surface this frame.
                    if (newY + capOffY < bmax.Y && newY + capOffY > bmin.Y && pos.Y + capOffY >= bmax.Y - 0.01f)
                    {
                        newY = bmax.Y - capOffY;
                        player.Player2DVelocityY = 0f;
                        grounded = true;
                    }
                }
                else
                {
                    // Rising: head crossed the box bottom surface this frame.
                    if (newY + capOffY + height > bmin.Y && newY + capOffY + height < bmax.Y && pos.Y + capOffY + height <= bmin.Y + 0.01f)
                    {
                        newY = bmin.Y - height - capOffY;
                        player.Player2DVelocityY = 0f;
                    }
                }
            }

            pos.Y = newY;
            player.Player2DGrounded = grounded;
            player.Position = pos;

            // ── Trigger areas: pass-through event volumes (capsule overlaps fire
            // OnEnter / OnStay / OnExit actions). Runs AFTER physics so the overlap
            // test uses the resolved position of this frame. Triggers never block —
            // detection only.
            TriggerEventSystem.LastPlayerPosition = pos;
            // Portal button mode: edge-detect the interact key so holding it doesn't
            // re-fire every frame. Default edge = E; the probe lets each portal use its
            // OWN configured key (PortalEnterKey, ImGuiKey name).
            bool portalKeyE = ImGui.IsKeyPressed(ImGuiKey.E, false);
            // PERF: the probe lambda + Enum.TryParse per frame → ONE cached delegate
            // (stateless static) assigned once per player loop instead of a fresh
            // closure built 60×/s. Semantics identical.
            TriggerEventSystem.PortalKeyProbe = PortalKeyProbeCached;
            // Trigger areas are map-local pixel rects mapped through each map's own
            // cell — evaluate EVERY visible scene map so portals/doors on other map
            // levels fire too (active map first). Then re-pin the checkpoint/snapping
            // map back to the ACTIVE one (Update overwrites it on every call).
            var feetPos = new Vector3(pos.X + capOffX, pos.Y + capOffY, pos.Z);
            foreach (var tmap in TriggerEventSystem.EnumerateSceneMaps(manager, activeMap))
                TriggerEventSystem.Update(tmap, feetPos, r, height, dt, velX, capOffX, capOffY, portalKeyE);
            TriggerEventSystem.PinActiveMap(activeMap);

            // ── Pit death: the world origin (0,0) is the map's bottom-left, so any
            // Y well below zero means the player fell through a hole. Respawn at the
            // saved checkpoint (trigger action) or the Start2D marker when none —
            // instead of falling forever (capsule-aware, same as spawn). ──
            // CheckpointPosition is stored in OBJECT space by SaveCheckpoint, so the
            // respawn must NOT subtract the capsule offsets again (double-applying the
            // offset put the player back BELOW the map → infinite respawn loop).
            if (pitCell > 0f && pos.Y < -pitCell * 2f)
            {
                // Feet-space target: object X + offset, ground-snapped onto the top of
                // the collision column at that X — never inside a tile, never floating.
                float feetX = pos.X + capOffX;
                if (TriggerEventSystem.CheckpointPosition.HasValue)
                    feetX = TriggerEventSystem.CheckpointPosition.Value.X + player.Player2DCapsuleOffsetX;
                else if (start2d != null)
                    feetX = start2d.Position.X + player.Player2DCapsuleOffsetX;

                float feetY;
                if (TriggerEventSystem.CheckpointPosition.HasValue)
                    feetY = TriggerEventSystem.CheckpointPosition.Value.Y + player.Player2DCapsuleOffsetY;
                else if (start2d != null)
                    feetY = start2d.Position.Y + player.Player2DCapsuleOffsetY;
                else
                    feetY = 0f;
                feetY = SnapFeetToGroundAny(mapData, feetX, feetY);

                player.Position = new System.Numerics.Vector3(
                    feetX - player.Player2DCapsuleOffsetX,
                    feetY - player.Player2DCapsuleOffsetY,
                    TriggerEventSystem.CheckpointPosition.HasValue ? TriggerEventSystem.CheckpointZ
                        : (start2d?.Position.Z ?? pos.Z));
                player.Player2DVelocityY = 0f;
                player.Player2DGrounded = false;
                player.Player2DJumpCutDone = true;
                // Respawn faces right in the idle state (standard sidescroller reset).
                player.Player2DMoving = false;
                player.Player2DFacingRight = true;
                Console.WriteLine($"[Player2D] Fell below the map — respawned at {(TriggerEventSystem.CheckpointPosition.HasValue ? "checkpoint" : "Start")} ({player.Position.X:F1}, {player.Position.Y:F1})");
            }
        }
        // Delayed trigger actions (queued with a Delay) tick every frame.
        TriggerEventSystem.TickDelayed(dt);

        // ── Particle effects: advance emitters + rain + integrate (render happens in
        // EditorObjectManager's 2D pass). Preview/in-game only, like physics above.
        Effect2DSystem.Tick(manager, activeMap, bridge?.Camera, dt);

        // ── Projectiles: integrate + collide + expire (render in the manager 2D pass).
        Projectile2DSystem.Tick(manager, TriggerEventSystem.EnumerateSceneMaps(manager, activeMap), dt);

        // ── Inventory feet anchor: use/equip FX bursts and future loot pickups spawn
        // at the MAIN player's feet (first visible Player2D).
        foreach (var o in manager.Objects)
        {
            if (o is { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Player2D })
            {
                InventorySystem.PlayerFeetX = o.Position.X;
                InventorySystem.PlayerFeetY = o.Position.Y;
                break;
            }
        }

        // ── World loot vacuum: drops pop, sit on the ground, and accelerate into the
        // player once inside the magnet radius (auto-collect + FX + flash). Runs AFTER
        // the feet anchor refresh so the pull targets the live position.
        InventorySystem.TickDrops(manager, activeMap, dt);

        // Keep the trigger runtime's start-point fallback + teleport handler fresh.
        // Load Checkpoint without a saved checkpoint teleports to this fallback
        // (the Start2D marker position, feet-anchored).
        if (start2d != null)
            TriggerEventSystem._fallbackSpawn = new Vector2(start2d.Position.X, start2d.Position.Y);

        // Live session → Player Info panel + UI Bars read fresh values.
        Player2DStats.SessionActive = true;
        TriggerEventSystem.RuntimeMap ??= activeMap; // session start: runtime level = active map
        TriggerEventSystem.OnTeleportPlayer = (target, z, destMap) =>
        {
            // Portals whose destination lives on ANOTHER scene map switch the runtime
            // LEVEL (checkpoint snapping re-targets the new map). The CAMERA is
            // deliberately NOT touched — no re-frame, no jump: the smooth follow
            // carries it to the arrival point like any other movement (user rule:
            // "camera tetap jangan berubah" saat pindah map level).
            if (destMap != null)
                TriggerEventSystem.RuntimeMap = destMap;
            foreach (var p in manager.Objects)
            {
                if (p is not { PrimitiveType: Objects.EditorPrimitiveType.Player2D }) continue;
                p.Position = new System.Numerics.Vector3(
                    target.X - p.Player2DCapsuleOffsetX,
                    target.Y - p.Player2DCapsuleOffsetY,
                    z);
                p.Player2DVelocityY = 0f;
                p.Player2DGrounded = false;
            }
        };

        // ── Platformer camera follow ──
        // Smooth follow (FollowSpeed), dead zone (DeadZoneWidth/Height), vertical
        // threshold (camera rises only above VerticalThreshold px, returns with
        // ReturnSpeed), look-ahead (LookAhead px toward the facing), world boundary
        // (clamped to the map's painted-tile extent), Camera Start Point (position +
        // zoom) honored on entry. Runs ONLY in preview/in-game.
        // The camera NEVER re-targets on level switches (portal to another map):
        // framing + world bounds stay tied to the session's ACTIVE map and the
        // smooth follow carries the camera to the player's arrival point (user rule:
        // "camera tetap jangan berubah" saat pindah map level).
        if (bridge?.Camera != null && activeMap != null)
        {
            var first = manager.Objects.FirstOrDefault(o =>
                o is { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Player2D });
            var camStart = manager.Objects.FirstOrDefault(o =>
                o is { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.CameraStart2D });
            var cam = bridge.Camera;

            // Session entry: frame the map so the grid origin (0,0) sits at the
            // bottom-left of the viewport. The ortho camera's Position is the VIEW
            // CENTER, so we offset it by (±halfView) to put (0,0) at the corner.
            if (!Objects.EditorObject.CameraFollowInitialized)
            {
                Objects.EditorObject.CameraFollowInitialized = true;
                cam.IsOrthographic = true;

                // Vertical framing: choose an ortho half-height so the map's bottom
                // (world Y = 0) is at the bottom edge of the view. Use at least the
                // full map height so nothing is cut off; a zoom factor can shrink it
                // when a CameraStart2D marker requests a tighter view.
                float mapHWorld = activeMap.Height * PxToWorld(activeMap);
                float startZoom = camStart != null && camStart.Scale.Y > 0.1f ? camStart.Scale.Y : 1f;
                float halfH = MathF.Max(mapHWorld * 0.5f, 1f) / startZoom;
                cam.OrthoSize = MathF.Max(0.5f, halfH);

                // Horizontal: match the view width to the window aspect so (0,0)
                // stays flush at the left edge.
                float halfW = halfH * cam.GetAspect();

                // View center that puts world (0,0) at the bottom-left corner:
                //   center.X = 0 + halfW   (left edge of view = world 0)
                //   center.Y = 0 + halfH   (bottom edge of view = world 0)
                Vector3 cornerAnchor = new Vector3(halfW, halfH, 0f);

                // If a Camera Start marker exists, center the view on it (still
                // keeping (0,0) visible at the corner when the map is wider than the
                // view). Otherwise center on the first player so they start in-frame.
                Vector3 center;
                if (camStart != null)
                {
                    center = camStart.Position;
                    // Re-derive half extents from the marker zoom so the start view is
                    // consistent with the marker's requested tightness.
                    halfH = MathF.Max(mapHWorld * 0.5f, 1f) / startZoom;
                    halfW = halfH * cam.GetAspect();
                    cam.OrthoSize = MathF.Max(0.5f, halfH);
                    // Still anchor (0,0) at the corner: shift center so left/bottom = 0.
                    center.X = MathF.Max(halfW, camStart.Position.X);
                    center.Y = MathF.Max(halfH, camStart.Position.Y);
                }
                else if (first != null)
                    center = new Vector3(first.Position.X, first.Position.Y + first.Player2DCapsuleHeight * 0.35f, 0f);
                else
                    center = new Vector3(halfW, halfH, 0f);

                // Frame the view so (0,0) is at the bottom-left corner, then apply the
                // user's view offset on top. The offset shifts the camera position, but
                // we re-clamp the bottom-left corner back to (0,0) so the offset only
                // moves the framed content within the viewport — positive Y lifts the
                // view (content slides down), positive X shifts right (content slides left).
                Vector3 viewCenter = new Vector3(center.X, center.Y, cam.Position.Z);
                // Apply offset to the camera (shifts the whole framed area).
                viewCenter += cam.ViewOffset;
                // Re-pin the bottom-left corner to world (0,0): the view's bottom-left
                // is (viewCenter - (halfW, halfH)), so enforce that >= (0,0).
                viewCenter.X = MathF.Max(viewCenter.X, halfW);
                viewCenter.Y = MathF.Max(viewCenter.Y, halfH);
                cam.Position = viewCenter;
            }
            else if (first != null)
            {
                float followSpeed = MathF.Max(0.1f, first.CameraFollowSpeed);
                float deadW = MathF.Max(0f, first.CameraDeadZoneWidth) * PxToWorld(activeMap);
                float deadH = MathF.Max(0f, first.CameraDeadZoneHeight) * PxToWorld(activeMap);
                float vThreshold = MathF.Max(0f, first.CameraVerticalThreshold) * PxToWorld(activeMap);
                float returnSpeed = MathF.Max(0.1f, first.CameraReturnSpeed);
                float lookAhead = first.CameraLookAhead * PxToWorld(activeMap);

                // Target: player lower-body (feet + a little), offset by look-ahead.
                var target = first.Position + new Vector3(0f, first.Player2DCapsuleHeight * 0.35f, 0f);
                var camPos = cam.Position;

                // ── Horizontal: dead zone around the camera axis, then smooth follow ──
                float desiredX = target.X + first.Player2DFacing * lookAhead;
                float dx = desiredX - camPos.X;
                if (MathF.Abs(dx) > deadW * 0.5f)
                    camPos.X += (dx - MathF.Sign(dx) * deadW * 0.5f) * MathF.Min(1f, followSpeed * dt);

                // ── Vertical: threshold-gated rise, gentle return, dead zone band ──
                float desiredY = target.Y;
                float dy = desiredY - camPos.Y;
                if (dy > 0f)
                {
                    if (dy > vThreshold + deadH * 0.5f)
                        camPos.Y += (dy - vThreshold - deadH * 0.5f) * MathF.Min(1f, followSpeed * dt);
                }
                else
                {
                    if (MathF.Abs(dy) > deadH * 0.5f)
                        camPos.Y += (dy + MathF.Sign(dy) * deadH * 0.5f) * MathF.Min(1f, returnSpeed * dt);
                }

                // ── World boundary: clamp the view inside the map's painted extent.
                // The bottom/left corner of the view is (camPos - halfExtent); keep it
                // at or above world (0,0) so the grid origin never leaves the viewport.
                // World-bound clamp follows the session's RUNTIME level: a portal
                // arrival point lives inside THAT map's painted extent — clamping to
                // the active map would fight the follow after a cross-map teleport.
                ComputeWorldBounds(TriggerEventSystem.RuntimeMap ?? activeMap,
                    out float worldL, out float worldR, out float worldB, out float worldT);
                float halfH = cam.OrthoSize;
                float halfW = halfH * cam.GetAspect();
                if (worldR - worldL > halfW * 2f)
                    camPos.X = MathF.Max(halfW, MathF.Min(worldR - halfW, camPos.X));
                else
                    camPos.X = MathF.Max(halfW, (worldL + worldR) * 0.5f);
                if (worldT - worldB > halfH * 2f)
                    camPos.Y = MathF.Max(halfH, MathF.Min(worldT - halfH, camPos.Y));
                else
                    camPos.Y = MathF.Max(halfH, (worldB + worldT) * 0.5f);

                cam.Position = new Vector3(camPos.X, camPos.Y, cam.Position.Z);

                // Apply the user's view offset, then re-pin the bottom-left corner to
                // world (0,0) so the offset only shifts content within the frame.
                var vOff = cam.ViewOffset;
                cam.Position += vOff;
                // Re-clamp: bottom-left of view = (camPos - (halfW, halfH)) must be >= (0,0).
                cam.Position.X = MathF.Max(cam.Position.X, halfW);
                cam.Position.Y = MathF.Max(cam.Position.Y, halfH);
            }
        }
    }

    /// <summary>Px → world conversion (the map grid uses TileSize × WorldScale).</summary>
    private static float PxToWorld(Tilemap2D map) => map.TileSize * Tilemap2D.WorldScale;

    /// <summary>Compute the world-space bounds of the PAINTED tiles (the valid play area).
    /// Empty maps fall back to the full grid extent. Bounds: left/right in X, bottom/top in Y.</summary>
    private static void ComputeWorldBounds(Tilemap2D map, out float left, out float right,
        out float bottom, out float top)
    {
        float cell = PxToWorld(map);
        int minGx = int.MaxValue, maxGx = int.MinValue, minGy = int.MaxValue, maxGy = int.MinValue;
        foreach (var layer in map.Layers)
        {
            if (layer == null) continue;
            for (int ty = 0; ty < map.Height; ty++)
                for (int tx = 0; tx < map.Width; tx++)
                    if (layer.GetTile(tx, ty) >= 0)
                    {
                        if (tx < minGx) minGx = tx;
                        if (tx > maxGx) maxGx = tx;
                        if (ty < minGy) minGy = ty;
                        if (ty > maxGy) maxGy = ty;
                    }
        }
        if (minGx == int.MaxValue) { minGx = 0; maxGx = map.Width - 1; minGy = 0; maxGy = map.Height - 1; }

        left = minGx * cell;
        right = (maxGx + 1) * cell;
        // Renderer convention: grid row ty spans world Y [(H-1-ty)*cell, (H-ty)*cell].
        top = (map.Height - minGy) * cell;
        bottom = (map.Height - 1 - maxGy) * cell;
    }

    // ── Grid ↔ world mapping helpers ────────────────────────────────────
    // Renderer convention (EditorObject tile bake + collision preview boxes):
    //   grid row ty occupies world Y in [(mapH-1-ty)*cell, (mapH-ty)*cell],
    //   grid col tx occupies world X in [tx*cell, (tx+1)*cell].
    // World origin (0,0) is the BOTTOM-LEFT of the map's bottom row.

    /// <summary>World X → grid column under the renderer's [tx*cell, (tx+1)*cell) mapping.</summary>
    private static int WorldColFloor(Tilemap2D map, float worldX, float cell)
        => (int)MathF.Floor(worldX / cell);

    /// <summary>World Y → grid row: inverted so world +Y maps to DECREASING row index.</summary>
    private static int WorldRowFloor(Tilemap2D map, float worldY, float cell)
        => (int)MathF.Floor((map.Height * cell - worldY) / cell);

    /// <summary>World-space left edge of a tile column.</summary>
    private static float TileWorldMinX(int gx, float cell) => gx * cell;

    /// <summary>World-space right edge of a tile column.</summary>
    private static float TileWorldMaxX(int gx, float cell) => (gx + 1) * cell;

    /// <summary>World-space bottom edge of a tile row (row 0 = top of the map).</summary>
    private static float TileWorldMinY(Tilemap2D map, int gy, float cell)
        => (map.Height - 1 - gy) * cell;

    /// <summary>World-space top edge of a tile row (row 0 = top of the map).</summary>
    private static float TileWorldMaxY(Tilemap2D map, int gy, float cell)
        => (map.Height - gy) * cell;

    /// <summary>PER-TILE TOP-EDGE OVERRIDE (collision box kiri/kanan terpisah): the
    /// interpolated world Y of the tile's TOP at world X — the L/R top fractions form
    /// a straight ramp across the tile. Full-height tiles (no override) return the
    /// plain tile top. Used by the feet probes so the player WALKS a slope instead of
    /// being blocked by a full-height box (walking up a wedge = feet follow the top).</summary>
    private static float TileWorldTopAt(Tilemap2D map, TileLayer layer, int layerIdx, int gx, int gy, float cell, float worldX)
    {
        float baseTop = TileWorldMaxY(map, gy, cell);
        int tile = map.GetTile(layerIdx, gx, gy);
        if (tile < 0 || !layer.TileHasCollision(tile)) return baseTop;
        var (lf, rf) = layer.GetTileTopFracs(gx, gy);
        if (lf >= 0.999f && rf >= 0.999f) return baseTop; // full-height, no ramp
        float t = Math.Clamp((worldX - gx * cell) / cell, 0f, 1f);
        float frac = Math.Clamp(lf + (rf - lf) * t, 0f, 1f);
        // frac = height FROM the tile's TOP edge downward? NO — frac is the top edge
        // measured from the tile's BOTTOM: 1.0 = full cell top, 0.5 = half high.
        return baseTop - (1f - frac) * cell;
    }

    /// <summary>Highest interpolated top across ALL collision layers in ONE map
    /// (the surface the feet can land on at worldX). Returns −∞ when the column
    /// holds NO ground anywhere — the step-up probe must then do nothing (the old
    /// "fall back to feetY" invariant fed the fallback straight back into the
    /// `aheadTop <= yBot + stepH` test as an exact-equality match, so walking off
    /// a ledge over a pit "stepped up" 0.35 units EVERY frame and the player rose
    /// into the sky instead of falling).</summary>
    private static float MapTopAt(List<(Tilemap2D map, float cell, List<(TileLayer layer, int idx)> layers)> mapData,
        float worldX, float feetY)
    {
        // BUGFIX (was "stuck"): best started AT feetY, so a caller passing feetY+stepH
        // made MathF.Max pick the DEFAULT over the real (lower) ground top — the probe
        // then "found" a step that wasn't there and the walk stride desynced. Start at
        // -∞ and report −∞ (no ground) instead of inventing a surface.
        float best = float.NegativeInfinity;
        foreach (var (m, cell, ls) in mapData)
        {
            int gx = WorldColFloor(m, worldX, cell);
            if (gx < 0 || gx >= m.Width) continue;
            int gy0 = WorldRowFloor(m, feetY, cell);
            // PERF: the probe looks for ground BELOW the feet only — scan a bounded
            // window (max 3 rows down covers any step height well past the worst-
            // case StepHeight) instead of every row to the map bottom.
            int gyMax = Math.Min(m.Height - 1, gy0 + 3);
            TelRow(gyMax - Math.Max(0, gy0) + 1);   // step-up probe rows
            for (int gy = Math.Max(0, gy0); gy <= gyMax; gy++)
            {
                bool solid = false;
                (TileLayer layer, int idx) topLayer = default;
                foreach (var (layer, idx) in ls)
                {
                    if (IsSolid(m, layer, idx, gx, gy))
                    {
                        solid = true; topLayer = (layer, idx); break;
                    }
                }
                if (!solid) continue;
                best = MathF.Max(best, TileWorldTopAt(m, topLayer.layer, topLayer.idx, gx, gy, cell, worldX));
                break; // topmost solid row of THIS map
            }
        }
        return best; // −∞ = no ground in this column (the caller must not step)
    }

    /// <summary>Skin width kept between the capsule and a resolved surface so the
    /// AABB never exactly touches the tile edge (prevents re-collision jitter).</summary>
    private const float SkinWidth = 0.001f;

    private static bool IsSolid(Tilemap2D map, TileLayer layer, int layerIdx, int gx, int gy)
    {
        if (TelemetryEnabled) _teleTilesProbed++;
        if (gx < 0 || gy < 0 || gx >= map.Width || gy >= map.Height) return false;
        int tile = map.GetTile(layerIdx, gx, gy);
        bool solid = tile >= 0 && layer.TileHasCollision(tile);
        if (solid && TelemetryEnabled) _teleSolidsProbed++;
        return solid;
    }

    /// <summary>Add ONE map to the per-frame collision list if it carries ANY
    /// collision-carrying layer. PERF: scratch buffers reused — ZERO allocation
    /// per frame in steady state; each map gets its OWN pooled list slot (see the
    /// aliasing warning on _scratchLayerPool). Maps all bake at the world origin,
    /// so the shared world↔grid mapping holds.</summary>
    private static void TryAddCollisionMap(Tilemap2D m,
        List<(Tilemap2D map, float cell, List<(TileLayer layer, int idx)> layers)> mapData)
    {
        float cell = m.TileSize * Tilemap2D.WorldScale;
        if (cell <= 0f) return;
        // Collision layers — resolve against EVERY layer that carries collision
        // IDs, not just the palette-selected ActiveLayer (paint-only layer).
        // Pool round-robin: slot i reused for map i; a pool slot never exceeds the
        // map count of one frame, and each map entry keeps a DISTINCT list.
        if (_scratchLayerPoolIdx >= _scratchLayerPool.Count)
            _scratchLayerPool.Add(new List<(TileLayer, int)>());
        var collisionLayers = _scratchLayerPool[_scratchLayerPoolIdx];
        _scratchLayerPoolIdx++;
        collisionLayers.Clear();
        for (int i = 0; i < m.Layers.Count; i++)
        {
            var l = m.Layers[i];
            if (l != null && l.CollisionTileIds.Count > 0) collisionLayers.Add((l, i));
        }
        if (collisionLayers.Count > 0) mapData.Add((m, cell, collisionLayers));
    }

    /// <summary>X-sweep push for ONE map: the capsule's leading edge entered column
    /// gxEdge — if any collision tile in that column overlaps the capsule's Y span
    /// [yBot, yTop], push newX back to the column face (dir +1 → left face, −1 →
    /// right face). Y-aware so sweeping other maps with all their rows can't
    /// phantom-block from a solid tile far above/below the capsule.</summary>
    private static void TryResolveXPush(Tilemap2D map, float cell,
        List<(TileLayer layer, int idx)> layers, int gxEdge,
        float yBot, float yTop, float dir, float r, float capOffX, ref float newX)
    {
        // PERF: the wall column is tested only over the ROWS the capsule actually
        // spans — the old loop scanned ALL rows of the map for every push (a
        // 100-row map = 100 row-bounds checks + IsSolid calls × layers, every
        // frame, per map). Row bounds map inverts Y (row 0 = top): compute the
        // window from the capsule's Y span directly.
        int gy0 = Math.Max(0, (int)MathF.Floor((map.Height * cell - yTop) / cell));
        int gy1 = Math.Min(map.Height - 1, (int)MathF.Floor((map.Height * cell - yBot) / cell));
        TelRow(gy1 - gy0 + 1);   // X-push wall-column rows
        for (int gy = gy0; gy <= gy1; gy++)
        {
            foreach (var (collisionLayer, layerIdx) in layers)
            {
                if (!IsSolid(map, collisionLayer, layerIdx, gxEdge, gy)) continue;
                // RAMP-AWARE walls: the leading edge's SIDE of the tile — pushing
                // into a lowered top must only block if the feet are BELOW the tile's
                // top edge at this X (walking onto a lowered/wedge top = no wall hit;
                // the Y-step/landing logic handles the stride instead).
                float capXWall = newX + capOffX + dir * r;
                float topAtEdge = TileWorldTopAt(map, collisionLayer, layerIdx, gxEdge, gy, cell, capXWall);
                if (yBot >= topAtEdge - 0.01f)
                    continue; // feet at/above the (possibly lowered) top → walk onto it
                newX = dir > 0f
                    ? TileWorldMinX(gxEdge, cell) - r - SkinWidth - capOffX
                    : TileWorldMaxX(gxEdge, cell) + r + SkinWidth - capOffX;
                return;
            }
        }
    }

    /// <summary>Multi-map pit-respawn snap: scan EVERY collision map from the feet
    /// row DOWN and land on the HIGHEST collision tile top at/below the feet
    /// (feet inside a tile pop to ITS top, same as the single-map snap). Returns
    /// feetY unchanged when no map has ground under the column.</summary>
    private static float SnapFeetToGroundAny(
        List<(Tilemap2D map, float cell, List<(TileLayer layer, int idx)> layers)> mapData,
        float worldX, float feetY)
    {
        float best = feetY;
        foreach (var (m, cell, layers) in mapData)
        {
            int gx = (int)MathF.Floor(worldX / cell);
            if (gx < 0 || gx >= m.Width) continue;
            int gy0 = (int)MathF.Floor((m.Height * cell - feetY) / cell);
            for (int gy = Math.Max(0, gy0); gy < m.Height; gy++)
            {
                bool solid = false;
                (TileLayer layer, int idx) topLayer = default;
                foreach (var (collisionLayer, layerIdx) in layers)
                {
                    if (IsSolid(m, collisionLayer, layerIdx, gx, gy))
                    {
                        solid = true; topLayer = (collisionLayer, layerIdx); break;
                    }
                }
                if (!solid) continue;
                // RAMP-AWARE: the interpolated top at the spawn column (a spawn ON a
                // slope starts standing on the surface, not inside the wedge).
                best = MathF.Max(best, TileWorldTopAt(m, topLayer.layer, topLayer.idx, gx, gy, cell, worldX) + 0.001f);
                break; // topmost solid row of THIS map
            }
        }
        return best;
    }
}
