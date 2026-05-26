using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
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
        public float baseSpeed = 0.0043f; // Normal: 1 real second = 1 game minute
        public float manualMultiplier = 60.0f; // Fast Forward: 1 real second = 1 game hour

        public Vector3 SunDir { get; private set; }
        public Vector3 FogColor { get; private set; }
        public Vector3 LightColor { get; private set; }

        public Lights(Vector3 _sundir, Vector3 _lightColor, Vector3 _viewpos, string? startTime = null)
        {
            vsunDirLoc = _sundir;
            vviewPosLoc = _viewpos;
            vlightColorLoc = _lightColor;

            // Set Initial Time
            float startHour;
            if (string.IsNullOrEmpty(startTime) || !TimeSpan.TryParse(startTime, out TimeSpan ts))
            {
                startHour = (float)(DateTime.Now.Hour + DateTime.Now.Minute / 60.0);
            }
            else
            {
                startHour = (float)ts.TotalHours;
            }

            // Convert Hour (0-24) to Radians (0 - 2*PI)
            WorldTime = (startHour / 24.0f) * (MathF.PI * 2.0f);

            Init();
        }

        //Init light location
        public void Init()
        {
            shaderProgram = Shader.GetShaderProgram();

            sunDirLoc = GL.GetUniformLocation(shaderProgram, "sunDir");
            viewPosLoc = GL.GetUniformLocation(shaderProgram, "viewPos");
            lightColorLoc = GL.GetUniformLocation(shaderProgram, "lightColor");
            fogColorLoc = GL.GetUniformLocation(shaderProgram, "fogColor"); 
            weatherModeLoc = GL.GetUniformLocation(Shader.GetSkyShaderProgram(), "weatherMode");
        }

        public void Update(float deltaTime,nint window,Vector3 currentViewPos)
        {

            // Always progress time slowly
            WorldTime += deltaTime * baseSpeed;


            GL.UseProgram(shaderProgram);
            GL.BindVertexArray(0);
            // Hitung sudut (0 radian = Terbit, PI/2 = Siang, PI = Terbenam)
            float sunAngle = WorldTime - (MathF.PI * 0.5f);

            float sunX = MathF.Cos(sunAngle);
            float sunY = MathF.Sin(sunAngle);
            float sunZ = 0.3f; // Buat Z statis agar cahaya selalu agak dari depan/samping

            // Normalize agar shader tidak bingung dengan panjang vektor
            Vector3 sunDir = Vector3.Normalize(new Vector3(sunX, sunY, sunZ));

            // Continuous day / dusk / night weights based on sun height (sunY).
            // tDay   ramps from 0 → 1 as sun rises from horizon to high in sky.
            // tNight ramps from 0 → 1 as sun dips below the horizon.
            // tDusk  fills the gap so the three weights always sum to 1 → no jumps.
            float tDay   = Smoothstep01(0.05f, 0.30f, sunY);
            float tNight = Smoothstep01(0.05f, -0.20f, sunY);
            float tDusk  = MathF.Max(0.0f, 1.0f - tDay - tNight);

            // Palette: bright neutral sunlight / warm dusk / dim moonlight blue.
            Vector3 dayLight    = new(0.95f, 0.93f, 0.88f);
            Vector3 duskLight   = new(1.00f, 0.55f, 0.25f);
            Vector3 nightLight  = new(0.05f, 0.07f, 0.14f);

            Vector3 dayFog      = new(0.70f, 0.80f, 1.00f);
            Vector3 duskFog     = new(0.55f, 0.30f, 0.18f);
            Vector3 nightFog    = new(0.02f, 0.03f, 0.06f);

            // Brightness multipliers per regime — also blended smoothly.
            const float dayBrightness   = 0.85f;
            const float duskBrightness  = 0.65f;
            const float nightBrightness = 0.55f; // applied to a dim color so net result stays dark

            Vector3 lightColor =
                dayLight   * (dayBrightness   * tDay)  +
                duskLight  * (duskBrightness  * tDusk) +
                nightLight * (nightBrightness * tNight);

            Vector3 fogColor =
                dayFog   * tDay  +
                duskFog  * tDusk +
                nightFog * tNight;

            // Kirim ke shader
            SunDir = sunDir;
            FogColor = fogColor;
            LightColor = lightColor;

            GL.Uniform3f(sunDirLoc, sunDir.X, sunDir.Y, sunDir.Z);
            GL.Uniform3f(lightColorLoc, lightColor.X, lightColor.Y, lightColor.Z);
            GL.Uniform3f(viewPosLoc, currentViewPos.X, currentViewPos.Y, currentViewPos.Z);
            GL.Uniform3f(fogColorLoc, fogColor.X, fogColor.Y, fogColor.Z);

            // Pastikan nama uniform "useFog" sesuai dengan yang ada di fragment shader Anda
            int useFogLocation = GL.GetUniformLocation(shaderProgram, "useFog");
            GL.Uniform1i(useFogLocation, Keyboard.GetIsFogActive() ? 1 : 0);


            float currentWeatherVal = Keyboard.GetCurrentWeather();
            float terrainLightIntensity = 1.0f - (currentWeatherVal * 0.80f);
            Vector3 dynamicTerrainLight = lightColor * terrainLightIntensity;

            int terrainLightColorLoc = GL.GetUniformLocation(Shader.GetShaderProgram(), "lightColor");
            // Kirim dynamicTerrainLight yang sudah redup, BUKAN lightColor mentah yang terang
            GL.Uniform3f(terrainLightColorLoc, dynamicTerrainLight.X, dynamicTerrainLight.Y, dynamicTerrainLight.Z);
             
            GL.ClearColor(fogColor.X, fogColor.Y, fogColor.Z, 1.0f);
        }


        // GLSL-style smoothstep. Handles reversed edges (edge0 > edge1) gracefully so
        // the same helper can drive both rising (day) and falling (night) transitions.
        private static float Smoothstep01(float edge0, float edge1, float x)
        {
            float denom = edge1 - edge0;
            if (MathF.Abs(denom) < 1e-6f) return x < edge0 ? 0.0f : 1.0f;
            float t = Math.Clamp((x - edge0) / denom, 0.0f, 1.0f);
            return t * t * (3.0f - 2.0f * t);
        }

        public string GetFormattedTime()
        {
            // Convert WorldTime (radians) to 0-24 hours
            // One full circle (2*PI) = 24 hours
            float totalHours = (WorldTime % (MathF.PI * 2.0f)) / (MathF.PI * 2.0f) * 24.0f;
            if (totalHours < 0) totalHours += 24.0f;

            int hours = (int)totalHours;
            int minutes = (int)((totalHours - hours) * 60);

            return $"{hours:D2}:{minutes:D2}";
        }
    }
}
