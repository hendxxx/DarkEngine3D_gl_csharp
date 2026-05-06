namespace DarkEngine3D_gl_csharp.Engine
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
        }

        public static void ActiveShader()
        {
             
            SetView();
            SetProjection();

            SetLineView();
            SetLineProjection(); 

        }
        public static void Cleanup()
        {
            // Bersihkan shader individu (opsional)
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            GL.DeleteShader(lineVertexShader);
            GL.DeleteShader(lineFragmentShader);

        }

        public static uint GetShaderProgram()
        {

            return shaderProgram;
        }
        public static uint GetLineShaderProgram()
        {
            return lineShaderProgram;
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

        public static void SetLineView()
        {

            viewLineLocation = GL.GetUniformLocation(lineShaderProgram, "view"); 
        }
        public static void SetLineProjection()
        {
             
            projectionLineLocation = GL.GetUniformLocation(lineShaderProgram, "projection");
             
        }

        public static void SetView( ) {

            viewLocation = GL.GetUniformLocation(shaderProgram, "view"); 

        }

        public static void SetProjection( )
        {
            projectionLocation = GL.GetUniformLocation(shaderProgram, "projection");
        }
    }
}
