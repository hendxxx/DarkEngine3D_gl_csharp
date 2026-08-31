using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Utils;
using DarkEngine3D_gl_csharp.Engine.Visual;
using DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Scene
{
    /// <summary>
    /// Main game scene. Contains the full game loop logic (input, physics, AI, rendering, HUD).
    /// Created by LoadingScene after all resources are loaded.
    /// </summary>
    public unsafe class GameScene : IScene
    {
        /// <summary>If >= 0, load this save slot on Enter(). Set by MainMenuScene before starting the game.</summary>
        public static int PendingLoadSlot = -1;

        public string Name => "GameScene";

        //  Per-scene render properties 
        private readonly SceneRenderProperties _renderProperties = new()
        {
            BackgroundColor = new System.Numerics.Vector3(0.07f, 0.13f, 0.17f),
            VSync = true,
            FaceCulling = CullMode.Back,
            FrontFaceWinding = WindingOrder.CCW,
            WireframeMode = false,
            DepthTest = true,
            Blending = false,
        };
        public SceneRenderProperties RenderProperties => _renderProperties;

        //  Dependencies 
        private readonly SceneManager _sceneManager;
        private Camera _camera;
        private Lights _light;
        private Texture[]? _skyTextures;
        private TerrainChunk? _gameTerrainChunk;
        private Skybox? _skybox;
        private HUD? _hud;
        private ObjectManager? _objectManager;

        //  Render state (initialized in Enter) 
        private PostProcessStack? _ppStack;
        private CSM? _csm;
        // Shader uniform locations (terrain)
        private uint _terrainShader;
        private int _terrainShadowMap0Loc, _terrainShadowMap1Loc, _terrainShadowMap2Loc;
        private int _terrainLightSpaceLoc0, _terrainLightSpaceLoc1, _terrainLightSpaceLoc2;
        private int _terrainCascadeEndsLoc0, _terrainCascadeEndsLoc1, _terrainCascadeEndsLoc2;

        // Shader uniform locations (gltf)
        private uint _gltfShader;
        private int _gltfShadowMap0Loc, _gltfShadowMap1Loc, _gltfShadowMap2Loc;
        private int _gltfLightSpaceLoc0, _gltfLightSpaceLoc1, _gltfLightSpaceLoc2;
        private int _gltfCascadeEndsLoc0, _gltfCascadeEndsLoc1, _gltfCascadeEndsLoc2;

        // Shadow shader uniform locations
        private uint _shadowShader;
        private int _shadowModelLoc, _shadowLightSpaceLoc;
        private uint _shadowSkinnedShader;
        private int _shadowSkinnedModelLoc, _shadowSkinnedLightSpaceLoc, _shadowSkinnedJointsLoc;
        private uint _shadowStaticAlphaShader;
        private int _shadowStaticAlphaModelLoc, _shadowStaticAlphaLightSpaceLoc;

        // Main shader view/projection locations
        private uint _shaderProgram;
        private int _projectionLocation, _viewLocation;

        //  Per-frame state 
        private float _time = 0f;
        private float _deltaTime = 0f;
        private CameraMode _lastCameraMode = CameraMode.FirstPerson;
        private float _lastTargetShoulderOffset;

        //  Pause menu (responsive grid) 
        private const int PauseItemCount = 6;
        private const int PauseBtnColStart = 3;
        private const int PauseBtnColEnd = 9;
        private const float PauseBtnH = 50f;
        private const float PauseBtnSpacing = 14f;
        private bool _paused = false;
        private int _pauseSelection = 0;
        private int _pauseLastHovered = -1; // only update selection from hover when this changes
        private bool _escapeWasDown = false;
        private bool _f5WasDown = false;
        private bool _f6WasDown = false;
        private bool _pauseUpWasDown = false;
        private bool _pauseDownWasDown = false;
        private bool _pauseEnterWasDown = false;

        //  Input cooldown after unpausing (prevents menu click bleed) 
        private float _inputCooldown = 0f;

        //  Save/Load system 
        private bool _saveLoadActive = false;
        private bool _isSaveMode = false; // true=save, false=load
        private bool _saveLoadWasAlreadyPaused = false; // true=opened from pause menu
        private int _saveLoadSelection = 0;
        private bool _saveLoadUpWasDown = false;
        private bool _saveLoadDownWasDown = false;
        private bool _saveLoadEnterWasDown = false;
        private bool _saveLoadEscapeWasDown = false;
        private bool _saveLoadLeftWasDown = false;
        private bool _saveLoadRightWasDown = false;
        private SaveSlotInfo[] _saveSlots = new SaveSlotInfo[SaveManager.NumSlots];
        private string _saveNotification = "";
        private float _saveNotificationTimer = 0f;
        private int _pendingScreenshotSlot = -1;

        //  Exit confirmation dialog 
        private const float _confirmDlgScale = 1.4f;
        private bool _confirmingExit = false;
        private int _confirmSelection = 0; // 0 = No, 1 = Yes
        private int _confirmLastHovered = -1;
        private bool _confirmLeftWasDown = false;
        private bool _confirmRightWasDown = false;
        private bool _confirmEnterWasDown = false;
        private bool _confirmEscapeWasDown = false;

        //  In-game settings panel (accessed from pause menu) 
        private bool _settingsActive = false;
        private int _settingsSelection = 0;
        private int _settingsLastHovered = -1;
        private bool _settingsUpWasDown = false;
        private bool _settingsDownWasDown = false;
        private bool _settingsLeftWasDown = false;
        private bool _settingsRightWasDown = false;
        private bool _settingsEnterWasDown = false;
        private bool _settingsEscapeWasDown = false;

        private readonly string[] _inGameSettingLabels = [
            "Field of View",
            "Mouse Sensitivity",
            "Quality",
            "VSync",
            "BACK",
        ];
        private readonly string[][] _inGameSettingOptions = [
            ["60", "70", "80", "90", "100", "110"],
            ["0.25", "0.50", "0.75", "1.0", "1.5", "2.0", "3.0"],
            ["LOW", "MEDIUM", "HIGH", "ULTRA"],
            ["OFF", "ON"],
        ];
        /// <summary>Current live value index for each setting. 0=FOV, 1=Mouse, 2=Shadow, 3=VSync. Synced in Enter().</summary>
        private int[] _inGameSettingValues = [0, 3, 3, 0];
        /// <summary>Snapshot taken when settings panel opens; restored on cancel (BACK/ESC).</summary>
        private int[] _settingsSnapshot = [0, 3, 3, 0];

        // Shadow quality presets shared via ShadowPresets.CascadeSizes (no local field needed)


        // ── Scene root element (holds the UI hierarchy tree) ──
        private readonly UIElement _sceneRoot = new()
        {
            Name = "GameScene",
            Type = UIElementType.Scene,
            IsVisible = false,
        };

        private int _renderedTris;

        //  IDE focus-camera state
        private Vector3? _cameraFocusPivot = null;
        private float _focusHoldTimer = 0f;
        //  Gizmo size shortcut edge tracking (= / - keys)
        private bool _gizmoPlusWasDown = false;
        private bool _gizmoMinusWasDown = false;
        //  Gizmo pivot override reset (ESC key)
        private bool _gizmoEscWasDown = false;
        //  Cursor visibility tracking (avoid redundant GLFW calls)
        private bool _prevCursorShown = true;
        //  Per-frame render timing (ms) 
        private System.Diagnostics.Stopwatch _renderTimer = new();
        private System.Diagnostics.Stopwatch _frameTotalTimer = new();
        private double _terrainTimeMs;
        private double _objectsTimeMs;
        private double _postProcessTimeMs;
        private double _totalRenderTimeMs;

        public GameScene(SceneManager sceneManager, Camera camera, Lights light)
        {
            _sceneManager = sceneManager;
            _camera = camera;
            _light = light;
        }

        /// <summary>Populate IDEBridge with the latest frame's data for IDE panels.</summary>
        private void UpdateBridgeData(float deltaTime)
        {
            var bridge = _sceneManager.Bridge;
            if (bridge == null) return;

            // Performance
            bridge.Fps = Glfw.GetLastFPS();
            bridge.FrameMs = deltaTime * 1000f;

            // Render-time breakdown (ms) for the in-game overlay debug panel
            bridge.RenderTerrainMs = (float)_terrainTimeMs;
            bridge.RenderObjectsMs = (float)_objectsTimeMs;
            // PostFX removed
            bridge.RenderTotalMs = (float)_totalRenderTimeMs;

            // Per-object render timings for the Render Time panel
            bridge.ObjectRenderTimings = _objectManager?.LastRenderTimings;

            // Camera
            bridge.Camera = _camera;
            bridge.CameraPosition = _camera.Position;
            bridge.CameraYaw = _camera.Yaw;
            bridge.CameraPitch = _camera.Pitch;

            // Scene name
            bridge.SceneName = "GameScene";

            // ── UI Hierarchy ──
            bridge.SceneRoot = _sceneRoot;
            bridge.SceneRootElements = [_sceneRoot];

            // ── Viewport click → select element ──
            if (bridge.IsViewportClicked)
            {
                // First try UI elements (pause menu, settings, etc.)
                var uiHit = UIElement.HitTestPoint(_sceneRoot.Children,
                    bridge.ViewportClickX, bridge.ViewportClickY);
                if (uiHit != null)
                {
                    bridge.SelectedUIElement = uiHit;
                    bridge.SelectedObject = null;
                    bridge.SelectedAgent = null;
                }
                else if (_objectManager != null && _camera != null)
                {
                    // No UI hit → raycast against 3D objects
                    _camera.ScreenToRay(bridge.ViewportClickX, bridge.ViewportClickY,
                        bridge.SceneTextureWidth, bridge.SceneTextureHeight,
                        out Vector3 rayOrigin, out Vector3 rayDir);

                    float closestHit = float.MaxValue;
                    GltfObject? hitObject = null;
                    CharacterAgent? hitAgent = null;

                    // Check animated objects (characters)
                    var animObjs = _objectManager.GetObjects();
                    for (int i = 0; i < animObjs.Count; i++)
                    {
                        var obj = animObjs[i];
                        if (!obj.IsVisible) continue;
                        var aabb = obj.WorldAABB;

                        if (Helpers.ObjectHelpers.AABB.RayIntersectsAABB(rayOrigin, rayDir, aabb,
                                out float tMin, out float _) && tMin > 0f && tMin < closestHit)
                        {
                            closestHit = tMin;
                            hitObject = obj;
                            hitAgent = null;
                        }
                    }

                    // Find agent for hit object
                    if (hitObject != null)
                    {
                        var agents = _objectManager.Agents;
                        for (int ai = 0; ai < agents.Count; ai++)
                        {
                            if (ReferenceEquals(agents[ai].GameObject, hitObject))
                            {
                                hitAgent = agents[ai];
                                break;
                            }
                        }
                    }

                    // Check static objects (trees, walls, rocks) — compete equally with animated
                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        var staticObjs = mgr.GetObjects();
                        for (int si = 0; si < staticObjs.Count; si++)
                        {
                            var sobj = staticObjs[si];
                            if (!sobj.IsVisible) continue;

                            if (ObjectHelpers.AABB.RayIntersectsAABB(rayOrigin, rayDir,
                                    sobj.CachedWorldAABB, out float tMin, out float _) &&
                                tMin > 0f && tMin < closestHit)
                            {
                                closestHit = tMin;
                                hitObject = null; // static object, not animated
                                hitAgent = null;
                            }
                        }
                    }

                    // ── Also raycast against editor objects ──
                    // Gizmo priority: if the click hit the SINGLE selection gizmo (one gizmo
                    // at the group center for multi-select), keep that selection (gizmo wins
                    // over overlapping objects). Only pick by nearest ray when no gizmo was hit.
                    bool gizmoClaimedClick = false;
                    if (bridge.SelectedEditorObject != null && bridge.EditorGizmo != null
                        && bridge.SceneTextureWidth > 0 && bridge.SceneTextureHeight > 0)
                    {
                        int vpwG = bridge.SceneTextureWidth;
                        int vphG = bridge.SceneTextureHeight;
                        float glClickYG = vphG - bridge.ViewportClickY;
                        var clickScreen = new Vector2(bridge.ViewportClickX, glClickYG);
                        if (bridge.GetEditorGizmoCenter() is Vector3 gizmoCenterG)
                        {
                            if (bridge.EditorGizmo.HitTest(clickScreen, _camera, gizmoCenterG, vpwG, vphG)
                                != TransformGizmo.Axis.None)
                            {
                                gizmoClaimedClick = true;
                            }
                        }
                    }

                    var editorMgr = bridge.EditorObjectManager;
                    if (editorMgr != null && !gizmoClaimedClick)
                    {
                        float editorHitDist;
                        Vector3 editorHitPoint;
                        var hitEditor = editorMgr.Raycast(rayOrigin, rayDir, out editorHitDist, out editorHitPoint);
                        if (hitEditor != null && editorHitDist > 0f && editorHitDist < closestHit)
                        {
                            closestHit = editorHitDist;
                            // Ctrl/Shift+Click adds to the multi-selection (primary = last clicked);
                            // plain click replaces the selection with just this object.
                            bridge.SelectEditorObject(hitEditor, bridge.ViewportCtrlHeld || bridge.ViewportShiftHeld);
                            bridge.SelectedObject = null;
                            bridge.SelectedAgent = null;
                            bridge.SelectedUIElement = null;
                            Console.WriteLine($"[Raycast] Selected editor object: {hitEditor.Name}");
                        }
                        else
                        {
                            // Clear editor selection when clicking anywhere that doesn't hit an editor object
                            // (empty space / grid / game object / static object) — unless Ctrl/Shift is held,
                            // which keeps the current multi-selection intact.
                            if (!bridge.ViewportCtrlHeld && !bridge.ViewportShiftHeld)
                                bridge.SelectEditorObject(null);
                        }
                    }

                    // Log if a static object was hit (closest but no animated match)
                    if (hitObject == null && closestHit < float.MaxValue)
                    {
                        Console.WriteLine("[Raycast] Hit static object");
                    }

                    // Set selection
                    if (hitObject != null)
                    {
                        bridge.SelectedObject = hitObject;
                        bridge.SelectedAgent = hitAgent;
                        bridge.SelectedUIElement = null;
                        Console.WriteLine($"[Raycast] Selected: {hitObject.GetHashCode():X8}");
                    }
                    else
                    {
                        // Static object or nothing — clear selection
                        bridge.SelectedObject = null;
                        bridge.SelectedAgent = null;
                    }
                }
            }

            // Auto-select first child ONLY if nothing at all is selected.
            // Don't override when a 3D/editor object is selected (user clicked viewport to select an object).
            if (bridge.SelectedUIElement == null
                && bridge.SelectedObject == null
                && bridge.SelectedEditorObject == null
                && _sceneRoot.Children.Count > 0)
            {
                bridge.SelectedUIElement = _sceneRoot.Children[0];
            }

            // Object counts
            if (_objectManager != null)
            {
                bridge.AnimatedObjectCount = _objectManager.GetObjects().Count;
                bridge.StaticObjectCount = _objectManager.staticObjectManagers?.Sum(m => m?.GetTotalObject ?? 0) ?? 0;
                bridge.TotalObjects = _objectManager.TotalObjects;
                bridge.DrawnObjects = _objectManager.DrawnObjects;
                bridge.TotalTriangles = (_objectManager.TotalObjectTriangles) + (TerrainChunk.GetTotalMapTriangles());
                bridge.RenderedTriangles = (_objectManager.RenderedTriangles) + (_renderedTris);
                bridge.AllAgents = _objectManager.Agents;
                bridge.AllObjects = _objectManager.GetObjects();

                // Wire select-by-index: maps hierarchy list index to agent or object
                bridge.SelectObjectByIndex = (int idx) =>
                {
                    var agents = _objectManager.Agents;
                    var objects = _objectManager.GetObjects();

                    if (idx >= 0 && idx < agents.Count)
                    {
                        // Select agent
                        bridge.SelectedAgent = agents[idx];
                        bridge.SelectedObject = agents[idx].GameObject;
                    }
                    else
                    {
                        // Find the (idx - agents.Count)-th non-agent object
                        int nonAgentIdx = idx - agents.Count;
                        int found = 0;
                        for (int oi = 0; oi < objects.Count; oi++)
                        {
                            bool isAgent = false;
                            for (int ai = 0; ai < agents.Count; ai++)
                            {
                                if (ReferenceEquals(agents[ai].GameObject, objects[oi]))
                                { isAgent = true; break; }
                            }
                            if (!isAgent)
                            {
                                if (found == nonAgentIdx)
                                {
                                    bridge.SelectedObject = objects[oi];
                                    bridge.SelectedAgent = null;
                                    return;
                                }
                                found++;
                            }
                        }
                    }
                };

                // Wire focus-camera action: sets pivot + timer so camera stays focused for ~2s
                bridge.FocusCameraOnSelected = () =>
                {
                    var obj = bridge.SelectedObject;
                    if (obj == null) return;

                    Vector3 targetPos = obj.Position;
                    _cameraFocusPivot = targetPos;
                    _focusHoldTimer = 2f;

                    // Place camera a few units behind, looking at the object
                    Vector3 camOffset = new Vector3(0f, 2.5f, 5f);
                    _camera.Position = targetPos + camOffset;
                    _camera.PushPosition(_camera.Position);

                    // Calculate yaw/pitch to look at object center
                    Vector3 dir = Vector3.Normalize(targetPos - _camera.Position);
                    _camera.Yaw = MathF.Atan2(dir.X, dir.Z) * 180f / MathF.PI;
                    _camera.Pitch = -MathF.Asin(dir.Y) * 180f / MathF.PI;
                    _camera.UpdateVectors();

                    // Adjust camera distance for the focus
                    float dist = Vector3.Distance(_camera.Position, targetPos);
                    CameraConfig.TargetCameraDistance = Math.Clamp(dist, 1f, 20f);
                };
            }
        }

        /// <summary>
        /// Called by LoadingScene after all assets are loaded to pass resources to GameScene.
        /// </summary>
        public void SetResources(
            Texture[] skyTextures,
            TerrainChunk terrain,
            Skybox skybox,
            HUD hud,
            ObjectManager objectManager)
        {
            _skyTextures = skyTextures;
            _gameTerrainChunk = terrain;
            _skybox = skybox;
            _hud = hud;
            _objectManager = objectManager;
        }

        public void Enter()
        {
            if (_skyTextures == null || _gameTerrainChunk == null || _skybox == null || _hud == null || _objectManager == null)
            {
                Console.Error.WriteLine("[GameScene] Error: Resources not set before Enter()!");
                return;
            }

            _shaderProgram = Shader.GetShaderProgram();
            _projectionLocation = GL.GetUniformLocation(_shaderProgram, "projection");
            _viewLocation = GL.GetUniformLocation(_shaderProgram, "view");

            //  Post-processing 
            _ppStack = new PostProcessStack(Glfw.WindowWidth, Glfw.WindowHeight);
            var invertPass = new InvertPass(Shader.GetInvertPassShaderProgram());
            // ppStack.AddPass(invertPass);

            //  CSM
            _csm = new CSM(Config.ShadowSettings.CascadeSizes[0]);

            // Cache terrain shader uniform locations
            _terrainShader = Shader.GetShaderProgram();
            _terrainShadowMap0Loc = GL.GetUniformLocation(_terrainShader, "shadowMap0");
            _terrainShadowMap1Loc = GL.GetUniformLocation(_terrainShader, "shadowMap1");
            _terrainShadowMap2Loc = GL.GetUniformLocation(_terrainShader, "shadowMap2");
            _terrainLightSpaceLoc0 = GL.GetUniformLocation(_terrainShader, "lightSpaceMatrices[0]");
            _terrainLightSpaceLoc1 = GL.GetUniformLocation(_terrainShader, "lightSpaceMatrices[1]");
            _terrainLightSpaceLoc2 = GL.GetUniformLocation(_terrainShader, "lightSpaceMatrices[2]");
            _terrainCascadeEndsLoc0 = GL.GetUniformLocation(_terrainShader, "cascadeEnds[0]");
            _terrainCascadeEndsLoc1 = GL.GetUniformLocation(_terrainShader, "cascadeEnds[1]");
            _terrainCascadeEndsLoc2 = GL.GetUniformLocation(_terrainShader, "cascadeEnds[2]");

            // Cache gltf shader uniform locations
            _gltfShader = GltfShader.GetShaderProgram();
            _gltfShadowMap0Loc = GL.GetUniformLocation(_gltfShader, "shadowMap0");
            _gltfShadowMap1Loc = GL.GetUniformLocation(_gltfShader, "shadowMap1");
            _gltfShadowMap2Loc = GL.GetUniformLocation(_gltfShader, "shadowMap2");
            _gltfLightSpaceLoc0 = GL.GetUniformLocation(_gltfShader, "lightSpaceMatrices[0]");
            _gltfLightSpaceLoc1 = GL.GetUniformLocation(_gltfShader, "lightSpaceMatrices[1]");
            _gltfLightSpaceLoc2 = GL.GetUniformLocation(_gltfShader, "lightSpaceMatrices[2]");
            _gltfCascadeEndsLoc0 = GL.GetUniformLocation(_gltfShader, "cascadeEnds[0]");
            _gltfCascadeEndsLoc1 = GL.GetUniformLocation(_gltfShader, "cascadeEnds[1]");
            _gltfCascadeEndsLoc2 = GL.GetUniformLocation(_gltfShader, "cascadeEnds[2]");

            // Cache shadow shader uniform locations
            _shadowShader = Shader.GetShadowShaderProgram();
            _shadowModelLoc = GL.GetUniformLocation(_shadowShader, "model");
            _shadowLightSpaceLoc = GL.GetUniformLocation(_shadowShader, "lightSpaceMatrix");

            _shadowSkinnedShader = Shader.GetShadowSkinnedShaderProgram();
            _shadowSkinnedModelLoc = GL.GetUniformLocation(_shadowSkinnedShader, "model");
            _shadowSkinnedLightSpaceLoc = GL.GetUniformLocation(_shadowSkinnedShader, "lightSpaceMatrix");
            _shadowSkinnedJointsLoc = GL.GetUniformLocation(_shadowSkinnedShader, "u_Joints");

            _shadowStaticAlphaShader = Shader.GetShadowStaticAlphaShaderProgram();
            _shadowStaticAlphaModelLoc = GL.GetUniformLocation(_shadowStaticAlphaShader, "model");
            _shadowStaticAlphaLightSpaceLoc = GL.GetUniformLocation(_shadowStaticAlphaShader, "lightSpaceMatrix");


            // Subscribe to window resize event for camera aspect ratio updates
            Glfw.OnWindowResized += OnWindowResized;

            _lastCameraMode = _camera.CurrentMode;
            _lastTargetShoulderOffset = CameraConfig.TargetShoulderOffset;

            _paused = false;
            _pauseSelection = 0;
            _pauseLastHovered = -1;
            _inputCooldown = 0.15f;
            _confirmLastHovered = -1;
            _saveLoadActive = false;
            _saveLoadSelection = 0;
            _saveNotification = "";

            _settingsActive = false;
            _settingsSelection = 0;

            // Sync in-game settings with current config/camera state
            _inGameSettingValues[0] = Math.Clamp(((int)_camera.BaseFoV - 60) / 10, 0, 5);

            // Reverse-lookup mouse sensitivity index
            float sens = Mouse.Sensitivity;
            float[] mouseMult = [0.25f, 0.50f, 0.75f, 1.0f, 1.5f, 2.0f, 3.0f];
            _inGameSettingValues[1] = 3; // default 1.0Ã—
            for (int m = 0; m < mouseMult.Length; m++)
            {
                if (Math.Abs(sens - 0.1f * mouseMult[m]) < 0.001f)
                { _inGameSettingValues[1] = m; break; }
            }

            // Quality preset: read the live preset index (MSAA + shadow + filter master).
            _inGameSettingValues[2] = Config.QualitySettings.Current;

            //  Load pending save (set by MainMenuScene Continue/Load Game) 
            if (PendingLoadSlot >= 0)
            {
                int slotToLoad = PendingLoadSlot;
                PendingLoadSlot = -1; // Reset immediately to prevent double-load

                var savedData = SaveManager.Load(slotToLoad);
                if (savedData != null)
                {
                    if (_objectManager != null && _objectManager.PlayerAgent != null && _objectManager.PlayerObject != null)
                    {
                        var loadPos = new Vector3(savedData.PlayerX, savedData.PlayerY, savedData.PlayerZ);
                        _objectManager.PlayerAgent.Position = loadPos;
                        _objectManager.PlayerObject.Position = loadPos;
                        _objectManager.PlayerAgent.Heading = savedData.PlayerHeading;
                        _objectManager.PlayerAgent.SetHealth(savedData.PlayerHealth);

                        if (Enum.IsDefined(typeof(CameraMode), savedData.CameraMode))
                            _camera.CurrentMode = (CameraMode)savedData.CameraMode;
                        _camera.Pitch = savedData.CameraPitch;
                        CameraConfig.TargetCameraDistance = savedData.CameraDistance;
                        _camera.ApplyPreset();
                        _lastCameraMode = _camera.CurrentMode;

                        _light.WorldTime = savedData.WorldTime;

                        Console.WriteLine($"[GameScene] Loaded save from slot {slotToLoad}: {savedData.SaveTime}");
                    }
                }
            }


            // ── Scene starts blank! No .ing file is loaded automatically. ──
            // User can create UI via the IDE SceneDetail panel (+ Add button),
            // or use the "↻ Reload" button to load from a previously saved .ing file.
            _sceneRoot.ClearChildren();

            // ── Register scene root for IDE Save All ──
            SceneAssetSerializer.RegisterSceneRoot("GameScene", _sceneRoot);

            Mouse.ShowMouse(false);
            _prevCursorShown = false;

            Console.WriteLine("[GameScene] Engine Running...");
        }

        /// <summary>Handle window resize â€” update viewport, camera aspect ratio and
        /// recreate the MSAA scene FBO at the new size (keeps the image crisp).</summary>
        private void OnWindowResized(int width, int height)
        {
            _camera.UpdateAspectRatio((float)width, (float)height);
            _ppStack?.Resize(width, height);
        }

        public void Update(float deltaTime)
        {
            nint window = Glfw.GetWindow();
            _deltaTime = deltaTime;

            // ── Cursor visibility: show when locked, viewport not focused, or paused ──
            bool viewportFocused = _sceneManager.Bridge?.IsViewportFocused ?? true;
            bool shouldShowCursor =  !viewportFocused || _paused;
            if (shouldShowCursor != _prevCursorShown)
            {
                Mouse.ShowMouse(shouldShowCursor);
                _prevCursorShown = shouldShowCursor;
            }

            // ── Input gate: when InGameActive=false, block ALL keyboard + mouse ──
            // The IDE F9 button is the only way to re-enable ingame input.
            bool ingameActive = _sceneManager.Bridge?.InGameActive ?? true;
            if (!ingameActive)
            {
                // Skip ALL keyboard/mouse input processing, but still run game logic below
                goto SkipInput;
            }

                //  ESCAPE: always toggle pause (ESC always opens/closes the menu) 
                bool escapeDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);
                if (escapeDown && !_escapeWasDown && !_confirmingExit)
                {
                    _paused = !_paused;
                    if (_paused)
                    {
                        _pauseSelection = 0;
                        Mouse.ShowMouse(true);
                    }
                    else
                    {
                        Mouse.ShowMouse(false);
                        Mouse.ResetState();
                    }
                }
                _escapeWasDown = escapeDown;

                //  F5/F6: Save/Load 
                bool f5Down = Keyboard.IsKeyDown(window, Const.GLFW_KEY_F5);
                bool f6Down = Keyboard.IsKeyDown(window, Const.GLFW_KEY_F6);

                if (f5Down && !_f5WasDown && !_saveLoadActive)
                {
                    OpenSaveLoadUI(true); // Save mode
                }
                if (f6Down && !_f6WasDown && !_saveLoadActive)
                {
                    OpenSaveLoadUI(false); // Load mode
                }
                _f5WasDown = f5Down;
                _f6WasDown = f6Down;

                //  Pause menu overlay input 
                if (_paused)
                {
                    if (_saveLoadActive)
                        HandleSaveLoadInput(window);
                    else if (_confirmingExit)
                        HandleConfirmInput(window);
                    else if (_settingsActive)
                        HandleSettingsInput(window);
                    else
                        HandlePauseInput(window);

                    if (!Config.GameplayConfig.PauseOnEsc)
                        return;
                }
                else
                {
                    _pauseUpWasDown = false;
                    _pauseDownWasDown = false;
                    _pauseEnterWasDown = false;
                    _settingsActive = false;
                }
            
            _time += deltaTime;

            //  Player input & camera control — only when not paused AND input not locked
            if (!_paused)
            {
                //  Input cooldown: skip game input for ~0.15s after unpausing to prevent menu click bleed 
                if (_inputCooldown > 0f)
                {
                    _inputCooldown -= deltaTime;
                    // Still allow camera to run (no mouse delta), skip character input
                }
                else
                {
                    //  Camera mode / freelook 
                    if (_camera.CurrentMode == CameraMode.FirstPerson)
                    {
                        _camera.freeLook = false;
                    }
                    else
                    {
                        _camera.freeLook =
                            (Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT_ALT) ||
                             Keyboard.IsKeyDown(window, Const.GLFW_KEY_RIGHT_ALT));
                    }

                    // 1. Mouse → yaw/pitch → vectors
                    Mouse.Update(window, _camera);
                    _camera.UpdateVectors();

                    CameraConfig.TargetShoulderOffset = _lastTargetShoulderOffset;

                    // 2. Third-person keeps ALT free-look
                    if (!_camera.freeLook && _objectManager != null)
                        _objectManager.PlayerAgent.Heading = _camera.Yaw;

                    // 3. Update keyboard
                    Keyboard.Update(window, _light, _camera, deltaTime, _gameTerrainChunk);



                    // 3A. Check if camera mode changed and notify player
                    if (_camera.CurrentMode != _lastCameraMode && _objectManager != null)
                    {
                        _lastCameraMode = _camera.CurrentMode;
                        _objectManager.PlayerAgent.OnCameraModeChanged(_camera.CurrentMode);
                    }
                }
            }

        SkipInput:
            // ── Editor fly mode: when viewport is focused, use WASD + mouse look
            // (fly mode) for camera navigation — works in both editor and in-game mode.
            bool editorFlyMode = _sceneManager.Bridge?.IsViewportFocused ?? false;

            // ── Gizmo size shortcuts: = to increase, - to decrease ──
            // Only active when viewport is focused (not during gameplay or when typing in other panels).
            // Edge-triggered so each press changes size by 0.1 increment.
            if (_sceneManager.Bridge?.IsViewportFocused ?? false)
            {
                bool gizmoPlusDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_EQUAL);
                bool gizmoMinusDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_MINUS);
                if (gizmoPlusDown && !_gizmoPlusWasDown)
                {
                    Config.CameraConfig.GizmoSize = Math.Clamp(Config.CameraConfig.GizmoSize + 0.1f, 0.25f, 3.0f);
                    Console.WriteLine($"[Gizmo] Size increased to {Config.CameraConfig.GizmoSize:F2}");
                }
                if (gizmoMinusDown && !_gizmoMinusWasDown)
                {
                    Config.CameraConfig.GizmoSize = Math.Clamp(Config.CameraConfig.GizmoSize - 0.1f, 0.25f, 3.0f);
                    Console.WriteLine($"[Gizmo] Size decreased to {Config.CameraConfig.GizmoSize:F2}");
                }
                _gizmoPlusWasDown = gizmoPlusDown;
                _gizmoMinusWasDown = gizmoMinusDown;

                // ── ESC: reset gizmo pivot override ──
                bool escDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);
                if (escDown && !_gizmoEscWasDown)
                {
                    var selObj = _sceneManager.Bridge?.SelectedEditorObject;
                    if (selObj != null && selObj.GizmoPivotOverride != null)
                    {
                        selObj.GizmoPivotOverride = null;
                        Console.WriteLine($"[Gizmo] Pivot override reset for '{selObj.Name}'");
                    }
                }
                _gizmoEscWasDown = escDown;
            }

            if (_objectManager != null)
            {
                // 4. Update agents (AI, physics, animations) — always runs
                _objectManager.Update(deltaTime);

                // 4A. Update player movement — skip when in editor fly mode
                if (!editorFlyMode && !_paused && _inputCooldown <= 0f)
                {
                    _objectManager.PlayerAgent.Move(window, _camera, deltaTime, _gameTerrainChunk, Vector3.Zero, 0f);
                }

                // 4B. Update NPC AI + movement — skip when in editor fly mode
                if (!editorFlyMode)
                {
                    _objectManager.UpdateAgents(window, deltaTime, _gameTerrainChunk, _camera);
                }

                // 5. Camera — use fly mode in editor, game camera otherwise
                if (editorFlyMode)
                {
                    // Update mouse deltas (game input was skipped via goto SkipInput)
                    // Mouse.Update() updates DeltaX/Y which SetCameraFlyMode() reads.
                    // The Yaw/Pitch changes from Mouse.Update() are harmless because
                    // SetCameraFlyMode() overwrites them with smoothYaw/smoothPitch.
                    Mouse.Update(window, _camera);
                    _camera.SetCameraFlyMode(window, _deltaTime, true);
                    // Reset focus pivot when in editor fly mode
                    _cameraFocusPivot = null;
                    _focusHoldTimer = 0f;
                }
                else
                {
                    Vector3 camPivot = _cameraFocusPivot ?? _objectManager.PlayerAgent.Position;
                    _camera.SetCamera(window, camPivot, _gameTerrainChunk, deltaTime, null);

                    // 5A. Focus hold timer: decrement and release back to player
                    if (_focusHoldTimer > 0f)
                    {
                        _focusHoldTimer -= deltaTime;
                        if (_focusHoldTimer <= 0f)
                        {
                            _cameraFocusPivot = null;
                            _focusHoldTimer = 0f;
                        }
                    }
                }
            }

            // 6. Update Light (always runs)
            // Apply the first placed Light/Sky marker to the in-game lights + skybox so the
            // scene lighting, sun position, cloud coverage and time of day match the editor.
            var envBridge = _sceneManager.Bridge;
            if (envBridge?.EditorObjectManager is { } envMgr)
            {
                EditorObject? skyM = null;
                foreach (var obj in envMgr.Objects)
                    if (skyM == null && obj.PrimitiveType == EditorPrimitiveType.Sky) skyM = obj;
                // Sky drives a DIRECT light — prefer it over other light types.
                EditorObject? lightM = EditorObject.PickSunLight(envMgr.Objects);
                EditorObject.ApplyEnvironmentMarkers(lightM, skyM, _light, _skybox, deltaTime);

                // ── Collect Point/Spot Light markers as local lights so they illuminate
                //    every object in the game (terrain, models, primitives) ──
                _light.CollectLocalLights(envMgr.Objects);
            }
            else
            {
                _light.LocalLights.Clear();
            }
            _light.Update(deltaTime, _camera.Position);

            //  CSM Shadow Pass (always runs for visual updates behind menu) 
            if (_csm != null)
            {
                _csm.UpdateMatrices(_camera, _light.ShadowDirStable);

                for (int i = 0; i < CSM.NumCascades; i++)
                {
                    _csm.BindFramebuffer(i);

                    Matrix4x4 lightSpace = _csm.LightSpaceMatrices[i];

                    GL.UseProgram(_shadowShader);
                    Visual.ShadowUniforms.UploadNormalBias(_shadowShader);
                    unsafe
                    {
                        GL.UniformMatrix4fv(_shadowLightSpaceLoc, 1, false, (float*)&lightSpace);
                    }

                    GL.UseProgram(_shadowSkinnedShader);
                    Visual.ShadowUniforms.UploadNormalBias(_shadowSkinnedShader);
                    unsafe
                    {
                        GL.UniformMatrix4fv(_shadowSkinnedLightSpaceLoc, 1, false, (float*)&lightSpace);
                    }

                    GL.UseProgram(_shadowStaticAlphaShader);
                    unsafe
                    {
                        GL.UniformMatrix4fv(_shadowStaticAlphaLightSpaceLoc, 1, false, (float*)&lightSpace);
                    }

                    _objectManager?.RenderShadow(_camera, _csm, i,
                            _shadowSkinnedShader, _shadowSkinnedModelLoc, _shadowSkinnedJointsLoc,
                            _shadowStaticAlphaShader, _shadowStaticAlphaModelLoc);

                    _gameTerrainChunk?.RenderShadow(_camera, _csm, i, _shadowShader, _shadowModelLoc);

                    // ── EditorObjectManager shadow pass (skipped when the viewport
                    // "Shadow" toggle is off — editor objects stop casting shadows). ──
                    var editorObjMgr = _sceneManager.Bridge?.EditorObjectManager;
                    if (editorObjMgr != null && (_sceneManager.Bridge?.ShowShadows ?? true))
                    {
                        editorObjMgr.RenderShadow(_camera, _csm, i);
                    }
                }

                // ── Local light (Point/Spot) shadow pass — each light renders the scene
                // into its own shadow map (cube for point, perspective map for spot) so
                // every light casts its own shadow, not just the sun. Reuses the same
                // casters as the CSM pass above via the per-cascade draw callback. ──
                _light.LocalShadow ??= new LocalLightShadow();
                _light.LocalShadow.RenderShadowPass(
                    _camera, _light.LocalLights,
                    _shadowShader, _shadowSkinnedShader, _shadowStaticAlphaShader,
                    (CSM csm, int ci) =>
                    {
                        _objectManager?.RenderShadow(_camera, csm, ci,
                            _shadowSkinnedShader, _shadowSkinnedModelLoc, _shadowSkinnedJointsLoc,
                            _shadowStaticAlphaShader, _shadowStaticAlphaModelLoc);
                        _gameTerrainChunk?.RenderShadow(_camera, csm, ci, _shadowShader, _shadowModelLoc);
                        var eo = _sceneManager.Bridge?.EditorObjectManager;
                        if (eo != null && (_sceneManager.Bridge?.ShowShadows ?? true))
                            eo.RenderShadow(_camera, csm, ci);
                    });

                // Restore default viewport and framebuffer
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
                GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);
            }


        }

        public void Render()
        {
            if (_ppStack == null || _csm == null || _skybox == null || _hud == null) return;

            // Wireframe toggle from keyboard overrides the per-scene setting
            bool wireframeMode = Keyboard.GetIsWireframe();
            _renderProperties.WireframeMode = wireframeMode;
            bool ideActive = _sceneManager.IsIdeActive;

            //  Start per-frame render timing 
            _renderTimer.Restart();
            _frameTotalTimer.Restart();

            //  MAIN RENDER PASS — always use FBO so Viewport has a texture when IDE is active
            if (wireframeMode)
            {
                // Wireframe: render directly to screen, skip postprocess (F1 conflicts with PP)
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
                GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);
                GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
            }
            else
            {
                _ppStack.BindSceneFBO();
                GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
            }

            // 3. Draw Skybox
            _skybox.Draw(_camera, _light, _deltaTime, _skyTextures!, _gameTerrainChunk);

            //  Bind CSM Shadow Maps 
            GL.ActiveTexture(Const.GL_TEXTURE0 + 6);
            GL.BindTexture(Const.GL_TEXTURE_2D, _csm.ShadowTextures[0]);

            GL.ActiveTexture(Const.GL_TEXTURE0 + 7);
            GL.BindTexture(Const.GL_TEXTURE_2D, _csm.ShadowTextures[1]);

            GL.ActiveTexture(Const.GL_TEXTURE0 + 8);
            GL.BindTexture(Const.GL_TEXTURE_2D, _csm.ShadowTextures[2]);

            // Upload shadow uniforms for Terrain
            GL.UseProgram(_terrainShader);
            GL.Uniform1i(_terrainShadowMap0Loc, 6);
            GL.Uniform1i(_terrainShadowMap1Loc, 7);
            GL.Uniform1i(_terrainShadowMap2Loc, 8);

            unsafe
            {
                fixed (float* p0 = &_csm.LightSpaceMatrices[0].M11)
                    GL.UniformMatrix4fv(_terrainLightSpaceLoc0, 1, false, p0);
                fixed (float* p1 = &_csm.LightSpaceMatrices[1].M11)
                    GL.UniformMatrix4fv(_terrainLightSpaceLoc1, 1, false, p1);
                fixed (float* p2 = &_csm.LightSpaceMatrices[2].M11)
                    GL.UniformMatrix4fv(_terrainLightSpaceLoc2, 1, false, p2);
            }
            GL.Uniform1f(_terrainCascadeEndsLoc0, _csm.CascadeEnds[0]);
            GL.Uniform1f(_terrainCascadeEndsLoc1, _csm.CascadeEnds[1]);
            GL.Uniform1f(_terrainCascadeEndsLoc2, _csm.CascadeEnds[2]);

            // Upload shadow uniforms for glTF
            GL.UseProgram(_gltfShader);
            GL.Uniform1i(_gltfShadowMap0Loc, 6);
            GL.Uniform1i(_gltfShadowMap1Loc, 7);
            GL.Uniform1i(_gltfShadowMap2Loc, 8);

            unsafe
            {
                fixed (float* p0 = &_csm.LightSpaceMatrices[0].M11)
                    GL.UniformMatrix4fv(_gltfLightSpaceLoc0, 1, false, p0);
                fixed (float* p1 = &_csm.LightSpaceMatrices[1].M11)
                    GL.UniformMatrix4fv(_gltfLightSpaceLoc1, 1, false, p1);
                fixed (float* p2 = &_csm.LightSpaceMatrices[2].M11)
                    GL.UniformMatrix4fv(_gltfLightSpaceLoc2, 1, false, p2);
            }
            GL.Uniform1f(_gltfCascadeEndsLoc0, _csm.CascadeEnds[0]);
            GL.Uniform1f(_gltfCascadeEndsLoc1, _csm.CascadeEnds[1]);
            GL.Uniform1f(_gltfCascadeEndsLoc2, _csm.CascadeEnds[2]);

            // 4. Ensure terrain shader has view/projection uniforms
            _camera.SetViewAndProjection(_viewLocation, _projectionLocation);

            //  Timing: terrain render start
            _renderTimer.Restart();
            _renderedTris = 0;
            //  Determine preview mode early so debug visualizations can be hidden
            //  Preview mode hides editor gizmos/helpers but keeps WASD camera fly working.
            bool isPreviewMode = _sceneManager.Bridge?.IsPreviewMode ?? false;
            if (_gameTerrainChunk != null)
            {
                Plane[]? cullFreezePlanes = null;
                if (Keyboard.GetCullFreezeMode())
                {
                    var freezeVP = Keyboard.GetCullFreezeViewProj();
                    cullFreezePlanes = TerrainChunk.ExtractFrustumPlanes(freezeVP);
                }

                _renderedTris = _gameTerrainChunk.Render(_camera, _gameTerrainChunk.GetFrozenPlanes(), cullFreezePlanes, skipDebug: isPreviewMode);
                 
            }

            //  Record terrain timing, start object timing
            _terrainTimeMs = _renderTimer.Elapsed.TotalMilliseconds;
            _renderTimer.Restart();

            //  glTF Object Manager — always-run frustum + distance cull for static objects
            if (_objectManager != null)
            {
                var frustumVP = _camera.GetViewMatrix() * _camera.GetProjectionMatrix();
                var frustumPlanes = StaticObjectManager.ExtractCameraFrustum(frustumVP);
                float farSq = _camera.FarDist * _camera.FarDist;

                if (_objectManager.staticObjectManagers != null)
                {
                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())
                        {
                            float dx = sobj.Position.X - _camera.Position.X;
                            float dz = sobj.Position.Z - _camera.Position.Z;
                            float distSq = dx * dx + dz * dz;
                            if (distSq > farSq)
                                sobj.IsVisible = false;
                            else if (!StaticObjectManager.IsAABBInFrustum(frustumPlanes, sobj.CachedWorldAABB, 3f))
                                sobj.IsVisible = false;
                        }
                    }
                }

                _objectManager.CullFreezeEnabled = Keyboard.GetCullFreezeMode();
                _objectManager.CullFreezeViewProj = Keyboard.GetCullFreezeViewProj();

                // Only capture per-object render timings while the Render Time panel is open
                _objectManager.CaptureRenderTimings = _sceneManager.Bridge?.CaptureRenderTimings ?? false;

                _objectManager.Draw(_camera, _light, _csm);
                _objectManager.DrawHealthBars(_camera, _hud);
            }

            // ── EditorObjectManager draw (editor primitives) ──
            var editorMgrBridge = _sceneManager.Bridge;
            var editorMgr = editorMgrBridge?.EditorObjectManager;
            if (editorMgr != null)
            {
                // Pass selection highlight color so selected objects get a mesh wireframe outline
                // Hidden in preview/in-game mode for a clean view
                Vector3? wireCol = (!isPreviewMode && editorMgrBridge is { SelectedEditorObjects.Count: > 0 })
                    ? editorMgrBridge.SelectionHighlights.EditorObject : null;
                // Hide sky gizmo in preview/in-game mode — only show in edit mode
                bool showSky = !isPreviewMode;
                // Hide all editor gizmos (2D markers, frustum, light gizmo, sky gizmo) in preview/in-game mode
                editorMgr.Draw(_camera, _light, _csm, wireCol, editorMgrBridge?.SelectedEditorObjects, showSkyGizmo: showSky, showEditorGizmos: !isPreviewMode);
            }

            // ── Editor debug grid (edit mode only, toggled from the viewport toolbar) ──
            if (editorMgrBridge is { ShowDebugGrid: true } && !isPreviewMode)
            {
                TerrainChunk.DrawDebugGrid(_camera);
            }

            //  Record objects timing, start post-process timing
            _objectsTimeMs = _renderTimer.Elapsed.TotalMilliseconds;
            _renderTimer.Restart();

            //  Post Process (render SceneFBO to screen) — run always; IDE scene stays in FBO for Viewport panel
            if (!wireframeMode)
            {
                _ppStack.RunStack(Glfw.WindowWidth, Glfw.WindowHeight, _time);
            }

            //  Record post-process timing
            _postProcessTimeMs = _renderTimer.Elapsed.TotalMilliseconds;
            _renderTimer.Restart();

            //  Capture screenshot right after post-process, before any UI overlays 
            if (_pendingScreenshotSlot >= 0)
            {
                int slot = _pendingScreenshotSlot;
                _pendingScreenshotSlot = -1; // Reset immediately
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
                SaveManager.CaptureScreenshot(slot);
            }

            //  When IDE is active, scene renders to FBO only — expose texture for Viewport panel
            if (ideActive && _ppStack != null)
            {
                var bridge = _sceneManager.Bridge;
                if (bridge != null)
                {
                    bridge.SceneTextureID = _ppStack.SceneColorTex;
                    bridge.SceneTextureWidth = Glfw.WindowWidth;
                    bridge.SceneTextureHeight = Glfw.WindowHeight;
                }
            }

            //  Pause blur overlay (skip in wireframe mode)
            if (_paused && !_confirmingExit && !_settingsActive)
            {
                if (!wireframeMode)
                    _ppStack.RenderBlurred(Glfw.WindowWidth, Glfw.WindowHeight, 5f, 1.0f);
            }

            //  Debug BBox Wireframe + LOD Labels (toggled with P key)
            //  Hidden in preview/in-game mode for a clean view.
            if (Keyboard.GetShowBBox() && _objectManager != null && !isPreviewMode)
            {
                GL.Disable(Const.GL_DEPTH_TEST);

                _objectManager.DrawDebugAABBs(_camera, null);

                // Animated objects â€” shapes drawn by ObjectManager.DrawDebugAABBs (capsule for characters)
                // Only draw LOD labels here
                var animObjs = _objectManager.GetObjects();
                for (int oi = 0; oi < animObjs.Count; oi++)
                {
                    var aabb = animObjs[oi].WorldAABB;
                    int animLod = animObjs[oi].AnimLOD;
                    Vector3 center = (aabb.Min + aabb.Max) * 0.5f;
                    DrawLODLabel(center, animLod, animObjs[oi].IsPlayer);
                }

                if (_objectManager.staticObjectManagers != null)
                {
                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;

                        foreach (var sobj in mgr.GetObjects())
                        {
                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                            if (distSq >= _camera.FarDist * _camera.FarDist) continue;

                            bool isCulled = !sobj.IsVisible && Config.OcclusionConfig.UseOcclusion;
                            Vector3 debugColor = isCulled ? new Vector3(1f, 0f, 0f)
                                : (mi == 1 ? new Vector3(1f, 0f, 1f) : new Vector3(1f, 1f, 0f));

                            TerrainChunk.DrawAABBWireframe(sobj.CachedWorldAABB, debugColor, _camera);

                            // LOD label for static objects
                            Vector3 sobjCenter = (sobj.CachedWorldAABB.Min + sobj.CachedWorldAABB.Max) * 0.5f;
                            DrawLODLabel(sobjCenter, sobj.CurrentLOD, false);
                        }
                    }
 
                }

                GL.Enable(Const.GL_DEPTH_TEST);
            }

            //  Billboard Atlas Debug Overlay (M key toggle — independent from AABB debug)
            //  Hidden in preview/in-game mode for a clean view.
            if (Keyboard.GetShowBillboardAtlas() && _objectManager != null && !isPreviewMode)
            {
                GL.Disable(Const.GL_DEPTH_TEST);
                GL.DepthMask(false);
                _objectManager.DrawBillboardAtlasDebug();
                GL.DepthMask(true);
                GL.Enable(Const.GL_DEPTH_TEST);
            }

            //  PAUSE MENU / SETTINGS / CONFIRM / SAVE-LOAD OVERLAY
            if (_paused)
            {
                if (_saveLoadActive)
                {
                    if (!wireframeMode)
                        _ppStack.RenderBlurred(Glfw.WindowWidth, Glfw.WindowHeight, 5f, 1.0f);
                    RenderSaveLoadPanel();
                }
                else if (_confirmingExit)
                {
                    if (!wireframeMode)
                        _ppStack.RenderBlurred(Glfw.WindowWidth, Glfw.WindowHeight, 5f, 1.0f);
                    RenderConfirmDialog();
                }
                else if (_settingsActive)
                {
                    if (!wireframeMode)
                        _ppStack.RenderBlurred(Glfw.WindowWidth, Glfw.WindowHeight, 5f, 1.0f);
                    RenderSettingsPanel();
                }
                else
                    RenderPauseMenu();
            }

            //  Frustum Frozen Debug Visualization (toggled with P key — freezes at capture)
            var _frozenCorners = Keyboard.GetFrozenCorners();
            if (_frozenCorners != null && _camera != null)
            {
                GL.Disable(Const.GL_DEPTH_TEST);

                // 12 edges of the frozen frustum -> 24 vertices (GL_LINES)
                var _verts = new List<Vector3>(24);
                // Near plane (indices 0,1,3,2)
                _verts.Add(_frozenCorners[0]); _verts.Add(_frozenCorners[1]);
                _verts.Add(_frozenCorners[1]); _verts.Add(_frozenCorners[3]);
                _verts.Add(_frozenCorners[3]); _verts.Add(_frozenCorners[2]);
                _verts.Add(_frozenCorners[2]); _verts.Add(_frozenCorners[0]);
                // Far plane (indices 4,5,7,6)
                _verts.Add(_frozenCorners[4]); _verts.Add(_frozenCorners[5]);
                _verts.Add(_frozenCorners[5]); _verts.Add(_frozenCorners[7]);
                _verts.Add(_frozenCorners[7]); _verts.Add(_frozenCorners[6]);
                _verts.Add(_frozenCorners[6]); _verts.Add(_frozenCorners[4]);
                // Connecting lines: near->far
                _verts.Add(_frozenCorners[0]); _verts.Add(_frozenCorners[4]);
                _verts.Add(_frozenCorners[1]); _verts.Add(_frozenCorners[5]);
                _verts.Add(_frozenCorners[2]); _verts.Add(_frozenCorners[6]);
                _verts.Add(_frozenCorners[3]); _verts.Add(_frozenCorners[7]);

                // Draw in cyan
                TerrainChunk.DrawLineSegments(_verts, new Vector3(0f, 1f, 1f), _camera);
                GL.Enable(Const.GL_DEPTH_TEST);
            }

            // ── Selection outline highlight (IDE mode) ──
            // Draws a pulsing yellow inverted-hull outline around the selected glTF object.
            if (_sceneManager.IsIdeActive && _sceneManager.Bridge != null)
            {
                var bridge = _sceneManager.Bridge;
                var selObj = bridge.SelectedObject;
                
                if (selObj != null)
                {
                    // Stencil-based inverted-hull outline (customizable color) with pulsing effect
                    // Uses Environment.TickCount to stay in sync with EditorObjectManager's pulse
                    float pulse = 0.6f + 0.4f * MathF.Sin(Environment.TickCount / 1000f * 4f);
                    Vector3 outlineCol = bridge.SelectionHighlights.GltfObject * pulse;

                    // ── Pass 1: Write stencil mask ──
                    GL.Enable(Const.GL_STENCIL_TEST);
                    GL.StencilMask(0xFF);
                    GL.Clear(Const.GL_STENCIL_BUFFER_BIT);
                    GL.StencilFunc(Const.GL_ALWAYS, 1, 0xFF);
                    GL.StencilOp(Const.GL_KEEP, Const.GL_KEEP, Const.GL_REPLACE);
                    GL.ColorMask(false, false, false, false);

                    selObj.DrawOutlineStencil(_camera);

                    GL.ColorMask(true, true, true, true);

                    // ── Pass 2: Draw expanded back faces where stencil != 1 ──
                    GL.StencilFunc(Const.GL_NOTEQUAL, 1, 0xFF);
                    GL.StencilOp(Const.GL_KEEP, Const.GL_KEEP, Const.GL_KEEP);

                    selObj.DrawOutline(_camera, outlineCol);

                    GL.Disable(Const.GL_STENCIL_TEST);
                }
                
                // Editor object outline is now drawn inside EditorObjectManager.Draw()
                // (replaces the old wireframe approach)

                // Gizmo is now rendered by SceneManager after this Render() returns,
                // so the position is updated in Update() before rendering.
            }

            //  Record total render time (from the dedicated total timer, not the section timer)
            _totalRenderTimeMs = _frameTotalTimer.Elapsed.TotalMilliseconds;

            // HUD 
            int totalMapTris = TerrainChunk.GetTotalMapTriangles();
            int totalObjTris = _objectManager?.TotalObjectTriangles ?? 0;
            int renderedObjTris = _objectManager?.RenderedTriangles ?? 0;
            int totalAllTris = totalMapTris + totalObjTris;
            int renderedAllTris = _renderedTris + renderedObjTris;
            string gTime = _light.GetFormattedTime();

            string title1 = $" [ {gTime} ]";
            float frameMs = _deltaTime * 1000f;
            string title2 = $" FPS: {Glfw.GetLastFPS()}  ({frameMs:F1}ms)  | t={_terrainTimeMs:N1}ms  o={_objectsTimeMs:N1}ms  fx={_postProcessTimeMs:N1}ms  tot={_totalRenderTimeMs:N1}ms";
            string freeze = Keyboard.GetCullFreezeMode() ? " [CULL FREEZE]" : "";
            string title3 = $" MODE: {_camera.CurrentMode}{freeze}";
            string title4 = $" TRIS: {renderedAllTris:N0} / {totalAllTris:N0}  (terrain {_renderedTris:N0} | objects {renderedObjTris:N0})";
            string title5 = $" POS: X ={_camera.Position.X:N2} Y={_camera.Position.Y:N2} Z={_camera.Position.Z:N2}";
            string title6 = "";
            if (_objectManager != null)
            {
                int culledTotal = _objectManager.TotalObjects - _objectManager.DrawnObjects;
                int animFrustum = _objectManager.CulledByFrustum;
                int animOcc = _objectManager.CulledByOcclusion;
                int animCount = _objectManager.GetObjects().Count;
                int staticCount = _objectManager.staticObjectManagers.Sum(m => m?.GetTotalObject ?? 0);
                // Debug: log actual values once
                //Console.WriteLine($"[HUD Debug] staticObjectManagers.Count={_objectManager.staticObjectManagers.Count} staticCount={staticCount} TotalObjects={_objectManager.TotalObjects} DrawnObjects={_objectManager.DrawnObjects} animCount={animCount}");
                title6 = $" Objects: {_objectManager.DrawnObjects:N0} drawn / {culledTotal:N0} culled / {_objectManager.TotalObjects:N0} total  ({animCount} chars + {staticCount:N0} static)";
            }
            //Save notification
            if (_saveNotificationTimer > 0f)
            {
                _saveNotificationTimer -= _deltaTime;
                float fade = Math.Min(1f, _saveNotificationTimer);
                var notifExt = _hud.GetTextExtents(_saveNotification);
                float notifX = Glfw.WindowWidth * 0.5f - notifExt.Width * 0.5f;
                float notifY = Glfw.WindowHeight * 0.15f;
                _hud.DrawText(_saveNotification, notifX, notifY, new Vector3(0.3f, 0.9f, 0.4f) * fade);
            }
            //  HUD debug overlay — hidden in preview/in-game mode for a clean view
            if (!isPreviewMode)
            {
                _hud.DrawText(title1, 10, 60, new Vector3(1, 0, 0));
                float debugLineH = _hud.MeasureTextHeight(title1) + 6f;
                _hud.DrawText(title2, 10, 60 + debugLineH, new Vector3(1, 0, 0));
                _hud.DrawText(title3, 10, 60 + debugLineH * 2, new Vector3(1, 1, 0));
                _hud.DrawText(title4, 10, 60 + debugLineH * 3, new Vector3(1, 0, 0));
                _hud.DrawText(title5, 10, 60 + debugLineH * 4, new Vector3(1, 0, 0));
                _hud.DrawText(title6, 10, 60 + debugLineH * 5, new Vector3(1, 0, 0));
            }

            // ── Flush all queued HUD commands ──
            _hud.Flush();

            // NOTE: The rolling FPS counter is driven by SceneManager's central main loop
            // (Glfw.UpdateFPS) so it stays live in every scene — do NOT call Glfw.ShowFPS
            // here anymore, it would double-count frames while GameScene is active.

            //  Populate IDEBridge AFTER rendering, so DrawnObjects/RenderedTriangles are current-frame
            UpdateBridgeData(_deltaTime);


        }

        /// <summary>Handle pause menu input (keyboard + mouse).</summary>
        private void HandlePauseInput(nint window)
        {
            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;
            var grid = new GridLayout(w, h);
            float pauseBtnW = grid.SpanW(PauseBtnColStart, PauseBtnColEnd);
            float titleY = h * 0.28f;
            float startY = titleY + 70f;
            string worldStatus = Config.GameplayConfig.PauseOnEsc ? "RUNNING" : "PAUSED";
            string[] pauseItems = ["RESUME", "SAVE GAME", "LOAD GAME", "SETTINGS", $"World: [{worldStatus}]", "BACK TO MAIN MENU"];

            float pauseBx = (w - pauseBtnW) * 0.5f;
            _hud.ClearButtons();
            for (int i = 0; i < PauseItemCount; i++)
            {
                float by = startY + i * (PauseBtnH + PauseBtnSpacing);
                int captured = i;
                _hud.AddButton(pauseItems[i], pauseBx, by, pauseBtnW, PauseBtnH,
                    () => ExecutePauseAction(captured));
            }
            _hud.UpdateButtons();

            // Sync keyboard selection from hover (only when hover changes)
            int hoveredIdx = -1;
            for (int i = 0; i < _hud.ButtonCount; i++)
                if (_hud.Buttons[i].IsHovered)
                    hoveredIdx = i;
            if (hoveredIdx >= 0 && hoveredIdx != _pauseLastHovered)
                _pauseSelection = hoveredIdx;
            _pauseLastHovered = hoveredIdx;

            // Keyboard navigation
            bool upDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_W);
            bool downDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);
            bool enterDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE);

            if (upDown && !_pauseUpWasDown)
                _pauseSelection = (_pauseSelection - 1 + PauseItemCount) % PauseItemCount;
            if (downDown && !_pauseDownWasDown)
                _pauseSelection = (_pauseSelection + 1) % PauseItemCount;
            if (enterDown && !_pauseEnterWasDown)
                ExecutePauseAction(_pauseSelection);

            _pauseUpWasDown = upDown;
            _pauseDownWasDown = downDown;
            _pauseEnterWasDown = enterDown;
        }

        /// <summary>Handle save/load slot selection input (mouse + keyboard), grid-aligned.</summary>
        private void HandleSaveLoadInput(nint window)
        {
            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;

            SaveSlotUI.GetPanelRect(w, h, out float panelX, out float panelW,
                out float panelY, out float panelH, out float startY);

            float bx = panelX + GridLayout.Gutter * 0.5f;
            float bw = panelW - GridLayout.Gutter;

            _hud.ClearButtons();
            for (int i = 0; i < SaveManager.NumSlots; i++)
            {
                float sy = startY + i * (SaveSlotUI.SlotRowH + SaveSlotUI.SlotGap);
                int captured = i;
                _hud.AddButton("", bx, sy, bw, SaveSlotUI.SlotRowH, () =>
                {
                    if (_isSaveMode)
                        SaveGameToSlot(captured);
                    else if (_saveSlots[captured].HasData)
                        LoadGameFromSlot(captured);
                });
            }
            _hud.UpdateButtons();

            for (int i = 0; i < _hud.ButtonCount; i++)
                if (_hud.Buttons[i].IsHovered)
                    _saveLoadSelection = i;
            //  Keyboard navigation 
            bool upDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_W);
            bool downDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);
            bool enterDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE);
            bool escDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);
            bool leftDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_A);
            bool rightDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_D);

            if (upDown && !_saveLoadUpWasDown)
                _saveLoadSelection = (_saveLoadSelection - 1 + SaveManager.NumSlots) % SaveManager.NumSlots;
            if (downDown && !_saveLoadDownWasDown)
                _saveLoadSelection = (_saveLoadSelection + 1) % SaveManager.NumSlots;

            // Enter â†’ confirm save/load
            if (enterDown && !_saveLoadEnterWasDown)
            {
                if (_isSaveMode)
                    SaveGameToSlot(_saveLoadSelection);
                else if (_saveSlots[_saveLoadSelection].HasData)
                    LoadGameFromSlot(_saveLoadSelection);
            }

            // ESC â†’ close save/load panel
            if (escDown && !_saveLoadEscapeWasDown)
            {
                CloseSaveLoadUI();
            }

            // Left/Right to cycle slots (wraparound)
            if ((leftDown && !_saveLoadLeftWasDown) || (rightDown && !_saveLoadRightWasDown))
            {
                // Cycle through empty slots quickly
                int dir = (leftDown && !_saveLoadLeftWasDown) ? -1 : 1;
                _saveLoadSelection = (_saveLoadSelection + dir + SaveManager.NumSlots) % SaveManager.NumSlots;
            }

            _saveLoadUpWasDown = upDown;
            _saveLoadDownWasDown = downDown;
            _saveLoadEnterWasDown = enterDown;
            _saveLoadEscapeWasDown = escDown;
            _saveLoadLeftWasDown = leftDown;
            _saveLoadRightWasDown = rightDown;

            // Keep other edge flags in sync
            _escapeWasDown = escDown;
        }

        /// <summary>Open the save/load UI overlay.</summary>
        private void OpenSaveLoadUI(bool saveMode)
        {
            _saveLoadActive = true;
            _isSaveMode = saveMode;
            _saveLoadSelection = 0;
            _saveLoadWasAlreadyPaused = _paused; // Remember if we were already paused
            _paused = true;
            Mouse.ShowMouse(true);

            // Refresh slot info
            _saveSlots = SaveManager.GetAllSlots();

            // Load thumbnails in the background (lazy load on render)
            for (int i = 0; i < _saveSlots.Length; i++)
            {
                if (_saveSlots[i].HasData)
                {
                    SaveManager.GetOrLoadThumbnail(ref _saveSlots[i], SaveSlotUI.ThumbW, SaveSlotUI.ThumbH);
                }
            }

            // Sync edge-tracking flags to prevent input bleed
            nint win = Glfw.GetWindow();
            _saveLoadUpWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_W);
            _saveLoadDownWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_S);
            _saveLoadEnterWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_SPACE);
            _saveLoadEscapeWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_ESCAPE);
            _saveLoadLeftWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_A);
            _saveLoadRightWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_D);

            Console.WriteLine($"[GameScene] Save/Load UI opened (mode: {(saveMode ? "Save" : "Load")})");
        }

        /// <summary>Close the save/load UI and return to pause menu or game.</summary>
        private void CloseSaveLoadUI()
        {
            _saveLoadActive = false;
            _saveNotification = "";

            // If save/load was opened from the game (not from pause menu), unpause
            if (!_saveLoadWasAlreadyPaused && !_settingsActive && !_confirmingExit)
            {
                _paused = false;
                _inputCooldown = 0.15f;
                Mouse.ShowMouse(false);
                Mouse.ResetState();
            }
            // Otherwise, return to the pause menu naturally (keep _paused = true)
            Console.WriteLine("[GameScene] Save/Load UI closed");
        }

        /// <summary>Save current game state to a slot.</summary>
        private void SaveGameToSlot(int slotIndex)
        {
            if (_objectManager == null || _objectManager.PlayerAgent == null) return;

            var data = new SaveData
            {
                PlayerX = _objectManager.PlayerAgent.Position.X,
                PlayerY = _objectManager.PlayerAgent.Position.Y,
                PlayerZ = _objectManager.PlayerAgent.Position.Z,
                PlayerHeading = _objectManager.PlayerAgent.Heading,
                PlayerHealth = _objectManager.PlayerAgent.Health,
                CameraMode = (int)_camera.CurrentMode,
                CameraDistance = CameraConfig.TargetCameraDistance,
                CameraYaw = _camera.Yaw,
                CameraPitch = _camera.Pitch,
                WorldTime = _light.WorldTime,
                SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            SaveManager.Save(slotIndex, data);
            _pendingScreenshotSlot = slotIndex; // Capture at end of Render() instead

            _saveNotification = $"Game saved to Slot {slotIndex + 1}!";
            _saveNotificationTimer = 3f;

            // Refresh slot info and thumbnail
            _saveSlots[slotIndex] = SaveManager.GetSlotInfo(slotIndex);
            if (_saveSlots[slotIndex].HasThumbnail)
                SaveManager.GetOrLoadThumbnail(ref _saveSlots[slotIndex], SaveSlotUI.ThumbW, SaveSlotUI.ThumbH);

            // Close save UI after saving
            _saveLoadActive = false;
            if (!_settingsActive && !_confirmingExit)
            {
                _paused = false;
                _inputCooldown = 0.15f;
                Mouse.ShowMouse(false);
                Mouse.ResetState();
            }

            Console.WriteLine($"[GameScene] Game saved to slot {slotIndex}");
        }

        /// <summary>Load game state from a slot.</summary>
        private void LoadGameFromSlot(int slotIndex)
        {
            var data = SaveManager.Load(slotIndex);
            if (data == null)
            {
                Console.WriteLine($"[GameScene] Failed to load slot {slotIndex}");
                return;
            }

            if (_objectManager == null || _objectManager.PlayerAgent == null || _objectManager.PlayerObject == null)
                return;

            // Restore player position
            var pos = new Vector3(data.PlayerX, data.PlayerY, data.PlayerZ);
            _objectManager.PlayerAgent.Position = pos;
            _objectManager.PlayerObject.Position = pos;
            _objectManager.PlayerAgent.Heading = data.PlayerHeading;
            _objectManager.PlayerAgent.SetHealth(data.PlayerHealth);

            // Restore camera
            if (Enum.IsDefined(typeof(CameraMode), data.CameraMode))
                _camera.CurrentMode = (CameraMode)data.CameraMode;
            _camera.Yaw = data.CameraYaw;
            _camera.Pitch = data.CameraPitch;
            CameraConfig.TargetCameraDistance = data.CameraDistance;
            _camera.ApplyPreset();
            _lastCameraMode = _camera.CurrentMode;

            // Restore world time
            _light.WorldTime = data.WorldTime;

            _saveNotification = $"Game loaded from Slot {slotIndex + 1}!";
            _saveNotificationTimer = 3f;

            // Close load UI after loading
            _saveLoadActive = false;
            _paused = false;
            _inputCooldown = 0.15f;
            Mouse.ShowMouse(false);
            Mouse.ResetState();

            Console.WriteLine($"[GameScene] Game loaded from slot {slotIndex}: {data.SaveTime}");
        }

        /// <summary>Render the save/load slot selection panel overlay (grid-aligned).</summary>
        private void RenderSaveLoadPanel()
        {
            if (_hud == null) return;

            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;
            var grid = new GridLayout(w, h);

            string title = _isSaveMode ? "SAVE GAME" : "LOAD GAME";

            SaveSlotUI.GetPanelRect(w, h, out float panelX, out float panelW,
                out float panelY, out float panelH, out float startY);

            // Panel frame
            SaveSlotUI.RenderPanelFrame(_hud, w, h, title, _saveSlots, panelX, panelW, panelY, panelH);

            // Slots
            SaveSlotUI.RenderSlots(_hud, w, h, _saveSlots, _saveLoadSelection, panelX, panelW, startY, title);

            // Hint text â€” centered using grid
            string hint = _isSaveMode
                ? "Select a slot to save  -  Enter to confirm  -  Esc to cancel"
                : "Select a slot to load  -  Enter to confirm  -  Esc to cancel";
            float hintY = panelY + panelH - 24f;
            var hintExt = _hud.GetTextExtents(hint);
            float hintCenterX = grid.CenterX(SaveSlotUI.PanelColStart, SaveSlotUI.PanelColEnd);
            float hintX = hintCenterX - hintExt.Width * 0.5f;
            _hud.DrawText(hint, hintX, hintY, new Vector3(0.35f, 0.35f, 0.45f));
        }

        /// <summary>Handle in-game settings panel input (mouse + keyboard).
        /// Each setting cycles immediately â€” no APPLY button needed.
        /// Index 5 (BACK) returns to the pause menu.</summary>
        private void HandleSettingsInput(nint window)
        {
            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;

            float panelX = w * 0.25f, panelW = w * 0.5f;
            float rowH = 42f, rowGap = 8f;
            float titleY = h * 0.28f;
            float startY = titleY + 70f;

            _hud.ClearButtons();
            for (int i = 0; i < _inGameSettingLabels.Length; i++)
            {
                float ry = startY + i * (rowH + rowGap);
                int captured = i;
                if (i == _inGameSettingLabels.Length - 1)
                {
                    _hud.AddButton("", panelX, ry, panelW, rowH,
                        () => ApplyAndExitInGameSettings());
                }
                else
                {
                    _hud.AddButton("", panelX, ry, panelW, rowH,
                        () => CycleInGameSetting(captured, 1));
                }
            }
            _hud.UpdateButtons();

            int hoveredIdx = -1;
            for (int i = 0; i < _hud.ButtonCount; i++)
                if (_hud.Buttons[i].IsHovered)
                    hoveredIdx = i;
            if (hoveredIdx >= 0 && hoveredIdx != _settingsLastHovered)
                _settingsSelection = hoveredIdx;
            _settingsLastHovered = hoveredIdx;
            //  Keyboard navigation 
            bool upDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_W);
            bool downDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_S);
            bool leftDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_A);
            bool rightDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_D);
            bool enterDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE);
            bool escDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);

            if (upDown && !_settingsUpWasDown)
                _settingsSelection = (_settingsSelection - 1 + _inGameSettingLabels.Length) % _inGameSettingLabels.Length;
            if (downDown && !_settingsDownWasDown)
                _settingsSelection = (_settingsSelection + 1) % _inGameSettingLabels.Length;

            // LEFT/RIGHT: cycle setting (not on BACK row)
            if (leftDown && !_settingsLeftWasDown && _settingsSelection < _inGameSettingLabels.Length - 1)
                CycleInGameSetting(_settingsSelection, -1);
            if (rightDown && !_settingsRightWasDown && _settingsSelection < _inGameSettingLabels.Length - 1)
                CycleInGameSetting(_settingsSelection, 1);

            // Enter â†’ BACK (save and exit) or cycle forward
            if (enterDown && !_settingsEnterWasDown)
            {
                if (_settingsSelection == _inGameSettingLabels.Length - 1) // BACK
                    ApplyAndExitInGameSettings();
                else
                    CycleInGameSetting(_settingsSelection, 1);
            }

            // ESC â†’ save and exit: save setting changes and return to pause menu
            if (escDown && !_settingsEscapeWasDown)
            {
                ApplyAndExitInGameSettings();
            }

            _settingsUpWasDown = upDown;
            _settingsDownWasDown = downDown;
            _settingsLeftWasDown = leftDown;
            _settingsRightWasDown = rightDown;
            _settingsEnterWasDown = enterDown;
            _settingsEscapeWasDown = escDown;
        }

        /// <summary>Apply and save settings, then exit settings panel.</summary>
        private void ApplyAndExitInGameSettings()
        {
            SaveInGameSettingsToJson();
            _settingsActive = false;
            Console.WriteLine("[Settings] Applied and saved in-game settings.");
        }

        /// <summary>Cancel settings and revert to snapshot values.</summary>
        private void CancelInGameSettings()
        {
            // Revert setting values to snapshot
            for (int i = 0; i < _inGameSettingValues.Length; i++)
            {
                if (_inGameSettingValues[i] != _settingsSnapshot[i])
                {
                    _inGameSettingValues[i] = _settingsSnapshot[i];
                    ApplySingleSetting(i);
                }
            }
            _settingsActive = false;
            Console.WriteLine("[Settings] Cancelled â€” reverted to previous values.");
        }

        /// <summary>Apply a single setting by index using the current _inGameSettingValues[i].</summary>
        private void ApplySingleSetting(int settingIndex)
        {
            int val = _inGameSettingValues[settingIndex];
            switch (settingIndex)
            {
                case 0: // FOV
                {
                    int fovVal = int.Parse(_inGameSettingOptions[0][val].Replace("Â°", ""));
                    _camera.BaseFoV = fovVal;
                    _camera.FoV = fovVal;
                    break;
                }
                case 1: // Mouse Sensitivity
                {
                    float[] multipliers = [0.25f, 0.50f, 0.75f, 1.0f, 1.5f, 2.0f, 3.0f];
                    Mouse.Sensitivity = 0.1f * multipliers[val];
                    break;
                }
                case 2: // Quality preset (MSAA + shadow resolution + shadow filter)
                {
                    // Route through QualitySettings so the whole preset (MSAA, shadow
                    // quality, shadow filter) applies together; CSM instances rebuild
                    // via ShadowSettings.Version, the scene FBO is recreated for MSAA.
                    Config.QualitySettings.Apply(val);
                    _csm?.Dispose();
                    _csm = new CSM(Config.ShadowSettings.CascadeSizes[0]);
                    _ppStack?.ApplyQuality();
                    break;
                }
                case 3: // VSync
                {
                    Glfw.SetSwapInterval(val == 1 ? 1 : 0);
                    break;
                }
            }

        }

        /// <summary>Cycle an in-game setting and apply immediately (live preview).</summary>
        private void CycleInGameSetting(int settingIndex, int direction)
        {
            int count = _inGameSettingOptions[settingIndex].Length;
            _inGameSettingValues[settingIndex] = (_inGameSettingValues[settingIndex] + direction + count) % count;
            ApplySingleSetting(settingIndex);
            Console.WriteLine($"[Settings] {_inGameSettingLabels[settingIndex]} = {_inGameSettingOptions[settingIndex][_inGameSettingValues[settingIndex]]}");
        }

        /// <summary>Handle confirmation dialog input.</summary>
        private void HandleConfirmInput(nint window)
        {
            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;

            _hud.ClearButtons();
            for (int i = 0; i < 2; i++)
            {
                ConfirmDialog.GetButtonRect(w, h, _confirmDlgScale, i,
                    out float bx, out float btnY, out float btnW, out float btnH);
                int captured = i;
                _hud.AddButton("", bx, btnY, btnW, btnH, () => ExecuteConfirmAction(captured));
            }
            _hud.UpdateButtons();

            int hoveredIdx = -1;
            for (int i = 0; i < _hud.ButtonCount; i++)
                if (_hud.Buttons[i].IsHovered)
                    hoveredIdx = i;
            if (hoveredIdx >= 0 && hoveredIdx != _confirmLastHovered)
                _confirmSelection = hoveredIdx;
            _confirmLastHovered = hoveredIdx;
            bool leftDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_A);
            bool rightDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_D);
            bool enterDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(window, Const.GLFW_KEY_SPACE);
            bool escDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_ESCAPE);

            if (leftDown && !_confirmLeftWasDown)
                _confirmSelection = 0;
            if (rightDown && !_confirmRightWasDown)
                _confirmSelection = 1;
            // Escape = cancel (go back to pause menu) â€” uses its own edge tracking
            if (escDown && !_confirmEscapeWasDown)
            {
                _confirmingExit = false;
                _confirmSelection = 0;
            }
            if (enterDown && !_confirmEnterWasDown)
                ExecuteConfirmAction(_confirmSelection);

            _confirmLeftWasDown = leftDown;
            _confirmRightWasDown = rightDown;
            _confirmEnterWasDown = enterDown;
            _confirmEscapeWasDown = escDown;
        }

        /// <summary>Execute the action for the given pause menu index.</summary>
        private void ExecutePauseAction(int index)
        {
            if (index == 0) // Resume
            {
                _paused = false;
                _inputCooldown = 0.15f;
                Mouse.ShowMouse(false);
                Mouse.ResetState();
            }
            else if (index == 1 || index == 2)
            {
                _hud?.ClearButtons();
                OpenSaveLoadUI(index == 1);
            }
            else if (index == 3) // Settings â€” save snapshot for cancel
            {
                // Save snapshot of current values for cancel/revert
                Array.Copy(_inGameSettingValues, _settingsSnapshot, _inGameSettingValues.Length);
                _hud?.ClearButtons();
                _settingsActive = true;
                _settingsSelection = 0;
                // Sync edge-tracking flags to prevent input bleed/flickering
                nint win = Glfw.GetWindow();
                _settingsUpWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_UP) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_W);
                _settingsDownWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_DOWN) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_S);
                _settingsLeftWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_A);
                _settingsRightWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_D);
                _settingsEnterWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_SPACE);
                _settingsEscapeWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_ESCAPE);
            }
            else if (index == 4) // Toggle PauseOnEsc
            {
                Config.GameplayConfig.PauseOnEsc = !Config.GameplayConfig.PauseOnEsc;
                Console.WriteLine($"[GameScene] PauseOnEsc = {Config.GameplayConfig.PauseOnEsc}");
            }
            else // Back to Main Menu (index 5) â†’ show confirmation
            {
                _hud?.ClearButtons();
                _confirmingExit = true;
                _confirmSelection = 0;
                // Sync edge-tracking flags to prevent input bleed/flickering
                nint win = Glfw.GetWindow();
                _confirmLeftWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_LEFT) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_A);
                _confirmRightWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_RIGHT) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_D);
                _confirmEnterWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_ENTER) || Keyboard.IsKeyDown(win, Const.GLFW_KEY_SPACE);
                _confirmEscapeWasDown = Keyboard.IsKeyDown(win, Const.GLFW_KEY_ESCAPE);
            }
        }

        /// <summary>Save current in-game settings to settings.json so they persist across sessions.</summary>
        private void SaveInGameSettingsToJson()
        {
            var saved = SettingsSave.Load();
            saved.Fov = 60 + _inGameSettingValues[0] * 10;
            saved.MouseSensitivity = _inGameSettingValues[1];
            saved.ShadowQuality = _inGameSettingValues[2];
            saved.VSync = _inGameSettingValues[3] == 1;
            SettingsSave.Save(saved);
            Console.WriteLine("[GameScene] In-game settings saved to settings.json");
        }

        /// <summary>Execute confirmation dialog action.</summary>
        private void ExecuteConfirmAction(int index)
        {
            if (index == 1) // Yes â†’ really exit
            {
                SaveInGameSettingsToJson();
                Console.WriteLine("[GameScene] Returning to Main Menu...");
                MainMenuScene mainMenu = new(_sceneManager, _camera, _light);
                _sceneManager.SwitchScene(mainMenu);
            }
            else // No â†’ go back to pause menu
            {
                _confirmingExit = false;
                _confirmSelection = 0;
            }
        }

        /// <summary>Render the exit confirmation dialog overlay.</summary>
        private void RenderConfirmDialog()
        {
            if (_hud == null) return;

            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;

            ConfirmDialog.DrawBox(_hud, w, h,
                "Exit to Main Menu?", "Any unsaved progress will be lost.",
                ["NO", "YES"],
                [new Vector3(0.22f, 0.28f, 0.45f), new Vector3(0.6f, 0.2f, 0.15f)],
                [new Vector3(0.5f, 0.6f, 1.0f), new Vector3(1.0f, 0.4f, 0.3f)],
                [new Vector3(0.7f, 0.7f, 0.9f), new Vector3(1.0f, 0.5f, 0.4f)],
                _confirmSelection, _confirmDlgScale);

            // Hint â€” centered below dialog bottom, matching main menu style
            float dlgH = ConfirmDialog.BaseDlgH * _confirmDlgScale;
            float dlgBottom = (h - dlgH) * 0.5f + dlgH;
            var chGrid = new GridLayout(w, h);
            float chCenterX = chGrid.CenterX(2, 10);
            string hint = "Arrow keys or mouse to navigate  -  Enter to select  -  Esc to go back";
            var chExt = _hud.GetTextExtents(hint);
            _hud.DrawText(hint, chCenterX - chExt.Width * 0.5f, dlgBottom + 22f,
                new Vector3(0.35f, 0.35f, 0.45f));
        }

        /// <summary>Render the in-game settings panel overlay.</summary>
        private void RenderSettingsPanel()
        {
            if (_hud == null) return;

            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;

            // Dark overlay
            _hud.DrawBox(0, 0, w, h, new Vector3(0f, 0f, 0f) * 0.45f);

            float panelX = w * 0.25f, panelW = w * 0.5f;
            float rowH = 42f, rowGap = 8f;

            // Title â€” centered
            var sgGrid = new GridLayout(w, h);
            float setCenterX = sgGrid.CenterX(2, 10);
            string title = "SETTINGS";
            var titleExt = _hud.GetTextExtents(title);
            float titleX = setCenterX - titleExt.Width * 0.5f;
            float titleY = h * 0.28f;
            _hud.DrawText(title, titleX, titleY, new Vector3(0.9f, 0.9f, 1.0f));

            // Decorative line
            float lineW = 80f;
            float lineX = setCenterX - lineW * 0.5f;
            _hud.DrawBox(lineX, titleY + titleExt.Height + 14f, lineW, 1f, new Vector3(0.4f, 0.5f, 0.9f) * 0.6f);

            // Setting rows
            float startY = titleY + 70f;

            for (int i = 0; i < _inGameSettingLabels.Length; i++)
            {
                float ry = startY + i * (rowH + rowGap);
                bool isSelected = (i == _settingsSelection);
                bool isBackBtn = (i == _inGameSettingLabels.Length - 1);

                // Row background
                if (isSelected)
                {
                    float glowPulse = 0.5f + 0.5f * MathF.Sin(_time * 3f);
                    _hud.DrawBox(panelX - 4f, ry - 3f, panelW + 8f, rowH + 6f,
                        new Vector3(0.3f, 0.4f, 0.9f) * (0.06f + glowPulse * 0.05f));
                }

                if (isBackBtn)
                {
                    // BACK button â€” full button style
                    Vector3 btnBg = isSelected
                        ? new Vector3(0.22f, 0.28f, 0.45f)
                        : new Vector3(0.10f, 0.12f, 0.18f);
                    _hud.DrawBox(panelX, ry, panelW, rowH, btnBg);

                    Vector3 border = isSelected
                        ? new Vector3(0.5f, 0.6f, 1.0f)
                        : new Vector3(0.15f, 0.18f, 0.25f);
                    _hud.DrawBox(panelX, ry, panelW, 1f, border);
                    _hud.DrawBox(panelX, ry + rowH - 1f, panelW, 1f, border);

                    if (isSelected)
                    {
                        _hud.DrawBox(panelX - 3f, ry + 4f, 3f, rowH - 8f, new Vector3(0.4f, 0.5f, 0.9f));
                    }

                    var backExt = _hud.GetTextExtents(_inGameSettingLabels[i]);
                    float backX = panelX + (panelW - backExt.Width) * 0.5f;
                    float backY = backExt.GetCenteredBaselineY(ry, rowH);
                    _hud.DrawText(_inGameSettingLabels[i], backX, backY,
                        isSelected ? new Vector3(0.95f, 0.95f, 1.0f) : new Vector3(0.6f, 0.6f, 0.7f));
                }
                else
                {
                    // Regular setting row â€” label left, value right
                    string label = _inGameSettingLabels[i];
                    string value = _inGameSettingOptions[i][_inGameSettingValues[i]];
                    string display = isSelected ? $"< {value} >" : value;

                    var lblExt = _hud.GetTextExtents(label);
                    float lblY = lblExt.GetCenteredBaselineY(ry, rowH);
                    _hud.DrawText(label, panelX + 14f, lblY,
                        isSelected ? new Vector3(0.9f, 0.9f, 1.0f) : new Vector3(0.6f, 0.6f, 0.75f));

                    var valExt = _hud.GetTextExtents(display);
                    float valX = panelX + panelW - 14f - valExt.Width;
                    float valY = valExt.GetCenteredBaselineY(ry, rowH);
                    _hud.DrawText(display, valX, valY,
                        isSelected ? new Vector3(0.6f, 0.7f, 1.0f) : new Vector3(0.5f, 0.5f, 0.6f));
                }
            }

            // Bottom hint
            string hint = "Arrow keys or mouse to navigate  -  Left/Right to cycle  -  Esc to go back";
            var phGrid = new GridLayout(w, h);
            float phCenterX = phGrid.CenterX(2, 10);
            var phExt = _hud.GetTextExtents(hint);
            _hud.DrawText(hint, phCenterX - phExt.Width * 0.5f,
                startY + _inGameSettingLabels.Length * (rowH + rowGap) + 20f,
                new Vector3(0.35f, 0.35f, 0.45f));
        }

        /// <summary>Render the pause menu overlay on top of the frozen game frame.</summary>
        private void RenderPauseMenu()
        {
            if (_hud == null) return;

            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;

            // Dark overlay
            _hud.DrawBox(0, 0, w, h, new Vector3(0f, 0f, 0f) * 0.45f);

            // Title — centered using grid
            var pgGrid = new GridLayout(w, h);
            float pauseCenterX = pgGrid.CenterX(2, 10);
            string title = "PAUSED";
            var titleExtents = _hud.GetTextExtents(title);
            float titleX = pauseCenterX - titleExtents.Width * 0.5f;
            float titleY = h * 0.28f;
            _hud.DrawText(title, titleX, titleY, new Vector3(0.9f, 0.9f, 1.0f));

            // Decorative line — centered in grid
            float lineW = 120f;
            float lineX = pauseCenterX - lineW * 0.5f;
            _hud.DrawBox(lineX, titleY + titleExtents.Height + 14f, lineW, 1f, new Vector3(0.4f, 0.5f, 0.9f) * 0.6f);

            // Buttons via HUD system
            _hud.DrawButtons(_time, _pauseSelection);
        }

        /// <summary>Draw a color-coded LOD level label at the given world position, projected to screen.</summary>
        private void DrawLODLabel(Vector3 worldPos, int lodLevel, bool isPlayer)
        {
            if (_hud == null) return;

            var viewMat = _camera.GetViewMatrix();
            var projMat = _camera.GetProjectionMatrix();
            var vpMat = viewMat * projMat;

            var clip = Vector4.Transform(new Vector4(worldPos, 1f), vpMat);
            if (clip.W <= 0.05f) return;

            float nx = clip.X / clip.W;
            float ny = clip.Y / clip.W;

            // Outside screen
            if (nx < -1.2f || nx > 1.2f || ny < -1.2f || ny > 1.2f) return;

            float sx = (nx * 0.5f + 0.5f) * Glfw.WindowWidth;
            float sy = (1f - (ny * 0.5f + 0.5f)) * Glfw.WindowHeight;

            // Color-code LOD level
            string label;
            Vector3 color;
            if (isPlayer)
            {
                label = "PLAYER";
                color = new Vector3(0f, 1f, 0f);
            }
            else
            {
                switch (lodLevel)
                {
                    case 0: label = "LOD0"; color = new Vector3(0.2f, 0.9f, 0.2f); break; // green
                    case 1: label = "LOD1"; color = new Vector3(0.2f, 0.5f, 1.0f); break; // blue
                    case 2: label = "LOD2"; color = new Vector3(1.0f, 0.9f, 0.2f); break; // yellow
                    case 3: label = "LOD3"; color = new Vector3(1.0f, 0.3f, 0.2f); break; // red
                    default: label = $"LOD{lodLevel}"; color = new Vector3(0.5f, 0.5f, 0.5f); break;
                }
            }

            _hud.DrawText(label, sx - _hud.GetTextExtents(label).Width * 0.5f, sy - 18f, color, new Vector3(0f, 0f, 0f), 1.5f);
        }

        /// <summary>Spawn random colored cubes above terrain so they fall with gravity.</summary>
        /// <summary>Clear existing physics cubes and respawn new ones above terrain.</summary>
        public void Exit()
        {
            Glfw.OnWindowResized -= OnWindowResized;

            // ── Free HUD GPU resources (font atlases, scratch textures) ──
            _hud?.Cleanup();

            // ── Clear IDE bridge references ──
            var bridge = _sceneManager.Bridge;
            if (bridge != null)
            {
                bridge.SelectedUIElement = null;
                bridge.SceneRootElements = null;
                bridge.SceneRoot = null;
            }

            _csm?.Dispose();
            _light?.DisposeLocalShadow();
            Console.WriteLine("[GameScene] Exited.");
        }

        public void Dispose()
        {
            _hud?.Cleanup();
            _csm?.Dispose();
            _light?.DisposeLocalShadow();
            _objectManager?.Dispose();
            _gameTerrainChunk?.Dispose();
            Console.WriteLine("[GameScene] Disposed.");
        }
    }
}
