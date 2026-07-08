with open('Engine/Scene/GameScene.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# Find Phase 1B foreach loop - it's the FIRST occurrence
# Pattern to replace: the foreach + the first few lines of the loop body
# We need to add Octree pre-filter + IsVisible check

pattern_start = """                        foreach (var sobj in mgr.GetObjects())
                        {
                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);"""

new_code = """                        // Octree frustum pre-filter: pre-cull objects outside frustum (saves loop body work)
                        if (mgr.UseOctree && mgr.SpatialOctree != null)
                        {
                            var _octreeIndices = new List<int>();
                            mgr.SpatialOctree.QueryFrustum(frustumPlanes, _octreeIndices);
                            var _octreeSet = new HashSet<int>(_octreeIndices);
                            var _mgrObjs = mgr.GetObjects();
                            for (int _oi = 0; _oi < _mgrObjs.Count; _oi++)
                                _mgrObjs[_oi].IsVisible = _octreeSet.Contains(_oi);
                        }

                        foreach (var sobj in mgr.GetObjects())
                        {
                            // Skip objects pre-culled by Octree frustum test
                            if (mgr.UseOctree && mgr.SpatialOctree != null && !sobj.IsVisible)
                                continue;

                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);"""

if pattern_start not in content:
    print("ERROR: Pattern not found!")
    exit(1)

content = content.replace(pattern_start, new_code, 1)

# Verify braces
open_b = content.count('{')
close_b = content.count('}')
print(f"Braces: {open_b} open, {close_b} close, balanced={open_b == close_b}")

if open_b == close_b:
    with open('Engine/Scene/GameScene.cs', 'w', encoding='utf-8') as f:
        f.write(content)
    print("File written!")
else:
    print("ERROR: Unbalanced braces!")
