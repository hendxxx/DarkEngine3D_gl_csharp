namespace DarkEngine3D_gl_csharp.Engine.Libs
{
    public unsafe class Shader  
    {
        static uint shaderProgram;
        static uint lineShaderProgram; 
        static uint hudShaderProgram; 
        static uint skyShaderProgram;
        static uint invertPassShaderProgram;
        static uint rainStreakShaderProgram;
        static uint rainOverlayShaderProgram;
         
        public Shader()
        {
            Init();
        }
        public static void Init()
        { 
            shaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/vertex_shader.glsl", "Artifacts/shaders/fragment_shader.glsl");

            lineShaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/lineVertex_shader.glsl", "Artifacts/shaders/lineFragment_shader.glsl");

            hudShaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/hudVertex_shader.glsl", "Artifacts/shaders/hudFragment_shader.glsl");

            skyShaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/sky_vertex.glsl", "Artifacts/shaders/sky_fragment.glsl");

            invertPassShaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/invertPass_vertex.glsl", "Artifacts/shaders/invertPass_fragment.glsl");
           
            rainStreakShaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/RainStreaks_vertex.glsl", "Artifacts/shaders/RainStreaks_fragment.glsl");
            
            rainOverlayShaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/RainOverlay_vertex.glsl", "Artifacts/shaders/RainOverlay_fragment.glsl");
             
        }
        public static uint GetRainStreakShaderProgram()
        {

            return rainStreakShaderProgram;
        }
        public static uint GetRainOverlayShaderProgram()
        {

            return rainOverlayShaderProgram;
        }

        public static uint GetInvertPassShaderProgram()
        {

            return invertPassShaderProgram;
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
