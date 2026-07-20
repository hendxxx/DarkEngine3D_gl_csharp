using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Scene;

/// <summary>
/// Main menu scene — entirely data-driven from .ing files.
/// No hardcoded UI. The UI hierarchy is loaded from MainMenu.ing
/// and behaviors (start game, settings, exit, etc.) are mapped
/// from ClickBehaviorLabel strings at runtime.
/// Retains the animated procedural background (particles, grid, vignette).
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

    // ── Scene root element (holds the UI hierarchy tree loaded from .ing) ──
    private readonly UIElement _sceneRoot = new()
    {
        Name = "MainMenu",
        Type = UIElementType.Scene,
        IsVisible = false,
    };

    // ── UI Button Data (rebuilt from loaded hierarchy each frame) ──
    private readonly List<UIButtonData> _uiButtons = [];

    // ── Random ──
    private readonly Random _rng = new();

    // ── Overlay state (toggled by behavior actions mapped from .ing) ──
    private bool _settingsOpen = false;
    private bool _loadGameActive = false;
    private bool _exitConfirmActive = false;
    private bool _confirmActive = false; // unsaved changes confirm in settings

    // ── Settings system ──
    private readonly string[] _settingLabels =
    [
        "Resolution",
        "Display Mode",
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
        ["EXCLUSIVE", "BORDERLESS", "WINDOWED"],
        ["OFF", "ON"],
        ["LOW", "MEDIUM", "HIGH", "ULTRA"],
        ["Software", "HiZ", "OFF"],
        ["60", "70", "80", "90", "100", "110"],
        ["0.25×", "0.50×", "0.75×", "1.0×", "1.5×", "2.0×", "3.0×"],
        [""],   // Apply button
        [""],   // Cancel button
    ];
    private readonly int[] _settingValues = [0, 0, 0, 3, 1, 0, 3, 0, 0];
    private readonly int[] _savedSettingValues = [0, 0, 0, 3, 1, 0, 3, 0, 0];
    private bool _hasUnsavedChanges = false;

    // ── Toast notification ──
    private string _notificationText = "";
    private float _notificationTimer = 0f;
    private const float NotificationDuration = 3f;

    // ── Save slots (for load game overlay) ──
    private int _loadGameSelection = 0;
    private SaveSlotInfo[] _loadSlots = new SaveSlotInfo[SaveManager.NumSlots];

    // ── Exit confirm selection ──
    private int _exitConfirmSelection = 0;

    // ── Settings navigation state ──
    private int _settingsSelection = 0;
    private int _settingsLastHoveredRow = -1;

    // ── Shared lookup tables ──
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

    // ══════════════════════════════════════════════
    //  ANIMATED BACKGROUND — Particles & Effects
    // ══════════════════════════════════════════════

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

    // ── Background objects (loaded from .ing scene data) ──
    private List<BackgroundObjectData> _bgObjectDataList = [];

    public MainMenuScene(SceneManager sceneManager, Camera camera, Lights light)
    {
        _sceneManager = sceneManager;
        _camera = camera;
        _light = light;
    }

    public void Enter()
    {
        _hud = new HUD("Artifacts\\fonts\\Worldstar.ttf", 28.0f);
        _totalTime = 0f;
        _scanLineY = 0f;

        Glfw.OnWindowResized += OnWindowResized;
        Mouse.ShowMouse(true);

        // ── Initialize particles ──
        var rng = new Random(42);
        for (int i = 0; i < ParticleCount; i++)
        {
            _particles[i] = new Particle
            {
                X = (float)(rng.NextDouble() * 1920),
                Y = (float)(rng.NextDouble() * 1080),
                SpeedX = (float)((rng.NextDouble() - 0.5) * 4f),
                SpeedY = (float)(-(rng.NextDouble() * 6f + 3f)),
                WobblePhase = (float)(rng.NextDouble() * Math.PI * 2),
                WobbleAmp = (float)(rng.NextDouble() * 10f + 5f),
                Size = (float)(rng.NextDouble() * 3f + 1.5f),
                Alpha = (float)(rng.NextDouble() * 0.5f + 0.15f),
                AlphaSpeed = (float)(rng.NextDouble() * 0.3f + 0.1f),
                Color = (i % 3) switch
                {
                    0 => new Vector3(0.3f, 0.5f, 0.9f),
                    1 => new Vector3(0.6f, 0.3f, 0.9f),
                    _ => new Vector3(0.2f, 0.7f, 0.8f),
                }
            };
        }

        // ── Load saved settings from JSON ──
        var saved = SettingsSave.Load();
        _settingValues[0] = saved.Resolution;
        _settingValues[1] = saved.BorderlessFullscreen ? 1 : (saved.Fullscreen ? 0 : 2);
        _settingValues[2] = saved.VSync ? 1 : 0;
        _settingValues[3] = saved.ShadowQuality;
        _settingValues[4] = saved.OcclusionMode;
        _settingValues[5] = (saved.Fov - 60) / 10;
        if (_settingValues[5] < 0) _settingValues[5] = 0;
        if (_settingValues[5] >= _settingOptions[5].Length) _settingValues[5] = _settingOptions[5].Length - 1;
        ApplyOcclusionMode(_settingValues[4]);
        _camera.BaseFoV = saved.Fov;
        _camera.FoV = saved.Fov;

        _settingValues[6] = saved.MouseSensitivity;
        if (_settingValues[6] < 0) _settingValues[6] = 0;
        if (_settingValues[6] >= _settingOptions[6].Length) _settingValues[6] = _settingOptions[6].Length - 1;
        ApplyMouseSensitivity(_settingValues[6]);

        Array.Copy(_settingValues, _savedSettingValues, _settingValues.Length);
        _hasUnsavedChanges = false;
        ResetOverlays();

        // ── Scene starts blank! No .ing file is loaded automatically. ──
        // User can create UI via the IDE SceneDetail panel (+ Add button),
        // or use the "↻ Reload" button to load from a previously saved .ing file.
        _sceneRoot.ClearChildren();
        _uiButtons.Clear();
        _bgObjectDataList = [];

        // ── Register for IDE Save All ──
        SceneAssetSerializer.RegisterSceneRoot("MainMenu", _sceneRoot);
        SceneAssetSerializer.RegisterBgObjects("MainMenu", _bgObjectDataList);

        Console.WriteLine("[MainMenu] Entered — blank start (no .ing file loaded).");
    }

    private void ResetOverlays()
    {
        _settingsOpen = false;
        _loadGameActive = false;
        _exitConfirmActive = false;
        _confirmActive = false;
        _settingsSelection = 0;
        _settingsLastHoveredRow = -1;
        _exitConfirmSelection = 0;
        _notificationText = "";
        _notificationTimer = 0f;
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

        // ── Update animations ──
        UpdateParticles(deltaTime);
        _scanLineY += deltaTime * 30f;
        if (_scanLineY > Glfw.WindowHeight)
            _scanLineY = 0f;
        _gridOffsetX += deltaTime * 2f;
        if (_gridOffsetX > 40f)
            _gridOffsetX -= 40f;

        // ── Sync UIElement positions from hierarchy (always, even when input locked) ──
        SyncHierarchyPositions();

        // ── Rebuild _uiButtons from loaded hierarchy so HUD renders them (always, for live preview in IDE) ──
        _uiButtons.Clear();
        RebuildUiButtonsFromHierarchy(_sceneRoot.Children);

        // ── HUD Button System: always rebuild buttons for rendering ──
        _hud.ClearButtons();
        for (int i = 0; i < _uiButtons.Count; i++)
        {
            var uiBtn = _uiButtons[i];
            if (!uiBtn.IsVisible) continue;
            _hud.AddButton(uiBtn.Text, uiBtn.X, uiBtn.Y, uiBtn.Width, uiBtn.Height,
                () => uiBtn.OnClick?.Invoke());
        }

        nint window = Glfw.GetWindow();

        // ── Input gate: block keyboard/mouse but keep UI rendering alive ──
        bool ingameActive = _sceneManager.Bridge?.InGameActive ?? true;
        if (!ingameActive)
            return;

        // ── Mouse tracking ──
        Mouse.GetCursorPosition(out double mouseX, out double mouseY);
        bool mousePressed = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);

        // ── Update HUD buttons & handle click-through to OnClick delegates ──
        _hud.UpdateButtons();

        // ── Load Game overlay: keyboard nav ──
        if (_loadGameActive)
        {
            HandleLoadGameInput(window);
            return;
        }

        // ── ESC to close overlays ──
        bool escapeDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);
        if (escapeDown)
        {
            if (_confirmActive)
            {
                _confirmActive = false;
                return;
            }
            if (_settingsOpen)
            {
                if (_hasUnsavedChanges)
                {
                    _confirmActive = true;
                }
                else
                {
                    _settingsOpen = false;
                }
                return;
            }
            if (_loadGameActive)
            {
                _loadGameActive = false;
                return;
            }
            if (_exitConfirmActive)
            {
                _exitConfirmActive = false;
                return;
            }
        }

        // ── Settings overlay: keyboard nav for cycling settings ──
        if (_settingsOpen)
        {
            bool upDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_W);
            bool downDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);
            bool leftDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_A);
            bool rightDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_D);

            // Navigation for settings rows
            if (upDown && _settingsSelection > 0)
                _settingsSelection--;
            if (downDown && _settingsSelection < _settingLabels.Length - 1)
                _settingsSelection++;
            if (leftDown)
                CycleSetting(_settingsSelection, -1);
            if (rightDown)
                CycleSetting(_settingsSelection, 1);
        }
    }

    private void UpdateParticles(float dt)
    {
        float w = Glfw.WindowWidth;
        float h = Glfw.WindowHeight;

        for (int i = 0; i < ParticleCount; i++)
        {
            var p = _particles[i];
            p.WobblePhase += dt * 0.8f;
            float wobbleX = MathF.Sin(p.WobblePhase) * p.WobbleAmp * dt;
            p.X += p.SpeedX * dt + wobbleX;
            p.Y += p.SpeedY * dt;
            p.Alpha += MathF.Sin(_totalTime * p.AlphaSpeed + i) * dt * 0.15f;
            p.Alpha = Math.Clamp(p.Alpha, 0.05f, 0.7f);

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

    /// <summary>Rebuild the legacy _uiButtons flat list from the loaded UIElement hierarchy.</summary>
    private void RebuildUiButtonsFromHierarchy(List<UIElement> elements)
    {
        foreach (var elem in elements)
        {
            if ((elem.Type == UIElementType.Button || elem.Type == UIElementType.Label) && !string.IsNullOrEmpty(elem.Text))
            {
                _uiButtons.Add(new UIButtonData
                {
                    Name = elem.Name,
                    Text = elem.Text,
                    X = elem.X,
                    Y = elem.Y,
                    Width = elem.Width,
                    Height = elem.Height,
                    FontSize = elem.FontSize,
                    FontPath = elem.FontPath,
                    TextColor = elem.TextColor,
                    BgColor = elem.BgColor,
                    HoverBgColor = elem.HoverBgColor,
                    BorderColor = elem.BorderColor,
                    HoverBorderColor = elem.HoverBorderColor,
                    Alignment = elem.Alignment,
                    IsVisible = elem.IsVisible,
                    ClickBehaviorLabel = elem.ClickBehaviorLabel,
                    OnClick = elem.OnClick,
                });
            }

            if (elem.Children.Count > 0)
                RebuildUiButtonsFromHierarchy(elem.Children);
        }
    }

    /// <summary>Map ClickBehavior strings to Action delegates on loaded elements.</summary>
    private void MapBehaviors(List<UIElement> elements)
    {
        foreach (var elem in elements)
        {
            if (!string.IsNullOrEmpty(elem.ClickBehaviorLabel))
            {
                elem.OnClick = MapBehaviorAction(elem.ClickBehaviorLabel);
            }
            if (elem.Children.Count > 0)
                MapBehaviors(elem.Children);
        }
    }

    /// <summary>Convert a behavior string to an Action delegate.</summary>
    private Action? MapBehaviorAction(string behavior)
    {
        var lower = behavior.ToLowerInvariant();

        // ── Cycle setting: "cycleSetting:N" ──
        if (lower.StartsWith("cyclesetting:"))
        {
            if (int.TryParse(lower["cyclesetting:".Length..], out int settingIdx)
                && settingIdx >= 0 && settingIdx < _settingLabels.Length)
            {
                int captured = settingIdx;
                return () => CycleSetting(captured, 1);
            }
            return null;
        }

        // ── Load slot: "loadSlot:N" ──
        if (lower.StartsWith("loadslot:"))
        {
            if (int.TryParse(lower["loadslot:".Length..], out int slotIdx)
                && slotIdx >= 0 && slotIdx < SaveManager.NumSlots)
            {
                int captured = slotIdx;
                return () => StartGameWithLoad(captured);
            }
            return null;
        }

        return lower switch
        {
            "startgame" => () =>
            {
                Console.WriteLine("[MainMenu] Starting new game...");
                GameScene.PendingLoadSlot = -1;
                _sceneManager.SwitchScene(new LoadingScene(_sceneManager, _camera, _light));
            },
            "continue" => () =>
            {
                int latestSlot = SaveManager.GetLatestSlot();
                if (latestSlot >= 0)
                {
                    Console.WriteLine($"[MainMenu] Continuing from slot {latestSlot}...");
                    GameScene.PendingLoadSlot = latestSlot;
                    _sceneManager.SwitchScene(new LoadingScene(_sceneManager, _camera, _light));
                }
            },
            "loadgame" => () =>
            {
                _loadGameActive = true;
                _loadGameSelection = 0;
                _loadSlots = SaveManager.GetAllSlots();
                for (int i = 0; i < _loadSlots.Length; i++)
                {
                    if (_loadSlots[i].HasData)
                        SaveManager.GetOrLoadThumbnail(ref _loadSlots[i], 128, 128);
                }
                Console.WriteLine("[MainMenu] Load Game UI opened");
            },
            "opensettings" => () =>
            {
                _settingsOpen = true;
                _settingsSelection = 0;
                Console.WriteLine("[MainMenu] Settings opened.");
            },
            "showexitconfirm" => () =>
            {
                _exitConfirmActive = true;
                _exitConfirmSelection = 0;
            },
            "cancel" or "canceleexit" => () => { _exitConfirmActive = false; _confirmActive = false; },
            "confirmexit" or "yes" => () =>
            {
                Console.WriteLine("[MainMenu] Exiting...");
                _sceneManager.Stop();
            },
            "applysettings" => () =>
            {
                ApplySettings();
                Array.Copy(_settingValues, _savedSettingValues, _settingValues.Length);
                _hasUnsavedChanges = false;
                ShowNotification("Settings applied!");
            },
            "cancelsettings" => () =>
            {
                Array.Copy(_savedSettingValues, _settingValues, _settingValues.Length);
                _hasUnsavedChanges = false;
                _settingsOpen = false;
                ApplyCurrentSettingsImmediate();
            },
            "discardchanges" => () =>
            {
                Array.Copy(_savedSettingValues, _settingValues, _settingValues.Length);
                _hasUnsavedChanges = false;
                _confirmActive = false;
                _settingsOpen = false;
                ApplyCurrentSettingsImmediate();
            },
            "keepediting" => () => { _confirmActive = false; },
            _ => null
        };
    }

    /// <summary>Sync UIElement positions for IDE tree display (no hardcoded layout).</summary>
    private void SyncHierarchyPositions()
    {
        int w = Glfw.WindowWidth;
        int h = Glfw.WindowHeight;
        if (_hud == null) return;

        var grid = new GridLayout(w, h);

        // Simply sync positions of all loaded buttons — the .ing file defines their coordinates.
        // If positions are zero, they'll get default positions from the loaded data.
        // The IDE's SceneDetail panel can be used to adjust positions and save.

        // ── Exit confirm dialog visibility ──
        foreach (var child in _sceneRoot.Children)
        {
            if (child.Type == UIElementType.Dialog && child.Name == "ExitConfirm")
            {
                child.IsVisible = _exitConfirmActive;
                if (_exitConfirmActive)
                {
                    child.X = (w - 400) * 0.5f;
                    child.Y = (h - 150) * 0.5f;
                    child.Width = 400;
                    child.Height = 150;
                }
            }
        }

        // ── Notification toast ──
        if (_notificationTimer > 0f && !string.IsNullOrEmpty(_notificationText))
        {
            foreach (var child in _sceneRoot.Children)
            {
                if (child.Name == "Notification")
                {
                    child.IsVisible = true;
                    child.Text = _notificationText;
                    var ext = _hud.GetTextExtents(_notificationText);
                    child.X = w * 0.5f - ext.Width * 0.5f;
                    child.Y = h * 0.20f;
                }
            }
        }
    }

    // ══════════════════════════════════════════════
    //  Settings Utilities
    // ══════════════════════════════════════════════

    private void CycleSetting(int settingIdx, int direction)
    {
        if (settingIdx < 0 || settingIdx >= _settingOptions.Length) return;

        var options = _settingOptions[settingIdx];
        if (options.Length == 0) return;

        _settingValues[settingIdx] = (_settingValues[settingIdx] + direction + options.Length) % options.Length;

        // Detect unsaved changes
        _hasUnsavedChanges = false;
        for (int i = 0; i < _settingValues.Length; i++)
        {
            if (_settingValues[i] != _savedSettingValues[i])
            {
                _hasUnsavedChanges = true;
                break;
            }
        }

        // Live-apply settings that take effect immediately
        if (settingIdx == 4) ApplyOcclusionMode(_settingValues[4]);
        if (settingIdx == 5)
        {
            int fovVal = int.Parse(_settingOptions[5][_settingValues[5]]);
            _camera.BaseFoV = fovVal;
            _camera.FoV = fovVal;
        }
        if (settingIdx == 6) ApplyMouseSensitivity(_settingValues[6]);
    }

    private void ApplySettings()
    {
        int res = _settingValues[0];
        int displayMode = _settingValues[1];
        bool vs = _settingValues[2] == 1;
        int sq = _settingValues[3];
        int oc = _settingValues[4];
        int fovVal = int.Parse(_settingOptions[5][_settingValues[5]]);

        var data = new SettingsData
        {
            Resolution = res,
            Fullscreen = displayMode != 2,
            BorderlessFullscreen = displayMode == 1,
            VSync = vs,
            ShadowQuality = sq,
            OcclusionMode = oc,
            Fov = fovVal,
            MouseSensitivity = _settingValues[6],
        };
        SettingsSave.Save(data);

        Config.ShadowConfig.CascadeSizes = Config.ShadowPresets.CascadeSizes[sq];
        ApplyOcclusionMode(oc);
        _camera.BaseFoV = fovVal;
        _camera.FoV = fovVal;
        ApplyMouseSensitivity(_settingValues[6]);
        Glfw.SetWindowSize(Resolutions[res].Width, Resolutions[res].Height);
        Glfw.SetSwapInterval(vs ? 1 : 0);
        ApplyDisplayMode(displayMode);
    }

    private void ApplyCurrentSettingsImmediate()
    {
        ApplyOcclusionMode(_settingValues[4]);
        int fovVal = int.Parse(_settingOptions[5][_settingValues[5]]);
        _camera.BaseFoV = fovVal;
        _camera.FoV = fovVal;
        ApplyMouseSensitivity(_settingValues[6]);
    }

    private static void ApplyDisplayMode(int mode)
    {
        // Display mode application — simplified for data-driven flow.
        // Full implementation can be added via the .ing behavior system.
        if (mode == 1)
        {
            // Borderless: hint via GLFW
            Console.WriteLine($"[MainMenu] Applied display mode: {(mode == 0 ? "Exclusive" : mode == 1 ? "Borderless" : "Windowed")}");
        }
    }

    private static void ApplyMouseSensitivity(int val)
    {
        float[] multipliers = [0.25f, 0.50f, 0.75f, 1.0f, 1.5f, 2.0f, 3.0f];
        Mouse.Sensitivity = 0.1f * multipliers[val];
    }

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
        else
        {
            Config.OcclusionConfig.UseOcclusion = false;
        }
    }

    private void ShowNotification(string text)
    {
        _notificationText = text;
        _notificationTimer = NotificationDuration;
    }

    /// <summary>Cancel settings — restore snapshot and close.</summary>
    private void CancelSettings()
    {
        Array.Copy(_savedSettingValues, _settingValues, _settingValues.Length);
        _hasUnsavedChanges = false;
        _settingsOpen = false;
        ApplyCurrentSettingsImmediate();
    }

    // ══════════════════════════════════════════════
    //  Load Game Overlay
    // ══════════════════════════════════════════════

    private void HandleLoadGameInput(nint window)
    {
        // ESC to close
        if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE))
        {
            _loadGameActive = false;
            return;
        }

        // Enter on a slot to load
        if (Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE))
        {
            if (_loadGameSelection >= 0 && _loadGameSelection < _loadSlots.Length && _loadSlots[_loadGameSelection].HasData)
            {
                int slot = _loadGameSelection;
                _loadGameActive = false;
                StartGameWithLoad(slot);
                return;
            }
        }

        // Arrow keys to navigate slots
        bool upDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_W);
        bool downDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);
        if (upDown && _loadGameSelection > 0)
            _loadGameSelection--;
        if (downDown && _loadGameSelection < _loadSlots.Length - 1)
            _loadGameSelection++;
    }

    private void StartGameWithLoad(int slotIndex)
    {
        Console.WriteLine($"[MainMenu] Starting game with load from slot {slotIndex}...");
        GameScene.PendingLoadSlot = slotIndex;
        _sceneManager.SwitchScene(new LoadingScene(_sceneManager, _camera, _light));
    }



    // ══════════════════════════════════════════════
    //  Render
    // ══════════════════════════════════════════════

    public void Render()
    {
        if (_hud == null) return;

        bool ideActive = _sceneManager.IsIdeActive;

        if (ideActive)
        {
            _sceneManager.EnsureSharedFBOExists();
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _sceneManager.SharedFBO);
        }

        GL.ClearColor(0f, 0f, 0f, 1f);
        GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);

        // ── Render background effects (particles, grid, vignette) ──
        RenderBackgroundEffects();

        // ── Render UI from loaded hierarchy via HUD ──
        RenderUI();

        // ── Update bridge with scene data (always, so IDE panels have current state) ──
        var bridge = _sceneManager.Bridge;
        if (bridge != null)
        {
            // Always set SceneRoot so HierarchyPanel can add elements even when IDE just opened
            bridge.SceneRoot = _sceneRoot;

            // ── Update bridge texture for IDE Viewport panel ──
            if (ideActive)
            {
                bridge.SceneTextureID = _sceneManager.SharedColorTex;
                bridge.SceneTextureWidth = Glfw.WindowWidth;
                bridge.SceneTextureHeight = Glfw.WindowHeight;

                // Expose scene root + its children for IDE SceneDetail panel
                bridge.SceneRootElements = _sceneRoot.Children.AsReadOnly();
            }
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
        }
        else
        {
            nint window = Glfw.GetWindow();
            OpenGL.SwapBuffer(window);
            OpenGL.PollEvents();
        }
    }

    private void RenderUI()
    {
        if (_hud == null) return;

        // Render all registered buttons from the loaded hierarchy
        _hud.DrawButtons(_totalTime, -1);
    }

    private void RenderBackgroundEffects()
    {
        if (_hud == null) return;

        int w = Glfw.WindowWidth;
        int h = Glfw.WindowHeight;

        // ── Grid lines ──
        float gridSpacing = 40f;
        float gridAlpha = 0.04f + 0.02f * MathF.Sin(_totalTime * 0.3f);
        var gridColor = new Vector3(0.3f, 0.5f, 0.9f) * gridAlpha;

        // ── Vignette ──
        float vigAlpha = 0.3f + 0.1f * MathF.Sin(_totalTime * 0.5f);
        var vigColor = new Vector3(0f, 0f, 0f) * vigAlpha;

        // ── Scan line ──
        var scanColor = new Vector3(0.8f, 0.9f, 1f) * 0.03f;
        _hud.DrawBox(0, _scanLineY, w, 2f, scanColor);

        // ── Vignette overlay (semi-transparent borders) ──
        float vigSize = 80f;
        _hud.DrawBox(0, 0, w, vigSize, vigColor); // top
        _hud.DrawBox(0, h - vigSize, w, vigSize, vigColor); // bottom
        _hud.DrawBox(0, 0, vigSize, h, vigColor); // left
        _hud.DrawBox(w - vigSize, 0, vigSize, h, vigColor); // right

        // ── Particles (small glowing dots) ──
        for (int i = 0; i < ParticleCount; i++)
        {
            var p = _particles[i];
            float size = p.Size * 2f;
            float particleAlpha = p.Alpha * 0.6f;
            _hud.DrawBox(p.X - size * 0.5f, p.Y - size * 0.5f, size, size, p.Color * particleAlpha);
        }
    }

    public void Exit()
    {
        Glfw.OnWindowResized -= OnWindowResized;

        // Background objects are lightweight data; no GPU cleanup needed

        // Clear IDE bridge references
        var bridge = _sceneManager.Bridge;
        if (bridge != null)
        {
            bridge.SelectedUIElement = null;
            bridge.SelectedUIElements.Clear();
            bridge.SceneRoot = null;
            bridge.SceneRootElements = null;
        }

        SceneAssetSerializer.UnregisterSceneRoot("MainMenu");
        Console.WriteLine("[MainMenu] Exited.");
    }

    public void Dispose()
    {
        _hud = null;
    }
}
