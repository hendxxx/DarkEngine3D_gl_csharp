using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    public unsafe class Skybox
    {
        private readonly uint vao, vbo;
        public uint skyShader;
        private readonly int viewLoc, projLoc, fogColorLoc, sunDirLoc, timeLoc;

        private readonly float[] vertices = [
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

        // ── New 3-type sky renderer ──
        private SkyRenderer? _skyRenderer;

        public Skybox()
        {
            skyShader = Shader.GetSkyShaderProgram();

            // VAO/VBO Setup
            fixed (uint* pVao = &vao) GL.GenVertexArrays(1, pVao);
            fixed (uint* pVbo = &vbo) GL.GenBuffers(1, pVbo);
            
            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
            fixed (float* p = vertices) {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (uint)(vertices.Length * sizeof(float)), (void*)p, Const.GL_STATIC_DRAW);
            }
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);

            viewLoc = GL.GetUniformLocation(skyShader, "view");
            projLoc = GL.GetUniformLocation(skyShader, "projection");
            fogColorLoc = GL.GetUniformLocation(skyShader, "fogColor");
            sunDirLoc = GL.GetUniformLocation(skyShader, "sunDir");
            timeLoc = GL.GetUniformLocation(skyShader, "time");

            // Initialize the new SkyRenderer lazily (on first use)
            _skyRenderer = new SkyRenderer();
        }

        private float totalTime = 0.0f;
#pragma warning disable CS0414
        private float exposureState = 0.0f;

        /// <summary>Optional cloud-coverage override (0..1). When set, this replaces
        /// <see cref="Keyboard.GetCurrentWeather()"/> as the sky shader's weather mode.
        /// Set by the editor from a placed Sky object's SkyCloudCoverage.</summary>
        public float? WeatherOverride = null;

        /// <summary>Optional SkySettings from the editor Sky object. When set and non-null,
        /// the SkyRenderer handles drawing based on the sky type (Procedural/Skybox/Dome).</summary>
        public SkySettings? ActiveSkySettings { get; set; } = null;

        /// <summary>Legacy Draw method (backward-compatible, uses the original procedural shader).</summary>
        public void Draw(Camera camera, Lights lights, float deltaTime, Texture[] skyTextures, TerrainChunk? terrain)
        {
            // If we have new SkySettings, delegate to SkyRenderer
            if (ActiveSkySettings != null && _skyRenderer != null)
            {
                _skyRenderer.WeatherOverride = WeatherOverride;
                _skyRenderer.Draw(camera, lights, deltaTime, ActiveSkySettings, skyTextures, terrain);
                return;
            }

            // Legacy path: original procedural sky shader
            DrawLegacy(camera, lights, deltaTime, skyTextures, terrain);
        }

        /// <summary>Legacy procedural sky draw (original shader).</summary>
        private void DrawLegacy(Camera camera, Lights lights, float deltaTime, Texture[] skyTextures, TerrainChunk? terrain)
        {
            // === PAKAI SHADER LANGIT ===
            GL.UseProgram(skyShader);

            int weatherModeLoc = GL.GetUniformLocation(skyShader, "weatherMode"); 
            int timeLoc = GL.GetUniformLocation(skyShader, "time");

            int aspectLoc = GL.GetUniformLocation(skyShader, "u_aspectRatio");
            float currentAspect = camera.GetAspect();

            // === WEATHER ===
            float currentWeatherVal = WeatherOverride ?? Keyboard.GetCurrentWeather();
            GL.Uniform1f(weatherModeLoc, currentWeatherVal);

            // === TIME ===
            totalTime += deltaTime;
            GL.Uniform3f(timeLoc, totalTime, 0, 0);

            // === CAMERA ===
            Matrix4x4 view = camera.GetViewMatrix();
            view.M41 = view.M42 = view.M43 = 0;
            view.M14 = view.M24 = view.M34 = 0; view.M44 = 1;

            Matrix4x4 projection = camera.GetProjectionMatrix();

            GL.UniformMatrix4fv(viewLoc, 1, false, (float*)&view);
            GL.UniformMatrix4fv(projLoc, 1, false, (float*)&projection);

            // === LIGHTS ===
            GL.Uniform3f(fogColorLoc, lights.FogColor.X, lights.FogColor.Y, lights.FogColor.Z);
            GL.Uniform3f(sunDirLoc, lights.RealSunDir.X, lights.RealSunDir.Y, lights.RealSunDir.Z);

            // === MOON TEXTURE ===
            GL.ActiveTexture(Const.GL_TEXTURE4);
            GL.BindTexture(Const.GL_TEXTURE_2D, skyTextures[0].ID);
            GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, Const.GL_CLAMP_TO_EDGE);
            GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, Const.GL_CLAMP_TO_EDGE);
            GL.Uniform1i(GL.GetUniformLocation(skyShader, "moonTex"), 4);

            // === ASPECT ===
            GL.Uniform1f(aspectLoc, currentAspect);

            // === DRAW SKY ===
            GL.DepthMask(false);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthFunc(Const.GL_LEQUAL);

            GL.BindVertexArray(vao);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 36);

            GL.DepthMask(true);
            GL.DepthFunc(Const.GL_LESS);
        }
    }
}
