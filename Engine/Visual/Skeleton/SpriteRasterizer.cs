using StbImageSharp;

namespace DarkEngine3D_gl_csharp.Engine.Visual.Skeleton;

/// <summary>A source region cropped out of a sprite sheet, ready to sample.</summary>
public sealed class RegionImage
{
    public int Width;
    public int Height;
    public byte[] Rgba = Array.Empty<byte>();
}

/// <summary>
/// CPU rasterizer for the skeleton bake: composites <see cref="PoseSlot"/> regions into a
/// straight-alpha RGBA buffer in WORLD PIXELS (design space). No OpenGL involved, so the
/// bake produces exactly the pixels the game will later draw from the atlas.
///
/// Sampling is bilinear with clamped edges; edges are hard (no coverage AA) because the
/// atlas frames are re-sampled by the GPU at arbitrary scales anyway, and the pixel art
/// this engine targets is drawn 1:1 inside the master box.
///
/// Sheet <see cref="SpriteSheet.FlipX"/>/<see cref="SpriteSheet.FlipY"/> are NOT applied
/// here: they are render-time normalization for the sprite draw path, and an attachment
/// can express a mirror through its own negative scale. Applying them twice would mirror
/// baked rig art the author did not ask for.
/// </summary>
public sealed class SpriteRasterizer
{
    private readonly Func<string, SpriteSheet?> _sheetLookup;
    private readonly Dictionary<string, SheetImage?> _sheets = new(StringComparer.OrdinalIgnoreCase);

    private sealed class SheetImage
    {
        public int Width;
        public int Height;
        public byte[] Rgba = Array.Empty<byte>();
    }

    public SpriteRasterizer(Func<string, SpriteSheet?> sheetLookup)
        => _sheetLookup = sheetLookup ?? throw new ArgumentNullException(nameof(sheetLookup));

    /// <summary>Drop every decoded sheet (the sheet list changed under the editor).</summary>
    public void InvalidateSheets() => _sheets.Clear();

    /// <summary>Crop one attachment's frame out of its source sheet. Returns null when the
    /// sheet/frame/pixels are unavailable — the caller skips the slot instead of failing a
    /// whole bake over one missing attachment.</summary>
    public RegionImage? GetRegion(Attachment2D attachment)
    {
        if (attachment == null || string.IsNullOrEmpty(attachment.SheetName)) return null;
        var sheet = GetSheet(attachment.SheetName);
        if (sheet == null) return null;

        var meta = GetSheetMeta(attachment.SheetName);
        if (meta == null || !TryGetFrameRect(meta, attachment.FrameIndex, out int fx, out int fy, out int fw, out int fh))
            return null;
        if (fw <= 0 || fh <= 0) return null;

        var region = new RegionImage { Width = fw, Height = fh, Rgba = new byte[fw * fh * 4] };
        for (int y = 0; y < fh; y++)
        {
            int srcY = fy + y;
            if (srcY < 0 || srcY >= sheet.Height) continue;
            int srcRow = (srcY * sheet.Width + fx) * 4;
            int dstRow = y * fw * 4;
            int copy = Math.Min(fw, sheet.Width - fx) * 4;
            if (copy <= 0) continue;
            Buffer.BlockCopy(sheet.Rgba, srcRow, region.Rgba, dstRow, copy);
        }
        return region;
    }

    /// <summary>World-space bounds of one placed region (y-up): left, bottom, right, top.</summary>
    public (float Left, float Bottom, float Right, float Top) MeasureSlot(PoseSlot slot, RegionImage region)
    {
        var corners = Corners(slot, region);
        float left = float.MaxValue, bottom = float.MaxValue, right = float.MinValue, top = float.MinValue;
        foreach (var (x, y) in corners)
        {
            if (x < left) left = x;
            if (x > right) right = x;
            if (y < bottom) bottom = y;
            if (y > top) top = y;
        }
        return (left, bottom, right, top);
    }

    /// <summary>
    /// Draw one slot into <paramref name="dest"/> (RGBA, row 0 = TOP). The canvas maps world
    /// x ∈ [worldLeft, worldLeft + destW] and world y ∈ [worldTop - destH, worldTop].
    /// </summary>
    public void DrawSlot(PoseSlot slot, RegionImage region, byte[] dest, int destW, int destH,
        float worldLeft, float worldTop)
    {
        if (region == null || slot?.Attachment == null) return;
        if (!slot.World.TryInvert(out var inverse)) return;

        var (left, bottom, right, top) = MeasureSlot(slot, region);
        int x0 = Math.Max(0, (int)MathF.Floor(left - worldLeft));
        int x1 = Math.Min(destW - 1, (int)MathF.Ceiling(right - worldLeft));
        int y0 = Math.Max(0, (int)MathF.Floor(worldTop - top));
        int y1 = Math.Min(destH - 1, (int)MathF.Ceiling(worldTop - bottom));

        float pivotX = slot.Attachment.PivotX;
        float pivotY = slot.Attachment.PivotY;
        float w = region.Width;
        float h = region.Height;
        float tintR = Math.Clamp(slot.R, 0f, 1f);
        float tintG = Math.Clamp(slot.G, 0f, 1f);
        float tintB = Math.Clamp(slot.B, 0f, 1f);
        float tintA = Math.Clamp(slot.A, 0f, 1f);
        if (tintA <= 0f) return;

        for (int py = y0; py <= y1; py++)
        {
            // Pixel center in world space (y-up).
            float wy = worldTop - (py + 0.5f);
            for (int px = x0; px <= x1; px++)
            {
                float wx = worldLeft + px + 0.5f;
                var (lx, ly) = inverse.TransformPoint(wx, wy);
                float u = (lx + pivotX * w) / w;
                float v = (ly + (1f - pivotY) * h) / h;
                if (u < 0f || u >= 1f || v < 0f || v >= 1f) continue;

                // v = 0 is the BOTTOM of the region; image row 0 is the top.
                SampleBilinear(region, u, 1f - v, out var sr, out var sg, out var sb, out var sa);
                if (sa <= 0) continue;
                sr *= tintR; sg *= tintG; sb *= tintB; sa *= tintA;

                int di = (py * destW + px) * 4;
                BlendOver(dest, di, sr, sg, sb, sa);
            }
        }
    }

    /// <summary>Straight-alpha source-over.</summary>
    private static void BlendOver(byte[] dest, int i, float sr, float sg, float sb, float sa)
    {
        float dr = dest[i] / 255f;
        float dg = dest[i + 1] / 255f;
        float db = dest[i + 2] / 255f;
        float da = dest[i + 3] / 255f;

        float outA = sa + da * (1f - sa);
        if (outA <= 0f)
        {
            dest[i] = dest[i + 1] = dest[i + 2] = dest[i + 3] = 0;
            return;
        }
        float outR = (sr * sa + dr * da * (1f - sa)) / outA;
        float outG = (sg * sa + dg * da * (1f - sa)) / outA;
        float outB = (sb * sa + db * da * (1f - sa)) / outA;
        dest[i] = ToByte(outR);
        dest[i + 1] = ToByte(outG);
        dest[i + 2] = ToByte(outB);
        dest[i + 3] = ToByte(outA);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static void SampleBilinear(RegionImage img, float u, float v,
        out float r, out float g, out float b, out float a)
    {
        float fx = Math.Clamp(u * img.Width - 0.5f, 0f, img.Width - 1f);
        float fy = Math.Clamp(v * img.Height - 0.5f, 0f, img.Height - 1f);
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, img.Width - 1);
        int y1 = Math.Min(y0 + 1, img.Height - 1);
        float tx = fx - x0, ty = fy - y0;

        int i00 = (y0 * img.Width + x0) * 4;
        int i10 = (y0 * img.Width + x1) * 4;
        int i01 = (y1 * img.Width + x0) * 4;
        int i11 = (y1 * img.Width + x1) * 4;
        var p = img.Rgba;

        r = Lerp(Lerp(p[i00], p[i10], tx), Lerp(p[i01], p[i11], tx), ty) / 255f;
        g = Lerp(Lerp(p[i00 + 1], p[i10 + 1], tx), Lerp(p[i01 + 1], p[i11 + 1], tx), ty) / 255f;
        b = Lerp(Lerp(p[i00 + 2], p[i10 + 2], tx), Lerp(p[i01 + 2], p[i11 + 2], tx), ty) / 255f;
        a = Lerp(Lerp(p[i00 + 3], p[i10 + 3], tx), Lerp(p[i01 + 3], p[i11 + 3], tx), ty) / 255f;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Region corners in world space, pivot-relative. <c>PivotY = 1</c> (the sheet
    /// default, bottom-center = feet) puts the art ABOVE the slot origin.</summary>
    private static (float X, float Y)[] Corners(PoseSlot slot, RegionImage region)
    {
        float w = region.Width;
        float h = region.Height;
        float px = slot.Attachment?.PivotX ?? 0.5f;
        float py = slot.Attachment?.PivotY ?? 1f;
        float lx0 = -px * w, lx1 = (1f - px) * w;
        float ly0 = -(1f - py) * h, ly1 = py * h;
        return
        [
            slot.World.TransformPoint(lx0, ly0),
            slot.World.TransformPoint(lx1, ly0),
            slot.World.TransformPoint(lx1, ly1),
            slot.World.TransformPoint(lx0, ly1),
        ];
    }

    private SheetImage? GetSheet(string sheetName)
    {
        if (_sheets.TryGetValue(sheetName, out var cached)) return cached;
        SheetImage? image = null;
        var meta = GetSheetMeta(sheetName);
        if (meta != null && !string.IsNullOrEmpty(meta.ImagePath) && File.Exists(meta.ImagePath))
        {
            try
            {
                using var stream = File.OpenRead(meta.ImagePath);
                var decoded = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                image = new SheetImage { Width = decoded.Width, Height = decoded.Height, Rgba = decoded.Data };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Skeleton] WARNING: cannot read '{meta.ImagePath}' for rig baking: {ex.Message}");
            }
        }
        _sheets[sheetName] = image;
        return image;
    }

    private SpriteSheet? GetSheetMeta(string sheetName)
    {
        try { return _sheetLookup(sheetName); }
        catch { return null; }
    }

    /// <summary>Frame rectangle inside the source image, matching
    /// <see cref="SpriteSheet.GetFrameUV"/>'s pixel math (custom frames, else the padded
    /// grid including <see cref="SpriteSheet.ColOffset"/>).</summary>
    public static bool TryGetFrameRect(SpriteSheet sheet, int frameIndex,
        out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        if (sheet == null || frameIndex < 0) return false;

        if (sheet.CustomFrames != null && frameIndex < sheet.CustomFrames.Count)
        {
            var f = sheet.CustomFrames[frameIndex];
            x = f.X; y = f.Y; width = f.Width; height = f.Height;
            return width > 0 && height > 0;
        }

        if (sheet.Columns <= 0) return false;
        int col = frameIndex % sheet.Columns;
        int row = frameIndex / sheet.Columns;
        x = sheet.OffsetX + sheet.ColOffset * (sheet.FrameWidth + sheet.PaddingX)
            + col * (sheet.FrameWidth + sheet.PaddingX);
        y = sheet.OffsetY + row * (sheet.FrameHeight + sheet.PaddingY);
        width = sheet.FrameWidth;
        height = sheet.FrameHeight;
        return width > 0 && height > 0;
    }
}
