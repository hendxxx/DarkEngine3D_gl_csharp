import re

with open('Engine/Scene/GameScene.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# ================================================================
# Helper: find matching closing brace for a code block
# ================================================================
def find_block_end(text, start_pos):
    """Find the position of the matching '}' starting from start_pos (the position of '{')"""
    brace_count = 1
    pos = start_pos
    while pos < len(text) and brace_count > 0:
        pos += 1
        if pos >= len(text):
            break
        ch = text[pos]
        if ch == '{':
            brace_count += 1
        elif ch == '}':
            brace_count -= 1
    return pos  # position of the matching '}'

# ================================================================
# 1. Phase 2A - Replace the inner foreach with Octree + fallback
# ================================================================

# Find the manager loop in Phase 2A (first occurrence of the foreach inside it)
idx_mgr_loop = content.find('for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)')
print(f"Phase 2A manager loop at position {idx_mgr_loop}")

# Find the foreach inside it
idx_foreach_p2a = content.find('foreach (var sobj in mgr.GetObjects())', idx_mgr_loop)
print(f"Phase 2A foreach at position {idx_foreach_p2a}")

# Find the opening brace of the foreach body
brace_start_p2a = content.find('{', idx_foreach_p2a)
# Find the matching closing brace
brace_end_p2a = find_block_end(content, brace_start_p2a)
print(f"Phase 2A foreach body: {brace_start_p2a} to {brace_end_p2a}")

# Extract the old foreach body (including the braces)
old_foreach_p2a = content[idx_foreach_p2a:brace_end_p2a + 1]

print(f"Old Phase 2A foreach starts with: {old_foreach_p2a[:80]}")
print(f"Old Phase 2A foreach ends with: ...{old_foreach_p2a[-50:]}")

# ================================================================
# 2. Phase 2B - Same analysis
# ================================================================

# Find second occurrence of the manager loop (Phase 2B)
idx_phase2b_start = content.find('// 2B. Test static objects against occluders')
idx_mgr_loop_2 = content.find('for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)', 
                               idx_phase2b_start)
print(f"\\nPhase 2B manager loop at position {idx_mgr_loop_2}")

# Find the foreach inside Phase 2B
idx_foreach_p2b = content.find('foreach (var sobj in mgr.GetObjects())', idx_mgr_loop_2)
print(f"Phase 2B foreach at position {idx_foreach_p2b}")

brace_start_p2b = content.find('{', idx_foreach_p2b)
brace_end_p2b = find_block_end(content, brace_start_p2b)
print(f"Phase 2B foreach body: {brace_start_p2b} to {brace_end_p2b}")

old_foreach_p2b = content[idx_foreach_p2b:brace_end_p2b + 1]
print(f"Old Phase 2B foreach ends with: ...{old_foreach_p2b[-100:]}")

# ================================================================
# 3. Create new Phase 2A foreach with Octree integration
# ================================================================

# Extract the original foreach body content (without the outer braces)
old_body_p2a = content[brace_start_p2a + 1:brace_end_p2a].strip()
# Get the indentation of the closing brace
close_indent = ''
for ch in reversed(content[brace_end_p2a::-1]):
    if ch in ' \t':
        close_indent += ch
    else:
        break

# Build the new code
NEW_FOREACH_P2A = f'''                if (mgr.UseOctree && mgr.SpatialOctree != null)
                {{
                    // Octree-based frustum culling
                    var _octreeIndices = new List<int>();
                    mgr.SpatialOctree.QueryFrustum(frustumPlanes, _octreeIndices);
                    var _octreeSet = new HashSet<int>(_octreeIndices);
                    var _mgrObjs = mgr.GetObjects();
                    for (int _oi = 0; _oi < _mgrObjs.Count; _oi++)
                    {{
                        if (!_octreeSet.Contains(_oi))
                        {{
                            _mgrObjs[_oi].IsVisible = false;
                            _staticFrustumCulled++;
                            continue;
                        }}
                        var sobj = _mgrObjs[_oi];
''' + old_body_p2a + f'''
                    }}
                }}
                else
                {{
                    foreach (var sobj in mgr.GetObjects())
                    {{
''' + old_body_p2a + f'''
                    }}
                }}'''

# ================================================================
# 4. Create new Phase 2B foreach with Octree integration
# ================================================================

old_body_p2b = content[brace_start_p2b + 1:brace_end_p2b].strip()

NEW_FOREACH_P2B = f'''                if (mgr.UseOctree && mgr.SpatialOctree != null)
                {{
                    // Octree-based occlusion culling (frustum + occlusion combined)
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
                    
                    // Reset all to invisible, only Octree-visible pass
                    for (int _oi = 0; _oi < _mgrObjs.Count; _oi++)
                        _mgrObjs[_oi].IsVisible = false;
                    
                    foreach (int _idx in _octreeVisible)
                    {{
                        if (_idx < 0 || _idx >= _mgrObjs.Count) continue;
                        var sobj = _mgrObjs[_idx];
                        
                        float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                        if (distSq > farSq) continue;
                        
                        // Terrain occlusion (HiZ only)
                        if (useHiZ)
                        {{
                            var rootAABB = sobj.OcclusionBVH != null
                                ? (sobj.OcclusionBVH.Root?.Bounds ?? sobj.CachedWorldAABB)
                                : sobj.CachedWorldAABB;
                            if (_hizOcc!.IsTerrainOccluded(_camera.Position, rootAABB))
                                continue;
                        }}
                        
                        sobj.IsVisible = true;
                        
                        // Register HiZ occluders for visible objects
                        if (useHiZ && sobj.IsOccluder)
                        {{
                            if (sobj.OcclusionBVH != null)
                                _hizOcc!.RegisterMeshOccluder(sobj.OcclusionBVH);
                            else
                                _hizOcc!.RegisterOccluder(sobj.CachedWorldAABB);
                        }}
                    }}
                }}
                else
                {{
                    foreach (var sobj in mgr.GetObjects())
                    {{
''' + old_body_p2b + f'''
                    }}
                }}'''

old_body_p2b_short = old_body_p2b
if len(old_body_p2b_short) < 100:
    print("ERROR: Phase 2B body too short - likely wrong block")
    exit(1)

# ================================================================
# 5. Apply replacements
# ================================================================

# Replace Phase 2A
if old_foreach_p2a in content:
    content = content.replace(old_foreach_p2a, NEW_FOREACH_P2A, 1)
    print(f"\\nPhase 2A: Applied! New code added.")
else:
    print("ERROR: Phase 2A exact pattern not found in content!")
    print(f"Expected pattern starts: {old_foreach_p2a[:80]}")

# Replace Phase 2B
if old_foreach_p2b in content:
    content = content.replace(old_foreach_p2b, NEW_FOREACH_P2B, 1)
    print("Phase 2B: Applied! New code added.")
else:
    print("ERROR: Phase 2B exact pattern not found in content!")
    print(f"Expected pattern starts: {old_foreach_p2b[:80]}")

# Verify brace balance
open_braces = content.count('{')
close_braces = content.count('}')
print(f"\\nBrace balance: open={open_braces}, close={close_braces}, balanced={open_braces == close_braces}")

if open_braces == close_braces:
    with open('Engine/Scene/GameScene.cs', 'w', encoding='utf-8') as f:
        f.write(content)
    print("\\nFile written successfully!")
else:
    print("\\nERROR: Unbalanced braces, NOT writing file!")
