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
            if (jump && player.Player2DGrounded)
            {
                player.Player2DVelocityY = 8f;
                player.Player2DGrounded = false;
            }

            var pos = player.Position;
            float r = player.Player2DCapsuleRadius;
            float height = player.Player2DCapsuleHeight;
            float capZMin = pos.Z - r, capZMax = pos.Z + r;

            // ════════════════════════════════════════════════════════════
            // STEP 1 — Move X, resolve X (walls) via swept AABB.
            // ════════════════════════════════════════════════════════════
            float newX = pos.X + velX * dt;

            // Vertical span covered during the X sweep (Y is unchanged here).
            // World +Y maps to DECREASING grid row, so the feet are the LARGER row
            // index and the head the SMALLER — iterate yHeadRow..yFeetRow.
            float yBot = pos.Y;
            float yTop = pos.Y + height;
            int yFeetRow = WorldRowFloor(map, yBot + 0.001f, cell);
            int yHeadRow = WorldRowFloor(map, yTop - 0.001f, cell);

            foreach (var (collisionLayer, layerIdx) in collisionLayers)
            {
                if (velX > 0f)
                {
                    // Right wall: the capsule's right edge enters column gxEdge —
                    // push back to that column's LEFT face.
                    int gxEdge = WorldColFloor(map, newX + r, cell);
                    for (int gy = yHeadRow; gy <= yFeetRow; gy++)
                    {
                        if (IsSolid(map, collisionLayer, layerIdx, gxEdge, gy))
                        {
                            newX = TileWorldMinX(gxEdge, cell) - r - SkinWidth;
                            break;
                        }
                    }
                }
                else if (velX < 0f)
                {
                    // Left wall: the capsule's left edge enters column gxEdge —
                    // push back to that column's RIGHT face.
                    int gxEdge = WorldColFloor(map, newX - r, cell);
                    for (int gy = yHeadRow; gy <= yFeetRow; gy++)
                    {
                        if (IsSolid(map, collisionLayer, layerIdx, gxEdge, gy))
                        {
                            newX = TileWorldMaxX(gxEdge, cell) + r + SkinWidth;
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

                if (newX + r > bmin.X && newX - r < bmax.X)
                {
                    float pushLeft = bmin.X - (newX + r);   // negative: eject to the left
                    float pushRight = bmax.X - (newX - r);  // positive: eject to the right
                    newX += MathF.Abs(pushLeft) < MathF.Abs(pushRight) ? pushLeft : pushRight;
                }
            }

            pos.X = newX;

            // ════════════════════════════════════════════════════════
            // STEP 2 — Gravity + move Y, resolve Y (ground/ceiling).
            // ════════════════════════════════════════════════════════
            player.Player2DVelocityY -= player.Player2DGravity * dt;
            float newY = pos.Y + player.Player2DVelocityY * dt;

            // Horizontal span of the capsule after the X resolve — used for the
            // tile column range of every vertical probe.
            int gx0 = WorldColFloor(map, pos.X - r + 0.001f, cell);
            int gx1 = WorldColFloor(map, pos.X + r - 0.001f, cell);

            bool grounded = false;

            if (player.Player2DVelocityY <= 0f)
            {
                // ── Falling: swept feet probe from old feet to new feet ──
                // Check every row the feet cross (topmost solid wins) plus the row
                // the new feet rest in, so tunneling can't slip through a tile.
                int rowFeetNew = WorldRowFloor(map, newY, cell);
                int rowFeetOld = WorldRowFloor(map, pos.Y, cell);
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
                    if (pos.Y >= tileTop - 0.01f)
                    {
                        // Came from above → land on top of this tile.
                        newY = tileTop;
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
                int rowHeadNew = WorldRowFloor(map, newY + height, cell);
                int rowHeadOld = WorldRowFloor(map, pos.Y + height, cell);
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
                    if (pos.Y + height <= tileBottom + 0.01f)
                    {
                        // Came from below → bump the ceiling.
                        newY = tileBottom - height;
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
                bool overlapXZ = pos.X + r > bmin.X && pos.X - r < bmax.X
                    && capZMax > bmin.Z && capZMin < bmax.Z;
                if (!overlapXZ) continue;

                if (player.Player2DVelocityY <= 0f)
                {
                    // Falling: feet crossed the box top surface this frame.
                    if (newY < bmax.Y && newY > bmin.Y && pos.Y >= bmax.Y - 0.01f)
                    {
                        newY = bmax.Y;
                        player.Player2DVelocityY = 0f;
                        grounded = true;
                    }
                }
                else
                {
                    // Rising: head crossed the box bottom surface this frame.
                    if (newY + height > bmin.Y && newY + height < bmax.Y && pos.Y + height <= bmin.Y + 0.01f)
                    {
                        newY = bmin.Y - height;
                        player.Player2DVelocityY = 0f;
                    }
                }
            }

            pos.Y = newY;
            player.Player2DGrounded = grounded;
            player.Position = pos;
        }
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
