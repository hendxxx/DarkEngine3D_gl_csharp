using DarkEngine3D_gl_csharp.Engine;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp;

public unsafe class Program
{
   
    public static void Main()
    {
        int WindowWidth = 1920;
        int WindowHeight = 1080;

        // Init GLFW and Create Window
        Glfw.Init(WindowWidth, WindowHeight, "My Native C# Engine");
        
        // Load Library GLFW
        IntPtr glfwLib = Glfw.GetglfwLib();
        IntPtr window = Glfw.GetWindow();
        
        // Load Library OpenGL
        OpenGL.Init();

        OpenGL.EnableDepthTest(true);
        OpenGL.EnableFaceCulling(true);

        // Init Shader
        Shader.Init();
        Shader.ActiveShader();

        // Init Camera
        Camera camera = new(0, 5, 20, WindowWidth/ WindowHeight, (float)Math.PI/4 , 0.01f,10000.0f);

        Vector3 sunDirLoc = new(-0.2f, -1.0f, -0.3f);
        Vector3 sunColorLoc = new(1.0f, 1.0f, 1.0f); // Cahaya Putih
        Vector3 viewPosLoc = new(camera.Position.X, camera.Position.Y, camera.Position.Z); // Cahaya Putih


          
        // Init Keyboard and Mouse
        Keyboard.Init(glfwLib, 10.0f);
        Mouse.Init(glfwLib, window);
        
        // Init TerrainChunk
        TerrainChunk gameTerrainChunk = new("Artifacts\\Maps\\map.jpg"); 

        // Init Object3D
        Object3D objTriangle = new(glfwLib, 0.0f, 15.0f, 0.0f);

        Lights light = new(sunDirLoc, sunColorLoc, viewPosLoc);

        // 2D UI overlay (HP bar, quick slots, FPS + coords).
        UIRenderer ui = new();

        // 10 random moving cubes that bump each other and damage the player on contact.
        MovingObjects movers = new(10);

        Glfw.Loop(glfwLib, camera, light, objTriangle, gameTerrainChunk, ui, movers);

        Console.WriteLine("Engine Shutdown."); 
    }

   
} 