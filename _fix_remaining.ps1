param()
$ErrorActionPreference = "Stop"
$filePath = "Engine/Scene/GameScene.cs"
$content = [System.IO.File]::ReadAllText((Resolve-Path $filePath))

$dollar = [char]0x0024

# Fix 1: Add _staticFrustumCulled++ to frustum cull block (missing increment)
$old1 = "                                sobj.IsVisible = false;
                                continue;
                            }"
# After first occurrence only (frustum cull block has no existing increment)
$new1 = "                                sobj.IsVisible = false;
                                _staticFrustumCulled++;
                                continue;
                            }"
$content = $content.Replace($old1, $new1)

# Fix 2: Add _staticTerrainOccluded++ to terrain occlusion block (missing increment)
# The terrain block has "sobj.IsVisible = false;`r`n                                    continue;"
$old2 = "                                    sobj.IsVisible = false;
                                    _staticTerrainOccluded++;
                                    continue;
                                }"
# Check if it already has the increment (from previous script attempt that might have partially worked)
# Just add it where it's missing
$target = "                                    sobj.IsVisible = false;
                                    continue;
                                }"
$replacement = "                                    sobj.IsVisible = false;
                                    _staticTerrainOccluded++;
                                    continue;
                                }"
$content = $content.Replace($target, $replacement)

# Fix 3: Add _staticOcclusionCulled++ to Phase 2B occlusion
# The Phase 2B block uses "if (occluded)`r`n                            {`r`n                                sobj.IsVisible = false;`r`n                            }"
$target3 = "                            if (occluded)
                            {
                                sobj.IsVisible = false;
                            }"
$replacement3 = "                            if (occluded)
                            {
                                sobj.IsVisible = false;
                                _staticOcclusionCulled++;
                            }"
$content = $content.Replace($target3, $replacement3)

# Fix 4: Fix debug BBox FarDist line - revert to NOT set IsVisible or count culled
$old4 = "if (distSq >= _camera.FarDist * _camera.FarDist) { sobj.IsVisible = false; _staticFrustumCulled++; continue; }"
# There are 2 occurrences: line 656 (Phase 1B - KEEP) and line 1069 (debug BBox - REVERT)
# Replace the SECOND occurrence with the original simpler line
# Unfortunately we can't use .Replace() because it replaces ALL occurrences
# Let's use a different approach - find the text with more context

# The context for the debug section is different - let's find it with more surrounding text
$old4_bbox = "                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);`r`n                            if (distSq >= _camera.FarDist * _camera.FarDist) { sobj.IsVisible = false; _staticFrustumCulled++; continue; }`r`n`r`n                            bool isCulled = !sobj.IsVisible && Config.OcclusionConfig.UseOcclusion;`r`n                            Vector3 debugColor = isCulled ? new Vector3(1f, 0f, 0f)"
$new4_bbox = "                            float distSq = Vector3.DistanceSquared(_camera.Position, sobj.Position);`r`n                            if (distSq >= _camera.FarDist * _camera.FarDist) continue;`r`n`r`n                            bool isCulled = !sobj.IsVisible && Config.OcclusionConfig.UseOcclusion;`r`n                            Vector3 debugColor = isCulled ? new Vector3(1f, 0f, 0f)"
$content = $content.Replace($old4_bbox, $new4_bbox)

# Fix 5: Remove dead ocStaticCount code
$old5 = "                int ocStaticCount = 0;`r`n                if (_objectManager != null && _objectManager.staticObjectManagers != null)`r`n                {`r`n                    for (int mi = 0; mi < _objectManager.staticObjectManagers.Count; mi++)`r`n                    {`r`n                        var mgr = _objectManager.staticObjectManagers[mi];`r`n                        if (mgr == null) continue;`r`n                        foreach (var sobj in mgr.GetObjects())`r`n                            if (!sobj.IsVisible) ocStaticCount++;`r`n                    }`r`n                }"
$content = $content.Replace($old5, "")

# Fix 6: Update title6 to show anim frustum/occlusion counts
$old6 = "                int culledTotal = _objectManager.TotalObjects - _objectManager.DrawnObjects;`r`n                title6 = `$`" Objects: {_objectManager.DrawnObjects:N0} drawn / {culledTotal:N0} culled / {_objectManager.TotalObjects:N0} total`";"
$new6 = "                int culledTotal = _objectManager.TotalObjects - _objectManager.DrawnObjects;`r`n                int animFrustum = _objectManager.CulledByFrustum;`r`n                int animOcc = _objectManager.CulledByOcclusion;`r`n                title6 = `$`" Objects: {_objectManager.DrawnObjects:N0} drawn / {culledTotal:N0} culled / {_objectManager.TotalObjects:N0} total  (anim: {animFrustum}f {animOcc}occ)`";"
$content = $content.Replace($old6, $new6)

[System.IO.File]::WriteAllText((Resolve-Path $filePath), $content)
Write-Host "All 6 fixes applied."
