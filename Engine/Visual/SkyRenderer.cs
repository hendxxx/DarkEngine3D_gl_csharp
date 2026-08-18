using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using System.Numerics;
using System.Runtime.CompilerServices;

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
        private int _domeRadiusLoc, _domeRotYLoc;

        // ── Uniform locations: Realtime ──
        private int _rtViewLoc, _rtProjLoc, _rtFogColorLoc, _rtSunDirLoc, _rtTimeLoc, _rtWeatherLoc, _rtAspectLoc;
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
        private int _rtMoonTintLoc, _rtMoonPhaseLoc;
        // Stars
        private int _rtStarBrightLoc, _rtStarDensLoc, _rtStarTwinkleLoc, _rtStarColorLoc, _rtStarsEnabledLoc;
        // Eclipses
        private int _rtSolarEclipseLoc, _rtLunarEclipseLoc, _rtEclipseGlowLoc;
        // SunRays
        private int _rtSunRayIntLoc, _rtSunRayCountLoc, _rtSunRayLenLoc, _rtSunRayColorLoc, _rtSunRaysEnabledLoc;

        // ── Texture caches ──
        private readonly Dictionary<string, uint> _textureCache = new();
        private uint _moonTextureID = 0;

        // ── Time accumulator ──
        private float _totalTime = 0f;

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

            // ── Cache realtime uniform locations ──
            _rtViewLoc = GL.GetUniformLocation(_realtimeShader, "view");
            _rtProjLoc = GL.GetUniformLocation(_realtimeShader, "projection");
            _rtFogColorLoc = GL.GetUniformLocation(_realtimeShader, "fogColor");
            _rtSunDirLoc = GL.GetUniformLocation(_realtimeShader, "sunDir");
            _rtTimeLoc = GL.GetUniformLocation(_realtimeShader, "time");
            _rtWeatherLoc = GL.GetUniformLocation(_realtimeShader, "weatherMode");
            _rtAspectLoc = GL.GetUniformLocation(_realtimeShader, "u_aspectRatio");

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
        }

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
        /// Bind a face texture to a texture unit, or unbind if path is empty.
        /// </summary>
        private void BindFaceTexture(string path, int unit, int texLoc, int hasLoc, uint shader)
        {
            uint texID = LoadTextureCached(path);
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
        /// Draw the sky. Delegates to the appropriate renderer based on SkySettings.Type.
        /// </summary>
        public void Draw(Camera camera, Lights lights, float deltaTime, SkySettings skySettings,
            Texture[]? legacyMoonTextures, TerrainChunk? terrain)
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
                    DrawProcedural(camera, lights, deltaTime, skySettings, legacyMoonTextures, terrain);
                    break;
            }
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
            Matrix4x4 projection = camera.GetProjectionMatrix();

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

            GL.DepthMask(false);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthFunc(Const.GL_LEQUAL);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 36);

            GL.DepthMask(true);
            GL.DepthFunc(Const.GL_LESS);
        }

        /// <summary>
        /// Draw a dome sphere with a panoramic equirectangular texture.
        /// </summary>
        private void DrawDome(Camera camera, Lights lights, SkySettings settings)
        {
            GL.UseProgram(_domeShader);

            Matrix4x4 view = camera.GetViewMatrix();
            view.M41 = view.M42 = view.M43 = 0;
            view.M14 = view.M24 = view.M34 = 0; view.M44 = 1;
            Matrix4x4 projection = camera.GetProjectionMatrix();

            GL.UniformMatrix4fv(_domeViewLoc, 1, false, (float*)&view);
            GL.UniformMatrix4fv(_domeProjLoc, 1, false, (float*)&projection);
            GL.Uniform3f(_domeTintLoc, settings.Dome.TintColor.X, settings.Dome.TintColor.Y, settings.Dome.TintColor.Z);
            GL.Uniform1f(_domeRadiusLoc, settings.Dome.Radius);
            GL.Uniform1f(_domeRotYLoc, settings.Dome.RotationY);

            uint texID = LoadTextureCached(settings.Dome.TexturePath);
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

            GL.DepthMask(false);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthFunc(Const.GL_LEQUAL);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 36);

            GL.DepthMask(true);
            GL.DepthFunc(Const.GL_LESS);
        }

        /// <summary>
        /// Draw the procedural realtime sky with all features.
        /// </summary>
        private void DrawProcedural(Camera camera, Lights lights, float deltaTime,
            SkySettings settings, Texture[]? legacyMoonTextures, TerrainChunk? terrain)
        {
            GL.UseProgram(_realtimeShader);

            Matrix4x4 view = camera.GetViewMatrix();
            view.M41 = view.M42 = view.M43 = 0;
            view.M14 = view.M24 = view.M34 = 0; view.M44 = 1;
            Matrix4x4 projection = camera.GetProjectionMatrix();

            GL.UniformMatrix4fv(_rtViewLoc, 1, false, (float*)&view);
            GL.UniformMatrix4fv(_rtProjLoc, 1, false, (float*)&projection);

            // Camera / Time / Weather
            float currentWeather = WeatherOverride ?? Keyboard.GetCurrentWeather();
            GL.Uniform3f(_rtFogColorLoc, lights.FogColor.X, lights.FogColor.Y, lights.FogColor.Z);
            GL.Uniform3f(_rtSunDirLoc, lights.RealSunDir.X, lights.RealSunDir.Y, lights.RealSunDir.Z);
            GL.Uniform3f(_rtTimeLoc, _totalTime, 0, 0);
            GL.Uniform1f(_rtWeatherLoc, currentWeather);
            GL.Uniform1f(_rtAspectLoc, camera.GetAspect());

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
            GL.DepthMask(false);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthFunc(Const.GL_LEQUAL);

            GL.BindVertexArray(_vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 36);

            GL.DepthMask(true);
            GL.DepthFunc(Const.GL_LESS);
        }
    }
}
