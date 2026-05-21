using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    // ===========================================================================
    //  ObjectManager — static mesh glTF, flyweight pattern, frustum culling
    // ===========================================================================
    public unsafe class ObjectManager
    {
        private readonly Dictionary<string, GltfModelGpuData> _modelCache = [];
        private readonly List<GltfObject>                     _objects    = [];

        private readonly uint _shaderProgram;
        private readonly int  _modelLoc;
        private readonly int  _viewLoc;
        private readonly int  _projLoc;
        private readonly int  _sunDirLoc;
        private readonly int  _lightColorLoc;
        private readonly int  _fogColorLoc;
        private readonly int  _viewPosLoc;
        private readonly int  _useFogLoc;
        private readonly int  _baseColorFactorLoc;
        private readonly int  _useAlbedoLoc;
        private readonly int  _albedoMapLoc;
        private readonly int  _jointLoc;

        public int  DrawnObjects  { get; private set; }
        public int  CulledObjects { get; private set; }
        public bool DisableFrustumCull = false;

        // -----------------------------------------------------------------------
        public ObjectManager()
        {
            _shaderProgram      = GltfShader.GetProgram();
            _modelLoc           = GL.GetUniformLocation(_shaderProgram, "model");
            _viewLoc            = GL.GetUniformLocation(_shaderProgram, "view");
            _projLoc            = GL.GetUniformLocation(_shaderProgram, "projection");
            _sunDirLoc          = GL.GetUniformLocation(_shaderProgram, "sunDir");
            _lightColorLoc      = GL.GetUniformLocation(_shaderProgram, "lightColor");
            _fogColorLoc        = GL.GetUniformLocation(_shaderProgram, "fogColor");
            _viewPosLoc         = GL.GetUniformLocation(_shaderProgram, "viewPos");
            _useFogLoc          = GL.GetUniformLocation(_shaderProgram, "useFog");
            _baseColorFactorLoc = GL.GetUniformLocation(_shaderProgram, "baseColorFactor");
            _useAlbedoLoc       = GL.GetUniformLocation(_shaderProgram, "useAlbedo");
            _albedoMapLoc       = GL.GetUniformLocation(_shaderProgram, "albedoMap");
            _jointLoc           = GL.GetUniformLocation(_shaderProgram, "joints");

            Console.WriteLine($"[ObjectManager] shader={_shaderProgram} model={_modelLoc} view={_viewLoc} proj={_projLoc}");
        }

        // -----------------------------------------------------------------------
        public GltfModelGpuData LoadModel(string path)
        {
            if (_modelCache.TryGetValue(path, out var cached)) return cached;
            Console.WriteLine($"[ObjectManager] Loading: {path}");
            var data    = GltfLoader.Load(path);
            var gpuData = new GltfModelGpuData(data);
            _modelCache[path] = gpuData;
            return gpuData;
        }

        // -----------------------------------------------------------------------
        public GltfObject AddObject(string modelPath, Vector3 position, float yawDegrees = 0f, float scale = 1f)
        {
            var gpuData = LoadModel(modelPath);
            var q       = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f);
            var obj     = new GltfObject(gpuData, position, q, scale);
            _objects.Add(obj);
            return obj;
        }

        // -----------------------------------------------------------------------
        public void Update(float dt)
        {
            foreach (var obj in _objects) obj.Update(dt);
        }

        // -----------------------------------------------------------------------
        public void Draw(Camera camera, Lights light)
        {
            DrawnObjects  = 0;
            CulledObjects = 0;

            GL.UseProgram(_shaderProgram);

            var view = camera.GetViewMatrix();
            var proj = camera.GetProjectionMatrix();
            GL.UniformMatrix4fv(_viewLoc, 1, false, (float*)Unsafe.AsPointer(ref view));
            GL.UniformMatrix4fv(_projLoc, 1, false, (float*)Unsafe.AsPointer(ref proj)); 

            GL.Uniform3f(_sunDirLoc,     light.SunDir.X,     light.SunDir.Y,     light.SunDir.Z);
            GL.Uniform3f(_lightColorLoc, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
            GL.Uniform3f(_fogColorLoc,   light.FogColor.X,   light.FogColor.Y,   light.FogColor.Z);
            GL.Uniform3f(_viewPosLoc,    camera.Position.X,  camera.Position.Y,  camera.Position.Z);
            GL.Uniform1i(_useFogLoc, Inputs.Keyboard.GetIsFogActive() ? 1 : 0); 

            var frustum = ExtractFrustumPlanes(Matrix4x4.Multiply(view, proj));

            foreach (var obj in _objects)
            {
                if (!DisableFrustumCull && !IsAABBInFrustum(frustum, obj.WorldAABB))
                {
                    CulledObjects++;
                    continue;
                }
                obj.Draw(_modelLoc, _baseColorFactorLoc, _useAlbedoLoc, _albedoMapLoc, _jointLoc);
                DrawnObjects++;
            }
        }

        // -----------------------------------------------------------------------
        public void SnapToTerrain(GltfObject obj, TerrainChunk terrain)
        {

            float terrainY = terrain.GetHeightAt(obj.Position.X, obj.Position.Z);
            obj.Position = new Vector3(obj.Position.X, terrainY, obj.Position.Z);
        }

        public void SnapAllToTerrain(TerrainChunk terrain)
        {
            foreach (var obj in _objects) SnapToTerrain(obj, terrain);
        }

        public List<GltfObject> GetObjects() => _objects;

        public void Dispose()
        {
            foreach (var (_, gpu) in _modelCache) gpu.Dispose();
            _modelCache.Clear();
            _objects.Clear();
        }

        // -----------------------------------------------------------------------
        private static Vector4[] ExtractFrustumPlanes(Matrix4x4 vp)
        {
            var p = new Vector4[6];
            p[0] = new Vector4(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41);
            p[1] = new Vector4(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41);
            p[2] = new Vector4(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42);
            p[3] = new Vector4(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42);
            p[4] = new Vector4(vp.M14 + vp.M13, vp.M24 + vp.M23, vp.M34 + vp.M33, vp.M44 + vp.M43);
            p[5] = new Vector4(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43);
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
                var pv = new Vector3(
                    pl.X >= 0 ? aabb.Max.X : aabb.Min.X,
                    pl.Y >= 0 ? aabb.Max.Y : aabb.Min.Y,
                    pl.Z >= 0 ? aabb.Max.Z : aabb.Min.Z);
                if (pv.X * pl.X + pv.Y * pl.Y + pv.Z * pl.Z + pl.W < 0f)
                    return false;
            }
            return true;
        }
    }
}
