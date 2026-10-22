using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using StbImageSharp;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>
    /// Unified renderer for all 3 sky types: Skybox, Dome, Procedural Realtime.
    /// Manages VAO/VBO, shader compilation, and texture loading per type.
    /// </summary>
    public unsafe class SkyRenderer
    {
        // ── Shared geometry (unit cube for all sky types) ──
        private readonly uint _vao, _vbo;

        // ── 2D celestial geometry (screen-space quad for the sun/moon) ──
        private readonly uint _celestialVao, _celestialVbo;
        private uint _celestialShader;
        private int _celCenterLoc, _celHalfSizeLoc, _celColorLoc, _celDiscScaleLoc;
        private int _celGlowLoc, _celUseTextureLoc, _celTexLoc;

        /// <summary>2D-only sun track: NDC y of the sun at the horizon and at its peak.
        /// The moon uses a lower band so day and night read as separate arcs.</summary>
        private const float SunArcBottom = -0.05f;
        private const float SunArcTop = 0.74f;
        private const float MoonArcBottom = -0.85f;
        private const float MoonArcTop = -0.46f;

        // ── Shader programs ──
        private uint _skyboxShader;
        private uint _domeShader;
        private uint _realtimeShader;

        // ── Uniform locations: Skybox ──
        private int _sbViewLoc, _sbProjLoc;
        private int _sbRightLoc, _sbLeftLoc, _sbTopLoc, _sbBotLoc, _sbFrontLoc, _sbBackLoc;
        private int _sbHasRightLoc, _sbHasLeftLoc, _sbHasTopLoc, _sbHasBotLoc, _sbHasFrontLoc, _sbHasBackLoc;
        private int _sbFallbackColorLoc;

        // ── Uniform locations: Dome ──
        private int _domeViewLoc, _domeProjLoc;
        private int _domeTexLoc, _domeTintLoc, _domeHasTexLoc;
        private int _domeRadiusLoc, _domeRotYLoc, _domeRotXLoc, _domeTimeLoc;

        // ── Uniform locations: Realtime ──
        private int _rtViewLoc, _rtProjLoc, _rtFogColorLoc, _rtSunDirLoc, _rtTimeLoc, _rtWeatherLoc, _rtAspectLoc;
        private int _rtHideCelestialLoc;
        // Sun
        private int _rtSunSizeLoc, _rtSunSoftnessLoc, _rtSunColorLoc, _rtSunGlowLoc;
        // Scattering
        private int _rtScatteringIntLoc, _rtRayleighLoc, _rtRayColorLoc, _rtRayHeightLoc;
        private int _rtMieLoc, _rtMieColorLoc, _rtMieFocusLoc, _rtMieHeightLoc;
        // Clouds
        private int _rtCloudDensityLoc, _rtCloudAltLoc, _rtCloudSpeedLoc, _rtCloudDetailLoc;
        private int _rtCloudErosionLoc, _rtCloudShadowLoc, _rtCloudScatterLoc, _rtCloudTintLoc;
        private int _rtCirrusLoc, _rtCloudsEnabledLoc;
        // Moon
        private int _rtMoonTexLoc, _rtMoonBrightLoc, _rtMoonSizeLoc, _rtMoonGlowLoc;
        private int _rtMoonTintLoc, _rtMoonPhaseLoc, _rtMoonRotSpeedLoc;
        // Stars
        private int _rtStarBrightLoc, _rtStarDensLoc, _rtStarTwinkleLoc, _rtStarColorLoc, _rtStarsEnabledLoc;
        // Eclipses
        private int _rtSolarEclipseLoc, _rtLunarEclipseLoc, _rtEclipseGlowLoc;
        // SunRays
        private int _rtSunRayIntLoc, _rtSunRayCountLoc, _rtSunRayLenLoc, _rtSunRayColorLoc, _rtSunRaysEnabledLoc;

        // ── Texture caches ──
        private readonly Dictionary<string, uint> _textureCache = new();
#pragma warning disable CS0414
        private uint _moonTextureID = 0;

        // ── Time accumulator ──
        private float _totalTime = 0f;

        // ── Diagnostic flags ──
        private bool _domeShaderLogged = false;

        // ── Weather override (from editor sky object) ──
        public float? WeatherOverride = null;

        // ── Shared cube vertices ──
        private readonly float[] _cubeVertices = [
            -1.0f,  1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  1.0f, -1.0f, -1.0f,
             1.0f, -1.0f, -1.0f,  1.0f,  1.0f, -1.0f, -1.0f,  1.0f, -1.0f,
            -1.0f, -1.0f,  1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f, -1.0f, -1.0f,  1.0f,  1.0f, -1.0f, -1.0f,  1.0f,
             1.0f, -1.0f, -1.0f,  1.0f, -1.0f,  1.0f,  1.0f,  1.0f,  1.0f,
             1.0f,  1.0f,  1.0f,  1.0f,  1.0f, -1.0f,  1.0f, -1.0f, -1.0f,
            -1.0f, -1.0f,  1.0f, -1.0f,  1.0f,  1.0f,  1.0f,  1.0f,  1.0f,
             1.0f,  1.0f,  1.0f,  1.0f, -1.0f,  1.0f, -1.0f, -1.0f,  1.0f,
            -1.0f,  1.0f, -1.0f,  1.0f,  1.0f, -1.0f,  1.0f,  1.0f,  1.0f,
             1.0f,  1.0f,  1.0f, -1.0f,  1.0f,  1.0f, -1.0f,  1.0f, -1.0f,
            -1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  1.0f,  1.0f, -1.0f, -1.0f,
             1.0f, -1.0f, -1.0f, -1.0f, -1.0f,  1.0f,  1.0f, -1.0f,  1.0f
        ];

        public SkyRenderer()
        {
            // ── VAO/VBO ──
            fixed (uint* pVao = &_vao) GL.GenVertexArrays(1, pVao);
            fixed (uint* pVbo = &_vbo) GL.GenBuffers(1, pVbo);

            GL.BindVertexArray(_vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);
            fixed (float* p = _cubeVertices)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (uint)(_cubeVertices.Length * sizeof(float)), (void*)p, Const.GL_STATIC_DRAW);
            }
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);
            GL.BindVertexArray(0);

            // ── 2D celestial quad (clip-space, drawn straight into NDC) ──
            fixed (uint* pVao = &_celestialVao) GL.GenVertexArrays(1, pVao);
            fixed (uint* pVbo = &_celestialVbo) GL.GenBuffers(1, pVbo);
            GL.BindVertexArray(_celestialVao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _celestialVbo);
            fixed (float* p = _celestialQuad)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (uint)(_celestialQuad.Length * sizeof(float)), (void*)p, Const.GL_STATIC_DRAW);
            }
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 2 * sizeof(float), (void*)0);
            GL.BindVertexArray(0);

            // ── Compile shaders ──
            _skyboxShader = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/skybox_vertex.glsl",
                "Artifacts/shaders/skybox_fragment.glsl");
            _domeShader = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/dome_vertex.glsl",
                "Artifacts/shaders/dome_fragment.glsl");
            _realtimeShader = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/realtime_vertex.glsl",
                "Artifacts/shaders/realtime_fragment.glsl");
            _celestialShader = Helpers.ShaderHelpers.LoadShader(
                "Artifacts/shaders/sky2d_celestial_vertex.glsl",
                "Artifacts/shaders/sky2d_celestial_fragment.glsl");

            // ── Cache skybox uniform locations ──
            _sbViewLoc = GL.GetUniformLocation(_skyboxShader, "view");
            _sbProjLoc = GL.GetUniformLocation(_skyboxShader, "projection");
            _sbRightLoc = GL.GetUniformLocation(_skyboxShader, "skyboxRight");
            _sbLeftLoc = GL.GetUniformLocation(_skyboxShader, "skyboxLeft");
            _sbTopLoc = GL.GetUniformLocation(_skyboxShader, "skyboxTop");
            _sbBotLoc = GL.GetUniformLocation(_skyboxShader, "skyboxBottom");
            _sbFrontLoc = GL.GetUniformLocation(_skyboxShader, "skyboxFront");
            _sbBackLoc = GL.GetUniformLocation(_skyboxShader, "skyboxBack");
            _sbHasRightLoc = GL.GetUniformLocation(_skyboxShader, "hasFaceRight");
            _sbHasLeftLoc = GL.GetUniformLocation(_skyboxShader, "hasFaceLeft");
            _sbHasTopLoc = GL.GetUniformLocation(_skyboxShader, "hasFaceTop");
            _sbHasBotLoc = GL.GetUniformLocation(_skyboxShader, "hasFaceBottom");
            _sbHasFrontLoc = GL.GetUniformLocation(_skyboxShader, "hasFaceFront");
            _sbHasBackLoc = GL.GetUniformLocation(_skyboxShader, "hasFaceBack");
            _sbFallbackColorLoc = GL.GetUniformLocation(_skyboxShader, "fallbackColor");

            // ── Cache dome uniform locations ──
            _domeViewLoc = GL.GetUniformLocation(_domeShader, "view");
            _domeProjLoc = GL.GetUniformLocation(_domeShader, "projection");
            _domeTexLoc = GL.GetUniformLocation(_domeShader, "domeTexture");
            _domeTintLoc = GL.GetUniformLocation(_domeShader, "tintColor");
            _domeHasTexLoc = GL.GetUniformLocation(_domeShader, "hasTexture");
            _domeRadiusLoc = GL.GetUniformLocation(_domeShader, "domeRadius");
            _domeRotYLoc = GL.GetUniformLocation(_domeShader, "domeRotationY");
            _domeRotXLoc = GL.GetUniformLocation(_domeShader, "domeRotationX");
            _domeTimeLoc = GL.GetUniformLocation(_domeShader, "time");

            // ── Cache realtime uniform locations ──
            _rtViewLoc = GL.GetUniformLocation(_realtimeShader, "view");
            _rtProjLoc = GL.GetUniformLocation(_realtimeShader, "projection");
            _rtFogColorLoc = GL.GetUniformLocation(_realtimeShader, "fogColor");
            _rtSunDirLoc = GL.GetUniformLocation(_realtimeShader, "sunDir");
            _rtTimeLoc = GL.GetUniformLocation(_realtimeShader, "time");
            _rtWeatherLoc = GL.GetUniformLocation(_realtimeShader, "weatherMode");
            _rtAspectLoc = GL.GetUniformLocation(_realtimeShader, "u_aspectRatio");
            _rtHideCelestialLoc = GL.GetUniformLocation(_realtimeShader, "hideCelestial");

            // Sun
            _rtSunSizeLoc = GL.GetUniformLocation(_realtimeShader, "sunSize");
            _rtSunSoftnessLoc = GL.GetUniformLocation(_realtimeShader, "sunSoftness");
            _rtSunColorLoc = GL.GetUniformLocation(_realtimeShader, "sunColor");
            _rtSunGlowLoc = GL.GetUniformLocation(_realtimeShader, "sunGlowIntensity");

            // Scattering
            _rtScatteringIntLoc = GL.GetUniformLocation(_realtimeShader, "scatteringIntensity");
            _rtRayleighLoc = GL.GetUniformLocation(_realtimeShader, "rayleighStrength");
            _rtRayColorLoc = GL.GetUniformLocation(_realtimeShader, "rayleighColor");
            _rtRayHeightLoc = GL.GetUniformLocation(_realtimeShader, "rayleighHeight");
            _rtMieLoc = GL.GetUniformLocation(_realtimeShader, "mieStrength");
            _rtMieColorLoc = GL.GetUniformLocation(_realtimeShader, "mieColor");
            _rtMieFocusLoc = GL.GetUniformLocation(_realtimeShader, "mieFocus");
            _rtMieHeightLoc = GL.GetUniformLocation(_realtimeShader, "mieHeight");

            // Clouds
            _rtCloudDensityLoc = GL.GetUniformLocation(_realtimeShader, "cloudDensity");
            _rtCloudAltLoc = GL.GetUniformLocation(_realtimeShader, "cloudAltitude");
            _rtCloudSpeedLoc = GL.GetUniformLocation(_realtimeShader, "cloudSpeed");
            _rtCloudDetailLoc = GL.GetUniformLocation(_realtimeShader, "cloudDetail");
            _rtCloudErosionLoc = GL.GetUniformLocation(_realtimeShader, "cloudErosion");
            _rtCloudShadowLoc = GL.GetUniformLocation(_realtimeShader, "cloudShadowStrength");
            _rtCloudScatterLoc = GL.GetUniformLocation(_realtimeShader, "cloudScatter");
            _rtCloudTintLoc = GL.GetUniformLocation(_realtimeShader, "cloudTintColor");
            _rtCirrusLoc = GL.GetUniformLocation(_realtimeShader, "cirrusStrength");
            _rtCloudsEnabledLoc = GL.GetUniformLocation(_realtimeShader, "cloudsEnabled");

            // Moon
            _rtMoonTexLoc = GL.GetUniformLocation(_realtimeShader, "moonTex");
            _rtMoonBrightLoc = GL.GetUniformLocation(_realtimeShader, "moonBrightness");
            _rtMoonSizeLoc = GL.GetUniformLocation(_realtimeShader, "moonSize");
            _rtMoonGlowLoc = GL.GetUniformLocation(_realtimeShader, "moonGlowRadius");
            _rtMoonTintLoc = GL.GetUniformLocation(_realtimeShader, "moonTintColor");
            _rtMoonPhaseLoc = GL.GetUniformLocation(_realtimeShader, "moonPhaseOffset");
            _rtMoonRotSpeedLoc = GL.GetUniformLocation(_realtimeShader, "moonRotationSpeed");

            // Stars
            _rtStarBrightLoc = GL.GetUniformLocation(_realtimeShader, "starBrightness");
            _rtStarDensLoc = GL.GetUniformLocation(_realtimeShader, "starDensity");
            _rtStarTwinkleLoc = GL.GetUniformLocation(_realtimeShader, "starTwinkleSpeed");
            _rtStarColorLoc = GL.GetUniformLocation(_realtimeShader, "starColor");
            _rtStarsEnabledLoc = GL.GetUniformLocation(_realtimeShader, "starsEnabled");

            // Eclipses
            _rtSolarEclipseLoc = GL.GetUniformLocation(_realtimeShader, "solarEclipse");
            _rtLunarEclipseLoc = GL.GetUniformLocation(_realtimeShader, "lunarEclipse");
            _rtEclipseGlowLoc = GL.GetUniformLocation(_realtimeShader, "eclipseGlowColor");

            // SunRays
            _rtSunRayIntLoc = GL.GetUniformLocation(_realtimeShader, "sunRayIntensity");
            _rtSunRayCountLoc = GL.GetUniformLocation(_realtimeShader, "sunRayCount");
            _rtSunRayLenLoc = GL.GetUniformLocation(_realtimeShader, "sunRayLength");
            _rtSunRayColorLoc = GL.GetUniformLocation(_realtimeShader, "sunRayColor");
            _rtSunRaysEnabledLoc = GL.GetUniformLocation(_realtimeShader, "sunRaysEnabled");

            // ── Cache 2D celestial uniform locations ──
            _celCenterLoc = GL.GetUniformLocation(_celestialShader, "uCenter");
            _celHalfSizeLoc = GL.GetUniformLocation(_celestialShader, "uHalfSize");
            _celColorLoc = GL.GetUniformLocation(_celestialShader, "uColor");
            _celDiscScaleLoc = GL.GetUniformLocation(_celestialShader, "uDiscScale");
            _celGlowLoc = GL.GetUniformLocation(_celestialShader, "uGlow");
            _celUseTextureLoc = GL.GetUniformLocation(_celestialShader, "uUseTexture");
            _celTexLoc = GL.GetUniformLocation(_celestialShader, "uTex");
        }

        // Unit quad (triangle strip) for the 2D sun/moon billboard.
        private readonly float[] _celestialQuad = [
            -1f, -1f,  1f, -1f,  -1f,  1f,  1f,  1f
        ];

        /// <summary>
        /// Load a texture from file, with caching to avoid reloading.
        /// </summary>
        private uint LoadTextureCached(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            if (_textureCache.TryGetValue(path, out uint cached) && cached != 0)
                return cached;

            try
            {
                var tex = new Texture(path);
                // Override wrap mode to CLAMP_TO_EDGE (Texture class defaults to REPEAT)
                // and use LINEAR filtering — critical for seamless cubemap faces
                GL.BindTexture(Const.GL_TEXTURE_2D, tex.ID);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
                GL.GenerateMipmap(Const.GL_TEXTURE_2D);
                _textureCache[path] = tex.ID;
                return tex.ID;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SkyRenderer] Failed to load texture '{path}': {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Load a cubemap face image and paint its 1-pixel border with the color
        /// of the adjacent interior pixel. This eliminates seam lines when the GPU
        /// clamps to the edge texel (CLAMP_TO_EDGE), because the border now matches
        /// the face interior.
        /// </summary>
        private unsafe uint LoadSkyboxFace(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            if (_textureCache.TryGetValue(path, out uint cached) && cached != 0)
                return cached;

            try
            {
                path = PathHelpers.Resolve(path);
                using var stream = File.OpenRead(path);
                var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

                int w = image.Width;
                int h = image.Height;
                byte[] data = image.Data;

                // Paint 1-pixel border with the nearest interior pixel color
                // This ensures CLAMP_TO_EDGE picks up a color matching the face interior
                for (int x = 0; x < w; x++)
                {
                    // Top row (y=0) ← copy from y=1
                    int topIdx = (0 * w + x) * 4;
                    int srcIdx = (1 * w + x) * 4;
                    data[topIdx + 0] = data[srcIdx + 0];
                    data[topIdx + 1] = data[srcIdx + 1];
                    data[topIdx + 2] = data[srcIdx + 2];
                    data[topIdx + 3] = data[srcIdx + 3];

                    // Bottom row (y=h-1) ← copy from y=h-2
                    int botIdx = ((h - 1) * w + x) * 4;
                    int srcBotIdx = ((h - 2) * w + x) * 4;
                    data[botIdx + 0] = data[srcBotIdx + 0];
                    data[botIdx + 1] = data[srcBotIdx + 1];
                    data[botIdx + 2] = data[srcBotIdx + 2];
                    data[botIdx + 3] = data[srcBotIdx + 3];
                }
                for (int y = 0; y < h; y++)
                {
                    // Left column (x=0) ← copy from x=1
                    int leftIdx = (y * w + 0) * 4;
                    int srcLeftIdx = (y * w + 1) * 4;
                    data[leftIdx + 0] = data[srcLeftIdx + 0];
                    data[leftIdx + 1] = data[srcLeftIdx + 1];
                    data[leftIdx + 2] = data[srcLeftIdx + 2];
                    data[leftIdx + 3] = data[srcLeftIdx + 3];

                    // Right column (x=w-1) ← copy from x=w-2
                    int rightIdx = (y * w + (w - 1)) * 4;
                    int srcRightIdx = (y * w + (w - 2)) * 4;
                    data[rightIdx + 0] = data[srcRightIdx + 0];
                    data[rightIdx + 1] = data[srcRightIdx + 1];
                    data[rightIdx + 2] = data[srcRightIdx + 2];
                    data[rightIdx + 3] = data[srcRightIdx + 3];
                }

                // Upload to GL
                uint textureID;
                GL.GenTextures(1, &textureID);
                GL.BindTexture(Const.GL_TEXTURE_2D, textureID);

                fixed (byte* ptr = data)
                {
                    GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                                  w, h, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, ptr);
                }

                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
                GL.GenerateMipmap(Const.GL_TEXTURE_2D);

                _textureCache[path] = textureID;
                return textureID;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SkyRenderer] Failed to load skybox face '{path}': {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Load a panoramic/dome texture with CLAMP_TO_EDGE and blended borders.
        /// For panoramic textures, the left and right edges wrap around horizontally,
        /// so we blend them together. Top/bottom borders use adjacent interior pixels.
        /// </summary>
        private unsafe uint LoadDomeTexture(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            if (_textureCache.TryGetValue(path, out uint cached) && cached != 0)
                return cached;

            try
            {
                path = PathHelpers.Resolve(path);
                using var stream = File.OpenRead(path);
                var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

                int w = image.Width;
                int h = image.Height;
                byte[] data = image.Data;

                // Paint borders to prevent seam lines:
                // 1. Top/bottom: copy from adjacent interior row
                // 2. Left/right: blend with opposite edge (panoramic wrapping)
                for (int x = 0; x < w; x++)
                {
                    // Top row ← y=1
                    int topIdx = (0 * w + x) * 4;
                    int srcIdx = (1 * w + x) * 4;
                    data[topIdx + 0] = data[srcIdx + 0];
                    data[topIdx + 1] = data[srcIdx + 1];
                    data[topIdx + 2] = data[srcIdx + 2];
                    data[topIdx + 3] = data[srcIdx + 3];

                    // Bottom row ← y=h-2
                    int botIdx = ((h - 1) * w + x) * 4;
                    int srcBotIdx = ((h - 2) * w + x) * 4;
                    data[botIdx + 0] = data[srcBotIdx + 0];
                    data[botIdx + 1] = data[srcBotIdx + 1];
                    data[botIdx + 2] = data[srcBotIdx + 2];
                    data[botIdx + 3] = data[srcBotIdx + 3];
                }
                // Left/right columns: blend with opposite edge (horizontal wrap)
                for (int y = 0; y < h; y++)
                {
                    int leftIdx = (y * w + 0) * 4;
                    int rightIdx = (y * w + (w - 1)) * 4;
                    int srcLeftIdx = (y * w + 1) * 4;
                    int srcRightIdx = (y * w + (w - 2)) * 4;

                    // Left ← average of right edge and interior-left
                    data[leftIdx + 0] = (byte)((data[rightIdx + 0] + data[srcLeftIdx + 0]) / 2);
                    data[leftIdx + 1] = (byte)((data[rightIdx + 1] + data[srcLeftIdx + 1]) / 2);
                    data[leftIdx + 2] = (byte)((data[rightIdx + 2] + data[srcLeftIdx + 2]) / 2);
                    data[leftIdx + 3] = (byte)((data[rightIdx + 3] + data[srcLeftIdx + 3]) / 2);

                    // Right ← average of left edge and interior-right
                    data[rightIdx + 0] = (byte)((data[leftIdx + 0] + data[srcRightIdx + 0]) / 2);
                    data[rightIdx + 1] = (byte)((data[leftIdx + 1] + data[srcRightIdx + 1]) / 2);
                    data[rightIdx + 2] = (byte)((data[leftIdx + 2] + data[srcRightIdx + 2]) / 2);
                    data[rightIdx + 3] = (byte)((data[leftIdx + 3] + data[srcRightIdx + 3]) / 2);
                }

                // Upload to GL
                uint textureID;
                GL.GenTextures(1, &textureID);
                GL.BindTexture(Const.GL_TEXTURE_2D, textureID);

                fixed (byte* ptr = data)
                {
                    GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                                  w, h, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, ptr);
                }

                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
                GL.GenerateMipmap(Const.GL_TEXTURE_2D);

                _textureCache[path] = textureID;
                return textureID;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SkyRenderer] Failed to load dome texture '{path}': {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Bind a face texture to a texture unit, or unbind if path is empty.
        /// </summary>
        private void BindFaceTexture(string path, int unit, int texLoc, int hasLoc, uint shader)
        {
            uint texID = LoadSkyboxFace(path);
            GL.ActiveTexture(Const.GL_TEXTURE0 + (uint)unit);
            if (texID != 0)
            {
                GL.BindTexture(Const.GL_TEXTURE_2D, texID);
                GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, Const.GL_CLAMP_TO_EDGE);
                GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
                GL.Uniform1i(texLoc, unit);
                GL.Uniform1f(hasLoc, 1.0f);
            }
            else
            {
                GL.BindTexture(Const.GL_TEXTURE_2D, 0);
                GL.Uniform1f(hasLoc, 0.0f);
            }
        }

        /// <summary>
        /// Projection used to draw the sky only.
        ///
        /// The sky is a closed unit cube centred on the camera (the view matrix has its
        /// translation stripped). A perspective camera sits inside that cube and every
        /// view ray hits a face, so the sky fills the frame. Under an orthographic
        /// projection the same cube is just 2×2 world units on screen — it shrinks to a
        /// small box in the middle of the viewport, because ortho has no FOV for it to
        /// fill. That is the bug this method fixes.
        ///
        /// In ortho we therefore draw the sky through a virtual perspective projection
        /// built from the camera FOV/aspect, so the cube surrounds the eye again and the
        /// sky fills the frame exactly like it does in perspective. Sky vertices always
        /// write depth = w (gl_Position.xyww), so the sky still resolves to the far plane
        /// and this projection cannot disturb the scene's depth buffer.
        /// </summary>
        private static Matrix4x4 GetSkyProjection(Camera camera)
        {
            if (!camera.IsOrthographic) return camera.GetProjectionMatrix();

            float fov = camera.FoV > 1f ? camera.FoV : (camera.BaseFoV > 1f ? camera.BaseFoV : 60f);
            float aspect = camera.GetAspect();
            if (!(aspect > 0.001f)) aspect = 1f;
            float near = camera.NearDist > 0.001f ? camera.NearDist : 0.1f;
            float far = camera.FarDist > near ? camera.FarDist : near + 1000f;

            return Matrix4x4.CreatePerspectiveFieldOfView(
                Helpers.OGLMath.ToRadians(fov), aspect, near, far);
        }

        /// <summary>
        /// Draw the sky. Delegates to the appropriate renderer based on SkySettings.Type.
        /// </summary>
        public void Draw(Camera camera, Lights lights, float deltaTime, SkySettings skySettings,
            Texture[]? legacyMoonTextures)
        {
            _totalTime += deltaTime;

            GL.UseProgram(0); // Reset

            switch (skySettings.Type)
            {
                case SkyType.Skybox:
                    DrawSkybox(camera, lights, skySettings, legacyMoonTextures);
                    break;
                case SkyType.Dome:
                    DrawDome(camera, lights, skySettings);
                    break;
                case SkyType.Procedural:
                default:
                    DrawProcedural(camera, lights, deltaTime, skySettings, legacyMoonTextures);
                    break;
            }

            // ── 2D LEVELS ONLY: the ortho camera has a narrow sky window, so the sky's
            //    sun/moon often sit outside it (or behind the level). Draw them as
            //    dedicated 2D celestial discs that always travel the authored arc
            //    (yellow sun over the sky, grey moon under it). 3D is untouched. ──
            if (camera.IsOrthographic)
                DrawCelestial2D(camera, lights, skySettings, legacyMoonTextures);
        }

        /// <summary>
        /// Draw the sun and moon for 2D (orthographic) views only.
        ///
        /// Position comes from the SAME sun direction the 3D sky and the 2D sprite
        /// lighting use, so the disc matches the scene's time of day / sun override:
        ///   x = −dir.x   (0.5 / 1.0 in the sun direction ⇒ left → right across the day)
        ///   y = arc band (sun above the level, moon below it)
        /// The sun shows while the sun is above the horizon, the moon while it is below.
        /// Drawn as screen-space billboards with blending, depth test off (the sky is
        /// already the backmost thing in the frame).
        /// </summary>
        private void DrawCelestial2D(Camera camera, Lights lights, SkySettings settings,
            Texture[]? legacyMoonTextures)
        {
            if (_celestialShader == 0) return;

            // RealSunDir = where the sun really is. Lights.SunDir is the ACTIVE shading
            // light, which flips to the moon at night — using it would put the "sun"
            // above the horizon all night and never draw the moon.
            var sun = lights.RealSunDir;
            var xy = new Vector2(sun.X, sun.Y);
            float len = xy.Length();
            if (len < 1e-4f) return;
            xy /= len;

            float aspect = camera.GetAspect();
            if (!(aspect > 0.001f)) aspect = 1f;

            bool day = xy.Y >= 0f;
            float elev = MathF.Abs(xy.Y);                     // 0 at the horizon, 1 at the peak
            // x = −direction of the body: sun dir = xy, moon dir = −xy.
            float xNdc = day ? -xy.X : xy.X;
            float yNdc = day
                ? SunArcBottom + (SunArcTop - SunArcBottom) * elev
                : MoonArcBottom + (MoonArcTop - MoonArcBottom) * elev;

            uint moonTex = 0;
            if (!day)
            {
                if (!string.IsNullOrEmpty(settings.Moon.TexturePath))
                    moonTex = LoadTextureCached(settings.Moon.TexturePath);
                if (moonTex == 0 && legacyMoonTextures is { Length: > 0 })
                    moonTex = legacyMoonTextures[0].ID;
            }

            GL.UseProgram(_celestialShader);
            GL.BindVertexArray(_celestialVao);
            GL.Disable(Const.GL_DEPTH_TEST);
            GL.DepthMask(false);
            GL.Enable(Const.GL_BLEND);
            GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);
            if (_celTexLoc >= 0) GL.Uniform1i(_celTexLoc, 0);

            if (day)
            {
                var sunColor = settings.Sun.Color;
                float radius = 0.05f * Math.Clamp(settings.Sun.Size, 0.4f, 2.5f);
                const float glowScale = 2.4f;
                DrawCelestialQuad(xNdc, yNdc, radius * glowScale, aspect,
                    sunColor.X, sunColor.Y, sunColor.Z, 1f,
                    discScale: 1f / glowScale, glow: 1f, useTexture: false, texture: 0);
            }
            else
            {
                var moonTint = settings.Moon.TintColor;
                float radius = 0.045f * Math.Clamp(settings.Moon.Size, 0.4f, 2.5f);
                DrawCelestialQuad(xNdc, yNdc, radius, aspect,
                    moonTint.X, moonTint.Y, moonTint.Z, 1f,
                    discScale: 1f, glow: 0f, useTexture: moonTex != 0, texture: moonTex);
            }

            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.Disable(Const.GL_BLEND);
            GL.DepthMask(true);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        private void DrawCelestialQuad(float xNdc, float yNdc, float radiusNdc, float aspect,
            float r, float g, float b, float a,
            float discScale, float glow, bool useTexture, uint texture)
        {
            GL.Uniform2f(_celCenterLoc, xNdc, yNdc);
            GL.Uniform2f(_celHalfSizeLoc, radiusNdc / aspect, radiusNdc);
            GL.Uniform4f(_celColorLoc, r, g, b, a);
            GL.Uniform1f(_celDiscScaleLoc, discScale);
            GL.Uniform1f(_celGlowLoc, glow);
            GL.Uniform1f(_celUseTextureLoc, useTexture ? 1f : 0f);
            if (useTexture)
            {
                GL.ActiveTexture(Const.GL_TEXTURE0);
                GL.BindTexture(Const.GL_TEXTURE_2D, texture);
            }
            GL.DrawArrays(Const.GL_TRIANGLE_STRIP, 0, 4);
        }

        /// <summary>
        /// Draw a cubemap skybox with 6 face textures.
        /// </summary>
        private void DrawSkybox(Camera camera, Lights lights, SkySettings settings, Texture[]? legacyMoonTextures)
        {
            GL.UseProgram(_skyboxShader);

            Matrix4x4 view = camera.GetViewMatrix();
            view.M41 = view.M42 = view.M43 = 0;
            view.M14 = view.M24 = view.M34 = 0; view.M44 = 1;
            Matrix4x4 projection = GetSkyProjection(camera);

            GL.UniformMatrix4fv(_sbViewLoc, 1, false, (float*)&view);
            GL.UniformMatrix4fv(_sbProjLoc, 1, false, (float*)&projection);
            GL.Uniform3f(_sbFallbackColorLoc, lights.FogColor.X, lights.FogColor.Y, lights.FogColor.Z);

            var faces = settings.SkyboxFaces;
            // Remap face textures to match the expected orientation:
            // Right(+X)←Front, Left(-X)←Back, Front(+Z)←Right, Back(-Z)←Left
            BindFaceTexture(faces.Front, 0, _sbRightLoc, _sbHasRightLoc, _skyboxShader);
            BindFaceTexture(faces.Back, 1, _sbLeftLoc, _sbHasLeftLoc, _skyboxShader);
            BindFaceTexture(faces.Top, 2, _sbTopLoc, _sbHasTopLoc, _skyboxShader);
            BindFaceTexture(faces.Bottom, 3, _sbBotLoc, _sbHasBotLoc, _skyboxShader);
            BindFaceTexture(faces.Right, 4, _sbFrontLoc, _sbHasFrontLoc, _skyboxShader);
            BindFaceTexture(faces.Left, 5, _sbBackLoc, _sbHasBackLoc, _skyboxShader);

            GL.Disable(Const.GL_CULL_FACE);
            GL.DepthMask(false);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthFunc(Const.GL_LEQUAL);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 36);

            GL.DepthMask(true);
            GL.DepthFunc(Const.GL_LESS);
            GL.Enable(Const.GL_CULL_FACE);
        }

        /// <summary>
        /// Draw a dome sphere with a panoramic equirectangular texture.
        /// </summary>
        private void DrawDome(Camera camera, Lights lights, SkySettings settings)
        {
            if (_domeShader == 0)
            {
                if (!_domeShaderLogged)
                {
                    Console.WriteLine("[SkyRenderer] DrawDome skipped: dome shader is invalid (compilation/link failed). Check console for [SHADER COMPILE ERROR] or [PROGRAM LINK ERROR].");
                    _domeShaderLogged = true;
                }
                return;
            }

            GL.UseProgram(_domeShader);

            Matrix4x4 view = camera.GetViewMatrix();
            view.M41 = view.M42 = view.M43 = 0;
            view.M14 = view.M24 = view.M34 = 0; view.M44 = 1;
            Matrix4x4 projection = GetSkyProjection(camera);

            GL.UniformMatrix4fv(_domeViewLoc, 1, false, (float*)&view);
            GL.UniformMatrix4fv(_domeProjLoc, 1, false, (float*)&projection);
            GL.Uniform3f(_domeTintLoc, settings.Dome.TintColor.X, settings.Dome.TintColor.Y, settings.Dome.TintColor.Z);
            GL.Uniform1f(_domeRadiusLoc, settings.Dome.Radius);

            // Auto-rotate
            float rotY = settings.Dome.RotationY;
            float rotX = 0f;
            if (settings.Dome.AutoRotate)
            {
                float speed = settings.Dome.RotateSpeed;
                // Apply speed variation: blend between constant speed and a sinusoidal
                // oscillation around the base speed, creating a natural breathing effect.
                float variation = Math.Clamp(settings.Dome.RotateVariation, 0f, 1f);
                float effectiveSpeed = speed * (1f + MathF.Sin(_totalTime * 0.37f) * variation * 0.5f
                                               + MathF.Sin(_totalTime * 0.73f) * variation * 0.3f);

                if (settings.Dome.PingPong)
                {
                    float amp = settings.Dome.PingPongAmplitude * (MathF.PI / 180f);
                    float pingPong = MathF.Sin(_totalTime * effectiveSpeed * 0.02f) * amp;
                    if (settings.Dome.RotateAxis == 0 || settings.Dome.RotateAxis == 2)
                        rotY += pingPong;
                    if (settings.Dome.RotateAxis == 1 || settings.Dome.RotateAxis == 2)
                        rotX += pingPong * 0.5f;
                }
                else
                {
                    float angle = _totalTime * effectiveSpeed * (MathF.PI / 180f);
                    if (settings.Dome.RotateAxis == 0 || settings.Dome.RotateAxis == 2)
                        rotY += angle;
                    if (settings.Dome.RotateAxis == 1 || settings.Dome.RotateAxis == 2)
                        rotX += angle * 0.3f;
                }
            }

            GL.Uniform1f(_domeRotYLoc, rotY);
            GL.Uniform1f(_domeRotXLoc, rotX);
            GL.Uniform1f(_domeTimeLoc, _totalTime);

            uint texID = LoadDomeTexture(settings.Dome.TexturePath);
            GL.ActiveTexture(Const.GL_TEXTURE0);
            if (texID != 0)
            {
                GL.BindTexture(Const.GL_TEXTURE_2D, texID);
                GL.Uniform1i(_domeTexLoc, 0);
                GL.Uniform1f(_domeHasTexLoc, 1.0f);
            }
            else
            {
                GL.BindTexture(Const.GL_TEXTURE_2D, 0);
                GL.Uniform1f(_domeHasTexLoc, 0.0f);
            }

            // Disable face culling — dome is a cube viewed from inside, all faces
            // are visible. Letting GL_BACK culling on would hide faces in solid mode.
            GL.Disable(Const.GL_CULL_FACE);

            GL.DepthMask(false);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthFunc(Const.GL_LEQUAL);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 36);

            GL.DepthMask(true);
            GL.DepthFunc(Const.GL_LESS);
            GL.Enable(Const.GL_CULL_FACE);
        }

        /// <summary>
        /// Draw the procedural realtime sky with all features.
        /// </summary>
        private void DrawProcedural(Camera camera, Lights lights, float deltaTime,
            SkySettings settings, Texture[]? legacyMoonTextures)
        {
            GL.UseProgram(_realtimeShader);

            Matrix4x4 view = camera.GetViewMatrix();
            view.M41 = view.M42 = view.M43 = 0;
            view.M14 = view.M24 = view.M34 = 0; view.M44 = 1;
            Matrix4x4 projection = GetSkyProjection(camera);

            GL.UniformMatrix4fv(_rtViewLoc, 1, false, (float*)&view);
            GL.UniformMatrix4fv(_rtProjLoc, 1, false, (float*)&projection);

            // Camera / Time / Weather
            float currentWeather = WeatherOverride ?? Keyboard.GetCurrentWeather();
            GL.Uniform3f(_rtFogColorLoc, lights.FogColor.X, lights.FogColor.Y, lights.FogColor.Z);
            GL.Uniform3f(_rtSunDirLoc, lights.RealSunDir.X, lights.RealSunDir.Y, lights.RealSunDir.Z);
            GL.Uniform3f(_rtTimeLoc, _totalTime, 0, 0);
            GL.Uniform1f(_rtWeatherLoc, currentWeather);
            GL.Uniform1f(_rtAspectLoc, camera.GetAspect());
            // 2D: the arc sun/moon discs are drawn separately (DrawCelestial2D).
            if (_rtHideCelestialLoc >= 0)
                GL.Uniform1f(_rtHideCelestialLoc, camera.IsOrthographic ? 1f : 0f);

            // Sun
            var sun = settings.Sun;
            GL.Uniform1f(_rtSunSizeLoc, sun.Size);
            GL.Uniform1f(_rtSunSoftnessLoc, sun.Softness);
            GL.Uniform3f(_rtSunColorLoc, sun.Color.X, sun.Color.Y, sun.Color.Z);
            GL.Uniform1f(_rtSunGlowLoc, sun.GlowIntensity);

            // Atmospheric Scattering
            var atmo = settings.Scattering;
            GL.Uniform1f(_rtScatteringIntLoc, atmo.Intensity);
            GL.Uniform1f(_rtRayleighLoc, atmo.Rayleigh);
            GL.Uniform3f(_rtRayColorLoc, atmo.RayColor.X, atmo.RayColor.Y, atmo.RayColor.Z);
            GL.Uniform1f(_rtRayHeightLoc, atmo.RayHeight);
            GL.Uniform1f(_rtMieLoc, atmo.Mie);
            GL.Uniform3f(_rtMieColorLoc, atmo.MieColor.X, atmo.MieColor.Y, atmo.MieColor.Z);
            GL.Uniform1f(_rtMieFocusLoc, atmo.MieFocus);
            GL.Uniform1f(_rtMieHeightLoc, atmo.MieHeight);

            // Clouds
            var clouds = settings.Clouds;
            GL.Uniform1f(_rtCloudDensityLoc, clouds.Density);
            GL.Uniform1f(_rtCloudAltLoc, clouds.Altitude);
            GL.Uniform1f(_rtCloudSpeedLoc, clouds.Speed);
            GL.Uniform1f(_rtCloudDetailLoc, clouds.Detail);
            GL.Uniform1f(_rtCloudErosionLoc, clouds.Erosion);
            GL.Uniform1f(_rtCloudShadowLoc, clouds.ShadowStrength);
            GL.Uniform1f(_rtCloudScatterLoc, clouds.Scatter);
            GL.Uniform3f(_rtCloudTintLoc, clouds.TintColor.X, clouds.TintColor.Y, clouds.TintColor.Z);
            GL.Uniform1f(_rtCirrusLoc, clouds.CirrusStrength);
            GL.Uniform1f(_rtCloudsEnabledLoc, clouds.Enabled ? 1.0f : 0.0f);

            // Moon texture
            var moon = settings.Moon;
            uint moonTexID = 0;
            if (!string.IsNullOrEmpty(moon.TexturePath))
            {
                moonTexID = LoadTextureCached(moon.TexturePath);
            }
            // Fallback to legacy moon texture
            if (moonTexID == 0 && legacyMoonTextures != null && legacyMoonTextures.Length > 0)
            {
                moonTexID = legacyMoonTextures[0].ID;
            }

            GL.ActiveTexture(Const.GL_TEXTURE4);
            if (moonTexID != 0)
            {
                GL.BindTexture(Const.GL_TEXTURE_2D, moonTexID);
                GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, Const.GL_CLAMP_TO_EDGE);
                GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, Const.GL_CLAMP_TO_EDGE);
            }
            else
            {
                GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            }
            GL.Uniform1i(_rtMoonTexLoc, 4);

            GL.Uniform1f(_rtMoonBrightLoc, moon.Brightness);
            GL.Uniform1f(_rtMoonSizeLoc, moon.Size);
            GL.Uniform1f(_rtMoonGlowLoc, moon.GlowRadius);
            GL.Uniform3f(_rtMoonTintLoc, moon.TintColor.X, moon.TintColor.Y, moon.TintColor.Z);
            GL.Uniform1f(_rtMoonPhaseLoc, moon.PhaseOffset);
            GL.Uniform1f(_rtMoonRotSpeedLoc, moon.RotationSpeed);

            // Stars
            var stars = settings.Stars;
            GL.Uniform1f(_rtStarBrightLoc, stars.Brightness);
            GL.Uniform1f(_rtStarDensLoc, stars.Density);
            GL.Uniform1f(_rtStarTwinkleLoc, stars.TwinkleSpeed);
            GL.Uniform3f(_rtStarColorLoc, stars.Color.X, stars.Color.Y, stars.Color.Z);
            GL.Uniform1f(_rtStarsEnabledLoc, stars.Enabled ? 1.0f : 0.0f);

            // Eclipses
            var eclipses = settings.Eclipses;
            GL.Uniform1f(_rtSolarEclipseLoc, eclipses.SolarEclipse);
            GL.Uniform1f(_rtLunarEclipseLoc, eclipses.LunarEclipse);
            GL.Uniform3f(_rtEclipseGlowLoc, eclipses.GlowColor.X, eclipses.GlowColor.Y, eclipses.GlowColor.Z);

            // Sun Rays
            var sunRays = settings.SunRays;
            GL.Uniform1f(_rtSunRayIntLoc, sunRays.Intensity);
            GL.Uniform1f(_rtSunRayCountLoc, sunRays.RayCount);
            GL.Uniform1f(_rtSunRayLenLoc, sunRays.Length);
            GL.Uniform3f(_rtSunRayColorLoc, sunRays.Color.X, sunRays.Color.Y, sunRays.Color.Z);
            GL.Uniform1f(_rtSunRaysEnabledLoc, sunRays.Enabled ? 1.0f : 0.0f);

            // Draw
            GL.Disable(Const.GL_CULL_FACE);
            GL.DepthMask(false);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthFunc(Const.GL_LEQUAL);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 36);

            GL.DepthMask(true);
            GL.DepthFunc(Const.GL_LESS);
            GL.Enable(Const.GL_CULL_FACE);
        }
    }
}
