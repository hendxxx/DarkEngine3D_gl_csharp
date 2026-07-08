with open('Engine/Scene/GameScene.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# ================================================================
# Strategy: Find the 3 manager loops by their context
# 1. Phase 1B (occluder registration): contains "sobj.IsOccluder" 
# 2. Phase 2A (frustum culling): after frustumPlanes, contains "distSq"  
# 3. Phase 2B (occlusion culling): after "Phase 2B"
# ================================================================

# Find the pattern: "if (mgr == null) continue;\n                        foreach (var sobj in mgr.GetObjects())"
pattern = "if (mgr == null) continue;\n                        foreach (var sobj in mgr.GetObjects())"

# Find ALL occurrences
positions = []
search_start = 0
while True:
    pos = content.find(pattern, search_start)
    if pos < 0:
        break
    positions.append(pos)
    search_start = pos + 1

print(f"Found {len(positions)} occurrences of the pattern")
for i, p in enumerate(positions):
    print(f"  #{i+1} at position {p}")
    # Show context around it
    ctx = content[p:p+200].replace('\n', '\\n')
    print(f"     Context: {ctx[:120]}")

if len(positions) < 3:
    print("ERROR: Expected at least 3 occurrences!")
    exit(1)

# The three positions should be:
# pos[0] = Phase 1B (before frustumPlanes)
# pos[1] = Phase 2A (frustum culling)
# pos[2] = Phase 2B (occlusion culling)

# Verify by checking the content around each
for i, p in enumerate(positions):
    context_start = max(0, p - 100)
    context = content[context_start:p]
    has_frustum = 'frustumPlanes' in context
    has_phase2b = 'Phase 2B' in context or '2B.' in context
    has_occluder = 'IsOccluder' in content[p:p+500]
    print(f"  #{i+1}: before frustumPlanes={has_frustum}, has Phase 2B={has_phase2b}, has IsOccluder={has_occluder}")

# Based on context, assign:
# Position where frustumPlanes is visible = Phase 2A and Phase 2B
# But frustumPlanes is declared BEFORE Phase 1B in the original code structure
# Let me think again...

# Actually, looking at the code structure:
# 1. frustumVP/frustumPlanes declared near Phase 1B/2A transition
# 2. Phase 1B (occluder registration) - mgr loop #1
# 3. Phase 2A (frustum culling) - mgr loop #2 (with _staticFrustumCulled)
# 4. Phase 2B (occlusion culling) - mgr loop #3 (with "2B." or "occluded" checks)

# Let me check the context
pos_1b = positions[0]  # Should be Phase 1B
pos_2a = positions[1]  # Should be Phase 2A  
pos_2b = positions[2]  # Should be Phase 2B

ctx_2a = content[pos_2a:pos_2a+300]
ctx_2b = content[pos_2b:pos_2b+300]

print(f"\nPhase 2A context (first 100 chars): {ctx_2a[:100]}")
print(f"Phase 2B context (first 100 chars): {ctx_2b[:100]}")

# Verify Phase 2B contains "occluded" or "IsOccludedByOccluders" or "terrain"
has_occlusion_check = 'occluded' in ctx_2b.lower() or 'terrain' in ctx_2b.lower()
print(f"Phase 2B has occlusion keywords: {has_occlusion_check}")

if not has_occlusion_check:
    print("WARNING: Phase 2B may be at wrong position!")
    # Try positions[1] and positions[2]
    ctx_alt = content[positions[1]:positions[1]+300]
    print(f"Alternative (pos 1): {ctx_alt[:100]}")
    
    # Check which position has occlusion keywords
    for i, p in enumerate(positions):
        ctx = content[p:p+500]
        if 'occluded' in ctx.lower() or 'IsOccluded' in ctx or 'testAABB' in ctx:
            print(f"  pos #{i+1} HAS occlusion keywords")
        if '_staticFrustumCulled' in ctx:
            print(f"  pos #{i+1} HAS frustum cull keywords")
    
    exit(1)

# ================================================================
# Phase 2A: Add Octree frustum pre-filter before the SECOND occurrence
# ================================================================
old_p2a = content[pos_2a:pos_2a + len(pattern)]
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

# Update positions for Phase 2B (shifted by new_p2a length)
shift = len(new_p2a) - len(pattern)
pos_2b_shifted = pos_2b + shift

print(f"\nPhase 2A: Replaced at original position {pos_2a}")

# ================================================================
# Phase 2B: Add Octree occlusion pre-filter before the THIRD occurrence
# ================================================================
old_p2b_pattern = "if (mgr == null) continue;\n                        foreach (var sobj in mgr.GetObjects())"

# Find the occurrence AFTER pos_2b_shifted
pos_2b_new = content.find(old_p2b_pattern, pos_2b_shifted - 10)
if pos_2b_new < 0:
    print("ERROR: Could not find Phase 2B pattern after shift!")
    exit(1)

print(f"Phase 2B: Found at new position {pos_2b_new}")

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

content = content[:pos_2b_new] + new_p2b + content[pos_2b_new + len(old_p2b_pattern):]
print("Phase 2B: Replaced!")

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
