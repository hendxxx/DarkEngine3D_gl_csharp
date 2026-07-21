namespace DarkEngine3D_gl_csharp.Engine.Config;

/// <summary>
/// 🛠️ FIX #9: Shared resolution constants extracted from Program.cs and MainMenuScene.cs.
/// Both files now reference this single source of truth instead of duplicating the
/// resolution switch logic and magic numbers.
/// </summary>
public static class ResolutionConfig
{
    /// <summary>A single resolution entry with width, height, and display labels.</summary>
    public readonly struct ResInfo
    {
        public readonly int Width, Height;
        public readonly string Label, CompactLabel;

        public ResInfo(int w, int h, string label)
        {
            Width = w; Height = h;
            Label = label;
            CompactLabel = $"{w}x{h}";
        }
    }

    /// <summary>All supported resolutions. Index matches the settings.json "Resolution" value.</summary>
    public static readonly ResInfo[] Resolutions =
    [
        new(1920, 1080, "1920 x 1080"),
        new(1280, 720,  "1280 x 720"),
        new(2560, 1440, "2560 x 1440"),
    ];

    /// <summary>Number of supported resolutions.</summary>
    public const int ResolutionCount = 3;

    /// <summary>Default resolution index (0 = 1920x1080).</summary>
    public const int DefaultResolutionIndex = 0;

    /// <summary>Get width for a resolution index. Clamped to valid range.</summary>
    public static int GetWidth(int index)
    {
        int clamped = Math.Clamp(index, 0, Resolutions.Length - 1);
        return Resolutions[clamped].Width;
    }

    /// <summary>Get height for a resolution index. Clamped to valid range.</summary>
    public static int GetHeight(int index)
    {
        int clamped = Math.Clamp(index, 0, Resolutions.Length - 1);
        return Resolutions[clamped].Height;
    }
}
