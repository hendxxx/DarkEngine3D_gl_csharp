using System.Net.NetworkInformation;
using System.Runtime.Intrinsics.X86;

namespace DarkEngine3D_gl_csharp.Engine.Libs
{
    public unsafe class Shader  
    {
        static uint shaderProgram;
        static uint lineShaderProgram; 
        static uint hudShaderProgram; 
        static uint skyShaderProgram;
        static uint postProdShaderProgram;
         
        public Shader()
        {
            Init();
        }
        public static void Init()
        { 
            shaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/vertex_shader.glsl",
                                      "Artifacts/shaders/fragment_shader.glsl");

            lineShaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/lineVertex_shader.glsl",
                                             "Artifacts/shaders/lineFragment_shader.glsl");

            hudShaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/hudVertex_shader.glsl",
                                             "Artifacts/shaders/hudFragment_shader.glsl");

            skyShaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/sky_vertex.glsl",
                                             "Artifacts/shaders/sky_fragment.glsl");

            postProdShaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/postPro_vertex.glsl",
                                               "Artifacts/shaders/postPro_fragment.glsl");
             
        } 

        public static uint GetPostProdShaderProgram()
        {

            return postProdShaderProgram;
        }

        public static uint GetSkyShaderProgram()
        {

            return skyShaderProgram;
        }
        public static uint GetShaderProgram()
        {

            return shaderProgram;
        }
        public static uint GetLineShaderProgram()
        {
            return lineShaderProgram;
        }
        public static uint GetHudShaderProgram()
        {
            return hudShaderProgram;
        }
         
    }
}
