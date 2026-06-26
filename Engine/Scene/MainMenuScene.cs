using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Scene
{
    /// <summary>
    /// Main menu scene with Start Game, Settings, and Exit buttons.
    /// Features an animated procedural background with floating particles,
    /// grid lines, vignette, and a pulsing button glow.
    /// </summary>
    public unsafe class MainMenuScene : IScene
    {
        public string Name => "MainMenu";

        // ── Dependencies ──
        private readonly SceneManager _sceneManager;
        private readonly Camera _camera;
        private readonly Lights _light;

        // ── Rendering ──
        private HUD? _hud;
        private float _deltaTime;
        private float _totalTime;

        // ── Menu state ──
        private enum MenuAction { StartGame, Settings, Exit }
        private readonly MenuAction[] _menuItems = [MenuAction.StartGame, MenuAction.Settings, MenuAction.Exit];
        private readonly string[] _menuLabels = ["START GAME", "SETTINGS", "EXIT"];
        private int _selectedIndex = 0;
        private bool _settingsOpen = false;

        // ── Random ──
        private readonly Random _rng = new();

        // ── Keyboard state (edge detection) ──
        private bool _upWasDown = false;
        private bool _downWasDown = false;
        private bool _enterWasDown = false;
        private bool _escapeWasDown = false;
        private bool _mouseWasDown = false;

        // ── Layout constants ──
        private const float ButtonWidth = 340f;
        private const float ButtonHeight = 58f;
        private const float ButtonSpacing = 16f;

        // ── Interactive Settings ──
        private int _settingsSelection = 0;
        private bool _settingsUpWasDown = false;
        private bool _settingsDownWasDown = false;
        private bool _settingsEnterWasDown = false;
        private bool _settingsMouseWasDown = false;
        private bool _settingsEscapeWasDown = false;

        private readonly string[] _settingLabels =
        [
            "Resolution",
            "Fullscreen",
            "VSync",
            "Shadow Quality",
            "OC Mode",
            "FOV",
            "Mouse Sensitivity",
            "APPLY & SAVE",
        ];

        private readonly string[][] _settingOptions =
        [
            ["1920 x 1080", "1280 x 720", "2560 x 1440"],
            ["OFF", "ON"],
            ["OFF", "ON"],
            ["LOW", "MEDIUM", "HIGH", "ULTRA"],
            ["Software", "HiZ", "OFF"],
            ["60", "70", "80", "90", "100", "110"],
            ["0.25×", "0.50×", "0.75×", "1.0×", "1.5×", "2.0×", "3.0×"],
            [""],   // Apply button — single empty option, no cycling
        ];

        // Current value index for each setting (last = 0 always for Apply)
        private readonly int[] _settingValues = [0, 0, 0, 3, 1, 0, 3, 0];
        // 0=Res,1=FS,2=VSync,3=Shadow,4=OC,5=FOV,6=Mouse,7=Apply

        // ── Shared lookup tables (avoid duplication) ──
        private static readonly int[][] ResolutionValues = [[1920, 1080], [1280, 720], [2560, 1440]];
        private static readonly int[][] CascadePresets =
        [
            [2048, 1024, 512],   // Low
            [4096, 2048, 1024],  // Medium
            [4096, 4096, 2048],  // High
            [8192, 4096, 2048],  // Ultra
        ];

        // ── Settings panel layout (shared between Update hover + RenderSettings) ──
        private readonly struct SettingsLayout
        {
            public readonly float PanelW, PanelH;
            public readonly float PanelX, PanelY;
            public readonly float TitleH;
            public readonly float DividerY;
            public readonly float LineH, LineGap;
            public readonly float ListStartY;

            public SettingsLayout(int screenW, int screenH, HUD hud)
            {
                PanelW = 500f;
                PanelH = 430f;
                PanelX = (screenW - PanelW) * 0.5f;
                PanelY = (screenH - PanelH) * 0.5f;
                TitleH = hud.MeasureTextHeight("SETTINGS");
                DividerY = PanelY + 20f + TitleH + 14f;
                LineH = hud.MeasureTextHeight("Resolution");
                LineGap = 10f;
                ListStartY = DividerY + 12f;
            }
        }

        // =================================================
        //  ANIMATED BACKGROUND — Particles & Effects
        // =================================================

        private const int ParticleCount = 60;
        private readonly Particle[] _particles = new Particle[ParticleCount];
        private float _scanLineY = 0f;
        private float _gridOffsetX = 0f;

        private struct Particle
        {
            public float X, Y;
            public float SpeedX, SpeedY;
            public float WobblePhase, WobbleAmp;
            public float Size;
            public float Alpha;
            public float AlphaSpeed;
            public Vector3 Color;
        }

        public MainMenuScene(SceneManager sceneManager, Camera camera, Lights light)
        {
            _sceneManager = sceneManager;
            _camera = camera;
            _light = light;
        }

        public void Enter()
        {
            nint window = Glfw.GetWindow();

            _hud = new HUD("Artifacts\\fonts\\Worldstar.ttf", 28.0f);
            _selectedIndex = 0;
            _settingsOpen = false;
            _settingsSelection = 0;
            _totalTime = 0f;
            _scanLineY = 0f;

            // Subscribe to resize event for camera aspect updates
            Glfw.OnWindowResized += OnWindowResized;

            Mouse.ShowMouse(true);

            // Reset all edge-detection flags to prevent held-down keys
            // (e.g. Enter/Esc from confirm dialog) from triggering immediate actions.
            _upWasDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_W);
            _downWasDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);
            _enterWasDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE);
            _escapeWasDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);
            _mouseWasDown = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);
            _settingsUpWasDown = _upWasDown;
            _settingsDownWasDown = _downWasDown;
            _settingsEnterWasDown = _enterWasDown;
            _settingsEscapeWasDown = _escapeWasDown;
            _settingsMouseWasDown = _mouseWasDown;

            // ── Initialize particles ──
            var rng = new Random(42);
            for (int i = 0; i < ParticleCount; i++)
            {
                _particles[i] = new Particle
                {
                    X = (float)(rng.NextDouble() * 1920),
                    Y = (float)(rng.NextDouble() * 1080),
                    SpeedX = (float)((rng.NextDouble() - 0.5) * 4f),
                    SpeedY = (float)(-(rng.NextDouble() * 6f + 3f)), // float upward
                    WobblePhase = (float)(rng.NextDouble() * Math.PI * 2),
                    WobbleAmp = (float)(rng.NextDouble() * 10f + 5f),
                    Size = (float)(rng.NextDouble() * 3f + 1.5f),
                    Alpha = (float)(rng.NextDouble() * 0.5f + 0.15f),
                    AlphaSpeed = (float)(rng.NextDouble() * 0.3f + 0.1f),
                    Color = (i % 3) switch
                    {
                        0 => new Vector3(0.3f, 0.5f, 0.9f), // blue
                        1 => new Vector3(0.6f, 0.3f, 0.9f), // purple
                        _ => new Vector3(0.2f, 0.7f, 0.8f), // cyan
                    }
                };
            }

            // ── Load saved settings from JSON ──
            var saved = SettingsSave.Load();
            _settingValues[0] = saved.Resolution;
            _settingValues[1] = saved.Fullscreen ? 1 : 0;
            _settingValues[2] = saved.VSync ? 1 : 0;
            _settingValues[3] = saved.ShadowQuality;
            _settingValues[4] = saved.OcclusionMode;
            _settingValues[5] = (saved.Fov - 60) / 10;  // FOV: clamp to valid range
            if (_settingValues[5] < 0) _settingValues[5] = 0;
            if (_settingValues[5] >= _settingOptions[5].Length) _settingValues[5] = _settingOptions[5].Length - 1;
            // Apply loaded OC Mode & FOV immediately
            ApplyOcclusionMode(_settingValues[4]);
            _camera.BaseFoV = saved.Fov;
            _camera.FoV = saved.Fov;

            // ── Load Mouse Sensitivity ──
            _settingValues[6] = saved.MouseSensitivity;
            if (_settingValues[6] < 0) _settingValues[6] = 0;
            if (_settingValues[6] >= _settingOptions[6].Length) _settingValues[6] = _settingOptions[6].Length - 1;
            ApplyMouseSensitivity(_settingValues[6]);

            Console.WriteLine("[MainMenu] Entered.");
        }

        private void OnWindowResized(int width, int height)
        {
            _camera.UpdateAspectRatio(width, height);
        }

        public void Update(float deltaTime)
        {
            _deltaTime = deltaTime;
            _totalTime += deltaTime;

            // ── Update particles ──
            UpdateParticles(deltaTime);

            // ── Update scan line ──
            _scanLineY += deltaTime * 30f;
            if (_scanLineY > Glfw.WindowHeight)
                _scanLineY = 0f;

            // ── Update grid offset ──
            _gridOffsetX += deltaTime * 2f;
            if (_gridOffsetX > 40f)
                _gridOffsetX -= 40f;

            nint window = Glfw.GetWindow();

            // ── Mouse position tracking ──
            Mouse.GetCursorPosition(out double mouseX, out double mouseY);
            bool mousePressed = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);

            // Calculate button positions (centered)
            float totalHeight = _menuItems.Length * ButtonHeight + (_menuItems.Length - 1) * ButtonSpacing;
            float startY = (Glfw.WindowHeight - totalHeight) * 0.5f;

            // ── Mouse hover detection — only for main menu buttons when settings closed ──
            int hoveredIndex = -1;
            if (!_settingsOpen)
            {
                for (int i = 0; i < _menuItems.Length; i++)
                {
                    float bx = (Glfw.WindowWidth - ButtonWidth) * 0.5f;
                    float by = startY + i * (ButtonHeight + ButtonSpacing);
                    if (mouseX >= bx && mouseX <= bx + ButtonWidth &&
                        mouseY >= by && mouseY <= by + ButtonHeight)
                    {
                        hoveredIndex = i;
                        break;
                    }
                }
                if (hoveredIndex >= 0)
                    _selectedIndex = hoveredIndex;
            }

            // ── Mouse click ──
            if (mousePressed && !_mouseWasDown)
            {
                _mouseWasDown = true;
                if (hoveredIndex >= 0 && !_settingsOpen)
                {
                    ExecuteMenuAction(_menuItems[hoveredIndex]);
                }
            }
            if (!mousePressed)
                _mouseWasDown = false;

            // ── Keyboard navigation ──
            bool upDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_W);
            bool downDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);
            bool enterDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE);
            bool escapeDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);

            if (_settingsOpen)
            {
                // ── Settings navigation ──
                if (escapeDown && !_settingsEscapeWasDown)
                    _settingsOpen = false;

                // Mouse hover in settings panel
                if (_hud != null)
                {
                    var lay = new SettingsLayout(Glfw.WindowWidth, Glfw.WindowHeight, _hud);
                    for (int i = 0; i < _settingLabels.Length; i++)
                    {
                        float itemY = lay.ListStartY + i * (lay.LineH + lay.LineGap);
                        // Hover zone exactly matches the visual row spacing:
                        // [itemY, itemY + lineH + lineGap) = 38px per item, no gaps, no overlap
                        if (mouseX >= lay.PanelX + 20f && mouseX <= lay.PanelX + lay.PanelW - 20f &&
                            mouseY >= itemY && mouseY < itemY + lay.LineH + lay.LineGap)
                        {
                            _settingsSelection = i;
                            break;
                        }
                    }
                }

                if (upDown && !_settingsUpWasDown)
                    _settingsSelection = (_settingsSelection - 1 + _settingLabels.Length) % _settingLabels.Length;
                if (downDown && !_settingsDownWasDown)
                    _settingsSelection = (_settingsSelection + 1) % _settingLabels.Length;

                // Enter/Space/Click → cycle current setting
                bool settingsActivate = (enterDown && !_settingsEnterWasDown) || (mousePressed && !_settingsMouseWasDown);
                if (settingsActivate)
                {
                    CycleSetting(_settingsSelection);
                }

                _settingsUpWasDown = upDown;
                _settingsDownWasDown = downDown;
                _settingsEnterWasDown = enterDown;
                _settingsEscapeWasDown = escapeDown;
                if (!mousePressed) _settingsMouseWasDown = false;
                if (mousePressed && !_settingsMouseWasDown) _settingsMouseWasDown = true;

                // Don't process main menu navigation when settings is open
                _upWasDown = upDown;
                _downWasDown = downDown;
                _enterWasDown = enterDown;
                _escapeWasDown = escapeDown;
                return;
            }
            else
            {
                if (upDown && !_upWasDown)
                    _selectedIndex = (_selectedIndex - 1 + _menuItems.Length) % _menuItems.Length;
                if (downDown && !_downWasDown)
                    _selectedIndex = (_selectedIndex + 1) % _menuItems.Length;
                if (enterDown && !_enterWasDown)
                    ExecuteMenuAction(_menuItems[_selectedIndex]);
            }

            _upWasDown = upDown;
            _downWasDown = downDown;
            _enterWasDown = enterDown;
            _escapeWasDown = escapeDown;
        }

        private void UpdateParticles(float dt)
        {
            float w = Glfw.WindowWidth;
            float h = Glfw.WindowHeight;

            for (int i = 0; i < ParticleCount; i++)
            {
                var p = _particles[i];

                // Wobble: sine horizontal oscillation
                p.WobblePhase += dt * 0.8f;
                float wobbleX = MathF.Sin(p.WobblePhase) * p.WobbleAmp * dt;

                p.X += p.SpeedX * dt + wobbleX;
                p.Y += p.SpeedY * dt;

                // Alpha oscillation
                p.Alpha += MathF.Sin(_totalTime * p.AlphaSpeed + i) * dt * 0.15f;
                p.Alpha = Math.Clamp(p.Alpha, 0.05f, 0.7f);

                // Respawn when off-screen
                if (p.Y < -10f || p.X < -50f || p.X > w + 50f)
                {
                    p.X = (float)(_rng.NextDouble() * w);
                    p.Y = h + (float)(_rng.NextDouble() * 20f);
                    p.SpeedX = (float)((_rng.NextDouble() - 0.5) * 4f);
                    p.SpeedY = (float)(-(_rng.NextDouble() * 6f + 3f));
                }

                _particles[i] = p;
            }
        }

        private void ExecuteMenuAction(MenuAction action)
        {
            switch (action)
            {
                case MenuAction.StartGame:
                    Console.WriteLine("[MainMenu] Starting game...");
                    LoadingScene loadingScene = new(_sceneManager, _camera, _light);
                    _sceneManager.SwitchScene(loadingScene);
                    break;

                case MenuAction.Settings:
                    _settingsOpen = true;
                    Console.WriteLine("[MainMenu] Settings opened.");
                    break;

                case MenuAction.Exit:
                    Console.WriteLine("[MainMenu] Exiting...");
                    _sceneManager.Stop();
                    break;
            }
        }

        /// <summary>Apply mouse sensitivity multiplier (0=0.25×, 1=0.50×, ..., 6=3.0×).</summary>
        private static void ApplyMouseSensitivity(int val)
        {
            float[] multipliers = [0.25f, 0.50f, 0.75f, 1.0f, 1.5f, 2.0f, 3.0f];
            Mouse.Sensitivity = 0.1f * multipliers[val];
        }

        /// <summary>Apply the OcclusionMode setting without writing to Config.OcclusionConfig.</summary>
        private static void ApplyOcclusionMode(int val)
        {
            if (val == 0)
            {
                Config.OcclusionConfig.Mode = Config.OcclusionMode.Software;
                Config.OcclusionConfig.UseOcclusion = true;
            }
            else if (val == 1)
            {
                Config.OcclusionConfig.Mode = Config.OcclusionMode.HiZ;
                Config.OcclusionConfig.UseOcclusion = true;
            }
            else // OFF
            {
                Config.OcclusionConfig.UseOcclusion = false;
            }
        }

        /// <summary>Apply all current settings — saves to JSON and applies immediately.</summary>
        private void ApplySettings()
        {
            int res = _settingValues[0];
            bool fs = _settingValues[1] == 1;
            bool vs = _settingValues[2] == 1;
            int sq = _settingValues[3];
            int oc = _settingValues[4];
            int fovVal = int.Parse(_settingOptions[5][_settingValues[5]]);

            // ── Save to JSON ──
            var data = new SettingsData
            {
                Resolution = res,
                Fullscreen = fs,
                VSync = vs,
                ShadowQuality = sq,
                OcclusionMode = oc,
                Fov = fovVal,
                MouseSensitivity = _settingValues[6],
            };
            SettingsSave.Save(data);

            // ── Apply Shadow Quality ──
            Config.ShadowConfig.CascadeSizes = CascadePresets[sq];

            // ── Apply OC Mode ──
            ApplyOcclusionMode(oc);

            // ── Live apply FOV ──
            _camera.BaseFoV = fovVal;
            _camera.FoV = fovVal;

            // ── Live apply Mouse Sensitivity ──
            ApplyMouseSensitivity(_settingValues[6]);

            // ── Live apply Resolution, VSync, Fullscreen ──
            Glfw.SetWindowSize(ResolutionValues[res][0], ResolutionValues[res][1]);
            Glfw.SetSwapInterval(vs ? 1 : 0);
            Glfw.SetFullscreen(fs);

            string[] resLabels = ["1920x1080", "1280x720", "2560x1440"];
            Console.WriteLine($"[Settings] Applied: Resolution={resLabels[res]}, Fullscreen={fs}, VSync={vs}, ShadowQuality={sq}, OC={oc}, FOV={fovVal}, MouseSens={_settingOptions[6][_settingValues[6]]}");
        }

        /// <summary>Cycle the given setting to its next option and apply the change.</summary>
        private void CycleSetting(int index)
        {
            // ── APPLY & SAVE button — execute, don't cycle ──
            if (index == 7)
            {
                ApplySettings();
                return;
            }

            _settingValues[index] = (_settingValues[index] + 1) % _settingOptions[index].Length;
            int val = _settingValues[index];

            switch (index)
            {
                case 0: // Resolution — live apply
                    {
                        int w = ResolutionValues[val][0], h = ResolutionValues[val][1];
                        Glfw.SetWindowSize(w, h);
                        Console.WriteLine($"[Settings] Resolution → {_settingOptions[index][val]} (live)");
                    }
                    break;

                case 1: // Fullscreen — live toggle
                    Glfw.SetFullscreen(val == 1);
                    Console.WriteLine($"[Settings] Fullscreen → {_settingOptions[index][val]} (live)");
                    break;

                case 2: // VSync — live apply
                    Glfw.SetSwapInterval(val); // 0=OFF, 1=ON
                    Console.WriteLine($"[Settings] VSync → {_settingOptions[index][val]} (live)");
                    break;

                case 3: // Shadow Quality — live apply
                    Config.ShadowConfig.CascadeSizes = CascadePresets[val];
                    Console.WriteLine($"[Settings] Shadow Quality → {_settingOptions[index][val]} (live)");
                    break;

                case 4: // OC Mode — live apply
                    ApplyOcclusionMode(val);
                    Console.WriteLine($"[Settings] OC Mode → {_settingOptions[index][val]} (live)");
                    break;

                case 5: // FOV — live apply
                    {
                        int fovVal = int.Parse(_settingOptions[index][val]);
                        _camera.BaseFoV = fovVal;
                        _camera.FoV = fovVal;
                        Console.WriteLine($"[Settings] FOV → {_settingOptions[index][val]}° (live)");
                    }
                    break;

                case 6: // Mouse Sensitivity — live apply
                    ApplyMouseSensitivity(val);
                    Console.WriteLine($"[Settings] Mouse Sensitivity → {_settingOptions[index][val]} (live)");
                    break;

                default:
                    Console.WriteLine($"[Settings] {_settingLabels[index]} → {_settingOptions[index][val]}");
                    break;
            }

            // ── Auto-save to JSON ──
            var data = new SettingsData
            {
                Resolution = _settingValues[0],
                Fullscreen = _settingValues[1] == 1,
                VSync = _settingValues[2] == 1,
                ShadowQuality = _settingValues[3],
                OcclusionMode = _settingValues[4],
                Fov = int.Parse(_settingOptions[5][_settingValues[5]]),
                MouseSensitivity = _settingValues[6],
            };
            SettingsSave.Save(data);
        }

        public void Render()
        {
            GL.ClearColor(0.04f, 0.04f, 0.06f, 1.0f);
            GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);

            if (_hud == null) return;

            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;

            // ==========================================
            //  LAYER 1: Procedural Background
            // ==========================================

            RenderBackground(w, h);

            // ==========================================
            //  LAYER 2: Particles
            // ==========================================

            RenderParticles(w, h);

            // ==========================================
            //  LAYER 3: Scan Line Effect
            // ==========================================

            RenderScanLine(w, h);

            // ==========================================
            //  LAYER 4: Grid Lines
            // ==========================================

            RenderGrid(w, h);

            // ==========================================
            //  LAYER 5: Vignette
            // ==========================================

            RenderVignette(w, h);

            // ==========================================
            //  LAYER 6: Title
            // ==========================================

            RenderTitle(w);

            // ── Settings overlay ──
            if (_settingsOpen)
            {
                RenderSettings(w, h);
                return;
            }

            // ==========================================
            //  LAYER 7: Buttons
            // ==========================================

            RenderButtons(w, h);

            // ==========================================
            //  LAYER 8: Bottom Hint
            // ==========================================

            string hint = "Arrow keys or mouse to navigate - Enter to select";
            float hintY = h - 45f;
            _hud.DrawCenteredText(hint, w, hintY, new Vector3(0.35f, 0.35f, 0.45f));
        }

        // =====================================================
        //  BACKGROUND RENDERERS
        // =====================================================

        private void RenderBackground(int w, int h)
        {
            if (_hud == null) return;

            // Deep gradient-like effect: overlapping bands
            float bandCount = 12;
            float bandH = h / bandCount;
            for (int i = 0; i < bandCount; i++)
            {
                float t = i / bandCount;
                float brightness = 0.03f + t * 0.04f; // darker at top
                _hud.DrawBox(0, i * bandH, w, bandH + 1, new Vector3(brightness, brightness * 1.1f, brightness * 1.3f));
            }

            // Subtle center glow (warm light from behind buttons)
            float glowCX = w * 0.5f;
            float glowCY = h * 0.5f;
            float glowW = 500f;
            float glowH = 300f;
            float pulse = 0.5f + 0.5f * MathF.Sin(_totalTime * 0.3f);
            float glowAlpha = 0.03f + pulse * 0.02f;
            _hud.DrawBox(glowCX - glowW * 0.5f, glowCY - glowH * 0.5f,
                glowW, glowH, new Vector3(0.15f * glowAlpha * 4f, 0.2f * glowAlpha * 4f, 0.4f * glowAlpha * 4f));
        }

        private void RenderParticles(int w, int h)
        {
            if (_hud == null) return;

            for (int i = 0; i < ParticleCount; i++)
            {
                var p = _particles[i];
                float alpha = p.Alpha;

                // Outer glow (larger, softer)
                float glowSize = p.Size * 4f;
                _hud.DrawBox(p.X - glowSize * 0.5f, p.Y - glowSize * 0.5f,
                    glowSize, glowSize, p.Color * alpha * 0.15f);

                // Core dot
                _hud.DrawBox(p.X - p.Size * 0.5f, p.Y - p.Size * 0.5f,
                    p.Size, p.Size, p.Color * alpha);
            }
        }

        private void RenderScanLine(int w, int h)
        {
            if (_hud == null) return;

            // Horizontal glowing scan line moving down
            float scanH = 2f;
            float scanAlpha = 0.08f * (1f - MathF.Abs(_scanLineY / h - 0.5f) * 1.5f);
            if (scanAlpha > 0f)
            {
                _hud.DrawBox(0, _scanLineY, w, scanH,
                    new Vector3(0.4f, 0.5f, 1.0f) * scanAlpha);

                // Glow trail above scan line
                for (int t = 1; t <= 6; t++)
                {
                    float trailAlpha = scanAlpha * 0.3f * (1f - t / 6f);
                    _hud.DrawBox(0, _scanLineY - t * 10f, w, scanH,
                        new Vector3(0.4f, 0.5f, 1.0f) * trailAlpha);
                }
            }
        }

        private void RenderGrid(int w, int h)
        {
            if (_hud == null) return;

            float gridSize = 40f;
            float gridAlpha = 0.04f;
            float pulse = 0.5f + 0.5f * MathF.Sin(_totalTime * 0.15f);
            gridAlpha *= (0.6f + 0.4f * pulse);

            // Vertical lines (animated horizontal offset)
            for (float x = _gridOffsetX; x < w; x += gridSize)
            {
                _hud.DrawBox(x, 0, 1f, h, new Vector3(0.3f, 0.4f, 0.8f) * gridAlpha);
            }

            // Horizontal lines
            for (float y = 0; y < h; y += gridSize)
            {
                _hud.DrawBox(0, y, w, 1f, new Vector3(0.3f, 0.4f, 0.8f) * gridAlpha);
            }
        }

        private void RenderVignette(int w, int h)
        {
            if (_hud == null) return;

            float edgeSize = 120f;
            float alpha = 0.6f;
            Vector3 color = new(0f, 0f, 0f);

            // Top
            _hud.DrawBox(0, 0, w, edgeSize, color * alpha * 0.7f);
            // Bottom
            _hud.DrawBox(0, h - edgeSize, w, edgeSize, color * alpha * 0.7f);
            // Left
            _hud.DrawBox(0, 0, edgeSize, h, color * alpha * 0.5f);
            // Right
            _hud.DrawBox(w - edgeSize, 0, edgeSize, h, color * alpha * 0.5f);

            // Corner accents (small bright lines in corners)
            float cornerSize = 60f;
            float cornerThick = 2f;
            Vector3 accentColor = new(0.3f, 0.4f, 0.8f);

            // Top-left
            _hud.DrawBox(20f, 20f, cornerSize, cornerThick, accentColor * 0.4f);
            _hud.DrawBox(20f, 20f, cornerThick, cornerSize, accentColor * 0.4f);
            // Top-right
            _hud.DrawBox(w - 20f - cornerSize, 20f, cornerSize, cornerThick, accentColor * 0.4f);
            _hud.DrawBox(w - 20f - cornerThick, 20f, cornerThick, cornerSize, accentColor * 0.4f);
            // Bottom-left
            _hud.DrawBox(20f, h - 20f - cornerThick, cornerSize, cornerThick, accentColor * 0.4f);
            _hud.DrawBox(20f, h - 20f - cornerSize, cornerThick, cornerSize, accentColor * 0.4f);
            // Bottom-right
            _hud.DrawBox(w - 20f - cornerSize, h - 20f - cornerThick, cornerSize, cornerThick, accentColor * 0.4f);
            _hud.DrawBox(w - 20f - cornerThick, h - 20f - cornerSize, cornerThick, cornerSize, accentColor * 0.4f);
        }

        // =====================================================
        //  UI RENDERERS
        // =====================================================

        private void RenderTitle(int w)
        {
            if (_hud == null) return;

            string title = "DARK ENGINE 3D";
            float titleY = 70f;

            // Title shadow
            _hud.DrawCenteredText(title, w, titleY + 2, new Vector3(0, 0, 0));

            // Title main — pulsing gold
            float pulse = 0.85f + 0.15f * MathF.Sin(_totalTime * 1.2f);
            _hud.DrawCenteredText(title, w, titleY, new Vector3(0.95f * pulse, 0.75f * pulse, 0.15f * pulse));

            // Decorative line under title
            float lineW = 200f + 40f * MathF.Sin(_totalTime * 0.5f);
            float lineX = (w - lineW) * 0.5f;
            float lineY = titleY + 42f;
            float lineAlpha = 0.3f + 0.2f * MathF.Sin(_totalTime * 0.7f);
            _hud.DrawBox(lineX, lineY, lineW, 1f, new Vector3(0.4f, 0.5f, 0.9f) * lineAlpha);

            // Subtitle
            string subtitle = "v1.0 — Made with ❤ in C# & OpenGL";
            _hud.DrawCenteredText(subtitle, w, titleY + 50f, new Vector3(0.5f, 0.5f, 0.6f));
        }

        private void RenderButtons(int w, int h)
        {
            if (_hud == null) return;

            float totalHeight = _menuItems.Length * ButtonHeight + (_menuItems.Length - 1) * ButtonSpacing;
            float startY = (h - totalHeight) * 0.5f;

            for (int i = 0; i < _menuItems.Length; i++)
            {
                float bx = (w - ButtonWidth) * 0.5f;
                float by = startY + i * (ButtonHeight + ButtonSpacing);
                bool isSelected = (i == _selectedIndex);

                // ── Pulsing glow behind selected button ──
                if (isSelected)
                {
                    float glowPulse = 0.5f + 0.5f * MathF.Sin(_totalTime * 2.5f);
                    float glowExpand = 10f + glowPulse * 6f;
                    float glowAlpha = 0.08f + glowPulse * 0.06f;
                    _hud.DrawBox(bx - glowExpand, by - glowExpand,
                        ButtonWidth + glowExpand * 2, ButtonHeight + glowExpand * 2,
                        new Vector3(0.3f, 0.4f, 0.9f) * glowAlpha);
                }

                // Button background
                Vector3 bgColor = isSelected
                    ? new Vector3(0.22f, 0.28f, 0.45f)
                    : new Vector3(0.10f, 0.12f, 0.18f);
                _hud.DrawBox(bx, by, ButtonWidth, ButtonHeight, bgColor);

                // Button borders (top and bottom accent lines)
                Vector3 borderColor = isSelected
                    ? new Vector3(0.5f, 0.6f, 1.0f)
                    : new Vector3(0.15f, 0.18f, 0.25f);
                _hud.DrawBox(bx, by, ButtonWidth, 1f, borderColor);
                _hud.DrawBox(bx, by + ButtonHeight - 1f, ButtonWidth, 1f, borderColor);

                // Selected: animated side bar
                if (isSelected)
                {
                    float barPulse = 0.7f + 0.3f * MathF.Sin(_totalTime * 3f);
                    _hud.DrawBox(bx - 3f, by + 4f, 3f, ButtonHeight - 8f,
                        new Vector3(0.4f, 0.5f, 0.9f) * barPulse);
                }

                // Button text — centered using GetTextExtents (single pass)
                Vector3 textColor = isSelected
                    ? new Vector3(0.95f, 0.95f, 1.0f)
                    : new Vector3(0.6f, 0.6f, 0.7f);
                var extents = _hud.GetTextExtents(_menuLabels[i]);
                float textX = bx + (ButtonWidth - extents.Width) * 0.5f;
                float textY = extents.GetCenteredBaselineY(by, ButtonHeight);
                _hud.DrawText(_menuLabels[i], textX, textY, textColor);

                // Selection indicator bar on the left (already drawn above)
            }
        }

        private void RenderSettings(int w, int h)
        {
            if (_hud == null) return;

            var lay = new SettingsLayout(w, h, _hud);
            float panelW = lay.PanelW, panelH = lay.PanelH;
            float panelX = lay.PanelX, panelY = lay.PanelY;
            float titleH = lay.TitleH, lineH = lay.LineH, lineGap = lay.LineGap;
            float dividerY = lay.DividerY, listStartY = lay.ListStartY;

            // Dim background overlay
            _hud.DrawBox(0, 0, w, h, new Vector3(0f, 0f, 0f) * 0.5f);

            // Panel background
            _hud.DrawBox(panelX, panelY, panelW, panelH, new Vector3(0.08f, 0.09f, 0.14f));

            // Top accent bar
            _hud.DrawBox(panelX, panelY, panelW, 2f, new Vector3(0.4f, 0.5f, 0.9f));
            // Bottom accent bar
            _hud.DrawBox(panelX, panelY + panelH - 2f, panelW, 2f, new Vector3(0.4f, 0.5f, 0.9f) * 0.5f);

            // Title
            float titleY = panelY + 20f;
            _hud.DrawText("SETTINGS", panelX + 20f, titleY, new Vector3(0.9f, 0.9f, 1.0f));

            // Divider — positioned based on title text height
            _hud.DrawBox(panelX + 20f, dividerY, panelW - 40f, 1f, new Vector3(0.2f, 0.22f, 0.3f));

            // Setting items — interactive, selectable, cycleable

            for (int i = 0; i < _settingLabels.Length; i++)
            {
                float itemY = listStartY + i * (lineH + lineGap);
                bool isSelected = (i == _settingsSelection);

                // Selected: highlight background + side bar
                if (isSelected)
                {
                    float pulse = 0.5f + 0.5f * MathF.Sin(_totalTime * 3f);
                    _hud.DrawBox(panelX + 20f, itemY - 2f, panelW - 40f, lineH + 4f,
                        new Vector3(0.3f, 0.4f, 0.9f) * (0.04f + pulse * 0.03f));
                    _hud.DrawBox(panelX + 18f, itemY + 1f, 2f, lineH - 2f,
                        new Vector3(0.4f, 0.5f, 0.9f));
                }

                // Label
                _hud.DrawText(_settingLabels[i], panelX + 30f, itemY,
                    isSelected ? new Vector3(0.9f, 0.9f, 1.0f) : new Vector3(0.6f, 0.6f, 0.75f));

                // Value (right-aligned) + cycle hint arrows
                if (i == _settingLabels.Length - 1)
                {
                    // ── APPLY button: separator + centered green button ──
                    // Separator line above Apply (always visible)
                    float sepY = itemY - lineGap * 0.5f;
                    _hud.DrawBox(panelX + 30f, sepY, panelW - 60f, 1f, new Vector3(0.2f, 0.22f, 0.25f));

                    // Full-width button highlight (green accent)
                    float btnPadX = 20f;
                    float btnPadY = 4f;
                    float btnW = panelW - btnPadX * 2f;
                    float btnH = lineH + btnPadY * 2f;
                    float btnX = panelX + btnPadX;
                    float btnY = itemY - btnPadY + 2f;

                    Vector3 btnBg = isSelected
                        ? new Vector3(0.15f, 0.50f, 0.20f)  // green when selected
                        : new Vector3(0.10f, 0.14f, 0.12f); // dark green bg
                    _hud.DrawBox(btnX, btnY, btnW, btnH, btnBg);

                    if (isSelected)
                    {
                        float pulse = 0.7f + 0.3f * MathF.Sin(_totalTime * 3f);
                        _hud.DrawBox(btnX, btnY, btnW, btnH, new Vector3(0.2f, 0.7f, 0.3f) * pulse * 0.12f);
                        // Left accent bar (green)
                        _hud.DrawBox(btnX + 2f, btnY + 3f, 2f, btnH - 6f, new Vector3(0.3f, 0.9f, 0.4f));
                        // Bottom border glow
                        _hud.DrawBox(btnX, btnY + btnH - 1f, btnW, 1f, new Vector3(0.3f, 0.9f, 0.4f) * 0.5f);
                    }
                    else
                    {
                        _hud.DrawBox(btnX, btnY, btnW, 1f, new Vector3(0.15f, 0.35f, 0.18f));
                        _hud.DrawBox(btnX, btnY + btnH - 1f, btnW, 1f, new Vector3(0.15f, 0.35f, 0.18f));
                    }

                    // Centered text
                    string applyLabel = isSelected ? "▶  APPLY SETTINGS  ◀" : "▶  APPLY SETTINGS";
                    var applyExt = _hud.GetTextExtents(applyLabel);
                    float applyX = panelX + (panelW - applyExt.Width) * 0.5f;
                    float applyY = applyExt.GetCenteredBaselineY(btnY, btnH);
                    _hud.DrawText(applyLabel, applyX, applyY,
                        isSelected ? new Vector3(0.5f, 1.0f, 0.5f) : new Vector3(0.5f, 0.75f, 0.5f));
                }
                else
                {
                    string value = _settingOptions[i][_settingValues[i]];
                    string display = isSelected
                        ? $"< {value} >"
                        : value;
                    var valExtents = _hud.GetTextExtents(display);
                    float valX = panelX + panelW - 30f - valExtents.Width;
                    _hud.DrawText(display, valX, itemY,
                        isSelected ? new Vector3(0.6f, 0.7f, 1.0f) : new Vector3(0.5f, 0.5f, 0.6f));
                }
            }

            // Back hint — positioned after last settings line
            float hintY = listStartY + _settingLabels.Length * (lineH + lineGap) + 14f;
            _hud.DrawText("Press ESC to go back", panelX + 20f, hintY, new Vector3(0.35f, 0.35f, 0.5f));
        }

        public void Exit()
        {
            Glfw.OnWindowResized -= OnWindowResized;
            Console.WriteLine("[MainMenu] Exited.");
        }

        public void Dispose()
        {
            _hud = null;
            Console.WriteLine("[MainMenu] Disposed.");
        }
    }
}
