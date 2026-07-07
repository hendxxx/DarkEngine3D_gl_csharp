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
        private OcclusionCulling? _occlusionCulling;
        private HiZOcc? _hizOcc;

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
        private int _occlusionFrameCount = 0;
        private int _staticFrustumCulled = 0;
        private int _staticTerrainOccluded = 0;
        private int _staticOcclusionCulled = 0;
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
            "Shadow Quality",
            "VSync",
            "â¬… BACK",
        ];
        private readonly string[][] _inGameSettingOptions = [
            ["60Â°", "70Â°", "80Â°", "90Â°", "100Â°", "110Â°"],
            ["0.25Ã—", "0.50Ã—", "0.75Ã—", "1.0Ã—", "1.5Ã—", "2.0Ã—", "3.0Ã—"],
            ["LOW", "MEDIUM", "HIGH", "ULTRA"],
            ["OFF", "ON"],
        ];
        /// <summary>Current live value index for each setting. 0=FOV, 1=Mouse, 2=Shadow, 3=VSync. Synced in Enter().</summary>
        private int[] _inGameSettingValues = [0, 3, 3, 0];
        /// <summary>Snapshot taken when settings panel opens; restored on cancel (BACK/ESC).</summary>
        private int[] _settingsSnapshot = [0, 3, 3, 0];

        // Shadow quality presets shared via ShadowPresets.CascadeSizes (no local field needed)

        //  FPS counter 
        //  I key edge detection for physics cube respawn
        private bool _iWasDown = false;

        private int _renderedTris;

        public GameScene(SceneManager sceneManager, Camera camera, Lights light)
        {
            _sceneManager = sceneManager;
            _camera = camera;
            _light = light;
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
            _csm = new CSM(Config.ShadowConfig.CascadeSizes[0]);

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

            //  Occlusion Culling 
            _occlusionCulling = new OcclusionCulling();
            if (Config.OcclusionConfig.Mode == OcclusionMode.HiZ)
            {
                _hizOcc = new HiZOcc();
            }

            // Register all animated objects for occlusion testing
            if (_objectManager != null)
            {
                for (int ai = 0; ai < _objectManager.GetObjects().Count; ai++)
                    _occlusionCulling.RegisterObject();
            }

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

            // Shadow quality: map cascade size to preset index
            _inGameSettingValues[2] = Config.ShadowConfig.CascadeSizes[0] switch
            {
                2048 => 0, 4096 => (Config.ShadowConfig.CascadeSizes[1] == 2048) ? 1 : 2,
                _ => 3
            };

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

            // Spawn physics cubes and spheres above terrain so they fall with gravity
            _objectManager.SpawnPhysicsCubes(_gameTerrainChunk);
            _objectManager.SpawnPhysicsSpheres(_gameTerrainChunk);

            Mouse.ShowMouse(false);

            Console.WriteLine("[GameScene] Engine Running...");
        }

        /// <summary>Handle window resize â€” update viewport and camera aspect ratio.</summary>
        private void OnWindowResized(int width, int height)
        {
            _camera.UpdateAspectRatio((float)width, (float)height);
        }

        public void Update(float deltaTime)
        {
            nint window = Glfw.GetWindow();
            _deltaTime = deltaTime;

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

            //  Player input & camera control â€” only when not paused 
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

                    // 1. Mouse â†’ yaw/pitch â†’ vectors
                    Mouse.Update(window, _camera);
                    _camera.UpdateVectors();

                    CameraConfig.TargetShoulderOffset = _lastTargetShoulderOffset;

                    // 2. Third-person keeps ALT free-look
                    if (!_camera.freeLook && _objectManager != null)
                        _objectManager.PlayerAgent.Heading = _camera.Yaw;

                    // 3. Update keyboard
                    Keyboard.Update(window, _light, _camera, deltaTime, _gameTerrainChunk);

                    // 3B. Check for I key to respawn physics cubes
                    bool iDown = Keyboard.IsKeyDown(window, Const.GLFW_KEY_I);
                    if (iDown && !_iWasDown)
                    {
                        _objectManager.RespawnPhysicsCubes(_gameTerrainChunk);
                    }
                    _iWasDown = iDown;


                    // 3A. Check if camera mode changed and notify player
                    if (_camera.CurrentMode != _lastCameraMode && _objectManager != null)
                    {
                        _lastCameraMode = _camera.CurrentMode;
                        _objectManager.PlayerAgent.OnCameraModeChanged(_camera.CurrentMode);
                    }
                }
            }

            if (_objectManager != null)
            {
                // 4. Update agents (AI, physics, animations) â€” always runs
                _objectManager.Update(deltaTime);

                // 4A. Update player movement â€” only when not paused (skip during input cooldown)
                if (!_paused && _inputCooldown <= 0f)
                    _objectManager.PlayerAgent.Move(window, _camera, deltaTime, _gameTerrainChunk, Vector3.Zero, 0f);

                //  COLLISION: push player out of static objects 
                var staticMgrs = _objectManager.staticObjectManagers;
                var playerAgent = _objectManager.PlayerAgent;
                var playerPos = playerAgent.Position;
                var pushedPlayer = Helpers.CollisionHelper.PushCharacterCapsule(playerPos, staticMgrs, playerAgent.CollisionHeight);
                if (pushedPlayer != playerPos)
                {
                    float terrainY = _gameTerrainChunk.GetHeightAt(pushedPlayer.X, pushedPlayer.Z);
                    // Preserve Y if character was lifted onto an obstacle (step-up)
                    if (pushedPlayer.Y <= terrainY + 0.01f)
                        pushedPlayer.Y = terrainY;
                    playerAgent.Position = pushedPlayer;
                }

                // 4B. Update NPC AI + movement
                _objectManager.UpdateAgents(window, deltaTime, _gameTerrainChunk, _camera);

                //  Update falling physics cubes and spheres
                _objectManager.UpdatePhysicsBoxes(deltaTime, _gameTerrainChunk);
                _objectManager.UpdatePhysicsSpheres(deltaTime, _gameTerrainChunk);

                //  Resolve physics objects vs static objects (bounce off walls)
                _objectManager.ResolvePhysicsStaticCollisions();

                //  COLLISION: physics objects vs player (sphere vs capsule push-out)
                {
                    var pPos = playerAgent.Position;
                    var pRadius = CharacterAgent.CollisionRadius;
                    float pSegMin = pPos.Y + pRadius;
                    float pSegMax = pPos.Y + playerAgent.CollisionHeight - pRadius;
                    bool playerPushed = false;

                    // Boxes vs player
                    var omBoxes = _objectManager.PhysicsBoxes;
                    for (int bi = 0; bi < omBoxes.Count; bi++)
                    {
                        var box = omBoxes[bi];
                        if (!box.Active) continue;

                        float closestX = Math.Clamp(pPos.X, box.Position.X - box.HalfWidth, box.Position.X + box.HalfWidth);
                        float closestZ = Math.Clamp(pPos.Z, box.Position.Z - box.HalfDepth, box.Position.Z + box.HalfDepth);
                        float dx = pPos.X - closestX;
                        float dz = pPos.Z - closestZ;
                        float distSq = dx * dx + dz * dz;

                        // Quick Y overlap check (capsule vs box)
                        float boxMinY = box.Position.Y - box.HalfHeight;
                        float boxMaxY = box.Position.Y + box.HalfHeight;
                        float overlapY = MathF.Min(pSegMax, boxMaxY) - MathF.Max(pSegMin, boxMinY);
                        if (overlapY <= 0.001f) continue;

                        float minDist = pRadius;
                        if (distSq >= minDist * minDist) continue;

                        float dist = MathF.Sqrt(distSq);
                        float nx, nz;
                        if (dist > 1e-4f) { nx = dx / dist; nz = dz / dist; }
                        else { nx = 1f; nz = 0f; }

                        float push = (minDist - dist) * 0.5f;
                        pPos.X += nx * push;
                        pPos.Z += nz * push;
                        box.Position.X -= nx * push;
                        box.Position.Z -= nz * push;
                        box.Object3D?.SetPosition(box.Position.X, box.Position.Y, box.Position.Z);
                        box.Physics.Velocity.X += nx * push * 1.5f;
                        box.Physics.Velocity.Z += nz * push * 1.5f;
                        playerPushed = true;
                    }

                    // Spheres vs player
                    var omSpheres = _objectManager.PhysicsSpheres;
                    for (int si = 0; si < omSpheres.Count; si++)
                    {
                        var sphere = omSpheres[si];
                        if (!sphere.Active) continue;

                        float dx = pPos.X - sphere.Position.X;
                        float dz = pPos.Z - sphere.Position.Z;
                        float distSq = dx * dx + dz * dz;

                        // Quick Y overlap check (capsule segment vs sphere)
                        float sphereMaxY = sphere.Position.Y + sphere.Radius;
                        float sphereMinY = sphere.Position.Y - sphere.Radius;
                        float overlapY = MathF.Min(pSegMax, sphereMaxY) - MathF.Max(pSegMin, sphereMinY);
                        if (overlapY <= 0.001f) continue;

                        float minDist = pRadius + sphere.Radius;
                        if (distSq >= minDist * minDist) continue;

                        float dist = MathF.Sqrt(distSq);
                        float nx, nz;
                        if (dist > 1e-4f) { nx = dx / dist; nz = dz / dist; }
                        else { nx = 1f; nz = 0f; }

                        float push = (minDist - dist) * 0.5f;
                        pPos.X += nx * push;
                        pPos.Z += nz * push;
                        sphere.Position.X -= nx * push;
                        sphere.Position.Z -= nz * push;
                        sphere.Object3D?.SetPosition(sphere.Position.X, sphere.Position.Y, sphere.Position.Z);
                        sphere.Physics.Velocity.X += nx * push * 1.5f;
                        sphere.Physics.Velocity.Z += nz * push * 1.5f;
                        playerPushed = true;
                    }

                    if (playerPushed)
                    {
                        float terrainY = _gameTerrainChunk.GetHeightAt(pPos.X, pPos.Z);
                        if (pPos.Y <= terrainY + 0.01f) pPos.Y = terrainY;
                    }
                    playerAgent.Position = pPos;
                }

                //  COLLISION: push NPC out of static objects (always runs) 
                var allObjs = _objectManager.GetObjects();
                for (int oi = 0; oi < allObjs.Count; oi++)
                {
                    if (allObjs[oi].IsPlayer) continue;
                    var npcPos = allObjs[oi].Position;
                    // All NPC agents share the same default capsule height
                    var pushedNpc = Helpers.CollisionHelper.PushCharacterCapsule(npcPos, staticMgrs);
                    if (pushedNpc != npcPos)
                    {
                        float terrainY = _gameTerrainChunk.GetHeightAt(pushedNpc.X, pushedNpc.Z);
                        // Preserve Y if character was lifted onto an obstacle (step-up)
                        if (pushedNpc.Y <= terrainY + 0.01f)
                            pushedNpc.Y = terrainY;
                        allObjs[oi].Position = pushedNpc;
                    }
                }

                //  COLLISION: physics objects vs NPC characters (push apart)
                for (int oi = 0; oi < allObjs.Count; oi++)
                {
                    if (allObjs[oi].IsPlayer) continue;
                    var npcPos2 = allObjs[oi].Position;
                    float npcRad = CharacterAgent.CollisionRadius;
                    float nSegMin = npcPos2.Y + npcRad;
                    float nSegMax = npcPos2.Y + CharacterAgent.CapsuleHeight - npcRad;

                    // Boxes vs NPC
                    var omBoxes = _objectManager.PhysicsBoxes;
                    for (int bi = 0; bi < omBoxes.Count; bi++)
                    {
                        var box = omBoxes[bi];
                        if (!box.Active) continue;

                        float closestX = Math.Clamp(npcPos2.X, box.Position.X - box.HalfWidth, box.Position.X + box.HalfWidth);
                        float closestZ = Math.Clamp(npcPos2.Z, box.Position.Z - box.HalfDepth, box.Position.Z + box.HalfDepth);
                        float dx = npcPos2.X - closestX;
                        float dz = npcPos2.Z - closestZ;
                        float distSq = dx * dx + dz * dz;

                        float boxMinY = box.Position.Y - box.HalfHeight;
                        float boxMaxY = box.Position.Y + box.HalfHeight;
                        float overlapY = MathF.Min(nSegMax, boxMaxY) - MathF.Max(nSegMin, boxMinY);
                        if (overlapY <= 0.001f) continue;

                        float minDist = npcRad;
                        if (distSq >= minDist * minDist) continue;

                        float dist = MathF.Sqrt(distSq);
                        float nx, nz;
                        if (dist > 1e-4f) { nx = dx / dist; nz = dz / dist; }
                        else { nx = 1f; nz = 0f; }

                        float push = (minDist - dist) * 0.5f;
                        npcPos2.X += nx * push;
                        npcPos2.Z += nz * push;
                        box.Position.X -= nx * push;
                        box.Position.Z -= nz * push;
                        box.Object3D?.SetPosition(box.Position.X, box.Position.Y, box.Position.Z);
                        box.Physics.Velocity.X += nx * push * 1.5f;
                        box.Physics.Velocity.Z += nz * push * 1.5f;
                    }

                    // Spheres vs NPC
                    var omSpheres = _objectManager.PhysicsSpheres;
                    for (int si = 0; si < omSpheres.Count; si++)
                    {
                        var sphere = omSpheres[si];
                        if (!sphere.Active) continue;

                        float dx = npcPos2.X - sphere.Position.X;
                        float dz = npcPos2.Z - sphere.Position.Z;
                        float distSq = dx * dx + dz * dz;

                        float sphereMaxY = sphere.Position.Y + sphere.Radius;
                        float sphereMinY = sphere.Position.Y - sphere.Radius;
                        float overlapY = MathF.Min(nSegMax, sphereMaxY) - MathF.Max(nSegMin, sphereMinY);
                        if (overlapY <= 0.001f) continue;

                        float minDist = npcRad + sphere.Radius;
                        if (distSq >= minDist * minDist) continue;

                        float dist = MathF.Sqrt(distSq);
                        float nx, nz;
                        if (dist > 1e-4f) { nx = dx / dist; nz = dz / dist; }
                        else { nx = 1f; nz = 0f; }

                        float push = (minDist - dist) * 0.5f;
                        npcPos2.X += nx * push;
                        npcPos2.Z += nz * push;
                        sphere.Position.X -= nx * push;
                        sphere.Position.Z -= nz * push;
                        sphere.Object3D?.SetPosition(sphere.Position.X, sphere.Position.Y, sphere.Position.Z);
                        sphere.Physics.Velocity.X += nx * push * 1.5f;
                        sphere.Physics.Velocity.Z += nz * push * 1.5f;
                    }

                    float terrainY2 = _gameTerrainChunk.GetHeightAt(npcPos2.X, npcPos2.Z);
                    if (npcPos2.Y <= terrainY2 + 0.01f) npcPos2.Y = terrainY2;
                    allObjs[oi].Position = npcPos2;
                }

                //  COLLISION: player vs AI characters (capsule vs capsule) 
                {
                    var pAgent = _objectManager.PlayerAgent;
                    var pPos = pAgent.Position;
                    float pSegMin = pPos.Y + CharacterAgent.CollisionRadius;
                    float pSegMax = pPos.Y + pAgent.CollisionHeight - CharacterAgent.CollisionRadius;

                    for (int oi = 0; oi < allObjs.Count; oi++)
                    {
                        if (allObjs[oi].IsPlayer) continue;
                        var aiPos = allObjs[oi].Position;

                        // Capsule Y-overlap check
                        float aSegMin = aiPos.Y + CharacterAgent.CollisionRadius;
                        float aSegMax = aiPos.Y + CharacterAgent.CapsuleHeight - CharacterAgent.CollisionRadius;
                        float overlapY = MathF.Min(pSegMax, aSegMax) - MathF.Max(pSegMin, aSegMin);
                        if (overlapY <= 0.001f) continue;

                        float dx = pPos.X - aiPos.X;
                        float dz = pPos.Z - aiPos.Z;
                        float distSq = dx * dx + dz * dz;
                        float minDist = CharacterAgent.CollisionRadius + CharacterAgent.CollisionRadius;

                        if (distSq >= minDist * minDist) continue;

                        float dist = MathF.Sqrt(distSq);
                        float nx, nz;
                        if (dist > 1e-4f) { nx = dx / dist; nz = dz / dist; }
                        else { nx = 1f; nz = 0f; }

                        float push = (minDist - dist) * 0.5f;
                        pPos.X += nx * push;
                        pPos.Z += nz * push;
                        aiPos.X -= nx * push;
                        aiPos.Z -= nz * push;

                        pPos.Y = _gameTerrainChunk.GetHeightAt(pPos.X, pPos.Z);
                        aiPos.Y = _gameTerrainChunk.GetHeightAt(aiPos.X, aiPos.Z);

                        allObjs[oi].Position = aiPos;
                    }
                    pAgent.Position = pPos;
                }

                //  RE-CHECK: player vs static objects 
                {
                    var recheckPos = playerAgent.Position;
                    var recheckPushed = Helpers.CollisionHelper.PushCharacterCapsule(recheckPos, staticMgrs, playerAgent.CollisionHeight);
                    if (recheckPushed != recheckPos)
                    {
                        float terrainY = _gameTerrainChunk.GetHeightAt(recheckPushed.X, recheckPushed.Z);
                        // Preserve Y if character was lifted onto an obstacle (step-up)
                        if (recheckPushed.Y <= terrainY + 0.01f)
                            recheckPushed.Y = terrainY;
                        playerAgent.Position = recheckPushed;
                    }
                }

                // 5. Set Camera orbital (with terrain + wall collision) â€” always runs
                _camera.SetCamera(window, _objectManager.PlayerAgent.Position, _gameTerrainChunk, deltaTime, staticMgrs);

                // Safety net: push Position dan sync smoothCamPos agar tidak jitter
                var camPos = _camera.Position;
                var safeCamPos = Helpers.CollisionHelper.PushCamera(camPos, staticMgrs);
                if (safeCamPos != camPos)
                    _camera.PushPosition(safeCamPos);
            }

            // 6. Update Light (always runs)
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
                    unsafe
                    {
                        GL.UniformMatrix4fv(_shadowLightSpaceLoc, 1, false, (float*)&lightSpace);
                    }

                    GL.UseProgram(_shadowSkinnedShader);
                    unsafe
                    {
                        GL.UniformMatrix4fv(_shadowSkinnedLightSpaceLoc, 1, false, (float*)&lightSpace);
                    }

                    GL.UseProgram(_shadowStaticAlphaShader);
                    unsafe
                    {
                        GL.UniformMatrix4fv(_shadowStaticAlphaLightSpaceLoc, 1, false, (float*)&lightSpace);
                    }

                    if (_objectManager != null)
                    {
                        _objectManager.RenderShadow(_camera, _csm, i,
                            _shadowSkinnedShader, _shadowSkinnedModelLoc, _shadowSkinnedJointsLoc,
                            _shadowStaticAlphaShader, _shadowStaticAlphaModelLoc);
                    }

                    if (_gameTerrainChunk != null)
                    {
                        _gameTerrainChunk.RenderShadow(_camera, _csm, i, _shadowShader, _shadowModelLoc);

                        // Physics boxes — render shadow with current shadow shader
                        foreach (var box in _objectManager.PhysicsBoxes)
                        {
                            if (box.Active && box.Object3D != null)
                            {
                                box.Object3D.UpdateModelMatriC(Matrix4x4.CreateTranslation(box.Position));
                                box.Object3D.RenderShadow(_camera, _csm, i, _shadowShader, _shadowModelLoc);
                            }
                        }

                        // Physics spheres — with rolling rotation
                        foreach (var sphere in _objectManager.PhysicsSpheres)
                        {
                            if (sphere.Active && sphere.Object3D != null)
                            {
                                var rotMat = Matrix4x4.CreateRotationX(sphere.RotationX) * Matrix4x4.CreateRotationZ(sphere.RotationZ);
                                var transMat = Matrix4x4.CreateTranslation(sphere.Position);
                                sphere.Object3D.UpdateModelMatriC(rotMat * transMat);
                                sphere.Object3D.RenderShadow(_camera, _csm, i, _shadowShader, _shadowModelLoc);
                            }
                        }
                    }
                }

                // Restore default viewport and framebuffer
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
                GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);
            }

            //  Occlusion Culling (always runs for visual updates behind menu) 
            if (Config.OcclusionConfig.UseOcclusion && _objectManager != null && _gameTerrainChunk != null)
            {
                _occlusionFrameCount++;

                // Reset BVH profiler + cull counters each frame
                BVH.ResetStats();
                OcclusionCulling.LastCheckVisibilityTicks = 0;
                OcclusionCulling.LastIsOccludedTicks = 0;
                _staticFrustumCulled = 0;
                _staticTerrainOccluded = 0;
                _staticOcclusionCulled = 0;

                bool useHiZ = (Config.OcclusionConfig.Mode == OcclusionMode.HiZ && _hizOcc != null);

                // Phase 0: Generate Hi-Z depth buffer
                if (useHiZ)
                    _hizOcc!.GenerateTerrainDepth(_camera, _gameTerrainChunk);

                // Phase 1: Register occluders
                _occlusionCulling.ClearOccluders();
                if (useHiZ)
                    _hizOcc!.ClearOccluders();

                if (_objectManager.staticObjectManagers != null)
                {
                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())
                            sobj.IsVisible = true;
                    }
                }

                var animObjs = _objectManager.GetObjects();
                for (int oi = 0; oi < animObjs.Count; oi++)
                    animObjs[oi].IsVisible = true;

                // 1B. Register static objects as occluders
                var frustumVP = _camera.GetViewMatrix() * _camera.GetProjectionMatrix();
                var frustumPlanes = StaticObjectManager.ExtractCameraFrustum(frustumVP);
                if (_objectManager.staticObjectManagers != null)
                {

                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())
                        {
                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                            if (distSq >= _camera.FarDist * _camera.FarDist) { sobj.IsVisible = false; _staticFrustumCulled++; continue; }
                            // Frustum cull: skip objects outside camera frustum
                            if (!StaticObjectManager.IsAABBInFrustum(frustumPlanes, sobj.CachedWorldAABB, 3f))
                            {
                                sobj.IsVisible = false;
                                continue;
                            }


                            if (!useHiZ)
                            {
                                bool terrainOccluded = false;

                                if (mgr.SkipTerrainRayMarch)
                                {
                                    float nearSq = Config.OcclusionConfig.TerrainOcclusionNearDist * Config.OcclusionConfig.TerrainOcclusionNearDist;
                                    if (distSq < nearSq)
                                    {
                                        var aabb = sobj.CachedWorldAABB;
                                        Vector3 bottomCenter = new(
                                            (aabb.Min.X + aabb.Max.X) * 0.5f,
                                            aabb.Min.Y,
                                            (aabb.Min.Z + aabb.Max.Z) * 0.5f
                                        );

                                        float camTerrainH = _gameTerrainChunk.GetHeightAt(_camera.Position.X, _camera.Position.Z);
                                        if (_camera.Position.Y >= camTerrainH - 0.5f)
                                        {
                                            Vector3 dir = bottomCenter - _camera.Position;
                                            float totalDist = dir.Length();
                                            if (totalDist >= 0.5f)
                                            {
                                                dir /= totalDist;
                                                const int numSamples = 8;
                                                float stepSize = totalDist / numSamples;
                                                for (int s = 1; s < numSamples; s++)
                                                {
                                                    Vector3 samplePos = _camera.Position + dir * (s * stepSize);
                                                    float terrainH = _gameTerrainChunk.GetHeightAt(samplePos.X, samplePos.Z);
                                                    if (samplePos.Y < terrainH - 0.3f)
                                                    {
                                                        terrainOccluded = true;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Vector3 midPoint = _camera.Position + (sobj.Position - _camera.Position) * 0.5f;
                                        float midTerrainH = _gameTerrainChunk.GetHeightAt(midPoint.X, midPoint.Z);
                                        float camTerrainH = _gameTerrainChunk.GetHeightAt(_camera.Position.X, _camera.Position.Z);
                                        if (_camera.Position.Y >= camTerrainH - 0.5f && midPoint.Y < midTerrainH - 0.5f)
                                            terrainOccluded = true;
                                    }
                                }
                                else
                                {
                                    var aabb = sobj.CachedWorldAABB;
                                    Vector3[] corners = new Vector3[8]
                                    {
                                        new(aabb.Min.X, aabb.Min.Y, aabb.Min.Z),
                                        new(aabb.Max.X, aabb.Min.Y, aabb.Min.Z),
                                        new(aabb.Max.X, aabb.Max.Y, aabb.Min.Z),
                                        new(aabb.Min.X, aabb.Max.Y, aabb.Min.Z),
                                        new(aabb.Min.X, aabb.Min.Y, aabb.Max.Z),
                                        new(aabb.Max.X, aabb.Min.Y, aabb.Max.Z),
                                        new(aabb.Max.X, aabb.Max.Y, aabb.Max.Z),
                                        new(aabb.Min.X, aabb.Max.Y, aabb.Max.Z),
                                    };

                                    float camTerrainH = _gameTerrainChunk.GetHeightAt(_camera.Position.X, _camera.Position.Z);
                                    bool allCornersBehindTerrain = true;

                                    for (int ci = 0; ci < 8; ci++)
                                    {
                                        Vector3 cornerPos = corners[ci];
                                        Vector3 dir = cornerPos - _camera.Position;
                                        float totalDist = dir.Length();
                                        if (totalDist < 0.5f) { allCornersBehindTerrain = false; break; }
                                        dir /= totalDist;

                                        if (_camera.Position.Y < camTerrainH - 0.5f) { allCornersBehindTerrain = false; break; }

                                        const int numSamples = 8;
                                        float stepSize = totalDist / numSamples;
                                        bool cornerBehindTerrain = false;
                                        for (int s = 1; s < numSamples; s++)
                                        {
                                            Vector3 samplePos = _camera.Position + dir * (s * stepSize);
                                            float terrainH = _gameTerrainChunk.GetHeightAt(samplePos.X, samplePos.Z);
                                            if (samplePos.Y < terrainH - 0.3f)
                                            {
                                                cornerBehindTerrain = true;
                                                break;
                                            }
                                        }

                                        if (!cornerBehindTerrain)
                                        {
                                            allCornersBehindTerrain = false;
                                            break;
                                        }
                                    }

                                    if (allCornersBehindTerrain)
                                        terrainOccluded = true;
                                }

                                if (terrainOccluded)
                                {
                                    sobj.IsVisible = false;
                                    continue;
                                }
                            }

                            if (sobj.IsOccluder)
                            {
                                if (sobj.CollisionBVH != null)
                                {
                                    // Mesh occluder: ray-triangle intersection for accuracy
                                    _occlusionCulling.RegisterMeshOccluder(sobj.CollisionBVH);
                                    
                                    // HiZ fallback: register root AABB
                                    var rootAABB = sobj.CollisionBVH.Root?.Bounds ?? sobj.CachedWorldAABB;
                                    if (useHiZ)
                                        _hizOcc!.RegisterOccluder(rootAABB);
                                }
                                else
                                {
                                    _occlusionCulling.RegisterOccluder(sobj.CachedWorldAABB);
                                    if (useHiZ)
                                        _hizOcc!.RegisterOccluder(sobj.CachedWorldAABB);
                                }
                            }
                        }
                    }
                }

                // Phase 2: Test all objects against occluders
                var objectAABBs = new Helpers.ObjectHelpers.AABB[animObjs.Count];
                for (int oi = 0; oi < animObjs.Count; oi++)
                    objectAABBs[oi] = animObjs[oi].WorldAABB;

                _occlusionCulling.CheckVisibility(_camera.Position, objectAABBs);

                for (int oi = 0; oi < animObjs.Count; oi++)
                    animObjs[oi].IsVisible = _occlusionCulling.IsVisible(oi);

                // 2B. Test static objects against occluders
                if (_objectManager.staticObjectManagers != null)
                {
                    float farSq = _camera.FarDist * _camera.FarDist;

                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())
                        {
                            if (!sobj.IsVisible) continue;
                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                            if (distSq > farSq) continue;

                            bool occluded;
                            var testAABB = sobj.CollisionBVH != null
                                ? (sobj.CollisionBVH.Root?.Bounds ?? sobj.CachedWorldAABB)
                                : sobj.CachedWorldAABB;
                            if (useHiZ)
                            {
                                occluded = _hizOcc!.IsTerrainOccluded(_camera.Position, testAABB);
                                if (!occluded)
                                    occluded = _hizOcc!.IsOccluded(_camera.Position, testAABB);
                            }
                            else
                            {
                                occluded = _occlusionCulling.IsOccludedByOccluders(_camera.Position, testAABB);
                            }

                            if (occluded)
                                sobj.IsVisible = false;
                        }
                    }
                }

                // Phase 3: Terrain height ray-marching for animated objects
                for (int oi = 0; oi < animObjs.Count; oi++)
                {
                    if (animObjs[oi].IsPlayer) continue;
                    if (!animObjs[oi].IsVisible) continue;

                    var aabb = animObjs[oi].WorldAABB;
                    Vector3[] corners = new Vector3[8]
                    {
                        new(aabb.Min.X, aabb.Min.Y, aabb.Min.Z),
                        new(aabb.Max.X, aabb.Min.Y, aabb.Min.Z),
                        new(aabb.Max.X, aabb.Max.Y, aabb.Min.Z),
                        new(aabb.Min.X, aabb.Max.Y, aabb.Min.Z),
                        new(aabb.Min.X, aabb.Min.Y, aabb.Max.Z),
                        new(aabb.Max.X, aabb.Min.Y, aabb.Max.Z),
                        new(aabb.Max.X, aabb.Max.Y, aabb.Max.Z),
                        new(aabb.Min.X, aabb.Max.Y, aabb.Max.Z),
                    };

                    float physCamTerrainH2 = _gameTerrainChunk.GetHeightAt(_camera.Position.X, _camera.Position.Z);
                    if (_camera.Position.Y < physCamTerrainH2 - 0.5f) continue;

                    bool allCornersBehindTerrain = true;

                    for (int ci = 0; ci < 8; ci++)
                    {
                        Vector3 cornerPos = corners[ci];
                        Vector3 dir2 = cornerPos - _camera.Position;
                        float totalDist2 = dir2.Length();
                        if (totalDist2 < 0.5f) { allCornersBehindTerrain = false; break; }
                        dir2 /= totalDist2;

                        const int numSamples2 = 16;
                        float stepSize2 = totalDist2 / numSamples2;
                        bool cornerOccluded = false;

                        int startSample = Math.Max(1, numSamples2 / 10);
                        for (int s = startSample; s < numSamples2; s++)
                        {
                            float t = s * stepSize2;
                            Vector3 samplePos = _camera.Position + dir2 * t;
                            float terrainH = _gameTerrainChunk.GetHeightAt(samplePos.X, samplePos.Z);

                            if (samplePos.Y < terrainH - 0.5f)
                            {
                                cornerOccluded = true;
                                break;
                            }
                        }

                        if (!cornerOccluded)
                        {
                            allCornersBehindTerrain = false;
                            break;
                        }
                    }

                    if (allCornersBehindTerrain)
                        animObjs[oi].IsVisible = false;
                }

                // Physics objects — full OC: distance → frustum → terrain → occluders
                var omBoxes = _objectManager.PhysicsBoxes;
                var omSpheres = _objectManager.PhysicsSpheres;
                foreach (var box in omBoxes) box.IsVisible = true;
                foreach (var sphere in omSpheres) sphere.IsVisible = true;

                float farSqPhysics = _camera.FarDist * _camera.FarDist;
                float physCamTerrainH = _gameTerrainChunk.GetHeightAt(_camera.Position.X, _camera.Position.Z);
                bool camAboveTerrain = _camera.Position.Y >= physCamTerrainH - 0.5f;

                // Boxes: distance -> frustum -> terrain ray-march -> occluders
                foreach (var box in omBoxes)
                {
                    if (!box.Active || box.Object3D == null) continue;

                    // Distance cull
                    float dx = box.Position.X - _camera.Position.X;
                    float dz = box.Position.Z - _camera.Position.Z;
                    if (dx * dx + dz * dz > farSqPhysics) { box.IsVisible = false; continue; }

                    // Frustum cull
                    if (!StaticObjectManager.IsAABBInFrustum(frustumPlanes, box.CachedWorldAABB, 3f))
                    { box.IsVisible = false; continue; }

                    // Terrain ray-march (simple mid-point check)
                    if (camAboveTerrain)
                    {
                        Vector3 midPoint = _camera.Position + (box.Position - _camera.Position) * 0.5f;
                        float midTerrainH = _gameTerrainChunk.GetHeightAt(midPoint.X, midPoint.Z);
                        if (midPoint.Y < midTerrainH - 0.5f)
                        { box.IsVisible = false; continue; }
                    }

                    // OC occluder test
                    box.IsVisible = !_occlusionCulling.IsOccludedByOccluders(_camera.Position, box.CachedWorldAABB);
                }

                // Spheres: distance -> frustum -> terrain ray-march -> occluders
                foreach (var sphere in omSpheres)
                {
                    if (!sphere.Active || sphere.Object3D == null) continue;

                    // Distance cull
                    float dx = sphere.Position.X - _camera.Position.X;
                    float dz = sphere.Position.Z - _camera.Position.Z;
                    if (dx * dx + dz * dz > farSqPhysics) { sphere.IsVisible = false; continue; }

                    // Frustum cull
                    if (!StaticObjectManager.IsAABBInFrustum(frustumPlanes, sphere.CachedWorldAABB, 3f))
                    { sphere.IsVisible = false; continue; }

                    // Terrain ray-march (simple mid-point check)
                    if (camAboveTerrain)
                    {
                        Vector3 midPoint = _camera.Position + (sphere.Position - _camera.Position) * 0.5f;
                        float midTerrainH = _gameTerrainChunk.GetHeightAt(midPoint.X, midPoint.Z);
                        if (midPoint.Y < midTerrainH - 0.5f)
                        { sphere.IsVisible = false; continue; }
                    }

                    // OC occluder test
                    sphere.IsVisible = !_occlusionCulling.IsOccludedByOccluders(_camera.Position, sphere.CachedWorldAABB);
                }

                // Player always visible
                _objectManager.PlayerObject.IsVisible = true;
            }
        }

        public void Render()
        {
            if (_ppStack == null || _csm == null || _skybox == null || _hud == null) return;

            bool wireframeMode = Keyboard.GetIsWireframe();

            //  MAIN RENDER PASS 
            if (wireframeMode)
            {
                // Wireframe: render directly to screen, skip postprocess (F1 conflicts with PP)
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
                GL.Viewport(0, 0, Glfw.WindowWidth, Glfw.WindowHeight);
                GL.ClearColor(0.07f, 0.13f, 0.17f, 1.0f);
                GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
            }
            else
            {
                _ppStack.BindSceneFBO();
                GL.ClearColor(0.07f, 0.13f, 0.17f, 1.0f);
                GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
            }

            // 3. Draw Skybox
            _skybox.Draw(_camera, _light, 0f, _skyTextures!, _gameTerrainChunk);

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

            _renderedTris = 0;
            if (_gameTerrainChunk != null)
            {
                Plane[]? cullFreezePlanes = null;
                if (Keyboard.GetCullFreezeMode())
                {
                    var freezeVP = Keyboard.GetCullFreezeViewProj();
                    cullFreezePlanes = TerrainChunk.ExtractFrustumPlanes(freezeVP);
                }

                _renderedTris = _gameTerrainChunk.Render(_camera, _gameTerrainChunk.GetFrozenPlanes(), cullFreezePlanes);

                //  Physics objects — always-run frustum + distance cull (handles pause state)
                if (_objectManager != null)
                {
                    var frustumVP = _camera.GetViewMatrix() * _camera.GetProjectionMatrix();
                    var frustumPlanes = StaticObjectManager.ExtractCameraFrustum(frustumVP);
                    float farSq = _camera.FarDist * _camera.FarDist;

                    // Boxes: distance + frustum cull
                    foreach (var box in _objectManager.PhysicsBoxes)
                    {
                        if (!box.Active || box.Object3D == null) continue;
                        float dx = box.Position.X - _camera.Position.X;
                        float dz = box.Position.Z - _camera.Position.Z;
                        if (dx * dx + dz * dz > farSq)
                            box.IsVisible = false;
                        else if (!StaticObjectManager.IsAABBInFrustum(frustumPlanes, box.CachedWorldAABB, 3f))
                            box.IsVisible = false;
                    }

                    // Spheres: distance + frustum cull
                    foreach (var sphere in _objectManager.PhysicsSpheres)
                    {
                        if (!sphere.Active || sphere.Object3D == null) continue;
                        float dx = sphere.Position.X - _camera.Position.X;
                        float dz = sphere.Position.Z - _camera.Position.Z;
                        if (dx * dx + dz * dz > farSq)
                            sphere.IsVisible = false;
                        else if (!StaticObjectManager.IsAABBInFrustum(frustumPlanes, sphere.CachedWorldAABB, 3f))
                            sphere.IsVisible = false;
                    }
                }

                //  Physics Boxes (falling cubes) and Spheres - drawn while main shader is active
                foreach (var box in _objectManager.PhysicsBoxes)
                {
                    if (box.IsVisible)
                        box.Draw();
                }
                foreach (var sphere in _objectManager.PhysicsSpheres)
                {
                    if (sphere.IsVisible)
                        sphere.Draw();
                }
            }

            //  glTF Object Manager — always-run frustum + distance cull for static objects (handles pause state)
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

                _objectManager.Draw(_camera, _light, _csm);
                _objectManager.DrawHealthBars(_camera, _hud);
            }

            //  Post Process (render SceneFBO to screen) â€” skip in wireframe mode 
            if (!wireframeMode)
                _ppStack.RunStack(Glfw.WindowWidth, Glfw.WindowHeight, _time);
            else
                GL.Enable(Const.GL_DEPTH_TEST);

            //  Capture screenshot right after post-process, before any UI overlays 
            if (_pendingScreenshotSlot >= 0)
            {
                int slot = _pendingScreenshotSlot;
                _pendingScreenshotSlot = -1; // Reset immediately
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
                SaveManager.CaptureScreenshot(slot);
            }

            //  Pause Blur Overlay (skip in wireframe mode) 
            if (_paused && !_confirmingExit && !_settingsActive)
            {
                if (!wireframeMode)
                    _ppStack.RenderBlurred(Glfw.WindowWidth, Glfw.WindowHeight, 5f, 1.0f);
            }

            //  HLOD Visualization (toggled with K key) 
            if (Keyboard.GetShowHLOD() && _objectManager != null)
            {
                GL.Disable(Const.GL_DEPTH_TEST);

                Plane[] hlodFrustum = StaticObjectManager.ExtractCameraFrustum(
                    Matrix4x4.Multiply(_camera.GetViewMatrix(), _camera.GetProjectionMatrix()));
                _objectManager.DrawHLODDebug(_camera, hlodFrustum);

                GL.Enable(Const.GL_DEPTH_TEST);
            }

            //  Impostor Visualization (toggled with J key) 
            if (Keyboard.GetShowImpostor() && _objectManager != null)
            {
                GL.Disable(Const.GL_DEPTH_TEST);

                Plane[] impFrustum = StaticObjectManager.ExtractCameraFrustum(
                    Matrix4x4.Multiply(_camera.GetViewMatrix(), _camera.GetProjectionMatrix()));
                _objectManager.DrawImpostorDebug(_camera, impFrustum);

                GL.Enable(Const.GL_DEPTH_TEST);
            }

            //  Debug BBox Wireframe + LOD Labels (toggled with P key) 
            if (Keyboard.GetShowBBox() && _objectManager != null)
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

                            // When BVH mesh is shown, skip AABB for objects with BVH collision
                            if (!Keyboard.GetShowBVHMesh() || sobj.CollisionBVH == null)
                            {
                                var debugAABB = (sobj.CollisionBVH != null && sobj.CollisionBVH.Root != null)
                                    ? sobj.CollisionBVH.Root.Bounds
                                    : sobj.CachedWorldAABB;
                                TerrainChunk.DrawAABBWireframe(debugAABB, debugColor, _camera);

                                // LOD label for static objects
                                Vector3 sobjCenter = (debugAABB.Min + debugAABB.Max) * 0.5f;
                                DrawLODLabel(sobjCenter, sobj.CurrentLOD, false);
                            }
                        }
                    }

                    var collisionColor = new Vector3(0.6f, 0f, 1f);
                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())
                        {
                            if (sobj.CachedCollisionAABB.HasValue)
                                TerrainChunk.DrawAABBWireframe(sobj.CachedCollisionAABB.Value, collisionColor, _camera);
                        }
                    }

                    //  BVH Collision Mesh Debug (toggled with P key) 
                    if (Keyboard.GetShowBVHMesh())
                    {
                        var bvhColor = new Vector3(0f, 1f, 0.5f);
                        for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)
                        {
                            var mgr = _objectManager.staticObjectManagers[mi];
                            if (mgr == null) continue;
                            foreach (var sobj in mgr.GetObjects())
                            {
                                if (sobj.CollisionBVH == null) continue;
                                var verts = sobj.CollisionBVH.GetTriangleLineVertices();
                                if (verts.Count > 0)
                                    TerrainChunk.DrawLineSegments(verts, bvhColor, _camera);
                            }
                        }
                    }
                }

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
                    RenderSettingsPanel();
                }
                else
                    RenderPauseMenu();
            }

            // HUD 
            int totalMapTris = TerrainChunk.GetTotalMapTriangles();
            int totalObjTris = _objectManager?.TotalObjectTriangles ?? 0;
            int renderedObjTris = _objectManager?.RenderedTriangles ?? 0;
            int totalAllTris = totalMapTris + totalObjTris;
            int renderedAllTris = _renderedTris + renderedObjTris;
            string gTime = _light.GetFormattedTime();

            string title1 = $" [ {gTime} ]";
            string title2 = $" FPS: {Glfw.GetLastFPS()}";
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
                title6 = $" Objects: {_objectManager.DrawnObjects:N0} drawn / {culledTotal:N0} culled / {_objectManager.TotalObjects:N0} total  (anim: {animFrustum}f {animOcc}occ)";
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

            string title7 = "";
            string title8 = "";
                            // BVH profiling stats
                int bvhRays = BVH.TotalRayTests;
                int bvhNodes = BVH.TotalNodeVisits;
                int bvhAABBs = BVH.TotalAABBTests;
                int bvhTris = BVH.TotalTriangleTests;
                long ocCheckVisTicks = OcclusionCulling.LastCheckVisibilityTicks;
                long ocIsOccTicks = OcclusionCulling.LastIsOccludedTicks;
                double freq = System.Diagnostics.Stopwatch.Frequency;
                double ocCheckVisMs = (double)ocCheckVisTicks / freq * 1000.0;
                double ocIsOccMs = (double)ocIsOccTicks / freq * 1000.0;
                title8 = $" BVH: rays={bvhRays} nodes={bvhNodes} aabb={bvhAABBs} tris={bvhTris}  OC: checkVis={ocCheckVisMs:N3}ms isOcc={ocIsOccMs:N3}ms";
                string ocMode = Inputs.Keyboard.GetOcclusionModeName();
            bool ocActive = Inputs.Keyboard.GetOcclusionCullingEnabled();
            if (ocActive && _occlusionFrameCount > 1)
            {

                int staticCulledAll = _staticFrustumCulled + _staticTerrainOccluded + _staticOcclusionCulled;
                title7 = $" OC [{ocMode}]: {_occlusionCulling!.VisibleCount}v / {_occlusionCulling.OccludedCount}o | static: {staticCulledAll}cld ({_staticFrustumCulled}f {_staticTerrainOccluded}t {_staticOcclusionCulled}o)";
            }
            else if (ocActive)
            {
                title7 = $" OC [{ocMode}]: waiting...";
            }

            _hud.DrawText(title1, 10, 60, new Vector3(1, 0, 0));
            float debugLineH = _hud.MeasureTextHeight(title1) + 6f;
            _hud.DrawText(title2, 10, 60 + debugLineH, new Vector3(1, 0, 0));
            _hud.DrawText(title3, 10, 60 + debugLineH * 2, new Vector3(1, 1, 0));
            _hud.DrawText(title4, 10, 60 + debugLineH * 3, new Vector3(1, 0, 0));
            _hud.DrawText(title5, 10, 60 + debugLineH * 4, new Vector3(1, 0, 0));
            _hud.DrawText(title6, 10, 60 + debugLineH * 5, new Vector3(1, 0, 0));
            if (!string.IsNullOrEmpty(title7))
                _hud.DrawText(title7, 10, 60 + debugLineH * 6, new Vector3(0, 1, 1));
            // BVH profiling display (dim cyan)
            if (!string.IsNullOrEmpty(title8))
                _hud.DrawText(title8, 10, 60 + debugLineH * 7, new Vector3(0.2f, 0.7f, 0.8f));

            //  HLOD Statistics (if any HLOD regions exist) 
            if (_objectManager != null && _objectManager.staticObjectManagers != null)
            {
                bool hasHLOD = false;
                int hlodTotalTris = 0, hlodMergedTris = 0, hlodRegions = 0, hlodObjs = 0, hlodVisible = 0;
                foreach (var mgr in _objectManager.staticObjectManagers)
                {
                    if (mgr != null && mgr.HLODRegionCount > 0)
                    {
                        hasHLOD = true;
                        hlodTotalTris += mgr.HLODTotalIndividualTris;
                        hlodMergedTris += mgr.HLODTotalMergedTris;
                        hlodRegions += mgr.HLODRegionCount;
                        hlodObjs += mgr.HLODTotalObjects;
                        hlodVisible += mgr.HLODVisibleRegions;
                    }
                }
                if (hasHLOD)
                {
                    float trisSavedPct = hlodTotalTris > 0
                        ? (1f - (float)hlodMergedTris / hlodTotalTris) * 100f
                        : 0f;
                    string hlodLine1 = $" HLOD: {hlodObjs:N0} objects  âž”  {hlodRegions} merged regions";
                    string hlodLine2 = $"      Tris: {hlodTotalTris:N0} (indiv) âž” {hlodMergedTris:N0} (merged)  = {trisSavedPct:N1}% saved";
                    string hlodLine3 = $"      Draw calls: {hlodTotalTris:N0} max (indiv) âž” ~{hlodVisible}/{hlodRegions} visible (merged)";
                    _hud.DrawText(hlodLine1, 10, 60 + debugLineH * 8, new Vector3(0.3f, 0.9f, 0.6f));
                    _hud.DrawText(hlodLine2, 10, 60 + debugLineH * 9, new Vector3(0.3f, 0.9f, 0.6f));
                    _hud.DrawText(hlodLine3, 10, 60 + debugLineH * 10, new Vector3(0.3f, 0.9f, 0.6f));
                }

                //  Impostor Statistics (shown when impostor visualization is ON via J key) 
                if (Keyboard.GetShowImpostor())
                {
                    int totalImpRegions = _objectManager.TotalImpostorRegions;
                    int totalImpVisible = _objectManager.TotalVisibleImpostors;

                    // Also collect per-manager NearDist/FarDist from the first manager that has impostors
                    float nearDist = 0f, farDist = 0f;
                    foreach (var mgr in _objectManager.staticObjectManagers)
                    {
                        if (mgr != null && mgr.ImpostorRegionCount > 0)
                        {
                            nearDist = mgr.ImpostorNearDist;
                            farDist = mgr.ImpostorFarDist;
                            break;
                        }
                    }

                    Vector3 impColor = totalImpVisible > 0
                        ? new Vector3(0.2f, 1.0f, 0.7f) // cyan-green if visible
                        : new Vector3(1.0f, 0.6f, 0.2f); // orange if none visible

                    string modeLabel = Keyboard.GetImpostorDebugMode() == 2 ? "AABB" : "Billboard";
                    string impLine1 = $"IMPOSTOR [{modeLabel}]: {totalImpRegions} regions  |  {totalImpVisible} visible  |  range [{nearDist:F0}-{farDist:F0}]m";
                    _hud.DrawText(impLine1, 10, 60 + debugLineH * 11, impColor);

                    // Draw a small colored indicator bar at the top-right corner
                    float indicatorSize = 12f;
                    float indicatorX = Glfw.WindowWidth - indicatorSize - 10f;
                    float indicatorY = 65f;
                    Vector3 indicatorBg = new Vector3(0.05f, 0.05f, 0.08f);
                    Vector3 indicatorColor = totalImpVisible > 0
                        ? new Vector3(0.0f, 1.0f, 0.5f)  // bright cyan = some visible
                        : new Vector3(0.3f, 0.3f, 0.3f); // dim gray = none visible
                    _hud.DrawBox(indicatorX - 2f, indicatorY - 2f, indicatorSize + 4f, indicatorSize + 4f, indicatorBg);
                    _hud.DrawBox(indicatorX, indicatorY, indicatorSize, indicatorSize, indicatorColor);
                }
            }

            Glfw.ShowFPS(_deltaTime, _renderedTris, totalMapTris, gTime);

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
                case 2: // Shadow Quality
                {
                    Config.ShadowConfig.CascadeSizes = Config.ShadowPresets.CascadeSizes[val];
                    _csm?.Dispose();
                    _csm = new CSM(Config.ShadowConfig.CascadeSizes[0]);
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
            else if (index == 1) // Save Game
            {
                OpenSaveLoadUI(true);
            }
            else if (index == 2) // Load Game
            {
                OpenSaveLoadUI(false);
            }
            else if (index == 3) // Settings â€” save snapshot for cancel
            {
                // Save snapshot of current values for cancel/revert
                Array.Copy(_inGameSettingValues, _settingsSnapshot, _inGameSettingValues.Length);
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
                MainMenuScene mainMenu = new(_sceneManager, _camera, _light, "In-game settings saved!");
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
            _hizOcc?.Dispose();
            _csm?.Dispose();
            Console.WriteLine("[GameScene] Exited.");
        }

        public void Dispose()
        {
            _hizOcc?.Dispose();
            _csm?.Dispose();
            _objectManager?.Dispose();
            _gameTerrainChunk?.Dispose();
            Console.WriteLine("[GameScene] Disposed.");
        }
    }
}
