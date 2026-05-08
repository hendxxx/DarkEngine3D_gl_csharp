using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine
{
    public unsafe class Keyboard
    {
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int> glfwGetKey;
         
        static bool isWireframe = false;
        static bool f1Pressed = false;
        static bool walkMode = true;   // start in walk mode: camera is clamped to terrain + eye height
        static bool f2Pressed = false;
        static float verticalVelocity = 0f;   // for jump/gravity
        static bool isGrounded = true;
        static int prevSpaceState = 0;
        static float speedCam = 1000.0f;
        static int lineVLoc = 0;
        static int linePLoc = 0;     // Lokasi uniform view & projection untuk shader garis
        static uint lineShaderProgram; // ID shader program untuk rendering garis
        static Vector3[]?frozenCorners = null; // Untuk menyimpan koordinat frustum yang di-freeze

        // state untuk tombol P (edge detection)
        static int prevPState = 0;
        static bool frozenMode = false;

        // Quick-slot 1..6 — read by UIRenderer to highlight the selected slot.
        public static int SelectedQuickSlot = 1;
        static int[] prevNumKeyState = new int[6];

        // Attack state
        static bool prevLmbDown = false;
        static float throwCooldown = 0f;
        static float punchCooldown = 0f;
        static float throwAnimTimer = 0f;   // counts DOWN
        static float punchAnimTimer = 0f;   // counts DOWN

        public static unsafe void Init(nint glfwLib, float _speedCam)
        {
            lineShaderProgram = Shader.GetLineShaderProgram();
            glfwGetKey = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)NativeLibrary.GetExport(glfwLib, "glfwGetKey");

            isWireframe = false;
            f1Pressed = false;

            speedCam = _speedCam;

            lineVLoc = GL.GetUniformLocation(lineShaderProgram, "view");
            linePLoc = GL.GetUniformLocation(lineShaderProgram, "projection");
        }

        public static unsafe void Update(nint glfwLib, nint window, Camera camera, float deltaTime, TerrainChunk gameTerrainChunk, MovingObjects movers, Projectiles projectiles)
        {
            // Tombol ESC untuk Keluar
            if (glfwGetKey(window, Const.GLFW_KEY_ESCAPE) == Const.GLFW_PRESS)
            {
                // Beritahu GLFW untuk menutup jendela
                var glfwSetWindowShouldClose = (delegate* unmanaged[Cdecl]<IntPtr, int, void>)NativeLibrary.GetExport(glfwLib, "glfwSetWindowShouldClose");
                glfwSetWindowShouldClose(window, 1);
            }

            // F1 toggle wireframe (rising edge)
            int f1State = glfwGetKey(window, Const.GLFW_KEY_F1);
            if (f1State == Const.GLFW_PRESS)
            {
                if (!f1Pressed)
                {
                    isWireframe = !isWireframe;
                    GL.PolygonMode(Const.GL_FRONT_AND_BACK, isWireframe ? Const.GL_LINE : Const.GL_FILL);
                    f1Pressed = true;
                    Console.WriteLine(isWireframe ? "Wireframe Mode: ON" : "Wireframe Mode: OFF");
                }
            }
            else
            {
                f1Pressed = false;
            }

            // F2 toggle walk mode (rising edge)
            int f2State = glfwGetKey(window, Const.GLFW_KEY_F2);
            if (f2State == Const.GLFW_PRESS)
            {
                if (!f2Pressed)
                {
                    walkMode = !walkMode;
                    f2Pressed = true;
                    Console.WriteLine(walkMode ? "Walk Mode: ON (clamped to terrain)" : "Walk Mode: OFF (free fly)");
                }
            }
            else
            {
                f2Pressed = false;
            }

            // === Read movement-related inputs ===
            bool dead = UIRenderer.CurrentHP <= 0;
            bool shiftPressed = glfwGetKey(window, Const.GLFW_KEY_LEFT_SHIFT) == Const.GLFW_PRESS
                                || glfwGetKey(window, Const.GLFW_KEY_RIGHT_SHIFT) == Const.GLFW_PRESS;
            bool wPressed = !dead && glfwGetKey(window, Const.GLFW_KEY_W) == Const.GLFW_PRESS;
            bool sPressed = !dead && glfwGetKey(window, Const.GLFW_KEY_S) == Const.GLFW_PRESS;
            bool aPressed = !dead && glfwGetKey(window, Const.GLFW_KEY_A) == Const.GLFW_PRESS;
            bool dPressed = !dead && glfwGetKey(window, Const.GLFW_KEY_D) == Const.GLFW_PRESS;
            bool moving = wPressed || sPressed || aPressed || dPressed;
            bool crouching = !dead && walkMode && glfwGetKey(window, Const.GLFW_KEY_C) == Const.GLFW_PRESS;

            // === Stamina state (read BEFORE update so flags reflect the current frame) ===
            float preStaminaPct = (UIRenderer.MaxStamina > 0f)
                ? UIRenderer.CurrentStamina / UIRenderer.MaxStamina * 100f
                : 0f;
            bool exhausted = preStaminaPct <= 0f;
            bool tired = preStaminaPct < Const.STAMINA_LOW_THRESHOLD_PCT;

            // Running needs Shift + standing up + not tired. Crouching disables run; tired disables run.
            bool running = shiftPressed && !crouching && !tired;

            // === Drain or regen ===
            // Crouch-walking drains at the run rate (more taxing than upright walking).
            // When exhausted, force regen so the player isn't locked at 0% while still holding WASD.
            if (walkMode && moving && !exhausted)
            {
                bool fastDrain = running || crouching;
                float drain = fastDrain ? Const.STAMINA_DRAIN_RUN : Const.STAMINA_DRAIN_WALK;
                UIRenderer.CurrentStamina = MathF.Max(0f, UIRenderer.CurrentStamina - drain * deltaTime);
            }
            else
            {
                UIRenderer.CurrentStamina = MathF.Min(UIRenderer.MaxStamina, UIRenderer.CurrentStamina + Const.STAMINA_REGEN * deltaTime);
            }

            // === Speed factor from stamina ===
            //   exhausted (= 0%)  → 0× (cannot move at all)
            //   below threshold   → very slow crawl
            //   otherwise         → full speed
            float staminaSpeedFactor;
            if (walkMode && exhausted)
                staminaSpeedFactor = 0f;
            else if (walkMode && tired)
                staminaSpeedFactor = Const.STAMINA_LOW_SPEED_FACTOR;
            else
                staminaSpeedFactor = 1.0f;

            // The shift/run multiplier only applies when we're actually running —
            // i.e. it's gated off while crouched, while tired, and while exhausted.
            float speed = speedCam * deltaTime
                          * (running ? Const.SHIFT_SPEED_MULTIPLIER : 1.0f)
                          * staminaSpeedFactor;

            // In walk mode, build a horizontal-only forward vector so looking up/down doesn't push the player up/down.
            Vector3 forward, right;
            if (walkMode)
            {
                Vector3 flatFront = new Vector3(camera.Front.X, 0f, camera.Front.Z);
                if (flatFront.LengthSquared() < 1e-6f) flatFront = new Vector3(0, 0, -1);
                forward = Vector3.Normalize(flatFront);
                right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            }
            else
            {
                forward = camera.Front;
                right = Vector3.Normalize(Vector3.Cross(camera.Front, camera.Up));
            }

            if (wPressed) camera.Position += forward * speed;
            if (sPressed) camera.Position -= forward * speed;
            if (aPressed) camera.Position -= right * speed;
            if (dPressed) camera.Position += right * speed;

            // Stop the player at the edge of the map (walk mode only — free-fly stays unrestricted for debugging).
            if (walkMode)
            {
                float half = TerrainChunk.GetHalfMapSize();
                float clampedX = Math.Clamp(camera.Position.X, -half, half);
                float clampedZ = Math.Clamp(camera.Position.Z, -half, half);
                camera.Position = new Vector3(clampedX, camera.Position.Y, clampedZ);
            }

            // Vertical physics: gravity + jump + crouch, with terrain as the floor.
            if (walkMode)
            {
                float currentEyeHeight = crouching ? Const.PLAYER_CROUCH_HEIGHT : Const.PLAYER_EYE_HEIGHT;

                // Jump: rising-edge of Space, only when grounded, has stamina, and not dead.
                int spaceState = glfwGetKey(window, Const.GLFW_KEY_SPACE);
                bool canAffordJump = UIRenderer.CurrentStamina >= Const.STAMINA_JUMP_COST;
                if (spaceState == Const.GLFW_PRESS && prevSpaceState != Const.GLFW_PRESS && isGrounded && canAffordJump && !dead)
                {
                    verticalVelocity = Const.PLAYER_JUMP_SPEED;
                    isGrounded = false;
                    UIRenderer.CurrentStamina = MathF.Max(0f, UIRenderer.CurrentStamina - Const.STAMINA_JUMP_COST);
                }
                prevSpaceState = spaceState;

                if (isGrounded)
                {
                    // Hugging the ground: directly snap to terrain (handles slopes + crouch toggle smoothly).
                    float groundY = TerrainChunk.GetHeightAt(camera.Position.X, camera.Position.Z) + currentEyeHeight;
                    camera.Position = new Vector3(camera.Position.X, groundY, camera.Position.Z);
                    verticalVelocity = 0f;
                }
                else
                {
                    // Airborne: integrate velocity, then test against terrain.
                    verticalVelocity -= Const.PLAYER_GRAVITY * deltaTime;
                    float newY = camera.Position.Y + verticalVelocity * deltaTime;
                    float groundY = TerrainChunk.GetHeightAt(camera.Position.X, camera.Position.Z) + currentEyeHeight;
                    if (newY <= groundY)
                    {
                        newY = groundY;
                        verticalVelocity = 0f;
                        isGrounded = true;
                    }
                    camera.Position = new Vector3(camera.Position.X, newY, camera.Position.Z);
                }
            }
             
            // Quick-slot select: 1..6 (rising edge)
            int[] numKeys = new[] { Const.GLFW_KEY_1, Const.GLFW_KEY_2, Const.GLFW_KEY_3, Const.GLFW_KEY_4, Const.GLFW_KEY_5, Const.GLFW_KEY_6 };
            for (int i = 0; i < numKeys.Length; i++)
            {
                int state = glfwGetKey(window, numKeys[i]);
                if (state == Const.GLFW_PRESS && prevNumKeyState[i] != Const.GLFW_PRESS)
                {
                    SelectedQuickSlot = i + 1;
                }
                prevNumKeyState[i] = state;
            }

            // P: toggle freeze frustum and set it into TerrainChunk (rising edge)
            int pState = glfwGetKey(window, Const.GLFW_KEY_P);
            if (pState == Const.GLFW_PRESS && prevPState != Const.GLFW_PRESS)
            {
                // toggle frozen mode
                frozenMode = !frozenMode;
                if (frozenMode)
                {
                    Matrix4x4 view = camera.GetViewMatrix();
                    Matrix4x4 proj = Camera.GetProjectionMatrix(camera.GetAspect(), camera.foV, camera.nearDist, camera.farDist);
                    frozenCorners = TerrainChunk.GetFrustumCorners(view, proj);
                    gameTerrainChunk.SetFrozenFrustumCorners(frozenCorners); // PASS frozen corners to TerrainChunk
                    gameTerrainChunk.SetHighlightFrustumMatches(true);
                }
                else
                {
                    frozenCorners = null;
                    gameTerrainChunk.ClearFrozenFrustumCorners();
                    gameTerrainChunk.SetHighlightFrustumMatches(false);
                }
            }
            prevPState = pState;

            // NOTE: drawing of the frozen frustum lines is handled inside TerrainChunk.Render now.

            // === Attack handling ===
            // Tick cooldowns + animation timers.
            if (throwCooldown > 0f)  throwCooldown  -= deltaTime;
            if (punchCooldown > 0f)  punchCooldown  -= deltaTime;
            if (throwAnimTimer > 0f) throwAnimTimer -= deltaTime;
            if (punchAnimTimer > 0f) punchAnimTimer -= deltaTime;

            bool lmbDown = !dead && Mouse.IsButtonDown(window, Const.GLFW_MOUSE_BUTTON_LEFT);
            // Rising-edge: only fire on the moment of click, not on hold.
            if (lmbDown && !prevLmbDown)
            {
                if (SelectedQuickSlot == 1 && throwCooldown <= 0f)
                {
                    // Spawn a stone in front of the player with a small upward arc.
                    Vector3 spawn = camera.Position + camera.Front * 0.8f;
                    Vector3 vel = camera.Front * Const.STONE_SPEED + Vector3.UnitY * Const.STONE_LAUNCH_UP;
                    projectiles.Spawn(spawn, vel);
                    throwCooldown  = Const.THROW_COOLDOWN;
                    throwAnimTimer = Const.THROW_ANIM_DURATION;
                }
                else if (SelectedQuickSlot == 2 && punchCooldown <= 0f)
                {
                    movers.HitMelee(camera.Position, camera.Front, Const.PUNCH_RANGE, Const.PUNCH_CONE_DOT, Const.PUNCH_DAMAGE);
                    punchCooldown  = Const.PUNCH_COOLDOWN;
                    punchAnimTimer = Const.PUNCH_ANIM_DURATION;
                }
            }
            prevLmbDown = lmbDown;

            // Drive the camera ViewBobOffset from whichever animation is active.
            // Throw: a downward dip (sin pulse). Punch: a forward thrust (sin pulse).
            Vector3 bob = Vector3.Zero;
            if (throwAnimTimer > 0f)
            {
                float t = 1f - (throwAnimTimer / Const.THROW_ANIM_DURATION);
                bob.Y += MathF.Sin(t * MathF.PI) * -0.20f;
            }
            if (punchAnimTimer > 0f)
            {
                float t = 1f - (punchAnimTimer / Const.PUNCH_ANIM_DURATION);
                Vector3 fwd = camera.Front; fwd.Y = 0f;
                if (fwd.LengthSquared() > 1e-6f) fwd = Vector3.Normalize(fwd);
                bob += fwd * (MathF.Sin(t * MathF.PI) * 0.30f);
            }
            camera.ViewBobOffset = bob;
        }
    }
}
