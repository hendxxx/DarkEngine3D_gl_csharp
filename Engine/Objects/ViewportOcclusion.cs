using System.Numerics;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects;

/// <summary>Analytic occlusion test for the viewport. An occludee is rejected only when
/// the occluder is entirely nearer AND the occluder's projected SILHOUETTE covers every
/// projected corner of the occludee. The silhouette is the convex hull of the occluder's
/// 8 projected corners — for a convex solid that is exactly its screen footprint, so a
/// rejected target is genuinely hidden (every ray from the eye to it passes through the
/// occluder first). No GL state, no depth buffer, no overlap fudge: pure geometry.</summary>
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

    /// <summary>Screen footprint (NDC) of a world BOUNDING SPHERE: centre, half extents
    /// along the camera's right/up axes, the depth of its nearest point and the axes used,
    /// so the projected rim can be rebuilt. A Sphere object is a sphere, not a box: the
    /// 8-corner AABB test refused to cull one that sat fully behind a hill because the box
    /// corners poked above the ridge line (the farther the camera, the smaller that error,
    /// which is why culling only ever seemed to work at distance).</summary>
    internal readonly struct SphereScreen
    {
        internal readonly float Cx, Cy, Rx, Ry, Nearest;
        internal readonly Vector3 Right, Up;
        internal readonly bool Valid;

        internal SphereScreen(float cx, float cy, float rx, float ry, float nearest,
            Vector3 right, Vector3 up)
        {
            Cx = cx; Cy = cy; Rx = rx; Ry = ry; Nearest = nearest;
            Right = right; Up = up; Valid = true;
        }
    }

    private static bool ScreenOffset(in Matrix4x4 vp, Vector3 point, float cx, float cy, out float offset)
    {
        var q = Vector4.Transform(new Vector4(point, 1f), vp);
        offset = 0f;
        if (!float.IsFinite(q.W) || q.W <= 1e-5f) return false;
        float dx = MathF.Abs(q.X / q.W - cx);
        float dy = MathF.Abs(q.Y / q.W - cy);
        offset = MathF.Max(dx, dy);
        return float.IsFinite(offset);
    }

    private static float NdcDepth(in Matrix4x4 vp, Vector3 point, float fallback)
    {
        var q = Vector4.Transform(new Vector4(point, 1f), vp);
        if (!float.IsFinite(q.W) || q.W <= 1e-5f) return fallback;
        float d = q.Z / q.W;
        return float.IsFinite(d) ? d : fallback;
    }

    /// <summary>Project a bounding sphere to NDC. Half extents come from the points one
    /// radius off the centre along the camera's right/up axes (exact for an orthographic
    /// camera, a tight over-estimate for a perspective one).</summary>
    internal static SphereScreen ProjectSphere(in Matrix4x4 viewProjection, Vector3 center, float radius)
    {
        var c = Vector4.Transform(new Vector4(center, 1f), viewProjection);
        if (!float.IsFinite(c.W) || c.W <= 1e-5f) return default;
        float cx = c.X / c.W, cy = c.Y / c.W, cz = c.Z / c.W;

        // Camera axes: the view-projection's third row is the axis clip.z is measured on.
        var axis = new Vector3(viewProjection.M31, viewProjection.M32, viewProjection.M33);
        if (axis.LengthSquared() < 1e-12f) return default;
        axis = Vector3.Normalize(axis);
        var worldUp = MathF.Abs(axis.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(worldUp, axis));
        var up = Vector3.Cross(axis, right);

        if (!ScreenOffset(viewProjection, center + right * radius, cx, cy, out float rx1) ||
            !ScreenOffset(viewProjection, center - right * radius, cx, cy, out float rx2) ||
            !ScreenOffset(viewProjection, center + up * radius, cx, cy, out float ry1) ||
            !ScreenOffset(viewProjection, center - up * radius, cx, cy, out float ry2))
            return default;

        float rx = MathF.Max(rx1, rx2), ry = MathF.Max(ry1, ry2);
        if (!float.IsFinite(rx) || !float.IsFinite(ry)) return default;

        // Nearest point along the view axis (sign of the axis row depends on the matrices,
        // so take whichever end projects nearer).
        float n1 = NdcDepth(viewProjection, center + axis * radius, cz);
        float n2 = NdcDepth(viewProjection, center - axis * radius, cz);
        float nearest = MathF.Min(cz, MathF.Min(n1, n2));
        return new SphereScreen(cx, cy, rx, ry, nearest, right, up);
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
        /// 0 = unknown / never matches.</summary>
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

    /// <summary>Andrew monotone chain over the 8 projected corners: the projection of a
    /// convex solid IS the convex hull of its projected vertices, i.e. its exact screen
    /// silhouette. Returns the hull point count (counter-clockwise), 0..hull.Length.</summary>
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
        Span<Vector3> corners = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
            if (!Project(Vector3.Transform(Corner(localBox, i), boxWorld), viewProjection, out corners[i]))
                return default;
        return new OccluderData(corners, ownerTag);
    }

    /// <summary>Prepare an occluder from an AXIS-ALIGNED world box (GLB bounds). A camera
    /// inside the box returns an invalid occluder: the box surrounds the eye.</summary>
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

    internal static bool IsOccluded(AABB target, Matrix4x4 boxWorld, Vector3 cameraPosition,
        Matrix4x4 viewProjection, int ownerTag, int targetOwnerTag)
    {
        return IsOccludedPrepared(target,
            Prepare(boxWorld, cameraPosition, viewProjection, ownerTag), viewProjection, targetOwnerTag);
    }

    /// <summary>Hull occlusion test for a SPHERE occludee: the sphere's projected disk (centre
    /// + 8 rim points) must lie inside the occluder silhouette and the sphere's nearest depth
    /// must be behind the occluder's farthest point.</summary>
    internal static bool IsOccludedSphere(Vector3 center, float radius, OccluderData occluder,
        Matrix4x4 viewProjection, int targetOwnerTag)
    {
        if (!occluder.IsValid || radius <= 0f) return false;
        if (targetOwnerTag != 0 && occluder.OwnerTag == targetOwnerTag) return false;

        var s = ProjectSphere(viewProjection, center, radius);
        if (!s.Valid) return false;
        if (s.Nearest <= -1f || s.Nearest >= 1f) return false;

        float farthest = float.NegativeInfinity;
        for (int i = 0; i < 8; i++) farthest = MathF.Max(farthest, occluder.Corner(i).Z);
        if (farthest + 0.0001f >= s.Nearest) return false;

        Span<Vector2> points = stackalloc Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            float a = i * (MathF.PI / 4f);
            var p = center + s.Right * (MathF.Cos(a) * radius) + s.Up * (MathF.Sin(a) * radius);
            if (!Project(p, viewProjection, out var ndc)) return false;
            points[i] = new Vector2(ndc.X, ndc.Y);
        }

        Span<Vector2> occluderPoints = stackalloc Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            var corner = occluder.Corner(i);
            occluderPoints[i] = new Vector2(corner.X, corner.Y);
        }
        Span<Vector2> hull = stackalloc Vector2[16];
        int hullCount = ConvexHull(occluderPoints, hull);
        if (hullCount < 3) return false;

        ReadOnlySpan<Vector2> silhouette = hull[..hullCount];
        for (int i = 0; i < points.Length; i++)
            if (!ContainsPoint(silhouette, points[i])) return false;
        return true;
    }

    internal static bool IsOccludedPrepared(AABB target, OccluderData occluder, Matrix4x4 viewProjection,
        int targetOwnerTag)
    {
        if (!occluder.IsValid) return false;

        // ── Self-occlusion is not occlusion ──
        if (targetOwnerTag != 0 && occluder.OwnerTag == targetOwnerTag) return false;

        Span<Vector3> targets = stackalloc Vector3[8];
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < 8; i++)
        {
            if (!Project(Corner(target, i), viewProjection, out targets[i])) return false;
            nearest = MathF.Min(nearest, targets[i].Z);
        }

        // The occluder must be entirely nearer than the occludee's nearest corner, so no
        // ray through it can reach the target in front of the box.
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
