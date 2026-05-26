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
        //// Add this right after GltfShader.Init() and before spawning objects
        //{
        //    Console.WriteLine("\n=== GLB INSPECTION ===");
        //    var inspectData = GltfLoader.Load("Artifacts\\objects\\Stuntman.glb");

        //    Console.WriteLine($"Meshes: {inspectData.Meshes.Length}");
        //    for (int i = 0; i < inspectData.Meshes.Length; i++)
        //    {
        //        var m = inspectData.Meshes[i];
        //        bool hasJoints = m.Vertices.Length > 0 && m.Vertices[0].BoneIds.X >= 0;
        //        bool hasWeights = m.Vertices.Length > 0 && m.Vertices[0].BoneWeights.X > 0;
        //        Console.WriteLine($"  [{i}] '{m.Name}': verts={m.Vertices.Length}, hasJoints={hasJoints}, hasWeights={hasWeights}");
        //    }

        //    Console.WriteLine($"Nodes: {inspectData.Nodes.Length}");
        //    for (int i = 0; i < inspectData.Nodes.Length; i++)
        //        Console.WriteLine($"  [{i}] '{inspectData.Nodes[i].Name}' mesh={inspectData.Nodes[i].Mesh}");

        //    Console.WriteLine($"Skins: {inspectData.Skins.Length}");
        //    for (int i = 0; i < inspectData.Skins.Length; i++)
        //        Console.WriteLine($"  [{i}] joints={inspectData.Skins[i].Joints.Length}, invBindMat={inspectData.Skins[i].InverseBindMatrices.Length}");

        //    Console.WriteLine($"Animations: {inspectData.Animations.Length}");
        //    for (int i = 0; i < inspectData.Animations.Length; i++)
        //        Console.WriteLine($"  [{i}] '{inspectData.Animations[i].Name}' duration={inspectData.Animations[i].Duration:F3}s, channels={inspectData.Animations[i].Channels.Length}");

        //    Console.WriteLine("===================\n");
        //}
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
        TerrainChunk.GlobalLODLevel = 3;
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

        objectManager.Init(gameTerrainChunk);

          
        // Init Loop
        Glfw.Loop(SkyTextures, camera, light, objTriangle, gameTerrainChunk, skybox, hud, objectManager);


        // Shutdown
        Console.WriteLine("Engine Shutdown.");
    }

}