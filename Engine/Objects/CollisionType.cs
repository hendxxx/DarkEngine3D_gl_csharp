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
        /// <summary>Convex hull: simplified mesh-based collision using BVH built from convex hull vertices.
        /// More accurate than Box, less expensive than full BVH mesh collision.
        /// Default collision type for static objects (except player/NPC).</summary>
        Convex,
        /// <summary>Full BVH mesh collision: accurate ray/triangle intersection using the complete mesh geometry.
        /// Most accurate but most expensive collision type.</summary>
        BVH
    }
}
