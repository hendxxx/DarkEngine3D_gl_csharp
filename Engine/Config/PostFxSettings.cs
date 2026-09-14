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
        private static bool _enabled = true;
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled != value)
                {
                    var st = new System.Diagnostics.StackTrace(1, true);
                    Console.WriteLine($"[PostFxSettings] Enabled: {_enabled} → {value} at {st.GetFrame(0)?.GetMethod()?.DeclaringType?.Name}.{st.GetFrame(0)?.GetMethod()?.Name}:{st.GetFrame(0)?.GetFileLineNumber()}");
                }
                _enabled = value;
            }
        }

        /// <summary>Bloom add-back strength (0 = off).</summary>
        public static float BloomIntensity = 1.0f;

        /// <summary>Luminance above which pixels start to bloom.</summary>
        public static float BloomThreshold = 0.5f;

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

        // ── Depth of Field: everything outside a screen-space focus circle blurs. ──
        // The focus point is normalized screen coordinates (0..1, Y up from bottom-left
        // to match the FBO/viewport convention) so it works for 2D sidescrollers and 3D
        // alike; the sliders live in the Post FX panel.
        /// <summary>Master switch for the DoF pass.</summary>
        public static bool DofEnabled = false;
        /// <summary>Focus point X, normalized screen width (0 = left, 0.5 = center).</summary>
        public static float DofFocusX = 0.5f;
        /// <summary>Focus point Y, normalized screen height (0 = bottom, 1 = top).</summary>
        public static float DofFocusY = 0.45f;
        /// <summary>Sharp radius around the focus point, as a fraction of screen HEIGHT.</summary>
        public static float DofRadius = 0.25f;
        /// <summary>Blur ramp width outside the radius (fraction of screen height).</summary>
        public static float DofFeather = 0.35f;
        /// <summary>Maximum blur disk radius at full defocus, in pixels. 12 = clearly
        /// visible; raise for a heavy tilt-shift look.</summary>
        public static float DofMaxBlur = 12f;
        /// <summary>What the focus circle tracks: 0 = manual sliders, 1 = Player2D,
        /// 2 = hovered map tile, 3 = hovered object, 4 = editor selection.
        /// See <see cref="Engine.Visual.PostProcessing.DofFocusTarget"/>.</summary>
        public static int DofFocusTarget = 0;
        /// <summary>How fast the focus glides after a moving target (1 = lazy, 30 = welded).</summary>
        public static float DofFollowSpeed = 14f;

        // ── Optional sprite-silhouette focus shape: instead of a circle, the sharp
        // region is the exact alpha silhouette of Player sprites or Sprite2D objects.
        /// <summary>DoF focus shape / mask mode: 0=Geometric Circle, 1=Player Sprite, 2=Sprite2D Layer, 3=Player + Sprite2D Layer, 4=Hybrid (Circle + Player).</summary>
        public static int DofFocusShape = 0;
        /// <summary>Invert DoF mask: false=subject sharp / background blurred, true=subject blurred / background sharp.</summary>
        public static bool DofInvertMask = false;
        /// <summary>Master switch for the sprite-shape focus mask (true when DofFocusShape != 0).</summary>
        public static bool DofSpriteShapeEnable = false;
        /// <summary>Which Sprite2D Render Layer forms the sharp silhouette (−10..10 slider).</summary>
        public static int DofSpriteShapeLayer = 0;
        /// <summary>Silhouette growth in mask texels (0 = exact alpha, 1–4 = softer edge).</summary>
        public static float DofSpriteExpandPx = 2f;
        /// <summary>Raises sprite alpha coverage so semi-transparent pixels stay sharp.</summary>
        public static float DofSpriteAlphaBias = 0.05f;
        /// <summary>Debug view: blur the WHOLE scene except the sprite silhouette.</summary>
        public static bool DofSpriteMaskOnly = false;

        /// <summary>Live computed exposure (write-only for the post-FX processor when
        /// auto-exposure is on; the panel shows it read-only). Not persisted.</summary>
        public static float CurrentAutoExposure = 1f;

        public static void ResetToDefaults()
        {
            Enabled = true;
            BloomIntensity = 1.0f;
            BloomThreshold = 0.5f;
            BloomSoftKnee = 0.15f;
            Exposure = 1.0f;
            Gamma = 2.2f;
            AutoExposure = true;
            AutoExposureMinExposure = 0.2f;
            AutoExposureMaxExposure = 4f;
            AutoExposureTargetLuminance = 0.18f;
            AutoExposureSpeed = 0.6f;
            CurrentAutoExposure = 1f;
            DofEnabled = false;
            DofFocusX = 0.5f;
            DofFocusY = 0.45f;
            DofRadius = 0.25f;
            DofFeather = 0.35f;
            DofMaxBlur = 12f;
            DofFocusTarget = 0;
            DofFollowSpeed = 14f;
            DofFocusShape = 0;
            DofInvertMask = false;
            DofSpriteShapeEnable = false;
            DofSpriteShapeLayer = 0;
            DofSpriteExpandPx = 2f;
            DofSpriteAlphaBias = 0.05f;
            DofSpriteMaskOnly = false;
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
                DofEnabled = s.PostFxDofEnabled;
                DofFocusX = Clamp(s.PostFxDofFocusX, 0f, 1f);
                DofFocusY = Clamp(s.PostFxDofFocusY, 0f, 1f);
                DofRadius = Clamp(s.PostFxDofRadius, 0.01f, 1f);
                DofFeather = Clamp(s.PostFxDofFeather, 0.01f, 1f);
                DofMaxBlur = Clamp(s.PostFxDofMaxBlur, 0f, 24f);
                DofFocusTarget = Math.Clamp(s.PostFxDofFocusTarget, 0, 4);
                DofFollowSpeed = Clamp(s.PostFxDofFollowSpeed, 1f, 30f);
                DofFocusShape = Math.Clamp(s.PostFxDofFocusShape, 0, 4);
                if (DofFocusShape == 0 && s.PostFxDofSpriteShapeEnable)
                    DofFocusShape = s.PostFxDofSpriteMaskOnly ? 2 : 4;
                DofInvertMask = s.PostFxDofInvertMask;
                DofSpriteShapeEnable = (DofFocusShape != 0) || s.PostFxDofSpriteShapeEnable;
                DofSpriteShapeLayer = Math.Clamp(s.PostFxDofSpriteShapeLayer, -10, 10);
                DofSpriteExpandPx = Clamp(s.PostFxDofSpriteExpandPx, 0f, 6f);
                DofSpriteAlphaBias = Clamp(s.PostFxDofSpriteAlphaBias, 0f, 0.5f);
                DofSpriteMaskOnly = s.PostFxDofSpriteMaskOnly;
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
                s.PostFxDofEnabled = DofEnabled;
                s.PostFxDofFocusX = DofFocusX;
                s.PostFxDofFocusY = DofFocusY;
                s.PostFxDofRadius = DofRadius;
                s.PostFxDofFeather = DofFeather;
                s.PostFxDofMaxBlur = DofMaxBlur;
                s.PostFxDofFocusTarget = DofFocusTarget;
                s.PostFxDofFollowSpeed = DofFollowSpeed;
                s.PostFxDofFocusShape = DofFocusShape;
                s.PostFxDofInvertMask = DofInvertMask;
                s.PostFxDofSpriteShapeEnable = (DofFocusShape != 0) || DofSpriteShapeEnable;
                s.PostFxDofSpriteShapeLayer = DofSpriteShapeLayer;
                s.PostFxDofSpriteExpandPx = DofSpriteExpandPx;
                s.PostFxDofSpriteAlphaBias = DofSpriteAlphaBias;
                s.PostFxDofSpriteMaskOnly = DofSpriteMaskOnly;
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
