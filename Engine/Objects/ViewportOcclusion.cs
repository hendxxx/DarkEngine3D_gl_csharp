using System.Numerics;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects;

/// <summary>Occlusion test for the viewport — deliberately SIMPLE, the same shape as the
/// frustum test: draw an object only while it is NOT behind an occluder. An occludee is
/// rejected when the whole occluder is nearer AND the occluder's projected SILHOUETTE
/// covers every projected corner of the occludee. For a convex occluder (a Box) that is
/// exactly "every ray from the eye to the occludee passes through it", so a rejected target
/// is genuinely hidden — no extra "overlap safety" net, no terrain tiles: behind the
/// occluder means not rendered.</summary>
internal static class ViewportOcclusion
{
    private static bool Project(Vector3 point, Matrix4x4 vp, out Vector3 projected)
    {
        var p = Vector4.Transform(new Vector4(point, 1f), vp);
        if (!float.IsFinite(p.W) || p.W <= 0.0001f || p.Z <= -p.W || p.Z >= p.W)
        {
            projected = default;
            return false;
        }
        projected = new Vector3(p.X, p.Y, p.Z) / p.W;
        return float.IsFinite(projected.X) && float.IsFinite(projected.Y) && float.IsFinite(projected.Z);
    }

    private static Vector3 Corner(AABB box, int i) => new(
        (i & 1) != 0 ? box.Max.X : box.Min.X,
        (i & 2) != 0 ? box.Max.Y : box.Min.Y,
        (i & 4) != 0 ? box.Max.Z : box.Min.Z);

    internal readonly struct OccluderData
    {
        private readonly Vector3 _c0, _c1, _c2, _c3, _c4, _c5, _c6, _c7;
        internal bool IsValid { get; }

        /// <summary>Identity of the object that submitted this occluder, so an object is
        /// never occlusion-culled by its own volume (every Box is an occluder by default).
        /// 0 = unknown/never matches.</summary>
        internal int OwnerTag { get; }

        internal OccluderData(ReadOnlySpan<Vector3> corners, int ownerTag)
        {
            _c0 = corners[0]; _c1 = corners[1]; _c2 = corners[2]; _c3 = corners[3];
            _c4 = corners[4]; _c5 = corners[5]; _c6 = corners[6]; _c7 = corners[7];
            OwnerTag = ownerTag;
            IsValid = true;
        }

        internal Vector3 Corner(int index) => index switch
        {
            0 => _c0, 1 => _c1, 2 => _c2, 3 => _c3,
            4 => _c4, 5 => _c5, 6 => _c6, _ => _c7
        };
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    /// <summary>Andrew monotone chain over the 8 projected box corners: the projection of
    /// a convex solid IS the convex hull of its projected vertices, i.e. its exact screen
    /// silhouette. Returns the hull count (counter-clockwise), 0..N.</summary>
    private static int ConvexHull(Span<Vector2> points, Span<Vector2> hull)
    {
        int n = points.Length;
        // Insertion sort (8 points): X then Y. Cheaper than any List/Array.Sort here.
        for (int i = 1; i < n; i++)
        {
            Vector2 key = points[i];
            int j = i - 1;
            while (j >= 0 && (points[j].X > key.X || (points[j].X == key.X && points[j].Y > key.Y)))
            {
                points[j + 1] = points[j];
                j--;
            }
            points[j + 1] = key;
        }

        Span<Vector2> chain = stackalloc Vector2[16];
        int k = 0;
        for (int i = 0; i < n; i++)
        {
            while (k >= 2 && Cross(chain[k - 2], chain[k - 1], points[i]) <= 0f) k--;
            chain[k++] = points[i];
        }
        int lower = k + 1;
        for (int i = n - 2; i >= 0; i--)
        {
            while (k >= lower && Cross(chain[k - 2], chain[k - 1], points[i]) <= 0f) k--;
            chain[k++] = points[i];
        }

        int count = k - 1; // the chain closes back onto its first point
        if (count > hull.Length) count = hull.Length;
        for (int i = 0; i < count; i++) hull[i] = chain[i];
        return count;
    }

    private static bool ContainsPoint(ReadOnlySpan<Vector2> hull, Vector2 point)
    {
        for (int i = 0; i < hull.Length; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Length];
            // Counter-clockwise hull → inside is on the left. A tiny tolerance keeps a
            // corner that grazes the silhouette edge on the "covered" side.
            if (Cross(a, b, point) < -1e-5f) return false;
        }
        return true;
    }

    internal static OccluderData Prepare(Matrix4x4 boxWorld, Vector3 cameraPosition, Matrix4x4 viewProjection,
        int ownerTag = 0)
    {
        if (!Matrix4x4.Invert(boxWorld, out var inverse)) return default;
        var localCamera = Vector3.Transform(cameraPosition, inverse);
        if (MathF.Abs(localCamera.X) <= 0.5f && MathF.Abs(localCamera.Y) <= 0.5f && MathF.Abs(localCamera.Z) <= 0.5f)
            return default;

        var localBox = new AABB(new Vector3(-0.5f), new Vector3(0.5f));
        Span<Vector3> world = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
            world[i] = Vector3.Transform(Corner(localBox, i), boxWorld);

        Span<Vector3> corners = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
            if (!Project(world[i], viewProjection, out corners[i]))
                return default;
        return new OccluderData(corners, ownerTag);
    }

    /// <summary>Prepare an occluder from an AXIS-ALIGNED world box (GLB bounds). A camera
    /// inside the box returns an invalid occluder: the box surrounds the eye, so none of
    /// its faces is a front face.</summary>
    internal static OccluderData PrepareBounds(AABB worldBounds, Vector3 cameraPosition, Matrix4x4 viewProjection,
        int ownerTag = 0)
    {
        if (cameraPosition.X >= worldBounds.Min.X && cameraPosition.X <= worldBounds.Max.X &&
            cameraPosition.Y >= worldBounds.Min.Y && cameraPosition.Y <= worldBounds.Max.Y &&
            cameraPosition.Z >= worldBounds.Min.Z && cameraPosition.Z <= worldBounds.Max.Z)
            return default;

        Span<Vector3> corners = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
            if (!Project(Corner(worldBounds, i), viewProjection, out corners[i]))
                return default;
        return new OccluderData(corners, ownerTag);
    }

    internal static bool IsOccluded(AABB target, Matrix4x4 boxWorld, Vector3 cameraPosition, Matrix4x4 viewProjection,
        int ownerTag = 0, int targetOwnerTag = 0)
    {
        return IsOccludedPrepared(target,
            Prepare(boxWorld, cameraPosition, viewProjection, ownerTag), viewProjection, targetOwnerTag);
    }

    internal static bool IsOccludedPrepared(AABB target, OccluderData occluder, Matrix4x4 viewProjection,
        int targetOwnerTag = 0)
    {
        if (!occluder.IsValid) return false;

        // ── Self-occlusion is not occlusion ──
        // Every Box is an occluder by default, so an object must never be culled by its
        // own volume.
        if (targetOwnerTag != 0 && occluder.OwnerTag == targetOwnerTag) return false;

        Span<Vector3> targets = stackalloc Vector3[8];
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < 8; i++)
        {
            if (!Project(Corner(target, i), viewProjection, out targets[i])) return false;
            nearest = MathF.Min(nearest, targets[i].Z);
        }

        // The occluder must be entirely nearer than the occludee's nearest corner,
        // otherwise a ray through it could still reach the target in front of the box.
        float farthest = float.NegativeInfinity;
        for (int i = 0; i < 8; i++) farthest = MathF.Max(farthest, occluder.Corner(i).Z);
        if (farthest + 0.0001f >= nearest) return false;

        Span<Vector2> points = stackalloc Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            var corner = occluder.Corner(i);
            points[i] = new Vector2(corner.X, corner.Y);
        }
        Span<Vector2> hull = stackalloc Vector2[16];
        int hullCount = ConvexHull(points, hull);
        if (hullCount < 3) return false;

        ReadOnlySpan<Vector2> silhouette = hull[..hullCount];
        for (int i = 0; i < 8; i++)
            if (!ContainsPoint(silhouette, new Vector2(targets[i].X, targets[i].Y)))
                return false;
        return true;
    }
}
