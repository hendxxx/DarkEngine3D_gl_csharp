using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    // ===========================================================================
    //  ObjectManager — mengelola ratusan GltfObject secara efisien
    //  Flyweight: satu GltfModelGpuData dibagi banyak GltfObject
    //  Optimization: frustum culling per AABB sebelum draw
    // ===========================================================================
    public unsafe class ObjectManager
    {
        // ---- Model cache: path → shared GPU data ----
        private readonly Dictionary<string, GltfModelGpuData> _modelCache = [];

        // ---- List semua instance ----
        private readonly List<GltfObject> _objects = [];

        // ---- Shader uniforms ----
        private readonly uint _shaderProgram;
        private readonly int _modelLoc;
        private readonly int _viewLoc;
        private readonly int _projLoc;
        private readonly int _jointMatsLoc;
        private readonly int _numJointsLoc;
        private readonly int _hasSkinLoc;
        private readonly int _sunDirLoc;
        private readonly int _lightColorLoc;
        private readonly int _fogColorLoc;
        private readonly int _viewPosLoc2;
        private readonly int _useFogLoc;

        // Stat untuk HUD / debugging
        public int DrawnObjects { get; private set; }
        public int CulledObjects { get; private set; }

        /// <summary>Matikan frustum cull sementara untuk debug (default=false).</summary>
        public bool DisableFrustumCull = false;

        // ===========================================================================
        public ObjectManager()
        {
            _shaderProgram = GltfShader.GetProgram();
            _modelLoc = GL.GetUniformLocation(_shaderProgram, "model");
            _viewLoc = GL.GetUniformLocation(_shaderProgram, "view");
            _projLoc = GL.GetUniformLocation(_shaderProgram, "projection");
            _jointMatsLoc = GL.GetUniformLocation(_shaderProgram, "jointMatrices");
            _numJointsLoc = GL.GetUniformLocation(_shaderProgram, "numJoints");
            _hasSkinLoc = GL.GetUniformLocation(_shaderProgram, "hasSkin");
            _sunDirLoc = GL.GetUniformLocation(_shaderProgram, "sunDir");
            _lightColorLoc = GL.GetUniformLocation(_shaderProgram, "lightColor");
            _fogColorLoc = GL.GetUniformLocation(_shaderProgram, "fogColor");
            _viewPosLoc2 = GL.GetUniformLocation(_shaderProgram, "viewPos");
            _useFogLoc = GL.GetUniformLocation(_shaderProgram, "useFog");

            Console.WriteLine($"[ObjectManager] shader={_shaderProgram} model={_modelLoc} view={_viewLoc} proj={_projLoc} joints={_jointMatsLoc} numJoints={_numJointsLoc} hasSkin={_hasSkinLoc}");
        }

        // -----------------------------------------------------------------------
        /// <summary>Load model dari file (cached). Return GltfModelGpuData yang bisa di-share.</summary>
        public GltfModelGpuData LoadModel(string path)
        {
            if (_modelCache.TryGetValue(path, out var cached)) return cached;
            Console.WriteLine($"[ObjectManager] Loading model: {path}");
            var data = GltfLoader.Load(path);
            var gpuData = new GltfModelGpuData(data);
            _modelCache[path] = gpuData;
            return gpuData;
        }

        // -----------------------------------------------------------------------
        /// <summary>Tambah instance baru. Posisi dalam world space.</summary>
        public GltfObject AddObject(string modelPath, Vector3 position, float yawDegrees = 0f, float scale = 1f)
        {
            var gpuData = LoadModel(modelPath);
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f);
            var obj = new GltfObject(gpuData, position, q, scale);
            _objects.Add(obj);
            return obj;
        }

        // -----------------------------------------------------------------------
        /// <summary>Update semua object (animasi, dsb).</summary>
        public void Update(float dt)
        {
            foreach (var obj in _objects)
                obj.Update(dt);
        }

        // -----------------------------------------------------------------------
        /// <summary>Draw semua object dengan frustum culling (AABB vs camera frustum).</summary>
        public void Draw(Camera camera, Lights light)
        {
            DrawnObjects = 0;
            CulledObjects = 0;

            GL.UseProgram(_shaderProgram);

            // Set view & projection ke shader gltf
            // Match terrain shader convention: System.Numerics row-major, transpose=false
            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            GL.UniformMatrix4fv(_viewLoc, 1, false, (float*)Unsafe.AsPointer(ref view));
            GL.UniformMatrix4fv(_projLoc, 1, false, (float*)Unsafe.AsPointer(ref proj));

            // Set lighting ke gltf shader
            GL.Uniform3f(_sunDirLoc, light.SunDir.X, light.SunDir.Y, light.SunDir.Z);
            GL.Uniform3f(_lightColorLoc, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
            GL.Uniform3f(_fogColorLoc, light.FogColor.X, light.FogColor.Y, light.FogColor.Z);
            GL.Uniform3f(_viewPosLoc2, camera.Position.X, camera.Position.Y, camera.Position.Z);
            GL.Uniform1i(_useFogLoc, Inputs.Keyboard.GetIsFogActive() ? 1 : 0);

            // Frustum planes dari VP matrix
            var frustum = ExtractFrustumPlanes(Matrix4x4.Multiply(view, proj));

            foreach (var obj in _objects)
            {
                // Frustum culling berdasarkan AABB world space
                if (!DisableFrustumCull && !IsAABBInFrustum(frustum, obj.WorldAABB))
                {
                    CulledObjects++;
                    continue;
                }

                // T-pose only — animasi dimatikan sementara
                GL.Uniform1i(_hasSkinLoc, 0);




                obj.Draw(_modelLoc, _jointMatsLoc, _numJointsLoc);
                DrawnObjects++;
            }
        }

        // -----------------------------------------------------------------------
        /// <summary>Periksa apakah object ini berbenturan dengan terrain (Y-axis).</summary>
        public void SnapToTerrain(GltfObject obj, TerrainChunk terrain)
        {
            float terrainY = terrain.GetHeightAt(obj.Position.X, obj.Position.Z);
            obj.Position = new Vector3(obj.Position.X, terrainY, obj.Position.Z);
        }

        /// <summary>Snap semua object ke terrain.</summary>
        public void SnapAllToTerrain(TerrainChunk terrain)
        {
            foreach (var obj in _objects) SnapToTerrain(obj, terrain);
        }

        // -----------------------------------------------------------------------
        /// <summary>Collision detection antar object (AABB broad phase).</summary>
        public List<(GltfObject A, GltfObject B)> DetectCollisions()
        {
            var result = new List<(GltfObject, GltfObject)>();
            for (int i = 0; i < _objects.Count; i++)
                for (int j = i + 1; j < _objects.Count; j++)
                {
                    if (_objects[i].WorldAABB.Intersects(_objects[j].WorldAABB))
                        result.Add((_objects[i], _objects[j]));
                }
            return result;
        }

        public List<GltfObject> GetObjects() => _objects;

        // ===========================================================================
        //  Frustum Culling Helpers
        // ===========================================================================
        private static Vector4[] ExtractFrustumPlanes(Matrix4x4 vp)
        {
            // 6 planes: left, right, bottom, top, near, far
            var p = new Vector4[6];
            p[0] = new Vector4(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41); // left
            p[1] = new Vector4(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41); // right
            p[2] = new Vector4(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42); // bottom
            p[3] = new Vector4(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42); // top
            p[4] = new Vector4(vp.M14 + vp.M13, vp.M24 + vp.M23, vp.M34 + vp.M33, vp.M44 + vp.M43); // near
            p[5] = new Vector4(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43); // far
            // Normalize
            for (int i = 0; i < 6; i++)
            {
                float len = MathF.Sqrt(p[i].X * p[i].X + p[i].Y * p[i].Y + p[i].Z * p[i].Z);
                if (len > 0f) p[i] /= len;
            }
            return p;
        }

        private static bool IsAABBInFrustum(Vector4[] planes, AABB aabb)
        {
            foreach (var pl in planes)
            {
                // Cek positive vertex (pojok paling depan ke arah normal plane)
                var pv = new Vector3(
                    pl.X >= 0 ? aabb.Max.X : aabb.Min.X,
                    pl.Y >= 0 ? aabb.Max.Y : aabb.Min.Y,
                    pl.Z >= 0 ? aabb.Max.Z : aabb.Min.Z);

                if (pv.X * pl.X + pv.Y * pl.Y + pv.Z * pl.Z + pl.W < 0f)
                    return false; // AABB di luar plane ini
            }
            return true;
        }

        public void Dispose()
        {
            foreach (var (_, gpu) in _modelCache) gpu.Dispose();
            _modelCache.Clear();
            _objects.Clear();
        }
    }
}
