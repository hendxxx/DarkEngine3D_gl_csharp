param()
$ErrorActionPreference = "Stop"
$filePath = "Engine/Scene/GameScene.cs"
$content = [System.IO.File]::ReadAllText((Resolve-Path $filePath))

# Use simple literal replacements - escape $ with backtick

# Edit 1: Add tracking fields
$content = $content.Replace(
    '        private int _occlusionFrameCount = 0;',
    '        private int _occlusionFrameCount = 0;' + "`r`n        private int _staticFrustumCulled = 0;`r`n        private int _staticTerrainOccluded = 0;`r`n        private int _staticOcclusionCulled = 0;"
)

# Edit 2: Reset counters
$content = $content.Replace(
    "                // Reset BVH profiler counters each frame",
    "                // Reset BVH profiler + cull counters each frame"
)
$content = $content.Replace(
    "                OcclusionCulling.LastIsOccludedTicks = 0;",
    "                OcclusionCulling.LastIsOccludedTicks = 0;`r`n                _staticFrustumCulled = 0;`r`n                _staticTerrainOccluded = 0;`r`n                _staticOcclusionCulled = 0;"
)

# Edit 3: Fix FarDist objects - set IsVisible=false + count
$content = $content.Replace(
    "if (distSq >= _camera.FarDist * _camera.FarDist) continue;",
    "if (distSq >= _camera.FarDist * _camera.FarDist) { sobj.IsVisible = false; _staticFrustumCulled++; continue; }"
)

# Edit 4: Count frustum-culled in IsAABBInFrustum block
$content = $content.Replace(
    "                                sobj.IsVisible = false;
                                continue;
                            }",
    "                                sobj.IsVisible = false;
                                _staticFrustumCulled++;
                                continue;
                            }"
)

# Edit 5: Count terrain-occluded
$content = $content.Replace(
    "                                    sobj.IsVisible = false;
                                    continue;
                                }",
    "                                    sobj.IsVisible = false;
                                    _staticTerrainOccluded++;
                                    continue;
                                }"
)

# Edit 6: Count occlusion-culled in Phase 2B
$content = $content.Replace(
    "                            if (occluded)
                                sobj.IsVisible = false;",
    "                            if (occluded)
                            {
                                sobj.IsVisible = false;
                                _staticOcclusionCulled++;
                            }"
)

# Edit 7: Update title6 to show CulledByFrustum/CulledByOcclusion
# Use [char]0x0024 to represent $ without triggering interpolation
$dollar = [char]0x0024
$old7 = "                int culledTotal = _objectManager.TotalObjects - _objectManager.DrawnObjects;
                title6 = `$`" Objects: {_objectManager.DrawnObjects:N0} drawn / {culledTotal:N0} culled / {_objectManager.TotalObjects:N0} total`";"
$new7 = "                int culledTotal = _objectManager.TotalObjects - _objectManager.DrawnObjects;
                int animFrustum = _objectManager.CulledByFrustum;
                int animOcc = _objectManager.CulledByOcclusion;
                title6 = `$`" Objects: {_objectManager.DrawnObjects:N0} drawn / {culledTotal:N0} culled / {_objectManager.TotalObjects:N0} total  (anim: {animFrustum}f {animOcc}occ)`";"
$content = $content.Replace($old7, $new7)

# Edit 8: Update title7 to show separate static counts
$old8 = "                title7 = `$`" OC [{ocMode}]: {_occlusionCulling!.VisibleCount}v / {_occlusionCulling.OccludedCount}o (static {ocStaticCount}o)`";"
$new8 = "                int staticCulledAll = _staticFrustumCulled + _staticTerrainOccluded + _staticOcclusionCulled;
                title7 = `$`" OC [{ocMode}]: {_occlusionCulling!.VisibleCount}v / {_occlusionCulling.OccludedCount}o | static: {staticCulledAll}cld ({_staticFrustumCulled}f {_staticTerrainOccluded}t {_staticOcclusionCulled}o)`";"
$content = $content.Replace($old8, $new8)

[System.IO.File]::WriteAllText((Resolve-Path $filePath), $content)
Write-Host "All 8 edits applied."
