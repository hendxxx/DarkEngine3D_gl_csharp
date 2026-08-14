using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Config
{
    /// <summary>Global fog settings edited from the IDE Inspector "Fog" section and uploaded
    /// to every fog-capable shader by <see cref="Visual.FogUniforms"/>. Mirrors the
    /// ShadowSettings pattern: live-tunable, persisted to settings.json, restored at startup.</summary>
    public static class FogSettings
    {
        /// <summary>Master switch. The F key quick-toggle in Keyboard flips this too.</summary>
        public static bool Enabled = true;

        /// <summary>Fog falloff model: 1 = Linear (start→end), 2 = Exponential,
        /// 3 = Exp2 + height blend (default — matches the original terrain fog).</summary>
        public static int Mode = 3;

        /// <summary>When true the fog color follows the sky/horizon (sun position);
        /// when false <see cref="Color"/> is used verbatim.</summary>
        public static bool UseSkyColor = true;

        /// <summary>Manual fog color (used when UseSkyColor is false).</summary>
        public static Vector3 Color = new(0.55f, 0.72f, 0.90f);

        /// <summary>Density for Exponential / Exp2 fog (world units⁻¹).</summary>
        public static float Density = 0.0035f;

        /// <summary>Linear fog start distance (world units).</summary>
        public static float StartDistance = 50f;

        /// <summary>Linear fog end distance (world units).</summary>
        public static float EndDistance = 300f;

        /// <summary>Height fog floor — the world Y below which fog is densest.</summary>
        public static float Height = 10f;

        /// <summary>Height falloff range — above Height + HeightRange the height fog fades out.</summary>
        public static float HeightRange = 45f;

        /// <summary>Write the current fog values to settings.json (persist across restarts).</summary>
        public static bool Persist()
        {
            try
            {
                var s = SettingsSave.Load();
                s.FogEnabled = Enabled;
                s.FogMode = Mode;
                s.FogUseSkyColor = UseSkyColor;
                s.FogColorR = Color.X;
                s.FogColorG = Color.Y;
                s.FogColorB = Color.Z;
                s.FogDensity = Density;
                s.FogStart = StartDistance;
                s.FogEnd = EndDistance;
                s.FogHeight = Height;
                s.FogHeightRange = HeightRange;
                SettingsSave.Save(s);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FogSettings] Persist failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Restore all fog values from settings.json at startup (called by Program.cs
        /// after SettingsSave.Load). Values are clamped to sane ranges so persisted garbage
        /// can never produce degenerate fog.</summary>
        public static void Apply(SettingsData s)
        {
            if (s == null) return;

            try
            {
                Enabled = s.FogEnabled;
                Mode = Math.Clamp(s.FogMode, 1, 3);
                UseSkyColor = s.FogUseSkyColor;
                Color = new Vector3(
                    Math.Clamp(s.FogColorR, 0f, 1f),
                    Math.Clamp(s.FogColorG, 0f, 1f),
                    Math.Clamp(s.FogColorB, 0f, 1f));
                Density = Math.Clamp(s.FogDensity, 0f, 0.05f);
                StartDistance = Math.Max(0f, s.FogStart);
                EndDistance = Math.Max(StartDistance + 1f, s.FogEnd);
                Height = s.FogHeight;
                HeightRange = Math.Max(1f, s.FogHeightRange);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FogSettings] Apply failed: {ex.Message}");
            }
        }
    }
}
