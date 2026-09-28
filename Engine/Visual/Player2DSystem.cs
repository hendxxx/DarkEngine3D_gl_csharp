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
    /// <summary>Advance animation clocks + run physics for all Player2D objects.
    /// Call once per frame from GameScene/SceneManager update when in-game or
    /// preview is active (not in pure edit mode).</summary>
    public static void Update(Objects.EditorObjectManager? manager, Tilemap2D? activeMap, float dt, IDEBridge? bridge)
    {
        if (manager == null) return;

        // Refresh the trigger runtime's manager reference (bubble actions anchor to
        // the player object found here). Also drives dialogue NPC interaction input.
        TriggerEventSystem.LastEditorObjectManager = manager;
        DialogueSystem.UpdateInteraction(manager, dt);

        // Start2D marker — used both for the deferred spawn below and for pit respawn.
        var start2d = manager.Objects.FirstOrDefault(o =>
            o is { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Start2D });

        // Deferred spawn: in-game entry sets Player2DSpawnPending because the .ing
        // reload re-creates objects AFTER the setter runs. On the first update frame
        // the re-created objects exist — teleport them to the Start2D marker now.
        if (Objects.EditorObject.Player2DSpawnPending)
        {
            Objects.EditorObject.Player2DSpawnPending = false;
            // New play session: the follow camera must re-snap to its start point.
            Objects.EditorObject.CameraFollowInitialized = false;
            start2d = manager.Objects.FirstOrDefault(o =>
                o is { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Start2D });
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
                    TriggerEventSystem.ResetRuntime(activeMap);
                    // Multi-map: reset the trigger runtime on EVERY scene map so
                    // portals/doors on other levels start the session clean too.
                    foreach (var o in manager.Objects)
                        if (o is { PrimitiveType: Objects.EditorPrimitiveType.Map2D, Map2dTilemap: { } om })
                            TriggerEventSystem.ResetRuntime(om);
                    // Fresh session: the runtime level restarts on the active map.
                    TriggerEventSystem.RuntimeMap = activeMap;
                }
            }
        }

        // ── Scene Box objects = solid AABB colliders (capsule vs box) ──
        // Axis-aligned from Position/Scale (rotation ignored — 2D sidescroller boxes
        // are unrotated). Feet-anchored capsule spans Z: Position.Z ± radius.
        var boxes = new List<(Vector3 min, Vector3 max)>();
        foreach (var b in manager.Objects)
        {
            if (b is not { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Box }) continue;
            var half = b.Scale * 0.5f;
            if (half.X <= 0f || half.Y <= 0f || half.Z <= 0f) continue;
            boxes.Add((b.Position - half, b.Position + half));
        }

        // ── Multi-map collision collection ──
        // Collision applies from EVERY visible Map2D tilemap in the scene (the
        // active map first), each against ALL of its collision-carrying layers.
        // Maps all bake at the world origin — one shared world↔grid mapping — so a
        // collision tile painted on ANY map level stops the capsule wherever it
        // stands (player on map 1, wall on map 3 → still solid).
        var mapData = new List<(Tilemap2D map, float cell, List<(TileLayer layer, int idx)> layers)>();
        foreach (var m in CollectCollisionMaps(manager, activeMap))
        {
            float cell = m.TileSize * Tilemap2D.WorldScale;
            if (cell <= 0f) continue;
            // Collision layers — resolve against EVERY layer that carries collision
            // IDs, not just the palette-selected ActiveLayer (paint-only layer).
            var collisionLayers = new List<(TileLayer layer, int idx)>();
            for (int i = 0; i < m.Layers.Count; i++)
            {
                var l = m.Layers[i];
                if (l != null && l.CollisionTileIds.Count > 0) collisionLayers.Add((l, i));
            }
            if (collisionLayers.Count > 0) mapData.Add((m, cell, collisionLayers));
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
            bool conversationActive = DialogueSystem.IsConversationActive;
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
            var jumpAction = player.Actions.FirstOrDefault(a => a.Name == "Jump");
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
                            act.ProjectileHitSheet, act.ProjectileHitClip, act.ProjectileHitFx,
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
            var fxCur = player.Actions.FirstOrDefault(a => a.Name == player.Player2DCurrentAction);
            if (fxCur != null && !string.IsNullOrWhiteSpace(fxCur.FxPreset) && fxCur.FxFollow)
            {
                var anchor = new Vector3(player.Position.X,
                    player.Position.Y + player.Player2DHeight * 0.5f, player.Position.Z + 0.05f);
                player.Effect2DRuntimeAnchor = anchor;
                var fxCfg = Effect2DSystem.PresetConfig(fxCur.FxPreset.Trim());
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
            if (velX > 0f)
            {
                foreach (var (cmap, ccell, clayers) in mapData)
                {
                    // Right wall: the capsule's right edge enters column gxEdge —
                    // push back to that column's LEFT face (newX stores pos.X, so the
                    // capsule center newX+capOffX lands skin-width from the face).
                    int gxEdge = (int)MathF.Floor((newX + capOffX + r) / ccell);
                    TryResolveXPush(cmap, ccell, clayers, gxEdge, yBot, yTop, +1f, r, capOffX, ref newX);
                }
            }
            else if (velX < 0f)
            {
                foreach (var (cmap, ccell, clayers) in mapData)
                {
                    // Left wall: the capsule's left edge enters column gxEdge —
                    // push back to that column's RIGHT face.
                    int gxEdge = (int)MathF.Floor((newX + capOffX - r) / ccell);
                    TryResolveXPush(cmap, ccell, clayers, gxEdge, yBot, yTop, -1f, r, capOffX, ref newX);
                }
            }

            // Box walls along X (same pass, least-penetration style push kept from
            // the original behavior but applied to the swept position).
            foreach (var (bmin, bmax) in boxes)
            {
                bool overlapZ = capZMax > bmin.Z && capZMin < bmax.Z;
                bool overlapY = yTop > bmin.Y + 0.001f && yBot < bmax.Y - 0.001f;
                if (!overlapZ || !overlapY) continue;

                if (newX + capOffX + r > bmin.X && newX + capOffX - r < bmax.X)
                {
                    float pushLeft = bmin.X - (newX + capOffX + r);   // negative: eject left
                    float pushRight = bmax.X - (newX + capOffX - r);  // positive: eject right
                    newX += MathF.Abs(pushLeft) < MathF.Abs(pushRight) ? pushLeft : pushRight;
                }
            }

            pos.X = newX;

            // ════════════════════════════════════════════════════════
            // STEP 2 — Gravity + move Y, resolve Y (ground/ceiling).
            // ════════════════════════════════════════════════════════
            player.Player2DVelocityY -= player.Player2DGravity * MathF.Max(0f, player.Player2DGravityScale) * dt;
            // Fall action while airborne and descending (auto-released on landing by action loop rules).
            if (player.Player2DVelocityY < -0.5f && !player.Player2DGrounded)
            {
                var fallAct = player.Actions.FirstOrDefault(a => a.Name == "Fall");
                if (fallAct != null && player.Player2DCurrentAction == "")
                    player.TryStartAction("Fall");
            }
            float newY = pos.Y + player.Player2DVelocityY * dt;

            bool grounded = false;

            if (player.Player2DVelocityY <= 0f)
            {
                // ── Falling: swept feet probe (per map) from old feet to new feet ──
                // Check every row the feet cross (topmost solid wins) plus the row
                // the new feet rest in, so tunneling can't slip through a tile.
                // Across maps the HIGHEST qualifying surface wins (a bridge tile on
                // map 3 lands the player even with map 1 open below).
                float? landTop = null;
                foreach (var (cmap, ccell, clayers) in mapData)
                {
                    int gx0 = WorldColFloor(cmap, capX - r + 0.001f, ccell);
                    int gx1 = WorldColFloor(cmap, capX + r - 0.001f, ccell);
                    int rowFeetNew = WorldRowFloor(cmap, newY + capOffY, ccell);
                    int rowFeetOld = WorldRowFloor(cmap, pos.Y + capOffY, ccell);
                    int rowTop = Math.Min(rowFeetOld, rowFeetNew); // smallest index = highest band
                    int rowBottom = Math.Max(rowFeetOld, rowFeetNew);
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

                        float tileTop = TileWorldMaxY(cmap, gy, ccell);
                        if (pos.Y + capOffY >= tileTop - 0.01f)
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
            TriggerEventSystem.PortalKeyProbe = keyName =>
                Enum.TryParse<ImGuiKey>(keyName, out var k) && k != ImGuiKey.None
                    ? ImGui.IsKeyPressed(k, false)
                    : false;
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

        // ── Particle effects: advance emitters + rain + integrate (render happens in
        // EditorObjectManager's 2D pass). Preview/in-game only, like physics above.
        Effect2DSystem.Tick(manager, activeMap, bridge?.Camera, dt);

        // ── Projectiles: integrate + collide + expire (render in the manager 2D pass).
        Projectile2DSystem.Tick(manager, TriggerEventSystem.EnumerateSceneMaps(manager, activeMap), dt);

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

    /// <summary>Skin width kept between the capsule and a resolved surface so the
    /// AABB never exactly touches the tile edge (prevents re-collision jitter).</summary>
    private const float SkinWidth = 0.001f;

    private static bool IsSolid(Tilemap2D map, TileLayer layer, int layerIdx, int gx, int gy)
    {
        if (gx < 0 || gy < 0 || gx >= map.Width || gy >= map.Height) return false;
        int tile = map.GetTile(layerIdx, gx, gy);
        return tile >= 0 && layer.TileHasCollision(tile);
    }

    /// <summary>Visible scene maps carrying collision: the ACTIVE map first, then
    /// every other visible Map2D tilemap (multi-map 2D levels). A map qualifies when
    /// ANY of its layers carries CollisionTileIds.</summary>
    private static List<Tilemap2D> CollectCollisionMaps(Objects.EditorObjectManager manager, Tilemap2D? activeMap)
    {
        var maps = new List<Tilemap2D>();
        foreach (var m in TriggerEventSystem.EnumerateSceneMaps(manager, activeMap))
        {
            if (m == null) continue;
            foreach (var l in m.Layers)
            {
                if (l != null && l.CollisionTileIds.Count > 0) { maps.Add(m); break; }
            }
        }
        return maps;
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
        for (int gy = 0; gy < map.Height; gy++)
        {
            float tMinY = (map.Height - 1 - gy) * cell;
            float tMaxY = tMinY + cell;
            if (yTop <= tMinY || yBot >= tMaxY) continue; // row outside the capsule span
            foreach (var (collisionLayer, layerIdx) in layers)
            {
                if (!IsSolid(map, collisionLayer, layerIdx, gxEdge, gy)) continue;
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
                foreach (var (collisionLayer, layerIdx) in layers)
                {
                    if (IsSolid(m, collisionLayer, layerIdx, gx, gy)) { solid = true; break; }
                }
                if (!solid) continue;
                best = MathF.Max(best, (m.Height - gy) * cell + 0.001f);
                break; // topmost solid row of THIS map
            }
        }
        return best;
    }
}
