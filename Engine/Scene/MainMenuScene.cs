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
        private HUD? _hudSmall;
        private float _deltaTime;
        private float _totalTime;

        // ── Menu state ──
        private enum MenuAction { Continue, LoadGame, StartGame, Settings, Exit }
        private MenuAction[] _menuItems = [MenuAction.StartGame, MenuAction.Settings, MenuAction.Exit];
        private string[] _menuLabels = ["START GAME", "SETTINGS", "EXIT"];
        private int _selectedIndex = 0;
        private int _menuLastHovered = -1; // only update selection from hover when this changes
        private bool _settingsOpen = false;

        // ── Random ──
        private readonly Random _rng = new();

        // ── Keyboard state (edge detection) ──
        private bool _upWasDown = false;
        private bool _downWasDown = false;
        private bool _enterWasDown = false;
        private bool _escapeWasDown = false;
        private bool _leftWasDown = false;
        private bool _rightWasDown = false;
        private bool _mouseWasDown = false;

        // ── Grid-based layout (responsive) ──
        private const int BtnColStart = 3;        // main menu buttons start column
        private const int BtnColEnd = 9;           // main menu buttons end column
        private const float ButtonHeight = 58f;
        private const float ButtonSpacing = 16f;

        // ── Interactive Settings ──
        private int _settingsSelection = 0;
        private int _settingsLastHoveredRow = -1; // only update selection from hover when this changes
        private bool _settingsUpWasDown = false;
        private bool _settingsDownWasDown = false;
        private bool _settingsEnterWasDown = false;
        private bool _settingsMouseWasDown = false;
        private bool _settingsEscapeWasDown = false;
        private bool _settingsLeftWasDown = false;
        private bool _settingsRightWasDown = false;

        // ── Snapshot for cancel / unsaved-changes detection ──
        private readonly int[] _savedSettingValues = [0, 0, 0, 3, 1, 0, 3, 0, 0];
        private bool _hasUnsavedChanges = false;

        // ── Toast notification ──
        private string _notificationText = "";
        private float _notificationTimer = 0f;
        private const float NotificationDuration = 3f;
        /// <summary>Optional message shown when this scene starts (e.g. "Settings saved!").</summary>
        private readonly string _startupNotification;

        // ── ESC confirm dialog (settings) ──
        private bool _confirmActive = false;
        private int _confirmSelection = 0;
        private int _confirmLastHovered = -1; // only update confirm selection from hover when this changes
        private bool _confirmUpWasDown = false;
        private bool _confirmDownWasDown = false;
        private bool _confirmLeftWasDown = false;
        private bool _confirmRightWasDown = false;
        private bool _confirmEnterWasDown = false;
        private bool _confirmEscapeWasDown = false;
        private bool _confirmMouseWasDown = false;

        // ── Load Game overlay ──
        private bool _loadGameActive = false;
        private int _loadGameSelection = 0;
        private bool _loadGameUpWasDown = false;
        private bool _loadGameDownWasDown = false;
        private bool _loadGameEnterWasDown = false;
        private bool _loadGameEscapeWasDown = false;
        private bool _loadGameLeftWasDown = false;
        private bool _loadGameRightWasDown = false;
        private bool _loadGameMouseWasDown = false;
        private SaveSlotInfo[] _loadSlots = new SaveSlotInfo[SaveManager.NumSlots];

        // ── Exit confirm dialog (main menu) ──
        private bool _exitConfirmActive = false;
        private int _exitConfirmSelection = 0; // 0 = Cancel, 1 = Yes
        private int _exitConfirmLastHovered = -1;
        private bool _exitConfirmLeftWasDown = false;
        private bool _exitConfirmRightWasDown = false;
        private bool _exitConfirmEnterWasDown = false;
        private bool _exitConfirmEscapeWasDown = false;
        private bool _exitConfirmMouseWasDown = false;

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
            "CANCEL",
        ];

        private readonly string[][] _settingOptions =
        [
            [Resolutions[0].Label, Resolutions[1].Label, Resolutions[2].Label],
            ["OFF", "ON"],
            ["OFF", "ON"],
            ["LOW", "MEDIUM", "HIGH", "ULTRA"],
            ["Software", "HiZ", "OFF"],
            ["60", "70", "80", "90", "100", "110"],
            ["0.25×", "0.50×", "0.75×", "1.0×", "1.5×", "2.0×", "3.0×"],
            [""],   // Apply button
            [""],   // Cancel button
        ];

        // Current value index for each setting (last 2 = 0 always for Apply/Cancel)
        private readonly int[] _settingValues = [0, 0, 0, 3, 1, 0, 3, 0, 0];
        // 0=Res,1=FS,2=VSync,3=Shadow,4=OC,5=FOV,6=Mouse,7=Apply,8=Cancel

        // ── Shared lookup tables (avoid duplication) ──
        private readonly struct ResInfo
        {
            public readonly int Width, Height;
            public readonly string Label, CompactLabel;
            public ResInfo(int w, int h, string label)
            {
                Width = w; Height = h;
                Label = label;
                CompactLabel = $"{w}x{h}";
            }
        }

        private static readonly ResInfo[] Resolutions =
        [
            new(1920, 1080, "1920 x 1080"),
            new(1280, 720,  "1280 x 720"),
            new(2560, 1440, "2560 x 1440"),
        ];
        // CascadePresets shared via ShadowPresets utility class

        // ── Settings panel layout (shared between Update hover + RenderSettings) ──
        private const int SettingsColStart = 2;
        private const int SettingsColEnd = 10;
        private const float SettingsPanelH = 540f;

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
                var grid = new GridLayout(screenW, screenH);
                PanelW = grid.SpanW(SettingsColStart, SettingsColEnd);
                PanelH = SettingsPanelH;
                PanelX = grid.ColX(SettingsColStart);
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

        public MainMenuScene(SceneManager sceneManager, Camera camera, Lights light,
            string startupNotification = "")
        {
            _sceneManager = sceneManager;
            _camera = camera;
            _light = light;
            _startupNotification = startupNotification;
        }

        public void Enter()
        {
            nint window = Glfw.GetWindow();

            _hud = new HUD("Artifacts\\fonts\\Worldstar.ttf", 28.0f);
            _hudSmall = new HUD("Artifacts\\fonts\\Worldstar.ttf", 16.0f);
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
            _leftWasDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_A);
            _rightWasDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_D);
            _mouseWasDown = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);
            _settingsUpWasDown = _upWasDown;
            _settingsDownWasDown = _downWasDown;
            _settingsEnterWasDown = _enterWasDown;
            _settingsEscapeWasDown = _escapeWasDown;
            _settingsLeftWasDown = _leftWasDown;
            _settingsRightWasDown = _rightWasDown;
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

            // ── Save snapshot for cancel detection ──
            Array.Copy(_settingValues, _savedSettingValues, _settingValues.Length);
            _hasUnsavedChanges = false;
            _confirmActive = false;
            _confirmLastHovered = -1;
            _exitConfirmActive = false;
            _exitConfirmSelection = 0;
            _exitConfirmLastHovered = -1;
            _menuLastHovered = -1;
            _settingsLastHoveredRow = -1;
            _loadGameActive = false;
            _loadGameSelection = 0;
            _notificationText = "";

            // ── Show startup notification if provided (e.g. "In-game settings saved")
            if (!string.IsNullOrEmpty(_startupNotification))
            {
                ShowNotification(_startupNotification);
            }

            // ── Build menu based on existing saves ──
            BuildMenu();

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

            // ── Update notification timer ──
            if (_notificationTimer > 0f)
                _notificationTimer -= deltaTime;

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

            // Calculate button positions (centered, responsive width)
            var btnGrid = new GridLayout(Glfw.WindowWidth, Glfw.WindowHeight);
            float btnWidth = btnGrid.SpanW(BtnColStart, BtnColEnd);
            float totalHeight = _menuItems.Length * ButtonHeight + (_menuItems.Length - 1) * ButtonSpacing;
            float startY = (Glfw.WindowHeight - totalHeight) * 0.5f;

            // ── Mouse hover detection — only for main menu buttons when settings closed ──
            int hoveredIndex = -1;
            if (!_settingsOpen)
            {
                float bx = (Glfw.WindowWidth - btnWidth) * 0.5f;
                for (int i = 0; i < _menuItems.Length; i++)
                {
                    float by = startY + i * (ButtonHeight + ButtonSpacing);
                    if (mouseX >= bx && mouseX <= bx + btnWidth &&
                        mouseY >= by && mouseY <= by + ButtonHeight)
                    {
                        hoveredIndex = i;
                        break;
                    }
                }
            }

            // ── Only update main menu selection from hover when no confirm dialog is blocking ──
            if (hoveredIndex >= 0 && hoveredIndex != _menuLastHovered && !_exitConfirmActive)
                _selectedIndex = hoveredIndex;
            _menuLastHovered = hoveredIndex;

            // ── Load Game overlay handling ──
            if (_loadGameActive)
            {
                HandleLoadGameInput(window);
                return;
            }

            // ── Mouse click — only for main menu when no confirm dialog is active ──
            if (mousePressed && !_mouseWasDown)
            {
                _mouseWasDown = true;
                if (hoveredIndex >= 0 && !_settingsOpen && !_exitConfirmActive)
                {
                    ExecuteMenuAction(_menuItems[hoveredIndex]);
                }
            }
            if (!mousePressed)
                _mouseWasDown = false;

            // ── Keyboard navigation ──
            bool upDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_W);
            bool downDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);
            bool leftDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_A);
            bool rightDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_D);
            bool enterDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE);
            bool escapeDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);

            if (_settingsOpen)
            {
                // ── Confirm dialog is active ──
                if (_confirmActive)
                {
                    // Up/Down/Left/Right to navigate
                    if ((upDown && !_confirmUpWasDown) || (leftDown && !_confirmLeftWasDown))
                        _confirmSelection = (_confirmSelection - 1 + 2) % 2;
                    if ((downDown && !_confirmDownWasDown) || (rightDown && !_confirmRightWasDown))
                        _confirmSelection = (_confirmSelection + 1) % 2;

                    // Mouse hover on dialog buttons — only update when hovering a different button
                    {
                        int hoveredConfirm = -1;
                        for (int b = 0; b < 2; b++)
                        {
                            ConfirmDialog.GetButtonRect(Glfw.WindowWidth, Glfw.WindowHeight, 1.0f, b,
                                out float bx, out float by, out float bw, out float bh);
                            if (mouseX >= bx && mouseX <= bx + bw &&
                                mouseY >= by && mouseY <= by + bh)
                            {
                                hoveredConfirm = b;
                                break;
                            }
                        }
                        if (hoveredConfirm >= 0 && hoveredConfirm != _confirmLastHovered)
                            _confirmSelection = hoveredConfirm;
                        _confirmLastHovered = hoveredConfirm;
                    }

                    // ESC → cancel (keep editing)
                    if (escapeDown && !_confirmEscapeWasDown)
                        _confirmActive = false;

                    // Enter/Space to confirm
                    bool confirmActivate = (enterDown && !_confirmEnterWasDown);
                    if (confirmActivate)
                    {
                        if (_confirmSelection == 0) // Discard
                        {
                            // Reset to snapshot and close
                            Array.Copy(_savedSettingValues, _settingValues, _settingValues.Length);
                            _hasUnsavedChanges = false;
                            _confirmActive = false;
                            _settingsOpen = false;
                            ApplyCurrentSettingsImmediate(); // re-apply old values
                        }
                        else // Keep editing
                        {
                            _confirmActive = false;
                        }
                    }

                    // Mouse click on dialog buttons
                    if (mousePressed && !_confirmMouseWasDown)
                    {
                        _confirmMouseWasDown = true;
                        for (int b = 0; b < 2; b++)
                        {
                            ConfirmDialog.GetButtonRect(Glfw.WindowWidth, Glfw.WindowHeight, 1.0f, b,
                                out float bx, out float by, out float bw, out float bh);
                            if (mouseX >= bx && mouseX <= bx + bw &&
                                mouseY >= by && mouseY <= by + bh)
                            {
                                _confirmSelection = b;
                                if (b == 0) // Discard
                                {
                                    Array.Copy(_savedSettingValues, _settingValues, _settingValues.Length);
                                    _hasUnsavedChanges = false;
                                    _confirmActive = false;
                                    _settingsOpen = false;
                                    ApplyCurrentSettingsImmediate();
                                }
                                else // Keep editing
                                {
                                    _confirmActive = false;
                                }
                                break;
                            }
                        }
                    }
                    if (!mousePressed)
                        _confirmMouseWasDown = false;

                    _confirmUpWasDown = upDown;
                    _confirmDownWasDown = downDown;
                    _confirmLeftWasDown = leftDown;
                    _confirmRightWasDown = rightDown;
                    _confirmEnterWasDown = enterDown;
                    _confirmEscapeWasDown = escapeDown;
                    _mouseWasDown = mousePressed;

                    // Still update main-menu flags to prevent bleed
                    _upWasDown = upDown;
                    _downWasDown = downDown;
                    _leftWasDown = leftDown;
                    _rightWasDown = rightDown;
                    _enterWasDown = enterDown;
                    _escapeWasDown = escapeDown;
                    return;
                }

                // ── ESC → confirm if dirty, else close ──
                if (escapeDown && !_settingsEscapeWasDown)
                {
                    if (_hasUnsavedChanges)
                    {
                        _confirmActive = true;
                        _confirmSelection = 1; // default to "Keep editing"
                        nint win = Glfw.GetWindow();
                        _confirmLeftWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_A);
                        _confirmRightWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_D);
                        _confirmUpWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_W);
                        _confirmDownWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_S);
                        _confirmEnterWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_SPACE);
                        _confirmEscapeWasDown = escapeDown;
                        _confirmMouseWasDown = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);
                    }
                    else
                    {
                        _settingsOpen = false;
                    }
                }

                // ── Mouse hover in settings panel — only update selection when hovering a different row ──
                if (_hud != null)
                {
                    var lay = new SettingsLayout(Glfw.WindowWidth, Glfw.WindowHeight, _hud);
                    int hoveredRow = -1;
                    for (int i = 0; i < _settingLabels.Length; i++)
                    {
                        float itemY = lay.ListStartY + i * (lay.LineH + lay.LineGap);
                        if (mouseX >= lay.PanelX && mouseX <= lay.PanelX + lay.PanelW &&
                            mouseY >= itemY && mouseY < itemY + lay.LineH + lay.LineGap)
                        {
                            hoveredRow = i;
                            break;
                        }
                    }
                    if (hoveredRow >= 0 && hoveredRow != _settingsLastHoveredRow)
                    {
                        _settingsSelection = hoveredRow;
                    }
                    _settingsLastHoveredRow = hoveredRow;
                }

                if (upDown && !_settingsUpWasDown)
                    _settingsSelection = (_settingsSelection - 1 + _settingLabels.Length) % _settingLabels.Length;
                if (downDown && !_settingsDownWasDown)
                    _settingsSelection = (_settingsSelection + 1) % _settingLabels.Length;

                // LEFT/RIGHT → cycle selected setting backward/forward
                if (leftDown && !_settingsLeftWasDown)
                    CycleSetting(_settingsSelection, -1);
                if (rightDown && !_settingsRightWasDown)
                    CycleSetting(_settingsSelection, 1);

                // Enter/Space/Click → cycle current setting forward
                bool settingsActivate = (enterDown && !_settingsEnterWasDown) || (mousePressed && !_settingsMouseWasDown);
                if (settingsActivate)
                {
                    CycleSetting(_settingsSelection, 1);
                }

                _settingsUpWasDown = upDown;
                _settingsDownWasDown = downDown;
                _settingsLeftWasDown = leftDown;
                _settingsRightWasDown = rightDown;
                _settingsEnterWasDown = enterDown;
                _settingsEscapeWasDown = escapeDown;
                if (!mousePressed) _settingsMouseWasDown = false;
                if (mousePressed && !_settingsMouseWasDown) _settingsMouseWasDown = true;

                // Don't process main menu navigation when settings is open
                _upWasDown = upDown;
                _downWasDown = downDown;
                _enterWasDown = enterDown;
                _escapeWasDown = escapeDown;
                _leftWasDown = leftDown;
                _rightWasDown = rightDown;
                return;
            }
            else
            {
                // ── Exit confirm dialog ──
                if (_exitConfirmActive)
                {
                    // Mouse hover on dialog buttons — only update when hovering a different button
                    {
                        int hoveredExit = -1;
                        for (int b = 0; b < 2; b++)
                        {
                            ConfirmDialog.GetButtonRect(Glfw.WindowWidth, Glfw.WindowHeight, 1.0f, b,
                                out float bx, out float by, out float bw, out float bh);
                            if (mouseX >= bx && mouseX <= bx + bw &&
                                mouseY >= by && mouseY <= by + bh)
                            {
                                hoveredExit = b;
                                break;
                            }
                        }
                        if (hoveredExit >= 0 && hoveredExit != _exitConfirmLastHovered)
                            _exitConfirmSelection = hoveredExit;
                        _exitConfirmLastHovered = hoveredExit;
                    }

                    // Left/Right to navigate
                    if (leftDown && !_exitConfirmLeftWasDown)
                        _exitConfirmSelection = 0;
                    if (rightDown && !_exitConfirmRightWasDown)
                        _exitConfirmSelection = 1;

                    // Enter/Space to confirm
                    if (enterDown && !_exitConfirmEnterWasDown)
                    {
                        if (_exitConfirmSelection == 1) // Yes → exit
                        {
                            Console.WriteLine("[MainMenu] Exiting...");
                            _sceneManager.Stop();
                        }
                        else // Cancel
                        {
                            _exitConfirmActive = false;
                        }
                    }

                    // ESC → cancel
                    if (escapeDown && !_exitConfirmEscapeWasDown)
                        _exitConfirmActive = false;

                    // Mouse click
                    if (mousePressed && !_exitConfirmMouseWasDown)
                    {
                        _exitConfirmMouseWasDown = true;
                        for (int b = 0; b < 2; b++)
                        {
                            ConfirmDialog.GetButtonRect(Glfw.WindowWidth, Glfw.WindowHeight, 1.0f, b,
                                out float bx, out float by, out float bw, out float bh);
                            if (mouseX >= bx && mouseX <= bx + bw &&
                                mouseY >= by && mouseY <= by + bh)
                            {
                                if (b == 1) // Yes → exit
                                {
                                    Console.WriteLine("[MainMenu] Exiting...");
                                    _sceneManager.Stop();
                                }
                                else // Cancel
                                {
                                    _exitConfirmActive = false;
                                }
                                break;
                            }
                        }
                    }
                    if (!mousePressed)
                        _exitConfirmMouseWasDown = false;

                    _exitConfirmLeftWasDown = leftDown;
                    _exitConfirmRightWasDown = rightDown;
                    _exitConfirmEnterWasDown = enterDown;
                    _exitConfirmEscapeWasDown = escapeDown;

                    _upWasDown = upDown;
                    _downWasDown = downDown;
                    _enterWasDown = enterDown;
                    _escapeWasDown = escapeDown;
                    _leftWasDown = leftDown;
                    _rightWasDown = rightDown;
                    return;
                }

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
            _leftWasDown = leftDown;
            _rightWasDown = rightDown;
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

        /// <summary>Build the main menu item list — NEW GAME always shown.</summary>
        private void BuildMenu()
        {
            if (SaveManager.HasAnySave())
            {
                _menuItems = [MenuAction.Continue, MenuAction.StartGame, MenuAction.LoadGame, MenuAction.Settings, MenuAction.Exit];
                _menuLabels = ["CONTINUE", "NEW GAME", "LOAD GAME", "SETTINGS", "EXIT"];
            }
            else
            {
                _menuItems = [MenuAction.StartGame, MenuAction.Settings, MenuAction.Exit];
                _menuLabels = ["NEW GAME", "SETTINGS", "EXIT"];
            }
            _selectedIndex = 0;
        }

        private void ExecuteMenuAction(MenuAction action)
        {
            switch (action)
            {
                case MenuAction.Continue:
                {
                    int latestSlot = SaveManager.GetLatestSlot();
                    if (latestSlot >= 0)
                    {
                        Console.WriteLine($"[MainMenu] Continuing from slot {latestSlot}...");
                        GameScene.PendingLoadSlot = latestSlot;
                        LoadingScene loadingScene = new(_sceneManager, _camera, _light);
                        _sceneManager.SwitchScene(loadingScene);
                    }
                    break;
                }

                case MenuAction.LoadGame:
                    OpenLoadGameUI();
                    break;

                case MenuAction.StartGame:
                {
                    Console.WriteLine("[MainMenu] Starting new game...");
                    GameScene.PendingLoadSlot = -1;
                    var loadingSceneNew = new LoadingScene(_sceneManager, _camera, _light);
                    _sceneManager.SwitchScene(loadingSceneNew);
                    break;
                }

                case MenuAction.Settings:
                    _settingsOpen = true;
                    Console.WriteLine("[MainMenu] Settings opened.");
                    // Sync edge-tracking flags to prevent input bleed
                    nint settingsWin = Glfw.GetWindow();
                    _settingsUpWasDown = Keyboard.IsKeyDown(settingsWin, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(settingsWin, Const.GLFW_KEY_W);
                    _settingsDownWasDown = Keyboard.IsKeyDown(settingsWin, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(settingsWin, Const.GLFW_KEY_S);
                    _settingsLeftWasDown = Keyboard.IsKeyDown(settingsWin, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(settingsWin, Const.GLFW_KEY_A);
                    _settingsRightWasDown = Keyboard.IsKeyDown(settingsWin, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(settingsWin, Const.GLFW_KEY_D);
                    _settingsEnterWasDown = Keyboard.IsKeyDown(settingsWin, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(settingsWin, Const.GLFW_KEY_SPACE);
                    _settingsEscapeWasDown = Keyboard.IsKeyDown(settingsWin, Const.GLFW_KEY_ESCAPE);
                    _settingsMouseWasDown = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);
                    break;

                case MenuAction.Exit:
                    _exitConfirmActive = true;
                    _exitConfirmSelection = 0; // default to Cancel
                    // Prevent held inputs from immediately confirming the dialog
                    nint exitWindow = Glfw.GetWindow();
                    _exitConfirmLeftWasDown = Keyboard.IsKeyDown(exitWindow, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(exitWindow, Const.GLFW_KEY_A);
                    _exitConfirmRightWasDown = Keyboard.IsKeyDown(exitWindow, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(exitWindow, Const.GLFW_KEY_D);
                    _exitConfirmEnterWasDown = Keyboard.IsKeyDown(exitWindow, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(exitWindow, Const.GLFW_KEY_SPACE);
                    _exitConfirmEscapeWasDown = Keyboard.IsKeyDown(exitWindow, Const.GLFW_KEY_ESCAPE);
                    _exitConfirmMouseWasDown = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);
                    break;

            }
        }

        /// <summary>Open the Load Game overlay with save slot selection.</summary>
        private void OpenLoadGameUI()
        {
            _loadGameActive = true;
            _loadGameSelection = 0;

            // Refresh slot info and load thumbnails
            _loadSlots = SaveManager.GetAllSlots();
            for (int i = 0; i < _loadSlots.Length; i++)
            {
                if (_loadSlots[i].HasData)
                    SaveManager.GetOrLoadThumbnail(ref _loadSlots[i], SaveSlotUI.ThumbW, SaveSlotUI.ThumbH);
            }

            // Sync edge-tracking flags
            nint win = Glfw.GetWindow();
            _loadGameUpWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_W);
            _loadGameDownWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_S);
            _loadGameEnterWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_SPACE);
            _loadGameEscapeWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_ESCAPE);
            _loadGameLeftWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_A);
            _loadGameRightWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_D);
            _loadGameMouseWasDown = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);

            Console.WriteLine("[MainMenu] Load Game UI opened");
        }

        /// <summary>Start the game and load a specific save slot.</summary>
        private void StartGameWithLoad(int slotIndex)
        {
            Console.WriteLine($"[MainMenu] Starting game with load from slot {slotIndex}...");
            GameScene.PendingLoadSlot = slotIndex;
            LoadingScene loadingScene = new(_sceneManager, _camera, _light);
            _sceneManager.SwitchScene(loadingScene);
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
            Config.ShadowConfig.CascadeSizes = Config.ShadowPresets.CascadeSizes[sq];

            // ── Apply OC Mode ──
            ApplyOcclusionMode(oc);

            // ── Live apply FOV ──
            _camera.BaseFoV = fovVal;
            _camera.FoV = fovVal;

            // ── Live apply Mouse Sensitivity ──
            ApplyMouseSensitivity(_settingValues[6]);

            // ── Live apply Resolution, VSync, Fullscreen ──

            Glfw.SetWindowSize(Resolutions[res].Width, Resolutions[res].Height);
            Glfw.SetSwapInterval(vs ? 1 : 0);
            Glfw.SetFullscreen(fs);
            Glfw.SetWindowPosition(0,0);

            Console.WriteLine($"[Settings] Applied: Resolution={Resolutions[res].CompactLabel}, Fullscreen={fs}, VSync={vs}, ShadowQuality={sq}, OC={oc}, FOV={fovVal}, MouseSens={_settingOptions[6][_settingValues[6]]}");
        }

        /// <summary>Re-apply the current _settingValues to render state (no save to JSON).
        /// Used after cancel to restore the old values from snapshot.</summary>
        private void ApplyCurrentSettingsImmediate()
        {
            int res = _settingValues[0];
            bool fs = _settingValues[1] == 1;
            bool vs = _settingValues[2] == 1;
            int sq = _settingValues[3];
            int oc = _settingValues[4];
            int fovVal = int.Parse(_settingOptions[5][_settingValues[5]]);

            Config.ShadowConfig.CascadeSizes = Config.ShadowPresets.CascadeSizes[sq];
            ApplyOcclusionMode(oc);
            _camera.BaseFoV = fovVal;
            _camera.FoV = fovVal;
            ApplyMouseSensitivity(_settingValues[6]);
            Glfw.SetWindowSize(Resolutions[res].Width, Resolutions[res].Height);
            Glfw.SetSwapInterval(vs ? 1 : 0);
            Glfw.SetFullscreen(fs);
            Glfw.SetWindowPosition(0, 0);
        }

        /// <summary>Cycle the given setting forward or backward — no live apply.
        /// Changes are only committed when the user presses APPLY & SAVE (index 7).
        /// Use direction = 1 for next, -1 for previous.</summary>
        private void CycleSetting(int index, int direction = 1)
        {
            // ── APPLY & SAVE button — execute, don't cycle ──
            if (index == 7)
            {
                ApplySettings();
                // Update snapshot after apply so cancel uses new saved values
                Array.Copy(_settingValues, _savedSettingValues, _settingValues.Length);
                _hasUnsavedChanges = false;
                ShowNotification("Settings applied!");
                return;
            }

            // ── CANCEL button — discard changes ──
            if (index == 8)
            {
                CancelSettings();
                return;
            }

            // Cycle the value index — no live update, no auto-save.
            int count = _settingOptions[index].Length;
            _settingValues[index] = (_settingValues[index] + direction + count) % count;
            int val = _settingValues[index];

            // Track dirty state
            _hasUnsavedChanges = HasChanges();
            Console.WriteLine($"[Settings] {_settingLabels[index]} → {_settingOptions[index][val]} (pending)");
        }

        /// <summary>Handle the Load Game overlay input (grid-aligned).</summary>
        private void HandleLoadGameInput(nint window)
        {
            Mouse.GetCursorPosition(out double mouseX, out double mouseY);
            bool mousePressed = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);

            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;

            SaveSlotUI.GetPanelRect(w, h, out float panelX, out float panelW,
                out float panelY, out float panelH, out float startY);

            float bx = panelX + GridLayout.Gutter * 0.5f;
            float bw = panelW - GridLayout.Gutter;

            // ── Mouse hover ──
            int hovered = -1;
            for (int i = 0; i < SaveManager.NumSlots; i++)
            {
                float sy = startY + i * (SaveSlotUI.SlotRowH + SaveSlotUI.SlotGap);
                if (mouseX >= bx && mouseX <= bx + bw &&
                    mouseY >= sy && mouseY <= sy + SaveSlotUI.SlotRowH)
                {
                    hovered = i;
                    break;
                }
            }
            if (hovered >= 0)
                _loadGameSelection = hovered;

            // ── Mouse click ──
            if (mousePressed && !_loadGameMouseWasDown)
            {
                _loadGameMouseWasDown = true;
                if (hovered >= 0 && _loadSlots[hovered].HasData)
                {
                    StartGameWithLoad(hovered);
                }
            }
            if (!mousePressed)
                _loadGameMouseWasDown = false;

            // ── Keyboard navigation ──
            bool upDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_W);
            bool downDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);
            bool enterDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE);
            bool escDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);
            bool leftDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_A);
            bool rightDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_D);

            if (upDown && !_loadGameUpWasDown)
                _loadGameSelection = (_loadGameSelection - 1 + SaveManager.NumSlots) % SaveManager.NumSlots;
            if (downDown && !_loadGameDownWasDown)
                _loadGameSelection = (_loadGameSelection + 1) % SaveManager.NumSlots;

            if (enterDown && !_loadGameEnterWasDown)
            {
                if (_loadSlots[_loadGameSelection].HasData)
                    StartGameWithLoad(_loadGameSelection);
            }

            if (escDown && !_loadGameEscapeWasDown)
            {
                _loadGameActive = false;
            }

            if ((leftDown && !_loadGameLeftWasDown) || (rightDown && !_loadGameRightWasDown))
            {
                int dir = (leftDown && !_loadGameLeftWasDown) ? -1 : 1;
                _loadGameSelection = (_loadGameSelection + dir + SaveManager.NumSlots) % SaveManager.NumSlots;
            }

            _loadGameUpWasDown = upDown;
            _loadGameDownWasDown = downDown;
            _loadGameEnterWasDown = enterDown;
            _loadGameEscapeWasDown = escDown;
            _loadGameLeftWasDown = leftDown;
            _loadGameRightWasDown = rightDown;

            _escapeWasDown = escDown;
        }

        /// <summary>Show a toast notification that auto-fades after NotificationDuration.</summary>
        private void ShowNotification(string text)
        {
            _notificationText = text;
            _notificationTimer = NotificationDuration;
        }

        /// <summary>Reset settings to the saved snapshot and close.</summary>
        private void CancelSettings()
        {
            Array.Copy(_savedSettingValues, _settingValues, _settingValues.Length);
            _hasUnsavedChanges = false;
            _settingsOpen = false;
            ApplyCurrentSettingsImmediate();
            Console.WriteLine("[Settings] Cancelled — reverted to saved values.");
        }

        /// <summary>Compare current values with saved snapshot to detect unsaved changes.</summary>
        private bool HasChanges()
        {
            for (int i = 0; i < _settingValues.Length; i++)
                if (_settingValues[i] != _savedSettingValues[i])
                    return true;
            return false;
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

            RenderTitle(w, h);

            // ── Toast notification (shown on main screen, above everything except exit confirm) ──
            if (_notificationTimer > 0f && !_settingsOpen)
            {
                float fadeAlpha = Math.Min(1f, _notificationTimer);
                string notifText = _notificationText;
                var notifExt = _hud.GetTextExtents(notifText);
                float notifX = w * 0.5f - notifExt.Width * 0.5f;
                float notifY = h * 0.20f;
                _hud.DrawText(notifText, notifX, notifY,
                    new Vector3(0.3f, 0.9f, 0.4f) * fadeAlpha);
            }

            // ── Load Game overlay ──
            if (_loadGameActive)
            {
                RenderLoadGamePanel(w, h);
                return;
            }

            // ── Exit confirm dialog (rendered before settings check) ──
            if (_exitConfirmActive)
            {
                RenderExitConfirmDialog(w, h);
                return;
            }

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
            var grid = new GridLayout(w, h);
            float hintCenterX = grid.CenterX(2, 10);
            var hintExt = _hud.GetTextExtents(hint);
            _hud.DrawText(hint, hintCenterX - hintExt.Width * 0.5f, hintY, new Vector3(0.35f, 0.35f, 0.45f));

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

        private void RenderTitle(int w, int h)
        {
            if (_hud == null) return;

            var grid = new GridLayout(w, h);
            float titleCenterX = grid.CenterX(2, 10);

            string title = "DARK ENGINE 3D";
            float titleY = 70f;

            // Title shadow
            var titleExt = _hud.GetTextExtents(title);
            _hud.DrawText(title, titleCenterX - titleExt.Width * 0.5f, titleY + 2, new Vector3(0, 0, 0));

            // Title main — pulsing gold
            float pulse = 0.85f + 0.15f * MathF.Sin(_totalTime * 1.2f);
            _hud.DrawText(title, titleCenterX - titleExt.Width * 0.5f, titleY, new Vector3(0.95f * pulse, 0.75f * pulse, 0.15f * pulse));

            // Decorative line under title — centered in grid span
            float lineW = 200f + 40f * MathF.Sin(_totalTime * 0.5f);
            float lineX = titleCenterX - lineW * 0.5f;
            float lineY = titleY + 42f;
            float lineAlpha = 0.3f + 0.2f * MathF.Sin(_totalTime * 0.7f);
            _hud.DrawBox(lineX, lineY, lineW, 1f, new Vector3(0.4f, 0.5f, 0.9f) * lineAlpha);

            // Subtitle
            string subtitle = "v1.0 - Made with love in C# & OpenGL";
            var subExt = _hud.GetTextExtents(subtitle);
            _hud.DrawText(subtitle, titleCenterX - subExt.Width * 0.5f, titleY + 50f, new Vector3(0.5f, 0.5f, 0.6f));
        }



        /// <summary>Render the Load Game overlay with save slot selection (grid-aligned).</summary>
        private void RenderLoadGamePanel(int w, int h)
        {
            if (_hud == null) return;

            var grid = new GridLayout(w, h);

            SaveSlotUI.GetPanelRect(w, h, out float panelX, out float panelW,
                out float panelY, out float panelH, out float startY);

            SaveSlotUI.RenderPanelFrame(_hud, w, h, "LOAD GAME", _loadSlots, panelX, panelW, panelY, panelH);
            SaveSlotUI.RenderSlots(_hudSmall, w, h, _loadSlots, _loadGameSelection, panelX, panelW, startY, "LOAD");

            string hint = "Select a slot to load  -  Enter to confirm  -  Esc to go back";
            float hintY = panelY + panelH - 10f;
            var hintExt = _hud.GetTextExtents(hint);
            float hintCenterX = grid.CenterX(SaveSlotUI.PanelColStart, SaveSlotUI.PanelColEnd);
            float hintX = hintCenterX - hintExt.Width * 0.5f;
            _hud.DrawText(hint, hintX, hintY, new Vector3(0.35f, 0.35f, 0.45f));
        }

        private void RenderExitConfirmDialog(int w, int h)
        {
            if (_hud == null) return;

            ConfirmDialog.DrawBox(_hud, w, h,
                "Exit Game?", "Are you sure you want to exit?",
                ["CANCEL", "YES"],
                [new Vector3(0.22f, 0.28f, 0.45f), new Vector3(0.6f, 0.2f, 0.15f)],
                [new Vector3(0.5f, 0.6f, 1.0f), new Vector3(1.0f, 0.4f, 0.3f)],
                [new Vector3(0.7f, 0.7f, 0.9f), new Vector3(1.0f, 0.5f, 0.4f)],
                _exitConfirmSelection);
        }

        private void RenderButtons(int w, int h)
        {
            if (_hud == null) return;

            var btnGrid = new GridLayout(w, h);
            float btnWidth = btnGrid.SpanW(BtnColStart, BtnColEnd);
            float totalHeight = _menuItems.Length * ButtonHeight + (_menuItems.Length - 1) * ButtonSpacing;
            float startY = (h - totalHeight) * 0.5f;

            for (int i = 0; i < _menuItems.Length; i++)
            {
                float bx = (w - btnWidth) * 0.5f;
                float by = startY + i * (ButtonHeight + ButtonSpacing);
                bool isSelected = (i == _selectedIndex);

                //// ── Pulsing glow behind selected button ──
                //if (isSelected)
                //{
                //    float glowPulse = 0.5f + 0.5f * MathF.Sin(_totalTime * 2.5f);
                //    float glowExpand = 10f + glowPulse * 6f;
                //    float glowAlpha = 0.08f + glowPulse * 0.06f;
                //    _hud.DrawBox(bx - glowExpand, by - glowExpand,
                //        btnWidth + glowExpand * 2, ButtonHeight + glowExpand * 2,
                //        new Vector3(0.3f, 0.4f, 0.9f) * glowAlpha);
                //}

                // Button background
                Vector3 bgColor = isSelected
                    ? new Vector3(0.22f, 0.28f, 0.45f)
                    : new Vector3(0.10f, 0.12f, 0.18f);
                _hud.DrawBox(bx, by, btnWidth, ButtonHeight, bgColor);

                //// Button borders (top and bottom accent lines)
                //Vector3 borderColor = isSelected
                //    ? new Vector3(0.5f, 0.6f, 1.0f)
                //    : new Vector3(0.15f, 0.18f, 0.25f);
                //_hud.DrawBox(bx, by, btnWidth, 1f, borderColor);
                //_hud.DrawBox(bx, by + ButtonHeight - 1f, btnWidth, 1f, borderColor);

                //// Selected: animated side bar
                //if (isSelected)
                //{
                //    float barPulse = 0.7f + 0.3f * MathF.Sin(_totalTime * 3f);
                //    _hud.DrawBox(bx - 3f, by + 4f, 3f, ButtonHeight - 8f,
                //        new Vector3(0.4f, 0.5f, 0.9f) * barPulse);
                //} 
                // Button text — centered using GetTextExtents (single pass)
                Vector3 textColor = isSelected
                    ? new Vector3(0.95f, 0.95f, 1.0f)
                    : new Vector3(0.6f, 0.6f, 0.7f);
                var extents = _hud.GetTextExtents(_menuLabels[i]);
                float textX = bx + (btnWidth - extents.Width) * 0.5f;
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

            // Title with dirty indicator
            string titleText = _hasUnsavedChanges ? "SETTINGS  *" : "SETTINGS";
            float titleY = panelY + 40f;
            _hud.DrawText(titleText, panelX + 20f, titleY, new Vector3(0.9f, 0.9f, 1.0f));

            // Divider — positioned based on title text height
            _hud.DrawBox(panelX + 20f, dividerY, panelW - 40f, 1f, new Vector3(0.2f, 0.22f, 0.3f));



            // ── Confirm dialog overlay ──
            if (_confirmActive)
            {
                ConfirmDialog.DrawBox(_hud, w, h,
                    "Unsaved Changes", "Discard changes?",
                    ["DISCARD", "KEEP EDITING"],
                    [new Vector3(0.6f, 0.2f, 0.15f), new Vector3(0.15f, 0.35f, 0.15f)],
                    [new Vector3(1.0f, 0.4f, 0.3f), new Vector3(0.4f, 1.0f, 0.4f)],
                    [new Vector3(1.0f, 0.5f, 0.4f), new Vector3(0.5f, 1.0f, 0.5f)],
                    _confirmSelection);
                return; // skip rest of settings rendering when confirm is active
            }

            // ── Setting items — interactive, selectable, cycleable ──
            for (int i = 0; i < _settingLabels.Length; i++)
            {
                float itemY = listStartY + i * (lineH + lineGap);
                bool isSelected = (i == _settingsSelection);

                // ── Shared button metrics (same for all rows) ──
                float btnPadX = 20f;
                float btnPadY = lineGap * 0.5f;
                float btnW = panelW - btnPadX * 2f;
                float btnH = lineH + btnPadY * 2f;
                float btnX = panelX + btnPadX;
                float btnY = itemY + (lineGap - btnPadY * 2f) * 0.5f;

                // Dirty indicator
                bool isDirty = i < 7 && _settingValues[i] != _savedSettingValues[i];

                // ── Regular setting row (index 0-6) — no background, just text ──
                if (i < 7)
                {
                    // Selected: subtle side bar highlight only
                    if (isSelected)
                    {
                        float pulse = 0.7f + 0.3f * MathF.Sin(_totalTime * 3f);
                        _hud.DrawBox(btnX + 3f, btnY + 2f, 2f, btnH - 4f,
                            new Vector3(0.4f, 0.5f, 0.9f) * (0.6f + pulse * 0.4f));
                    }

                    // Label (left) with dirty mark — centered vertically
                    string label = _settingLabels[i] + (isDirty ? "*" : "");
                    var labelExt = _hud.GetTextExtents(label);
                    float labelY = labelExt.GetCenteredBaselineY(btnY, btnH);
                    _hud.DrawText(label, btnX + 14f, labelY,
                        isSelected ? new Vector3(0.9f, 0.9f, 1.0f) : new Vector3(0.6f, 0.6f, 0.75f));

                    // Value (right) — centered vertically
                    string value = _settingOptions[i][_settingValues[i]];
                    string display = isSelected ? $"< {value} >" : value;
                    var valExtents = _hud.GetTextExtents(display);
                    float valX = btnX + btnW - 14f - valExtents.Width;
                    float valY = valExtents.GetCenteredBaselineY(btnY, btnH);
                    _hud.DrawText(display, valX, valY,
                        isSelected ? new Vector3(0.6f, 0.7f, 1.0f) : new Vector3(0.5f, 0.5f, 0.6f));
                }
                // ── APPLY & SAVE button (index 7) ──
                else if (i == 7)
                {
                    float sepY = itemY - lineGap * 0.5f;
                    _hud.DrawBox(panelX + 30f, sepY, panelW - 60f, 1f, new Vector3(0.2f, 0.22f, 0.25f));

                    Vector3 btnBg = isSelected
                        ? new Vector3(0.15f, 0.50f, 0.20f)
                        : new Vector3(0.10f, 0.14f, 0.12f);
                    _hud.DrawBox(btnX, btnY, btnW, btnH, btnBg);

                    if (isSelected)
                    {
                        float pulse = 0.7f + 0.3f * MathF.Sin(_totalTime * 3f);
                        _hud.DrawBox(btnX, btnY, btnW, btnH, new Vector3(0.2f, 0.7f, 0.3f) * pulse * 0.12f);
                        _hud.DrawBox(btnX + 2f, btnY + 3f, 2f, btnH - 6f, new Vector3(0.3f, 0.9f, 0.4f));
                        _hud.DrawBox(btnX, btnY + btnH - 1f, btnW, 1f, new Vector3(0.3f, 0.9f, 0.4f) * 0.5f);
                    }
                    else
                    {
                        _hud.DrawBox(btnX, btnY, btnW, 1f, new Vector3(0.15f, 0.35f, 0.18f));
                        _hud.DrawBox(btnX, btnY + btnH - 1f, btnW, 1f, new Vector3(0.15f, 0.35f, 0.18f));
                    }

                    string applyLabel = isSelected ? "APPLY & SAVE" : "APPLY & SAVE";
                    var applyExt = _hud.GetTextExtents(applyLabel);
                    float applyX = panelX + (panelW - applyExt.Width) * 0.5f;
                    float applyY = applyExt.GetCenteredBaselineY(btnY, btnH);
                    _hud.DrawText(applyLabel, applyX, applyY,
                        isSelected ? new Vector3(0.5f, 1.0f, 0.5f) : new Vector3(0.5f, 0.75f, 0.5f));
                }
                // ── CANCEL button (index 8) ──
                else
                {
                    Vector3 btnBg = isSelected
                        ? new Vector3(0.50f, 0.12f, 0.12f)
                        : new Vector3(0.14f, 0.10f, 0.10f);
                    _hud.DrawBox(btnX, btnY, btnW, btnH, btnBg);

                    if (isSelected)
                    {
                        float pulse = 0.7f + 0.3f * MathF.Sin(_totalTime * 3f);
                        _hud.DrawBox(btnX, btnY, btnW, btnH, new Vector3(0.7f, 0.2f, 0.2f) * pulse * 0.12f);
                        _hud.DrawBox(btnX + 2f, btnY + 3f, 2f, btnH - 6f, new Vector3(0.9f, 0.3f, 0.3f));
                        _hud.DrawBox(btnX, btnY + btnH - 1f, btnW, 1f, new Vector3(0.9f, 0.3f, 0.3f) * 0.5f);
                    }
                    else
                    {
                        _hud.DrawBox(btnX, btnY, btnW, 1f, new Vector3(0.35f, 0.15f, 0.15f));
                        _hud.DrawBox(btnX, btnY + btnH - 1f, btnW, 1f, new Vector3(0.35f, 0.15f, 0.15f));
                    }

                    string cancelLabel = isSelected ? "CANCEL" : "CANCEL";
                    var cancelExt = _hud.GetTextExtents(cancelLabel);
                    float cancelX = panelX + (panelW - cancelExt.Width) * 0.5f;
                    float cancelY = cancelExt.GetCenteredBaselineY(btnY, btnH);
                    _hud.DrawText(cancelLabel, cancelX, cancelY,
                        isSelected ? new Vector3(1.0f, 0.5f, 0.5f) : new Vector3(0.75f, 0.5f, 0.5f));
                }
            }

            // Back hint — centered within panel, matching main menu hint style
            float hintY = listStartY + _settingLabels.Length * (lineH + lineGap) + 40f;
            string hintText = _hasUnsavedChanges
                ? "Arrow keys or mouse to cycle  -  Enter to select  -  ESC to discard"
                : "Arrow keys or mouse to cycle  -  Enter to select  -  ESC to close";
            var hintExt = _hud.GetTextExtents(hintText);
            float hintX = panelX + (panelW - hintExt.Width) * 0.5f;
            _hud.DrawText(hintText, hintX, hintY, new Vector3(0.35f, 0.35f, 0.45f));
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
