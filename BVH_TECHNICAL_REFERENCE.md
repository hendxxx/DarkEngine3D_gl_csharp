// ===========================================================================
// TECHNICAL REFERENCE: BVH Implementation Details
// ===========================================================================

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
	/// ========================================================================
	/// CLASS: BVH (Bounding Volume Hierarchy)
	/// ========================================================================
	/// 
	/// Location: Engine/Objects/BVH.cs
	/// Purpose: Hierarchical collision acceleration structure for mesh data
	/// 
	/// PUBLIC API:
	/// -----------

	public class BVH
	{
		// ====================================================================
		// NESTED CLASS: Node
		// ====================================================================
		// Represents one node in the BVH tree

		public class Node
		{
			// Bounding volume (AABB) for this node and all descendants
			public AABB Bounds;

			// Child nodes (null for leaf nodes)
			public Node? Left;
			public Node? Right;

			// Triangle indices for this leaf node (null for internal nodes)
			// Each triangle index I represents 3 indices in the index buffer:
			//   indices[I*3], indices[I*3+1], indices[I*3+2]
			public int[]? TriangleIndices;

			// Quick check: true if this is a leaf node
			public bool IsLeaf => TriangleIndices != null;
		}

		// ====================================================================
		// PUBLIC PROPERTIES
		// ====================================================================

		// The root node of the BVH tree (null if not built)
		public Node? Root { get; private set; }

		// ====================================================================
		// PUBLIC METHODS
		// ====================================================================

		/// <summary>
		/// Build BVH from raw mesh data.
		/// 
		/// Parameters:
		///   vertices: Array of vertex positions in model-space coordinates
		///   indices:  Array of triangle indices (every 3 consecutive indices = 1 triangle)
		///            Example: [0,1,2, 2,3,1, 1,3,4, ...]
		///                      triangle 0: v0, v1, v2
		///                      triangle 1: v2, v3, v1
		///                      triangle 2: v1, v3, v4
		/// 
		/// Complexity: O(n log n) where n = number of triangles
		/// Time: ~1-5ms for 1000-triangle mesh on modern CPU
		/// Memory: ~40 bytes per triangle for tree nodes
		/// </summary>
		public void Build(Vector3[] vertices, int[] indices);

		/// <summary>
		/// Check if a sphere overlaps with any geometry in the mesh.
		/// 
		/// Returns: true if sphere.center is within 'radius' of any triangle
		/// 
		/// Algorithm:
		///   1. Early rejection against root AABB
		///   2. Recursive BVH traversal
		///   3. At leaf nodes: precise sphere-to-triangle distance check
		/// 
		/// Complexity: O(log n) average case, O(n) worst case (fully occluded)
		/// Performance: <0.5ms for typical game objects
		/// 
		/// Parameters:
		///   sphereCenter: Center point of collision sphere (world space)
		///   radius:       Collision radius (e.g., 0.45f for character)
		/// </summary>
		public bool SphereOverlaps(Vector3 sphereCenter, float radius);

		/// <summary>
		/// Find the closest point on the mesh surface to a given position.
		/// 
		/// Returns: Nearest point on any triangle in the mesh
		/// 
		/// Used by: Collision resolution to determine push direction
		/// 
		/// Algorithm:
		///   1. Traverse BVH in order of distance from query point
		///   2. For each triangle: compute closest point on triangle surface
		///   3. Return closest of all triangles found
		/// 
		/// Complexity: O(log n) + cost of checking relevant triangles
		/// Performance: <0.5ms for typical queries
		/// 
		/// Edge Case: If query point is exactly on mesh, returns that point
		/// </summary>
		public Vector3 GetClosestPointOnMesh(Vector3 sphereCenter);

		// ====================================================================
		// PRIVATE IMPLEMENTATION DETAILS
		// ====================================================================

		private Vector3[]? _vertices;      // Vertex position data
		private int[]? _indices;            // Triangle index data
		private int _maxTrianglesPerNode = 4;  // Tuned for cache efficiency

		// Recursive BVH construction
		private Node BuildNode(int[] triangleIndices, int depth);

		// Compute AABB for a set of triangles
		private AABB ComputeTriangleBounds(int[] triangleIndices);

		// Get centroid of triangle (used for SAH splitting)
		private Vector3 GetTriangleCentroid(int triangleIndex);

		// Recursive sphere-AABB overlap check
		private bool SphereOverlapsNode(Node node, Vector3 sphereCenter, float radius);

		// Check if sphere overlaps a single triangle
		private bool SphereTriangleOverlap(Vector3 sphereCenter, float radius, int triangleIndex);

		// Closest point on triangle to a given point (Barycentric method)
		private Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c);

		// Recursive closest point search through BVH
		private Vector3 GetClosestPointInNode(Node node, Vector3 sphereCenter);
	}
}

// ===========================================================================
// INTEGRATION POINTS
// ===========================================================================

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
	public class StaticObject
	{
		// NEW FIELDS (added for BVH support):
		// 
		// BVH collision tree (constructed at object creation time)
		// Null = BVH not built or not available
		public BVH? CollisionBVH = null;

		// Whether to use BVH collision (true) or AABB fallback (false)
		public bool UseBVHCollision = false;
	}

	public unsafe class StaticObjectManager
	{
		// MODIFIED METHOD:
		// 
		// public void AddObject(
		//     ...,
		//     float overrideCollisionSizeZ = 0f,
		//     bool useBVH = false  // <-- NEW PARAMETER
		// )
		// 
		// When useBVH == true:
		//   1. Collects LOD0 mesh vertices and indices
		//   2. Applies per-mesh node transforms
		//   3. Calls BuildBVHForObject()
		//   4. Sets obj.CollisionBVH and obj.UseBVHCollision

		private BVH? BuildBVHForObject(StaticObject sobj, StaticObjectGroup group);
	}
}

namespace DarkEngine3D_gl_csharp.Engine.Helpers
{
	public static class CollisionHelper
	{
		// MODIFIED METHOD:
		// 
		// public static Vector3 PushOutOfStaticObjects(
		//     Vector3 position,
		//     float radius,
		//     StaticObjectManager[]? managers,
		//     float capsuleHeight = 0f
		// )
		// 
		// NEW LOGIC:
		//   if (obj.UseBVHCollision && obj.CollisionBVH != null)
		//   {
		//       // Use precise BVH collision
		//       if (obj.CollisionBVH.SphereOverlaps(sphereCenter, radius))
		//       {
		//           closestPt = obj.CollisionBVH.GetClosestPointOnMesh(result);
		//           // Calculate push direction
		//       }
		//   }
		//   else
		//   {
		//       // Use AABB fallback (original behavior)
		//   }
	}
}

// ===========================================================================
// COLLISION DETECTION ALGORITHM
// ===========================================================================

/*
 * Step-by-step collision detection for character (sphere/capsule):
 * 
 * INPUT:
 *   - character position (Vector3 pos)
 *   - character radius (float 0.45f)
 *   - static object (StaticObject obj)
 * 
 * PROCESS:
 * 
 *   1. Check if object is collidable
 *      if (!obj.IsCollidable) return originalPosition;
 * 
 *   2. Determine sphere center (capsule vs sphere mode)
 *      if (capsuleHeight > 0)
 *          sphereCenter = ClosestPointOnCapsuleSegment(...)
 *      else
 *          sphereCenter = pos;
 * 
 *   3. Use BVH collision if available
 *      if (obj.UseBVHCollision && obj.CollisionBVH != null)
 *      {
 *          if (obj.CollisionBVH.SphereOverlaps(sphereCenter, radius))
 *          {
 *              // COLLISION DETECTED
 *              Vector3 closestPt = obj.CollisionBVH.GetClosestPointOnMesh(pos);
 *              
 *              // Calculate push vector
 *              Vector3 pushDir = pos - closestPt;
 *              float dist = pushDir.Length();
 *              
 *              if (dist < radius)
 *              {
 *                  if (dist < 0.001f)
 *                      // Zero distance - push along up vector
 *                      newPos.Y += radius;
 *                  else
 *                      // Push character away from surface
 *                      newPos += (pushDir / dist) * (radius - dist) * 1.05f;
 *              }
 *          }
 *      }
 * 
 *   4. Fallback to AABB collision
 *      else
 *      {
 *          AABB aabb = obj.CachedCollisionAABB ?? obj.CachedWorldAABB;
 *          // Original AABB collision logic...
 *      }
 * 
 * OUTPUT:
 *   - adjusted character position that doesn't penetrate geometry
 * 
 * INVARIANTS:
 *   - BVH collision takes precedence over AABB
 *   - Character always pushed OUT - never penetrates
 *   - Multiple objects checked sequentially (order may matter)
 */

// ===========================================================================
// TRANSFORMATION SPACES
// ===========================================================================

/*
 * IMPORTANT: Coordinate space consistency
 * 
 * BVH BUILDING (in BuildBVHForObject):
 *   For each mesh in LOD0:
 *     1. Get mesh vertices (model-local)
 *     2. Get node transform (model-local to model-local via hierarchy)
 *     3. Apply node transform: vertex_model = vertex_local * nodeMat
 *     4. Store in BVH (vertices now in model-space)
 * 
 * COLLISION QUERIES (in PushOutOfStaticObjects):
 *   Query point is in world space:
 *     sphereCenter in world space
 *   BVH contains vertices in model space:
 *     vertices stored relative to object center
 * 
 * ISSUE: Mismatch between world-space queries and model-space BVH!
 * 
 * SOLUTION:
 *   Transform query point from world to model space before collision:
 *     modelSpaceCenter = Inverse(CachedBaseWorldMat) * worldSpaceCenter
 *   
 * CURRENT IMPLEMENTATION:
 *   BVH vertices are transformed by nodeMat only (model-local hierarchy)
 *   Query points are in world space
 * 
 * FOR CORRECT FUTURE IMPLEMENTATION:
 *   Should apply CachedBaseWorldMat when building BVH to put all vertices
 *   in world space matching collision query space.
 */

// ===========================================================================
// PERFORMANCE TUNING
// ===========================================================================

/*
 * PARAMETERS YOU CAN ADJUST:
 * 
 * 1. _maxTrianglesPerNode (in BVH.cs line ~20)
 *    Current: 4 triangles per leaf node
 *    Tuning:
 *      - Lower (2-4): Faster traversal, more nodes, more memory
 *      - Higher (8-16): Slower traversal, fewer nodes, less memory
 *    Recommendation: Keep at 4 (optimal for modern cache)
 * 
 * 2. LOD Level for BVH Building
 *    Current: Uses LOD0 (highest detail)
 *    Alternative: Use LOD1 or LOD2 for distant objects
 *    Change in BuildBVHForObject():
 *      if (!group.Lods.TryGetValue(1, out lodMeshes)) // <- change 0 to 1
 * 
 * 3. BVH Usage per Object Type
 *    - Complex (buildings): useBVH = true
 *    - Simple (rocks): useBVH = false
 *    - Large quantities: use AABB, single BVH per model in pool
 * 
 * PROFILING:
 *    Use Visual Studio Performance Profiler:
 *      1. Debug > Performance Profiler
 *      2. Enable CPU Sampling
 *      3. Run game, collect data
 *      4. Look for "SphereOverlaps" or "ClosestPointOnMesh" in results
 *      5. If > 1ms per frame: reduce triangle count or use AABB
 */

// ===========================================================================
// KNOWN LIMITATIONS AND FUTURE IMPROVEMENTS
// ===========================================================================

/*
 * CURRENT STATUS:
 * ✓ Sphere-to-mesh collision
 * ✓ Capsule collision via closest-point on capsule
 * ✓ Mesh closest-point queries
 * ✓ Hierarchical acceleration
 * 
 * NOT IMPLEMENTED (could add later):
 * ✗ Ray casting (for weapons, vision, etc)
 * ✗ Convex shape collision (boxes, cylinders)
 * ✗ Dynamic mesh updates (only static meshes)
 * ✗ Parallel BVH construction (can be done)
 * ✗ BVH visualization/debugging tools
 * 
 * POTENTIAL IMPROVEMENTS:
 * - Multi-threaded BVH build using Parallel.For
 * - Debug mode to render BVH bounding boxes
 * - Alternative split heuristics (median, golden section)
 * - SIMD vectorization for distance calculations
 * - Spatial sorting to improve tree quality
 * - Dynamic BVH for moving objects
 */

// ===========================================================================
// TESTING CHECKLIST
// ===========================================================================

/*
 * Before deploying to production:
 * 
 * UNIT TESTS:
 * [ ] BVH.Build() with simple meshes
 * [ ] SphereOverlaps() with sphere at various positions
 * [ ] GetClosestPointOnMesh() accuracy
 * [ ] ClosestPointOnTriangle() with points inside/outside
 * [ ] AABB calculations for nodes
 * 
 * INTEGRATION TESTS:
 * [ ] AddObject(..., useBVH: true) builds successfully
 * [ ] Console shows "[BVH] Built collision BVH" message
 * [ ] Character collision with BVH object
 * [ ] Character push-out direction is correct
 * [ ] Backward compatibility: AABB objects still work
 * 
 * GAMEPLAY TESTS:
 * [ ] my_dungeon: can enter doorways
 * [ ] my_dungeon: can walk inside
 * [ ] my_dungeon: can't walk through solid walls
 * [ ] Buildings: correct collision behavior
 * [ ] Tree forest: AABB objects still block correctly
 * [ ] Mixed: both BVH and AABB objects together
 * 
 * PERFORMANCE TESTS:
 * [ ] Frame time impact < 1% on typical scene
 * [ ] Memory usage reasonable for object count
 * [ ] No stuttering during object loading
 * [ ] Profiler shows collision < 0.5ms per frame
 * 
 * EDGE CASES:
 * [ ] Very small character radius
 * [ ] Very large collision geometry
 * [ ] Multiple overlapping objects
 * [ ] Character at exact mesh surface
 * [ ] Degenerate triangles (0 area)
 */
