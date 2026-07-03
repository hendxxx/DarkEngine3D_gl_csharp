param()
$ErrorActionPreference = "Stop"
$filePath = "Engine/Scene/GameScene.cs"
$content = [System.IO.File]::ReadAllText((Resolve-Path $filePath))

# Fix 1: Add string title8 = ""; after string title7 = ""; (outside if block)
$content = $content.Replace('string title7 = "";', 'string title7 = "";' + "`r`n            string title8 = `"`";")

# Fix 2: Remove 'string' from title8 assignment inside if block
$content = $content.Replace('string title8 = $" BVH: rays=', 'title8 = $" BVH: rays=')

# Fix 3: Shift HLOD debugLineH indices from 7,8,9 to 8,9,10
# Use temporary markers to avoid sequential replacement issues
$content = $content.Replace('debugLineH * 7,', 'debugLineH * HLOD7_MARKER,')
$content = $content.Replace('debugLineH * 8,', 'debugLineH * HLOD8_MARKER,')
$content = $content.Replace('debugLineH * 9,', 'debugLineH * HLOD9_MARKER,')
# Now shift: HLOD7→8, HLOD8→9, HLOD9→10
$content = $content.Replace('debugLineH * HLOD7_MARKER,', 'debugLineH * 8,')
$content = $content.Replace('debugLineH * HLOD8_MARKER,', 'debugLineH * 9,')
$content = $content.Replace('debugLineH * HLOD9_MARKER,', 'debugLineH * 10,')

[System.IO.File]::WriteAllText((Resolve-Path $filePath), $content)
Write-Host "All fixes applied."
