using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using System.Numerics;

namespace DarkEngine3D_gl_csharp;

public unsafe class Program
{

    public static void Main()
    {
        Glfw.WindowWidth = 1920;
        Glfw.WindowHeight = 1080;

        // Init GLFW and Create Window
        Glfw.Init("My Native C# Engine", false);

        // Load Library GLFW
        IntPtr glfwLib = Glfw.GetglfwLib();
        IntPtr window = Glfw.GetWindow();

        // Load Library OpenGL
        OpenGL.Init();
        OpenGL.CacheGlfwFunctions(glfwLib);

        OpenGL.EnableDepthTest(true);
        OpenGL.EnableFaceCulling(true);

        // Init Shader
        Shader.Init();
        Shader.ActiveShader();

        // Init Camera
        Camera camera = new(0, 50, 0, Glfw.WindowWidth / Glfw.WindowHeight, (float)Math.PI / 4, 0.01f, 10000.0f);
        Glfw.SetMainCamera(camera);

        Keyboard.IsFogActive = false;

        // Init Light
        // Semakin kecil nilai Y (mendekati 0), semakin panjang bayangannya (dramatis)
        Vector3 sunDirLoc = new(1.0f, 0.5f, 0.0f);

        // Opsional: Kalau siang hari terlalu putih, warnanya bisa dibuat agak kekuningan
        Vector3 sunColorLoc = new(1.0f, 0.95f, 0.8f);
        Vector3 viewPosLoc = new(camera.Position.X, camera.Position.Y, camera.Position.Z); // Cahaya Putih
        Lights light = new(sunDirLoc, sunColorLoc, viewPosLoc, "17:00");

        // Init Keyboard and Mouse
        Keyboard.Init(glfwLib, 1.0f); // Increased speed for freefly mode
        Mouse.Init(glfwLib, window);

        // Init Terrain textures
        Texture[] TerrainTextures =
        [
            // new("Artifacts\\Textures\\dark_grass.png"), // texture 0 Grass
            // new("Artifacts\\Textures\\light_grass.png"), // texture 1 Dark Grass
            new("Artifacts\\textures\\aerial grass\\aerial_grass_rock_diff_4k.jpg"), // texture 0 Rock
            new("Artifacts\\textures\\aerial rock\\aerial_rocks_04_diff_4k.jpg"),  // texture 1 Dark Rock
            new("Artifacts\\textures\\snow\\snow_01_diff_4k.jpg"), // texture 2 Snow
            new("Artifacts\\textures\\cliff side\\cliff_side_diff_4k.jpg")

        ];

        //Init Sky Textures
        Texture[] SkyTextures =
        [
            new("Artifacts\\textures\\moon.png")

        ];

        // Init TerrainChunk
        TerrainChunk.GlobalLODLevel = 1;
        TerrainChunk.HeightScale = 80.0f;
        TerrainChunk.TerrainScale = 1.0f;
        TerrainChunk.OnLoadProgress += (progress) =>
        {
            int filled = (int)(progress * 20);
            string bar = new string('#', filled) + new string('-', 20 - filled);
            Console.Write($"\rTerrain Loading: [{bar}] {progress * 100:F1}%");

            if (progress >= 1.0f)
                Console.WriteLine(); // newline setelah selesai
        };


        // Generate a high-quality procedural heightmap if it doesn't exist
        string mapPath = "Artifacts\\maps\\photoreal_v1.raw";
        if (!File.Exists(mapPath))
        {
            MapLoader.GeneratePhotorealHeightmap(mapPath, 513); // 513x513 standard size
        }

        TerrainChunk gameTerrainChunk = new(mapPath, TerrainTextures);

        // Init Skybox
        Skybox skybox = new();

        // Init Object3D
        Object3D objTriangle = new(glfwLib, 0.0f, 5.0f, 0.0f);

        // Init HUD (On-Screen Display)
        HUD hud = new("Artifacts\\fonts\\Ngaco.ttf", 32.0f);

        // Init ObjectManager & spawn 10 Xbot di area ~5×5 meter
        GltfShader.Init(); // Compile gltf shader setelah OpenGL siap
        ObjectManager objectManager = new();

        string xbotPath = "Artifacts\\objects\\Stuntman.glb";
        var rng = new Random(42);

        float spawnCX = 0f;
        float spawnCZ = 0f;
        float minDist = 1.5f;   // jarak minimum antar object (meter)
        float spawnRadius = 8f; // area spawn

        var spawnedPositions = new List<Vector2>();

        for (int i = 0; i < 10; i++)
        {
            float px, pz;
            int tries = 0;
            do
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float dist = (float)(rng.NextDouble() * spawnRadius);
                px = spawnCX + MathF.Cos(angle) * dist;
                pz = spawnCZ + MathF.Sin(angle) * dist;
                tries++;

                // Cek overlap dengan semua posisi yang sudah ada
                bool overlaps = spawnedPositions.Any(p =>
                    MathF.Sqrt((p.X - px) * (p.X - px) + (p.Y - pz) * (p.Y - pz)) < minDist);
                if (!overlaps) break;

                // Jika terlalu banyak percobaan, paksa geser
                if (tries > 50)
                {
                    px += minDist * MathF.Cos(i * 1.1f);
                    pz += minDist * MathF.Sin(i * 1.1f);
                    break;
                }
            } while (true);

            spawnedPositions.Add(new Vector2(px, pz));
            float yaw = (float)(rng.NextDouble() * 360.0);

            var obj = objectManager.AddObject(xbotPath, new Vector3(px, 0, pz), yaw, 1.0f);
            objectManager.SnapToTerrain(obj, gameTerrainChunk);
            Console.WriteLine($"[Spawn] Xbot #{i + 1} pos=({px:F1}, {obj.Position.Y:F1}, {pz:F1}) yaw={yaw:F0}° tries={tries}");
        }


        Console.WriteLine($"[ObjectManager] {objectManager.GetObjects().Count} objects spawned.");
        objectManager.DisableFrustumCull = true; // DEBUG: bypass frustum cull sementara

        // Init Loop
        Glfw.Loop(SkyTextures, camera, light, objTriangle, gameTerrainChunk, skybox, hud, objectManager);


        // Shutdown
        Console.WriteLine("Engine Shutdown.");
    }

}