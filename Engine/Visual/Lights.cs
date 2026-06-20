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

        // smoothing shadowDir: makin besar, makin cepat ngejar matahari
        // Dengan sun speed 30x, nilai 5.0 terlalu agresif → shadow flicker.
        // 0.8 = smooth tapi tetap update cepat saat matahari bergerak
        private const float ShadowSmoothSpeed = 0.8f;

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

            Vector3 dayFog = new(0.70f, 0.80f, 1.00f);
            Vector3 duskFog = new(0.55f, 0.30f, 0.18f);
            Vector3 nightFog = new(0.02f, 0.03f, 0.06f);

            const float dayBrightness = 0.85f;
            const float duskBrightness = 0.65f;
            const float nightBrightness = 0.55f;

            Vector3 lightColor =
                dayLight * (dayBrightness * tDay) +
                duskLight * (duskBrightness * tDusk) +
                nightLight * (nightBrightness * tNight);

            Vector3 fogColor =
                dayFog * tDay +
                duskFog * tDusk +
                nightFog * tNight;

            RealSunDir = sunDir;
            SunDir = ShadowDirStable;
            FogColor = fogColor;
            LightColor = lightColor;

            // kirim arah matahari asli (realSunDir) ke terrain shader
            int realSunDirLoc = GL.GetUniformLocation(shaderProgram, "realSunDir");
            GL.Uniform3f(realSunDirLoc, sunDir.X, sunDir.Y, sunDir.Z);

            // kirim ke sky shader (visual)
            int skySunDirLoc = GL.GetUniformLocation(Shader.GetSkyShaderProgram(), "sunDir");
            GL.Uniform3f(skySunDirLoc, sunDir.X, sunDir.Y, sunDir.Z);

            // kirim arah shadow yang sudah stabil
            int shadowDirLoc = GL.GetUniformLocation(shaderProgram, "shadowDir");
            GL.Uniform3f(shadowDirLoc, ShadowDirStable.X, ShadowDirStable.Y, ShadowDirStable.Z);

            GL.Uniform3f(lightColorLoc, lightColor.X, lightColor.Y, lightColor.Z);
            GL.Uniform3f(viewPosLoc, currentViewPos.X, currentViewPos.Y, currentViewPos.Z);
            GL.Uniform3f(fogColorLoc, fogColor.X, fogColor.Y, fogColor.Z);

            int useFogLocation = GL.GetUniformLocation(shaderProgram, "useFog");
            GL.Uniform1i(useFogLocation, Keyboard.GetIsFogActive() ? 1 : 0);

            int shadowFilterMode = GL.GetUniformLocation(shaderProgram, "shadowFilterMode");
            GL.Uniform1i(shadowFilterMode, Keyboard.GetIsHardShadow());

            float currentWeatherVal = Keyboard.GetCurrentWeather();
            float terrainLightIntensity = 1.0f - (currentWeatherVal * 0.80f);
            Vector3 dynamicTerrainLight = lightColor * terrainLightIntensity;

            int terrainLightColorLoc = GL.GetUniformLocation(Shader.GetShaderProgram(), "lightColor");
            GL.Uniform3f(terrainLightColorLoc, dynamicTerrainLight.X, dynamicTerrainLight.Y, dynamicTerrainLight.Z);

            GL.ClearColor(fogColor.X, fogColor.Y, fogColor.Z, 1.0f);
        }

        private static float Smoothstep01(float edge0, float edge1, float x)
        {
            float denom = edge1 - edge0;
            if (MathF.Abs(denom) < 1e-6f) return x < edge0 ? 0.0f : 1.0f;
            float t = Math.Clamp((x - edge0) / denom, 0.0f, 1.0f);
            return t * t * (3.0f - 2.0f * t);
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
