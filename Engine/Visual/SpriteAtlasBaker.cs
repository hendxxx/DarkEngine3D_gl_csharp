using StbImageSharp;
using System.IO.Compression;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>Pixel data for one source-sheet frame that is about to be baked.</summary>
public sealed class BakedSpriteFrame
{
    public required byte[] Pixels { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required SpriteFrame Metadata { get; init; }
}

/// <summary>Result of packing one clip into a project sprite atlas.</summary>
public sealed class SpriteAtlasBakeResult
{
    public required SpriteSheet Atlas { get; init; }
    public required List<int> FrameIndices { get; init; }
}

/// <summary>
/// Packs baked animation frames into 4096x4096 per-project PNG atlases.
/// Source frames receive one transparent gutter pixel to reduce texture bleeding.
/// A flat tangent-space normal map is emitted alongside each color atlas; its alpha
/// follows the color atlas so transparent pixels remain transparent.
/// </summary>
public static class SpriteAtlasBaker
{
    public const int AtlasSize = 4096;
    public const int CurrentNormalMapVersion = 1;
    private const int Gutter = 1;
    private const float NormalStrength = 4f;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    private sealed record Placement(int X, int Y, int Width, int Height);

    /// <summary>Ensure the project's first color and normal atlas exist.</summary>
    public static void EnsureDefaultAtlas(string spriteDirectory)
    {
        Directory.CreateDirectory(spriteDirectory);
        string colorPath = Path.Combine(spriteDirectory, "mainsprite1.png");
        string normalPath = Path.Combine(spriteDirectory, "mainsprite1Normal.png");
        if (!File.Exists(colorPath)) WriteBlankAtlas(colorPath, normal: false);
        if (!File.Exists(normalPath)) WriteBlankAtlas(normalPath, normal: true);
    }

    /// <summary>
    /// Bake all frames of one clip into one atlas. <paramref name="clipsToKeep"/> must
    /// omit the clip being replaced; its old frame slots are then available for reuse.
    /// </summary>
    public static SpriteAtlasBakeResult? BakeClip(
        string spriteDirectory,
        string clipName,
        IReadOnlyList<BakedSpriteFrame> frames,
        IReadOnlyList<SpriteSheet> atlasSheets,
        IReadOnlyList<AnimationClip2D> clipsToKeep)
    {
        if (frames.Count == 0) return null;
        EnsureDefaultAtlas(spriteDirectory);

        foreach (var frame in frames)
        {
            if (frame.Width <= 0 || frame.Height <= 0 ||
                frame.Pixels.Length != checked(frame.Width * frame.Height * 4))
                throw new InvalidDataException($"Clip '{clipName}' has an invalid frame buffer.");
            if (frame.Width + Gutter * 2 > AtlasSize || frame.Height + Gutter * 2 > AtlasSize)
            {
                Console.WriteLine($"[SpriteEditor] WARNING: clip '{clipName}' has a {frame.Width}x{frame.Height} frame; atlas limit is {AtlasSize - Gutter * 2}x{AtlasSize - Gutter * 2}px. Frame was not baked.");
                return null;
            }
        }

        var byNumber = atlasSheets
            .Where(s => TryGetAtlasNumber(s.Name, out _))
            .OrderBy(s => GetAtlasNumber(s.Name))
            .ToList();
        if (byNumber.Count == 0)
            byNumber.Add(CreateAtlasSheet(spriteDirectory, 1));

        foreach (var atlas in byNumber)
        {
            var occupied = GetOccupiedRects(atlas, clipsToKeep);
            if (!TryPack(frames, occupied, out var placements)) continue;
            return WriteClipToAtlas(spriteDirectory, atlas, frames, placements, clipsToKeep);
        }

        int nextNumber = Math.Max(1, byNumber.Max(s => GetAtlasNumber(s.Name)) + 1);
        var nextAtlas = CreateAtlasSheet(spriteDirectory, nextNumber);
        var emptyRects = new List<Placement>();
        if (!TryPack(frames, emptyRects, out var newPlacements))
        {
            Console.WriteLine($"[SpriteEditor] WARNING: clip '{clipName}' cannot fit in a {AtlasSize}x{AtlasSize} atlas; no atlas was changed.");
            return null;
        }
        return WriteClipToAtlas(spriteDirectory, nextAtlas, frames, newPlacements, clipsToKeep);
    }

    private static SpriteAtlasBakeResult WriteClipToAtlas(
        string spriteDirectory,
        SpriteSheet atlas,
        IReadOnlyList<BakedSpriteFrame> frames,
        IReadOnlyList<Placement> placements,
        IReadOnlyList<AnimationClip2D> clipsToKeep)
    {
        string colorPath = Path.Combine(spriteDirectory, $"{atlas.Name}.png");
        string normalPath = Path.Combine(spriteDirectory, $"{atlas.Name}Normal.png");
        byte[] color = LoadOrCreatePixels(colorPath, normal: false);
        atlas.CustomFrames ??= new List<SpriteFrame>();

        var liveIndices = clipsToKeep
            .Where(c => string.Equals(c.SpriteSheetName, atlas.Name, StringComparison.OrdinalIgnoreCase))
            .SelectMany(c => c.FrameIndices)
            .ToHashSet();
        var reusable = new Queue<int>(Enumerable.Range(0, atlas.CustomFrames.Count)
            .Where(i => !liveIndices.Contains(i)));
        var newIndices = new List<int>(frames.Count);

        for (int i = 0; i < frames.Count; i++)
        {
            var placement = placements[i];
            var source = frames[i];
            int x = placement.X + Gutter;
            int y = placement.Y + Gutter;
            CopyFrame(source, color, x, y);

            var metadata = CloneMetadata(source.Metadata);
            metadata.X = x;
            metadata.Y = y;
            metadata.Width = source.Width;
            metadata.Height = source.Height;
            if (reusable.Count > 0)
            {
                int slot = reusable.Dequeue();
                atlas.CustomFrames[slot] = metadata;
                newIndices.Add(slot);
            }
            else
            {
                newIndices.Add(atlas.CustomFrames.Count);
                atlas.CustomFrames.Add(metadata);
            }
        }

        atlas.Name = Path.GetFileNameWithoutExtension(colorPath);
        atlas.ImagePath = Engine.Helpers.PathHelpers.MakeProjectRelative(colorPath);
        atlas.ImageWidth = AtlasSize;
        atlas.ImageHeight = AtlasSize;
        atlas.Columns = Math.Max(1, atlas.CustomFrames.Count);
        atlas.Rows = 1;
        atlas.FrameWidth = 1;
        atlas.FrameHeight = 1;
        atlas.NormalMapVersion = CurrentNormalMapVersion;

        WritePng(colorPath, color, normal: false);
        byte[] normal = CreateBlankPixels(normal: true);
        RegenerateFrameNormals(color, normal, atlas.CustomFrames);
        WritePng(normalPath, normal, normal: true);
        return new SpriteAtlasBakeResult { Atlas = atlas, FrameIndices = newIndices };
    }

    /// <summary>Regenerate the companion normal atlas from an existing color atlas.</summary>
    public static void RegenerateNormalMap(string spriteDirectory, SpriteSheet atlas)
    {
        string colorPath = Path.Combine(spriteDirectory, $"{atlas.Name}.png");
        string normalPath = Path.Combine(spriteDirectory, $"{atlas.Name}Normal.png");
        if (!File.Exists(colorPath)) return;
        byte[] color = LoadOrCreatePixels(colorPath, normal: false);
        byte[] normal = CreateBlankPixels(normal: true);
        RegenerateFrameNormals(color, normal, atlas.CustomFrames ?? []);
        WritePng(normalPath, normal, normal: true);
        atlas.NormalMapVersion = CurrentNormalMapVersion;
    }

    private static void RegenerateFrameNormals(byte[] color, byte[] normal, IEnumerable<SpriteFrame> frames)
    {
        foreach (var frame in frames)
        {
            if (frame.Width <= 0 || frame.Height <= 0 || frame.X < 0 || frame.Y < 0 ||
                frame.X + frame.Width > AtlasSize || frame.Y + frame.Height > AtlasSize)
                continue;
            GenerateFrameNormals(color, normal, frame.X, frame.Y, frame.Width, frame.Height);
        }
    }

    private static List<Placement> GetOccupiedRects(SpriteSheet atlas, IReadOnlyList<AnimationClip2D> clips)
    {
        var frames = atlas.CustomFrames;
        if (frames == null || frames.Count == 0) return new List<Placement>();
        var live = clips
            .Where(c => string.Equals(c.SpriteSheetName, atlas.Name, StringComparison.OrdinalIgnoreCase))
            .SelectMany(c => c.FrameIndices)
            .Where(i => i >= 0 && i < frames.Count)
            .ToHashSet();
        var rects = new List<Placement>(live.Count);
        foreach (int index in live)
        {
            var frame = frames[index];
            if (frame.Width <= 0 || frame.Height <= 0) continue;
            rects.Add(new Placement(frame.X - Gutter, frame.Y - Gutter,
                frame.Width + Gutter * 2, frame.Height + Gutter * 2));
        }
        return rects;
    }

    private static bool TryPack(IReadOnlyList<BakedSpriteFrame> frames, List<Placement> occupied,
        out List<Placement> placements)
    {
        placements = new List<Placement>(frames.Count);
        foreach (var frame in frames)
        {
            int width = frame.Width + Gutter * 2;
            int height = frame.Height + Gutter * 2;
            var yCandidates = new SortedSet<int> { 0 };
            foreach (var rect in occupied) yCandidates.Add(rect.Y + rect.Height);
            Placement? found = null;
            foreach (int y in yCandidates)
            {
                if (y + height > AtlasSize) continue;
                var xCandidates = new SortedSet<int> { 0 };
                foreach (var rect in occupied)
                    if (rect.Y < y + height && rect.Y + rect.Height > y)
                        xCandidates.Add(rect.X + rect.Width);
                foreach (int x in xCandidates)
                {
                    if (x + width > AtlasSize) continue;
                    bool overlaps = occupied.Any(r => x < r.X + r.Width && x + width > r.X
                        && y < r.Y + r.Height && y + height > r.Y);
                    if (overlaps) continue;
                    found = new Placement(x, y, width, height);
                    break;
                }
                if (found != null) break;
            }
            if (found == null)
            {
                placements.Clear();
                return false;
            }
            placements.Add(found);
            occupied.Add(found);
        }
        return true;
    }

    private static void CopyFrame(BakedSpriteFrame frame, byte[] atlas, int x, int y)
    {
        for (int row = 0; row < frame.Height; row++)
        {
            int sourceOffset = row * frame.Width * 4;
            int targetOffset = ((y + row) * AtlasSize + x) * 4;
            Buffer.BlockCopy(frame.Pixels, sourceOffset, atlas, targetOffset, frame.Width * 4);
        }
    }

    private static void GenerateFrameNormals(byte[] color, byte[] normal, int x, int y, int width, int height)
    {
        var heights = new float[checked(width * height)];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                int pixel = ((y + row) * AtlasSize + x + col) * 4;
                float alpha = color[pixel + 3] / 255f;
                float luminance = (color[pixel] * 0.2126f + color[pixel + 1] * 0.7152f +
                    color[pixel + 2] * 0.0722f) / 255f;
                heights[row * width + col] = alpha * (0.55f + luminance * 0.45f);
            }
        }

        float HeightAt(int px, int py) => px < 0 || py < 0 || px >= width || py >= height
            ? 0f : heights[py * width + px];

        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                float gx = (HeightAt(col + 1, row - 1) + 2f * HeightAt(col + 1, row) + HeightAt(col + 1, row + 1)
                    - HeightAt(col - 1, row - 1) - 2f * HeightAt(col - 1, row) - HeightAt(col - 1, row + 1)) / 8f;
                float gy = (HeightAt(col - 1, row + 1) + 2f * HeightAt(col, row + 1) + HeightAt(col + 1, row + 1)
                    - HeightAt(col - 1, row - 1) - 2f * HeightAt(col, row - 1) - HeightAt(col + 1, row - 1)) / 8f;

                float nx = -gx * NormalStrength;
                float ny = gy * NormalStrength;
                float nz = 1f;
                float inverseLength = 1f / MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                int dst = ((y + row) * AtlasSize + x + col) * 4;
                normal[dst] = ToByte(nx * inverseLength * 0.5f + 0.5f);
                normal[dst + 1] = ToByte(ny * inverseLength * 0.5f + 0.5f);
                normal[dst + 2] = ToByte(nz * inverseLength * 0.5f + 0.5f);
                normal[dst + 3] = color[dst + 3];
            }
        }
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static byte[] LoadOrCreatePixels(string path, bool normal)
    {
        if (File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            if (image.Width == AtlasSize && image.Height == AtlasSize)
                return image.Data;
            Console.WriteLine($"[SpriteEditor] WARNING: {Path.GetFileName(path)} is {image.Width}x{image.Height}; expected 4096x4096. Replacing it with a fresh atlas.");
        }
        return CreateBlankPixels(normal);
    }

    private static byte[] CreateBlankPixels(bool normal)
    {
        var pixels = new byte[checked(AtlasSize * AtlasSize * 4)];
        if (!normal) return pixels;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 128;
            pixels[i + 1] = 128;
            pixels[i + 2] = 255;
            pixels[i + 3] = 0;
        }
        return pixels;
    }

    private static void WriteBlankAtlas(string path, bool normal)
        => WritePng(path, CreateBlankPixels(normal), normal);

    private static SpriteSheet CreateAtlasSheet(string spriteDirectory, int number)
    {
        string name = $"mainsprite{number}";
        string path = Path.Combine(spriteDirectory, name + ".png");
        if (!File.Exists(path)) WriteBlankAtlas(path, normal: false);
        string normalPath = Path.Combine(spriteDirectory, name + "Normal.png");
        if (!File.Exists(normalPath)) WriteBlankAtlas(normalPath, normal: true);
        return new SpriteSheet
        {
            Name = name,
            ImagePath = Engine.Helpers.PathHelpers.MakeProjectRelative(path),
            ImageWidth = AtlasSize,
            ImageHeight = AtlasSize,
            Columns = 1,
            Rows = 1,
            FrameWidth = 1,
            FrameHeight = 1,
            NormalMapVersion = CurrentNormalMapVersion,
            CustomFrames = new List<SpriteFrame>()
        };
    }

    private static SpriteFrame CloneMetadata(SpriteFrame source) => new()
    {
        Name = source.Name,
        AnchorX = source.AnchorX,
        AnchorY = source.AnchorY,
        RenderOffsetX = source.RenderOffsetX,
        RenderOffsetY = source.RenderOffsetY,
        HitboxX = source.HitboxX,
        HitboxY = source.HitboxY,
        HitboxW = source.HitboxW,
        HitboxH = source.HitboxH,
        Tags = source.Tags != null ? new Dictionary<string, string>(source.Tags) : new()
    };

    private static bool TryGetAtlasNumber(string name, out int number)
    {
        const string prefix = "mainsprite";
        number = 0;
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(prefix.Length), out number) && number > 0;
    }

    private static int GetAtlasNumber(string name)
        => TryGetAtlasNumber(name, out int number) ? number : int.MaxValue;

    private static void WritePng(string path, byte[] rgba, bool normal)
    {
        if (rgba.Length != checked(AtlasSize * AtlasSize * 4))
            throw new InvalidDataException("Atlas buffer dimensions do not match 4096x4096 RGBA.");
        string temp = path + ".tmp";
        using (var output = File.Create(temp))
        {
            output.Write(PngSignature);
            Span<byte> header = stackalloc byte[13];
            WriteBigEndian(header, 0, AtlasSize);
            WriteBigEndian(header, 4, AtlasSize);
            header[8] = 8;
            header[9] = 6;
            WriteChunk(output, "IHDR", header);

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                byte[] row = new byte[1 + AtlasSize * 4];
                for (int y = 0; y < AtlasSize; y++)
                {
                    row[0] = 0; // PNG filter: None
                    Buffer.BlockCopy(rgba, y * AtlasSize * 4, row, 1, AtlasSize * 4);
                    zlib.Write(row);
                }
            }
            WriteChunk(output, "IDAT", compressed.ToArray());
            WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        }
        File.Move(temp, path, overwrite: true);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteBigEndian(length, 0, data.Length);
        output.Write(length);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (byte b in typeBytes) crc = UpdateCrc(crc, b);
        foreach (byte b in data) crc = UpdateCrc(crc, b);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteBigEndian(crcBytes, 0, unchecked((int)~crc));
        output.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        return crc;
    }

    private static void WriteBigEndian(Span<byte> target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }
}
