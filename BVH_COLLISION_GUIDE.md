// ===========================================================================
// BVH (Bounding Volume Hierarchy) Collision Implementation Guide
// ===========================================================================
//
// This document explains how to use the new BVH collision system for 
// more accurate mesh-based collision detection on static objects.
//
// ===========================================================================
// PROBLEM SOLVED
// ===========================================================================
//
// Previously: Static objects used simple AABB (axis-aligned bounding boxes)
//   - Too restrictive for complex geometry (e.g., my_dungeon with openings)
//   - Players couldn't enter doorways or navigate interior spaces
//   - Collision geometry didn't match visual geometry
//
// Now: With BVH collision enabled
//   - Collision follows actual mesh geometry
//   - Players can walk through doorways and enter buildings
//   - Multiple collision triangles checked hierarchically for precision
//
// ===========================================================================
// BASIC USAGE
// ===========================================================================
//
// Example: Enable BVH collision for my_dungeon object
//
//   // In GameScene.cs or wherever you add objects:
//   var dungeonManager = new StaticObjectManager();
//
//   // Add object WITH BVH collision enabled (new parameter)
//   dungeonManager.AddObject(
//       path: "path/to/ArtifactsMy_dungeon.glb",
//       pos: new Vector3(0, 0, 0),
//       yaw: 0f,
//       scale: 1.0f,
//       groupName: "",
//       snapToTerrain: true,
//       terrain: gameTerrainChunk,
//       collisionPart: null,  // Use all meshes for collision
//       overrideCollisionSizeX: 0f,
//       overrideCollisionSizeZ: 0f,
//       useBVH: true  // <-- NEW PARAMETER: Enable BVH collision!
//   );
//
// ===========================================================================
// PARAMETER DETAILS
// ===========================================================================
//
// useBVH: bool (default: false)
//   - Set to TRUE to build a BVH tree for mesh-based collision
//   - Set to FALSE to use traditional AABB collision (faster but less accurate)
//   - Recommended: TRUE for complex static objects (buildings, dungeons, etc)
//   - Recommended: FALSE for simple objects (rocks, trees, etc)
//
// ===========================================================================
// HOW BVH WORKS INTERNALLY
// ===========================================================================
//
// 1. Object Loading Phase:
//    - All LOD0 meshes from the object are extracted
//    - Vertices and triangle indices are collected
//    - Node transforms are applied to vertices in model-local space
//
// 2. BVH Construction:
//    - Triangle data is organized into a binary tree (BVH)
//    - Each node covers a bounding volume (AABB) of its child triangles
//    - Leaf nodes contain sets of triangles (typically 4-8 per leaf)
//    - Root node bounds the entire mesh
//
// 3. Collision Detection (Every Frame):
//    - Character sphere/capsule checks against BVH
//    - Tree traversal starts from root
//    - Only overlapping BVH nodes are checked
//    - Precise sphere-to-triangle collision detection
//    - Push direction calculated from closest point on mesh
//
// 4. Performance:
//    - BVH traversal is O(log n) instead of O(n)
//    - Minimal frame-time impact (milliseconds for typical objects)
//    - One-time construction cost at object creation
//
// ===========================================================================
// COLLISION DETECTION FLOW
// ===========================================================================
//
// In CollisionHelper.cs (PushOutOfStaticObjects):
//
//   if (obj.UseBVHCollision && obj.CollisionBVH != null)
//   {
//       // Use precise BVH mesh collision
//       if (obj.CollisionBVH.SphereOverlaps(sphereCenter, radius))
//       {
//           Vector3 closestPt = obj.CollisionBVH.GetClosestPointOnMesh(result);
//           // Calculate push direction based on closest point on mesh
//           // Push character away from mesh surface
//       }
//   }
//   else
//   {
//       // Fallback to traditional AABB collision
//       // (backward compatible with existing objects)
//   }
//
// ===========================================================================
// EXAMPLE: COMPLETE SETUP
// ===========================================================================
//
//   void SetupStaticObjects(TerrainChunk terrain)
//   {
//       var staticMgr = new StaticObjectManager();
//
//       // Complex structure with BVH collision
//       staticMgr.AddObject(
//           "Artifacts/objects/my_dungeon.glb",
//           new Vector3(50, 0, 50),
//           yaw: 45f,
//           scale: 1.0f,
//           groupName: "",
//           snapToTerrain: true,
//           terrain: terrain,
//           collisionPart: null,
//           overrideCollisionSizeX: 0f,
//           overrideCollisionSizeZ: 0f,
//           useBVH: true  // <-- BVH enabled
//       );
//
//       // Simple object, faster with traditional AABB
//       staticMgr.AddObject(
//           "Artifacts/objects/big_rock.glb",
//           new Vector3(100, 0, 100),
//           yaw: 0f,
//           scale: 2.0f,
//           groupName: "",
//           snapToTerrain: true,
//           terrain: terrain,
//           collisionPart: null,
//           overrideCollisionSizeX: 0f,
//           overrideCollisionSizeZ: 0f,
//           useBVH: false  // <-- Traditional AABB (default)
//       );
//   }
//
// ===========================================================================
// PERFORMANCE GUIDELINES
// ===========================================================================
//
// When to use BVH (useBVH: true):
//   - Complex buildings with multiple rooms/doorways
//   - Objects with interior spaces (dungeons, caves, etc)
//   - Objects with detailed geometry (intricate structures)
//   - Objects where collision accuracy is critical
//
// When to use AABB (useBVH: false):
//   - Simple shapes (rocks, trees, props)
//   - Objects with convex or near-convex geometry
//   - When performance is more critical than accuracy
//   - Large numbers of similar simple objects
//
// Memory Impact:
//   - Each BVH uses ~40 bytes per triangle in the mesh
//   - Example: 1000-triangle dungeon = ~40KB
//   - Negligible for typical static objects
//
// Construction Time:
//   - ~1-5ms for typical 1000-triangle objects
//   - Happens during object loading (not per-frame)
//   - Can be parallelized if needed
//
// ===========================================================================
// TROUBLESHOOTING
// ===========================================================================
//
// Problem: Player still can't enter doorway even with BVH
// Solution:
//   1. Check that useBVH: true is set
//   2. Verify Console shows "[BVH] Built collision BVH for 'my_dungeon'"
//   3. Check that collision geometry matches visual mesh in GLB
//   4. Try increasing character radius if geometry is very tight
//   5. Use collisionPart parameter to exclude certain mesh parts
//
// Problem: Character falls through ceiling or walls intermittently
// Solution:
//   1. Increase character radius slightly (CharacterRadius = 0.45f)
//   2. Verify mesh normals are consistent (pointing outward)
//   3. Check for non-manifold mesh geometry
//   4. Consider using multiple simpler objects instead of one complex mesh
//
// Problem: Performance degradation with BVH
// Solution:
//   1. Check LOD0 triangle count (should be < 10000 for complex objects)
//   2. Use simpler collision meshes (separate collision model if possible)
//   3. Disable BVH for nearby copies of same object (use AABB instead)
//   4. Profile with Visual Studio Profiler to find bottleneck
//
// ===========================================================================
// API REFERENCE
// ===========================================================================
//
// StaticObject Properties:
//   - CollisionBVH: BVH? { get; set; }
//       The BVH tree, null if not built
//   - UseBVHCollision: bool { get; set; }
//       Whether to use BVH collision (true) or AABB fallback
//
// StaticObjectManager.AddObject():
//   - useBVH: bool = false
//       New parameter to enable BVH collision on this object
//
// BVH Class:
//   - Build(Vector3[] vertices, int[] indices): void
//       Construct the BVH tree from mesh data
//   - SphereOverlaps(Vector3 center, float radius): bool
//       Check if sphere overlaps any triangle
//   - GetClosestPointOnMesh(Vector3 sphereCenter): Vector3
//       Find closest point on mesh surface to given point
//
// ===========================================================================
// ADVANCED: CUSTOM COLLISION MESHES
// ===========================================================================
//
// If your GLB has a specific collision mesh part, you can use it:
//
//   staticMgr.AddObject(
//       "Artifacts/objects/my_dungeon.glb",
//       new Vector3(50, 0, 50),
//       collisionPart: "Collision",  // Only use meshes named "*Collision*"
//       useBVH: true
//   );
//
// This allows you to create simplified collision geometry separate
// from the visual mesh, improving both performance and accuracy.
//
// ===========================================================================
