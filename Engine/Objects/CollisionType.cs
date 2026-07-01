namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    public enum CollisionType
    {
        /// <summary>Sphere with radius only.</summary>
        Sphere,
        /// <summary>Capsule (standing upright): radius + height. 
        /// Segment from (0, radius, 0) to (0, height - radius, 0) relative to foot position.</summary>
        Capsule,
        /// <summary>Box AABB with half-extents.</summary>
        Box,

        BVH
    }
}
