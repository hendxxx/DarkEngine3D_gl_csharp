using DarkEngine3D_gl_csharp.Engine;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DarkEngine3D_gl_csharp;

public unsafe class Program
{ 
    public static void Main()
    {

        // Load Library GLFW
        Glfw.Init(1920,1080, "My Native C# Engine");
        IntPtr glfwLib = Glfw.GetglfwLib();
        IntPtr window = Glfw.GetWindow();

        // Load Library OpenGL
        OpenGL.Init();

        // Init Camera
        Camera camera = new(0, 5, 20);
         
        Shader.Init();
        Shader.ActiveShader();
        uint shaderProgram = Shader.GetShaderProgram();
        uint lineShaderProgram = Shader.GetLineShaderProgram();

        // Init Keyboard and Mouse
        Keyboard.Init(glfwLib, lineShaderProgram);
        Mouse.Init(glfwLib, window);

        //Terrain terrain = new Terrain();
        //terrain.Generate(shaderProgram, 0, 0);
        Terrain gameTerrain = new();
        gameTerrain.Init(shaderProgram); 
        
        //TerrainManager terrainMan = new();
         
        Object3D objTriangle = new(glfwLib, shaderProgram, 0, 1, 0);

        // Pastikan nama string "view" dan "projection" sama persis dengan yang ada di kode GLSL Anda
        int viewLocation = Shader.GetView();
        int projectionLocation = Shader.GetProjection();

        GL.Enable(Const.GL_CULL_FACE);
        GL.CullFace(Const.GL_BACK);
        GL.FrontFace(Const.GL_CCW);

        Glfw.Loop(glfwLib, camera, objTriangle, shaderProgram, viewLocation, projectionLocation, gameTerrain);        

        Console.WriteLine("Engine Shutdown.");
    }

   
} 