with open('Engine/Scene/GameScene.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Add Octree.ResetFrameStats() at the start of rendering (before Phase 1B)
# Find the frustumVP computation line (Phase 1B start)
marker_reset = "var frustumVP = _camera.GetViewMatrix() * _camera.GetProjectionMatrix();"
reset_code = "Octree.ResetFrameStats();\n                "
content = content.replace(marker_reset, reset_code + marker_reset, 1)
print("Added ResetFrameStats()!")

# 2. Add Octree stats to title7
# Find the current title7 assignment
old_title7 = """                int staticCulledAll = _staticFrustumCulled + _staticTerrainOccluded + _staticOcclusionCulled;
                title7 = $\" OC [{ocMode}]: {_occlusionCulling!.VisibleCount}v / {_occlusionCulling.OccludedCount}o | static: {staticCulledAll}cld ({_staticFrustumCulled}f {_staticTerrainOccluded}t {_staticOcclusionCulled}o)\";"""

new_title7 = """                int staticCulledAll = _staticFrustumCulled + _staticTerrainOccluded + _staticOcclusionCulled;
                int octNodes = Octree.TotalNodesVisited;
                int octFCulled = Octree.TotalNodesFrustumCulled;
                int octOccluded = Octree.TotalNodesOcclusionCulled;
                title7 = $\" OC [{ocMode}]: {_occlusionCulling!.VisibleCount}v / {_occlusionCulling.OccludedCount}o | static: {staticCulledAll}cld ({_staticFrustumCulled}f {_staticTerrainOccluded}t {_staticOcclusionCulled}o) | Octree: {octNodes}nd {octFCulled}fc {octOccluded}occ\";"""

if old_title7 in content:
    content = content.replace(old_title7, new_title7, 1)
    print("Added Octree stats to title7!")
else:
    print("WARNING: title7 pattern not found!")
    # Find it
    idx = content.find("staticCulledAll = _staticFrustumCulled")
    if idx >= 0:
        print(f"Found at {idx}: {content[idx:idx+300]}")

# 3. Verify braces
open_b = content.count('{')
close_b = content.count('}')
print(f"Braces: {open_b} open, {close_b} close, balanced={open_b == close_b}")

if open_b == close_b:
    with open('Engine/Scene/GameScene.cs', 'w', encoding='utf-8') as f:
        f.write(content)
    print("File written!")
else:
    print("ERROR: Unbalanced braces!")
