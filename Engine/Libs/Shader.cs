using System.Runtime.Intrinsics.X86;

namespace DarkEngine3D_gl_csharp.Engine.Libs
{
    public unsafe class Shader
    {
        static uint shaderProgram;
        static uint lineShaderProgram;

        static int viewLocation;
        static int projectionLocation;
        static int viewLineLocation;
        static int projectionLineLocation;

        static uint vertexShader;
        static uint fragmentShader;

        static uint lineVertexShader;
        static uint lineFragmentShader;
        
        static uint hudShaderProgram;
        static uint hudVertexShader;
        static uint hudFragmentShader;

        static uint skyShaderProgram;

        static int useTextureLoc;
        static int tex0Loc;
        static int tex1Loc;
        static int tex2Loc;
        static int tex3Loc;
        static int tex4Loc;
        static int tex5Loc;
        static int heightScaleLoc; 
        static int showLODColorLoc;
        static int lodLevelLoc;

        public static void Init()
        {
            //load shader file from artifacts folder
            string vertexShaderSource = File.ReadAllText("Artifacts\\shaders\\vertex_shader.glsl");
            string fragmentShaderSource = File.ReadAllText("Artifacts\\shaders\\fragment_shader.glsl");


            // 1. Buat & Compile Vertex Shader
            vertexShader = GL.CreateShader(Const.GL_VERTEX_SHADER);
            GL.ShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);

            // 2. Buat & Compile Fragment Shader
            fragmentShader = GL.CreateShader(Const.GL_FRAGMENT_SHADER);
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
            GL.CompileShader(fragmentShader);

            // 3. Link ke dalam Shader Program
            shaderProgram = GL.CreateProgram();
            GL.AttachShader(shaderProgram, vertexShader);
            GL.AttachShader(shaderProgram, fragmentShader);
            GL.LinkProgram(shaderProgram);

            string lineVertexShaderSource = File.ReadAllText("Artifacts\\shaders\\lineVertex_shader.glsl");
            string lineFragmentShaderSource = File.ReadAllText("Artifacts\\shaders\\lineFragment_shader.glsl");
            // 1. Buat & Compile Vertex Shader
            lineVertexShader = GL.CreateShader(Const.GL_VERTEX_SHADER);
            GL.ShaderSource(lineVertexShader, lineVertexShaderSource);
            GL.CompileShader(lineVertexShader);

            // 2. Buat & Compile Fragment Shader
            lineFragmentShader = GL.CreateShader(Const.GL_FRAGMENT_SHADER);
            GL.ShaderSource(lineFragmentShader, lineFragmentShaderSource);
            GL.CompileShader(lineFragmentShader);

            // 3. Link ke dalam Shader Program Line
            lineShaderProgram = GL.CreateProgram();
            GL.AttachShader(lineShaderProgram, lineVertexShader);
            GL.AttachShader(lineShaderProgram, lineFragmentShader);
            GL.LinkProgram(lineShaderProgram);
             
            string hudVSource = File.ReadAllText("Artifacts\\shaders\\hudVertex_shader.glsl");
            string hudFSource = File.ReadAllText("Artifacts\\shaders\\hudFragment_shader.glsl");
            
            hudVertexShader = GL.CreateShader(Const.GL_VERTEX_SHADER);
            GL.ShaderSource(hudVertexShader, hudVSource);
            GL.CompileShader(hudVertexShader);

            hudFragmentShader = GL.CreateShader(Const.GL_FRAGMENT_SHADER);
            GL.ShaderSource(hudFragmentShader, hudFSource);
            GL.CompileShader(hudFragmentShader);

            hudShaderProgram = GL.CreateProgram();
            GL.AttachShader(hudShaderProgram, hudVertexShader);
            GL.AttachShader(hudShaderProgram, hudFragmentShader);
            GL.LinkProgram(hudShaderProgram);

            // Compile Sky Shader
            string vSource = File.ReadAllText("Artifacts\\shaders\\sky_vertex.glsl");
            string fSource = File.ReadAllText("Artifacts\\shaders\\sky_fragment.glsl");

            uint vs = GL.CreateShader(Const.GL_VERTEX_SHADER);
            GL.ShaderSource(vs, vSource);
            GL.CompileShader(vs);

            uint fs = GL.CreateShader(Const.GL_FRAGMENT_SHADER);
            GL.ShaderSource(fs, fSource);
            GL.CompileShader(fs);

            skyShaderProgram = GL.CreateProgram();
            GL.AttachShader(skyShaderProgram, vs);
            GL.AttachShader(skyShaderProgram, fs);
            GL.LinkProgram(skyShaderProgram);




            // Bersihkan shader individu setelah link (objects tidak lagi dibutuhkan)
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            GL.DeleteShader(lineVertexShader);
            GL.DeleteShader(lineFragmentShader); 
            GL.DeleteShader(hudVertexShader);
            GL.DeleteShader(hudFragmentShader);


            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
        }

        public static void ActiveShader()
        {

            SetView();
            SetProjection();

            SetLineView();
            SetLineProjection();

            SetUseTexture();
            SetTerrainTexture(); 

        }
        public static void Cleanup()
        {

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

        public static int GetTexLoc(int index)
        {
            return index switch
            {
                0 => tex0Loc,
                1 => tex1Loc,
                2 => tex2Loc,
                3 => tex3Loc,
                4 => tex4Loc,
                5 => tex5Loc,
                _ => tex0Loc
            };
        }
        public static int GetHeightScaleLoc()
        {
            return heightScaleLoc;
        }
        public static int GetView()
        {

            return viewLocation;
        }
        public static int GetProjection()
        {
            return projectionLocation;
        }


        public static int GetLineView()
        {

            return viewLineLocation;
        }
        public static int GetLineProjection()
        {
            return projectionLineLocation;
        }
        public static int GetUseTexture()
        {
            return useTextureLoc;

        }
        public static void SetLineView()
        {

            viewLineLocation = GL.GetUniformLocation(lineShaderProgram, "view");
        }
        public static void SetLineProjection()
        {

            projectionLineLocation = GL.GetUniformLocation(lineShaderProgram, "projection");

        }

        public static void SetView()
        {

            viewLocation = GL.GetUniformLocation(shaderProgram, "view");

        }
        public static void SetTerrainTexture()
        {
            tex0Loc = GL.GetUniformLocation(shaderProgram, "tex0");
            tex1Loc = GL.GetUniformLocation(shaderProgram, "tex1");
            tex2Loc = GL.GetUniformLocation(shaderProgram, "tex2");
            tex3Loc = GL.GetUniformLocation(shaderProgram, "tex3");
            tex4Loc = GL.GetUniformLocation(shaderProgram, "tex4");
            tex5Loc = GL.GetUniformLocation(shaderProgram, "tex5");
            heightScaleLoc = GL.GetUniformLocation(shaderProgram, "heightScale");
            showLODColorLoc = GL.GetUniformLocation(shaderProgram, "showLODColor");
            lodLevelLoc = GL.GetUniformLocation(shaderProgram, "lodLevel");
        }

        public static int GetShowLODColorLoc() => showLODColorLoc;
        public static int GetLodLevelLoc() => lodLevelLoc;

        public static void SetProjection()
        {
            projectionLocation = GL.GetUniformLocation(shaderProgram, "projection");
        }
        public static void SetUseTexture()
        {
            useTextureLoc = GL.GetUniformLocation(shaderProgram, "useTexture");

        }
         

    }
}
