using System.Numerics;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Multi-layer parallax scrolling background for 2D sidescroller.
/// Each layer scrolls at a different speed relative to the camera.
/// Supports repeating tiles, day/night tinting, and depth-based fog.
/// </summary>
public class ParallaxBackground2D
{
    public string Name = "ParallaxBG";

    /// <summary>Background layers from back (farthest) to front (nearest).</summary>
    public List<ParallaxLayer> Layers = new();

    /// <summary>Global tint applied on top of layer tints (for day/night).</summary>
    public Vector4 GlobalTint = Vector4.One;

    /// <summary>Camera reference (set by renderer).</summary>
    public Vector2 CameraPosition;

    /// <summary>
    /// Update all layers based on camera position.
    /// </summary>
    public void Update(float cameraX, float cameraY)
    {
        CameraPosition = new Vector2(cameraX, cameraY);
        foreach (var layer in Layers)
        {
            if (!layer.IsEnabled) continue;
            layer.UpdateOffset(cameraX, cameraY);
        }
    }

    /// <summary>
    /// Create a default parallax setup for a sidescroller.
    /// </summary>
    public static ParallaxBackground2D CreateDefault() => new()
    {
        Layers =
        [
            new() { Name = "Sky",       ScrollFactor = 0.0f,  Depth = 100f, TileX = true, TileY = false },
            new() { Name = "Far Mountains", ScrollFactor = 0.1f,  Depth = 80f,  TileX = true, TileY = false },
            new() { Name = "Hills",     ScrollFactor = 0.3f,  Depth = 60f,  TileX = true, TileY = false },
            new() { Name = "Trees",     ScrollFactor = 0.5f,  Depth = 40f,  TileX = true, TileY = false },
            new() { Name = "Near Ground", ScrollFactor = 0.8f,  Depth = 20f,  TileX = true, TileY = false },
            new() { Name = "Foreground", ScrollFactor = 1.0f,  Depth = 0f,   TileX = true, TileY = false },
        ]
    };

    // ── Serialization ──

    public ParallaxBG2DData ToData() => new()
    {
        Name = Name,
        GlobalTint = new float[] { GlobalTint.X, GlobalTint.Y, GlobalTint.Z, GlobalTint.W },
        Layers = Layers.Select(l => l.ToData()).ToList()
    };

    public static ParallaxBackground2D FromData(ParallaxBG2DData data)
    {
        return new ParallaxBackground2D
        {
            Name = data.Name,
            GlobalTint = data.GlobalTint != null
                ? new Vector4(data.GlobalTint[0], data.GlobalTint[1], data.GlobalTint[2], data.GlobalTint[3])
                : Vector4.One,
            Layers = data.Layers?.Select(l => ParallaxLayer.FromData(l)).ToList() ?? new()
        };
    }
}

/// <summary>
/// A single parallax layer with its own scroll factor and texture.
/// </summary>
public class ParallaxLayer
{
    public string Name = "Layer";
    public bool IsEnabled = true;

    /// <summary>How much this layer scrolls relative to camera. 0=static, 1= same as camera.</summary>
    public float ScrollFactor = 0.5f;

    /// <summary>Depth value for rendering order (higher = farther back).</summary>
    public float Depth = 50f;

    /// <summary>Texture file path.</summary>
    public string TexturePath = "";

    /// <summary>GPU texture ID (set at runtime).</summary>
    public uint TextureId;

    /// <summary>Layer dimensions in pixels.</summary>
    public float ImageWidth = 1920f;
    public float ImageHeight = 1080f;

    /// <summary>Vertical offset from camera (parallax Y offset).</summary>
    public float VerticalOffset;

    /// <summary>Repeat horizontally when scrolling past edge.</summary>
    public bool TileX = true;

    /// <summary>Repeat vertically.</summary>
    public bool TileY;

    /// <summary>Layer tint color (RGBA).</summary>
    public Vector4 Tint = Vector4.One;

    /// <summary>Opacity (0-1).</summary>
    public float Opacity = 1f;

    // ── Runtime ──

    /// <summary>Current world-space offset applied to this layer.</summary>
    public Vector2 CurrentOffset;

    /// <summary>
    /// Compute the layer's world-space offset based on camera position.
    /// </summary>
    public void UpdateOffset(float cameraX, float cameraY)
    {
        CurrentOffset = new Vector2(
            -cameraX * ScrollFactor,
            VerticalOffset - cameraY * ScrollFactor * 0.5f // Reduced Y scroll for typical side-scroller
        );
    }

    /// <summary>
    /// Get the UV tile offset for wrapping (when tile mode is enabled).
    /// </summary>
    public float GetTileOffsetX(float cameraX)
    {
        if (!TileX || ImageWidth <= 0) return 0f;
        float scrolled = cameraX * ScrollFactor;
        float tileWidth = ImageWidth;
        float offset = scrolled % tileWidth;
        return offset / tileWidth;
    }

    // ── Serialization ──

    public ParallaxLayerData ToData() => new()
    {
        Name = Name,
        IsEnabled = IsEnabled,
        ScrollFactor = ScrollFactor,
        Depth = Depth,
        TexturePath = TexturePath,
        ImageWidth = ImageWidth,
        ImageHeight = ImageHeight,
        VerticalOffset = VerticalOffset,
        TileX = TileX,
        TileY = TileY,
        Tint = new float[] { Tint.X, Tint.Y, Tint.Z, Tint.W },
        Opacity = Opacity
    };

    public static ParallaxLayer FromData(ParallaxLayerData data)
    {
        return new ParallaxLayer
        {
            Name = data.Name,
            IsEnabled = data.IsEnabled,
            ScrollFactor = data.ScrollFactor,
            Depth = data.Depth,
            TexturePath = data.TexturePath,
            ImageWidth = data.ImageWidth,
            ImageHeight = data.ImageHeight,
            VerticalOffset = data.VerticalOffset,
            TileX = data.TileX,
            TileY = data.TileY,
            Tint = data.Tint != null
                ? new Vector4(data.Tint[0], data.Tint[1], data.Tint[2], data.Tint[3])
                : Vector4.One,
            Opacity = data.Opacity
        };
    }
}

// ── Serialization DTOs ──

public class ParallaxBG2DData
{
    public string Name { get; set; } = "ParallaxBG";
    public float[]? GlobalTint { get; set; }
    public List<ParallaxLayerData>? Layers { get; set; }
}

public class ParallaxLayerData
{
    public string Name { get; set; } = "Layer";
    public bool IsEnabled { get; set; } = true;
    public float ScrollFactor { get; set; } = 0.5f;
    public float Depth { get; set; } = 50f;
    public string TexturePath { get; set; } = "";
    public float ImageWidth { get; set; } = 1920f;
    public float ImageHeight { get; set; } = 1080f;
    public float VerticalOffset { get; set; }
    public bool TileX { get; set; }
    public bool TileY { get; set; }
    public float[]? Tint { get; set; }
    public float Opacity { get; set; } = 1f;
}
