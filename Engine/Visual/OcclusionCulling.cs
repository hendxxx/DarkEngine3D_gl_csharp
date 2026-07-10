using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using System.Diagnostics;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>
    /// Software Occlusion Culling — CPU-based, menggunakan ray-AABB intersection test.
    /// Tidak bergantung pada GPU occlusion query (yang tidak berfungsi di beberapa driver).
    /// </summary>
    public class OcclusionCulling
    {
        public static bool Enabled { get; set; } = true;

        // ── Profiling ──
        public static long LastCheckVisibilityTicks = 0;
        public static long LastIsOccludedTicks = 0;

        // Occluders: world-space AABBs dari object besar (test boxes, nantinya terrain chunk)
        private readonly List<Helpers.ObjectHelpers.AABB> _occluders = [];

        // Mesh occluders: BVH untuk occlusion akurat berbasis mesh (bukan AABB)
        private readonly List<BVH> _meshOccluders = [];

        // Visibility state untuk setiap object (di-set per frame oleh CheckVisibility)
        private readonly List<bool> _visibilityResults = [];

        // ── Hysteresis ──
        // Distance margin to prevent jitter near occluder edges.
        // When a corner's distance to the occluder is within this margin of an occlusion
        // transition (e.g. camera inside AABB and objDist ≈ tmax), the margin prevents
        // the object from flickering between visible/occluded every frame.
        private const float HysteresisMargin = 0.5f;

        public OcclusionCulling() { }

        /// <summary>Daftarkan occluder — object yang bisa menghalangi pandangan.</summary>
        public void RegisterOccluder(Helpers.ObjectHelpers.AABB worldAABB)
        {
            _occluders.Add(worldAABB);
        }

        /// <summary>Daftarkan mesh occluder — BVH untuk ray-triangle occlusion test yang akurat.</summary>
        public void RegisterMeshOccluder(BVH bvh)
        {
            _meshOccluders.Add(bvh);
        }

        /// <summary>Daftarkan object yang akan dicek occlusion-nya.</summary>
        public int RegisterObject()
        {
            int idx = _visibilityResults.Count;
            _visibilityResults.Add(true);
            return idx;
        }

        /// <summary>Clear occluders setiap frame (biar bisa di-update).</summary>
        public void ClearOccluders()
        {
            _occluders.Clear();
            _meshOccluders.Clear();
        }

        /// <summary>
        /// Cek visibility untuk semua object yang terdaftar.
        /// Panggil SETIAP frame sebelum render.
        /// </summary>
        public void CheckVisibility(Vector3 cameraPos, Span<Helpers.ObjectHelpers.AABB> objectAABBs)
        {
            if (!Enabled) return;
            if ((_occluders.Count == 0 && _meshOccluders.Count == 0) || objectAABBs.Length == 0) return;

            var sw = Stopwatch.StartNew();

            // Allocate once outside the loop to avoid CA2014 warning
            Span<Vector3> corners = stackalloc Vector3[8];

            for (int oi = 0; oi < objectAABBs.Length && oi < _visibilityResults.Count; oi++)
            {
                var aabb = objectAABBs[oi];

                // Fill 8 corners for this AABB
                corners[0] = new Vector3(aabb.Min.X, aabb.Min.Y, aabb.Min.Z);
                corners[1] = new Vector3(aabb.Max.X, aabb.Min.Y, aabb.Min.Z);
                corners[2] = new Vector3(aabb.Max.X, aabb.Max.Y, aabb.Min.Z);
                corners[3] = new Vector3(aabb.Min.X, aabb.Max.Y, aabb.Min.Z);
                corners[4] = new Vector3(aabb.Min.X, aabb.Min.Y, aabb.Max.Z);
                corners[5] = new Vector3(aabb.Max.X, aabb.Min.Y, aabb.Max.Z);
                corners[6] = new Vector3(aabb.Max.X, aabb.Max.Y, aabb.Max.Z);
                corners[7] = new Vector3(aabb.Min.X, aabb.Max.Y, aabb.Max.Z);

                bool allCornersOccluded = true;
                for (int ci = 0; ci < 8; ci++)
                {
                    Vector3 dir = corners[ci] - cameraPos;
                    float objDist = dir.Length();
                    if (objDist < 0.001f) { allCornersOccluded = false; break; }
                    dir /= objDist;

                    bool cornerOccluded = false;

                    // Check AABB occluders
                    for (int bi = 0; bi < _occluders.Count; bi++)
                    {
                        if (RayIntersectsAABB(cameraPos, dir, _occluders[bi], out float hitDist, out bool camInside))
                        {
                            // Hysteresis: when camera is inside the occluder and objDist ≈ tmax
                            // (object near exit wall), add margin to prevent jitter.
                            float compareDist = camInside ? hitDist + HysteresisMargin : hitDist;
                            if (hitDist > 0.001f && compareDist < objDist)
                            {
                                cornerOccluded = true;
                                break;
                            }
                        }
                    }

                    // Check mesh occluders (BVH ray-triangle intersection)
                    // Skip occluders whose root AABB fully contains the object —
                    // prevents "container" meshes (e.g. dungeon) from occluding objects inside them.
                    if (!cornerOccluded)
                    {
                        for (int mi = 0; mi < _meshOccluders.Count; mi++)
                        {
                            if (_meshOccluders[mi].Root != null &&
                                IsAABBContained(aabb, _meshOccluders[mi].Root!.Bounds))
                                continue;
                            if (_meshOccluders[mi].RayIntersects(cameraPos, dir, objDist))
                            {
                                cornerOccluded = true;
                                break;
                            }
                        }
                    }

                    if (!cornerOccluded)
                    {
                        allCornersOccluded = false;
                        break;
                    }
                }

                _visibilityResults[oi] = !allCornersOccluded;
            }

            sw.Stop();
            LastCheckVisibilityTicks = sw.ElapsedTicks;
        }

        /// <summary>Dapatkan hasil visibility untuk satu object.</summary>
        public bool IsVisible(int queryIndex)
        {
            if (!Enabled) return true;
            if (queryIndex < 0 || queryIndex >= _visibilityResults.Count) return true;
            return _visibilityResults[queryIndex];
        }

        public int ObjectCount => _visibilityResults.Count;
        public int OccluderCount => _occluders.Count;
        public List<Helpers.ObjectHelpers.AABB> GetAABBOccluders() => _occluders;
        public List<BVH> GetMeshOccluders() => _meshOccluders;

        /// <summary>
        /// Mesh-aware occludee test: uses the occludee's own BVH leaf nodes for more accurate testing.
        /// Instead of testing 8 corners of the root AABB, tests ray from camera to each leaf center.
        /// If all leaf centers are occluded → object is occluded.
        /// </summary>
        /// <param name="occludeeBVH">BVH of the object being tested (for leaf sampling).</param>
        /// <returns>True if the object is occluded by any registered occluder.</returns>
        public bool IsMeshOccluded(Vector3 cameraPos, BVH occludeeBVH)
        {
            if (!Enabled) return false;
            if (_occluders.Count == 0 && _meshOccluders.Count == 0) return false;

            // Test 8 corners of root AABB — sama seperti IsOccludedByOccluders,
            // tapi skip occludee's own BVH untuk mencegah self-occlusion
            var rootBounds = occludeeBVH.Root?.Bounds;
            if (!rootBounds.HasValue) return false;

            Span<Vector3> corners = stackalloc Vector3[8]
            {
                new(rootBounds.Value.Min.X, rootBounds.Value.Min.Y, rootBounds.Value.Min.Z),
                new(rootBounds.Value.Max.X, rootBounds.Value.Min.Y, rootBounds.Value.Min.Z),
                new(rootBounds.Value.Max.X, rootBounds.Value.Max.Y, rootBounds.Value.Min.Z),
                new(rootBounds.Value.Min.X, rootBounds.Value.Max.Y, rootBounds.Value.Min.Z),
                new(rootBounds.Value.Min.X, rootBounds.Value.Min.Y, rootBounds.Value.Max.Z),
                new(rootBounds.Value.Max.X, rootBounds.Value.Min.Y, rootBounds.Value.Max.Z),
                new(rootBounds.Value.Max.X, rootBounds.Value.Max.Y, rootBounds.Value.Max.Z),
                new(rootBounds.Value.Min.X, rootBounds.Value.Max.Y, rootBounds.Value.Max.Z),
            };

            for (int ci = 0; ci < 8; ci++)
            {
                Vector3 dir = corners[ci] - cameraPos;
                float objDist = dir.Length();
                if (objDist < 0.001f) return false;
                dir /= objDist;

                bool cornerOccluded = false;

                // Check AABB occluders
                for (int bi = 0; bi < _occluders.Count; bi++)
                {
                    if (RayIntersectsAABB(cameraPos, dir, _occluders[bi], out float hitDist, out bool camInside))
                    {
                        float compareDist = camInside ? hitDist + HysteresisMargin : hitDist;
                        if (hitDist > 0.001f && compareDist < objDist)
                        {
                            cornerOccluded = true;
                            break;
                        }
                    }
                }

                // Check mesh occluders (skip self BVH + contained BVHs)
                if (!cornerOccluded)
                {
                    for (int mi = 0; mi < _meshOccluders.Count; mi++)
                    {
                        if (_meshOccluders[mi] == occludeeBVH) continue;
                        // Skip if occludee's root AABB is inside the occluder's root AABB
                        // (container mesh should not occlude contained meshes)
                        if (_meshOccluders[mi].Root != null &&
                            occludeeBVH.Root != null &&
                            IsAABBContained(occludeeBVH.Root.Bounds, _meshOccluders[mi].Root.Bounds))
                            continue;
                        if (_meshOccluders[mi].RayIntersects(cameraPos, dir, objDist))
                        {
                            cornerOccluded = true;
                            break;
                        }
                    }
                }

                if (!cornerOccluded)
                    return false; // Found a visible corner → object is visible
            }

            return true; // All corners occluded
        }

        /// <summary>
        /// Test a single AABB against all registered occluders.
        /// Tests all 8 corners — returns true ONLY if ALL corners are occluded.
        /// Returns true if the object is occluded (hidden behind any occluder).
        /// </summary>
        public bool IsOccludedByOccluders(Vector3 cameraPos, Helpers.ObjectHelpers.AABB objAABB)
        {
            if (!Enabled) return false;
            if (_occluders.Count == 0 && _meshOccluders.Count == 0) return false;

            var sw = Stopwatch.StartNew();

            // Test all 8 corners — object is visible if ANY corner is visible
            Span<Vector3> corners = stackalloc Vector3[8]
            {
                new(objAABB.Min.X, objAABB.Min.Y, objAABB.Min.Z),
                new(objAABB.Max.X, objAABB.Min.Y, objAABB.Min.Z),
                new(objAABB.Max.X, objAABB.Max.Y, objAABB.Min.Z),
                new(objAABB.Min.X, objAABB.Max.Y, objAABB.Min.Z),
                new(objAABB.Min.X, objAABB.Min.Y, objAABB.Max.Z),
                new(objAABB.Max.X, objAABB.Min.Y, objAABB.Max.Z),
                new(objAABB.Max.X, objAABB.Max.Y, objAABB.Max.Z),
                new(objAABB.Min.X, objAABB.Max.Y, objAABB.Max.Z),
            };

            for (int ci = 0; ci < 8; ci++)
            {
                Vector3 dir = corners[ci] - cameraPos;
                float objDist = dir.Length();
                if (objDist < 0.001f) return false;
                dir /= objDist;

                bool cornerOccluded = false;

                // Check AABB occluders
                for (int bi = 0; bi < _occluders.Count; bi++)
                {
                    if (RayIntersectsAABB(cameraPos, dir, _occluders[bi], out float hitDist, out bool camInside))
                    {
                        float compareDist = camInside ? hitDist + HysteresisMargin : hitDist;
                        if (hitDist > 0.001f && compareDist < objDist)
                        {
                            cornerOccluded = true;
                            break;
                        }
                    }
                }

                // Check mesh occluders (BVH ray-triangle intersection)
                // Skip occluders whose root AABB fully contains the object —
                // prevents "container" meshes (e.g. dungeon) from occluding objects inside them.
                if (!cornerOccluded)
                {
                    for (int mi = 0; mi < _meshOccluders.Count; mi++)
                    {
                        if (_meshOccluders[mi].Root != null &&
                            IsAABBContained(objAABB, _meshOccluders[mi].Root!.Bounds))
                            continue;
                        if (_meshOccluders[mi].RayIntersects(cameraPos, dir, objDist))
                        {
                            cornerOccluded = true;
                            break;
                        }
                    }
                }

                if (!cornerOccluded)
                    return false; // found a visible corner → object is visible
            }

            sw.Stop();
            LastIsOccludedTicks = sw.ElapsedTicks;
            return true; // all corners occluded
        }

    public int OccludedCount
        {
            get
            {
                if (!Enabled) return 0;
                int c = 0;
                for (int i = 0; i < _visibilityResults.Count; i++)
                    if (!_visibilityResults[i]) c++;
                return c;
            }
        }

        public int VisibleCount
        {
            get
            {
                if (!Enabled) return _visibilityResults.Count;
                int c = 0;
                for (int i = 0; i < _visibilityResults.Count; i++)
                    if (_visibilityResults[i]) c++;
                return c;
            }
        }

        /// <summary>
        /// Check if an AABB is fully contained within another AABB.
        /// Used to detect when an object is inside a "container" mesh
        /// (e.g., a prop inside a dungeon) so the container mesh doesn't
        /// occlude the contained object.
        /// </summary>
        private static bool IsAABBContained(Helpers.ObjectHelpers.AABB inner, Helpers.ObjectHelpers.AABB outer)
        {
            return inner.Min.X >= outer.Min.X &&
                   inner.Min.Y >= outer.Min.Y &&
                   inner.Min.Z >= outer.Min.Z &&
                   inner.Max.X <= outer.Max.X &&
                   inner.Max.Y <= outer.Max.Y &&
                   inner.Max.Z <= outer.Max.Z;
        }

        /// <summary>Ray-AABB intersection test (slabs method). Returns entry (tmin) or exit (tmax) distance.</summary>
        /// <remarks>
        /// Camera-inside-occluder logic: when camera is inside AABB (tmin ≤ 0), we return
        /// tmax (exit point) as the hit distance. This allows the caller's hitDist < objDist
        /// check to correctly determine:
        ///   - Object inside same AABB (objDist < tmax) → visible ✓
        ///   - Object outside AABB (objDist > tmax) → occluded ✓
        ///
        /// Hysteresis: callers add a 0.5m margin (hitDist + HysteresisMargin < objDist) when
        /// camera is inside, preventing jitter when objDist ≈ tmax at the exit wall.
        /// </remarks>
        /// <summary>Ray-AABB intersection test (slabs method). Returns entry/exit distance + inside flag.</summary>
        /// <param name="cameraInside">True if the ray origin is inside the AABB (tmin ≤ 0).</param>
        /// <remarks>
        /// Camera-inside-occluder logic: when origin is inside AABB (tmin ≤ 0), we return
        /// tmax (exit point) as the hit distance. This allows the caller to correctly determine:
        ///   - Object inside same AABB (objDist < tmax) → visible ✓
        ///   - Object outside AABB (objDist > tmax) → occluded ✓
        ///
        /// Hysteresis: when cameraInside is true, callers add HysteresisMargin to the comparison
        /// to prevent jitter when objDist ≈ tmax (object near the exit wall).
        /// </remarks>
        private static bool RayIntersectsAABB(Vector3 origin, Vector3 dir, Helpers.ObjectHelpers.AABB box, out float t, out bool cameraInside)
        {
            t = 0f;
            cameraInside = false;
            float tmin = 0f;
            float tmax = float.MaxValue;

            for (int axis = 0; axis < 3; axis++)
            {
                float originComp = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
                float dirComp = axis == 0 ? dir.X : axis == 1 ? dir.Y : dir.Z;
                float minComp = axis == 0 ? box.Min.X : axis == 1 ? box.Min.Y : box.Min.Z;
                float maxComp = axis == 0 ? box.Max.X : axis == 1 ? box.Max.Y : box.Max.Z;

                if (MathF.Abs(dirComp) < 1e-7f)
                {
                    if (originComp < minComp || originComp > maxComp)
                        return false;
                }
                else
                {
                    float invD = 1.0f / dirComp;
                    float t1 = (minComp - originComp) * invD;
                    float t2 = (maxComp - originComp) * invD;
                    if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                    tmin = MathF.Max(tmin, t1);
                    tmax = MathF.Min(tmax, t2);
                    if (tmin > tmax) return false;
                }
            }

            // Camera-inside-occluder: return exit point (tmax) so callers can test objDist < tmax
            if (tmin <= 0f && tmax > 0.001f)
            {
                cameraInside = true;
                t = tmax;
            }
            else
            {
                t = tmin;
            }
            return true;
        }
    }
}
