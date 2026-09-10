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
        public CameraPreset? CurrentPreset => CameraConfig.Presets.TryGetValue(_cameraMode, out var p) ? p : null;

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

        // First person head bobbing
        private float headBobTimer = 0f;
        private const float HeadBobFrequency = 5.0f;
        private const float HeadBobAmount = 0.05f;
        private const float FirstPersonHeadHeight = 1.7f;

        public bool IsADS = false;
        
        public bool FlyMode = false;
        /// <summary>Live-reads from Config.CameraConfig.CameraFlySpeed so Inspector slider changes take effect immediately.</summary>
        public float FlySpeed => Config.CameraConfig.CameraFlySpeed;

        public bool IsFlyMode=false;

        /// <summary>When true, fly-mode mouse look stays active without holding CTRL.
        /// Toggled by the "Fly" button in the viewport toolbar.</summary>
        public bool FlyMouseLook = false;

        /// <summary>When true, fly-mode WASD movement (and scroll dolly) is suppressed so
        /// external logic owns the camera position — e.g. 2D level mode where the camera
        /// is panned/zoomed with right-drag/scroll and follows the Player2D during
        /// preview/in-game. Rotation (✈ fly look) and RMB pan stay unaffected.</summary>
        public bool LockTranslation = false;

        /// <summary>Editor viewport camera presets (perspective + orthographic-style side views).
        /// Applied by the "Views" button in the viewport toolbar.</summary>
        public enum EditorViewPreset
        {
            Perspective,
            Top,
            Bottom,
            Front,
            Back,
            Left,
            Right,
        }

        /// <summary>Snap the editor fly-camera to a preset view (position + orientation).
        /// Also syncs the internal smoothing targets so fly-mode doesn't snap back next frame.
        /// Top/Bottom look straight down/up; Front/Back/Left/Right look at the world origin.
        /// NOTE: pitch ±89 is only valid in fly mode (UpdateCameraVectorsFly doesn't clamp);
        /// non-fly paths clamp to the preset pitch limits (~±85). The editor camera is always fly.</summary>
        public void SetEditorViewPreset(EditorViewPreset preset)
        {
            Vector3 pos;
            float yaw, pitch;
            switch (preset)
            {
                case EditorViewPreset.Perspective:
                    pos = new Vector3(0f, 10f, 15f); yaw = 180f; pitch = -33.7f; break;
                case EditorViewPreset.Top:
                    pos = new Vector3(0f, 40f, 0f); yaw = 180f; pitch = -89f; break;
                case EditorViewPreset.Bottom:
                    pos = new Vector3(0f, -40f, 0f); yaw = 180f; pitch = 89f; break;
                case EditorViewPreset.Front:
                    pos = new Vector3(0f, 5f, 40f); yaw = 180f; pitch = 0f; break;
                case EditorViewPreset.Back:
                    pos = new Vector3(0f, 5f, -40f); yaw = 0f; pitch = 0f; break;
                case EditorViewPreset.Left:
                    pos = new Vector3(40f, 5f, 0f); yaw = -90f; pitch = 0f; break;
                case EditorViewPreset.Right:
                    pos = new Vector3(-40f, 5f, 0f); yaw = 90f; pitch = 0f; break;
                default:
                    return;
            }
            Position = pos;
            Yaw = yaw;
            Pitch = pitch;
            smoothYaw = yaw;
            smoothPitch = pitch;
            smoothCamPos = pos;
            UpdateCameraVectorsFly();
        }

        /// <summary>Teleport the editor fly-camera to an explicit position + yaw/pitch
        /// (degrees), syncing the internal smoothing targets so fly-mode doesn't snap back
        /// next frame. Used by the Inspector's "Preview from Camera" button to place the
        /// editor camera exactly at a placed camera marker (position + rotation + FOV).
        /// Also sets the FOV to match the marker's camera.</summary>
        public void SetEditorViewTransform(Vector3 pos, float yawDeg, float pitchDeg, float fovDeg)
        {
            Position = pos;
            Yaw = yawDeg;
            Pitch = pitchDeg;
            smoothYaw = yawDeg;
            smoothPitch = pitchDeg;
            smoothCamPos = pos;
            if (fovDeg > 0f)
                FoV = fovDeg; // setter already marks the projection dirty
            UpdateCameraVectorsFly();
        }

        // ── Mouse look toggle: only active when CTRL is held ──
        private bool _mouseLookWasActive = false;

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

        /// <summary>
        /// Reset camera to origin (0,0,0) with default orientation.
        /// Applies to all camera modes including freefly.
        /// Position: 0, 0, 0
        /// Rotation: Yaw = 180°, Pitch = 0° (looking down -Z axis, same as editor default)
        /// </summary>
        public void ResetToOrigin()
        {
            Position = Vector3.Zero;
            Yaw = 180f;
            Pitch = 0f;

            // Sync smoothing targets for fly mode so it doesn't snap back
            smoothCamPos = Position;
            smoothYaw = Yaw;
            smoothPitch = Pitch;

            UpdateVectors();
            _projectionDirty = true;
        }
        public void UpdateAspectRatio(float newWidth, float newHeight)
        {
            if (newHeight <= 0) newHeight = 1;
            _aspect = newWidth / newHeight;
            _projectionDirty = true;
        }

        /// <summary>Sync smoothYaw/smoothPitch with the current Yaw/Pitch.
        /// Call after externally setting Yaw/Pitch (e.g. loading from file, scene switch)
        /// so freefly mode doesn't snap back to stale values.</summary>
        public void SyncSmoothVectors()
        {
            smoothYaw = Yaw;
            smoothPitch = Pitch;
            smoothCamPos = Position;
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

        /// <summary>
        /// Convert viewport pixel coordinates (0..screenW, 0..screenH) to a world-space ray.
        /// The viewport coordinates are scene-space pixels matching the render target size.
        /// </summary>
        public void ScreenToRay(float screenX, float screenY, int screenW, int screenH,
            out Vector3 rayOrigin, out Vector3 rayDir)
        {
            // Convert to NDC (-1 to 1)
            float ndcX = (screenX / screenW) * 2f - 1f;
            float ndcY = (screenY / screenH) * 2f - 1f;

            Matrix4x4 view = GetViewMatrix();
            Matrix4x4 projection = GetProjectionMatrix();

            // Row-vector: clipPos = worldPos * view * projection
            // worldPos = clipPos * (view * projection)^-1
            Matrix4x4 vp = view * projection;
            if (!Matrix4x4.Invert(vp, out Matrix4x4 invVp))
            {
                rayOrigin = Position;
                rayDir = Front;
                return;
            }

            // Near plane (z=0) and far plane (z=1) in clip space
            Vector4 nearPt = new Vector4(ndcX, ndcY, 0f, 1f);
            Vector4 farPt = new Vector4(ndcX, ndcY, 1f, 1f);

            nearPt = Vector4.Transform(nearPt, invVp);
            farPt = Vector4.Transform(farPt, invVp);

            if (nearPt.W != 0f) nearPt /= nearPt.W;
            if (farPt.W != 0f) farPt /= farPt.W;

            rayOrigin = new Vector3(nearPt.X, nearPt.Y, nearPt.Z);
            Vector3 farPos = new Vector3(farPt.X, farPt.Y, farPt.Z);
            rayDir = Vector3.Normalize(farPos - rayOrigin);
        }

    /// <summary>When true, the editor viewport uses an orthographic projection
    /// (no perspective foreshortening) — useful for top/side architectural views.
    /// Toggled from the viewport camera-view menu.</summary>
    public bool IsOrthographic { get; set; } = false;
    private float _orthoSize = 20f;
    /// <summary>Half-height of the orthographic view volume in world units
    /// (scales how much of the scene is visible in ortho mode).
    /// Setting it dirties the cached projection matrix so the change takes effect.</summary>
    public float OrthoSize
    {
        get => _orthoSize;
        set
        {
            if (MathF.Abs(_orthoSize - value) > 0.001f)
            {
                _orthoSize = value;
                _projectionDirty = true;
            }
        }
    }

    /// <summary>Toggle between perspective and orthographic projection (editor only).</summary>
    public void ToggleProjection()
    {
        IsOrthographic = !IsOrthographic;
        _projectionDirty = true;
    }

    public Matrix4x4 GetProjectionMatrix()
{
    if (_projectionDirty)
    {
        float nearDist = _cameraMode == CameraMode.FirstPerson ? 0.1f : NearDist;

        if (IsOrthographic)
        {
            // Orthographic: world-space box centered on the camera's view direction.
            // Half-extents = OrthoSize (vertical); horizontal scales by aspect ratio.
            float halfH = OrthoSize;
            float halfW = halfH * _aspect;
            _projection = Matrix4x4.CreateOrthographicOffCenter(
                -halfW, halfW, -halfH, halfH, nearDist, FarDist);
        }
        else
        {
            // Convert FoV (degrees) → radians
            float fovRad = Helpers.OGLMath.ToRadians(FoV);

            _projection = Matrix4x4.CreatePerspectiveFieldOfView(
                fovRad,
                _aspect,
                nearDist,
                FarDist
            );
        }

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
        /// <summary>Free-fly camera with WASD + mouse look. Mouse look is active when the ✈ Fly
        /// toggle (FlyMouseLook) is on, OR while the Right Mouse Button is held in the viewport — the
        /// RMB hold is a TEMPORARY freefly-style look that never enables FlyMouseLook/FlyMode itself.
        /// Pressing ESC cancels the Fly toggle. While CTRL is held, both mouse-look and WASD movement
        /// are suppressed so editor shortcuts (Ctrl+D duplicate, Ctrl+Z undo, etc.) don't move the camera.</summary>
        public void SetCameraFlyMode(nint window, float dt, bool processInput = true)
        {
            if (processInput)
            {
                // ── ESC: cancel fly mouse-look toggle ──
                if (Keyboard.IsKeyPressed(window, Const.GLFW_KEY_ESCAPE))
                {
                    FlyMouseLook = false;
                    if (_mouseLookWasActive)
                    {
                        Mouse.ShowMouse(true);
                        Mouse.ResetState();
                        _mouseLookWasActive = false;
                    }
                }

                // CTRL is reserved for editor shortcuts (Ctrl+D duplicate, Ctrl+Z undo, ...).
                // When held, suppress mouse-look AND movement so shortcuts don't move the camera.
                bool ctrlHeld = Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT_CONTROL) ||
                                Keyboard.IsKeyDown(window, Const.GLFW_KEY_RIGHT_CONTROL);

                // ── Right-drag pan + Fly mouse-look ──
                // Right-drag ALWAYS pans the camera in the view plane (screen X → camera
                // Right, screen Y → camera Up) — it never rotates the view and never moves
                // any object. The ✈ Fly toggle is the only way to rotate/look around.
                bool rightLook = Mouse.IsButtonDown(Const.GLFW_MOUSE_BUTTON_RIGHT);
                if (!ctrlHeld && rightLook)
                {
                    // Pan speed scales with the view volume so the drag feels 1:1 with the
                    // cursor: ortho pans roughly one view height across the viewport.
                    float panK = IsOrthographic ? OrthoSize * 0.002f : (Position - Vector3.Zero).Length() * 0.0015f;
                    Position += (Right * -Mouse.DeltaX + Up * Mouse.DeltaY) * panK;

                    // Cancel a fly mouse-look in progress so the pan cleanly takes over.
                    if (_mouseLookWasActive)
                    {
                        Mouse.ShowMouse(true);
                        Mouse.ResetState();
                        _mouseLookWasActive = false;
                    }
                }
                else if (!ctrlHeld && FlyMouseLook)
                {
                    // Use configurable sensitivity from CameraConfig
                    float sens = Config.CameraConfig.FlyMouseSensitivity;
                    smoothYaw -= Mouse.DeltaX * sens;
                    smoothPitch -= Mouse.DeltaY * sens;
                    smoothPitch = Math.Clamp(smoothPitch, -89f, 89f);

                    if (!_mouseLookWasActive)
                    {
                        Mouse.ShowMouse(false);
                        // GLFW re-centers the hidden cursor (disabled mode) — drop the stale
                        // delta so the camera doesn't snap on the first look frame.
                        Mouse.ResetState();
                        _mouseLookWasActive = true;
                    }
                }
                else
                {
                    if (_mouseLookWasActive)
                    {
                        Mouse.ShowMouse(true);
                        Mouse.ResetState();
                        _mouseLookWasActive = false;
                    }
                }

                Yaw = smoothYaw;
                Pitch = smoothPitch;

                // ── Update vectors BEFORE movement so Front/Right are correct ──
                UpdateCameraVectorsFly();

                // Movement (WASD + scroll) — suppressed while CTRL is held (editor shortcuts).
                // WASD translation is additionally suppressed while LockTranslation is set
                // (2D level mode: the camera follows the Player2D and is panned with
                // right-drag instead). Scroll zoom stays available in both cases.
                if (!ctrlHeld)
                {
                    if (!LockTranslation)
                    {
                        Vector3 move = Vector3.Zero;

                        if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_W))
                            move += Front;
                        if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_S))
                            move -= Front;
                        if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_A))
                            move -= Right;
                        if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_D))
                            move += Right;

                        if (move.LengthSquared() > 0)
                            move = Vector3.Normalize(move);

                        Position += move * FlySpeed * dt;
                    }

                    // Scroll wheel for zoom. In perspective this dollies the camera
                    // along Front (its own FlyZoomSpeed keeps zooming responsive even
                    // though WASD movement is deliberately slower). In orthographic the
                    // view distance does nothing, so scroll scales the ortho volume
                    // (OrthoSize) instead — the classic ortho zoom.
                    if (Mouse.ScrollY != 0)
                    {
                        if (IsOrthographic)
                        {
                            OrthoSize = Math.Clamp(OrthoSize * (1f - Mouse.ScrollY * 0.08f), 1f, 200f);
                            Mouse.ResetScroll();
                        }
                        else
                        {
                            Position += Front * Mouse.ScrollY * Config.CameraConfig.FlyZoomSpeed * dt;
                            Mouse.ResetScroll();
                        }
                    }
                }
            }
            else
            {
                // Still update vectors even without input processing so the camera faces the right way
                UpdateCameraVectorsFly();
            }
        }

        /// <summary>
        /// Update Front/Right/Up vectors using the SAME formula as UpdateVectors()
        /// so fly mode behaves consistently with other camera modes.
        /// At Yaw=0 => Front faces +Z (not +X).
        /// </summary>
        private void UpdateCameraVectorsFly()
        {
            float yawRad = Helpers.OGLMath.ToRadians(Yaw);
            float pitchRad = Helpers.OGLMath.ToRadians(Pitch);

            Front.X = MathF.Sin(yawRad) * MathF.Cos(pitchRad);
            Front.Y = MathF.Sin(pitchRad);
            Front.Z = MathF.Cos(yawRad) * MathF.Cos(pitchRad);
            Front = Vector3.Normalize(Front);

            Right = Vector3.Normalize(Vector3.Cross(Front, Vector3.UnitY));
            Up = Vector3.Normalize(Vector3.Cross(Right, Front));
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

        public void SetCamera(nint window, Vector3 position, TerrainChunk gameTerrainChunk, float dt, List<StaticObjectManager>? staticManagers = null, bool processInput = true)
        {
            // Prevent scroll accumulation when viewport is not focused (applies to all camera modes)
            if (!processInput)
                Mouse.ResetScroll();

            // Toggle FlyMode — only when input is allowed
            if (processInput && Keyboard.IsKeyPressed(window, Const.GLFW_KEY_G))
                FlyMode = !FlyMode;
            
            IsFlyMode = FlyMode; 

            if (FlyMode)
            {
                IsFlyMode = true; 
                SetCameraFlyMode(window, dt, processInput);
                return;
            } 

            if (_cameraMode == CameraMode.FirstPerson)
                SetCameraFirstPerson(window, position, gameTerrainChunk, dt, processInput);
            else
                SetCameraThirdPerson(window, position, gameTerrainChunk, dt, staticManagers, processInput);
        }

        private void SetCameraThirdPerson(nint window, Vector3 position, TerrainChunk gameTerrainChunk, float dt, List<StaticObjectManager>? staticManagers = null, bool processInput = true)
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

            if (processInput)
            {
                // AIM MODE (ADS)
                if (Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_RIGHT))
                    IsADS = true;
                else
                    IsADS = false;

                // SHOULDER SWAP
                if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_Q))
                    targetShoulderOffset = -MathF.Abs(targetShoulderOffset);

                if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_E))
                    targetShoulderOffset = MathF.Abs(targetShoulderOffset);
            }

            float targetFov = IsADS ? 45.0f : BaseFoV;
            FoV = Helpers.OGLMath.Lerp(FoV, targetFov, 1f - MathF.Exp(-8f * dt));
            _projectionDirty = true;

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

            // CAMERA SWAY — only when input is allowed (moving key detection requires keyboard)
            if (processInput)
            {
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

            // SCROLL ZOOM — only when input is allowed
            if (processInput && preset.AllowZoom && Mouse.ScrollY != 0)
            {
                CameraConfig.TargetCameraDistance -= Mouse.ScrollY * (CameraConfig.ZoomSpeed * camScale);
                CameraConfig.TargetCameraDistance = Math.Clamp(
                    CameraConfig.TargetCameraDistance,
                    preset.MinDistance * camScale,
                    preset.MaxDistance * camScale
                );

                Mouse.ResetScroll();
            }

            // Prevent scroll accumulation when viewport is not focused
            if (!processInput)
                Mouse.ResetScroll();

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
         
        private void SetCameraFirstPerson(nint window, Vector3 position, TerrainChunk gameTerrainChunk, float dt, bool processInput = true)
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

            if (processInput)
            {
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
