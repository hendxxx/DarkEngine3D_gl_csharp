with open('Engine/Scene/GameScene.cs', 'r', encoding='utf-8') as f:
    lines = f.readlines()
    content = ''.join(lines)

# Find the mgr loops by position
pattern = "if (mgr == null) continue;\n                        foreach (var sobj in mgr.GetObjects())"

positions = []
search_start = 0
while True:
    pos = content.find(pattern, search_start)
    if pos < 0:
        break
    line_no = content[:pos].count('\n') + 1
    positions.append((pos, line_no))
    search_start = pos + 1

print(f"Found {len(positions)} manager loops:")
for pos, line in positions:
    print(f"  Line {line}, position {pos}")

# Based on earlier output:
# Line 872 = Phase 1B (loop #1)
# Line 1034 = Phase 2B (loop #3)
# Loop #2 is between them = Phase 2A

if len(positions) < 3:
    print("ERROR: Expected at least 3 loops")
    exit(1)

pos_1b, line_1b = positions[0]
pos_2a, line_2a = positions[1]
pos_2b, line_2b = positions[2]

print(f"\nPhase 1B: line {line_1b}")
print(f"Phase 2A: line {line_2a}")
print(f"Phase 2B: line {line_2b}")

# ================================================================
# Phase 2A: Replace loop #2
# ================================================================
old_p2a_pat = content[pos_2a:pos_2a + len(pattern)]
new_p2a = """                        if (mgr == null) continue;

                        // Octree frustum pre-filter: pre-mark objects outside frustum as invisible
                        if (mgr.UseOctree && mgr.SpatialOctree != null)
                        {
                            var _octreeIndices = new List<int>();
                            mgr.SpatialOctree.QueryFrustum(frustumPlanes, _octreeIndices);
                            var _octreeSet = new HashSet<int>(_octreeIndices);
                            var _mgrObjs = mgr.GetObjects();
                            for (int _oi = 0; _oi < _mgrObjs.Count; _oi++)
                                _mgrObjs[_oi].IsVisible = _octreeSet.Contains(_oi);
                        }

                        foreach (var sobj in mgr.GetObjects())"""

content = content[:pos_2a] + new_p2a + content[pos_2a + len(pattern):]
print("Phase 2A: OK")

# ================================================================
# Phase 2B: Replace loop #3 (position may have shifted)
# ================================================================
shift = len(new_p2a) - len(pattern)
pos_2b_shifted = pos_2b + shift

# Verify we can still find the pattern at the shifted position
pat_at_2b = content[pos_2b_shifted:pos_2b_shifted + len(old_p2a_pat)]
if pat_at_2b == old_p2a_pat:
    print("Phase 2B pattern found at shifted position")
else:
    print(f"ERROR: Phase 2B pattern mismatch at position {pos_2b_shifted}")
    # Try to find it nearby
    nearby = content.find(pattern, pos_2b_shifted - 50)
    if nearby >= 0:
        pos_2b_shifted = nearby
        print(f"Found Phase 2B nearby at {nearby}")
    else:
        exit(1)

new_p2b = """                        if (mgr == null) continue;

                        // Octree occlusion pre-filter: skip objects in occluded Octree subtrees
                        if (mgr.UseOctree && mgr.SpatialOctree != null)
                        {
                            var _octreeVisible = new List<int>();
                            var _mgrObjs = mgr.GetObjects();

                            Func<int, BVH?> _getSelfBVH = (idx) =>
                                (idx >= 0 && idx < _mgrObjs.Count) ? _mgrObjs[idx].OcclusionBVH : null;

                            mgr.SpatialOctree.QueryOccluded(
                                frustumPlanes,
                                _camera.Position,
                                useHiZ ? null : _occlusionCulling.GetAABBOccluders(),
                                useHiZ ? null : _occlusionCulling.GetMeshOccluders(),
                                _getSelfBVH,
                                _octreeVisible
                            );

                            var _octreeOcclusionSet = new HashSet<int>(_octreeVisible);
                            for (int _oi = 0; _oi < _mgrObjs.Count; _oi++)
                                if (!_octreeOcclusionSet.Contains(_oi))
                                    _mgrObjs[_oi].IsVisible = false;
                        }

                        foreach (var sobj in mgr.GetObjects())"""

content = content[:pos_2b_shifted] + new_p2b + content[pos_2b_shifted + len(pattern):]
print("Phase 2B: OK")

# ================================================================
# Final brace check
# ================================================================
open_braces = content.count('{')
close_braces = content.count('}')
print(f"\nBraces: {{ x{open_braces}, }} x{close_braces}, balanced={open_braces == close_braces}")

if open_braces == close_braces:
    with open('Engine/Scene/GameScene.cs', 'w', encoding='utf-8') as f:
        f.write(content)
    print("File written!")
else:
    print("ERROR: Unbalanced braces - NOT writing!")
