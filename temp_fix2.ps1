$path = "Engine/Scene/GameScene.cs"
$content = Get-Content -Path $path -Raw

# First check: omBoxes in NPC section (replace the line "for (int bi = 0; bi < omBoxes.Count; bi++)" under "// Boxes vs NPC")
$content = $content -replace '(?s)(// Boxes vs NPC\r?\n)(\s*)for \(int bi = 0; bi < omBoxes\.Count; bi\+\+\)', ('$1$2var omBoxes = _objectManager.PhysicsBoxes;' + "`r`n" + '$2for (int bi = 0; bi < omBoxes.Count; bi++)')

# Second check: omSpheres in NPC section
$content = $content -replace '(?s)(// Spheres vs NPC\r?\n)(\s*)for \(int si = 0; si < omSpheres\.Count; si\+\+\)', ('$1$2var omSpheres = _objectManager.PhysicsSpheres;' + "`r`n" + '$2for (int si = 0; si < omSpheres.Count; si++)')

Set-Content -Path $path -Value $content -NoNewLine
Write-Host "Done fixing omBoxes/omSpheres scope"
