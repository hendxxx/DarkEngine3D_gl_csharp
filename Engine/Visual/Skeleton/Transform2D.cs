namespace DarkEngine3D_gl_csharp.Engine.Visual.Skeleton;

/// <summary>
/// A 2D affine transform (rotation + scale + shear + translation) in RIG DESIGN SPACE:
/// pixels, +X right, +Y UP, origin at the rig root (the feet anchor). Column-vector
/// convention — <c>v' = M · v</c> — so a child's world transform is
/// <c>parent.World * child.Local</c>.
///
/// Kept as a small struct with explicit fields (no <see cref="System.Numerics.Matrix4x4"/>)
/// because the pose evaluator runs per bone per frame during a bake and the bake is on
/// the UI thread.
/// </summary>
public readonly struct Transform2D
{
    public readonly float M11, M12;
    public readonly float M21, M22;
    public readonly float Tx, Ty;

    public Transform2D(float m11, float m12, float m21, float m22, float tx, float ty)
    {
        M11 = m11; M12 = m12;
        M21 = m21; M22 = m22;
        Tx = tx; Ty = ty;
    }

    public static readonly Transform2D Identity = new(1f, 0f, 0f, 1f, 0f, 0f);

    public static Transform2D Translation(float x, float y) => new(1f, 0f, 0f, 1f, x, y);

    public static Transform2D RotationDegrees(float degrees)
    {
        float rad = degrees * MathF.PI / 180f;
        float c = MathF.Cos(rad), s = MathF.Sin(rad);
        // +Y up: positive rotation turns counter-clockwise on screen.
        // Column-vector form: X' = c·x − s·y, Y' = s·x + c·y.
        return new Transform2D(c, -s, s, c, 0f, 0f);
    }

    public static Transform2D Scale(float sx, float sy) => new(sx, 0f, 0f, sy, 0f, 0f);

    /// <summary>Shear, in degrees, applied between rotation and scale (Spine order).</summary>
    public static Transform2D ShearDegrees(float shearX, float shearY)
    {
        float sx = MathF.Tan(shearX * MathF.PI / 180f);
        float sy = MathF.Tan(shearY * MathF.PI / 180f);
        // M12 = X skew from Y, M21 = Y skew from X.
        return new Transform2D(1f, sx, sy, 1f, 0f, 0f);
    }

    /// <summary>T · R · Shear · S — the local transform of a bone or attachment.</summary>
    public static Transform2D Trss(float x, float y, float rotationDeg, float scaleX, float scaleY,
        float shearX, float shearY)
        => Translation(x, y) * RotationDegrees(rotationDeg)
           * ShearDegrees(shearX, shearY) * Scale(scaleX, scaleY);

    /// <summary><paramref name="a"/> applied first, then <paramref name="b"/>'s parent
    /// relationship: <c>(b * a)</c> means "a is a child of b".</summary>
    public static Transform2D operator *(Transform2D parent, Transform2D child) => new(
        parent.M11 * child.M11 + parent.M12 * child.M21,
        parent.M11 * child.M12 + parent.M12 * child.M22,
        parent.M21 * child.M11 + parent.M22 * child.M21,
        parent.M21 * child.M12 + parent.M22 * child.M22,
        parent.M11 * child.Tx + parent.M12 * child.Ty + parent.Tx,
        parent.M21 * child.Tx + parent.M22 * child.Ty + parent.Ty);

    public (float X, float Y) TransformPoint(float x, float y)
        => (M11 * x + M12 * y + Tx, M21 * x + M22 * y + Ty);

    /// <summary>World → local. Used by the rasterizer to walk a destination pixel back
    /// into attachment space (0..1 across the region) for sampling.</summary>
    public bool TryInvert(out Transform2D inverse)
    {
        float det = M11 * M22 - M12 * M21;
        if (MathF.Abs(det) < 1e-8f)
        {
            inverse = Identity;
            return false;
        }
        float invDet = 1f / det;
        float i11 = M22 * invDet;
        float i12 = -M12 * invDet;
        float i21 = -M21 * invDet;
        float i22 = M11 * invDet;
        inverse = new Transform2D(i11, i12, i21, i22,
            -(i11 * Tx + i12 * Ty),
            -(i21 * Tx + i22 * Ty));
        return true;
    }
}
