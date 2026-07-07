$path = "Engine/Scene/GameScene.cs"
$content = Get-Content -Path $path -Raw
$lines = $content -split "`r?`n"

# Remove old physics methods at lines 2319-2604 (0-indexed: 2318-2603)
$newLines = @()
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($i -ge 2318 -and $i -lt 2604) {
        # skip these lines
        continue
    }
    $newLines += $lines[$i]
}

$newContent = $newLines -join "`r`n"
Set-Content -Path $path -Value $newContent -NoNewLine
Write-Host "Removed " (2604-2318) " lines. New total: " $newLines.Length
