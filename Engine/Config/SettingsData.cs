using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Config
{
    /// <summary>JSON-serializable settings data — mirrors the settings UI values.</summary>
    public class SettingsData
    {
        public int Resolution { get; set; } = 0;       // 0=1920x1080, 1=1280x720, 2=2560x1440
        public bool Fullscreen { get; set; } = true;
        public bool VSync { get; set; } = false;
        public int ShadowQuality { get; set; } = 3;    // 0=Low, 1=Medium, 2=High, 3=Ultra
        public int OcclusionMode { get; set; } = 1;    // 0=Software, 1=HiZ, 2=OFF
        public int Fov { get; set; } = 60;
        public int MouseSensitivity { get; set; } = 3;  // 0=0.25×, 1=0.50×, 2=0.75×, 3=1.0×, 4=1.5×, 5=2.0×, 6=3.0×
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
