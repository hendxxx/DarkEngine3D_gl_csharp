using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Scene
{
    /// <summary>
    /// Loading screen scene. Handles all synchronous resource loading (terrain, objects, etc.)
    /// while rendering a loading animation with progress feedback each frame.
    /// When loading completes, switches to the GameScene on the next frame's Render()
    /// so the Viewport panel gets a chance to display the loading screen.
    /// </summary>
    public class LoadingScene : IScene
    {
        private readonly SceneManager _sceneManager;
        private readonly Camera _camera;
        private readonly Lights _light;

        // Target scene from game.ing (optional). When set, we load its asset and switch to
        // that scene after resources are ready instead of switching to a blank GameScene.
        private readonly string? _targetSceneName;

        // ── Scene root element for IDE hierarchy ──
        private readonly UIElement _sceneRoot = new()
        {
            Name = "LoadingScene",
            Type = UIElementType.Scene,
            IsVisible = false,
        };

        // Loading UI
        private HUD? _hud;
        private Texture[]? _images;
        private string _status = "Loading...";
        private float _deltaTime = 0f;

        // When true, synchronous loading is done and we should switch to the target scene.
        private bool _loadingComplete = false;

        // Game resources that will be created during loading
        private TerrainChunk? _gameTerrainChunk;
        private Skybox? _skybox;
        private ObjectManager? _objectManager;
        private Texture[]? _skyTextures;

        // Active tilemap restored from the target game.ing scene (if any).
        // Propagated to the IDE bridge before switching so GameScene can use
        // map-based player spawn / level camera re-anchoring.
        private Tilemap2D? _activeTilemap;

        // Game scene created during loading and handed off to the engine.
        private GameScene? _gameScene;

        public string Name => "LoadingScene";

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

        public LoadingScene(SceneManager sceneManager, Camera camera, Lights light, string? targetSceneName = null)
        {
            _sceneManager = sceneManager;
            _camera = camera;
            _light = light;
            _targetSceneName = targetSceneName;
        }

        public void Enter()
        {
            // ── Loading screen UI assets ──
            Texture[] images =
            [
                new("Artifacts\\\\images\\\\loading01_image.png"),
                new("Artifacts\\\\images\\\\spinner01_image.png"),
            ];
            _images = images;

            HUD hud = new("Artifacts\\\\fonts\\\\Ngaco.ttf", 13.0f);
            _hud = hud;

            RenderFrame("Loading engine ...");
            Thread.Sleep(500);

            // ── Terrain textures ──
            Texture[] TerrainTextures =
            [
                new("Artifacts\\\\textures\\\\aerial grass\\\\aerial_grass_rock_diff_4k.jpg"),
                new("Artifacts\\\\textures\\\\aerial rock\\\\aerial_rocks_04_diff_4k.jpg"),
                new("Artifacts\\\\textures\\\\snow\\\\snow_01_diff_4k.jpg"),
                new("Artifacts\\\\textures\\\\cliff side\\\\cliff_side_diff_4k.jpg"),
            ];

            // ── Sky textures ──
            Texture[] SkyTextures =
            [
                new("Artifacts\\\\textures\\\\moon.png"),
            ];
            _skyTextures = SkyTextures;

            // ── TerrainChunk setup ──
            TerrainChunk.GlobalLODLevel = 1;
            TerrainChunk.ChunksPerSide = 16;
            TerrainChunk.HeightScale = 50.0f;
            TerrainChunk.TerrainScale = 1.0f;
            TerrainChunk.OnLoadProgress += (progress) =>
            {
                int filled = (int)(progress * 20);
                string bar = new string('#', filled) + new string('-', 20 - filled);
                Console.Write($"\rTerrain Loading: [{bar}] {progress * 100:F1}%");

                RenderFrame($"Loading Terrain {progress * 100:F1}%");

                if (progress >= 1.0f)
                    Console.WriteLine();
            };

            // Generate heightmap if it doesn't exist
            string mapPath = "Artifacts\\\\maps\\\\map.png";
            if (!File.Exists(mapPath))
            {
                MapLoader.GeneratePhotorealHeightmap(mapPath, 513);
            }

            TerrainChunk gameTerrainChunk = new(mapPath, TerrainTextures);
            _gameTerrainChunk = gameTerrainChunk;

            // ── Skybox ──
            Skybox skybox = new();
            _skybox = skybox;

            // ── ObjectManager ──
            GltfShader.Init();

            RenderFrame("Loading objects ...");

            ObjectManager objectManager = new();
            objectManager.OnLoadProgress += (progress, status) =>
            {
                int filled = (int)(progress * 20);
                string bar = new string('#', filled) + new string('-', 20 - filled);
                Console.Write($"\r{status} [{bar}] {progress * 100:F1}%");
                RenderFrame(status);
                if (progress >= 1.0f)
                    Console.WriteLine();
            };
            objectManager.Init(_camera!, gameTerrainChunk);
            _objectManager = objectManager;

            Thread.Sleep(500);

        // ── If a real game scene from game.ing was requested, restore its editor objects
            // (Player2D, Map2D, lights, ...) into an EditorObjectManager and wire that manager
            // into the IDE bridge so the loaded scene has the same world/content as the editor
            // in-game path. Only the runtime ObjectManager is handed to GameScene below.
            if (!string.IsNullOrEmpty(_targetSceneName))
            {
                var targetAsset = SceneAssetSerializer.FindScene(_targetSceneName);
                if (targetAsset != null && string.Equals(targetAsset.SceneType, "GameScene", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[LoadingScene] Target game scene '{_targetSceneName}' found in game.ing — restoring its editor objects");

                    var editorMgr = new EditorObjectManager();
                    _activeTilemap = RestoreEditorObjects(editorMgr, targetAsset);

                    var b = _sceneManager.Bridge;
                    if (b != null)
                    {
                        b.EditorObjectManager = editorMgr;
                        if (_activeTilemap != null)
                            b.ActiveTilemap = _activeTilemap;
                    }
                }
            }

            // ── Finalize: create GameScene and set loaded resources ──
            var gameScene = new GameScene(_sceneManager, _camera, _light);
            gameScene.SetResources(_skyTextures, _gameTerrainChunk, _skybox, _hud, _objectManager);
            _gameScene = gameScene;

            // ── If we restored a real game scene from game.ing, also load its UI hierarchy
            // into the GameScene root so main-menu goto scene looks the same as the editor path.
            if (!string.IsNullOrEmpty(_targetSceneName))
            {
                var targetAsset = SceneAssetSerializer.FindScene(_targetSceneName);
                if (targetAsset != null && string.Equals(targetAsset.SceneType, "GameScene", StringComparison.OrdinalIgnoreCase))
                {
                    gameScene.LoadUIHierarchyFromAsset(targetAsset);
                }
            }


            // ── Scene starts blank! No .ing file is loaded automatically. ──
            // User can create UI via the IDE SceneDetail panel or use reload from .ing.
            _sceneRoot.ClearChildren();

            // ── Register scene root for IDE Save All ──
            SceneAssetSerializer.RegisterSceneRoot("LoadingScene", _sceneRoot);

            // ── Expose scene root to IDE bridge so HierarchyPanel can add elements ──
            var bridge = _sceneManager.Bridge;
            if (bridge != null)
            {
                bridge.SceneRoot = _sceneRoot;
                bridge.SceneRootElements = _sceneRoot.Children.AsReadOnly();
            }

            // ── Mark loading complete — actual scene switch happens in Render() ──
            // This gives the main loop one frame to display the loading screen
            // in the Viewport panel before switching to GameScene.
            Console.WriteLine("[LoadingScene] Loading complete — switching on next render frame.");
            _loadingComplete = true;
        }

        public void Update(float deltaTime)
        {
            _deltaTime = deltaTime;
        }

        public void Render()
        {
            bool ingameActive = _sceneManager.Bridge?.InGameActive ?? false;

            // ── Render one more loading frame so the Viewport panel displays it ──
            RenderFrame(_status);

            // ── Ensure bridge texture is set for Viewport panel ──
            var bridge = _sceneManager.Bridge;
            if (bridge != null && _sceneManager.IsIdeActive)
            {
                _sceneManager.EnsureSharedFBOExists();
                bridge.SceneTextureID = _sceneManager.SharedColorTex;
                bridge.SceneTextureWidth = Glfw.WindowWidth;
                bridge.SceneTextureHeight = Glfw.WindowHeight;
            }

            // ── Switch to GameScene on the NEXT frame (after IDE has rendered this frame) ──
            if (_loadingComplete)
            {
                _loadingComplete = false;

                // If we restored a Map2D from the target scene asset, wire it into the IDE
                // bridge before switching so GameScene.Enter() can use map-based player spawn
                // and level camera re-anchoring consistently with the editor in-game path.
                if (_activeTilemap != null)
                {
                    var b = _sceneManager.Bridge;
                    if (b != null)
                        b.ActiveTilemap = _activeTilemap;
                }

                _sceneManager.SwitchScene(_gameScene);
            }
        }

        public void Exit()
        {
            // Resources are handed off to GameScene, so don't dispose them here

            // ── Clear IDE bridge references ──
            var bridge = _sceneManager.Bridge;
            if (bridge != null)
            {
                bridge.SelectedUIElement = null;
                bridge.SceneRootElements = null;
                bridge.SceneRoot = null;
            }
        }

        public void Dispose()
        {
            // HUD was handed off to GameScene — do NOT clean up here, GameScene owns it
            _hud = null;
            _images = null;
        }

        /// <summary>
        /// Render a single frame of the loading screen (Clear, draw image, text, spinner, swap, poll).
        /// Called inline during synchronous loading so the window stays responsive.
        /// When IDE is active, renders into the shared FBO so the loading screen appears in the
        /// Viewport panel instead of taking over the full screen.
        /// </summary>
        private void RenderFrame(string text)
        {
            if (_hud == null || _images == null) return;

            _status = text;

            nint window = Glfw.GetWindow();
            float dt = Glfw.GetDeltaTime();
            bool ideActive = _sceneManager.IsIdeActive;

            // ── When IDE is active, render into the shared FBO so Viewport panel shows loading progress ──
            if (ideActive)
            {
                // Ensure shared FBO exists (creates it on first call)
                _sceneManager.EnsureSharedFBOExists();
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _sceneManager.SharedFBO);
            }

            GL.ClearColor(0, 0, 0, 1);
            GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);

            _hud.DrawImage(0, 0, Glfw.WindowWidth, Glfw.WindowHeight, _images[0].ID);

            var grid = new GridLayout(Glfw.WindowWidth, Glfw.WindowHeight);
            float spinnerSize = 100f;

            // RIGHT-BOTTOM SPINNER — positioned using grid (col 9-11, bottom)
            float spinnerX = grid.ColX(11);
            float spinnerY = Glfw.WindowHeight - spinnerSize - grid.Margin;
            _hud.DrawSpinner(spinnerX, spinnerY, spinnerSize, _images[1].ID, dt);

            // LEFT-BOTTOM TEXT — aligned to grid col 2, vertically centered with spinner
            float spinnerCenterY = spinnerY + spinnerSize * 0.5f;
            var extents = _hud.GetTextExtents(text);
            float textX = grid.ColX(0);
            float textY = spinnerCenterY - (extents.MinY + extents.Height * 0.5f);
            _hud.DrawText(text, textX, textY, new Vector3(0.85f, 0.85f, 0.9f), new Vector3(0f, 0f, 0f), 1.5f);

            // ── Flush all queued HUD commands ──
            _hud.Flush();

            // ── Update bridge texture so Viewport panel shows loading screen ──
            if (ideActive)
            {
                var bridge = _sceneManager.Bridge;
                if (bridge != null)
                {
                    bridge.SceneTextureID = _sceneManager.SharedColorTex;
                    bridge.SceneTextureWidth = Glfw.WindowWidth;
                    bridge.SceneTextureHeight = Glfw.WindowHeight;
                }

                // Restore framebuffer 0 for the IDE's render pass (SceneManager handles swapping)
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);

                // Still poll events to keep the window responsive during long loading
                OpenGL.PollEvents();
            }
            else
            {
                // Normal: swap buffer + poll events to show loading progress on screen
                OpenGL.SwapBuffer(window);
                OpenGL.PollEvents();
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Restore scene content from game.ing into the loaded ObjectManager
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Restore editor-placed 3D objects from a game.ing scene asset into the given manager.
        /// Returns the active Tilemap2D (if any) so the loader can propagate it to the IDE
        /// bridge / level camera path before switching to the game scene.
        /// </summary>
        private Tilemap2D? RestoreEditorObjects(EditorObjectManager manager, SceneAsset asset)
        {
            Tilemap2D? activeTilemap = null;

            if (asset.EditorObjects == null || manager == null) return activeTilemap;

            foreach (var objData in asset.EditorObjects)
            {
                var primType = objData.PrimitiveType.ToLowerInvariant() switch
                {
                    "plane" => EditorPrimitiveType.Plane,
                    "sphere" => EditorPrimitiveType.Sphere,
                    "box" => EditorPrimitiveType.Box,
                    "glbreference" => EditorPrimitiveType.GlbReference,
                    "camera" => EditorPrimitiveType.Camera,
                    "light" => EditorPrimitiveType.Light,
                    "sky" => EditorPrimitiveType.Sky,
                    "map2d" => EditorPrimitiveType.Map2D,
                    "player2d" => EditorPrimitiveType.Player2D,
                    "start2d" => EditorPrimitiveType.Start2D,
                    "camerastart2d" => EditorPrimitiveType.CameraStart2D,
                    _ => EditorPrimitiveType.Box,
                };

                var pos = new System.Numerics.Vector3(objData.PosX, objData.PosY, objData.PosZ);
                var obj = manager.AddPrimitive(primType, pos);
                obj.Name = objData.Name;
                obj.RotationEuler = new System.Numerics.Vector3(objData.RotX, objData.RotY, objData.RotZ);
                obj.Scale = new System.Numerics.Vector3(objData.ScaleX, objData.ScaleY, objData.ScaleZ);
                obj.Color = new System.Numerics.Vector3(objData.ColorR, objData.ColorG, objData.ColorB);
                obj.CastShadow = objData.CastShadow;
                obj.IsVisible = objData.IsVisible;

                // Restore GLB model path (resolved against the exe folder, same as the IDE path).
                if (!string.IsNullOrEmpty(objData.GlbFilePath))
                    obj.GlbFilePath = PathHelpers.Resolve(objData.GlbFilePath);

                // ── Per-type runtime properties ──
                // (Only restore fields the runtime actually uses; the rest are editor-only.)
                obj.CameraFov = objData.CameraFov;
                obj.CameraNear = objData.CameraNear;
                obj.CameraFar = objData.CameraFar;
                obj.LightDirection = new System.Numerics.Vector3(objData.LightDirX, objData.LightDirY, objData.LightDirZ);
                obj.LightIntensity = objData.LightIntensity;
                obj.LightTypeEnum = (LightType)Math.Clamp(objData.LightType, 0, 2);
                obj.LightConeAngle = Math.Clamp(objData.LightConeAngle, 1f, 89f);
                obj.LightPointRadius = Math.Max(0f, objData.LightPointRadius);
                obj.SkyTimeOfDay = objData.SkyTimeOfDay;
                obj.SkySunPitch = objData.SkySunPitch;
                obj.SkySunYaw = objData.SkySunYaw;
                obj.SkyCloudCoverage = objData.SkyCloudCoverage;
                obj.SkySunIntensity = objData.SkySunIntensity;
                obj.SkyTimeAnimSpeed = objData.SkyTimeAnimSpeed;
                obj.SkyTimeAnimPaused = objData.SkyTimeAnimPaused;
                obj.ShowFrustum = objData.ShowFrustum;
                obj.ShowLightGizmo = objData.ShowLightGizmo;
                obj.ShowSkyGizmo = objData.ShowSkyGizmo;
                if (objData.SkySettings != null)
                    obj.SkySettings = objData.SkySettings;

                // ── Player2D sprite sizing + capsule collider (offset lives on the object) ──
                obj.Player2DSpriteSheet = objData.Player2DSpriteSheet;
                obj.Player2DAnimationClip = objData.Player2DAnimationClip;
                obj.Player2DHeight = objData.Player2DHeight;
                obj.Player2DCapsuleRadius = objData.Player2DCapsuleRadius;
                obj.Player2DCapsuleHeight = objData.Player2DCapsuleHeight;
                obj.Player2DCapsuleOffsetX = objData.Player2DCapsuleOffsetX;
                obj.Player2DCapsuleOffsetY = objData.Player2DCapsuleOffsetY;

                // Restore per-object gizmo pivot override (nullable).
                if (objData.PivotOverrideX.HasValue && objData.PivotOverrideY.HasValue && objData.PivotOverrideZ.HasValue)
                    obj.GizmoPivotOverride = new System.Numerics.Vector3(objData.PivotOverrideX.Value, objData.PivotOverrideY.Value, objData.PivotOverrideZ.Value);

                // ── Map2D payload ──
                if (primType == EditorPrimitiveType.Map2D && objData.Tilemap != null)
                {
                    var mapObj = obj as Objects.EditorObject;
                    if (mapObj != null && mapObj.Map2dTilemap == null)
                    {
                        var tilemap = Visual.Tilemap2D.FromData(objData.Tilemap);
                        mapObj.Map2dTilemap = tilemap;
                        activeTilemap = tilemap;
                    }
                }
            }

            return activeTilemap;
        }
    }
}
