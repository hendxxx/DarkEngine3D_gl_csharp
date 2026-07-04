using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
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
        private float _fov;
        public float FoV
        {
            get => _fov;
            set
            {
                if (Math.Abs(_fov - value) > 0.0001f)
                {
                    _fov = value;
                    _projectionDirty = true;
                }
            }
        }

        private float _baseFov = 60f;
        public float BaseFoV
        {
            get => _baseFov;
            set
            {
                if (Math.Abs(_baseFov - value) > 0.0001f)
                {
                    _baseFov = value;
                    _projectionDirty = true;
                }
            }
        }
        public float NearDist;
        public float FarDist;
        private float _aspect;

        private Matrix4x4 _projection;
        private bool _projectionDirty = true;

        // Terrain clamp
        private float lastTerrainY = 0f;

        // Camera mode — property dibacking oleh _cameraMode agar HUD dan logic selalu sinkron
        private CameraMode _cameraMode = CameraMode.Orbit;
        public CameraMode CurrentMode
        {
            get => _cameraMode;
            set => _cameraMode = value;
        }
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
        public float zoomSpeed = CameraConfig.ZoomSpeed;

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

        public bool IsADS = false;
        
        public bool FlyMode = false;
        public float FlySpeed = Config.CameraConfig.CameraFlySpeed; // sesuai permintaan

        public bool IsFlyMode=false;
         

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
                CameraConfig.TargetCameraDistance = preset.DefaultDistance;
                CameraConfig.CameraMinDistance = preset.MinDistance;
                CameraConfig.MaxCameraDistance = preset.MaxDistance;
                targetShoulderOffset = preset.ShoulderOffset;
                CameraConfig.CameraOffsetHeight = preset.HeightOffset;
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
        float nearDist = _cameraMode == CameraMode.FirstPerson ? 0.1f : NearDist;

        // Convert FoV (degrees) → radians
        float fovRad = Helpers.OGLMath.ToRadians(FoV);

        _projection = Matrix4x4.CreatePerspectiveFieldOfView(
            fovRad,
            _aspect,
            nearDist,
            FarDist
        );

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
        private void SetCameraFlyMode(nint window, float dt)
        {
            // Mouse look (FPS style)
            smoothYaw += Mouse.DeltaX * 0.1f;
            smoothPitch -= Mouse.DeltaY * 0.1f;

            smoothPitch = Math.Clamp(smoothPitch, -89f, 89f);

            Yaw = smoothYaw;
            Pitch = smoothPitch;

            // Update camera vectors (FPS)
            Front.X = MathF.Cos(Helpers.OGLMath.ToRadians(Yaw)) * MathF.Cos(Helpers.OGLMath.ToRadians(Pitch));
            Front.Y = MathF.Sin(Helpers.OGLMath.ToRadians(Pitch));
            Front.Z = MathF.Sin(Helpers.OGLMath.ToRadians(Yaw)) * MathF.Cos(Helpers.OGLMath.ToRadians(Pitch));
            Front = Vector3.Normalize(Front);

            Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            Up = Vector3.Normalize(Vector3.Cross(Right, Front));

            // Movement
            Vector3 move = Vector3.Zero;

            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_W))
                move += Front;
            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_S))
                move -= Front;
            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_A))
                move -= Right;
            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_D))
                move += Right;
            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE))
                move += Vector3.UnitY;
            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT_CONTROL))
                move -= Vector3.UnitY;

            if (move.LengthSquared() > 0)
                move = Vector3.Normalize(move);

            Position += move * FlySpeed * dt;

            // No gravity, no terrain clamp, no collision
        }


        /// <summary>Push camera position + sync smoothing agar tidak jitter.</summary>
        public void PushPosition(Vector3 newPos)
        {
            Position = newPos;
            smoothCamPos = newPos;
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

        public void SetCamera(nint window, Vector3 position, TerrainChunk gameTerrainChunk, float dt, List<StaticObjectManager>? staticManagers = null)
        {
            // Toggle FlyMode
            if (Keyboard.IsKeyPressed(window, Const.GLFW_KEY_G))
                FlyMode = !FlyMode;
            
            IsFlyMode = FlyMode; 

            if (FlyMode)
            {
                IsFlyMode = true; 
                SetCameraFlyMode(window, dt);
                return;
            } 

            if (_cameraMode == CameraMode.FirstPerson)
                SetCameraFirstPerson(window, position, gameTerrainChunk, dt);
            else
                SetCameraThirdPerson(window, position, gameTerrainChunk, dt, staticManagers);
        }

        private void SetCameraThirdPerson(nint window, Vector3 position, TerrainChunk gameTerrainChunk, float dt, List<StaticObjectManager>? staticManagers = null)
        {
            var preset = CurrentPreset;
            if (preset == null) return;

            float camScale = 1.0f;
            if (ScaleConfig.ScaleCamera)
            {
                camScale = ScaleHelpers.Normalize(
                    1.0f, // kalau mau nanti diganti dengan scale player dari luar
                    ScaleConfig.CameraBaseScale,
                    ScaleConfig.CameraMinMul,
                    ScaleConfig.CameraMaxMul
                );
            }

            // LIMIT PITCH KHUSUS OTS (agar player tetap on-cam)
            if (preset.TrueOTS)
            {
                float minPitchOTS = -20f;
                float maxPitchOTS = 10f;
                Pitch = Math.Clamp(Pitch, minPitchOTS, maxPitchOTS);
            }

            float heightOffset = preset.HeightOffset * camScale;
            float minDist = preset.MinDistance * camScale;
            float collisionPush = 0.35f;

            // Pivot is at the character's upper body / head
            Vector3 pivotPos = position + new Vector3(0, heightOffset, 0);

            // AIM MODE (ADS)
            if (Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_RIGHT))
                IsADS = true;
            else
                IsADS = false;

            float targetFov = IsADS ? 45.0f : BaseFoV;
            FoV = Helpers.OGLMath.Lerp(FoV, targetFov, 1f - MathF.Exp(-8f * dt));
            _projectionDirty = true;

            // SHOULDER SWAP
            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_Q))
                targetShoulderOffset = -MathF.Abs(targetShoulderOffset);

            if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_E))
                targetShoulderOffset = MathF.Abs(targetShoulderOffset);

            shoulderOffset = Helpers.OGLMath.Lerp(
                shoulderOffset,
                targetShoulderOffset * camScale,
                1f - MathF.Exp(-CameraConfig.CameraFollowSpeed * dt)
            );

            float shoulder = shoulderOffset;
            float camDist = CameraConfig.CameraDistance * camScale;

            // ZOOM
            CameraConfig.CameraDistance =
                Helpers.OGLMath.Lerp(CameraConfig.CameraDistance,
                                     CameraConfig.TargetCameraDistance,
                                     1f - MathF.Exp(-CameraConfig.CameraFollowSpeed * dt));


            if (IsADS)
            {
                shoulder = Helpers.OGLMath.Lerp(shoulder, 0.25f * camScale, 1f - MathF.Exp(-10f * dt));
                camDist = Helpers.OGLMath.Lerp(camDist, 1.2f * camScale, 1f - MathF.Exp(-10f * dt));
            }
            else
            {
                shoulder = Helpers.OGLMath.Lerp(shoulder, targetShoulderOffset, 1f - MathF.Exp(-6f * dt));
                camDist = Helpers.OGLMath.Lerp(camDist, CameraConfig.TargetCameraDistance, 1f - MathF.Exp(-6f * dt));
            }

            Vector3 offset = new Vector3(shoulder, 0, -camDist);

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
            smoothYaw = Helpers.OGLMath.LerpAngle(smoothYaw, Yaw, 1f - MathF.Exp(-rotSmooth * dt));
            smoothPitch = Helpers.OGLMath.Lerp(smoothPitch, Pitch, 1f - MathF.Exp(-rotSmooth * dt));

            // In TrueOTS mode the camera looks forward parallel to pitch.
            // In orbit/look-at modes the camera position orbits around the pivot.
            // Whether to negate pitch is controlled per-preset via InvertOrbitPitch:
            //   - Orbit/Follow/Chase: true  (negate, so mouse-up = camera rises above player)
            //   - Tactical:          false (no negate, pitch is already forced positive/top-down)
            float orbitPitch = (!preset.TrueOTS && preset.InvertOrbitPitch) ? -smoothPitch : smoothPitch;

            Matrix4x4 rot = Matrix4x4.CreateFromYawPitchRoll(
                Helpers.OGLMath.ToRadians(smoothYaw),
                Helpers.OGLMath.ToRadians(orbitPitch),
                0
            );

            // SCROLL ZOOM
            if (preset.AllowZoom && Mouse.ScrollY != 0)
            {
                CameraConfig.TargetCameraDistance -= Mouse.ScrollY * (CameraConfig.ZoomSpeed * camScale);
                CameraConfig.TargetCameraDistance = Math.Clamp(
                    CameraConfig.TargetCameraDistance,
                    preset.MinDistance * camScale,
                    preset.MaxDistance * camScale
                );

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

            // ── Wall collision: slide finalPos ke arah pivot (player) SEBELUM smoothing ──
            // Pakai SlideCameraToPivot bukan PushCamera — jadi camera maju ke player saat
            // kena wall, bukan didorong ke samping/tembus ke belakang tembok.
            finalPos = CollisionHelper.SlideCameraToPivot(finalPos, pivotPos, minDist, staticManagers);

            // Smooth camera movement
            float lag = 6f;
            smoothCamPos = Vector3.Lerp(smoothCamPos, finalPos, 1f - MathF.Exp(-lag * dt));
            Position = smoothCamPos;
            if (preset.TrueOTS)
            {
                float minCamHeight = pivotPos.Y - 0.2f; // sedikit di bawah bahu
                float maxCamHeight = pivotPos.Y + 1.2f; // sedikit di atas kepala

                Position.Y = Math.Clamp(Position.Y, minCamHeight, maxCamHeight);
            }

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
            float camScale = 1.0f;
            if (ScaleConfig.ScaleCamera)
            {
                camScale = ScaleHelpers.Normalize(
                    1.0f,
                    ScaleConfig.CameraBaseScale,
                    ScaleConfig.CameraMinMul,
                    ScaleConfig.CameraMaxMul
                );
            }

            // First person: camera at head height above player center
            Vector3 headPos = position + new Vector3(0, FirstPersonHeadHeight * camScale, 0);

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
                headPos += Vector3.Normalize(flatFront) * CameraConfig.FirstPersonCameraForwardOffset * camScale;

            // Smooth camera position
            //float lag = 3f;
            //smoothCamPos = Vector3.Lerp(smoothCamPos, headPos, 1f - MathF.Exp(-lag * dt));
            Position = headPos;

            // Update vectors from mouse yaw/pitch (UpdateVectors already does this)
            UpdateVectors();
        }
    }
}
