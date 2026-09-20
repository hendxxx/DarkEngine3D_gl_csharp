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
        static uint dofSpriteMaskShaderProgram;
        static uint outlineShaderProgram;

        // ── AAA post-processing: reactive bloom (bright-pass + mip downsample/upsample
        // chain) and final composite (scene + bloom → ACES tonemap → gamma) ──
        static uint postFxBrightShaderProgram;
        static uint postFxBloomDownsampleShaderProgram;
        static uint postFxBloomUpsampleShaderProgram;
        static uint postFxCompositeShaderProgram;

        // Depth of field — slider-driven focus circle + variable disk blur.
        static uint postFxDofShaderProgram;
        
        static uint shadowShaderProgram;
        static uint shadowSkinnedShaderProgram;
        static uint shadowStaticAlphaShaderProgram;

        // Editor terrain (Plane → advanced terrain) — separate program so the game's
        // terrain fragment shader (with hardcoded height/slope bands) stays untouched.
        static uint editorTerrainShaderProgram;

        // Editor primitives with a PBR material (Box/Sphere/flat plane) — dedicated
        // program with 7 optional maps + tuning (reuses the shared vertex shader).
        static uint objectPbrShaderProgram;
        static uint objectPbrDisplaceShaderProgram;

#pragma warning disable CS0649
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

            // Core shaders — use SafeLoad so one failure doesn't crash the editor.
            shaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/vertex_shader.glsl",
                "Artifacts/shaders/fragment_shader.glsl");

            lineShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/lineVertex_shader.glsl",
                "Artifacts/shaders/lineFragment_shader.glsl");

            hudShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/hudVertex_shader.glsl",
                "Artifacts/shaders/hudFragment_shader.glsl");

            skyShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/sky_vertex.glsl",
                "Artifacts/shaders/sky_fragment.glsl");

            invertPassShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/invertPass_vertex.glsl",
                "Artifacts/shaders/invertPass_fragment.glsl");

            blurPassShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/blur_vertex.glsl",
                "Artifacts/shaders/blur_fragment.glsl");

            // DoF sprite-shape focus mask (sprite alpha → white silhouette).
            dofSpriteMaskShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/post_vertex.glsl",
                "Artifacts/shaders/dof_sprite_mask_fragment.glsl");

            outlineShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/outline_vertex.glsl",
                "Artifacts/shaders/outline_fragment.glsl");

            // AAA post-processing chain (shared fullscreen-quad vertex shader).
            postFxBrightShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/post_vertex.glsl",
                "Artifacts/shaders/postFxBright_fragment.glsl");
            postFxBloomDownsampleShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/post_vertex.glsl",
                "Artifacts/shaders/postFxBloomDownsample_fragment.glsl");
            postFxBloomUpsampleShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/post_vertex.glsl",
                "Artifacts/shaders/postFxBloomUpsample_fragment.glsl");
            postFxCompositeShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/post_vertex.glsl",
                "Artifacts/shaders/postFxComposite_fragment.glsl");
            postFxDofShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/post_vertex.glsl",
                "Artifacts/shaders/dof_fragment.glsl");

            shadowShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/shadow_vertex.glsl",
                "Artifacts/shaders/shadow_fragment.glsl");

            shadowSkinnedShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/shadow_skinned_vertex.glsl",
                "Artifacts/shaders/shadow_fragment.glsl");

            shadowStaticAlphaShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/shadow_static_vertex.glsl",
                "Artifacts/shaders/shadow_static_alpha_fragment.glsl");

            // Terrain editor shader
            editorTerrainShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/vertex_shader.glsl",
                "Artifacts/shaders/terrainEditor_fragment.glsl");

            // PBR object shader
            objectPbrShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/vertex_shader.glsl",
                "Artifacts/shaders/objectPbr_fragment.glsl");

            // PBR object shader with GEOMETRIC displacement (vertex texture fetch):
            // used for planes whose height map displaces the actual vertices
            // ("Vertex Displacement" toggle) — shares the objectPbr fragment stage.
            objectPbrDisplaceShaderProgram = Helpers.ShaderHelpers.SafeLoad(
                "Artifacts/shaders/pbrDisplace_vertex.glsl",
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

        /// <summary>PBR object shader whose vertex stage displaces vertices along their
        /// normals by the height map (true geometric displacement for dense planes).</summary>
        public static uint GetObjectPbrDisplaceShaderProgram()
        {
            return objectPbrDisplaceShaderProgram;
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

        /// <summary>Depth-of-field post-process program (focus circle + variable disk blur).</summary>
        /// <summary>DoF sprite-shape focus mask program (0 = failed to compile — caller skips the mask).</summary>
        public static uint GetDofSpriteMaskShaderProgram()
        {
            if (dofSpriteMaskShaderProgram == 0) Init();
            return dofSpriteMaskShaderProgram;
        }

        public static uint GetPostFxDofShaderProgram()
        {
            return postFxDofShaderProgram;
        }

        /// <summary>Shader used for the bloom bright-pass extraction.</summary>
        public static uint GetPostFxBrightShaderProgram()
        {
            return postFxBrightShaderProgram;
        }

        /// <summary>Shader used to downsample one bloom mip into the next (13-tap).</summary>
        public static uint GetPostFxBloomDownsampleShaderProgram()
        {
            return postFxBloomDownsampleShaderProgram;
        }

        /// <summary>Shader used to additively upsample a bloom mip into the level below.</summary>
        public static uint GetPostFxBloomUpsampleShaderProgram()
        {
            return postFxBloomUpsampleShaderProgram;
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