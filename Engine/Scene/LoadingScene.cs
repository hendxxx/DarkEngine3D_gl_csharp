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
    /// When loading completes, switches to the GameScene.
    /// </summary>
    public class LoadingScene : IScene
    {
        private readonly SceneManager _sceneManager;
        private readonly GameScene _gameScene;

        // Loading UI
        private HUD? _hud;
        private Texture[]? _images;
        private string _status = "Loading...";
        private float _deltaTime = 0f;

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
            nint window = Glfw.GetWindow();

            // ── Loading screen UI assets ──
            Texture[] images =
            [
                new("Artifacts\\images\\loading01_image.png"),
                new("Artifacts\\images\\spinner01_image.png"),
            ];
            _images = images;

            HUD hud = new("Artifacts\\fonts\\Ngaco.ttf", 28.0f);
            _hud = hud;

            RenderFrame("Loading engine ...");
            Thread.Sleep(500);

            // ── Terrain textures ──
            Texture[] TerrainTextures =
            [
                new("Artifacts\\textures\\aerial grass\\aerial_grass_rock_diff_4k.jpg"),
                new("Artifacts\\textures\\aerial rock\\aerial_rocks_04_diff_4k.jpg"),
                new("Artifacts\\textures\\snow\\snow_01_diff_4k.jpg"),
                new("Artifacts\\textures\\cliff side\\cliff_side_diff_4k.jpg"),
            ];

            // ── Sky textures ──
            Texture[] SkyTextures =
            [
                new("Artifacts\\textures\\moon.png"),
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
            string mapPath = "Artifacts\\maps\\map.png";
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

            // ── Switch to GameScene ──
            Console.WriteLine("[LoadingScene] Loading complete — switching to GameScene.");
            _sceneManager.SwitchScene(_gameScene);
        }

        public void Update(float deltaTime)
        {
            _deltaTime = deltaTime;
        }

        public void Render()
        {
            // If for some reason we're still rendering while waiting for switch,
            // show the last frame of the loading screen
            RenderFrame(_status);
        }

        public void Exit()
        {
            // Resources are handed off to GameScene, so don't dispose them here
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
        /// </summary>
        private void RenderFrame(string text)
        {
            if (_hud == null || _images == null) return;

            _status = text;

            nint window = Glfw.GetWindow();
            float dt = Glfw.GetDeltaTime();

            GL.ClearColor(0, 0, 0, 1);
            GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);

            _hud.DrawImage(0, 0, Glfw.WindowWidth, Glfw.WindowHeight, _images[0].ID);

            float margin = 40;
            float spinnerSize = 128;

            // RIGHT-BOTTOM SPINNER — anchor for vertical alignment
            float spinnerX = Glfw.WindowWidth - spinnerSize - margin;
            float spinnerY = Glfw.WindowHeight - spinnerSize - margin;
            _hud.DrawSpinner(spinnerX, spinnerY, spinnerSize, _images[1].ID, dt);

            // LEFT-BOTTOM TEXT — vertically aligned to spinner center using TextExtents
            float spinnerCenterY = spinnerY + spinnerSize * 0.5f;
            var extents = _hud.GetTextExtents(text);
            float textX = margin;
            float textY = spinnerCenterY - (extents.MinY + extents.Height * 0.5f);
            _hud.DrawText(text, textX, textY, new Vector3(0, 0, 0));

            OpenGL.SwapBuffer(window);
            OpenGL.PollEvents();
        }
    }
}
