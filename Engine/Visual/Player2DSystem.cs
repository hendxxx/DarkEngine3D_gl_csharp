using System.Numerics;
using System.Linq;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Minimal capsule-vs-tile physics for Player2D objects in preview/in-game.
/// Applies gravity to every Player2D EditorObject, then resolves the player's
/// feet-anchored capsule AABB against the active tilemap's collision tiles
/// (CollisionTileIds) using swept-axis resolution: move Y, resolve Y (ground/
/// ceiling), then move X, resolve X (walls). Grounded players can be moved
/// horizontally with the editor fly keys (a full input controller comes later).
/// </summary>
public static class Player2DSystem
{
    /// <summary>Advance animation clocks + run physics for all Player2D objects.
    /// Call once per frame from GameScene/SceneManager update when in-game or
    /// preview is active (not in pure edit mode).</summary>
    public static void Update(Objects.EditorObjectManager? manager, Tilemap2D? map, float dt)
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

        foreach (var player in manager.Objects)
        {
            if (player is not { IsVisible: true, PrimitiveType: Objects.EditorPrimitiveType.Player2D })
                continue;

            // NOTE: the animation clock advances inside DrawPlayer2D (single source of
            // truth) — updating it here too would double the playback speed.

            // ── Physics: capsule AABB (feet-anchored) vs collision tiles ──
            float r = player.Player2DCapsuleRadius;
            float height = player.Player2DCapsuleHeight;
            var pos = player.Position;

            // Velocity integration (Y only — sidescroller).
            player.Player2DVelocityY -= player.Player2DGravity * dt;
            float newY = pos.Y + player.Player2DVelocityY * dt;

            var aabb = new Vector4(pos.X - r, newY, pos.X + r, newY + height); // minX, minY, maxX, maxY

            bool grounded = false;
            var collisionLayer = map.ActiveLayer;
            if (collisionLayer != null && collisionLayer.CollisionTileIds.Count > 0)
            {
                float cell = map.TileSize * Tilemap2D.WorldScale;

                // ── Vertical resolve ──
                int gy0 = (int)MathF.Floor(aabb.Y / cell);
                int gy1 = (int)MathF.Floor((aabb.W - 0.001f) / cell);
                int gx0 = (int)MathF.Floor(aabb.X / cell);
                int gx1 = (int)MathF.Floor((aabb.Z - 0.001f) / cell);

                if (player.Player2DVelocityY <= 0f)
                {
                    // Falling: find the highest solid tile top under the capsule feet.
                    int gyFeet = gy0;
                    for (int gx = gx0; gx <= gx1; gx++)
                    {
                        if (IsSolid(map, collisionLayer, gx, gyFeet))
                        {
                            newY = (gyFeet + 1) * cell;
                            player.Player2DVelocityY = 0f;
                            grounded = true;
                            break;
                        }
                    }
                }
                else
                {
                    // Rising: ceiling check at the capsule head.
                    int gyHead = gy1;
                    for (int gx = gx0; gx <= gx1; gx++)
                    {
                        if (IsSolid(map, collisionLayer, gx, gyHead))
                        {
                            newY = gyHead * cell - height;
                            player.Player2DVelocityY = 0f;
                            break;
                        }
                    }
                }
            }

            pos.Y = newY;
            player.Player2DGrounded = grounded;
            player.Position = pos;
        }
    }

    private static bool IsSolid(Tilemap2D map, TileLayer layer, int gx, int gy)
    {
        if (gx < 0 || gy < 0 || gx >= map.Width || gy >= map.Height) return false;
        int tile = map.GetTile(map.Layers.IndexOf(layer), gx, gy);
        return tile >= 0 && layer.TileHasCollision(tile);
    }
}
