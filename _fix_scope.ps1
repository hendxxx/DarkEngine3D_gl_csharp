param()
$ErrorActionPreference = "Stop"
$filePath = "Engine/Scene/GameScene.cs"
$content = [System.IO.File]::ReadAllText((Resolve-Path $filePath))

# Fix 1: Add string title8 = ""; after string title7 = ""; (outside if block)
# Find "string title7 = "";" and insert "string title8 = "";" after it
$find1 = 'string title7 = "";'
$insert1 = "`r`n            string title8 = `"`";"
$content = $content.Replace($find1, $find1 + $insert1)

# Fix 2: Remove the 'string' keyword from the title8 assignment inside the if block
# Change: 'string title8 = $" BVH: ..."' to 'title8 = $" BVH: ..."'
$find2 = 'string title8 = '
$content = $content.Replace($find2, 'title8 = ')

# Fix 3: Shift HLOD lines from 7/8/9 to 8/9/10
# Find HLOD lines using debugLineH * 7, debugLineH * 8, debugLineH * 9
$content = $content.Replace('debugLineH * 7', 'debugLineH * 8')
$content = $content.Replace('debugLineH * 8', 'debugLineH * 9')
$content = $content.Replace('debugLineH * 9', 'debugLineH * 10')

# But wait, the first replacement already changed * 7 to * 8, so the second * 8 would match both original * 8 and the one we just changed from * 7!
# This is a problem with sequential replacement. Let me fix this differently.
