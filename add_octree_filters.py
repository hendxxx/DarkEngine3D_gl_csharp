with open('Engine/Scene/GameScene.cs', 'r', encoding='utf-8') as f:
    content = f.read()
    original = content

# ================================================================
# Phase 2A: Add Octree frustum pre-filter after "if (mgr == null) continue;"
# ================================================================
old_p2a = """                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())"""

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

if old_p2a not in content:
    print("ERROR: Phase 2A pattern not found!")
    # Debug: find it
    idx = content.find('if (mgr == null) continue;')
    if idx >= 0:
        print("Found at idx", idx)
        print(content[idx:idx+200])
    exit(1)

content = content.replace(old_p2a, new_p2a, 1)
print("Phase 2A: OK!")

# ================================================================
# Phase 2B: Find the second foreach and add Octree occlusion pre-filter
# ================================================================
# Find Phase 2B section
idx_2b = content.find('// 2B. Test static objects against occluders')
if idx_2b < 0:
    print("ERROR: Phase 2B section not found!")
    exit(1)

# Find the second "if (mgr == null) continue;" AFTER the Phase 2B section
idx_mgr_null_2 = content.find('if (mgr == null) continue;', idx_2b)
if idx_mgr_null_2 < 0:
    print("ERROR: Phase 2B mgr null check not found!")
    exit(1)

# Verify it's followed by a foreach
idx_foreach_2 = content.find('foreach (var sobj in mgr.GetObjects())', idx_mgr_null_2)
if idx_foreach_2 < 0 or idx_foreach_2 - idx_mgr_null_2 > 200:
    print("ERROR: Phase 2B foreach not found after null check!")
    exit(1)

print(f"Phase 2B: mgr null at {idx_mgr_null_2}, foreach at {idx_foreach_2}")
print(content[idx_mgr_null_2:idx_mgr_null_2+200])

# Extract the exact text
old_p2b = content[idx_mgr_null_2:content.find('\n', idx_foreach_2) + 1]

# Actually, just replace the specific pattern for Phase 2B
old_p2b_pattern = """                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())"""

# But we need to match the SECOND occurrence (after Phase 2B)
# Find the second occurrence
first_occurrence = content.find(old_p2b_pattern)
second_occurrence = content.find(old_p2b_pattern, first_occurrence + 1)

if second_occurrence < 0:
    print("ERROR: Second occurrence of Phase 2B pattern not found!")
    exit(1)

print(f"First occurrence at {first_occurrence}, second at {second_occurrence}")

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
                            
                            // Register HiZ occluders for Octree-visible objects
                            if (useHiZ)
                            {
                                foreach (int _idx in _octreeVisible)
                                {
                                    if (_idx >= 0 && _idx < _mgrObjs.Count)
                                    {
                                        var _sobj = _mgrObjs[_idx];
                                        if (_sobj.IsOccluder)
                                        {
                                            if (_sobj.OcclusionBVH != null)
                                                _hizOcc!.RegisterMeshOccluder(_sobj.OcclusionBVH);
                                            else
                                                _hizOcc!.RegisterOccluder(_sobj.CachedWorldAABB);
                                        }
                                    }
                                }
                            }
                        }

                        foreach (var sobj in mgr.GetObjects())"""

# Replace only the second occurrence
content = content[:second_occurrence] + new_p2b + content[second_occurrence + len(old_p2b_pattern):]

print("Phase 2B: OK!")

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
