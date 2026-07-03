# PowerShell script to add BVH profiling to GameScene.cs

$filePath = "Engine/Scene/GameScene.cs"
$content = [System.IO.File]::ReadAllText($filePath)

# --- Edit 1: Add BVH.ResetStats() at start of occlusion culling section ---
$old1 = "            // ── Occlusion Culling (always runs for visual updates behind menu) ──`r`n            if (Config.OcclusionConfig.UseOcclusion && _objectManager != null && _gameTerrainChunk != null)`r`n            {`r`n                _occlusionFrameCount++;`r`n`r`n                bool useHiZ = (Config.OcclusionConfig.Mode == OcclusionMode.HiZ && _hizOcc != null);`r`n`r`n                // Phase 0: Generate Hi-Z depth buffer"

$new1 = "            // ── Occlusion Culling (always runs for visual updates behind menu) ──`r`n            if (Config.OcclusionConfig.UseOcclusion && _objectManager != null && _gameTerrainChunk != null)`r`n            {`r`n                _occlusionFrameCount++;`r`n`r`n                // Reset BVH profiler counters each frame`r`n                BVH.ResetStats();`r`n                OcclusionCulling.LastCheckVisibilityTicks = 0;`r`n                OcclusionCulling.LastIsOccludedTicks = 0;`r`n`r`n                bool useHiZ = (Config.OcclusionConfig.Mode == OcclusionMode.HiZ && _hizOcc != null);`r`n`r`n                // Phase 0: Generate Hi-Z depth buffer"

if ($content -match [regex]::Escape($old1)) {
    $content = $content -replace [regex]::Escape($old1), $new1
    Write-Host "Edit 1 applied: BVH.ResetStats() added"
} else {
    Write-Host "ERROR: Edit 1 pattern not found"
}

# --- Edit 2: Add BVH stats capture after title7 assignment ---
$old2 = "                title7 = `$`" OC [`$ocMode`]: `$_occlusionCulling!.VisibleCount`v / `$_occlusionCulling.OccludedCount`o (static `$ocStaticCount`o)`";"
$new2 = "                // BVH profiling stats`r`n                int bvhRays = BVH.TotalRayTests;`r`n                int bvhNodes = BVH.TotalNodeVisits;`r`n                int bvhAABBs = BVH.TotalAABBTests;`r`n                int bvhTris = BVH.TotalTriangleTests;`r`n                long ocCheckVisTicks = OcclusionCulling.LastCheckVisibilityTicks;`r`n                long ocIsOccTicks = OcclusionCulling.LastIsOccludedTicks;`r`n                double freq = System.Diagnostics.Stopwatch.Frequency;`r`n                double ocCheckVisMs = (double)ocCheckVisTicks / freq * 1000.0;`r`n                double ocIsOccMs = (double)ocIsOccTicks / freq * 1000.0;`r`n                title7 = `$`" OC [`$ocMode`]: `$_occlusionCulling!.VisibleCount`v / `$_occlusionCulling.OccludedCount`o (static `$ocStaticCount`o)`";`r`n                string title8 = `$`" BVH: rays=`$bvhRays nodes=`$bvhNodes aabb=`$bvhAABBs tris=`$bvhTris  OC: checkVis=`$ocCheckVisMs:N3`ms isOcc=`$ocIsOccMs:N3`ms`";"

if ($content -match [regex]::Escape($old2)) {
    $content = $content -replace [regex]::Escape($old2), $new2
    Write-Host "Edit 2 applied: BVH stats capture added"
} else {
    Write-Host "ERROR: Edit 2 pattern not found"
}

# --- Edit 3: Add title8 display after title7 display ---
$old3 = "            if (!string.IsNullOrEmpty(title7))`r`n                _hud.DrawText(title7, 10, 60 + debugLineH * 6, new Vector3(0, 1, 1));"

$new3 = "            if (!string.IsNullOrEmpty(title7))`r`n                _hud.DrawText(title7, 10, 60 + debugLineH * 6, new Vector3(0, 1, 1));`r`n            // BVH profiling display (dim cyan)`r`n            if (!string.IsNullOrEmpty(title8))`r`n                _hud.DrawText(title8, 10, 60 + debugLineH * 7, new Vector3(0.2f, 0.7f, 0.8f));"

if ($content -match [regex]::Escape($old3)) {
    $content = $content -replace [regex]::Escape($old3), $new3
    Write-Host "Edit 3 applied: title8 display added"
} else {
    Write-Host "ERROR: Edit 3 pattern not found"
}

# Write back
[System.IO.File]::WriteAllText($filePath, $content)
Write-Host "File written successfully."
