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
/// feet-anchored capsule AABB against the active tilemap's collision tiles
/// (CollisionTileIds) using swept-axis resolution: move X, resolve X (walls),
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
    /// <summary>Advance animation clocks + run physics for all Player2D objects.
    /// Call once per frame from GameScene/SceneManager update when in-game or
    /// preview is active (not in pure edit mode).</summary>
    public static void Update(Objects.EditorObjectManager? manager, Tilemap2D? map, float dt, IDEBridge? bridge)
    {
        if (manager == null || map == null) return;

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
                    // Fresh run: clear any stale walk/facing state carried over from
                    // a previous in-game session.
                    p.Player2DMoving = false;
                    p.Player2DFacingRight = true;
                    Console.WriteLine($"[Player2D] Spawned at Start ({p.Position.X:F1}, {p.Position.Y:F1}) (capsule offset {p.Player2DCapsuleOffsetX:F2},{p.Player2DCapsuleOffsetY:F2})");
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

        float cell = map.TileSize * Tilemap2D.WorldScale;
        if (cell <= 0f) return;

        // Collision layers — resolve against EVERY layer that carries collision IDs,
        // not just the palette-selected ActiveLayer (which may be a paint-only layer).
        var collisionLayers = new List<(TileLayer layer, int idx)>();
        for (int i = 0; i < map.Layers.Count; i++)
        {
            var l = map.Layers[i];
            if (l != null && l.CollisionTileIds.Count > 0) collisionLayers.Add((l, i));
        }

        foreach (var player in manager.Objects)
        {
            if (player is not { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Player2D })
                continue;

            // NOTE: the animation clock advances inside DrawPlayer2D (single source of
            // truth) — updating it here too would double the playback speed.

            // ── Input-driven movement + jump (preview/in-game only) ──
            // A/D or Left/Right move horizontally with acceleration/deceleration;
            // LeftShift runs (RunSpeed); Space/Up jumps when grounded; W/S fly-style
            // vertical movement retained as a debug convenience.
            float walkSpeed = MathF.Max(0.1f, player.Player2DMoveSpeed);
            float runSpeed = MathF.Max(walkSpeed, player.Player2DRunSpeed);
            bool left = ImGui.IsKeyDown(ImGuiKey.A) || ImGui.IsKeyDown(ImGuiKey.LeftArrow);
            bool right = ImGui.IsKeyDown(ImGuiKey.D) || ImGui.IsKeyDown(ImGuiKey.RightArrow);
            bool upHeld = ImGui.IsKeyDown(ImGuiKey.W);
            bool downHeld = ImGui.IsKeyDown(ImGuiKey.S);
            bool runHeld = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
            bool jump = ImGui.IsKeyPressed(ImGuiKey.Space) || ImGui.IsKeyPressed(ImGuiKey.UpArrow);
            float targetVx = 0f;
            if (left && !right) targetVx = -(runHeld ? runSpeed : walkSpeed);
            else if (right && !left) targetVx = runHeld ? runSpeed : walkSpeed;

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
            if (jump && player.Player2DGrounded)
            {
                player.Player2DVelocityY = MathF.Max(1f, player.Player2DJumpForce);
                player.Player2DGrounded = false;
                // Jump action fires automatically (priority-gated).
                player.TryStartAction("Jump");
            }

            // ── User-bound action keys (Inspector: Attack = J, Block = K, ...) ──
            // Track whether the bound key for the currently active action is still held —
            // used by ResolveLocomotionAction to release the action when the key goes up.
            bool currentActionKeyHeld = false;
            foreach (var act in player.Actions)
            {
                if (string.IsNullOrWhiteSpace(act.KeyBinding) || act.KeyBinding == "None") continue;
                if (Enum.TryParse<ImGuiKey>(act.KeyBinding, out var k) && k != ImGuiKey.None)
                {
                    if (ImGui.IsKeyPressed(k))
                        player.TryStartAction(act.Name);
                    if (act.Name == player.Player2DCurrentAction)
                        currentActionKeyHeld = ImGui.IsKeyDown(k);
                }
            }

            // Auto-resolve locomotion action (idle/walk/run) to match current state.
            // Idle when grounded+still, Walk/Run by velocity. Pass whether the currently
            // active key-bound action's key is still held — if released, locomotion takes
            // over (e.g. Run bound to J: hold = Run, release = back to Idle/Walk).
            player.ResolveLocomotionAction(currentActionKeyHeld);
            // W/S override gravity while held (fly-style vertical movement).
            if (upHeld && !downHeld) player.Player2DVelocityY = walkSpeed;
            else if (downHeld && !upHeld) player.Player2DVelocityY = -walkSpeed;

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
            // World +Y maps to DECREASING grid row, so the feet are the LARGER row
            // index and the head the SMALLER — iterate yHeadRow..yFeetRow.
            float yBot = pos.Y + capOffY;
            float yTop = pos.Y + capOffY + height;
            int yFeetRow = WorldRowFloor(map, yBot + 0.001f, cell);
            int yHeadRow = WorldRowFloor(map, yTop - 0.001f, cell);

            foreach (var (collisionLayer, layerIdx) in collisionLayers)
            {
                if (velX > 0f)
                {
                    // Right wall: the capsule's right edge enters column gxEdge —
                    // push back to that column's LEFT face (newX stores pos.X, so the
                    // capsule center newX+capOffX lands skin-width from the face).
                    int gxEdge = WorldColFloor(map, newX + capOffX + r, cell);
                    for (int gy = yHeadRow; gy <= yFeetRow; gy++)
                    {
                        if (IsSolid(map, collisionLayer, layerIdx, gxEdge, gy))
                        {
                            newX = TileWorldMinX(gxEdge, cell) - r - SkinWidth - capOffX;
                            break;
                        }
                    }
                }
                else if (velX < 0f)
                {
                    // Left wall: the capsule's left edge enters column gxEdge —
                    // push back to that column's RIGHT face.
                    int gxEdge = WorldColFloor(map, newX + capOffX - r, cell);
                    for (int gy = yHeadRow; gy <= yFeetRow; gy++)
                    {
                        if (IsSolid(map, collisionLayer, layerIdx, gxEdge, gy))
                        {
                            newX = TileWorldMaxX(gxEdge, cell) + r + SkinWidth - capOffX;
                            break;
                        }
                    }
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

            // Horizontal span of the capsule after the X resolve — used for the
            // tile column range of every vertical probe.
            int gx0 = WorldColFloor(map, capX - r + 0.001f, cell);
            int gx1 = WorldColFloor(map, capX + r - 0.001f, cell);

            bool grounded = false;

            if (player.Player2DVelocityY <= 0f)
            {
                // ── Falling: swept feet probe from old feet to new feet ──
                // Check every row the feet cross (topmost solid wins) plus the row
                // the new feet rest in, so tunneling can't slip through a tile.
                int rowFeetNew = WorldRowFloor(map, newY + capOffY, cell);
                int rowFeetOld = WorldRowFloor(map, pos.Y + capOffY, cell);
                int rowTop = Math.Min(rowFeetOld, rowFeetNew); // smallest index = highest band
                int rowBottom = Math.Max(rowFeetOld, rowFeetNew);
                // Cross rows in the order the feet pass them: highest band first.
                for (int gy = rowTop; gy <= rowBottom; gy++)
                {
                    bool solid = false;
                    foreach (var (collisionLayer, layerIdx) in collisionLayers)
                    {
                        for (int gx = gx0; gx <= gx1; gx++)
                        {
                            if (IsSolid(map, collisionLayer, layerIdx, gx, gy))
                            {
                                solid = true;
                                break;
                            }
                        }
                        if (solid) break;
                    }
                    if (!solid) continue;

                    float tileTop = TileWorldMaxY(map, gy, cell);
                    if (pos.Y + capOffY >= tileTop - 0.01f)
                    {
                        // Came from above → land on top of this tile (feet = pos.Y + capOffY).
                        newY = tileTop - capOffY;
                        player.Player2DVelocityY = 0f;
                        grounded = true;
                        break;
                    }
                    // Feet started inside/below this band's top edge → not a landing
                    // surface; keep checking lower rows.
                }
            }
            else
            {
                // ── Rising: swept head probe from old head to new head ──
                int rowHeadNew = WorldRowFloor(map, newY + capOffY + height, cell);
                int rowHeadOld = WorldRowFloor(map, pos.Y + capOffY + height, cell);
                int rowTop = Math.Min(rowHeadOld, rowHeadNew);
                int rowBottom = Math.Max(rowHeadOld, rowHeadNew);
                // Cross rows in the order the head passes them: lowest band first.
                for (int gy = rowBottom; gy >= rowTop; gy--)
                {
                    bool solid = false;
                    foreach (var (collisionLayer, layerIdx) in collisionLayers)
                    {
                        for (int gx = gx0; gx <= gx1; gx++)
                        {
                            if (IsSolid(map, collisionLayer, layerIdx, gx, gy))
                            {
                                solid = true;
                                break;
                            }
                        }
                        if (solid) break;
                    }
                    if (!solid) continue;

                    float tileBottom = TileWorldMinY(map, gy, cell);
                    if (pos.Y + capOffY + height <= tileBottom + 0.01f)
                    {
                        // Came from below → bump the ceiling.
                        newY = tileBottom - height - capOffY;
                        player.Player2DVelocityY = 0f;
                        break;
                    }
                    // Head started above this band's bottom edge → not a ceiling here;
                    // keep checking higher rows.
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

            // ── Pit death: the world origin (0,0) is the map's bottom-left, so any
            // Y well below zero means the player fell through a hole. Respawn at the
            // Start2D marker instead of falling forever (capsule-aware, same as spawn). ──
            if (pos.Y < -cell * 2f && start2d != null)
            {
                player.Position = new System.Numerics.Vector3(
                    start2d.Position.X - player.Player2DCapsuleOffsetX,
                    start2d.Position.Y - player.Player2DCapsuleOffsetY,
                    start2d.Position.Z);
                player.Player2DVelocityY = 0f;
                player.Player2DGrounded = false;
                // Respawn faces right in the idle state (standard sidescroller reset).
                player.Player2DMoving = false;
                player.Player2DFacingRight = true;
                Console.WriteLine($"[Player2D] Fell below the map — respawned at Start ({start2d.Position.X:F1}, {start2d.Position.Y:F1})");
            }
        }
        // ── Platformer camera follow ──
        // Smooth follow (FollowSpeed), dead zone (DeadZoneWidth/Height), vertical
        // threshold (camera rises only above VerticalThreshold px, returns with
        // ReturnSpeed), look-ahead (LookAhead px toward the facing), world boundary
        // (clamped to the map's painted-tile extent), Camera Start Point (position +
        // zoom) honored on entry. Runs ONLY in preview/in-game.
        if (bridge?.Camera != null)
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
                float mapHWorld = map.Height * PxToWorld(map);
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
                float deadW = MathF.Max(0f, first.CameraDeadZoneWidth) * PxToWorld(map);
                float deadH = MathF.Max(0f, first.CameraDeadZoneHeight) * PxToWorld(map);
                float vThreshold = MathF.Max(0f, first.CameraVerticalThreshold) * PxToWorld(map);
                float returnSpeed = MathF.Max(0.1f, first.CameraReturnSpeed);
                float lookAhead = first.CameraLookAhead * PxToWorld(map);

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
                ComputeWorldBounds(map, out float worldL, out float worldR, out float worldB, out float worldT);
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
}
