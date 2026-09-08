using System.Numerics;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// 2D tilemap for sidescroller levels.
/// Grid-based map with multiple layers, per-tile data, and collision support.
/// </summary>
public class Tilemap2D
{
    public string Name = "Tilemap";

    // ── Grid settings ──
    public int Width;            // Number of tiles horizontally
    public int Height;           // Number of tiles vertically
    public int TileSize = 32;    // Pixels per tile

    // ── Layers (bottom to top) ──
    public List<TileLayer> Layers = new();

    // ── Tileset reference ──
    public string TilesetImagePath = "";
    public uint TilesetTextureId;
    public int TilesetColumns = 8;
    public int TilesetRows = 8;
    public bool TilesetFlipV = false;

    // ── Grid display (per-tilemap, saved in the standalone .tilemap.json) ──
    public bool ShowGrid = true;
    public Vector4 GridColor = new(1f, 1f, 1f, 0.12f);

    // ── World offset ──
    public Vector2 Offset; // Position of tilemap origin in world space

    /// <summary>World units per pixel. The 3D Map2D plane renders at this scale so a
    /// 1600px-wide map is 160 world units — matches the editor viewport scale.</summary>
    public const float WorldScale = 0.1f;

    // ── Default layer ──
    public TileLayer ActiveLayer
    {
        get
        {
            if (Layers.Count == 0)
                Layers.Add(new TileLayer { Name = "Ground" });
            return Layers[^1];
        }
    }

    /// <summary>
    /// Create an empty tilemap with given dimensions.
    /// </summary>
    public Tilemap2D(int width, int height, int tileSize = 32)
    {
        Width = width;
        Height = height;
        TileSize = tileSize;
        Layers.Add(new TileLayer { Name = "Background" });
        Layers.Add(new TileLayer { Name = "Ground" });
        Layers.Add(new TileLayer { Name = "Foreground" });
    }

    public Tilemap2D() { }

    /// <summary>
    /// Get/set tile at grid position on a layer.
    /// </summary>
    public int GetTile(int layerIndex, int x, int y)
    {
        if (layerIndex < 0 || layerIndex >= Layers.Count) return -1;
        if (x < 0 || x >= Width || y < 0 || y >= Height) return -1;
        return Layers[layerIndex].GetTile(x, y);
    }

    public void SetTile(int layerIndex, int x, int y, int tileId)
    {
        if (layerIndex < 0 || layerIndex >= Layers.Count) return;
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        Layers[layerIndex].SetTile(x, y, tileId);
    }

    /// <summary>
    /// Convert grid position to world position (bottom-left of tile).
    /// </summary>
    public Vector2 GridToWorld(int x, int y)
    {
        float scale = TileSize * WorldScale;
        return Offset + new Vector2(x * scale, (Height - 1 - y) * scale);
    }

    /// <summary>
    /// Convert world position to grid position.
    /// </summary>
    public (int x, int y) WorldToGrid(Vector2 worldPos)
    {
        float scale = TileSize * WorldScale;
        int gx = (int)MathF.Floor((worldPos.X - Offset.X) / scale);
        int gy = (int)MathF.Floor((Offset.Y + Height * scale - worldPos.Y) / scale);
        return (gx, gy);
    }

    /// <summary>
    /// Get all tiles on a layer that have collision.
    /// </summary>
    public List<(int x, int y, int tileId)> GetCollisionTiles(int layerIndex)
    {
        var result = new List<(int, int, int)>();
        if (layerIndex < 0 || layerIndex >= Layers.Count) return result;
        var layer = Layers[layerIndex];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int t = layer.GetTile(x, y);
                if (t >= 0 && layer.TileHasCollision(t))
                    result.Add((x, y, t));
            }
        return result;
    }

    /// <summary>
    /// Fill a rectangular region with a tile.
    /// </summary>
    public void FillRect(int layerIndex, int x0, int y0, int x1, int y1, int tileId)
    {
        int minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1);
        int minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                SetTile(layerIndex, x, y, tileId);
    }

    /// <summary>
    /// Erase tiles in a rectangular region.
    /// </summary>
    public void EraseRect(int layerIndex, int x0, int y0, int x1, int y1)
    {
        FillRect(layerIndex, x0, y0, x1, y1, -1);
    }

    /// <summary>
    /// Flood fill from a point.
    /// </summary>
    public void FloodFill(int layerIndex, int startX, int startY, int tileId)
    {
        if (layerIndex < 0 || layerIndex >= Layers.Count) return;
        int targetTile = GetTile(layerIndex, startX, startY);
        if (targetTile == tileId) return;

        var stack = new Stack<(int x, int y)>();
        stack.Push((startX, startY));
        var visited = new HashSet<(int, int)>();

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || x >= Width || y < 0 || y >= Height) continue;
            if (!visited.Add((x, y))) continue;
            if (GetTile(layerIndex, x, y) != targetTile) continue;

            SetTile(layerIndex, x, y, tileId);
            stack.Push((x + 1, y));
            stack.Push((x - 1, y));
            stack.Push((x, y + 1));
            stack.Push((x, y - 1));
        }
    }

    // ── Serialization ──

    public Tilemap2DData ToData() => new()
    {
        Name = Name,
        Width = Width,
        Height = Height,
        TileSize = TileSize,
        TilesetImagePath = TilesetImagePath,
        TilesetColumns = TilesetColumns,
        TilesetRows = TilesetRows,
        TilesetFlipV = TilesetFlipV,
        OffsetX = Offset.X,
        OffsetY = Offset.Y,
        ShowGrid = ShowGrid,
        GridColorR = GridColor.X,
        GridColorG = GridColor.Y,
        GridColorB = GridColor.Z,
        GridColorA = GridColor.W,
        Layers = Layers.Select(l => l.ToData()).ToList()
    };

    public static Tilemap2D FromData(Tilemap2DData data)
    {
        var map = new Tilemap2D
        {
            Name = data.Name,
            Width = data.Width,
            Height = data.Height,
            TileSize = data.TileSize,
            TilesetImagePath = data.TilesetImagePath,
            TilesetColumns = data.TilesetColumns,
            TilesetRows = data.TilesetRows,
            TilesetFlipV = data.TilesetFlipV,
            Offset = new Vector2(data.OffsetX, data.OffsetY),
            ShowGrid = data.ShowGrid,
            GridColor = new Vector4(data.GridColorR, data.GridColorG, data.GridColorB, data.GridColorA),
            Layers = data.Layers.Select(l => TileLayer.FromData(l)).ToList()
        };
        return map;
    }
}

/// <summary>
/// A single tile layer within a tilemap.
/// </summary>
public class TileLayer
{
    public string Name = "Layer";
    public bool IsVisible = true;
    public bool IsLocked;
    public float Opacity = 1f;

    /// <summary>Tile data as flat array [y * Width + x]. -1 = empty.</summary>
    private int[] _tiles = [];

    /// <summary>Per-tile collision flags. null = no collision data.</summary>
    private bool[]? _collisionFlags;

    public int Width;
    public int Height;

    /// <summary>Collision tile IDs (tiles with these IDs have collision).</summary>
    public HashSet<int> CollisionTileIds = new();

    public int GetTile(int x, int y)
    {
        if (_tiles.Length == 0 || x < 0 || x >= Width || y < 0 || y >= Height) return -1;
        return _tiles[y * Width + x];
    }

    public void SetTile(int x, int y, int tileId)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        if (_tiles.Length == 0 || _tiles.Length != Width * Height)
        {
            _tiles = new int[Width * Height];
            Array.Fill(_tiles, -1);
        }
        _tiles[y * Width + x] = tileId;
    }

    public void Resize(int newWidth, int newHeight)
    {
        var oldTiles = _tiles;
        int oldW = Width, oldH = Height;
        Width = newWidth;
        Height = newHeight;
        _tiles = new int[newWidth * newHeight];
        Array.Fill(_tiles, -1);

        // Copy old data
        if (oldTiles.Length > 0)
        {
            int copyW = Math.Min(oldW, newWidth);
            int copyH = Math.Min(oldH, newHeight);
            for (int y = 0; y < copyH; y++)
                for (int x = 0; x < copyW; x++)
                    _tiles[y * newWidth + x] = oldTiles[y * oldW + x];
        }
    }

    public bool TileHasCollision(int tileId) => CollisionTileIds.Contains(tileId);

    // ── Serialization ──

    public TileLayerData ToData() => new()
    {
        Name = Name,
        IsVisible = IsVisible,
        IsLocked = IsLocked,
        Opacity = Opacity,
        Width = Width,
        Height = Height,
        Tiles = _tiles.ToArray(),
        CollisionTileIds = CollisionTileIds.ToList()
    };

    public static TileLayer FromData(TileLayerData data)
    {
        var layer = new TileLayer
        {
            Name = data.Name,
            IsVisible = data.IsVisible,
            IsLocked = data.IsLocked,
            Opacity = data.Opacity,
            Width = data.Width,
            Height = data.Height,
            _tiles = data.Tiles?.ToArray() ?? [],
            CollisionTileIds = new HashSet<int>(data.CollisionTileIds ?? [])
        };
        return layer;
    }
}

// ── Serialization DTOs ──

public class Tilemap2DData
{
    public string Name { get; set; } = "Tilemap";
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileSize { get; set; } = 32;
    public string TilesetImagePath { get; set; } = "";
    public int TilesetColumns { get; set; } = 8;
    public int TilesetRows { get; set; } = 8;
    public bool TilesetFlipV { get; set; } = false;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public bool ShowGrid { get; set; } = true;
    public float GridColorR { get; set; } = 1f;
    public float GridColorG { get; set; } = 1f;
    public float GridColorB { get; set; } = 1f;
    public float GridColorA { get; set; } = 0.12f;
    public List<TileLayerData> Layers { get; set; } = new();
}

public class TileLayerData
{
    public string Name { get; set; } = "Layer";
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public float Opacity { get; set; } = 1f;
    public int Width { get; set; }
    public int Height { get; set; }
    public int[]? Tiles { get; set; }
    public List<int>? CollisionTileIds { get; set; }
}
