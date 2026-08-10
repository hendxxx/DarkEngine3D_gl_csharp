using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using System;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    public unsafe class Lights
    {
        uint shaderProgram;
        int sunDirLoc, viewPosLoc, lightColorLoc, fogColorLoc, weatherModeLoc;

        Vector3 vsunDirLoc;
        Vector3 vviewPosLoc;
        Vector3 vlightColorLoc;

        public float WorldTime = 0.0f;

        // --- TIME SYSTEM ---

        //1 detik real = 1 detik game 0.0000727f
        //1 detik real = 30 detik game 0.00218f
        //1 detik real = 1 menit game 0.0043633f
        //1 detik real = 30 menit game    0.1309f
        //1 detik real = 1 jam game   0.2618f
        //1 detik real = 1 hari game	6.28318f (2π)
        public float baseSpeed = 0.00218f;
        public float manualMultiplier = 30.0f;   // bisa kamu ubah ke 2, 4, 8 buat fast-forward

        public Vector3 SunDir { get; private set; }       // arah matahari visual
        public Vector3 RealSunDir { get; private set; }   // sama dengan SunDir
        public Vector3 ShadowDirStable { get; private set; } // arah shadow yang dismoothing

        public Vector3 FogColor { get; private set; }
        public Vector3 LightColor { get; private set; }

        // ── Editor light-object overrides ──
        // When a Light editor object exists in the scene, SceneManager copies its
        // direction / color / intensity here so Update() uses them instead of the
        // procedurally-computed sun. Null = fall back to computed values.
        /// <summary>When set, overrides the computed sun direction (editor Light object).</summary>
        public Vector3? SunDirOverride { get; set; }
        /// <summary>When set, overrides the computed light color (editor Light object).</summary>
        public Vector3? LightColorOverride { get; set; }
        /// <summary>Brightness multiplier applied to LightColorOverride. Default 1.</summary>
        public float LightIntensity { get; set; } = 1f;
        /// <summary>Sun brightness multiplier applied to the procedural/overridden sun color.
        /// Set by the editor from a placed Sky object's SkySunIntensity. Default 1.</summary>
        public float SunBrightness { get; set; } = 1f;

        // smoothing shadowDir: makin besar, makin cepat ngejar matahari
        // Dengan sun speed 30x, nilai 5.0 terlalu agresif → shadow flicker.
        // 0.8 = smooth tapi tetap update cepat saat matahari bergerak
        private const float ShadowSmoothSpeed = 0.95f;

        public Lights(Vector3 _sundir, Vector3 _lightColor, Vector3 _viewpos, string? startTime = null)
        {
            vsunDirLoc = _sundir;
            vviewPosLoc = _viewpos;
            vlightColorLoc = _lightColor;

            float startHour;
            if (string.IsNullOrEmpty(startTime) || !TimeSpan.TryParse(startTime, out TimeSpan ts))
                startHour = (float)(DateTime.Now.Hour + DateTime.Now.Minute / 60.0);
            else
                startHour = (float)ts.TotalHours;

            WorldTime = (startHour / 24.0f) * (MathF.PI * 2.0f);

            // inisialisasi shadowDir stabil dengan arah awal
            ShadowDirStable = Vector3.Normalize(_sundir);

            Init();
        }

        public void Init()
        {
            shaderProgram = Shader.GetShaderProgram();

            sunDirLoc = GL.GetUniformLocation(shaderProgram, "sunDir");
            viewPosLoc = GL.GetUniformLocation(shaderProgram, "viewPos");
            lightColorLoc = GL.GetUniformLocation(shaderProgram, "lightColor");
            fogColorLoc = GL.GetUniformLocation(shaderProgram, "fogColor");
            weatherModeLoc = GL.GetUniformLocation(Shader.GetSkyShaderProgram(), "weatherMode");
        }

        public void Update(float deltaTime, Vector3 currentViewPos)
        {
            // waktu dunia (radian)
            WorldTime += deltaTime * baseSpeed ;

            GL.UseProgram(shaderProgram);
            GL.BindVertexArray(0);

            // hitung sudut matahari (visual)
            float sunAngle = WorldTime - (MathF.PI * 0.5f);

            float sunX = MathF.Cos(sunAngle);
            float sunY = MathF.Sin(sunAngle);
            float sunZ = 0.3f;

            Vector3 sunDir = Vector3.Normalize(new Vector3(sunX, sunY, sunZ));
            Vector3 moonDir = -sunDir;

            // blend day–night
            float nightBlend = Math.Clamp((0.15f - sunDir.Y) / 0.15f, 0.0f, 1.0f);

            // arah shadow ideal (sun/moon)
            Vector3 targetShadowDir = sunDir;
            if (nightBlend > 0.5f)
                targetShadowDir = moonDir;

            targetShadowDir = Vector3.Normalize(targetShadowDir);

            // === KUNCI STABILITAS: smoothing arah shadow ===
            float t = 1.0f - MathF.Exp(-ShadowSmoothSpeed * deltaTime);
            ShadowDirStable = Vector3.Normalize(Vector3.Lerp(ShadowDirStable, targetShadowDir, t));

            // lighting color
            float sunYForLight = sunDir.Y;

            float tDay = Smoothstep01(0.05f, 0.30f, sunYForLight);
            float tNight = Smoothstep01(0.05f, -0.20f, sunYForLight);
            float tDusk = MathF.Max(0.0f, 1.0f - tDay - tNight);

            Vector3 dayLight = new(0.95f, 0.93f, 0.88f);
            Vector3 duskLight = new(1.00f, 0.55f, 0.25f);
            Vector3 nightLight = new(0.05f, 0.07f, 0.14f);

            const float dayBrightness = 0.85f;
            const float duskBrightness = 0.65f;
            const float nightBrightness = 0.55f;

            Vector3 lightColor =
                (dayLight * (dayBrightness * tDay) +
                 duskLight * (duskBrightness * tDusk) +
                 nightLight * (nightBrightness * tNight)) * SunBrightness;

            // ── Horizon fog color (sama persis dengan GetHorizonFogColor di shader) ──
            // Computed AFTER the override block below so the fog color follows the
            // overridden sun direction too (not just the procedural one).

            // ── Weather dimming — pow curve for more dramatic overcast effect ──
            float w = Keyboard.GetCurrentWeather();
            float weatherDim = 1.0f / (1.0f + w * 4.0f);

            // ── Editor light-object overrides: replace the procedural sun with the
            //    user-placed Light object's direction/color if set. Applied BEFORE the
            //    public properties are assigned so the overridden values flow through to
            //    the uniforms AND to the property consumers (EditorObjectManager.Draw,
            //    StaticObjectManager.Draw, ObjectManager.Draw, BillboardManager, Skybox). ──
            if (SunDirOverride.HasValue)
            {
                sunDir = Vector3.Normalize(SunDirOverride.Value);
                targetShadowDir = sunDir;
                ShadowDirStable = sunDir;
            }
            if (LightColorOverride.HasValue)
            {
                // Sky sun brightness still applies on top of the light-marker color so the
                // intensity slider keeps working even when a Light object is present.
                lightColor = LightColorOverride.Value * LightIntensity * SunBrightness;
            }

            Vector3 horizonFogColor = ComputeHorizonFogColor(sunDir);

            RealSunDir = sunDir;
            SunDir = ShadowDirStable;
            FogColor = horizonFogColor * weatherDim;
            LightColor = lightColor * weatherDim;

            // kirim arah matahari asli (realSunDir) ke terrain shader
            int realSunDirLoc = GL.GetUniformLocation(shaderProgram, "realSunDir");
            GL.Uniform3f(realSunDirLoc, sunDir.X, sunDir.Y, sunDir.Z);

            // kirim ke sky shader (visual)
            int skySunDirLoc = GL.GetUniformLocation(Shader.GetSkyShaderProgram(), "sunDir");
            GL.Uniform3f(skySunDirLoc, sunDir.X, sunDir.Y, sunDir.Z);

            // kirim arah shadow yang sudah stabil
            int shadowDirLoc = GL.GetUniformLocation(shaderProgram, "shadowDir");
            GL.Uniform3f(shadowDirLoc, ShadowDirStable.X, ShadowDirStable.Y, ShadowDirStable.Z);

            GL.Uniform3f(lightColorLoc, LightColor.X, LightColor.Y, LightColor.Z);
            GL.Uniform3f(viewPosLoc, currentViewPos.X, currentViewPos.Y, currentViewPos.Z);
            GL.Uniform3f(fogColorLoc, FogColor.X, FogColor.Y, FogColor.Z);

            int useFogLocation = GL.GetUniformLocation(shaderProgram, "useFog");
            GL.Uniform1i(useFogLocation, Keyboard.GetIsFogActive() ? 1 : 0);

            int shadowFilterMode = GL.GetUniformLocation(shaderProgram, "shadowFilterMode");
            GL.Uniform1i(shadowFilterMode, Keyboard.GetIsHardShadow());

            // ── Live shadow bias / blend tuning (Shadow Settings panel). The main shader
            // program is shared by game terrain, game primitives and editor objects, so one
            // upload here covers every main-shader render path. ──
            ShadowUniforms.UploadMain(shaderProgram);

            GL.ClearColor(FogColor.X, FogColor.Y, FogColor.Z, 1.0f);
        }

        private static float Smoothstep01(float edge0, float edge1, float x)
        {
            float denom = edge1 - edge0;
            if (MathF.Abs(denom) < 1e-6f) return x < edge0 ? 0.0f : 1.0f;
            float t = Math.Clamp((x - edge0) / denom, 0.0f, 1.0f);
            return t * t * (3.0f - 2.0f * t);
        }

        /// <summary>Compute horizon fog color — sama persis dengan fungsi GetHorizonFogColor() di shader.</summary>
        private static Vector3 ComputeHorizonFogColor(Vector3 sd)
        {
            float sunY = sd.Y;
            float sunIntensity = Math.Clamp(sunY * 1.5f + 0.5f, 0.0f, 2.0f);

            // Simplified Mie scattering at horizon
            float mu = Math.Clamp(Vector3.Dot(new Vector3(0.0f, 0.0f, 1.0f), sd), -1.0f, 1.0f);
            float g = 0.76f;
            float phaseMie = (1.0f - g * g) / MathF.Pow(1.0f + g * g - 2.0f * g * mu, 1.5f);

            Vector3 scattering = new Vector3(1.0f, 0.85f, 0.65f) * phaseMie * sunIntensity * 0.008f;

            // Day / dusk / night blend (thresholds identik dengan GetHorizonFogColor di shader)
            float tDay = Smoothstep01(0.05f, 0.30f, sunY);
            float tNight = 1.0f - Smoothstep01(-0.20f, 0.05f, sunY);
            float tDusk = MathF.Max(0.0f, 1.0f - tDay - tNight);

            Vector3 dayColor   = new(0.55f, 0.72f, 0.90f);
            Vector3 duskColor  = new(0.85f, 0.40f, 0.22f);
            Vector3 nightColor = new(0.02f, 0.03f, 0.06f);

            Vector3 color = dayColor * tDay + duskColor * tDusk + nightColor * tNight;
            color += scattering * 0.5f;

            return color;
        }

        public string GetFormattedTime()
        {
            float totalHours = (WorldTime % (MathF.PI * 2.0f)) / (MathF.PI * 2.0f) * 24.0f;
            if (totalHours < 0) totalHours += 24.0f;

            int hours = (int)totalHours;
            int minutes = (int)((totalHours - hours) * 60);

            return $"{hours:D2}:{minutes:D2}";
        }
    }
}
