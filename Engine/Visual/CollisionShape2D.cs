using System.Numerics;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// 2D collision shape attached to a sprite or tile.
/// Supports rectangle, circle, and convex polygon.
/// </summary>
public class CollisionShape2D
{
    public enum ShapeType { Rectangle, Circle, Polygon }

    public ShapeType Type = ShapeType.Rectangle;
    public bool IsTrigger;   // Trigger = no physical collision, just overlap detection
    public bool IsEnabled = true;

    // ── Rectangle ──
    /// <summary>Offset from parent sprite's position (local space).</summary>
    public Vector2 RectOffset;
    public float RectWidth = 64f;
    public float RectHeight = 64f;

    // ── Circle ──
    public Vector2 CircleOffset;
    public float CircleRadius = 32f;

    // ── Polygon (convex, CCW winding) ──
    public List<Vector2> PolygonVertices = new();

    // ── Physics properties ──
    public bool IsStatic;       // Static = doesn't move (tiles, platforms)
    public bool IsKinematic;    // Kinematic = moved by code, not physics
    public float Restitution = 0f;  // Bounciness (0-1)
    public float Friction = 0.5f;
    public int LayerBits = 1;   // Collision layer bitmask
    public int MaskBits = 1;    // Collision mask bitmask

    // ── Owner reference ──
    public string OwnerName = "";

    /// <summary>
    /// Test if a world-space point is inside this shape.
    /// </summary>
    public bool ContainsPoint(Vector2 worldPos, Vector2 ownerPosition)
    {
        switch (Type)
        {
            case ShapeType.Rectangle:
                var rectMin = ownerPosition + RectOffset - new Vector2(RectWidth * 0.5f, RectHeight * 0.5f);
                var rectMax = rectMin + new Vector2(RectWidth, RectHeight);
                return worldPos.X >= rectMin.X && worldPos.X <= rectMax.X &&
                       worldPos.Y >= rectMin.Y && worldPos.Y <= rectMax.Y;

            case ShapeType.Circle:
                var center = ownerPosition + CircleOffset;
                float distSq = Vector2.DistanceSquared(worldPos, center);
                return distSq <= CircleRadius * CircleRadius;

            case ShapeType.Polygon:
                if (PolygonVertices.Count < 3) return false;
                return PointInConvexPolygon(worldPos - ownerPosition, PolygonVertices);

            default:
                return false;
        }
    }

    /// <summary>
    /// AABB overlap test between two shapes.
    /// </summary>
    public bool Overlaps(CollisionShape2D other, Vector2 posA, Vector2 posB)
    {
        var aabbA = GetAABB(posA);
        var aabbB = GetAABB(posB);
        return aabbA.min.X <= aabbB.max.X && aabbA.max.X >= aabbB.min.X &&
               aabbA.min.Y <= aabbB.max.Y && aabbA.max.Y >= aabbB.min.Y;
    }

    /// <summary>
    /// Get world-space AABB for this shape.
    /// </summary>
    public (Vector2 min, Vector2 max) GetAABB(Vector2 ownerPosition)
    {
        switch (Type)
        {
            case ShapeType.Rectangle:
                var half = new Vector2(RectWidth * 0.5f, RectHeight * 0.5f);
                var center = ownerPosition + RectOffset;
                return (center - half, center + half);

            case ShapeType.Circle:
                var c = ownerPosition + CircleOffset;
                return (c - Vector2.One * CircleRadius, c + Vector2.One * CircleRadius);

            case ShapeType.Polygon:
                if (PolygonVertices.Count == 0)
                    return (ownerPosition, ownerPosition);
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                foreach (var v in PolygonVertices)
                {
                    var w = ownerPosition + v;
                    if (w.X < minX) minX = w.X;
                    if (w.Y < minY) minY = w.Y;
                    if (w.X > maxX) maxX = w.X;
                    if (w.Y > maxY) maxY = w.Y;
                }
                return (new Vector2(minX, minY), new Vector2(maxX, maxY));

            default:
                return (ownerPosition, ownerPosition);
        }
    }

    /// <summary>
    /// Create a default rectangle shape for a sprite.
    /// </summary>
    public static CollisionShape2D CreateRect(float width, float height) => new()
    {
        Type = ShapeType.Rectangle,
        RectWidth = width,
        RectHeight = height
    };

    /// <summary>
    /// Create a default circle shape.
    /// </summary>
    public static CollisionShape2D CreateCircle(float radius) => new()
    {
        Type = ShapeType.Circle,
        CircleRadius = radius
    };

    /// <summary>
    /// Create a box polygon from width/height.
    /// </summary>
    public static CollisionShape2D CreateBoxPolygon(float width, float height)
    {
        float hw = width * 0.5f;
        float hh = height * 0.5f;
        return new CollisionShape2D
        {
            Type = ShapeType.Polygon,
            PolygonVertices =
            [
                new(-hw, -hh),
                new(hw, -hh),
                new(hw, hh),
                new(-hw, hh)
            ]
        };
    }

    // ── Serialization ──

    public CollisionShape2DData ToData() => new()
    {
        Type = Type.ToString(),
        IsTrigger = IsTrigger,
        IsEnabled = IsEnabled,
        RectOffsetX = RectOffset.X, RectOffsetY = RectOffset.Y,
        RectWidth = RectWidth, RectHeight = RectHeight,
        CircleOffsetX = CircleOffset.X, CircleOffsetY = CircleOffset.Y,
        CircleRadius = CircleRadius,
        PolygonVertices = PolygonVertices.Select(v => new float[] { v.X, v.Y }).ToList(),
        IsStatic = IsStatic,
        IsKinematic = IsKinematic,
        Restitution = Restitution,
        Friction = Friction,
        LayerBits = LayerBits,
        MaskBits = MaskBits,
        OwnerName = OwnerName
    };

    public static CollisionShape2D FromData(CollisionShape2DData data) => new()
    {
        Type = Enum.Parse<ShapeType>(data.Type),
        IsTrigger = data.IsTrigger,
        IsEnabled = data.IsEnabled,
        RectOffset = new Vector2(data.RectOffsetX, data.RectOffsetY),
        RectWidth = data.RectWidth,
        RectHeight = data.RectHeight,
        CircleOffset = new Vector2(data.CircleOffsetX, data.CircleOffsetY),
        CircleRadius = data.CircleRadius,
        PolygonVertices = data.PolygonVertices?.Select(v => new Vector2(v[0], v[1])).ToList() ?? new(),
        IsStatic = data.IsStatic,
        IsKinematic = data.IsKinematic,
        Restitution = data.Restitution,
        Friction = data.Friction,
        LayerBits = data.LayerBits,
        MaskBits = data.MaskBits,
        OwnerName = data.OwnerName
    };

    // ── Helpers ──

    private static bool PointInConvexPolygon(Vector2 point, List<Vector2> vertices)
    {
        bool inside = true;
        int n = vertices.Count;
        for (int i = 0; i < n; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % n];
            var edge = b - a;
            var toPoint = point - a;
            float cross = edge.X * toPoint.Y - edge.Y * toPoint.X;
            if (cross < 0) { inside = false; break; }
        }
        return inside;
    }
}

// ── Serialization DTO ──

public class CollisionShape2DData
{
    public string Type { get; set; } = "Rectangle";
    public bool IsTrigger { get; set; }
    public bool IsEnabled { get; set; } = true;
    public float RectOffsetX { get; set; }
    public float RectOffsetY { get; set; }
    public float RectWidth { get; set; } = 64f;
    public float RectHeight { get; set; } = 64f;
    public float CircleOffsetX { get; set; }
    public float CircleOffsetY { get; set; }
    public float CircleRadius { get; set; } = 32f;
    public List<float[]>? PolygonVertices { get; set; }
    public bool IsStatic { get; set; }
    public bool IsKinematic { get; set; }
    public float Restitution { get; set; }
    public float Friction { get; set; } = 0.5f;
    public int LayerBits { get; set; } = 1;
    public int MaskBits { get; set; } = 1;
    public string OwnerName { get; set; } = "";
}
