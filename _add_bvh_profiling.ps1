param()
$ErrorActionPreference = "Stop"
$filePath = "Engine/Scene/GameScene.cs"
$content = Get-Content $filePath -Raw

# Edit 1: Add BVH.ResetStats() at start of occlusion section
$old1 = "// ── Occlusion Culling (always runs for visual updates behind menu) ──`r`n            if (Config.OcclusionConfig.UseOcclusion && _objectManager != null && _gameTerrainChunk != null)`r`n            {`r`n                _occlusionFrameCount++;`r`n`r`n                bool useHiZ"

$new1 = "// ── Occlusion Culling (always runs for visual updates behind menu) ──`r`n            if (Config.OcclusionConfig.UseOcclusion && _objectManager != null && _gameTerrainChunk != null)`r`n            {`r`n                _occlusionFrameCount++;`r`n`r`n                // Reset BVH profiler counters each frame`r`n                BVH.ResetStats();`r`n                OcclusionCulling.LastCheckVisibilityTicks = 0;`r`n                OcclusionCulling.LastIsOccludedTicks = 0;`r`n`r`n                bool useHiZ"

$content = $content.Replace($old1, $new1)

# Edit 2: Add BVH stats after title7 assignment
$old2Line = 'title7 = $" OC [{ocMode}]: {_occlusionCulling!.VisibleCount}v / {_occlusionCulling.OccludedCount}o (static {ocStaticCount}o)";'
$new2Lines = @(
    '                // BVH profiling stats'
    '                int bvhRays = BVH.TotalRayTests;'
    '                int bvhNodes = BVH.TotalNodeVisits;'
    '                int bvhAABBs = BVH.TotalAABBTests;'
    '                int bvhTris = BVH.TotalTriangleTests;'
    '                long ocCheckVisTicks = OcclusionCulling.LastCheckVisibilityTicks;'
    '                long ocIsOccTicks = OcclusionCulling.LastIsOccludedTicks;'
    '                double freq = System.Diagnostics.Stopwatch.Frequency;'
    '                double ocCheckVisMs = (double)ocCheckVisTicks / freq * 1000.0;'
    '                double ocIsOccMs = (double)ocIsOccTicks / freq * 1000.0;'
    '                title7 = $" OC [{ocMode}]: {_occlusionCulling!.VisibleCount}v / {_occlusionCulling.OccludedCount}o (static {ocStaticCount}o)";'
    '                string title8 = $" BVH: rays={bvhRays} nodes={bvhNodes} aabb={bvhAABBs} tris={bvhTris}  OC: checkVis={ocCheckVisMs:N3}ms isOcc={ocIsOccMs:N3}ms";'
)
$new2Joined = $new2Lines -join "`r`n                "
$new2Full = "                $new2Joined"
$content = $content.Replace($old2Line, $new2Full)

# Edit 3: Add title8 draw call after title7 draw call
$old3Line = 'if (!string.IsNullOrEmpty(title7))'
$old3Full = "            $old3Line`r`n                _hud.DrawText(title7, 10, 60 + debugLineH * 6, new Vector3(0, 1, 1));"
$new3Full = "            $old3Line`r`n                _hud.DrawText(title7, 10, 60 + debugLineH * 6, new Vector3(0, 1, 1));`r`n            // BVH profiling display (dim cyan)`r`n            if (!string.IsNullOrEmpty(title8))`r`n                _hud.DrawText(title8, 10, 60 + debugLineH * 7, new Vector3(0.2f, 0.7f, 0.8f));"
$content = $content.Replace($old3Full, $new3Full)

Set-Content $filePath $content -NoNewLine
Write-Host "All 3 edits applied successfully."
