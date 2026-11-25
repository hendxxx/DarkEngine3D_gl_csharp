namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    // ===========================================================================
    //  GltfShader — static mesh shader (compile once, reuse)
    // ===========================================================================
    public static class GltfShader
    {
        private static uint _shaderProgram;
        private static bool _loadFailureLogged;

        public static uint GetShaderProgram()
        {
            // Retry until the program really exists. The old "_initialized" latch was set
            // even when LoadShader returned 0 (shader file not resolvable yet, first project
            // still opening), which left every GLB reference invisible for the rest of the
            // session no matter what was loaded afterwards.
            if (_shaderProgram == 0) Init();
            return _shaderProgram;
        }

        public static void Init()
        {
            if (_shaderProgram != 0) return;

            //string vSrc = File.ReadAllText("Artifacts\\shaders\\gltf_vertex.glsl");
            //string fSrc = File.ReadAllText("Artifacts\\shaders\\gltf_fragment.glsl");

            //uint vs = GL.CreateShader(Const.GL_VERTEX_SHADER);
            //GL.ShaderSource(vs, vSrc);
            //GL.CompileShader(vs);
            //string vsLog = GL.GetShaderInfoLog(vs);
            //if (!string.IsNullOrEmpty(vsLog))
            //    Console.WriteLine($"[GltfShader] VERTEX ERROR:\n{vsLog}");

            //uint fs = GL.CreateShader(Const.GL_FRAGMENT_SHADER);
            //GL.ShaderSource(fs, fSrc);
            //GL.CompileShader(fs);
            //string fsLog = GL.GetShaderInfoLog(fs);
            //if (!string.IsNullOrEmpty(fsLog))
            //    Console.WriteLine($"[GltfShader] FRAGMENT ERROR:\n{fsLog}");

            //_program = GL.CreateProgram();
            //GL.AttachShader(_program, vs);
            //GL.AttachShader(_program, fs);
            //GL.LinkProgram(_program);
            //string pLog = GL.GetProgramInfoLog(_program);
            //if (!string.IsNullOrEmpty(pLog))
            //    Console.WriteLine($"[GltfShader] LINK ERROR:\n{pLog}");

            //GL.DeleteShader(vs);
            //GL.DeleteShader(fs);

            //_initialized = true;
            //Console.WriteLine($"[GltfShader] Program ID={_program}. Ready.");
            _shaderProgram = Helpers.ShaderHelpers.LoadShader("Artifacts/shaders/gltf_vertex.glsl",
                                      "Artifacts/shaders/gltf_fragment.glsl");

            if (_shaderProgram == 0 && !_loadFailureLogged)
            {
                _loadFailureLogged = true;
                Console.WriteLine("[GltfShader] gltf shader failed to load — GLB references cannot render yet; retrying on next use.");
            }
        } 
    }
}
