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
        private const float CharacterHeight = 1.8f;   // sama dengan CharacterAgent.CharacterHeight
        private const float CameraRadius = 0.3f;

        /// <summary>
        /// Push position keluar dari semua static object yang IsCollidable=true.
        /// Cek sphere/capsule (center + radius) terhadap AABB setiap object collidable.
        /// Jika overlap, posisi di-push keluar sepanjang sumbu terdekat.
        /// </summary>
        /// <param name="position">Center position (foot position untuk capsule).</param>
        /// <param name="radius">Collision radius.</param>
        /// <param name="managers">Static object managers.</param>
        /// <param name="capsuleHeight">If > 0, treat as vertical capsule (standing character).</param>
        public static Vector3 PushOutOfStaticObjects(
            Vector3 position,
            float radius,
            StaticObjectManager[]? managers,
            float capsuleHeight = 0f)
        {
            if (managers == null) return position;

            Vector3 result = position;

            foreach (var mgr in managers)
            {
                if (mgr == null) continue;

                foreach (var obj in mgr.GetObjects())
                {
                    if (!obj.IsCollidable) continue;

                    var aabb = obj.CachedCollisionAABB ?? obj.CachedWorldAABB;

                    // Determine sphere center:
                    //   Sphere mode:     position (center)
                    //   Capsule mode:    closest point on capsule segment [pos.Y+radius, pos.Y+height-radius] to AABB
                    Vector3 sphereCenter;
                    if (capsuleHeight > 0f && capsuleHeight > radius * 2f)
                    {
                        // Capsule segment is vertical: (pos.X, pos.Y + radius, pos.Z) → (pos.X, pos.Y + capsuleHeight - radius, pos.Z)
                        float segStartY = position.Y + radius;
                        float segEndY = position.Y + capsuleHeight - radius;

                        // Find closest Y on segment to AABB center Y
                        float aabbCenterY = (aabb.Min.Y + aabb.Max.Y) * 0.5f;
                        float t = (segEndY - segStartY) > 0.001f
                            ? Math.Clamp((aabbCenterY - segStartY) / (segEndY - segStartY), 0f, 1f)
                            : 0.5f;
                        float sphereY = segStartY + t * (segEndY - segStartY);
                        sphereCenter = new Vector3(position.X, sphereY, position.Z);
                    }
                    else
                    {
                        sphereCenter = result;
                    }

                    // Closest point on AABB to sphere center
                    float closestX = Math.Clamp(sphereCenter.X, aabb.Min.X, aabb.Max.X);
                    float closestY = Math.Clamp(sphereCenter.Y, aabb.Min.Y, aabb.Max.Y);
                    float closestZ = Math.Clamp(sphereCenter.Z, aabb.Min.Z, aabb.Max.Z);

                    float dx = sphereCenter.X - closestX;
                    float dy = sphereCenter.Y - closestY;
                    float dz = sphereCenter.Z - closestZ;
                    float distSq = dx * dx + dy * dy + dz * dz;

                    if (distSq < radius * radius && distSq >= 0f)
                    {
                        // Push the ORIGINAL position, not the capsule center
                        // Re-compute closest point from result (original position)
                        float closestXr = Math.Clamp(result.X, aabb.Min.X, aabb.Max.X);
                        float closestYr = Math.Clamp(result.Y, aabb.Min.Y, aabb.Max.Y);
                        float closestZr = Math.Clamp(result.Z, aabb.Min.Z, aabb.Max.Z);

                        float dxr = result.X - closestXr;
                        float dyr = result.Y - closestYr;
                        float dzr = result.Z - closestZr;
                        float distSqr = dxr * dxr + dyr * dyr + dzr * dzr;

                        if (distSqr < radius * radius)
                        {
                            float dist = MathF.Sqrt(distSqr);
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
                                float push = (radius - dist) * 1.05f;
                                result.X += (dxr / dist) * push;
                                result.Y += (dyr / dist) * push;
                                result.Z += (dzr / dist) * push;
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>Character collision — sphere mode (default).</summary>
        public static Vector3 PushCharacter(Vector3 pos, StaticObjectManager[]? managers)
            => PushOutOfStaticObjects(pos, CharacterRadius, managers);

        /// <summary>Character collision — capsule mode (standing character).</summary>
        public static Vector3 PushCharacterCapsule(Vector3 pos, StaticObjectManager[]? managers, float? height = null)
            => PushOutOfStaticObjects(pos, CharacterRadius, managers, height ?? CharacterHeight);

        /// <summary>Camera collision (third person).</summary>
        public static Vector3 PushCamera(Vector3 pos, StaticObjectManager[]? managers)
            => PushOutOfStaticObjects(pos, CameraRadius, managers);

        /// <summary>
        /// Camera wall collision: geser camera ke arah pivot (player) sampai tidak overlap dengan wall.
        /// Berbeda dengan PushCamera yang push ke arah sembarang, method ini menjaga kamera tetap
        /// di belakang player — jika ada wall antara camera dan player, camera mundur (zoom in).
        /// </summary>
        /// <param name="cameraPos">Posisi camera saat ini (ideal position setelah terrain collision).</param>
        /// <param name="pivotPos">Posisi pivot (player head/body).</param>
        /// <param name="minDist">Jarak minimal camera ke pivot (tidak boleh lebih dekat dari ini).</param>
        /// <param name="managers">Static object managers untuk collision check.</param>
        /// <returns>Posisi camera yang sudah di-safe dari wall, lebih dekat ke pivot jika perlu.</returns>
        public static Vector3 SlideCameraToPivot(
            Vector3 cameraPos,
            Vector3 pivotPos,
            float minDist,
            StaticObjectManager[]? managers)
        {
            if (managers == null) return cameraPos;

            Vector3 camToPivot = pivotPos - cameraPos;
            float distToPivot = camToPivot.Length();
            if (distToPivot < 0.001f) return cameraPos;

            float radius = CameraRadius;

            Vector3 result = cameraPos;
            Vector3 dirToPivot = camToPivot / distToPivot; // arah dari camera ke player
            const int maxIterations = 5;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                // Update dirToPivot dari posisi result terkini (setelah push iterasi sebelumnya)
                Vector3 currentToPivot = pivotPos - result;
                float currentDist = currentToPivot.Length();
                if (currentDist < 0.001f) break;
                dirToPivot = currentToPivot / currentDist;

                bool anyOverlap = false;

                foreach (var mgr in managers)
                {
                    if (mgr == null) continue;
                    foreach (var obj in mgr.GetObjects())
                    {
                        if (!obj.IsCollidable) continue;

                        var aabb = obj.CachedCollisionAABB ?? obj.CachedWorldAABB;

                        // Sphere vs AABB: cek overlap
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
                            float pushDist = (radius - dist) * 1.05f;

                            if (dist < 0.001f)
                            {
                                // Camera center di dalam AABB — push searah dirToPivot
                                result += dirToPivot * pushDist;
                            }
                            else
                            {
                                // Proyeksikan push ke arah pivot (supaya camera maju ke player,
                                // bukan ke samping/tembus ke belakang wall)
                                float dot = (dx * dirToPivot.X + dy * dirToPivot.Y + dz * dirToPivot.Z) / dist;
                                if (dot > 0.01f)
                                {
                                    result += dirToPivot * (pushDist / dot);
                                }
                                else
                                {
                                    // Arah push tegak lurus atau menjauhi pivot — fallback ke push biasa
                                    result.X += (dx / dist) * pushDist;
                                    result.Y += (dy / dist) * pushDist;
                                    result.Z += (dz / dist) * pushDist;
                                }
                            }
                            anyOverlap = true;
                        }
                    }
                }

                if (!anyOverlap) break;
            }

            // Clamp jarak ke pivot — tidak boleh lebih dekat dari minDist
            Vector3 finalDir = pivotPos - result;
            float finalDist = finalDir.Length();
            if (finalDist < minDist)
            {
                if (finalDist < 0.001f)
                    result = pivotPos - dirToPivot * minDist;
                else
                    result = pivotPos - (finalDir / finalDist) * minDist;
            }

            return result;
        }
    }
}
