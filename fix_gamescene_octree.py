with open('Engine/Scene/GameScene.cs', 'r', encoding='utf-8') as f:
    content = f.read()

# ============================================================
# Phase 2A: Replace the inner foreach over objects with Octree walk
# ============================================================
# Find the start of Phase 2A inner foreach loop
old_phase2a = """                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())
                        {
                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                            if (distSq >= _camera.FarDist * _camera.FarDist) { sobj.IsVisible = false; _staticFrustumCulled++; continue; }
                            // Frustum cull: skip objects outside camera frustum
                            if (!StaticObjectManager.IsAABBInFrustum(frustumPlanes, sobj.CachedWorldAABB, 3f))
                            {
                                sobj.IsVisible = false;
                                continue;
            """

new_phase2a = """                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        
                        // ── Octree-based frustum culling (faster for large scenes) ──
                        if (mgr.UseOctree && mgr.SpatialOctree != null)
                        {
                            var octreeVisible = new List<int>();
                            mgr.SpatialOctree.QueryFrustum(frustumPlanes, octreeVisible);
                            
                            // Mark all objects as invisible first, then selectively set visible
                            var objs = mgr.GetObjects();
                            for (int oi = 0; oi < objs.Count; oi++)
                                objs[oi].IsVisible = false;
                            
                            // Pre-compute farDistSq and budgetFarSq for distance checks
                            float farDistSq = _camera.FarDist * _camera.FarDist;
                            float budgetFarSq = _budgetFarSq;
                            
                            foreach (int idx in octreeVisible)
                            {
                                if (idx < 0 || idx >= objs.Count) continue;
                                var sobj = objs[idx];
                                
                                float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                                if (distSq >= farDistSq) { _staticFrustumCulled++; continue; }
                                if (distSq > budgetFarSq && !sobj.IsOccluder) { _staticFrustumCulled++; continue; }
                                
                                bool terrainOccluded = false;
                                if (!useHiZ)
                                {
                                    if (mgr.SkipTerrainRayMarch)
                                    {
                                        float nearSq = Config.OcclusionConfig.TerrainOcclusionNearDist * Config.OcclusionConfig.TerrainOcclusionNearDist;
                                        if (distSq < nearSq)
                                        {
                                            var aabb = sobj.CachedWorldAABB;
                                            Vector3 bottomCenter = new(
                                                (aabb.Min.X + aabb.Max.X) * 0.5f,
                                                aabb.Min.Y,
                                                (aabb.Min.Z + aabb.Max.Z) * 0.5f
                                            );
                                            float camTerrainH = _gameTerrainChunk.GetHeightAt(_camera.Position.X, _camera.Position.Z);
                                            if (_camera.Position.Y >= camTerrainH - 0.5f)
                                            {
                                                Vector3 dir = bottomCenter - _camera.Position;
                                                float totalDist = dir.Length();
                                                if (totalDist >= 0.5f)
                                                {
                                                    dir /= totalDist;
                                                    const int numSamples = 8;
                                                    float stepSize = totalDist / numSamples;
                                                    for (int s = 1; s < numSamples; s++)
                                                    {
                                                        Vector3 samplePos = _camera.Position + dir * (s * stepSize);
                                                        float terrainH = _gameTerrainChunk.GetHeightAt(samplePos.X, samplePos.Z);
                                                        if (samplePos.Y < terrainH - 0.3f) { terrainOccluded = true; break; }
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            Vector3 midPoint = _camera.Position + (sobj.Position - _camera.Position) * 0.5f;
                                            float midTerrainH = _gameTerrainChunk.GetHeightAt(midPoint.X, midPoint.Z);
                                            float camTerrainH = _gameTerrainChunk.GetHeightAt(_camera.Position.X, _camera.Position.Z);
                                            if (_camera.Position.Y >= camTerrainH - 0.5f && midPoint.Y < midTerrainH - 0.5f)
                                                terrainOccluded = true;
                                        }
                                    }
                                    else
                                    {
                                        var aabb = sobj.CachedWorldAABB;
                                        Vector3[] corners = new Vector3[8]
                                        {
                                            new(aabb.Min.X, aabb.Min.Y, aabb.Min.Z),
                                            new(aabb.Max.X, aabb.Min.Y, aabb.Min.Z),
                                            new(aabb.Max.X, aabb.Max.Y, aabb.Min.Z),
                                            new(aabb.Min.X, aabb.Max.Y, aabb.Min.Z),
                                            new(aabb.Min.X, aabb.Min.Y, aabb.Max.Z),
                                            new(aabb.Max.X, aabb.Min.Y, aabb.Max.Z),
                                            new(aabb.Max.X, aabb.Max.Y, aabb.Max.Z),
                                            new(aabb.Min.X, aabb.Max.Y, aabb.Max.Z),
                                        };
                                        float camTerrainH = _gameTerrainChunk.GetHeightAt(_camera.Position.X, _camera.Position.Z);
                                        bool allCornersBehindTerrain = true;
                                        for (int ci = 0; ci < 8; ci++)
                                        {
                                            Vector3 dir = corners[ci] - _camera.Position;
                                            float totalDist = dir.Length();
                                            if (totalDist < 0.5f) { allCornersBehindTerrain = false; break; }
                                            dir /= totalDist;
                                            const int numSamples = 8;
                                            float stepSize = totalDist / numSamples;
                                            bool cornerVisible = false;
                                            int sampleIdx = _camera.Position.Y < camTerrainH - 0.5f ? 1 : 2;
                                            for (int s = sampleIdx; s < numSamples; s++)
                                            {
                                                Vector3 samplePos = _camera.Position + dir * (s * stepSize);
                                                float terrainH = _gameTerrainChunk.GetHeightAt(samplePos.X, samplePos.Z);
                                                if (samplePos.Y >= terrainH - 0.3f) { cornerVisible = true; break; }
                                            }
                                            if (cornerVisible) { allCornersBehindTerrain = false; break; }
                                        }
                                        if (allCornersBehindTerrain) terrainOccluded = true;
                                    }
                                }
                                
                                if (terrainOccluded) { _staticTerrainOccluded++; continue; }
                                sobj.IsVisible = true;
                                
                                // ── Register as occluder (same as original) ──
                                if (sobj.IsOccluder)
                                {
                                    if (sobj.OcclusionBVH != null)
                                    {
                                        _occlusionCulling.RegisterMeshOccluder(sobj.OcclusionBVH);
                                        if (useHiZ) _hizOcc!.RegisterMeshOccluder(sobj.OcclusionBVH);
                                    }
                                    else
                                    {
                                        _occlusionCulling.RegisterOccluder(sobj.CachedWorldAABB);
                                        if (useHiZ) _hizOcc!.RegisterOccluder(sobj.CachedWorldAABB);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Original linear iteration
                            foreach (var sobj in mgr.GetObjects())
                            {
                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                            if (distSq >= _camera.FarDist * _camera.FarDist) { sobj.IsVisible = false; _staticFrustumCulled++; continue; }
                            // Frustum cull: skip objects outside camera frustum
                            if (!StaticObjectManager.IsAABBInFrustum(frustumPlanes, sobj.CachedWorldAABB, 3f))
                            {
                                sobj.IsVisible = false;
                                continue;
            """

if old_phase2a not in content:
    print("ERROR: Phase 2A pattern not found!")
    # Debug: find approximate location
    idx = content.find("for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)")
    if idx >= 0:
        print(f"Found manager loop at index {idx}")
        print(content[idx:idx+500])
    exit(1)

content = content.replace(old_phase2a, new_phase2a, 1)
print("Phase 2A updated successfully!")

# ============================================================
# Phase 2B: Replace the inner foreach over objects with Octree walk
# ============================================================
# Find Phase 2B code
old_phase2b_start = """                // 2B. Test static objects against occluders"""

# We need to find the inner foreach in Phase 2B
# Phase 2B iterates: foreach (var sobj in mgr.GetObjects())
# But this must be the second occurrence (after Phase 2A)
# Let me find it by finding the phase2b section

# Actually, let me look for the pattern after "Phase 2B"
idx_phase2b = content.find("// Phase 2B-1")
if idx_phase2b < 0:
    print("WARNING: Phase 2B-1 comment not found, trying alternative")
    idx_phase2b = content.find("Phase 2B-1")

# Find the inner foreach in Phase 2B - it's after Phase 2B comments, not in Phase 1B section
# The Phase 2B code after occluder registration:
# It iterates: for (int mi = 0; mi < ...) { foreach (var sobj in mgr.GetObjects()) { ... occlusion test ... } }

# Let me find the second "foreach (var sobj in mgr.GetObjects())"
# First find all occurrences
count = 0
search_from = 0
while True:
    found = content.find("foreach (var sobj in mgr.GetObjects())", search_from)
    if found < 0:
        break
    count += 1
    print(f"Found 'foreach (var sobj in mgr.GetObjects())' #{count} at index {found}")
    search_from = found + 1

if count < 2:
    print("ERROR: Need at least 2 occurrences of the foreach loop")
    exit(1)

# The second occurrence is in Phase 2B
# Let me find the exact text to replace
# Phase 2B code starts after the animated object IsVisible check
idx_anim_check = content.find("animObjs[oi].IsVisible = _occlusionCulling.IsVisible(oi);")
if idx_anim_check < 0:
    print("WARNING: anim check not found, trying alternative")
    # Try to find Phase 2B by the manager loop
    idx_phase2b_mgr = content.find("for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)", 
                                    idx_phase2b - 50 if idx_phase2b >= 0 else 0)
else:
    idx_phase2b_mgr = content.find("for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)", 
                                    idx_anim_check)

if idx_phase2b_mgr < 0:
    print("WARNING: Phase 2B manager loop not found by anim check")
    # Try by index of second occurrence
    count = 0
    search_from = 0
    second_mgr_loop = -1
    while True:
        found = content.find("for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)", search_from)
        if found < 0:
            break
        count += 1
        if count == 2:
            second_mgr_loop = found
            break
        search_from = found + 1
    if second_mgr_loop >= 0:
        idx_phase2b_mgr = second_mgr_loop

if idx_phase2b_mgr < 0:
    print("ERROR: Could not find Phase 2B manager loop")
    exit(1)

print(f"Phase 2B manager loop found at index {idx_phase2b_mgr}")
print(f"Context: {content[idx_phase2b_mgr:idx_phase2b_mgr+300]}")

# Now replace the Phase 2B manager loop
old_phase2b = """                if (_objectManager.staticObjectManagers != null)
                {
                    float farSq = _camera.FarDist * _camera.FarDist;
                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        foreach (var sobj in mgr.GetObjects())
                        {
                            if (!sobj.IsVisible) continue;
                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                            if (distSq > farSq) continue;

                            // Root AABB untuk terrain occlusion test
                            var rootAABB = sobj.OcclusionBVH != null
                                ? (sobj.OcclusionBVH.Root?.Bounds ?? sobj.CachedWorldAABB)
                                : sobj.CachedWorldAABB;

                            bool occluded = false;

                            // Phase 2B-1: Terrain occlusion (always uses root AABB)
                            if (useHiZ)
                                occluded = _hizOcc!.IsTerrainOccluded(_camera.Position, rootAABB);

                            // Phase 2B-2: Wall occlusion — mesh-aware jika OcclusionBVH tersedia
                            if (!occluded)
                            {
                                if (sobj.OcclusionBVH != null)
                                {
                                    if (useHiZ)
                                        occluded = _hizOcc!.IsMeshOccluded(_camera.Position, sobj.OcclusionBVH);
                                    else
                                        occluded = _occlusionCulling.IsMeshOccluded(_camera.Position, sobj.OcclusionBVH);
                                }
                                else
                                {
                                    if (useHiZ)
                                        occluded = _hizOcc!.IsOccluded(_camera.Position, rootAABB);
                                    else
                                        occluded = _occlusionCulling.IsOccludedByOccluders(_camera.Position, rootAABB);
                                }
                            }

                            if (occluded)
                                sobj.IsVisible = false;
                        }
                    }
                }

                // Phase 3: Terrain height ray-marching for animated objects"""

new_phase2b = """                if (_objectManager.staticObjectManagers != null)
                {
                    float farSq = _camera.FarDist * _camera.FarDist;
                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)
                    {
                        var mgr = _objectManager.staticObjectManagers[mi];
                        if (mgr == null) continue;
                        
                        // ── Octree-based occlusion culling ──
                        if (mgr.UseOctree && mgr.SpatialOctree != null)
                        {
                            var octreeVisible = new List<int>();
                            var objs = mgr.GetObjects();
                            
                            // Build occludee BVH lookup function for self-occlusion skip
                            Func<int, BVH?> getSelfBVH = (idx) => 
                            {
                                if (idx >= 0 && idx < objs.Count) 
                                    return objs[idx].OcclusionBVH;
                                return null;
                            };
                            
                            // Walk Octree: frustum + occlusion combined
                            mgr.SpatialOctree.QueryOccluded(
                                frustumPlanes,
                                _camera.Position,
                                useHiZ ? null : _occlusionCulling.GetAABBOccluders(),
                                useHiZ ? null : _occlusionCulling.GetMeshOccluders(),
                                getSelfBVH,
                                octreeVisible
                            );
                            
                            // Reset visibility: only Octree-visible + distance-checked objects pass
                            for (int oi = 0; oi < objs.Count; oi++)
                                objs[oi].IsVisible = false;
                            
                            foreach (int idx in octreeVisible)
                            {
                                if (idx < 0 || idx >= objs.Count) continue;
                                var sobj = objs[idx];
                                
                                float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                                if (distSq > farSq) continue;
                                
                                // Phase 2B-1: Terrain occlusion (HiZ only)
                                if (useHiZ)
                                {
                                    var rootAABB = sobj.OcclusionBVH != null
                                        ? (sobj.OcclusionBVH.Root?.Bounds ?? sobj.CachedWorldAABB)
                                        : sobj.CachedWorldAABB;
                                    if (_hizOcc!.IsTerrainOccluded(_camera.Position, rootAABB))
                                        continue;
                                }
                                
                                sobj.IsVisible = true;
                            }
                            
                            // HiZ mode: register mesh occluders for visible objects
                            // (SW mode already registered them in Phase 2A)
                            if (useHiZ)
                            {
                                foreach (int idx in octreeVisible)
                                {
                                    if (idx < 0 || idx >= objs.Count) continue;
                                    var sobj = objs[idx];
                                    if (!sobj.IsVisible) continue;
                                    if (sobj.IsOccluder)
                                    {
                                        if (sobj.OcclusionBVH != null)
                                            _hizOcc!.RegisterMeshOccluder(sobj.OcclusionBVH);
                                        else
                                            _hizOcc!.RegisterOccluder(sobj.CachedWorldAABB);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Original linear iteration
                            foreach (var sobj in mgr.GetObjects())
                            {
                                if (!sobj.IsVisible) continue;
                                float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);
                                if (distSq > farSq) continue;

                                // Root AABB untuk terrain occlusion test
                                var rootAABB = sobj.OcclusionBVH != null
                                    ? (sobj.OcclusionBVH.Root?.Bounds ?? sobj.CachedWorldAABB)
                                    : sobj.CachedWorldAABB;

                                bool occluded = false;

                                // Phase 2B-1: Terrain occlusion (always uses root AABB)
                                if (useHiZ)
                                    occluded = _hizOcc!.IsTerrainOccluded(_camera.Position, rootAABB);

                                // Phase 2B-2: Wall occlusion — mesh-aware jika OcclusionBVH tersedia
                                if (!occluded)
                                {
                                    if (sobj.OcclusionBVH != null)
                                    {
                                        if (useHiZ)
                                            occluded = _hizOcc!.IsMeshOccluded(_camera.Position, sobj.OcclusionBVH);
                                        else
                                            occluded = _occlusionCulling.IsMeshOccluded(_camera.Position, sobj.OcclusionBVH);
                                    }
                                    else
                                    {
                                        if (useHiZ)
                                            occluded = _hizOcc!.IsOccluded(_camera.Position, rootAABB);
                                        else
                                            occluded = _occlusionCulling.IsOccludedByOccluders(_camera.Position, rootAABB);
                                    }
                                }

                                if (occluded)
                                    sobj.IsVisible = false;
                            }
                        }
                    }
                }

                // Phase 3: Terrain height ray-marching for animated objects"""

if old_phase2b in content:
    content = content.replace(old_phase2b, new_phase2b, 1)
    print("Phase 2B updated successfully!")
else:
    print("WARNING: Phase 2B exact pattern not found!")
    # Try to find the approximate location
    idx = content.find("Phase 2B-1")
    if idx >= 0:
        print(f"Found 'Phase 2B-1' at index {idx}")
        print(f"Context: {content[idx:idx+500]}")

with open('Engine/Scene/GameScene.cs', 'w', encoding='utf-8') as f:
    f.write(content)

print("Done!")
