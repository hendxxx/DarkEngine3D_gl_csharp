using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    public sealed class Camera
    {
        public float GetAspect() => _aspect;
        // Transform
        public Vector3 Position = new(0, 0, 0);
        public Vector3 Front = new(0, 0, -1);
        public Vector3 Up = Vector3.UnitY;
        public Vector3 Right = Vector3.UnitX;

        // Euler
        public float Yaw = 0.0f;
        public float Pitch = 0.0f;

        // Projection params
        public float FoV;
        public float NearDist;
        public float FarDist;
        private float _aspect;

        private Matrix4x4 _projection;
        private bool _projectionDirty = true;

        // Terrain clamp
        private float lastTerrainY = 0f;

        // Shoulder swap
        private float shoulderOffset = Config.PlayerConfig.ShoulderOffset;
        private float targetShoulderOffset = Config.PlayerConfig.TargetShoulderOffset;

        // Free look
        public bool freeLook = false;

        // Cinematic smoothing
        private Vector3 smoothCamPos;
        private float smoothYaw;
        private float smoothPitch;
        public float savedYaw;
        public float zoomSpeed = Config.PlayerConfig.ZoomSpeed;

        // Camera sway
        private float swayTimer = 0f;

        public Camera(float x, float y, float z, float yaw, float pitch, float aspect, float fov, float nearDist, float farDist)
        {
            _aspect = aspect;
            FoV = fov;
            NearDist = nearDist;
            FarDist = farDist;

            Init(x, y, z, yaw, pitch);

            smoothCamPos = Position;
            smoothYaw = Yaw;
            smoothPitch = Pitch;
        }

        public void Init(float x, float y, float z, float yaw, float pitch)
        {
            Position = new(x, y, z);
            Yaw = yaw;
            Pitch = pitch;

            UpdateVectors();
            _projectionDirty = true;
        }
        public void UpdateAspectRatio(float newWidth, float newHeight)
        {
            if (newHeight <= 0) newHeight = 1;
            _aspect = newWidth / newHeight;
            _projectionDirty = true;
        }

        public void UpdateVectors()
        {
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

        public Matrix4x4 GetViewMatrix()
        {
            return Matrix4x4.CreateLookAt(Position, Position + Front, Up);
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

        public void ClampToTerrain(MapLoader mapLoader, float dt)
        {
            float minHeight = 1.0f;

            float terrainY = mapLoader.GetHeightInterpolated(Position.X, Position.Z);
            float targetY = terrainY + minHeight;

            lastTerrainY = lastTerrainY * 0.9f + targetY * 0.1f;

            float smooth = 12f;
            Position.Y = Helpers.OGLMath.Lerp(Position.Y, lastTerrainY, 1f - MathF.Exp(-smooth * dt));
        }

        public void SetCamera(nint window, Vector3 p, TerrainChunk gameTerrainChunk, float dt)
        {
            float heightOffset = Config.PlayerConfig.CameraOffsetHeight;
            float minDist = Config.PlayerConfig.CameraMinDistance;
            float collisionPush = 0.35f;

            // SHOULDER SWAP
            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_Q))
                targetShoulderOffset = -MathF.Abs(targetShoulderOffset);

            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_E))
                targetShoulderOffset = MathF.Abs(targetShoulderOffset);

            shoulderOffset = Helpers.OGLMath.Lerp(shoulderOffset, targetShoulderOffset, Config.PlayerConfig.CameraFollowSpeed);

            // FREE LOOK
            freeLook = Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT_ALT);

            // ZOOM
            Config.PlayerConfig.CameraDistance =
                Helpers.OGLMath.Lerp(Config.PlayerConfig.CameraDistance,
                                     Config.PlayerConfig.TargetCameraDistance,
                                     Config.PlayerConfig.CameraFollowSpeed);

            // OFFSET
            Vector3 offset = new(shoulderOffset, heightOffset, -Config.PlayerConfig.CameraDistance);

            // CAMERA SWAY
            bool isMoving =
                Keyboard.IsKeyDown(window, Const.GLFW_KEY_W) ||
                Keyboard.IsKeyDown(window, Const.GLFW_KEY_A) ||
                Keyboard.IsKeyDown(window, Const.GLFW_KEY_S) ||
                Keyboard.IsKeyDown(window, Const.GLFW_KEY_D);

            if (isMoving)
            {
                swayTimer += dt * 6f;
                float sway = MathF.Sin(swayTimer) * 0.05f;
                offset.X += sway;
            }
            else
            {
                swayTimer = 0f;
            }

            // CINEMATIC ROTATION
            float rotSmooth = 10f;
            smoothYaw = Helpers.OGLMath.LerpAngle(smoothYaw, Yaw, rotSmooth * dt);
            smoothPitch = Helpers.OGLMath.Lerp(smoothPitch, Pitch, rotSmooth * dt);

            Matrix4x4 rot = Matrix4x4.CreateFromYawPitchRoll(
                Helpers.OGLMath.ToRadians(smoothYaw),
                Helpers.OGLMath.ToRadians(smoothPitch),
                0
            );

            // SCROLL ZOOM
            if (Mouse.ScrollY != 0)
            {
                PlayerConfig.TargetCameraDistance -= Mouse.ScrollY * Config.PlayerConfig.ZoomSpeed; // speed zoom
                PlayerConfig.TargetCameraDistance = Math.Clamp(PlayerConfig.TargetCameraDistance, Config.PlayerConfig.CameraMinDistance, Config.PlayerConfig.MaxCameraDistance);

                Mouse.ResetScroll();
            }


            Vector3 camOffset = Vector3.TransformNormal(offset, rot);
            Vector3 idealPos = p + camOffset;

            Vector3 idealDir = Vector3.Normalize(idealPos - p);
            float idealDist = Vector3.Distance(p, idealPos);

            float terrainY = gameTerrainChunk.GetHeightAt(idealPos.X, idealPos.Z);
            float minHeight = 0.1f;

            Vector3 finalPos = idealPos;

            if (idealPos.Y < terrainY + minHeight)
            {
                float newDist = idealDist - collisionPush;
                newDist = MathF.Max(minDist, newDist);

                finalPos = p + idealDir * newDist;
                finalPos.Y = terrainY + minHeight;
            }

            // CINEMATIC CAMERA LAG
            float lag = 6f;
            smoothCamPos = Vector3.Lerp(smoothCamPos, finalPos, 1f - MathF.Exp(-lag * dt));
            Position = smoothCamPos;

            // LOOK AT PLAYER
            Front = Vector3.Normalize(p - Position);
            Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            Up = Vector3.Normalize(Vector3.Cross(Right, Front));
        }
    }
}
