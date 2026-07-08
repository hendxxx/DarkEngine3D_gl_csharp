with open('Engine/Scene/GameScene.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# ================================================================
# Find brace-balanced blocks
# ================================================================
def find_matching_brace(text, start_pos):
    if text[start_pos] != '{':
        return -1
    depth = 1
    pos = start_pos
    while pos < len(text) - 1 and depth > 0:
        pos += 1
        if text[pos] == '{':
            depth += 1
        elif text[pos] == '}':
            depth -= 1
    return pos

# ================================================================
# 1. Phase 2A - find the foreach body
# ================================================================
idx_mgr = content.find('for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)')
print("Mgr loop at idx=" + str(idx_mgr))

mgr_brace = content.find('{', idx_mgr)
mgr_brace_end = find_matching_brace(content, mgr_brace)

idx_foreach = content.find('foreach (var sobj in mgr.GetObjects())', idx_mgr, mgr_brace_end)
print("Foreach at idx=" + str(idx_foreach))

foreach_brace = content.find('{', idx_foreach, mgr_brace_end)
foreach_brace_end = find_matching_brace(content, foreach_brace)
print("Foreach body: " + str(foreach_brace) + " -> " + str(foreach_brace_end))

old_foreach_full = content[idx_foreach:foreach_brace_end + 1]
old_body = content[foreach_brace + 1:foreach_brace_end]

print("Old body length: " + str(len(old_body)))
print("Old body braces: open=" + str(old_body.count('{')) + " close=" + str(old_body.count('}')))

old_opens = old_foreach_full.count('{')
old_closes = old_foreach_full.count('}')
print("Old foreach full braces: open=" + str(old_opens) + " close=" + str(old_closes))

# ================================================================
# 2. Phase 2B - same analysis
# ================================================================
idx_2b = content.find('// 2B. Test static objects against occluders')
print()
print("Phase 2B at idx=" + str(idx_2b))

idx_mgr_2 = content.find('for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)', idx_2b)
mgr_brace_2 = content.find('{', idx_mgr_2)
mgr_brace_end_2 = find_matching_brace(content, mgr_brace_2)

idx_foreach_2 = content.find('foreach (var sobj in mgr.GetObjects())', idx_mgr_2, mgr_brace_end_2)
print("Foreach2 at idx=" + str(idx_foreach_2))

foreach_brace_2 = content.find('{', idx_foreach_2, mgr_brace_end_2)
foreach_brace_end_2 = find_matching_brace(content, foreach_brace_2)
print("Foreach2 body: " + str(foreach_brace_2) + " -> " + str(foreach_brace_end_2))

old_foreach_full_2 = content[idx_foreach_2:foreach_brace_end_2 + 1]
old_body_2 = content[foreach_brace_2 + 1:foreach_brace_end_2]

op2 = old_foreach_full_2.count('{')
cl2 = old_foreach_full_2.count('}')
print("Old foreach2 full braces: open=" + str(op2) + " close=" + str(cl2))

# ================================================================
# 3. Build new code - use simple string concat, not f-strings
# ================================================================

# New Phase 2A with Octree
new_p2a = """                        if (mgr.UseOctree && mgr.SpatialOctree != null)
                        {
                            // Octree-based frustum culling
                            var _octreeIndices = new List<int>();
                            mgr.SpatialOctree.QueryFrustum(frustumPlanes, _octreeIndices);
                            var _octreeSet = new HashSet<int>(_octreeIndices);
                            var _mgrObjs = mgr.GetObjects();
                            for (int _oi = 0; _oi < _mgrObjs.Count; _oi++)
                            {
                                if (!_octreeSet.Contains(_oi))
                                {
                                    _mgrObjs[_oi].IsVisible = false;
                                    _staticFrustumCulled++;
                                    continue;
                                }
                                var sobj = _mgrObjs[_oi];
""" + old_body + """
                            }
                        }
                        else
                        {
                            foreach (var sobj in mgr.GetObjects())
                            {
""" + old_body + """
                            }
                        }"""

op_new = new_p2a.count('{')
cl_new = new_p2a.count('}')
print()
print("New P2A braces: open=" + str(op_new) + " close=" + str(cl_new) + " diff=" + str(op_new - cl_new))

# New Phase 2B with Octree
new_p2b = """                        if (mgr.UseOctree && mgr.SpatialOctree != null)
                        {
                            // Octree-based occlusion culling
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

                            for (int _oi = 0; _oi < _mgrObjs.Count; _oi++)
                                _mgrObjs[_oi].IsVisible = false;

                            foreach (int _idx in _octreeVisible)
                            {
                                if (_idx < 0 || _idx >= _mgrObjs.Count) continue;
                                var sobj = _mgrObjs[_idx];

                                float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                                if (distSq > farSq) continue;

                                if (useHiZ)
                                {
                                    var rootAABB = sobj.OcclusionBVH != null
                                        ? (sobj.OcclusionBVH.Root?.Bounds ?? sobj.CachedWorldAABB)
                                        : sobj.CachedWorldAABB;
                                    if (_hizOcc!.IsTerrainOccluded(_camera.Position, rootAABB))
                                        continue;
                                }

                                sobj.IsVisible = true;

                                if (useHiZ && sobj.IsOccluder)
                                {
                                    if (sobj.OcclusionBVH != null)
                                        _hizOcc!.RegisterMeshOccluder(sobj.OcclusionBVH);
                                    else
                                        _hizOcc!.RegisterOccluder(sobj.CachedWorldAABB);
                                }
                            }
                        }
                        else
                        {
                            foreach (var sobj in mgr.GetObjects())
                            {
""" + old_body_2 + """
                            }
                        }"""

op_new2 = new_p2b.count('{')
cl_new2 = new_p2b.count('}')
print("New P2B braces: open=" + str(op_new2) + " close=" + str(cl_new2) + " diff=" + str(op_new2 - cl_new2))

# ================================================================
# 4. Apply replacements
# ================================================================
if old_foreach_full not in content:
    print("ERROR: Phase 2A pattern not found in file!")
    exit(1)

if old_foreach_full_2 not in content:
    print("ERROR: Phase 2B pattern not found in file!")
    exit(1)

content = content.replace(old_foreach_full, new_p2a, 1)
print()
print("Phase 2A: Replaced!")

content = content.replace(old_foreach_full_2, new_p2b, 1)
print("Phase 2B: Replaced!")

# ================================================================
# 5. Final brace check
# ================================================================
total_open = content.count('{')
total_close = content.count('}')
print()
print("Final brace count: open=" + str(total_open) + " close=" + str(total_close) + " balanced=" + str(total_open == total_close))

if total_open == total_close:
    with open('Engine/Scene/GameScene.cs', 'w', encoding='utf-8') as f:
        f.write(content)
    print("File written successfully!")
else:
    print("ERROR: Unbalanced braces - NOT writing!")
