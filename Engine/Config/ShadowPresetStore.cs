using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Config
{
    /// <summary>One saved shadow configuration — a snapshot of every value the
    /// Shadow Settings panel edits (quality preset, cascade splits, all biases,
    /// blend range, normal bias, depth-map filtering and shadow filter mode).</summary>
    public class ShadowPresetData
    {
        public string Name { get; set; } = "Preset";
        public int Quality { get; set; } = ShadowSettings.QualityLow;
        public float[] CascadeLayer { get; set; } = [38f, 117f, 342f];
        public float ConstantBias { get; set; } = 0.00005f;
        public float SlopeBias { get; set; } = 0.00008f;   // ~4 texels steep (cascade-0 reference)
        public float MinBias { get; set; } = 0.00002f;     // ~2.5 texels flat
        public float GltfConstantBias { get; set; } = 0.000001f;
        public float GltfSlopeBias { get; set; } = 0f; // ~5 texels steep
        public float GltfMinBias { get; set; } = 0f;   // ~2.5 texels flat
        public float BlendRange { get; set; } = 0.5f;
        public float NormalBias { get; set; } = 0f; // vertex extrusion (world units, anti-acne)
        public float MaxWorldBias0 { get; set; } = 0.29f;  // cap on fragment bias WORLD offset (m), per cascade
        public float MaxWorldBias1 { get; set; } = 0.43f;
        public float MaxWorldBias2 { get; set; } = 1.5f;  // far cascade: keeps ~3-4 far texels of anti-acne bias
        /// <summary>Strength of the CSM LOD color overlay (L key debug tint), 0..1.</summary>
        public float CascadeOverlayAlpha { get; set; } = 0.36f;
        public bool LinearShadowMap { get; set; } = true;
        public int FilterMode { get; set; } = 0;
    }

    /// <summary>Persists named shadow presets to <c>shadow_presets.json</c> next to the
    /// executable (same folder as settings.json). Live values always live in
    /// <see cref="ShadowSettings"/> — a preset is just a named snapshot you can recall.</summary>
    public static class ShadowPresetStore
    {
        private static readonly string FilePath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "shadow_presets.json");

        /// <summary>Load all saved presets. Returns an empty list on missing/corrupt file.</summary>
        public static List<ShadowPresetData> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var list = JsonSerializer.Deserialize<List<ShadowPresetData>>(File.ReadAllText(FilePath));
                    if (list != null) return list;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShadowPresetStore] Load failed: {ex.Message}");
            }
            return [];
        }

        /// <summary>Write the full preset list to disk (atomic replace via temp file).</summary>
        public static void Save(List<ShadowPresetData> presets)
        {
            try
            {
                string tmp = FilePath + ".tmp";
                string json = JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, overwrite: true);
                Console.WriteLine($"[ShadowPresetStore] Saved {presets.Count} preset(s) to {FilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShadowPresetStore] Save failed: {ex.Message}");
            }
        }
    }
}
