using DarkEngine3D_gl_csharp.Engine.Config;
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
        private readonly GameScene _gameScene;

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

        // When true, synchronous loading is done and we should switch to GameScene
        private bool _loadingComplete = false;

        // Game resources that will be created during loading
        private Camera? _camera;
        private Lights? _light;
        private TerrainChunk? _gameTerrainChunk;
        private Skybox? _skybox;
        private ObjectManager? _objectManager;
        private Texture[]? _skyTextures;

        public string Name => "LoadingScene";

        public LoadingScene(SceneManager sceneManager, Camera camera, Lights light)
        {
            _sceneManager = sceneManager;
            _camera = camera;
            _light = light;

            // Create GameScene early (we'll pass fully loaded resources to it)
            _gameScene = new GameScene(sceneManager, camera, light);
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

            HUD hud = new("Artifacts\\\\fonts\\\\Ngaco.ttf", 28.0f);
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

            // ── Finalize: set up GameScene with loaded resources ──
            _gameScene.SetResources(_skyTextures, _gameTerrainChunk, _skybox, _hud, _objectManager);

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
            // Cleanup loading screen specific resources
            _images = null;
            _hud = null;
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
    }
}
