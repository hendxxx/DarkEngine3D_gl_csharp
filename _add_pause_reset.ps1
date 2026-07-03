$path = "Engine/Scene/GameScene.cs"
$lines = Get-Content $path

for ($i = 0; $i -lt $lines.Count; $i++) {
    # Find: "_pauseSelection = 0;" that follows "_paused = false;"
    if ($lines[$i] -match '^\s+_pauseSelection = 0;$' -and $i -gt 0 -and $lines[$i-1] -match '_paused') {
        # Check if next line already has _pauseLastHovered
        if ($i + 1 -lt $lines.Count -and $lines[$i+1] -notmatch '_pauseLastHovered') {
            $lines[$i+1] = "            _pauseLastHovered = -1;" + [Environment]::NewLine + $lines[$i+1]
            Write-Host "Inserted _pauseLastHovered reset after line $i"
        } else {
            Write-Host "_pauseLastHovered already present or at end of file"
        }
        break
    }
}

$lines | Set-Content $path
Write-Host "Done"
