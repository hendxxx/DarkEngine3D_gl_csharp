using System.Numerics;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects;

/// <summary>Conservative, current-frame occlusion by a previously rendered solid box.</summary>
internal static class ViewportOcclusion
{
    private static readonly int[,] Faces =
    {
        { 0, 1, 3, 2 }, { 4, 6, 7, 5 },
        { 0, 4, 5, 1 }, { 2, 3, 7, 6 },
        { 0, 2, 6, 4 }, { 1, 5, 7, 3 }
    };

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

    internal static bool IsOccluded(AABB target, Matrix4x4 boxWorld, Vector3 cameraPosition, Matrix4x4 viewProjection)
    {
        if (!Matrix4x4.Invert(boxWorld, out var inverse)) return false;
        var localCamera = Vector3.Transform(cameraPosition, inverse);
        if (MathF.Abs(localCamera.X) <= 0.5f && MathF.Abs(localCamera.Y) <= 0.5f && MathF.Abs(localCamera.Z) <= 0.5f)
            return false;

        Span<Vector3> targets = stackalloc Vector3[8];
        float closest = float.PositiveInfinity;
        for (int i = 0; i < 8; i++)
        {
            if (!Project(Corner(target, i), viewProjection, out targets[i])) return false;
            closest = MathF.Min(closest, targets[i].Z);
        }

        var localBox = new AABB(new Vector3(-0.5f), new Vector3(0.5f));
        Span<Vector3> corners = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
            if (!Project(Vector3.Transform(Corner(localBox, i), boxWorld), viewProjection, out corners[i]))
                return false;

        for (int f = 0; f < 6; f++)
        {
            bool usable = true;
            float farthest = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                int index = Faces[f, i];
                farthest = MathF.Max(farthest, corners[index].Z);
            }
            if (farthest + 0.0001f >= closest) continue;

            float winding = 0f;
            for (int i = 0; i < 4 && usable; i++)
            {
                var a = corners[Faces[f, i]];
                var b = corners[Faces[f, (i + 1) % 4]];
                var c = corners[Faces[f, (i + 2) % 4]];
                float cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                if (MathF.Abs(cross) < 0.000001f) { usable = false; break; }
                if (i == 0) winding = MathF.Sign(cross);
                else if (cross * winding <= 0f) usable = false;
            }
            if (!usable) continue;

            for (int i = 0; i < 8 && usable; i++)
                for (int edge = 0; edge < 4; edge++)
                {
                    var a = corners[Faces[f, edge]];
                    var b = corners[Faces[f, (edge + 1) % 4]];
                    var p = targets[i];
                    float cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                    if (cross * winding <= 0.00001f) { usable = false; break; }
                }
            if (usable) return true;
        }
        return false;
    }
}
