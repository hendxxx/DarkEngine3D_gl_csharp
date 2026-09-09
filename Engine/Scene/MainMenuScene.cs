using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Visual;
using StbImageSharp;
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

        //  Per-scene render properties 
        private readonly SceneRenderProperties _renderProperties = new()
        {
            BackgroundColor = new System.Numerics.Vector3(0f, 0f, 0f),
            VSync = true,
            FaceCulling = CullMode.None,
            FrontFaceWinding = WindingOrder.CCW,
            WireframeMode = false,
            DepthTest = false,
            Blending = true,
        };
    public SceneRenderProperties RenderProperties => _renderProperties;

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

    // ── Image texture cache (path → OpenGL texture ID + dimensions) ──
    private readonly Dictionary<string, uint> _imageTextureCache = [];
    private readonly Dictionary<string, (int w, int h)> _imageTextureDims = [];

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
        "Quality",
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
#pragma warning disable CS0414
    private int _exitConfirmSelection = 0;

    // ── Settings navigation state ──
    private int _settingsSelection = 0;
#pragma warning disable CS0414
    private int _settingsLastHoveredRow = -1;

    // 🛠️ FIX #9: Use shared ResolutionConfig instead of local ResInfo struct.
    // This eliminates the duplicate definition between Program.cs and MainMenuScene.cs.
    private static readonly Config.ResolutionConfig.ResInfo[] Resolutions = Config.ResolutionConfig.Resolutions;

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

    // ── Editor grid (3D ground plane with axis helpers for IDE viewport) ──
    private uint _gridVAO = 0;
    private uint _gridVBO = 0;
    private int _gridVertexCount = 0;
    private uint _axisVAO = 0;
    private uint _axisVBO = 0;

    // ── CSM shadow maps for the IDE viewport (editor objects cast shadows matching
    // the sky sun / light marker, same shadow pass GameScene uses) ──
    private CSM? _csm;

    private readonly string? _initialSceneName;

    public MainMenuScene(SceneManager sceneManager, Camera camera, Lights light, string? initialSceneName = null)
    {
        _sceneManager = sceneManager;
        _camera = camera;
        _light = light;
        _initialSceneName = initialSceneName;
    }

    /// <summary>Load UI hierarchy from .ing file into the given root element.
    /// Tries individual scene file first, then falls back to game.ing manifest.
    /// Returns true if data was loaded.</summary>
    private bool LoadHierarchyFromIng(UIElement root)
    {
        string sceneKey = _initialSceneName ?? "MainMenu";
        string scenePath = SceneAssetSerializer.GetScenePath(sceneKey);
        var asset = SceneAssetSerializer.LoadScene(scenePath)
                 ?? SceneAssetSerializer.FindScene(sceneKey);

        if (asset == null || asset.Elements.Count == 0)
        {
            Console.WriteLine($"[MainMenu] No .ing file found for '{sceneKey}' — starting with empty hierarchy.");
            return false;
        }

        // Clear existing children and load from asset.
        // If the saved asset has a top-level Scene element, unwrap it so its
        // children become the runtime scene root's children (avoid nesting).
        root.ClearChildren();
        foreach (var elemData in asset.Elements)
        {
            if (!string.IsNullOrEmpty(elemData.Type) && elemData.Type.Equals("Scene", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var childData in elemData.Children)
                {
                    var child = SceneAssetSerializer.ToUIElement(childData);
                    root.AddChild(child);
                }
            }
            else
            {
                var child = SceneAssetSerializer.ToUIElement(elemData);
                root.AddChild(child);
            }
        }

        Console.WriteLine($"[MainMenu] Loaded hierarchy from .ing ({root.Children.Count} top-level elements)");
        return true;
    }

    public void Enter()
    {
        _hud = new HUD("Artifacts\\fonts\\Worldstar.ttf", 13.0f);
        _totalTime = 0f;
        _scanLineY = 0f;

        // ── Set camera to a good default position looking at the origin from above ──
        // Position: above and slightly behind the grid, looking down at ~30°
        // This ensures the grid and editor objects are immediately visible in the viewport.
        // Only set if the camera hasn't been positioned elsewhere (e.g. from a saved scene).
        if (_camera.Position.LengthSquared() < 0.01f)
        {
            _camera.Init(0f, 8f, 10f, 0f, -35f);
        }

        // ── Create editor grid (ground plane + axes) for IDE viewport ──
        CreateEditorGrid();

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
        _settingValues[3] = saved.QualityPreset;
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

        // ── Load UI hierarchy from .ing file ──
        // This populates _sceneRoot with saved elements so both IDE editing
        // and game runtime have the same content.
        _sceneRoot.ClearChildren();
        _uiButtons.Clear();
        _bgObjectDataList = [];
        LoadHierarchyFromIng(_sceneRoot);

        // ── Ensure default runtime UI elements (ExitConfirm dialog, etc.) ──
        EnsureDefaultUI();

        // ── Register for IDE Save All ──
        string registerKey = _initialSceneName ?? "MainMenu";
        SceneAssetSerializer.RegisterSceneRoot(registerKey, _sceneRoot);
        SceneAssetSerializer.RegisterBgObjects(registerKey, _bgObjectDataList);

        Console.WriteLine("[MainMenu] Entered.");
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
        // Update fit-to-window elements to match new window size
        UpdateFitToWindowElements();
    }

    /// <summary>Find all elements with "FitToWindow" behavior and resize them to current window dimensions.</summary>
    private void UpdateFitToWindowElements()
    {
        foreach (var child in _sceneRoot.Children)
        {
            if (child.Name == "Background" || child.ClickBehaviorLabel == "fittowindow")
            {
                child.X = 0;
                child.Y = 0;
                child.Width = Glfw.WindowWidth;
                child.Height = Glfw.WindowHeight;
            }
        }
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

        // ── Apply the first Sky/Light editor marker to the scene lights so the viewport
        // lighting follows the sky-sun gizmo drag IMMEDIATELY (same helper GameScene uses
        // every frame). Runs in both edit and game mode. No Sky object → falls back to the
        // procedural sun (SunDirOverride cleared, brightness restored). ──
        var envMgr = _sceneManager.Bridge?.EditorObjectManager;
        if (envMgr != null)
        {
            EditorObject? skyObj = null;
            foreach (var obj in envMgr.Objects)
                if (skyObj == null && obj.PrimitiveType == EditorPrimitiveType.Sky) skyObj = obj;
            // Sky drives a DIRECT light — prefer it over other light types.
            EditorObject? lightObj = EditorObject.PickSunLight(envMgr.Objects);
            EditorObject.ApplyEnvironmentMarkers(lightObj, skyObj, _light, null, deltaTime);

            // ── Collect Point/Spot Light markers as local lights ──
            _light.CollectLocalLights(envMgr.Objects);
        }
        else
        {
            _light.LocalLights.Clear();
        }

        // Always advance the scene lights (procedural sun + fog), independent of whether
        // any editor markers exist — keeps ShadowDirStable valid for the viewport CSM pass.
        _light.Update(deltaTime, _camera.Position);

        // ── Sync UIElement positions from hierarchy (always, even when input locked) ──
        SyncHierarchyPositions();

        // 🛠️ FIX #2: Map behavior strings to Action delegates BEFORE rebuilding _uiButtons.
        // This ensures elements added/modified via the IDE HierarchyPanel
        // (which set ClickBehaviorLabel but never wire OnClick) have their
        // behaviors activated immediately, and the OnClick delegates are
        // captured in the _uiButtons snapshot below.
        MapBehaviors(_sceneRoot.Children);

        // ── Rebuild _uiButtons from loaded hierarchy so HUD renders them (always, for live preview in IDE) ──
        _uiButtons.Clear();
        RebuildUiButtonsFromHierarchy(_sceneRoot.Children);

        // ── Update fit-to-window elements ──
        UpdateFitToWindowElements();

        // ── Load image textures for elements with ImagePath ──
        LoadImageTexturesFromHierarchy(_sceneRoot.Children);

        // ── HUD Button System: always rebuild buttons for rendering ──
        _hud.ClearButtons();
        for (int i = 0; i < _uiButtons.Count; i++)
        {
            var uiBtn = _uiButtons[i];
            if (!uiBtn.IsVisible) continue;

            // Skip elements with ImagePath — they're rendered as images, not buttons
            if (!string.IsNullOrEmpty(uiBtn.ImagePath))
                continue;

            // Determine font slot for this button (each can have its own size)
            string btnFontPath = !string.IsNullOrEmpty(uiBtn.FontPath) ? uiBtn.FontPath : "Artifacts\\fonts\\Worldstar.ttf";
            float btnFontSize = uiBtn.FontSize > 0 ? uiBtn.FontSize : 13f;

            // Get or create font slot — HUD maintains an internal O(1) cache
            int fontSlot = _hud.GetOrCreateFontSlot(btnFontPath, btnFontSize);

            _hud.AddButton(uiBtn.Text, uiBtn.X, uiBtn.Y, uiBtn.Width, uiBtn.Height,
                () => uiBtn.OnClick?.Invoke(), fontSlotIndex: fontSlot);
        }

        nint window = Glfw.GetWindow();

        // ── Mouse tracking ──
        Mouse.GetCursorPosition(out double mouseX, out double mouseY);
        bool mousePressed = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);

        // ── Update HUD buttons & handle click-through to OnClick delegates ──
        // This runs BEFORE the input gate so button clicks work even in IDE preview mode.
        _hud.UpdateButtons();

        // ── Input gate: block keyboard/mouse but keep UI rendering alive ──
        bool ingameActive = _sceneManager.Bridge?.InGameActive ?? true;
        if (!ingameActive)
        {
            // ── IDE mode: free-fly camera (WASD + mouse look) when viewport is focused ──
            // A visible overlay is modal → camera input frozen while it's up.
            var ideBridge = _sceneManager.Bridge;
            if (ideBridge != null && ideBridge.IsViewportFocused && !ideBridge.IsOverlayVisible)
            {
                _camera.SetCameraFlyMode(window, _deltaTime, true);
            }
            return;
        }

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
        // Auto-layout (auto-center / auto-fill) applies against the window size so the
        // registered buttons render centered in-game exactly like the editor preview.
        float canvasW = Glfw.WindowWidth;
        float canvasH = Glfw.WindowHeight;

        foreach (var elem in elements)
        {
            if ((elem.Type == UIElementType.Button || elem.Type == UIElementType.Label) && !string.IsNullOrEmpty(elem.Text))
            {
                var (ex, ey, ew, eh) = elem.GetLayoutBounds(canvasW, canvasH);
                _uiButtons.Add(new UIButtonData
                {
                    Name = elem.Name,
                    Text = elem.Text,
                    X = ex,
                    Y = ey,
                    Width = ew,
                    Height = eh,
                    FontSize = elem.FontSize,
                    FontPath = elem.FontPath,
                    TextColor = elem.TextColor,
                    BgColor = elem.BgColor,
                    HoverBgColor = elem.HoverBgColor,
                    BorderColor = elem.BorderColor,
                    HoverBorderColor = elem.HoverBorderColor,
                    Alignment = elem.Alignment,
                    WordWrap = elem.WordWrap,
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

        // Timer behavior: "timer:<seconds>:<innerBehavior>"
        if (lower.StartsWith("timer:"))
        {
            // Split into 3 parts max: timer, seconds, rest
            var parts = behavior.Split(':', 3);
            if (parts.Length >= 3 && float.TryParse(parts[1], out float secs) && secs >= 0f)
            {
                string inner = parts[2];
                return () =>
                {
                    Console.WriteLine($"[MainMenu] Scheduling '{inner}' in {secs} seconds");
                    _sceneManager.ScheduleBehavior(inner, secs);
                };
            }
        }

        // ── Direct scene switch: "scene:Name" ──
        if (lower.StartsWith("scene:"))
        {
            string target = behavior["scene:".Length..];
            return () =>
            {
                var asset = SceneAssetSerializer.FindScene(target);
                if (asset != null)
                {
                    Console.WriteLine($"[MainMenu] scene:{target} → switching to scene '{target}'");
                    _sceneManager.SwitchScene(new MainMenuScene(_sceneManager, _camera, _light, target));
                }
                else
                {
                    Console.WriteLine($"[MainMenu] scene:{target} → scene not found");
                }
            };
        }

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
                // If IDE is active (preview mode), just disable ingame input to return to edit mode
                var bridge = _sceneManager.Bridge;
                if (bridge != null && _sceneManager.IsIdeActive)
                {
                    Console.WriteLine("[MainMenu] Preview mode — disabling ingame input (back to editor)");
                    bridge.InGameActive = false;
                    _exitConfirmActive = false;
                }
                else
                {
                    Console.WriteLine("[MainMenu] Exiting...");
                    _sceneManager.Stop();
                }
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

    /// <summary>
    /// Ensure default UI elements that are required at runtime but not necessarily
    /// saved in the .ing file. Currently adds the ExitConfirmation dialog.
    /// The dialog is created once with IsVisible=false and shown/hidden by _exitConfirmActive.
    /// </summary>
    private void EnsureDefaultUI()
    {
        // Check if ExitConfirm dialog already exists in hierarchy
        bool hasExitConfirm = false;
        foreach (var child in _sceneRoot.Children)
        {
            if (child.Type == UIElementType.Container && child.Name == "ExitConfirm")
            {
                hasExitConfirm = true;
                break;
            }
        }

        if (hasExitConfirm) return;

        // ── Create ExitConfirm dialog (default: hidden) ──
        var exitDlg = new UIElement
        {
            Name = "ExitConfirm",
            Type = UIElementType.Container,
            Text = "",
            IsVisible = true, // visibility synced by SyncHierarchyPositions each frame
            BgColor = new Vector3(0.12f, 0.13f, 0.19f), // dark bg
            BorderColor = new Vector3(0.5f, 0.3f, 0.3f), // red accent
        };

        // Dark overlay behind the dialog
        var overlay = new UIElement
        {
            Name = "ExitDlgOverlay",
            Type = UIElementType.Container,
            Text = "",
            IsVisible = true,
            BgColor = new Vector3(0f, 0f, 0f) * 0.55f,
            X = 0,
            Y = 0,
            Width = Glfw.WindowWidth,
            Height = Glfw.WindowHeight,
        };
        exitDlg.AddChild(overlay);

        // Title label
        var title = new UIElement
        {
            Name = "ExitDlgTitle",
            Type = UIElementType.Label,
            Text = "Exit Game?",
            FontSize = 32f,
            FontPath = "Artifacts\\fonts\\Worldstar.ttf",
            TextColor = new Vector3(1f, 1f, 1f),
            Alignment = TextAlignment.Center,
            IsVisible = true,
        };
        exitDlg.AddChild(title);

        // Message label
        var message = new UIElement
        {
            Name = "ExitDlgMessage",
            Type = UIElementType.Label,
            Text = "Are you sure you want to exit?",
            FontSize = 18f,
            FontPath = "Artifacts\\fonts\\Worldstar.ttf",
            TextColor = new Vector3(0.85f, 0.85f, 0.9f),
            Alignment = TextAlignment.Center,
            IsVisible = true,
        };
        exitDlg.AddChild(message);

        // Cancel button (left)
        var cancelBtn = new UIElement
        {
            Name = "ExitDlgCancel",
            Type = UIElementType.Button,
            Text = "Cancel",
            Width = 150f,
            Height = 44f,
            FontSize = 20f,
            FontPath = "Artifacts\\fonts\\Worldstar.ttf",
            TextColor = new Vector3(0.95f, 0.95f, 1f),
            BgColor = new Vector3(0.12f, 0.13f, 0.18f),
            HoverBgColor = new Vector3(0.22f, 0.28f, 0.45f),
            BorderColor = new Vector3(0.15f, 0.18f, 0.25f),
            HoverBorderColor = new Vector3(0.5f, 0.6f, 1.0f),
            Alignment = TextAlignment.Center,
            IsVisible = true,
            ClickBehaviorLabel = "cancel",
        };
        exitDlg.AddChild(cancelBtn);

        // Yes, Exit button (right)
        var exitBtn = new UIElement
        {
            Name = "ExitDlgConfirm",
            Type = UIElementType.Button,
            Text = "Yes, Exit",
            Width = 150f,
            Height = 44f,
            FontSize = 20f,
            FontPath = "Artifacts\\fonts\\Worldstar.ttf",
            TextColor = new Vector3(1f, 1f, 1f),
            BgColor = new Vector3(0.35f, 0.10f, 0.12f),      // red bg
            HoverBgColor = new Vector3(0.55f, 0.20f, 0.22f), // brighter red on hover
            BorderColor = new Vector3(0.5f, 0.2f, 0.2f),
            HoverBorderColor = new Vector3(0.8f, 0.4f, 0.4f),
            Alignment = TextAlignment.Center,
            IsVisible = true,
            ClickBehaviorLabel = "confirmexit",
        };
        exitDlg.AddChild(exitBtn);

        _sceneRoot.AddChild(exitDlg);
        Console.WriteLine("[MainMenu] Created default ExitConfirm dialog.");
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

        // ── Exit confirm dialog visibility & positioning ──
        foreach (var child in _sceneRoot.Children)
        {
            if (child.Type == UIElementType.Container && child.Name == "ExitConfirm")
            {
                child.IsVisible = _exitConfirmActive;
                // Sync children visibility to match parent — prevents buttons
                // from appearing in _uiButtons flat list when dialog is hidden.
                foreach (var sub in child.Children)
                    sub.IsVisible = _exitConfirmActive;

                if (_exitConfirmActive)
                {
                    // Dialog container: centered, sized for title + message + buttons
                    float dlgW = 440f;
                    float dlgH = 210f;
                    child.X = (w - dlgW) * 0.5f;
                    child.Y = (h - dlgH) * 0.5f;
                    child.Width = dlgW;
                    child.Height = dlgH;

                    // Position child elements
                    foreach (var sub in child.Children)
                    {
                        switch (sub.Name)
                        {
                            case "ExitDlgOverlay":
                                sub.X = 0;
                                sub.Y = 0;
                                sub.Width = w;
                                sub.Height = h;
                                break;
                            case "ExitDlgTitle":
                                sub.X = child.X + (dlgW - _hud.GetTextExtents(sub.Text, 0).Width) * 0.5f;
                                sub.Y = child.Y + 20f;
                                break;
                            case "ExitDlgMessage":
                                sub.X = child.X + (dlgW - _hud.GetTextExtents(sub.Text, 0).Width) * 0.5f;
                                sub.Y = child.Y + 65f;
                                break;
                            case "ExitDlgCancel":
                                sub.X = child.X + (dlgW * 0.5f - sub.Width - 10f);
                                sub.Y = child.Y + 115f;
                                break;
                            case "ExitDlgConfirm":
                                sub.X = child.X + (dlgW * 0.5f + 10f);
                                sub.Y = child.Y + 115f;
                                break;
                        }
                    }
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
        if (settingIdx == 3) ApplyShadowQualityImmediate();
    }

    /// <summary>Apply the unified quality preset immediately when the Quality setting
    /// changes (MSAA + shadow quality + shadow filter together). Rebuilds the viewport
    /// CSM so the new cascade resolutions take effect right away (the CSM captures the
    /// cascade-size array at construction).</summary>
    private void ApplyShadowQualityImmediate()
    {
        // Route through QualitySettings so the preset drives MSAA, shadow quality and
        // shadow filter together (CSM instances rebuild via ShadowSettings.Version).
        Config.QualitySettings.Apply(_settingValues[3]);
        _csm?.Dispose();
        _csm = null;
    }

    private void ApplySettings()
    {
        int res = _settingValues[0];
        int displayMode = _settingValues[1];
        bool vs = _settingValues[2] == 1;
        int sq = _settingValues[3];
        int oc = _settingValues[4];
        int fovVal = int.Parse(_settingOptions[5][_settingValues[5]]);

        // Load first so the persisted Shadow Settings panel prefs (biases, cascade splits,
        // normal bias, filter mode) are preserved — a fresh SettingsData would reset them.
        var data = SettingsSave.Load();
        data.Resolution = res;
        data.Fullscreen = displayMode != 2;
        data.BorderlessFullscreen = displayMode == 1;
        data.VSync = vs;
        data.ShadowQuality = sq;
        data.QualityPreset = sq;
        data.OcclusionMode = oc;
        data.Fov = fovVal;
        data.MouseSensitivity = _settingValues[6];
        SettingsSave.Save(data);

        Config.QualitySettings.Apply(sq);
        _csm?.Dispose();
        _csm = null; // rebuild CSM with the new cascade resolutions
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

        // ── Render editor grid (ground plane + axis helpers) when IDE active ──
        // Enable depth test so lines sit properly in 3D space.
        // Hidden in preview mode.
        if (_sceneManager.IsIdeActive && !(_sceneManager.Bridge?.IsPreviewMode ?? false))
        {
            GL.Enable(Const.GL_DEPTH_TEST);
            RenderEditorGrid();
        }

        // ── Render EditorObjectManager 3D objects (primitives added via IDE toolbar) ──
        if (_sceneManager.IsIdeActive)
        {
            var editorBridge = _sceneManager.Bridge;
            if (editorBridge?.EditorObjectManager is { Count: > 0 } editorObjMgr)
            {
                // ── CSM shadow pass so editor objects cast shadows matching the sun
                // direction (sky gizmo / light marker) in this viewport. Skipped when the
                // viewport "Shadow" toggle is off (maps are cleared to fully-lit instead). ──
                _csm ??= new CSM(Config.ShadowSettings.CascadeSizes[0]);
                _light.LocalShadowsEnabled = editorBridge.ShowShadows;
                if (editorBridge.ShowShadows)
                {
                    editorObjMgr.RenderShadowPass(_camera, _light, _csm);

                    // Local light (Point/Spot) shadow pass — each light casts its own
                    // shadow map. Editor-object renderers self-upload the face matrix from
                    // the CSM shim, so no per-program uniform plumbing is needed here.
                    _light.LocalShadow ??= new LocalLightShadow();
                    _light.LocalShadow.RenderShadowPass(
                        _camera, _light.LocalLights,
                        Shader.GetShadowShaderProgram(), Shader.GetShadowSkinnedShaderProgram(),
                        Shader.GetShadowStaticAlphaShaderProgram(),
                        (CSM csm, int ci) => editorObjMgr.RenderShadow(_camera, csm, ci));
                }
                else
                {
                    _csm.ClearShadowMaps();
                }

                // Restore the shared FBO + scene render state after the depth-only shadow
                // pass (re-apply this scene's render properties, then force depth on for
                // the 3D content — same effective state the grid/objects already rely on)
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _sceneManager.SharedFBO);
                GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);
                _renderProperties.Apply();
                GL.Enable(Const.GL_DEPTH_TEST);

                // Bind the cascade shadow maps (units 6..8 — matches the main shader)
                GL.ActiveTexture(Const.GL_TEXTURE0 + 6);
                GL.BindTexture(Const.GL_TEXTURE_2D, _csm.ShadowTextures[0]);
                GL.ActiveTexture(Const.GL_TEXTURE0 + 7);
                GL.BindTexture(Const.GL_TEXTURE_2D, _csm.ShadowTextures[1]);
                GL.ActiveTexture(Const.GL_TEXTURE0 + 8);
                GL.BindTexture(Const.GL_TEXTURE_2D, _csm.ShadowTextures[2]);

                // Pass selection highlight color so selected objects get a mesh wireframe outline
                Vector3? wireCol = editorBridge is { SelectedEditorObjects.Count: > 0 }
                    ? editorBridge.SelectionHighlights.EditorObject : null;
                // Game-mode render: hide ALL editor gizmos (2D markers, frustum, light gizmo,
                // sky gizmo) — they only show in the edit-mode viewport.
                editorBridge.EditorObjectManager.Draw(_camera, _light, _csm, wireCol,
                    editorBridge.SelectedEditorObjects, showSkyGizmo: false, showEditorGizmos: false);
            }
        }            // ── Update bridge with scene data (always, so IDE panels have current state) ──
            var bridge = _sceneManager.Bridge;
            if (bridge != null)
            {
                // Always set SceneRoot so HierarchyPanel can add elements even when IDE just opened
                bridge.SceneRoot = _sceneRoot;

                // Expose the camera so the viewport Fly toggle (FlyMouseLook) and gizmo
                // interaction target the same camera that drives freefly in this scene.
                bridge.Camera = _camera;

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

    /// <summary>Load OpenGL textures for elements with ImagePath.</summary>
    private void LoadImageTexturesFromHierarchy(List<UIElement> elements)
    {
        foreach (var elem in elements)
        {
            if (!string.IsNullOrEmpty(elem.ImagePath))
                LoadImageTexture(elem.ImagePath);
            if (elem.Children.Count > 0)
                LoadImageTexturesFromHierarchy(elem.Children);
        }
    }

    private unsafe void LoadImageTexture(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (_imageTextureCache.ContainsKey(path)) return;

        if (!File.Exists(path))
        {
            Console.WriteLine($"[MainMenu] Image not found: {path}");
            return;
        }

        try
        {
            uint texID;
            GL.GenTextures(1, &texID);
            GL.BindTexture(Const.GL_TEXTURE_2D, texID);

            using var stream = File.OpenRead(path);
            var image = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);

            fixed (byte* ptr = image.Data)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                              image.Width, image.Height, 0,
                              Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, ptr);
            }

            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);

            _imageTextureCache[path] = texID;
            _imageTextureDims[path] = (image.Width, image.Height);
            Console.WriteLine($"[MainMenu] Loaded image: {path} ({image.Width}×{image.Height})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainMenu] Failed to load image {path}: {ex.Message}");
        }
    }

    /// <summary>Calculate draw rectangle based on image mode.</summary>
    private static (float x, float y, float w, float h) CalcImageRect(
        float elemX, float elemY, float elemW, float elemH,
        float imgW, float imgH, ImageMode mode)
    {
        if (imgW <= 0 || imgH <= 0)
            return (elemX, elemY, elemW, elemH);

        switch (mode)
        {
            case ImageMode.Zoom:
                {
                    float aspect = imgW / imgH;
                    float elemAspect = elemW / elemH;
                    if (aspect > elemAspect)
                    {
                        float drawW = elemW;
                        float drawH = elemW / aspect;
                        return (elemX, elemY + (elemH - drawH) * 0.5f, drawW, drawH);
                    }
                    else
                    {
                        float drawH = elemH;
                        float drawW = elemH * aspect;
                        return (elemX + (elemW - drawW) * 0.5f, elemY, drawW, drawH);
                    }
                }
            case ImageMode.Fill:
                {
                    float aspect = imgW / imgH;
                    float elemAspect = elemW / elemH;
                    if (aspect > elemAspect)
                    {
                        float drawH = elemH;
                        float drawW = elemH * aspect;
                        return (elemX + (elemW - drawW) * 0.5f, elemY, drawW, drawH);
                    }
                    else
                    {
                        float drawW = elemW;
                        float drawH = elemW / aspect;
                        return (elemX, elemY + (elemH - drawH) * 0.5f, drawW, drawH);
                    }
                }
            default: // Stretch
                return (elemX, elemY, elemW, elemH);
        }
    }

    /// <summary>Render image elements from the hierarchy (elements with ImagePath set).</summary>
    private void RenderImageElements(List<UIElement> elements)
    {
        if (_hud == null) return;

        // The HUD renders in window pixel space, so auto-layout centers against the
        // actual window size (same as the editor preview's scene texture).
        float canvasW = Glfw.WindowWidth;
        float canvasH = Glfw.WindowHeight;

        foreach (var elem in elements)
        {
            if (!elem.IsVisible) continue;

            // Auto-center / auto-fill MUST also apply in-game (bug #6) — compute the
            // effective bounds without mutating the stored values.
            var (ex, ey, ew, eh) = elem.GetLayoutBounds(canvasW, canvasH);

            if (!string.IsNullOrEmpty(elem.ImagePath))
            {
                if (_imageTextureCache.TryGetValue(elem.ImagePath, out uint texID) && texID != 0)
                {
                    int imgW = 1, imgH = 1;
                    if (_imageTextureDims.TryGetValue(elem.ImagePath, out var d))
                    {
                        imgW = d.w;
                        imgH = d.h;
                    }
                    var (drawX, drawY, drawW, drawH) = CalcImageRect(
                        ex, ey, ew, eh,
                        imgW, imgH, elem.ImageMode);

                    _hud.DrawImage(drawX, drawY, drawW, drawH, texID);
                }
                else
                {
                    // Image not loaded — draw fallback text (separate from Text property)
                    if (!string.IsNullOrEmpty(elem.FallbackText) && elem.Opacity > 0.01f)
                    {
                        _hud.DrawText(elem.FallbackText, ex, ey, elem.TextColor);
                    }
                }
            }

            if (elem.Children.Count > 0)
                RenderImageElements(elem.Children);
        }
    }

    private void RenderUI()
    {
        if (_hud == null) return;

        // Render image elements first (with ImagePath set)
        RenderImageElements(_sceneRoot.Children);

        // Render all registered buttons from the loaded hierarchy
        _hud.DrawButtons(_totalTime, -1);
    }

    // ══════════════════════════════════════════════
    //  Editor Grid (Ground plane + Axis helpers)
    // ══════════════════════════════════════════════    /// <summary>Create the ground-plane grid and axis helpers as line VAOs.</summary>
    private void CreateEditorGrid()
    {
        CleanupEditorGrid();

        // ── Ground plane grid: 50x50 units centered at origin, spacing 1 ──
        // Sized so a default 25x25 plane (the default plane scale) sits proportionally
        // inside the grid, covering about half of it on each axis.
        const float halfSize = 25f;
        const int divisions = 50;
        const int linesPerDir = divisions + 1; // 51

        // Build vertices: horizontal lines (along X) + vertical lines (along Z)
        var gridVerts = new List<float>();

        // Lines are offset by 0.5 so they sit on half-integer coordinates: a 25x25 plane
        // centered at an integer position spans -12.5..12.5 and covers exactly 25x25 grid squares.
        for (int i = 0; i < linesPerDir; i++)
        {
            float pos = -halfSize - 0.5f + i; // -25.5 to +25.5

            // Horizontal: ( -halfSize, 0, pos ) → ( halfSize, 0, pos )
            gridVerts.Add(-halfSize); gridVerts.Add(0f); gridVerts.Add(pos);
            gridVerts.Add(halfSize); gridVerts.Add(0f); gridVerts.Add(pos);

            // Vertical: ( pos, 0, -halfSize ) → ( pos, 0, halfSize )
            gridVerts.Add(pos); gridVerts.Add(0f); gridVerts.Add(-halfSize);
            gridVerts.Add(pos); gridVerts.Add(0f); gridVerts.Add(halfSize);
        }

        _gridVertexCount = gridVerts.Count / 3;

        float[] gridArray = gridVerts.ToArray();
        uint gridVAO = 0, gridVBO = 0;
        GL.GenVertexArrays(1, &gridVAO);
        GL.GenBuffers(1, &gridVBO);
        GL.BindVertexArray(gridVAO);
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, gridVBO);
        fixed (float* ptr = gridArray)
        {
            GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(gridArray.Length * sizeof(float)), ptr, Const.GL_STATIC_DRAW);
        }
        GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), null);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
        _gridVAO = gridVAO;
        _gridVBO = gridVBO;

        // ── Axis helpers: 3 lines, each rendered separately with different color ──
        // X line: (0,0,0) → (2,0,0)
        // Y line: (0,0,0) → (0,2,0)
        // Z line: (0,0,0) → (0,0,2)
        float[] axisVerts =
        [
            0f, 0f, 0f,   2f, 0f, 0f,
            0f, 0f, 0f,   0f, 2f, 0f,
            0f, 0f, 0f,   0f, 0f, 2f,
        ];

        uint axisVAO = 0, axisVBO = 0;
        GL.GenVertexArrays(1, &axisVAO);
        GL.GenBuffers(1, &axisVBO);
        GL.BindVertexArray(axisVAO);
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, axisVBO);
        fixed (float* ptr = axisVerts)
        {
            GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(axisVerts.Length * sizeof(float)), ptr, Const.GL_STATIC_DRAW);
        }
        GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), null);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
        _axisVAO = axisVAO;
        _axisVBO = axisVBO;

        Console.WriteLine($"[MainMenu] Editor grid created. LineShaderProgram={Shader.GetLineShaderProgram()}");
    }

    /// <summary>Render the editor ground-plane grid and axis helpers.</summary>
    private void RenderEditorGrid()
    {
        if (_gridVAO == 0 || _axisVAO == 0)
        {
            Console.WriteLine("[Grid] VAO not initialized");
            return;
        }

        uint prog = Shader.GetLineShaderProgram();
        if (prog == 0)
        {
            Console.WriteLine("[Grid] Line shader program is 0!");
            return;
        }

        GL.UseProgram(prog);

        // ── Set view/projection from camera ──
        Matrix4x4 viewMatrix = _camera.GetViewMatrix();
        Matrix4x4 projMatrix = _camera.GetProjectionMatrix();

        int viewLoc = GL.GetUniformLocation(prog, "view");
        int projLoc = GL.GetUniformLocation(prog, "projection");
        int modelLoc = GL.GetUniformLocation(prog, "model");
        int colorLoc = GL.GetUniformLocation(prog, "lineColor");

        // Pass matrix pointers (local structs on stack — safe without fixed)
        GL.UniformMatrix4fv(viewLoc, 1, false, &viewMatrix.M11);
        GL.UniformMatrix4fv(projLoc, 1, false, &projMatrix.M11);

        // Identity for ground grid
        Matrix4x4 ident = Matrix4x4.Identity;
        GL.UniformMatrix4fv(modelLoc, 1, false, &ident.M11);

        // ── Draw ground grid (subtle blue-gray) ──
        var gridColor = new Vector3(0.35f, 0.45f, 0.65f);
        GL.Uniform3f(colorLoc, gridColor.X, gridColor.Y, gridColor.Z);

        GL.BindVertexArray(_gridVAO);
        GL.DrawArrays(Const.GL_LINES, 0, _gridVertexCount);
        GL.BindVertexArray(0);

        // ── Draw axes (RGB colors) ──
        GL.BindVertexArray(_axisVAO);

        // X axis (red) — vertices 0-1
        GL.Uniform3f(colorLoc, 1f, 0.2f, 0.2f);
        GL.DrawArrays(Const.GL_LINES, 0, 2);

        // Y axis (green) — vertices 2-3
        GL.Uniform3f(colorLoc, 0.2f, 1f, 0.2f);
        GL.DrawArrays(Const.GL_LINES, 2, 2);

        // Z axis (blue) — vertices 4-5
        GL.Uniform3f(colorLoc, 0.2f, 0.3f, 1f);
        GL.DrawArrays(Const.GL_LINES, 4, 2);

        GL.BindVertexArray(0);

        GL.UseProgram(0);
    }

    /// <summary>Delete GPU resources for editor grid.</summary>
    private void CleanupEditorGrid()
    {
        if (_gridVAO != 0)
        {
            uint vao = _gridVAO;
            GL.DeleteVertexArrays(1, &vao);
            _gridVAO = 0;
        }
        if (_gridVBO != 0)
        {
            uint vbo = _gridVBO;
            GL.DeleteBuffers(1, &vbo);
            _gridVBO = 0;
        }
        if (_axisVAO != 0)
        {
            uint vao = _axisVAO;
            GL.DeleteVertexArrays(1, &vao);
            _axisVAO = 0;
        }
        if (_axisVBO != 0)
        {
            uint vbo = _axisVBO;
            GL.DeleteBuffers(1, &vbo);
            _axisVBO = 0;
        }
        _gridVertexCount = 0;
    }

    private void RenderBackgroundEffects()
    {
        if (_hud == null) return;

        int w = Glfw.WindowWidth;
        int h = Glfw.WindowHeight;

#pragma warning disable CS0219
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

    private void CleanupImageTextures()
    {
        foreach (var kvp in _imageTextureCache)
        {
            uint tex = kvp.Value;
            if (tex != 0)
                GL.DeleteTextures(1, &tex);
        }
        _imageTextureCache.Clear();
        _imageTextureDims.Clear();
    }

    public void Exit()
    {
        Glfw.OnWindowResized -= OnWindowResized;

        // Clean up GPU resources
        _hud?.Cleanup();
        CleanupImageTextures();
        CleanupEditorGrid();
        _csm?.Dispose();
        _csm = null;
        _light?.DisposeLocalShadow();

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
        _hud?.Cleanup();
        _hud = null;
        CleanupImageTextures();
        CleanupEditorGrid();
    }
}
