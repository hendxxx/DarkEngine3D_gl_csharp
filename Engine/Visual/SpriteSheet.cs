using System.Numerics;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Manages a sprite sheet: slicing a single texture into frames,
/// storing per-frame metadata, and providing UV lookup for animation.
/// </summary>
public class SpriteSheet
{
    /// <summary>Display name (usually filename without extension).</summary>
    public string Name = "SpriteSheet";

    /// <summary>Absolute path to the source image file.</summary>
    public string ImagePath = "";

    /// <summary>Source image dimensions in pixels.</summary>
    public int ImageWidth;
    public int ImageHeight;

    /// <summary>Number of columns and rows in the grid.</summary>
    public int Columns = 1;
    public int Rows = 1;

    /// <summary>Individual frame size in pixels (uniform grid mode).</summary>
    public int FrameWidth = 64;
    public int FrameHeight = 64;

    /// <summary>Pixel padding between frames (for atlas with spacing).</summary>
    public int PaddingX;
    public int PaddingY;

    /// <summary>Pixel offset from top-left of image to first frame.</summary>
    public int OffsetX;
    public int OffsetY;

    /// <summary>Custom frame regions (non-uniform mode). Null = use uniform grid.</summary>
    public List<SpriteFrame>? CustomFrames;

    // ── Runtime ──
    public uint TextureId;

    /// <summary>
    /// Get total frame count based on grid dimensions.
    /// </summary>
    public int FrameCount => CustomFrames?.Count ?? (Columns * Rows);

    /// <summary>
    /// Get UV coordinates for a frame index (0-based, left-to-right, top-to-bottom).
    /// Returns (uvMin, uvMax) in screen-space UV (0,0=top-left, 1,1=bottom-right).
    /// Note: OpenGL UV Y is flipped (0,0=bottom-left), so vMin/vMax are swapped for GL.
    /// </summary>
    public (Vector2 uvMin, Vector2 uvMax) GetFrameUV(int frameIndex)
    {
        if (CustomFrames != null && frameIndex < CustomFrames.Count)
        {
            var f = CustomFrames[frameIndex];
            float uMin = (float)f.X / ImageWidth;
            float uMax = (float)(f.X + f.Width) / ImageWidth;
            // OpenGL: V is bottom-up, so flip Y
            float vMin = 1f - (float)(f.Y + f.Height) / ImageHeight;
            float vMax = 1f - (float)f.Y / ImageHeight;
            return (new Vector2(uMin, vMin), new Vector2(uMax, vMax));
        }

        int col = frameIndex % Columns;
        int row = frameIndex / Columns;

        float u0 = (float)(OffsetX + col * (FrameWidth + PaddingX)) / ImageWidth;
        float u1 = u0 + (float)FrameWidth / ImageWidth;
        // Flip V for OpenGL
        float v0 = 1f - (float)(OffsetY + (row + 1) * (FrameHeight + PaddingY)) / ImageHeight;
        float v1 = v0 + (float)FrameHeight / ImageHeight;

        return (new Vector2(u0, v0), new Vector2(u1, v1));
    }

    /// <summary>
    /// Get all frame UVs at once.
    /// </summary>
    public List<(Vector2 uvMin, Vector2 uvMax)> GetAllFrameUVs()
    {
        var result = new List<(Vector2, Vector2)>(FrameCount);
        for (int i = 0; i < FrameCount; i++)
            result.Add(GetFrameUV(i));
        return result;
    }

    /// <summary>
    /// Auto-detect grid dimensions from image size and frame size.
    /// </summary>
    public void AutoDetectGrid()
    {
        if (ImageWidth <= 0 || ImageHeight <= 0 || FrameWidth <= 0 || FrameHeight <= 0) return;
        Columns = (ImageWidth - OffsetX + PaddingX) / (FrameWidth + PaddingX);
        Rows = (ImageHeight - OffsetY + PaddingY) / (FrameHeight + PaddingY);
        if (Columns < 1) Columns = 1;
        if (Rows < 1) Rows = 1;
    }

    /// <summary>
    /// Create a uniform grid of custom frames from current settings.
    /// </summary>
    public void BakeUniformFrames()
    {
        CustomFrames = new List<SpriteFrame>();
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Columns; c++)
            {
                CustomFrames.Add(new SpriteFrame
                {
                    X = OffsetX + c * (FrameWidth + PaddingX),
                    Y = OffsetY + r * (FrameHeight + PaddingY),
                    Width = FrameWidth,
                    Height = FrameHeight,
                    Name = $"Frame {CustomFrames.Count}"
                });
            }
        }
    }

    /// <summary>
    /// Apply UV slice to a Sprite2D for a given frame.
    /// </summary>
    public void ApplyToSprite(Sprite2D sprite, int frameIndex)
    {
        var (uvMin, uvMax) = GetFrameUV(frameIndex);
        sprite.UVMin = uvMin;
        sprite.UVMax = uvMax;
        sprite.TextureId = TextureId;
        sprite.PixelWidth = FrameWidth;
        sprite.PixelHeight = FrameHeight;
    }

    // ── Serialization ──

    public SpriteSheetData ToData() => new()
    {
        Name = Name,
        ImagePath = ImagePath,
        ImageWidth = ImageWidth,
        ImageHeight = ImageHeight,
        Columns = Columns,
        Rows = Rows,
        FrameWidth = FrameWidth,
        FrameHeight = FrameHeight,
        PaddingX = PaddingX,
        PaddingY = PaddingY,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        CustomFrames = CustomFrames?.Select(f => new SpriteFrameData
        {
            X = f.X, Y = f.Y, Width = f.Width, Height = f.Height,
            Name = f.Name, AnchorX = f.AnchorX, AnchorY = f.AnchorY,
            HitboxX = f.HitboxX, HitboxY = f.HitboxY,
            HitboxW = f.HitboxW, HitboxH = f.HitboxH,
            Tags = f.Tags?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? null
        }).ToList()
    };

    public static SpriteSheet FromData(SpriteSheetData data) => new()
    {
        Name = data.Name,
        ImagePath = data.ImagePath,
        ImageWidth = data.ImageWidth,
        ImageHeight = data.ImageHeight,
        Columns = data.Columns,
        Rows = data.Rows,
        FrameWidth = data.FrameWidth,
        FrameHeight = data.FrameHeight,
        PaddingX = data.PaddingX,
        PaddingY = data.PaddingY,
        OffsetX = data.OffsetX,
        OffsetY = data.OffsetY,
        CustomFrames = data.CustomFrames?.Select(f => new SpriteFrame
        {
            X = f.X, Y = f.Y, Width = f.Width, Height = f.Height,
            Name = f.Name, AnchorX = f.AnchorX, AnchorY = f.AnchorY,
            HitboxX = f.HitboxX, HitboxY = f.HitboxY,
            HitboxW = f.HitboxW, HitboxH = f.HitboxH,
            Tags = f.Tags != null ? new Dictionary<string, string>(f.Tags) : new()
        }).ToList()
    };
}

/// <summary>
/// A single frame region within a sprite sheet.
/// </summary>
public class SpriteFrame
{
    public int X, Y;          // Pixel position in source image
    public int Width, Height; // Pixel dimensions
    public string Name = "";

    /// <summary>Pivot/anchor point within frame (0-1, 0=left/top, 1=right/bottom).</summary>
    public float AnchorX = 0.5f;
    public float AnchorY = 1f; // Default: bottom-center (feet anchor for characters)

    /// <summary>Per-frame hitbox override (in frame-local pixels, 0,0 = top-left).</summary>
    public int HitboxX, HitboxY, HitboxW, HitboxH;

    /// <summary>Per-frame custom data (tags, events, etc.).</summary>
    public Dictionary<string, string> Tags = new();
}

// ── Serialization DTOs ──

public class SpriteSheetData
{
    public string Name { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public int FrameWidth { get; set; } = 64;
    public int FrameHeight { get; set; } = 64;
    public int PaddingX { get; set; }
    public int PaddingY { get; set; }
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public List<SpriteFrameData>? CustomFrames { get; set; }
}

public class SpriteFrameData
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Name { get; set; } = "";
    public float AnchorX { get; set; } = 0.5f;
    public float AnchorY { get; set; } = 1f;
    public int HitboxX { get; set; }
    public int HitboxY { get; set; }
    public int HitboxW { get; set; }
    public int HitboxH { get; set; }
    /// <summary>Per-frame custom tags/events.</summary>
    public Dictionary<string, string>? Tags { get; set; }
}
