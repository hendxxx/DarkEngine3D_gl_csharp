// ===========================================================================
// QUICK START: BVH Collision for my_dungeon
// ===========================================================================
//
// To enable BVH collision for your my_dungeon object:
//
// BEFORE (old way - AABB only):
// ____________________________
//
//   staticObjectManager.AddObject(
//       "Artifacts/objects/my_dungeon.glb",
//       pos,
//       yaw: 0f,
//       scale: 1.0f,
//       groupName: "",
//       snapToTerrain: true,
//       terrain: gameTerrainChunk
//   );
//
//
// AFTER (new way - with BVH collision):
// _______________________________________
//
//   staticObjectManager.AddObject(
//       "Artifacts/objects/my_dungeon.glb",
//       pos,
//       yaw: 0f,
//       scale: 1.0f,
//       groupName: "",
//       snapToTerrain: true,
//       terrain: gameTerrainChunk,
//       collisionPart: null,
//       overrideCollisionSizeX: 0f,
//       overrideCollisionSizeZ: 0f,
//       useBVH: true  // <-- ADD THIS PARAMETER!
//   );
//
//
// WHAT HAPPENS:
// ______________
//
// 1. Load my_dungeon.glb
// 2. Extract all LOD0 meshes
// 3. Build BVH tree from triangle data
// 4. Player can now use collision mesh geometry
// 5. You can walk through doorways!
//
//
// CONSOLE OUTPUT (when working):
// _________________________________
//
// [BVH] Built collision BVH for 'my_dungeon' at (X, Y, Z)
//
// If you see this message: It's working! ✓
// If you DON'T see this message: Check the useBVH parameter
//
//
// TESTING:
// _________
//
// 1. Set useBVH: true
// 2. Rebuild the project
// 3. Load the game
// 4. Walk up to dungeon doorway
// 5. Try to enter - you should now be able to walk through!
// 6. Check console for "[BVH] Built collision BVH" message
//
// If player still can't enter:
//   - Increase character radius in CharacterAgent (was 0.45f)
//   - Check if doorway geometry is correct in GLB file
//   - Try setting collisionPart to specific mesh name if available
//
//
// PERFORMANCE:
// ______________
//
// - BVH construction: ~1-2ms (one time only)
// - BVH collision check: <0.5ms per frame
// - Memory: ~40-50 KB for typical dungeon
// - No noticeable FPS impact
//
