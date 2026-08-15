using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Config
{
    /// <summary>JSON-serializable settings data — mirrors the settings UI values.</summary>
    public class SettingsData
    {
        public int Resolution { get; set; } = 2;       // 0=1920x1080, 1=1280x720, 2=2560x1440
        public bool Fullscreen { get; set; } = false;
        public bool BorderlessFullscreen { get; set; } = false;   // true = fullscreen windowed (borderless, uses work area)
        public bool VSync { get; set; } = false;
        public int ShadowQuality { get; set; } = 0;    // 0=Low, 1=Medium, 2=High, 3=Ultra
        /// <summary>Unified quality preset (0=Low, 1=Medium, 2=High, 3=Ultra) that
        /// drives MSAA samples + shadow quality + shadow filter together (QualitySettings).
        /// Defaults to Low so it runs clean on any GPU out of the box.</summary>
        public int QualityPreset { get; set; } = 0;
        public int OcclusionMode { get; set; } = 1;    // 0=Software, 1=HiZ, 2=OFF
        public int Fov { get; set; } = 70;
        public int MouseSensitivity { get; set; } = 4;  // 0=0.25×, 1=0.50×, 2=0.75×, 3=1.0×, 4=1.5×, 5=2.0×, 6=3.0×
        public bool InGameActive { get; set; } = true;   // F9 toggle — persists IDE input lock state

        // ── Viewport editor prefs (persisted so they survive restarts) ──
        /// <summary>Editor debug grid on/off (viewport toolbar "Grid").</summary>
        public bool ShowDebugGrid { get; set; } = false;
        /// <summary>Editor viewport shadows on/off (viewport toolbar "Shadow").
        /// When off, the CSM shadow pass is skipped and shadow maps read as fully lit.</summary>
        public bool ShowShadows { get; set; } = true;
        /// <summary>UI snap-to-grid on/off (viewport toolbar "Snap").</summary>
        public bool SnapEnabled { get; set; } = true;
        /// <summary>UI snap grid size in px (viewport toolbar grid combo).</summary>
        public float SnapGridSize { get; set; } = 20f;

        // ── Shadow Settings (tuned live in the IDE Shadow Settings panel; persisted so
        // they survive restarts — ShadowSettings.Persist()/Apply()) ──
        /// <summary>Cascade split distances in world units.</summary>
        public float ShadowCascade0 { get; set; } = 38f;
        public float ShadowCascade1 { get; set; } = 117f;
        public float ShadowCascade2 { get; set; } = 342f;
        /// <summary>Always-added fragment bias (all surfaces).</summary>
        public float ShadowConstantBias { get; set; } = 0.00005f;
        /// <summary>Slope-scaled fragment bias (main + terrain shaders). ~4 texels on steep faces.</summary>
        public float ShadowSlopeBias { get; set; } = 0.00008f;
        /// <summary>Minimum fragment bias (flat, light-facing surfaces). ~2.5 texels.</summary>
        public float ShadowMinBias { get; set; } = 0.00002f;
        /// <summary>Always-added fragment bias (gltf / GLB shader).</summary>
        public float ShadowGltfConstantBias { get; set; } = 0.000001f;
        /// <summary>Slope-scaled fragment bias (gltf / GLB shader). ~5 texels on steep faces.</summary>
        public float ShadowGltfSlopeBias { get; set; } = 0f;
        /// <summary>Minimum fragment bias (gltf / GLB shader). ~2.5 texels.</summary>
        public float ShadowGltfMinBias { get; set; } = 0f;
        /// <summary>Cascade blend width, as a fraction of the split distance.</summary>
        public float ShadowBlendRange { get; set; } = 0.5f;
        /// <summary>Normal bias — vertex extrusion when casting shadows (world units).</summary>
        public float ShadowNormalBias { get; set; } = 0f;
        /// <summary>Hard cap on the fragment bias' WORLD offset, per cascade (m) — kills
        /// peter-panning outline; cascade 0 tight, cascade 2 loose (1.0 m ≈ 2 far texels
        /// even at the game camera's 2800 m far plane keeps the far cascade acne-free).</summary>
        public float ShadowMaxWorldBias0 { get; set; } = 0.29f;
        public float ShadowMaxWorldBias1 { get; set; } = 0.43f;
        public float ShadowMaxWorldBias2 { get; set; } = 1.5f;
        /// <summary>Strength of the CSM LOD color overlay (L key debug tint), 0..1.</summary>
        public float ShadowCascadeOverlayAlpha { get; set; } = 0.36f;
        /// <summary>Depth-map texture filtering: true = LINEAR, false = NEAREST.</summary>
        public bool ShadowLinearMap { get; set; } = true;
        /// <summary>Shadow filter mode (0-9: PCF 16, Hard, PCF, PCSS...).</summary>
        public int ShadowFilterMode { get; set; } = 0;   // PCF 16 — minimal, clean-edged shadows

        // ── Post-Processing (AAA: bloom + ACES tonemapping + gamma; tuned live via
        // PostFxSettings, persisted here so the look survives restarts) ──
        public bool PostFxEnabled { get; set; } = true;
        public float PostFxBloomIntensity { get; set; } = 0.05f;
        public float PostFxBloomThreshold { get; set; } = 0.8f;
        public float PostFxBloomSoftKnee { get; set; } = 0.15f;
        public float PostFxExposure { get; set; } = 1.0f;
        public float PostFxGamma { get; set; } = 2.2f;
        public bool PostFxAutoExposure { get; set; } = true;
        public float PostFxAutoExposureMin { get; set; } = 0.2f;
        public float PostFxAutoExposureMax { get; set; } = 4f;
        public float PostFxAutoExposureTarget { get; set; } = 0.18f;
        public float PostFxAutoExposureSpeed { get; set; } = 0.6f;

        // ── Fog (Inspector "Fog" section) ──
        public bool FogEnabled { get; set; } = true;
        /// <summary>1 = Linear, 2 = Exponential, 3 = Exp2 + height blend.</summary>
        public int FogMode { get; set; } = 2;
        public bool FogUseSkyColor { get; set; } = true;
        public float FogColorR { get; set; } = 0f;
        public float FogColorG { get; set; } = 0f;
        public float FogColorB { get; set; } = 0f;
        public float FogDensity { get; set; } = 0.0034f;
        public float FogStart { get; set; } = 50f;
        public float FogEnd { get; set; } = 300f;
        public float FogHeight { get; set; } = 0f;
        public float FogHeightRange { get; set; } = 100f;
    }

    /// <summary>Loads/saves SettingsData to a JSON file next to the executable.</summary>
    public static class SettingsSave
    {
        private static readonly string FilePath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static SettingsData Load()
        {
            try
            {
                if (System.IO.File.Exists(FilePath))
                {
                    string json = System.IO.File.ReadAllText(FilePath);
                    var data = JsonSerializer.Deserialize<SettingsData>(json);
                    if (data != null)
                    {
                        Console.WriteLine($"[SettingsSave] Loaded from {FilePath}");
                        return data;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SettingsSave] Load failed: {ex.Message}");
            }
            return new SettingsData();
        }

        public static void Save(SettingsData data)
        {
            try
            {
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                System.IO.File.WriteAllText(FilePath, json);
                Console.WriteLine($"[SettingsSave] Saved to {FilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SettingsSave] Save failed: {ex.Message}");
            }
        }
    }
}
