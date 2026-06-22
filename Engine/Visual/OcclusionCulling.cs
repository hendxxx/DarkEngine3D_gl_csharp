using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Libs;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>
    /// Software Occlusion Culling — CPU-based, menggunakan ray-AABB intersection test.
    /// Tidak bergantung pada GPU occlusion query (yang tidak berfungsi di beberapa driver).
    /// </summary>
    public class OcclusionCulling
    {
        public static bool Enabled { get; set; } = false;

        // Occluders: world-space AABBs dari object besar (test boxes, nantinya terrain chunk)
        private readonly List<Helpers.ObjectHelpers.AABB> _occluders = [];

        // Visibility state untuk setiap object (di-set per frame oleh CheckVisibility)
        private readonly List<bool> _visibilityResults = [];

        public OcclusionCulling() { }

        /// <summary>Daftarkan occluder — object yang bisa menghalangi pandangan.</summary>
        public void RegisterOccluder(Helpers.ObjectHelpers.AABB worldAABB)
        {
            _occluders.Add(worldAABB);
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
        }

        /// <summary>
        /// Cek visibility untuk semua object yang terdaftar.
        /// Panggil SETIAP frame sebelum render.
        /// </summary>
        public void CheckVisibility(Vector3 cameraPos, Span<Helpers.ObjectHelpers.AABB> objectAABBs)
        {
            if (!Enabled) return;
            if (_occluders.Count == 0 || objectAABBs.Length == 0) return;

            for (int oi = 0; oi < objectAABBs.Length && oi < _visibilityResults.Count; oi++)
            {
                var aabb = objectAABBs[oi];
                Vector3 center = (aabb.Min + aabb.Max) * 0.5f;
                Vector3 dir = center - cameraPos;
                float objDist = dir.Length();
                if (objDist < 0.001f) { _visibilityResults[oi] = true; continue; }
                dir /= objDist;

                bool occluded = false;
                for (int bi = 0; bi < _occluders.Count; bi++)
                {
                    if (RayIntersectsAABB(cameraPos, dir, _occluders[bi], out float hitDist))
                    {
                        if (hitDist < objDist)
                        {
                            occluded = true;
                            break;
                        }
                    }
                }

                _visibilityResults[oi] = !occluded;
            }
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

        /// <summary>Ray-AABB intersection test (slabs method).</summary>
        private static bool RayIntersectsAABB(Vector3 origin, Vector3 dir, Helpers.ObjectHelpers.AABB box, out float t)
        {
            t = 0f;
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
                    // Ray sejajar dengan axis ini — cek apakah origin di dalam slab
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

            t = tmin;
            return true;
        }
    }
}
