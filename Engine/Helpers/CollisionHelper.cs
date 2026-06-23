using DarkEngine3D_gl_csharp.Engine.Objects;
using System.Numerics;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Helpers
{
    /// <summary>
    /// Collision detection: sphere vs AABB push-out.
    /// Digunakan untuk player, NPC, dan camera supaya tidak tembus object statis.
    /// </summary>
    public static class CollisionHelper
    {
        private const float CharacterRadius = 0.45f;  // sama dengan CharacterAgent.Radius
        private const float CameraRadius = 0.3f;

        /// <summary>
        /// Push position keluar dari semua static object yang IsCollidable=true.
        /// Cek sphere (center + radius) terhadap AABB setiap object collidable.
        /// Jika overlap, posisi di-push keluar sepanjang sumbu terdekat.
        /// </summary>
        public static Vector3 PushOutOfStaticObjects(
            Vector3 position,
            float radius,
            StaticObjectManager[]? managers)
        {
            if (managers == null) return position;

            Vector3 result = position;
            bool pushed = false;

            foreach (var mgr in managers)
            {
                if (mgr == null) continue;

                foreach (var obj in mgr.GetObjects())
                {
                    if (!obj.IsCollidable) continue;

                    var aabb = obj.CachedCollisionAABB ?? obj.CachedWorldAABB;

                    // Closest point on AABB to sphere center
                    float closestX = Math.Clamp(result.X, aabb.Min.X, aabb.Max.X);
                    float closestY = Math.Clamp(result.Y, aabb.Min.Y, aabb.Max.Y);
                    float closestZ = Math.Clamp(result.Z, aabb.Min.Z, aabb.Max.Z);

                    float dx = result.X - closestX;
                    float dy = result.Y - closestY;
                    float dz = result.Z - closestZ;
                    float distSq = dx * dx + dy * dy + dz * dz;

                    if (distSq < radius * radius)
                    {
                        float dist = MathF.Sqrt(distSq);
                        if (dist < 0.001f)
                        {
                            // Center inside AABB — push along shortest axis
                            float ex = MathF.Min(result.X - aabb.Min.X, aabb.Max.X - result.X);
                            float ey = MathF.Min(result.Y - aabb.Min.Y, aabb.Max.Y - result.Y);
                            float ez = MathF.Min(result.Z - aabb.Min.Z, aabb.Max.Z - result.Z);

                            if (ex <= ey && ex <= ez)
                                result.X = result.X <= aabb.Min.X + ex ? aabb.Min.X - radius : aabb.Max.X + radius;
                            else if (ey <= ez)
                                result.Y = result.Y <= aabb.Min.Y + ey ? aabb.Min.Y - radius : aabb.Max.Y + radius;
                            else
                                result.Z = result.Z <= aabb.Min.Z + ez ? aabb.Min.Z - radius : aabb.Max.Z + radius;
                        }
                        else
                        {
                            // Push out along direction from closest point
                            float push = (radius - dist) * 1.05f; // slight over-push to avoid sticking
                            result.X += (dx / dist) * push;
                            result.Y += (dy / dist) * push;
                            result.Z += (dz / dist) * push;
                        }
                        pushed = true;
                    }
                }
            }

            return result;
        }

        /// <summary>Character collision (player dan NPC).</summary>
        public static Vector3 PushCharacter(Vector3 pos, StaticObjectManager[]? managers)
            => PushOutOfStaticObjects(pos, CharacterRadius, managers);

        /// <summary>Camera collision (third person).</summary>
        public static Vector3 PushCamera(Vector3 pos, StaticObjectManager[]? managers)
            => PushOutOfStaticObjects(pos, CameraRadius, managers);
    }
}
