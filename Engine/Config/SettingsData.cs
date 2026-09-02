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
        public int ShadowQuality { get; set; } = 2;    // 0=Low, 1=Medium, 2=High, 3=Ultra
        /// <summary>Unified quality preset (0=Low, 1=Medium, 2=High, 3=Ultra) that
        /// drives MSAA samples + shadow quality + shadow filter together (QualitySettings).
        /// Defaults to Low so it runs clean on any GPU out of the box.</summary>
        public int QualityPreset { get; set; } = 0;
        public int OcclusionMode { get; set; } = 1;    // 0=Software, 1=HiZ, 2=OFF
        public int Fov { get; set; } = 70;
        public int MouseSensitivity { get; set; } = 4;  // 0=0.25×, 1=0.50×, 2=0.75×, 3=1.0×, 4=1.5×, 5=2.0×, 6=3.0×
        public bool InGameActive { get; set; } = false;   // F9 toggle — persists IDE input lock state
        /// <summary>When true, mouse cursor stays visible during in-game mode.</summary>
        public bool ShowCursorInGame { get; set; } = false;

        // ── IDE Font (editor panels font) ──
        public string IDEFontPath { get; set; } = "";   // empty = default font
        public float IDEFontSize { get; set; } = 20f;
        public float ViewportFontSize { get; set; } = 16f;

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
        /// <summary>Font size for the viewport toolbar buttons (px).</summary>
        public float ToolbarFontSize { get; set; } = 14f;

        // ── Transition defaults (persisted from TransitionPanel's "Apply Global Default") ──
        public int DefaultTransitionType { get; set; } = 0; // 0=Fade, 1=SlideLeft, 2=SlideRight
        public float DefaultTransitionDuration { get; set; } = 0.6f;
        public float DefaultTransitionColorR { get; set; } = 0f;
        public float DefaultTransitionColorG { get; set; } = 0f;
        public float DefaultTransitionColorB { get; set; } = 0f;
        public string DefaultTransitionEasing { get; set; } = "linear";
        public bool DefaultTransitionBlockInput { get; set; } = false;

        // ── Shadow Settings (tuned live in the IDE Shadow Settings panel; persisted so
        // they survive restarts — ShadowSettings.Persist()/Apply()) ──
        /// <summary>Cascade split distances in world units.</summary>
        public float ShadowCascade0 { get; set; } = 50f;
        public float ShadowCascade1 { get; set; } = 150f;
        public float ShadowCascade2 { get; set; } = 350f;
        /// <summary>Always-added fragment bias (all surfaces).</summary>
        public float ShadowConstantBias { get; set; } = 0.00005f;
        /// <summary>Slope-scaled fragment bias (main + terrain shaders) — Tutorial 16 slope
        /// formula: bias ∝ tan(acos(N·L)), so this coefficient ×~20 at grazing angles.</summary>
        public float ShadowSlopeBias { get; set; } = 0.00005f;
        /// <summary>Minimum fragment bias (flat, light-facing surfaces). ~2.5 texels.</summary>
        public float ShadowMinBias { get; set; } = 0.00001f;
        /// <summary>Always-added fragment bias (gltf / GLB shader).</summary>
        public float ShadowGltfConstantBias { get; set; } = 0.0005f;
        /// <summary>Slope-scaled fragment bias (gltf / GLB shader). ~5 texels on steep faces.</summary>
        public float ShadowGltfSlopeBias { get; set; } = 0.00005f;
        /// <summary>Minimum fragment bias (gltf / GLB shader). ~2.5 texels.</summary>
        public float ShadowGltfMinBias { get; set; } = 0.00001f;
        /// <summary>Cascade blend width, as a fraction of the split distance.</summary>
        public float ShadowBlendRange { get; set; } = 0.5f;
        /// <summary>Normal bias — vertex extrusion when casting shadows (world units).
        /// Primary anti-acne for self-shadowing (Tutorial 16's back-face culling can't be
        /// used — the shadow pass renders both faces for single-sided casters).</summary>
        public float ShadowNormalBias { get; set; } = 0.0f;
        /// <summary>Hard cap on the fragment bias' WORLD offset, per cascade (m) — kills
        /// peter-panning outline; roughly the cascade split distance, so the cap never
        /// binds before the cascade's own split in typical scenes.</summary>
        public float ShadowMaxWorldBias0 { get; set; } = 50.5f;
        public float ShadowMaxWorldBias1 { get; set; } = 150.5f;
        public float ShadowMaxWorldBias2 { get; set; } = 350.5f;
        /// <summary>Strength of the CSM LOD color overlay (L key debug tint), 0..1.</summary>
        public float ShadowCascadeOverlayAlpha { get; set; } = 0.5f;
        /// <summary>Depth-map texture filtering: true = LINEAR, false = NEAREST.</summary>
        public bool ShadowLinearMap { get; set; } = true;
        /// <summary>Shadow filter mode (0-9: PCF 16, Hard, PCF, PCSS...).</summary>
        public int ShadowFilterMode { get; set; } = 1;   // Hard — crisp, unfiltered shadow edges

        // ── Post-Processing (AAA: bloom + ACES tonemapping + gamma; tuned live via
        // PostFxSettings, persisted here so the look survives restarts) ──
        public bool PostFxEnabled { get; set; } = true;
        public float PostFxBloomIntensity { get; set; } = 0.8f;
        public float PostFxBloomThreshold { get; set; } = 0.45f;
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
                        return data;
                    }
                    Console.WriteLine($"[SettingsSave] Deserialize returned null!");
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SettingsSave] Save failed: {ex.Message}");
            }
        }
    }
}
