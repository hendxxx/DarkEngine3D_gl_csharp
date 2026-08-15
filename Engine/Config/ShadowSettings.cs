using DarkEngine3D_gl_csharp.Engine.Inputs;
using System;

namespace DarkEngine3D_gl_csharp.Engine.Config
{
    /// <summary>
    /// Live-tunable shadow configuration driven by the IDE Shadow panel
    /// (Engine/IDE/Panels/ShadowPanel.cs).
    ///
    /// Bias / blend values are read every frame by <see cref="Visual.ShadowUniforms"/>
    /// and uploaded to the shaders, so they apply immediately with no rebuild.
    /// Cascade size / split changes bump <see cref="Version"/> — every CSM instance
    /// notices in <c>CSM.EnsureCurrent()</c> (called from UpdateMatrices) and rebuilds
    /// its depth maps automatically, so no scene wiring is needed.
    /// </summary>
    public static class ShadowSettings
    {
        public const int QualityLow = 0;
        public const int QualityMedium = 1;
        public const int QualityHigh = 2;
        public const int QualityUltra = 3;

        /// <summary>Selected cascade-size preset (index into ShadowPresets.CascadeSizes).
        /// Defaults to LOW so shadows work on any GPU out of the box.</summary>
        public static int Quality = QualityLow;

        /// <summary>Per-cascade shadow-map resolutions. Replaced wholesale when Quality changes.</summary>
        public static int[] CascadeSizes = ShadowPresets.CascadeSizes[QualityLow];

        /// <summary>Cascade split distances in world units (cascade i covers [prev, CascadeLayer[i]]).
        /// Tuned to the game scene's camera range (game far plane 2800 m).</summary>
        public static float[] CascadeLayer = [38f, 117f, 342f];

        // ── Fragment bias — main shader + terrain-editor shader ──
        // bias = max(ConstantBias + SlopeBias * (1 - N·L), MinBias)
        // Values are tuned for the IDE's typical scene (cascade-0 depth range ≈ 400 m,
        // texel ≈ 0.03 m): ~2.5 texels flat (Min), ~4 texels on steep faces (Slope).
        // The texel-proportional per-cascade scaling keeps this texel count at every
        // distance, so these defaults stay the ideal anti-acne/peter-panning balance.
        public static float ConstantBias = 0.00005f;   // always-added term (uniform across all surfaces)
        public static float SlopeBias = 0.00008f;   // extra bias on steep faces (slope-scaled)
        public static float MinBias = 0.00002f;     // safety floor for flat, light-facing surfaces

        // ── Fragment bias — gltf (PBR) shader (separate tuning values) ──
        public static float GltfConstantBias = 0.000001f;
        public static float GltfSlopeBias = 0f;   // ~5 texels on steep faces
        public static float GltfMinBias = 0f;     // ~2.5 texels flat

        /// <summary>Cascade blend width, as a fraction of the split distance (0.5 = 50%).</summary>
        public static float BlendRange = 0.5f;

        /// <summary>Normal bias — vertex extrusion when casting shadows (anti-acne).
        /// Extrusion happens in WORLD space (after the model transform — the shadow vertex
        /// shaders now extrude worldPos, not aPos), so this value is directly in world
        /// units for identity-model casters (game terrain, primitives); the terrain shadow
        /// passes multiply it by a size-based boost (EditorTerrainMesh / TerrainChunk) so
        /// large heightmap surfaces get proportionally more. Scaled per cascade by the
        /// texel ratio, but the scale is capped in CSM.LastTexelScale (8×) so the world
        /// extrusion never reaches meters in the far cascades (that would detach the
        /// shadow from the object — the bright "outline" peter-panning artifact).
        /// 0.02 m ≈ 1 texel of the near cascade in the editor viewport — the "standard
        /// 0.02 extrusion" the terrain-bias comment below references; anything much
        /// smaller lets geometry self-shadow acne through (the fragment bias alone
        /// can't cover texel-size errors on the casters' own surfaces).
        /// Tuned to 0 (extrusion off) for the game scene — the widened fragment bias
        /// caps (MaxWorldBias) handle anti-acne without moving the shadow silhouette.</summary>
        public static float NormalBias = 0f;

        /// <summary>Hard cap on the fragment bias' WORLD offset (bias_ndc × depthRange),
        /// per cascade. The texel-proportional per-cascade scaling keeps a constant TEXEL
        /// count, but in the far cascades a texel is ~0.5-1 m — so "2.5 texels" becomes a
        /// meters-scale world offset and shadows detach from their casters (bright outline).
        /// Capping the world offset directly bounds the peter-panning while keeping the
        /// near cascade tight: cascade 0 (0.15 m) stays tight (kills the thin outline at
        /// object bases), while cascade 1-2 are loosened (0.4 / 1.5 m) so they keep
        /// ~3-4 texels of anti-acne bias at distance — the previous 0.25 / 1.0 m caps left
        /// them at only ~1.7-2 texels, which is below the recommended 2-4 texel range and
        /// let acne speckle the terrain/objects in the mid-to-far cascades.
        /// Set individually in the panel.</summary>
        public static float[] MaxWorldBias = [0.29f, 0.43f, 1.5f];

        /// <summary>Depth-map texture filtering: LINEAR softens hard-shadow sampling slightly.</summary>
        public static bool LinearShadowMap = true;

        /// <summary>Strength of the CSM LOD color overlay (L key), 0..1. The cascade
        /// debug tint is mixed at this alpha — low keeps the scene readable, high makes
        /// the cascade bands pop. Shared by all three fragment shaders.</summary>
        public static float CascadeOverlayAlpha = 0.36f;

        /// <summary>Bumped when cascade sizes or split distances change → CSM rebuilds.</summary>
        public static int Version = 0;

        /// <summary>Bumped when only the texture filter changes (cheap TexParameteri, no rebuild).</summary>
        public static int FilterVersion = 0;

        /// <summary>Switch the cascade-size preset (Low/Medium/High/Ultra).</summary>
        public static void ApplyQuality(int quality)
        {
            quality = Math.Clamp(quality, QualityLow, QualityUltra);
            if (quality == Quality) return;

            Quality = quality;
            CascadeSizes = ShadowPresets.CascadeSizes[quality];
            Version++;
        }

        /// <summary>Set the three cascade split distances (world units) if they are strictly
        /// ascending. Returns true when applied — inverted/equal splits are rejected so the
        /// cascade perspective projections never degenerate. Shared by Apply(SettingsData),
        /// ApplyPreset and the panel's split drags (via ApplyCascadeLayer).</summary>
        private static bool TrySetCascadeLayer(float c0, float c1, float c2)
        {
            if (!(c1 > c0 && c2 > c1)) return false;
            CascadeLayer = [c0, c1, c2];
            Version++;
            return true;
        }

        /// <summary>Apply the three cascade split distances (world units).
        /// Rejects non-ascending splits — inverted cascade projections would be degenerate.</summary>
        public static void ApplyCascadeLayer(float[] layer)
        {
            if (layer == null || layer.Length != CascadeLayer.Length) return;

            for (int i = 1; i < layer.Length; i++)
                if (layer[i] <= layer[i - 1]) return;

            bool changed = false;
            for (int i = 0; i < CascadeLayer.Length; i++)
            {
                if (MathF.Abs(CascadeLayer[i] - layer[i]) > 0.001f) changed = true;
                CascadeLayer[i] = layer[i];
            }
            if (changed) Version++;
        }

        /// <summary>Restore the same values the shaders ship with as their GLSL defaults.</summary>
        public static void ResetToDefaults()
        {
            Quality = QualityLow;
            CascadeSizes = ShadowPresets.CascadeSizes[QualityLow];
            CascadeLayer = [38f, 117f, 342f];
            Keyboard.SetShadowFilterMode(0);   // PCF 16 — minimal-quality default
            ConstantBias = 0.00005f;
            SlopeBias = 0.00008f;
            MinBias = 0.00002f;
            GltfConstantBias = 0.000001f;
            GltfSlopeBias = 0f;
            GltfMinBias = 0f;
            BlendRange = 0.5f;
            NormalBias = 0f;
            MaxWorldBias = [0.29f, 0.43f, 1.5f];
            CascadeOverlayAlpha = 0.36f;
            LinearShadowMap = true;
            Version++;
            FilterVersion++;
        }

        /// <summary>Snapshot the current live shadow values into a named preset.</summary>
        public static ShadowPresetData CapturePreset(string name)
        {
            return new ShadowPresetData
            {
                Name = name,
                Quality = Quality,
                CascadeLayer = (float[])CascadeLayer.Clone(),
                ConstantBias = ConstantBias,
                SlopeBias = SlopeBias,
                MinBias = MinBias,
                GltfConstantBias = GltfConstantBias,
                GltfSlopeBias = GltfSlopeBias,
                GltfMinBias = GltfMinBias,
                BlendRange = BlendRange,
                NormalBias = NormalBias,
                MaxWorldBias0 = MaxWorldBias[0],
                MaxWorldBias1 = MaxWorldBias[1],
                MaxWorldBias2 = MaxWorldBias[2],
                CascadeOverlayAlpha = CascadeOverlayAlpha,
                LinearShadowMap = LinearShadowMap,
                FilterMode = Keyboard.GetIsHardShadow(),
            };
        }

        /// <summary>Apply a saved preset to the live shadow settings (quality, splits,
        /// every bias, filter). Bumps Version/FilterVersion so CSM instances rebuild.</summary>
        public static void ApplyPreset(ShadowPresetData p)
        {
            if (p == null) return;

            try
            {
                ApplyQuality(p.Quality);

                // Only accept ascending cascade splits (inverted splits are degenerate).
                if (p.CascadeLayer is { Length: 3 } layer)
                    TrySetCascadeLayer(layer[0], layer[1], layer[2]);

                ConstantBias = p.ConstantBias;
                SlopeBias = p.SlopeBias;
                MinBias = p.MinBias;
                GltfConstantBias = p.GltfConstantBias;
                GltfSlopeBias = p.GltfSlopeBias;
                GltfMinBias = p.GltfMinBias;
                BlendRange = p.BlendRange;
                NormalBias = p.NormalBias;
                MaxWorldBias = [p.MaxWorldBias0, p.MaxWorldBias1, p.MaxWorldBias2];
                CascadeOverlayAlpha = p.CascadeOverlayAlpha;
                LinearShadowMap = p.LinearShadowMap;
                Keyboard.SetShadowFilterMode(p.FilterMode);

                // Make sure already-created CSM instances refresh their depth-map filter.
                FilterVersion++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShadowSettings] ApplyPreset failed: {ex.Message}");
            }
        }

        /// <summary>Write all current shadow values to settings.json so the tuning survives
        /// restarts. Called by the IDE Shadow panel whenever a value changes.
        /// Returns true when the write succeeded (used for UI feedback).</summary>
        public static bool Persist()
        {
            try
            {
                var s = SettingsSave.Load();
                s.ShadowQuality = Quality;
                s.ShadowCascade0 = CascadeLayer[0];
                s.ShadowCascade1 = CascadeLayer[1];
                s.ShadowCascade2 = CascadeLayer[2];
                s.ShadowConstantBias = ConstantBias;
                s.ShadowSlopeBias = SlopeBias;
                s.ShadowMinBias = MinBias;
                s.ShadowGltfConstantBias = GltfConstantBias;
                s.ShadowGltfSlopeBias = GltfSlopeBias;
                s.ShadowGltfMinBias = GltfMinBias;
                s.ShadowBlendRange = BlendRange;
                s.ShadowNormalBias = NormalBias;
                s.ShadowMaxWorldBias0 = MaxWorldBias[0];
                s.ShadowMaxWorldBias1 = MaxWorldBias[1];
                s.ShadowMaxWorldBias2 = MaxWorldBias[2];
                s.ShadowCascadeOverlayAlpha = CascadeOverlayAlpha;
                s.ShadowLinearMap = LinearShadowMap;
                s.ShadowFilterMode = Keyboard.GetIsHardShadow();
                SettingsSave.Save(s);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShadowSettings] Persist failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Restore all shadow values from settings.json at startup (called by
        /// Program.cs after SettingsSave.Load). Quality is applied via ApplyQuality, so
        /// invalid persisted values are clamped and CSM instances pick up the change via
        /// ShadowSettings.Version.</summary>
        public static void Apply(SettingsData s)
        {
            if (s == null) return;

            try
            {
                ApplyQuality(s.ShadowQuality);

                // Only accept ascending cascade splits (inverted splits are degenerate).
                TrySetCascadeLayer(s.ShadowCascade0, s.ShadowCascade1, s.ShadowCascade2);

                ConstantBias = s.ShadowConstantBias;
                SlopeBias = s.ShadowSlopeBias;
                MinBias = s.ShadowMinBias;
                GltfConstantBias = s.ShadowGltfConstantBias;
                GltfSlopeBias = s.ShadowGltfSlopeBias;
                GltfMinBias = s.ShadowGltfMinBias;
                BlendRange = s.ShadowBlendRange;
                NormalBias = s.ShadowNormalBias;
                MaxWorldBias = [s.ShadowMaxWorldBias0, s.ShadowMaxWorldBias1, s.ShadowMaxWorldBias2];
                CascadeOverlayAlpha = s.ShadowCascadeOverlayAlpha;
                LinearShadowMap = s.ShadowLinearMap;
                Keyboard.SetShadowFilterMode(s.ShadowFilterMode);

                // Make sure already-created CSM instances refresh their depth-map filter.
                FilterVersion++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShadowSettings] Apply failed: {ex.Message}");
            }
        }
    }
}
