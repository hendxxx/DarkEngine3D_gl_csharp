using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    public sealed class Camera
    {
        // Transform
        public Vector3 Position = new(0, 0, 0);
        public Vector3 Front = new(0, 0, -1);
        public Vector3 Up = Vector3.UnitY;
        public Vector3 Right = Vector3.UnitX;

        // Euler (bisa nanti diganti quaternion kalau mau)
        public float Yaw = 0.0f; 
        public float Pitch = 0.0f;

        // Projection params
        public float FoV;
        public float NearDist;
        public float FarDist;
        private float _aspect;

        // Cached projection
        private Matrix4x4 _projection;
        private bool _projectionDirty = true;

        // Terrain clamp
        private float _currentVelocityY = 0f;

        public Camera(float x, float y, float z, float yaw, float pitch, float aspect, float fov, float nearDist, float farDist)
        {
            _aspect = aspect;
            FoV = fov;
            NearDist = nearDist;
            FarDist = farDist;
            Yaw = yaw;
            Pitch = pitch;

            Init(x, y, z, yaw, pitch);
        }

        public void Init(float x, float y, float z, float yaw, float pitch)
        {
            Position = new(x, y, z);

            Yaw = yaw;
            Pitch = pitch;

            UpdateVectors();
            _projectionDirty = true;
        }
        public void Follow(Vector3 targetPos, Vector3 offset) 
        {
            Position = targetPos + offset;

            // Kamera selalu melihat ke player
            Front = Vector3.Normalize(targetPos - Position);

            // Hitung Right & Up
            Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            Up = Vector3.Normalize(Vector3.Cross(Right, Front));
        }
        public void UpdateVectors()
        {
            // 1. Clamp pitch dulu
            Pitch = Math.Clamp(Pitch, -30f, 30f);

            float yawRad = Helpers.TerrainsHelpers.OGLMath.ToRadians(Yaw);
            float pitchRad = Helpers.TerrainsHelpers.OGLMath.ToRadians(Pitch);

            Vector3 front;
            front.X = MathF.Cos(yawRad) * MathF.Cos(pitchRad);
            front.Y = MathF.Sin(pitchRad);
            front.Z = MathF.Sin(yawRad) * MathF.Cos(pitchRad);

            Front = Vector3.Normalize(front);
            Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            Up = Vector3.Normalize(Vector3.Cross(Right, Front));

        }


        public float GetAspect() => _aspect;

        public Matrix4x4 GetViewMatrix()
        {
            // Pastikan UpdateVectors() sudah dipanggil sebelum render frame ini
            return Matrix4x4.CreateLookAt(Position, Position + Front, Up);
        }

        public void UpdateAspectRatio(float newWidth, float newHeight)
        {
            if (newHeight <= 0) newHeight = 1;
            _aspect = newWidth / newHeight;
            _projectionDirty = true;
        }

        public Matrix4x4 GetProjectionMatrix()
        {
            if (_projectionDirty)
            {
                _projection = Matrix4x4.CreatePerspectiveFieldOfView(FoV, _aspect, NearDist, FarDist);
                _projectionDirty = false;
            }
            return _projection;
        }

        public void SetViewAndProjection(int viewLocation, int projectionLocation)
        {
            GL.UseProgram(Shader.GetShaderProgram());
            Matrix4x4 view = GetViewMatrix();
            Matrix4x4 projection = GetProjectionMatrix();

            unsafe
            {
                GL.UniformMatrix4fv(viewLocation, 1, false, (float*)&view);
                GL.UniformMatrix4fv(projectionLocation, 1, false, (float*)&projection);
            }
        }
        private float lastTerrainY = 0f;
        public void ClampToTerrain(MapLoader mapLoader, float dt)
        {
            float minHeight = 1.0f;

            // 1. Ambil terrain height
            float terrainY = mapLoader.GetHeightInterpolated(Position.X, Position.Z);
            float targetY = terrainY + minHeight;

            // 2. Smooth terrain noise (hilangkan jitter)
            // simpan lastTerrainY sebagai field di Camera
            lastTerrainY = lastTerrainY * 0.9f + targetY * 0.1f;

            // 3. Smooth camera Y (tidak snap)
            float smooth = 12f; // semakin besar semakin cepat
            Position.Y = Helpers.ShaderHelpers.Lerp(Position.Y, lastTerrainY, 1f - MathF.Exp(-smooth * dt));
        }



    }
}
