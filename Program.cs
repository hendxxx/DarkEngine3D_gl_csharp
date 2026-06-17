using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;
using System.Runtime.InteropServices;
using static System.Net.Mime.MediaTypeNames;

namespace DarkEngine3D_gl_csharp;

public unsafe class Program
{    
    private static float deltaTime =  0.0f;
    public static void Main()
    {
        //Glfw.WindowWidth = 2560;
        //Glfw.WindowHeight = 1440;
        Glfw.WindowWidth = 1920;
        Glfw.WindowHeight = 1080;

        // Init GLFW and Create Window
        //Glfw.Init("My Native C# Engine", true);
        Glfw.Init("My Native C# Engine", false);

        // Load Library GLFW
        IntPtr glfwLib = Glfw.GetglfwLib();
        IntPtr window = Glfw.GetWindow();

        // Load Library OpenGL
        OpenGL.Init();
        OpenGL.CacheGlfwFunctions(glfwLib);

        var glVersion = GL.GetString(Const.VERSION);
        Console.WriteLine($"Versi OpenGL aktif: {glVersion}");

        OpenGL.EnableDepthTest(true);
        OpenGL.EnableFaceCulling(true);  // Enable culling from start - default state

        // Init Shader
        Shader.Init(); 
        
        // Init Camera
        Camera camera = new(0, 0, 0, PlayerConfig.InitialHeading, 10, Glfw.WindowWidth / Glfw.WindowHeight, (float)Math.PI / 4, 0.1f, 2500.0f);
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

        Texture[] images =
        [
            new("Artifacts\\images\\loading01_image.png"),
            new("Artifacts\\images\\spinner01_image.png"),

        ];

        // Init HUD (On-Screen Display)
        HUD hud = new("Artifacts\\fonts\\Ngaco.ttf", 32.0f);

        deltaTime = Glfw.GetDeltaTime();
        UpdateLoading(window, deltaTime, hud, images, "Loading engine ...");
        Thread.Sleep(500);

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
        TerrainChunk.HeightScale = 50.0f;
        TerrainChunk.TerrainScale = 1.0f;
        TerrainChunk.OnLoadProgress += (progress) =>
        {
            int filled = (int)(progress * 20);
            string bar = new string('#', filled) + new string('-', 20 - filled);
            Console.Write($"\rTerrain Loading: [{bar}] {progress * 100:F1}%");


            UpdateLoading(window, deltaTime, hud, images, $"Loading Terrain {progress * 100:F1}%");


            if (progress >= 1.0f)
                Console.WriteLine(); // newline setelah selesai
        };


        // Generate a high-quality procedural heightmap if it doesn't exist
        string mapPath = "Artifacts\\maps\\map.png";
        if (!File.Exists(mapPath))
        {
            MapLoader.GeneratePhotorealHeightmap(mapPath, 513); // 513x513 standard size
        }

        TerrainChunk gameTerrainChunk = new(mapPath, TerrainTextures);

        // Init Skybox
        Skybox skybox = new();
         
        // Init ObjectManager & spawn 10 Xbot di area ~5×5 meter
        GltfShader.Init(); // Compile gltf shader setelah OpenGL siap

        UpdateLoading(window, deltaTime, hud, images, "Loading objects ... ");

        ObjectManager objectManager = new();
        objectManager.Init(camera,gameTerrainChunk);

         
        StaticObjectManager[] staticManagers = new[]
        {
            new StaticObjectManager { RotationCorrection = new Vector3(180, 0, 0) },
            new StaticObjectManager { RotationCorrection = new Vector3(-90, 0, 0) }
        };

        staticManagers[0].AddRandomObjects("Artifacts/objects/biomes/trees.glb", 20, new Vector3(0, 0, 0), 100f, gameTerrainChunk);
        staticManagers[1].AddRandomObjects("Artifacts/objects/biomes/daises.glb", 50, new Vector3(0, 0, 0), 100f, gameTerrainChunk);

        Thread.Sleep(500);

        Mouse.ShowMouse(false); 

        // Init Loop
        Glfw.Loop( SkyTextures, camera, light, gameTerrainChunk, skybox, hud, objectManager, staticManagers);
        //Glfw.Loop( SkyTextures, camera, light, null, null, skybox, hud,null);
         
        // Shutdown
        Console.WriteLine("Engine Shutdown.");
    }

    private static void UpdateLoading(nint window, float deltaTime, HUD hud, Texture[] images, string text )
    {
        GL.ClearColor(0, 0, 0, 1);
        GL.Clear(Const.GL_COLOR_BUFFER_BIT | Const.GL_DEPTH_BUFFER_BIT);
        deltaTime = Glfw.GetDeltaTime();

        hud.DrawImage(0, 0, Glfw.WindowWidth, Glfw.WindowHeight, images[0].ID);
        float margin = 40;
        float spinnerSize = 128;

        // LEFT-BOTTOM TEXT
        float textX = margin;
        float textY = Glfw.WindowHeight - margin;
        hud.DrawText(text, textX, textY, new Vector3(0,0,0));

        // RIGHT-BOTTOM SPINNER
        float spinnerX = Glfw.WindowWidth - spinnerSize - margin;
        float spinnerY = Glfw.WindowHeight - spinnerSize - margin;
        hud.DrawSpinner(spinnerX, spinnerY, spinnerSize, images[1].ID, deltaTime);


        OpenGL.SwapBuffer(window);
        OpenGL.PollEvents();
    }
}