param()
$ErrorActionPreference = "Stop"
$filePath = "Engine/Scene/GameScene.cs"
$content = Get-Content $filePath -Raw

# Edit 1: Add BVH.ResetStats() after "_occlusionFrameCount++;"
$target = "_occlusionFrameCount++;`r`n`r`n                bool useHiZ"
$replacement = "_occlusionFrameCount++;`r`n`r`n                // Reset BVH profiler counters each frame`r`n                BVH.ResetStats();`r`n                OcclusionCulling.LastCheckVisibilityTicks = 0;`r`n                OcclusionCulling.LastIsOccludedTicks = 0;`r`n`r`n                bool useHiZ"
$content = $content.Replace($target, $replacement)

# Edit 2: Add BVH stats capture before title7 assignment
$target2 = 'string ocMode = Inputs.Keyboard.GetOcclusionModeName();'
$replacement2 = "                // BVH profiling stats`r`n                int bvhRays = BVH.TotalRayTests;`r`n                int bvhNodes = BVH.TotalNodeVisits;`r`n                int bvhAABBs = BVH.TotalAABBTests;`r`n                int bvhTris = BVH.TotalTriangleTests;`r`n                long ocCheckVisTicks = OcclusionCulling.LastCheckVisibilityTicks;`r`n                long ocIsOccTicks = OcclusionCulling.LastIsOccludedTicks;`r`n                double freq = System.Diagnostics.Stopwatch.Frequency;`r`n                double ocCheckVisMs = (double)ocCheckVisTicks / freq * 1000.0;`r`n                double ocIsOccMs = (double)ocIsOccTicks / freq * 1000.0;`r`n                string title8 = `$`" BVH: rays={bvhRays} nodes={bvhNodes} aabb={bvhAABBs} tris={bvhTris}  OC: checkVis={ocCheckVisMs:N3}ms isOcc={ocIsOccMs:N3}ms`";`r`n                $target2"

$content = $content.Replace($target2, $replacement2)

# Edit 3: Add title8 draw call after title7 draw call - find exact match
$target3 = "if (!string.IsNullOrEmpty(title7))`r`n                _hud.DrawText(title7, 10, 60 + debugLineH * 6, new Vector3(0, 1, 1));"
$replacement3 = "$target3`r`n            // BVH profiling display (dim cyan)`r`n            if (!string.IsNullOrEmpty(title8))`r`n                _hud.DrawText(title8, 10, 60 + debugLineH * 7, new Vector3(0.2f, 0.7f, 0.8f));"
$content = $content.Replace($target3, $replacement3)

[System.IO.File]::WriteAllText((Resolve-Path $filePath), $content)
Write-Host "All 3 edits applied successfully."
