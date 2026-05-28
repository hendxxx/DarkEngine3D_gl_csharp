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
        public float Yaw = -90.0f; // menghadap -Z
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

        public Camera(float x, float y, float z, float aspect, float fov, float nearDist, float farDist)
        {
            _aspect = aspect;
            FoV = fov;
            NearDist = nearDist;
            FarDist = farDist;

            Init(x, y, z);
        }

        public void Init(float x, float y, float z)
        {
            Position = new(x, y, z);

            Yaw = -90.0f;
            Pitch = 0.0f;

            UpdateVectors();
            _projectionDirty = true;
        }

        public void UpdateVectors()
        {
            float yawRad = Helpers.TerrainsHelpers.OGLMath.ToRadians(Yaw);
            float pitchRad = Helpers.TerrainsHelpers.OGLMath.ToRadians(Pitch);

            Vector3 front;
            front.X = MathF.Cos(yawRad) * MathF.Cos(pitchRad);
            front.Y = MathF.Sin(pitchRad);
            front.Z = MathF.Sin(yawRad) * MathF.Cos(pitchRad);
            Front = Vector3.Normalize(front);

            // Right & Up yang stabil
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

        public void ClampToTerrain(MapLoader mapLoader, float deltaTime)
        {
            float minHeight = 1.0f;
            float gravity = 25.0f;   // lebih besar → lebih stabil
            float damping = 6.0f;    // untuk menghilangkan jitter

            float terrainHeight = mapLoader.GetHeightInterpolated(Position.X, Position.Z);
            float targetY = terrainHeight + minHeight;

            float diff = Position.Y - targetY;

            // Jika kamera terlalu tinggi → jatuhkan
            if (diff > 0.01f)
            {
                _currentVelocityY -= gravity * deltaTime;
                Position.Y += _currentVelocityY * deltaTime;
            }
            // Jika kamera terlalu rendah → snap ke target
            else if (diff < -0.01f)
            {
                Position.Y = targetY;
                _currentVelocityY = 0f;
            }
            else
            {
                // Dalam dead-zone → stabilkan
                Position.Y = targetY;
                _currentVelocityY *= (1f - damping * deltaTime);
                if (MathF.Abs(_currentVelocityY) < 0.01f)
                    _currentVelocityY = 0f;
            }
        }

    }
}
