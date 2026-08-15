namespace DarkEngine3D_gl_csharp.Engine.Libs
{
    public unsafe class Shader
    {
        static uint shaderProgram;
        static uint lineShaderProgram;
        static uint hudShaderProgram;
        static uint skyShaderProgram;
        static uint invertPassShaderProgram;
        static uint blurPassShaderProgram;
        static uint outlineShaderProgram;

        // ── AAA post-processing: bloom (bright-pass + separable blur) and final
        // composite (scene + bloom → ACES tonemap → gamma) ──
        static uint postFxBrightShaderProgram;
        static uint postFxBlurShaderProgram;
        static uint postFxCompositeShaderProgram;
        
        static uint shadowShaderProgram;
        static uint shadowSkinnedShaderProgram;
        static uint shadowStaticAlphaShaderProgram;

        // Editor terrain (Plane → advanced terrain) — separate program so the game's
        // terrain fragment shader (with hardcoded height/slope bands) stays untouched.
        static uint editorTerrainShaderProgram;

        // Editor primitives with a PBR material (Box/Sphere/flat plane) — dedicated
        // program with 7 optional maps + tuning (reuses the shared vertex shader).
        static uint objectPbrShaderProgram;

        static uint rainStreakShaderProgram;
        static uint rainOverlayShaderProgram;
        static uint rainGlassShaderProgram; 
        
        
        public uint ProgramId { get; private set; }

        static bool _initialized = false;

        public Shader()
        {
            Init();
        }

        public Shader(string vs, string fs)
        {
            ProgramId = Helpers.ShaderHelpers.LoadShaderFromString(vs, fs);
            Init();
        }

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            shaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/vertex_shader.glsl",
                "Artifacts/shaders/fragment_shader.glsl");

            lineShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/lineVertex_shader.glsl",
                "Artifacts/shaders/lineFragment_shader.glsl");

            hudShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/hudVertex_shader.glsl",
                "Artifacts/shaders/hudFragment_shader.glsl");

            skyShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/sky_vertex.glsl",
                "Artifacts/shaders/sky_fragment.glsl");

            invertPassShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/invertPass_vertex.glsl",
                "Artifacts/shaders/invertPass_fragment.glsl");
             
            blurPassShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/blur_vertex.glsl",
                "Artifacts/shaders/blur_fragment.glsl");
             
            outlineShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/outline_vertex.glsl",
                "Artifacts/shaders/outline_fragment.glsl");

            // AAA post-processing chain (shared fullscreen-quad vertex shader).
            postFxBrightShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/post_vertex.glsl",
                "Artifacts/shaders/postFxBright_fragment.glsl");
            postFxBlurShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/post_vertex.glsl",
                "Artifacts/shaders/postFxBlur_fragment.glsl");
            postFxCompositeShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/post_vertex.glsl",
                "Artifacts/shaders/postFxComposite_fragment.glsl");
             
            shadowShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/shadow_vertex.glsl",
                "Artifacts/shaders/shadow_fragment.glsl");

            shadowSkinnedShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/shadow_skinned_vertex.glsl",
                "Artifacts/shaders/shadow_fragment.glsl");

            shadowStaticAlphaShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/shadow_static_vertex.glsl",
                "Artifacts/shaders/shadow_static_alpha_fragment.glsl");

            // Reuse the standard vertex shader (same layout: pos/normal/color/uv) with a
            // custom fragment shader that blends 4 custom terrain layers by height + slope.
            editorTerrainShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/vertex_shader.glsl",
                "Artifacts/shaders/terrainEditor_fragment.glsl");

            // Editor primitives with PBR material — same vertex layout, UV-based PBR
            // fragment shader with optional albedo/normal/metallic/roughness/AO/height/
            // emission maps and per-map-type tuning.
            objectPbrShaderProgram = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/vertex_shader.glsl",
                "Artifacts/shaders/objectPbr_fragment.glsl");
        }

        public void Use()
        {
            GL.UseProgram(ProgramId);
        }

        public static uint GetShadowShaderProgram()
        {
            return shadowShaderProgram;
        }

        public static uint GetShadowSkinnedShaderProgram()
        {
            return shadowSkinnedShaderProgram;
        }

        public static uint GetShadowStaticAlphaShaderProgram()
        {
            return shadowStaticAlphaShaderProgram;
        }

        /// <summary>Shader program used to render editor terrain planes (custom 4-layer texturing).</summary>
        public static uint GetEditorTerrainShaderProgram()
        {
            return editorTerrainShaderProgram;
        }

        /// <summary>Shader program used to render editor primitives with a PBR material
        /// (optional albedo/normal/metallic/roughness/AO/height/emission maps + tuning).</summary>
        public static uint GetObjectPbrShaderProgram()
        {
            return objectPbrShaderProgram;
        }

        public static uint GetRainStreakShaderProgram()
        {
            return rainStreakShaderProgram;
        }

        public static uint GetRainOverlayShaderProgram()
        {
            return rainOverlayShaderProgram;
        }
        public static uint GetRainGlassShaderProgram()
        {
            return rainGlassShaderProgram;
        } 

        public static uint GetInvertPassShaderProgram()
        {
            return invertPassShaderProgram;
        }

        /// <summary>Shader used for the bloom bright-pass extraction.</summary>
        public static uint GetPostFxBrightShaderProgram()
        {
            return postFxBrightShaderProgram;
        }

        /// <summary>Shader used for the separable bloom blur passes.</summary>
        public static uint GetPostFxBlurShaderProgram()
        {
            return postFxBlurShaderProgram;
        }

        /// <summary>Shader used for the final composite (scene + bloom → ACES → gamma).</summary>
        public static uint GetPostFxCompositeShaderProgram()
        {
            return postFxCompositeShaderProgram;
        }

        public static uint GetBlurPassShaderProgram()
        {
            return blurPassShaderProgram;
        }

        public static uint GetOutlineShaderProgram()
        {
            return outlineShaderProgram;
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

        public void SetInt(string name, int value)
        {
            int loc = GL.GetUniformLocation(ProgramId, name);
            GL.Uniform1i(loc, value);
        }

        public void SetFloat(string name, float value)
        {
            int loc = GL.GetUniformLocation(ProgramId, name);
            GL.Uniform1f(loc, value);
        }

        public void SetVec2(string name, float x, float y)
        {
            int loc = GL.GetUniformLocation(ProgramId, name);
            GL.Uniform2f(loc, x, y);
        }

        public void Dispose()
        {
            if (ProgramId != 0)
            {
                uint p = ProgramId;
                GL.DeleteProgram(p);   // <-- INI YANG BENAR
                ProgramId = 0;
            }
        }
    }
}