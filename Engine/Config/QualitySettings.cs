using DarkEngine3D_gl_csharp.Engine.Inputs;
using System;

namespace DarkEngine3D_gl_csharp.Engine.Config
{
    /// <summary>
    /// Unified AAA quality presets (Low/Medium/High/Ultra) that drive three things at
    /// once: the scene FBO MSAA sample count, the CSM shadow quality preset, and the
    /// shadow filter mode. <see cref="Apply(int)"/> reconfigures all three together —
    /// the one switch a user needs for the whole look/performance balance.
    ///
    /// MSAA takes effect when the scene FBOs are (re)created: at game start, on window
    /// resize, via <c>PostProcessStack.ApplyQuality()</c>, or the SceneManager shared
    /// FBO's self-rebuild in EnsureSharedFBO(). Shadow settings apply immediately
    /// (CSM instances self-rebuild through ShadowSettings.Version).
    /// </summary>
    public static class QualitySettings
    {
        public const int QualityLow = 0;
        public const int QualityMedium = 1;
        public const int QualityHigh = 2;
        public const int QualityUltra = 3;

        /// <summary>Currently selected preset level (index into <see cref="Presets"/>).
        /// Defaults to LOW so the engine runs clean on any GPU out of the box — the user
        /// can raise it to High/Ultra when the target hardware is known.</summary>
        public static int Current = QualityLow;

        /// <summary>MSAA sample count for the scene FBOs (1 = MSAA off / single-sample).
        /// Consumed by PostProcessStack and the SceneManager shared FBO at creation time.</summary>
        public static int MsaaSamples = 1;

        // ── Preset table: (MSAA samples, shadow quality, shadow filter mode) ──
        // Shadow filter modes: 0=PCF16, 1=Hard, 2=PCF16, 3=PCF16 Soft, 4=PCF32,
        // 5=PCF32 Soft, 6=PCSS16, 7=PCSS16 Soft, 8=PCSS32, 9=PCSS32 Soft.
        private static readonly (int msaa, int shadow, int filter)[] Presets =
        [
            (1, ShadowSettings.QualityLow, 0),    // Low    — MSAA off, Low shadows, PCF 16 (default)
            (2, ShadowSettings.QualityMedium, 4), // Medium — MSAA 2x, Medium shadows, PCF 32
            (4, ShadowSettings.QualityHigh, 8),   // High   — MSAA 4x, High shadows, PCSS 32
            (4, ShadowSettings.QualityUltra, 8),  // Ultra  — MSAA 4x, Ultra shadows, PCSS 32
                                                 //   (PCSS 32 Soft's radius-12 sampling can
                                                 //   look noisy on the far cascade)
        ];

        /// <summary>Apply a quality preset: MSAA samples + shadow quality + shadow filter.</summary>
        public static void Apply(int quality)
        {
            quality = Math.Clamp(quality, QualityLow, QualityUltra);
            Current = quality;

            var p = Presets[quality];
            MsaaSamples = p.msaa;
            ShadowSettings.ApplyQuality(p.shadow);
            Keyboard.SetShadowFilterMode(p.filter);

            Console.WriteLine($"[QualitySettings] Preset {PresetName(quality)} — MSAA {MsaaSamples}x, shadow {ShadowSettings.CascadeSizes[0]}/{ShadowSettings.CascadeSizes[1]}/{ShadowSettings.CascadeSizes[2]}, filter {p.filter}");
        }

        public static string PresetName(int quality) => quality switch
        {
            QualityLow => "LOW",
            QualityMedium => "MEDIUM",
            QualityHigh => "HIGH",
            _ => "ULTRA",
        };

        /// <summary>Restore the quality preset from settings.json at startup (Program.cs).</summary>
        public static void Apply(SettingsData s)
        {
            if (s == null) return;
            try { Apply(s.QualityPreset); }
            catch (Exception ex) { Console.WriteLine($"[QualitySettings] Apply failed: {ex.Message}"); }
        }

        /// <summary>Write the current preset level to settings.json.</summary>
        public static bool Persist()
        {
            try
            {
                var s = SettingsSave.Load();
                s.QualityPreset = Current;
                SettingsSave.Save(s);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QualitySettings] Persist failed: {ex.Message}");
                return false;
            }
        }
    }
}
