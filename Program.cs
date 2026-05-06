using DarkEngine3D_gl_csharp.Engine;
using System.Numerics;
using System.Runtime.CompilerServices;

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

        // Init Camera
        Camera camera = new(0, 5, 20, WindowWidth/ WindowHeight, (float)Math.PI/4 , 0.01f,1000.0f);

        // Init Shader
        Shader.Init();
        Shader.ActiveShader();


        // Init Keyboard and Mouse
        Keyboard.Init(glfwLib, 10.0f);
        Mouse.Init(glfwLib, window);
        
        // Init Terrain
        Terrain gameTerrain = new(256,16); 

        // Init Object3D
        Object3D objTriangle = new(glfwLib, 0.0f, 7.0f, 0.0f);
         
        OpenGL.EnableDepthTest(true);
        OpenGL.EnableFaceCulling(true);

        Glfw.Loop(glfwLib, camera, objTriangle, gameTerrain);        

        Console.WriteLine("Engine Shutdown.");
    }

   
} 