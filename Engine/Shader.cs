namespace DarkEngine3D_gl_csharp.Engine
{
    public unsafe class Shader
    {
        static uint shaderProgram;
        static int viewLocation;
        static int projectionLocation;
        static uint vertexShader;
        static uint fragmentShader;

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
        }

        public static void ActiveShader()
        {
             
            SetView();
            SetProjection();

        }
        public static void Cleanup()
        {
            // Bersihkan shader individu (opsional)
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        public static uint GetShaderProgram()
        {

            return shaderProgram;
        }
        public static int GetView()
        {

            return viewLocation;
        }

        public static int GetProjection()
        {
            return projectionLocation;
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
