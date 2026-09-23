param([string]$TargetPath = "Engine/Objects/EditorObject.cs")

$lines = Get-Content $TargetPath
Write-Host ("Total lines before: " + $lines.Count)

# Find start indices of target classes
$classStarts = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^public\s+class\s+(TerrainLayer|PbrSplatLayerData|TerrainPbrLayerData)\b') {
        # Walk back over doc comments
        $begin = $i
        for ($j = $i - 1; $j -ge 0; $j--) {
            $trimmed = $lines[$j].Trim()
            if ($trimmed.StartsWith("///")) {
                $begin = $j
            } elseif ($trimmed -eq "") {
                break
            } else {
                break
            }
        }
        Write-Host ("Class match at line $($i+1), block begins at line $($begin+1)")
        
        # Find end brace via depth counting
        $depth = 0
        $started = $false
        $endIdx = -1
        for ($k = $i; $k -lt $lines.Count; $k++) {
            foreach ($ch in $lines[$k].ToCharArray()) {
                if ($ch -eq '{') { $depth++; $started = $true }
                elseif ($ch -eq '}') { $depth-- }
            }
            if ($started -and $depth -eq 0) {
                $endIdx = $k
                break
            }
        }
        if ($endIdx -ge 0) {
            Write-Host ("Block ends at line $($endIdx+1)")
            $classStarts += ,@($begin, $endIdx)
        }
    }
}

if ($classStarts.Count -eq 0) {
    Write-Host "No terrain classes found — already clean?"
    exit 0
}

# Mark lines to delete (sort ranges descending so we don't shift indices)
$sortedRanges = $classStarts | Sort-Object { $_[0] } -Descending
$deleteSet = New-Object System.Collections.Generic.HashSet[int]
foreach ($range in $sortedRanges) {
    for ($d = $range[0]; $d -le $range[1]; $d++) {
        [void]$deleteSet.Add($d)
    }
}

$newLines = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if (-not $deleteSet.Contains($i)) {
        $newLines += $lines[$i]
    }
}

Write-Host ("Removed " + $deleteSet.Count + " lines -> new count: " + $newLines.Count)
$newLines | Set-Content $TargetPath -Encoding UTF8
Write-Host "Done."