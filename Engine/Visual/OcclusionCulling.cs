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
            if (_occluders.Count == 0 || objectAABBs.Length == 0) return;

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
                        if (RayIntersectsAABB(cameraPos, dir, _occluders[bi], out float hitDist))
                        {
                            // Skip occluder if camera is inside it (hitDist <= 0)
                            if (hitDist > 0.001f && hitDist < objDist)
                            {
                                cornerOccluded = true;
                                break;
                            }
                        }
                    }

                    // Check mesh occluders (BVH ray-triangle intersection)
                    if (!cornerOccluded)
                    {
                        for (int mi = 0; mi < _meshOccluders.Count; mi++)
                        {
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

        /// <summary>
        /// Test a single AABB against all registered occluders.
        /// Tests all 8 corners — returns true ONLY if ALL corners are occluded.
        /// Returns true if the object is occluded (hidden behind any occluder).
        /// </summary>
        public bool IsOccludedByOccluders(Vector3 cameraPos, Helpers.ObjectHelpers.AABB objAABB)
        {
            if (!Enabled) return false;
            if (_occluders.Count == 0) return false;

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
                    if (RayIntersectsAABB(cameraPos, dir, _occluders[bi], out float hitDist))
                    {
                        // Skip occluder if camera is inside it (hitDist <= 0)
                        if (hitDist > 0.001f && hitDist < objDist)
                        {
                            cornerOccluded = true;
                            break;
                        }
                    }
                }

                // Check mesh occluders (BVH ray-triangle intersection)
                if (!cornerOccluded)
                {
                    for (int mi = 0; mi < _meshOccluders.Count; mi++)
                    {
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

        /// <summary>Ray-AABB intersection test (slabs method). Returns both entry (t) and exit distance.</summary>
        private static bool RayIntersectsAABB(Vector3 origin, Vector3 dir, Helpers.ObjectHelpers.AABB box, out float t)
        {
            t = 0f;
            // exitDist computed inline (not stored separately)
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

            // Camera-inside-occluder fix: when camera is inside AABB (tmin <= 0),
            // use exit point (tmax) as hit distance instead of skipping the occluder.
            if (tmin <= 0f && tmax > 0.001f)
                t = tmax;
            else
                t = tmin;
            return true;
        }
    }
}
