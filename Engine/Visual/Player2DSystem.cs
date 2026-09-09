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
/// (CollisionTileIds) using swept-axis resolution: move Y, resolve Y (ground/
/// ceiling), then move X, resolve X (walls). Visible Box objects in the scene
/// also act as solid AABB colliders (capsule vs box): land on top, bump the
/// ceiling, get pushed out sideways. Runs ONLY in preview/in-game — Update is
/// called exclusively from those modes. Grounded players can be moved
/// horizontally with the editor fly keys (a full input controller comes later).
/// </summary>
public static class Player2DSystem
{
    /// <summary>Advance animation clocks + run physics for all Player2D objects.
    /// Call once per frame from GameScene/SceneManager update when in-game or
    /// preview is active (not in pure edit mode).</summary>
    public static void Update(Objects.EditorObjectManager? manager, Tilemap2D? map, float dt, IDEBridge? bridge)
    {
        if (manager == null || map == null) return;

        // Deferred spawn: in-game entry sets Player2DSpawnPending because the .ing
        // reload re-creates objects AFTER the setter runs. On the first update frame
        // the re-created objects exist — teleport them to the Start2D marker now.
        if (Objects.EditorObject.Player2DSpawnPending)
        {
            Objects.EditorObject.Player2DSpawnPending = false;
            var start2d = manager.Objects.FirstOrDefault(o =>
                o is { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Start2D });
            if (start2d != null)
            {
                foreach (var p in manager.Objects)
                {
                    if (p is not { PrimitiveType: Objects.EditorPrimitiveType.Player2D }) continue;
                    p.Position = start2d.Position;
                    p.Player2DVelocityY = 0f;
                    p.Player2DAnimTime = 0f;
                    Console.WriteLine($"[Player2D] Spawned at Start ({p.Position.X:F1}, {p.Position.Y:F1})");
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
        // Debug: log every active box collider whenever Player2DSystem runs (edit,
        // preview, or in-game), so you can verify the collider list and positions.
        if (bridge != null)
        {
            foreach (var box in boxes)
            {
                var bmin = box.min;
                var bmax = box.max;
                Console.WriteLine($"[BoxDebug] box min({bmin.X:F2},{bmin.Y:F2},{bmin.Z:F2}) max({bmax.X:F2},{bmax.Y:F2},{bmax.Z:F2}) (preview={bridge.IsPreviewMode}, ingame={bridge.InGameActive})");
            }
        }

        foreach (var player in manager.Objects)
        {
            if (player is not { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Player2D })
                continue;

            // NOTE: the animation clock advances inside DrawPlayer2D (single source of
            // truth) — updating it here too would double the playback speed.

            // ── Input-driven horizontal movement + jump (preview/in-game only) ──
            // Arrow keys or A/D move horizontally; Space/Up/LeftShift jump when grounded.
            float playerSpeed = 4f;
            bool left = ImGui.IsKeyDown(ImGuiKey.A) || ImGui.IsKeyDown(ImGuiKey.LeftArrow);
            bool right = ImGui.IsKeyDown(ImGuiKey.D) || ImGui.IsKeyDown(ImGuiKey.RightArrow);
            bool jump = ImGui.IsKeyPressed(ImGuiKey.Space) || ImGui.IsKeyPressed(ImGuiKey.UpArrow)
                || ImGui.IsKeyPressed(ImGuiKey.LeftShift);
            float velX = 0f;
            if (left && !right) velX = -playerSpeed;
            else if (right && !left) velX = playerSpeed;
            else if ((left || right) && (player.Player2DGrounded || player.Player2DVelocityY > -2f))
            {
                // slight lateral nudge when on ground to keep player responsive.
                velX = left ? -playerSpeed * 0.6f : playerSpeed * 0.6f;
            }
            if (jump && player.Player2DGrounded)
            {
                player.Player2DVelocityY = 8f;
                player.Player2DGrounded = false;
            }
            var pos = player.Position;
            pos.X += velX * dt;

            // ── Physics: capsule AABB (feet-anchored) vs collision tiles ──
            float r = player.Player2DCapsuleRadius;
            float height = player.Player2DCapsuleHeight;

            // Velocity integration (Y only — sidescroller).
            player.Player2DVelocityY -= player.Player2DGravity * dt;
            float newY = pos.Y + player.Player2DVelocityY * dt;

            var aabb = new Vector4(pos.X - r, newY, pos.X + r, newY + height); // minX, minY, maxX, maxY

            bool grounded = false;
            float capZMin = pos.Z - r, capZMax = pos.Z + r;
            // Resolve against EVERY collision layer of the tilemap, not just the palette-
            // selected ActiveLayer (Map Editor palette selection has no collision tiles).
            var collisionLayers = new List<TileLayer>();
            foreach (var layer in map.Layers)
                if (layer != null && layer.CollisionTileIds.Count > 0)
                    collisionLayers.Add(layer);
            Console.WriteLine($"[Player2D] player {player.Name}: pos=({pos.X:F2},{pos.Y:F2},{pos.Z:F2}) velY={player.Player2DVelocityY:F3} height={height:F2} r={r:F2}");
            Console.WriteLine($"[Player2D] capsule aabb(Y range)=[{aabb.Y:F2}, {aabb.W:F2}] cell={map.TileSize * Tilemap2D.WorldScale:F3} gy0={MathF.Floor(aabb.Y/(map.TileSize*Tilemap2D.WorldScale)):F0} gy1={MathF.Floor((aabb.W-0.001f)/(map.TileSize*Tilemap2D.WorldScale)):F0}");
            Console.WriteLine($"[Player2D] collisionLayers.Count={collisionLayers.Count}");
            if (collisionLayers.Count > 0)
            {
                float cell = map.TileSize * Tilemap2D.WorldScale;

                // ── Vertical resolve ──
                // Swept AABB from previous feet (pos.Y) to new feet (newY).
                // The capsule covers Y in [min(feetOld,feetNew), max(headOld,headNew)].
                float feetOld = pos.Y, feetNew = newY;
                float headOld = pos.Y + height, headNew = newY + height;
                float yMin = MathF.Min(feetOld, feetNew);
                float yMax = MathF.Max(headOld, headNew);
                int gy0 = (int)MathF.Floor(yMin / cell);
                int gy1 = (int)MathF.Floor((yMax - 0.001f) / cell);
                int gx0 = (int)MathF.Floor(aabb.X / cell);
                int gx1 = (int)MathF.Floor((aabb.Z - 0.001f) / cell);

                foreach (var collisionLayer in collisionLayers)
                {
                    var layerIdx = map.Layers.IndexOf(collisionLayer);
                    Console.WriteLine($"[Player2D]  layer={collisionLayer.Name}(idx={layerIdx}) velY<=0? {player.Player2DVelocityY<=0} sweepY=[{gy0},{gy1}] checkX=[{gx0},{gx1}] prevY={pos.Y:F2} newY={newY:F2}");
                    if (player.Player2DVelocityY <= 0f)
                    {
                        // Falling: find the highest solid tile top under the capsule feet.
                        int gyFeet = gy0;
                        for (int gx = gx0; gx <= gx1; gx++)
                        {
                            bool solid = IsSolid(map, collisionLayer, gx, gyFeet);
                            Console.WriteLine($"[Player2D]    fall gx={gx} gyFeet={gyFeet} tile={map.GetTile(layerIdx, gx, gyFeet)} solid={solid}");
                            if (solid)
                            {
                                newY = (gyFeet + 1) * cell;
                                player.Player2DVelocityY = 0f;
                                grounded = true;
                                Console.WriteLine($"[Player2D]    >>> landed on tile at Y={newY:F2} (grounded)");
                                goto tileVertResolved;
                            }
                        }
                    }
                    else
                    {
                        // Rising: ceiling check at the capsule head.
                        int gyHead = gy1;
                        for (int gx = gx0; gx <= gx1; gx++)
                        {
                            bool solid = IsSolid(map, collisionLayer, gx, gyHead);
                            Console.WriteLine($"[Player2D]    rise gx={gx} gyHead={gyHead} tile={map.GetTile(layerIdx, gx, gyHead)} solid={solid}");
                            if (solid)
                            {
                                newY = gyHead * cell - height;
                                player.Player2DVelocityY = 0f;
                                Console.WriteLine($"[Player2D]    >>> ceiling bump at Y={newY:F2}");
                                goto tileVertResolved;
                            }
                        }
                    }
                }
                tileVertResolved: ;
            }
            else
            {
                Console.WriteLine($"[Player2D]  NO COLLISION LAYERS on this tilemap!");
            }

            // ── Box vertical resolve: land on top / bump ceiling ──
            // Same swept logic as tiles but against scene Box AABBs. Requires the
            // capsule to overlap the box in X/Z, and the previous position to have been
            // OUTSIDE the box along the movement axis (so walking into a side doesn't
            // teleport the player onto the top — the horizontal pass handles that).
            foreach (var (bmin, bmax) in boxes)
            {
                bool overlapXZ = pos.X + r > bmin.X && pos.X - r < bmax.X
                    && capZMax > bmin.Z && capZMin < bmax.Z;
                if (!overlapXZ)
                {
                    Console.WriteLine($"[Player2D] box bmin=({bmin.X:F2},{bmin.Y:F2},{bmin.Z:F2}) bmax=({bmax.X:F2},{bmax.Y:F2},{bmax.Z:F2}) dxOverlap? {pos.X+r>bmin.X && pos.X-r<bmax.X} dzOverlap? {capZMax>bmin.Z && capZMin<bmax.Z}  -> skip (no X/Z overlap)");
                    continue;
                }
                Console.WriteLine($"[Player2D] box bmin=({bmin.X:F2},{bmin.Y:F2},{bmin.Z:F2}) bmax=({bmax.X:F2},{bmax.Y:F2},{bmax.Z:F2}) OVERLAP_XZ");
                if (player.Player2DVelocityY <= 0f)
                {
                    // Falling: feet crossed the box top surface this frame.
                    if (newY < bmax.Y && newY > bmin.Y && pos.Y >= bmax.Y - 0.01f)
                    {
                        newY = bmax.Y;
                        player.Player2DVelocityY = 0f;
                        grounded = true;
                        Console.WriteLine($"[Player2D] >>> landed on box top at Y={newY:F2} (grounded)");
                    }
                    else
                    {
                        Console.WriteLine($"[Player2D]  box-top not met: newY={newY:F2} in?({newY < bmax.Y && newY > bmin.Y}) posY>=bmaxY? {pos.Y >= bmax.Y - 0.01f}");
                    }
                }
                else
                {
                    // Rising: head crossed the box bottom surface this frame.
                    if (newY + height > bmin.Y && newY + height < bmax.Y && pos.Y + height <= bmin.Y + 0.01f)
                    {
                        newY = bmin.Y - height;
                        player.Player2DVelocityY = 0f;
                        Console.WriteLine($"[Player2D] >>> hit box ceiling at Y={newY:F2}");
                    }
                }
            }

            pos.Y = newY;

            // ── Box horizontal resolve: push out of side walls (least penetration) ──
            // Strict Y margins so standing exactly on a box top / under a ceiling does
            // not count as a side hit.
            foreach (var (bmin, bmax) in boxes)
            {
                bool overlapZ = capZMax > bmin.Z && capZMin < bmax.Z;
                bool overlapY = pos.Y + height > bmin.Y + 0.001f && pos.Y < bmax.Y - 0.001f;
                if (!overlapZ || !overlapY) continue;

                if (pos.X + r > bmin.X && pos.X - r < bmax.X)
                {
                    float pushLeft = bmin.X - (pos.X + r);   // negative: eject to the left
                    float pushRight = bmax.X - (pos.X - r);  // positive: eject to the right
                    pos.X += MathF.Abs(pushLeft) < MathF.Abs(pushRight) ? pushLeft : pushRight;
                    Console.WriteLine($"[Player2D] horizontal push box=({bmin.X:F2},{bmin.Y:F2},{bmin.Z:F2})-({bmax.X:F2},{bmax.Y:F2},{bmax.Z:F2}) push={(MathF.Abs(pushLeft) < MathF.Abs(pushRight)?pushLeft:pushRight):F3}");
                }
            }

            player.Player2DGrounded = grounded;
            player.Position = pos;
            Console.WriteLine($"[Player2D] END  newPos=({pos.X:F2},{pos.Y:F2},{pos.Z:F2}) grounded={grounded} velY={player.Player2DVelocityY:F3}");
        }
    }

    private static bool IsSolid(Tilemap2D map, TileLayer layer, int gx, int gy)
    {
        if (gx < 0 || gy < 0 || gx >= map.Width || gy >= map.Height) return false;
        int tile = map.GetTile(map.Layers.IndexOf(layer), gx, gy);
        return tile >= 0 && layer.TileHasCollision(tile);
    }
}
