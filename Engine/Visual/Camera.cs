using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Inputs;
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
            Pitch = Math.Clamp(Pitch, -60f, 60f);

            float yawRad = Helpers.OGLMath.ToRadians(Yaw);
            float pitchRad = Helpers.OGLMath.ToRadians(Pitch);

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
            Position.Y = Helpers.OGLMath.Lerp(Position.Y, lastTerrainY, 1f - MathF.Exp(-smooth * dt));
        }

        private float shoulderOffset = 0.6f;     // default kanan
        private float targetShoulderOffset = 0.6f;

        public bool freeLook = false;
        public float savedYaw;                  // simpan yaw player saat ALT ditekan

        public void SetCamera(nint window, Vector3 p, TerrainChunk gameTerrainChunk)
        { 
            // --- CONFIG ---
            
            float heightOffset = 1.8f;      // tinggi kamera dari player
            float minDist = 1.5f;
            float maxDist = PlayerConfig.MaxCameraDistance;
            float collisionPush = 0.35f;     // seberapa jauh kamera dipush saat nabrak
            float smoothFactor = 0.12f;     // smoothing kamera
            float zoomSpeed = 0.5f;

            // SHOULDER SWAP
            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_Q))
            {
                targetShoulderOffset = -MathF.Abs(targetShoulderOffset); // kiri
            }

            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_E))
            {
                targetShoulderOffset = MathF.Abs(targetShoulderOffset);  // kanan
            }

            // SMOOTH SHOULDER TRANSITION
            shoulderOffset = Helpers.OGLMath.Lerp(shoulderOffset, targetShoulderOffset, 0.15f);

            // FREE LOOK (ALT)
            bool altDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT_ALT);

            if (altDown && !freeLook)
            {
                freeLook = true;
                //savedYaw = Yaw;   // simpan yaw player
            }
            else if (!altDown && freeLook)
            {
                freeLook = false;
                //Yaw = savedYaw;   // kembalikan arah player
            }

            // Smooth zoom
            Config.PlayerConfig.CameraDistance = Helpers.OGLMath.Lerp(Config.PlayerConfig.CameraDistance, Config.PlayerConfig.targetCameraDistance, 0.15f);

            // --- OFFSET KAMERA ---
            Vector3 offset = new(shoulderOffset, heightOffset, -Config.PlayerConfig.CameraDistance);

            // --- ROTASI ---
            Matrix4x4 rot = Matrix4x4.CreateFromYawPitchRoll(
                Helpers.OGLMath.ToRadians(Yaw),
                Helpers.OGLMath.ToRadians(Pitch),
                0
            );

            // --- POSISI IDEAL ---
            Vector3 camOffset = Vector3.TransformNormal(offset, rot);
            Vector3 idealPos = p + camOffset;

            // Simpan arah & jarak ideal
            Vector3 idealDir = Vector3.Normalize(idealPos - p);
            float idealDist = Vector3.Distance(p, idealPos);

            // --- COLLISION ---
            
            float minHeight = 0.1f;

            Vector3 finalPos = idealPos;
            float terrainY = gameTerrainChunk.GetHeightAt(idealPos.X, idealPos.Z);

            if (idealPos.Y < terrainY + minHeight)
            {
                // Push-in halus
                float newDist = idealDist - collisionPush;
                newDist = MathF.Max(minDist, newDist);

                finalPos = p + idealDir * newDist;

                // Angkat sedikit
                finalPos.Y = terrainY + minHeight;
            }

            // --- SMOOTH CAMERA POSITION ---
            Position = Vector3.Lerp(Position, finalPos, smoothFactor);

            // --- LOOK AT PLAYER ---
            Front = Vector3.Normalize(p - Position);
            Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            Up = Vector3.Normalize(Vector3.Cross(Right, Front));

        }

    }
}
