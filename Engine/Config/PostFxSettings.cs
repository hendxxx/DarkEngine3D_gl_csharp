using System;

namespace DarkEngine3D_gl_csharp.Engine.Config
{
    /// <summary>
    /// Live post-processing configuration (AAA: bloom + ACES tonemapping + gamma
    /// correction), consumed by <see cref="Visual.PostProcessing.PostProcessStack"/>
    /// every frame. Values are read directly each frame, so changes apply immediately;
    /// <see cref="Persist"/> writes them to settings.json so they survive restarts.
    /// </summary>
    public static class PostFxSettings
    {
        /// <summary>Master switch for the whole post-FX chain (bloom/tonemap/gamma).</summary>
        public static bool Enabled = true;

        /// <summary>Bloom add-back strength (0 = off).</summary>
        public static float BloomIntensity = 0.05f;

        /// <summary>Luminance above which pixels start to bloom.</summary>
        public static float BloomThreshold = 0.8f;

        /// <summary>Soft transition width below the threshold (avoids hard bloom edges).</summary>
        public static float BloomSoftKnee = 0.15f;

        /// <summary>Exposure multiplier applied before tonemapping (used when
        /// <see cref="AutoExposure"/> is off).</summary>
        public static float Exposure = 1.0f;

        /// <summary>Gamma correction applied after tonemapping (2.2 = standard sRGB).</summary>
        public static float Gamma = 2.2f;

        // ── Auto-exposure: measure the scene's average luminance every frame and adapt
        // the exposure so bright scenes don't blow out and dark scenes stay visible. ──
        /// <summary>Master switch: when on, the computed exposure overrides <see cref="Exposure"/>.</summary>
        public static bool AutoExposure = true;

        /// <summary>Lower clamp for the computed exposure (dark-scene brightening limit).</summary>
        public static float AutoExposureMinExposure = 0.2f;

        /// <summary>Upper clamp for the computed exposure (bright-scene darkening limit).</summary>
        public static float AutoExposureMaxExposure = 4f;

        /// <summary>Target average luminance (middle grey). Lower = darker average, higher = brighter.</summary>
        public static float AutoExposureTargetLuminance = 0.18f;

        /// <summary>Adaptation speed (per second) — how fast exposure follows scene brightness.</summary>
        public static float AutoExposureSpeed = 0.6f;

        /// <summary>Live computed exposure (write-only for the post-FX processor when
        /// auto-exposure is on; the panel shows it read-only). Not persisted.</summary>
        public static float CurrentAutoExposure = 1f;

        public static void ResetToDefaults()
        {
            Enabled = true;
            BloomIntensity = 0.05f;
            BloomThreshold = 0.8f;
            BloomSoftKnee = 0.15f;
            Exposure = 1.0f;
            Gamma = 2.2f;
            AutoExposure = true;
            AutoExposureMinExposure = 0.2f;
            AutoExposureMaxExposure = 4f;
            AutoExposureTargetLuminance = 0.18f;
            AutoExposureSpeed = 0.6f;
            CurrentAutoExposure = 1f;
        }

        /// <summary>Restore post-FX values from settings.json at startup (called by Program.cs).</summary>
        public static void Apply(SettingsData s)
        {
            if (s == null) return;
            try
            {
                Enabled = s.PostFxEnabled;
                BloomIntensity = Clamp(s.PostFxBloomIntensity, 0f, 4f);
                BloomThreshold = Clamp(s.PostFxBloomThreshold, 0f, 2f);
                BloomSoftKnee = Clamp(s.PostFxBloomSoftKnee, 0f, 1f);
                Exposure = Clamp(s.PostFxExposure, 0.1f, 8f);
                Gamma = Clamp(s.PostFxGamma, 0.4f, 4f);
                AutoExposure = s.PostFxAutoExposure;
                AutoExposureMinExposure = Clamp(s.PostFxAutoExposureMin, 0.05f, 8f);
                AutoExposureMaxExposure = Clamp(s.PostFxAutoExposureMax, 0.05f, 8f);
                AutoExposureTargetLuminance = Clamp(s.PostFxAutoExposureTarget, 0.01f, 1f);
                AutoExposureSpeed = Clamp(s.PostFxAutoExposureSpeed, 0.01f, 10f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PostFxSettings] Apply failed: {ex.Message}");
            }
        }

        /// <summary>Write all current post-FX values to settings.json.</summary>
        public static bool Persist()
        {
            try
            {
                var s = SettingsSave.Load();
                s.PostFxEnabled = Enabled;
                s.PostFxBloomIntensity = BloomIntensity;
                s.PostFxBloomThreshold = BloomThreshold;
                s.PostFxBloomSoftKnee = BloomSoftKnee;
                s.PostFxExposure = Exposure;
                s.PostFxGamma = Gamma;
                s.PostFxAutoExposure = AutoExposure;
                s.PostFxAutoExposureMin = AutoExposureMinExposure;
                s.PostFxAutoExposureMax = AutoExposureMaxExposure;
                s.PostFxAutoExposureTarget = AutoExposureTargetLuminance;
                s.PostFxAutoExposureSpeed = AutoExposureSpeed;
                SettingsSave.Save(s);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PostFxSettings] Persist failed: {ex.Message}");
                return false;
            }
        }

        private static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
    }
}
