using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>
    /// Hi-Z Occlusion Culling — screen-space depth buffer via CPU ray-march.
    /// 
    /// Depth buffer: configurable via HiZResolutionScale.
    /// Default (0.25): 32×18 = 576 pixel, ~2.880 GetHeightAt/frame.
    /// SW mode: ~63K GetHeightAt/frame.
    /// 
    /// Terrain occlusion: 1-point check (bottom center AABB).
    /// Wall occlusion: 8-corner AABB test (sama dengan SW mode).
    /// </summary>
    public class HiZOcc : IDisposable
    {
        // Depth buffer resolution (dari config HiZResolutionScale)
        private readonly int _depthW;
        private readonly int _depthH;
        private readonly float[] _depthBuffer;

        // Cached camera state
        private Vector3 _lastCamPos;
        private Vector3 _lastCamFront;
        private Vector3 _lastCamRight;
        private Vector3 _lastCamUp;
        private float _lastTanHalfFov;
        private float _lastAspect;
        private bool _depthValid;

        // Occluder AABBs untuk Phase 2B (wall occlusion)
        private readonly List<Helpers.ObjectHelpers.AABB> _occluders = [];

        public HiZOcc()
        {
            float scale = DarkEngine3D_gl_csharp.Engine.Config.OcclusionConfig.HiZResolutionScale;
            _depthW = Math.Max(8, (int)(128 * scale));
            _depthH = Math.Max(4, (int)(72 * scale));
            _depthBuffer = new float[_depthW * _depthH];
        }

        // =====================================================
        // OCCLUDER API (Phase 2B — wall occlusion)
        // =====================================================

        public void ClearOccluders()
        {
            _occluders.Clear();
        }

        public void RegisterOccluder(Helpers.ObjectHelpers.AABB aabb)
        {
            _occluders.Add(aabb);
        }

        public int OccluderCount => _occluders.Count;

        /// <summary>8-corner AABB test terhadap wall occluders.</summary>
        public bool IsOccluded(Vector3 cameraPos, Helpers.ObjectHelpers.AABB objAABB)
        {
            if (_occluders.Count == 0) return false;

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

                if (!cornerOccluded)
                    return false;
            }

            return true;
        }

        // =====================================================
        // DEPTH BUFFER GENERATION (Phase 0)
        // =====================================================

        /// <summary>
        /// Generate depth buffer: ray-march terrain untuk cari jarak ke permukaan pertama.
        /// Panggil SETIAP frame sebelum occlusion test.
        /// Cost: ~_depthW × _depthH × ~5 step GetHeightAt/frame.
        /// </summary>
        public void GenerateTerrainDepth(Camera camera, TerrainChunk terrain)
        {
            _lastCamPos = camera.Position;
            _lastCamFront = camera.Front;
            _lastCamRight = camera.Right;
            _lastCamUp = camera.Up;
            _lastTanHalfFov = MathF.Tan(camera.FoV * 0.5f * MathF.PI / 180f);
            _lastAspect = (float)Glfw.WindowWidth / (float)Glfw.WindowHeight;
            _depthValid = true;

            float camTerrainH = terrain.GetHeightAt(_lastCamPos.X, _lastCamPos.Z);
            bool camBelowTerrain = _lastCamPos.Y < camTerrainH - 0.5f;

            int totalCells = _depthW * _depthH;
            for (int py = 0; py < _depthH; py++)
            {
                for (int px = 0; px < _depthW; px++)
                {
                    // Pixel center → NDC
                    float ndcX = (px + 0.5f) / _depthW * 2f - 1f;
                    float ndcY = ((py + 0.5f) / _depthH * 2f - 1f);

                    // Camera-space direction (looking along -Z)
                    float camX = ndcX * _lastAspect * _lastTanHalfFov;
                    float camY = ndcY * _lastTanHalfFov;

                    // Transform ke world space
                    Vector3 dir = Vector3.Normalize(
                        _lastCamRight * camX + _lastCamUp * camY + _lastCamFront
                    );

                    // Ray-march terrain
                    float depth = camBelowTerrain
                        ? float.MaxValue // kamera di bawah terrain → skip (semua visible)
                        : RayMarchFast(_lastCamPos, dir, terrain, camTerrainH);

                    _depthBuffer[py * _depthW + px] = depth;
                }
            }
        }

        /// <summary>Ray-march dengan step dari config agar ridge tipis tidak terlewat.</summary>
        private static float RayMarchFast(Vector3 origin, Vector3 dir, TerrainChunk terrain, float camTerrainH)
        {
            // Early-out: kamera di atas terrain dan melihat ke atas → tidak kena terrain
            // Note: dir.Y < 0 berarti ray menurun → mungkin kena terrain meski kamera di atas
            if (dir.Y >= 0f && origin.Y > camTerrainH)
                return float.MaxValue;

            float stepSize = DarkEngine3D_gl_csharp.Engine.Config.OcclusionConfig.HiZStepSize;
            int maxSteps = (int)(300f / stepSize); // 300m range

            // Skip very near camera (2m) untuk hindari false hit dengan terrain di dekat kaki
            int startStep = (int)(2f / stepSize) + 1;

            for (int step = startStep; step <= maxSteps; step++)
            {
                float t = step * stepSize;
                Vector3 samplePos = origin + dir * t;
                float terrainH = terrain.GetHeightAt(samplePos.X, samplePos.Z);

                if (samplePos.Y < terrainH)
                {
                    // Interpolasi linear antara step sebelumnya dan step ini
                    float tPrev = (step - 1) * stepSize;
                    Vector3 prevPos = origin + dir * tPrev;
                    float prevH = terrain.GetHeightAt(prevPos.X, prevPos.Z);
                    float prevDiff = prevPos.Y - prevH;
                    float currDiff = samplePos.Y - terrainH;

                    if (prevDiff > 0f && currDiff < 0f)
                    {
                        float frac = prevDiff / (prevDiff - currDiff);
                        return tPrev + stepSize * Math.Clamp(frac, 0f, 1f);
                    }
                    return t;
                }
            }

            return float.MaxValue;
        }

        // =====================================================
        // TERRAIN OCCLUSION TEST (Phase 2B) — 1-point bottom center
        // =====================================================

        /// <summary>
        /// Test apakah AABB object teroklusi oleh terrain (berdasarkan depth buffer).
        /// Project BOTTOM CENTER AABB ke screen → sample depth → compare jarak.
        /// 
        /// Hanya 1 point (bukan 8 corners) karena:
        /// - Daisies/grass: bottom center = posisi di tanah, paling representatif
        /// - Trees: bottom center cukup untuk cek apakah pohon di balik bukit
        /// </summary>
        public bool IsTerrainOccluded(Vector3 cameraPos, Helpers.ObjectHelpers.AABB aabb)
        {
            if (!_depthValid || _lastCamPos != cameraPos)
                return false;

            // Bottom center AABB — titik paling relevan untuk terrain occlusion
            Vector3 bottomCenter = new(
                (aabb.Min.X + aabb.Max.X) * 0.5f,
                aabb.Min.Y,
                (aabb.Min.Z + aabb.Max.Z) * 0.5f
            );

            Vector3 toObj = bottomCenter - _lastCamPos;
            float objDist = toObj.Length();
            if (objDist < 1f) return false;

            // Camera-space projection
            float camZ = -Vector3.Dot(toObj, _lastCamFront);
            if (camZ <= 0f) return false; // behind camera

            float camX = Vector3.Dot(toObj, _lastCamRight);
            float camY = Vector3.Dot(toObj, _lastCamUp);

            // NDC
            float ndcX = camX / (camZ * _lastAspect * _lastTanHalfFov);
            float ndcY = camY / (camZ * _lastTanHalfFov);

            // Di luar viewport → tidak bisa ditentukan → visible
            if (ndcX < -1.1f || ndcX > 1.1f || ndcY < -1.1f || ndcY > 1.1f)
                return false;

            // NDC → depth buffer pixel
            float px = (ndcX + 1f) * 0.5f * _depthW;
            float py = (1f - ndcY) * 0.5f * _depthH;

            // Sample depth buffer
            float sampledDepth = SampleDepthBilinear(px, py);

            // Tidak ada terrain di arah ini → visible
            if (sampledDepth >= 1e9f) return false;

            // Object center lebih jauh dari terrain hit → occluded (bias 1m)
            return objDist > sampledDepth + 1.0f;
        }

        /// <summary>Bilinear sample depth buffer (clamp to edge).</summary>
        private float SampleDepthBilinear(float px, float py)
        {
            px = Math.Clamp(px, 0f, _depthW - 1);
            py = Math.Clamp(py, 0f, _depthH - 1);

            int ix = (int)px;
            int iy = (int)py;

            if (ix >= _depthW - 1) ix = _depthW - 2;
            if (iy >= _depthH - 1) iy = _depthH - 2;

            float fx = px - ix;
            float fy = py - iy;

            float d00 = _depthBuffer[iy * _depthW + ix];
            float d10 = _depthBuffer[iy * _depthW + ix + 1];
            float d01 = _depthBuffer[(iy + 1) * _depthW + ix];
            float d11 = _depthBuffer[(iy + 1) * _depthW + ix + 1];

            float d0 = d00 + (d10 - d00) * fx;
            float d1 = d01 + (d11 - d01) * fx;
            return d0 + (d1 - d0) * fy;
        }

        // =====================================================
        // RAY-AABB (slabs method)
        // =====================================================

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

        public void Dispose()
        {
            _occluders.Clear();
        }
    }
}
