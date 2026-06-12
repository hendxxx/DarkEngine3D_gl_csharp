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

        // Camera mode
        private CameraMode _cameraMode = CameraMode.OTS;
        public CameraMode CurrentMode => _cameraMode;
        public CameraPreset CurrentPreset => CameraConfig.Presets.TryGetValue(_cameraMode, out var p) ? p : null;

        // Shoulder swap
        private float shoulderOffset = 0.6f;
        private float targetShoulderOffset = 0.6f;

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

        // Collision parameters
        private const float CameraCollisionRadius = 0.5f;
        private const float CollisionResponseSpeed = 8.0f;
        private const float CollisionDistance = 0.3f;

        // First person head bobbing
        private float headBobTimer = 0f;
        private const float HeadBobFrequency = 5.0f;
        private const float HeadBobAmount = 0.05f;
        private const float FirstPersonHeadHeight = 1.7f;

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

            ApplyPreset();
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
            float minPitch = CurrentPreset?.MinPitch ?? -85f;
            float maxPitch = CurrentPreset?.MaxPitch ?? 85f;

            // Clamp Pitch to prevent gimbal lock, allowing almost 180 degrees up/down
            Pitch = Math.Clamp(Pitch, minPitch, maxPitch);

            float yawRad = Helpers.OGLMath.ToRadians(Yaw);
            float pitchRad = Helpers.OGLMath.ToRadians(Pitch);

            Vector3 front;
            front.X = MathF.Sin(yawRad) * MathF.Cos(pitchRad);
            front.Y = MathF.Sin(pitchRad);
            front.Z = MathF.Cos(yawRad) * MathF.Cos(pitchRad);

            Front = Vector3.Normalize(front);
            Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            Up = Vector3.Normalize(Vector3.Cross(Right, Front));
        }


        public void ClampFirstPersonHeadYaw(float bodyYaw)
        {
            if (_cameraMode != CameraMode.FirstPerson) return;

            // Hitung selisih yaw antara badan dan kamera
            float yawOffset = GetAngleDelta(bodyYaw, Yaw);

            // Batasi rotasi kepala ±85 derajat
            yawOffset = Math.Clamp(yawOffset, -85f, 85f);

            // Terapkan kembali ke kamera
            Yaw = bodyYaw + yawOffset;

            UpdateVectors();
        }

        private static float GetAngleDelta(float from, float to)
        {
            return ((to - from + 540f) % 360f) - 180f;
        }

        public void ToggleCameraMode(int direction = 1)
        {
            var modes = (CameraMode[])Enum.GetValues(typeof(CameraMode));
            int nextIndex = (((int)_cameraMode + direction) % modes.Length + modes.Length) % modes.Length;
            _cameraMode = modes[nextIndex];
            _projectionDirty = true;
            ApplyPreset();
            Console.WriteLine($"Camera Mode: {_cameraMode}");
        }

        public void ApplyPreset()
        {
            var preset = CurrentPreset;
            if (preset != null)
            {
                Config.PlayerConfig.TargetCameraDistance = preset.DefaultDistance;
                Config.PlayerConfig.CameraMinDistance = preset.MinDistance;
                Config.PlayerConfig.MaxCameraDistance = preset.MaxDistance;
                targetShoulderOffset = preset.ShoulderOffset;
                Config.PlayerConfig.CameraOffsetHeight = preset.HeightOffset;
            }
        }

        public Matrix4x4 GetViewMatrix()
        {
            return Matrix4x4.CreateLookAt(Position, Position + Front, Up);
        }

        public Matrix4x4 GetProjectionMatrix()
        {
            if (_projectionDirty)
            {
                // Use smaller near plane in 1st person to avoid body clipping
                float nearDist = _cameraMode == CameraMode.FirstPerson ? 0.1f : NearDist;
                _projection = Matrix4x4.CreatePerspectiveFieldOfView(FoV, _aspect, nearDist, FarDist);
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

        public void SetCamera(nint window, Vector3 position, TerrainChunk gameTerrainChunk, float dt)
        {
            if (_cameraMode == CameraMode.FirstPerson)
                SetCameraFirstPerson(window, position, gameTerrainChunk, dt);
            else
                SetCameraThirdPerson(window, position, gameTerrainChunk, dt);
        }

        private void SetCameraThirdPerson(nint window, Vector3 position, TerrainChunk gameTerrainChunk, float dt)
        {
            var preset = CurrentPreset;
            if (preset == null) return;

            float heightOffset = preset.HeightOffset;
            float minDist = preset.MinDistance;
            float collisionPush = 0.35f;

            // Pivot is at the character's upper body / head
            Vector3 pivotPos = position + new Vector3(0, heightOffset, 0);

            // SHOULDER SWAP
            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_Q))
                targetShoulderOffset = -MathF.Abs(targetShoulderOffset);

            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_E))
                targetShoulderOffset = MathF.Abs(targetShoulderOffset);

            shoulderOffset = Helpers.OGLMath.Lerp(shoulderOffset, targetShoulderOffset, Config.PlayerConfig.CameraFollowSpeed);

            // ZOOM
            Config.PlayerConfig.CameraDistance =
                Helpers.OGLMath.Lerp(Config.PlayerConfig.CameraDistance,
                                     Config.PlayerConfig.TargetCameraDistance,
                                     Config.PlayerConfig.CameraFollowSpeed);

            // OFFSET (relative to pivot)
            Vector3 offset = new(shoulderOffset, 0, -Config.PlayerConfig.CameraDistance);

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
            if (preset.AllowZoom && Mouse.ScrollY != 0)
            {
                PlayerConfig.TargetCameraDistance -= Mouse.ScrollY * Config.PlayerConfig.ZoomSpeed;
                PlayerConfig.TargetCameraDistance = Math.Clamp(PlayerConfig.TargetCameraDistance, preset.MinDistance, preset.MaxDistance);

                Mouse.ResetScroll();
            }

            Vector3 camOffset = Vector3.TransformNormal(offset, rot);
            Vector3 idealPos = pivotPos + camOffset;

            Vector3 idealDir = Vector3.Normalize(idealPos - pivotPos);
            float idealDist = Vector3.Distance(pivotPos, idealPos);

            float terrainY = gameTerrainChunk.GetHeightAt(idealPos.X, idealPos.Z);
            float minHeight = 0.1f;

            Vector3 finalPos = idealPos;

            // Collision detection: raycast from pivot to ideal position
            if (idealPos.Y < terrainY + minHeight)
            {
                if (idealDir.Y < -0.05f) // Camera is below the pivot
                {
                    // Calculate distance along idealDir where it intersects the terrain plane
                    float t = (terrainY + minHeight - pivotPos.Y) / idealDir.Y;
                    if (t < 0) t = minDist; // If terrain is above pivot, zoom fully in

                    float newDist = MathF.Max(minDist, t - collisionPush);
                    newDist = MathF.Min(newDist, idealDist);

                    finalPos = pivotPos + idealDir * newDist;
                    
                    // Fallback safety
                    if (finalPos.Y < terrainY + minHeight)
                        finalPos.Y = terrainY + minHeight;
                }
                else
                {
                    // Camera is above the pivot but hitting a slope/cliff
                    float newDist = idealDist - collisionPush;
                    newDist = MathF.Max(minDist, newDist);

                    finalPos = pivotPos + idealDir * newDist;
                    finalPos.Y = terrainY + minHeight;
                }
            }

            // Smooth camera movement
            float lag = 6f;
            smoothCamPos = Vector3.Lerp(smoothCamPos, finalPos, 1f - MathF.Exp(-lag * dt));
            Position = smoothCamPos;

            // TRUE OTS LOOK PARALLEL TO ROTATION OR LOOK AT PIVOT
            if (preset.TrueOTS)
            {
                Front.X = MathF.Sin(Helpers.OGLMath.ToRadians(smoothYaw)) * MathF.Cos(Helpers.OGLMath.ToRadians(smoothPitch));
                Front.Y = MathF.Sin(Helpers.OGLMath.ToRadians(smoothPitch));
                Front.Z = MathF.Cos(Helpers.OGLMath.ToRadians(smoothYaw)) * MathF.Cos(Helpers.OGLMath.ToRadians(smoothPitch));
                Front = Vector3.Normalize(Front);
            }
            else
            {
                Front = Vector3.Normalize(pivotPos - Position);
            }
            Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            Up = Vector3.Normalize(Vector3.Cross(Right, Front));
        }
         
        private void SetCameraFirstPerson(nint window, Vector3 position, TerrainChunk gameTerrainChunk, float dt)
        {
            // First person: camera at head height above player center
            Vector3 headPos = position + new Vector3(0, FirstPersonHeadHeight, 0);

            // Head bobbing when moving
            bool isMoving =
                Keyboard.IsKeyDown(window, Const.GLFW_KEY_W) ||
                Keyboard.IsKeyDown(window, Const.GLFW_KEY_A) ||
                Keyboard.IsKeyDown(window, Const.GLFW_KEY_S) ||
                Keyboard.IsKeyDown(window, Const.GLFW_KEY_D);

            if (isMoving)
            {
                headBobTimer += dt * HeadBobFrequency;
                float bobY = MathF.Sin(headBobTimer) * HeadBobAmount;
                headPos.Y += bobY;
            }
            else
            {
                headBobTimer = 0f;
            }

            Vector3 flatFront = new(Front.X, 0, Front.Z);
            if (flatFront.LengthSquared() > 0.0001f)
                headPos += Vector3.Normalize(flatFront) * Config.PlayerConfig.FirstPersonCameraForwardOffset;

            // Smooth camera position
            //float lag = 3f;
            //smoothCamPos = Vector3.Lerp(smoothCamPos, headPos, 1f - MathF.Exp(-lag * dt));
            Position = headPos;

            // Update vectors from mouse yaw/pitch (UpdateVectors already does this)
            UpdateVectors();
        }
    }
}
