$path = "Engine/Scene/GameScene.cs"
$content = Get-Content -Path $path -Raw

# Replace the two lines with three lines
$content = $content -replace "(_objectManager\.UpdatePhysicsBoxes\(deltaTime, _gameTerrainChunk\);\r?\n\s*_objectManager\.UpdatePhysicsSpheres\(deltaTime, _gameTerrainChunk\);)",
'$1

                //  Resolve physics objects vs static objects (bounce off walls)
                _objectManager.ResolvePhysicsStaticCollisions();'

Set-Content -Path $path -Value $content -NoNewLine
Write-Host "Done adding ResolvePhysicsStaticCollisions call"
