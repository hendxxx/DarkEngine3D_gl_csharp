$path = "Engine\Scene\GameScene.cs"
$content = Get-Content $path -Raw

$anchorText = @"
                //  Resolve collisions among physics objects (box-box, box-sphere, sphere-sphere)
                ResolvePhysicsObjectCollisions();

                //  COLLISION: push NPC out of static objects (always runs) 
                var allObjs = _objectManager.GetObjects();
"@

$insertText = @"
                //  COLLISION: physics objects vs player (sphere vs capsule push-out)
                {
                    var pPos = playerAgent.Position;
                    var pRadius = CharacterAgent.CollisionRadius;
                    float pSegMin = pPos.Y + pRadius;
                    float pSegMax = pPos.Y + playerAgent.CollisionHeight - pRadius;
                    bool playerPushed = false;

                    // Boxes vs player
                    for (int bi = 0; bi < _physicsBoxes.Count; bi++)
                    {
                        var box = _physicsBoxes[bi];
                        if (!box.Active) continue;

                        float closestX = Math.Clamp(pPos.X, box.Position.X - box.HalfWidth, box.Position.X + box.HalfWidth);
                        float closestZ = Math.Clamp(pPos.Z, box.Position.Z - box.HalfDepth, box.Position.Z + box.HalfDepth);
                        float dx = pPos.X - closestX;
                        float dz = pPos.Z - closestZ;
                        float distSq = dx * dx + dz * dz;

                        // Quick Y overlap check (capsule vs box)
                        float boxMinY = box.Position.Y - box.HalfHeight;
                        float boxMaxY = box.Position.Y + box.HalfHeight;
                        float overlapY = MathF.Min(pSegMax, boxMaxY) - MathF.Max(pSegMin, boxMinY);
                        if (overlapY <= 0.001f) continue;

                        float minDist = pRadius;
                        if (distSq >= minDist * minDist) continue;

                        float dist = MathF.Sqrt(distSq);
                        float nx, nz;
                        if (dist > 1e-4f) { nx = dx / dist; nz = dz / dist; }
                        else { nx = 1f; nz = 0f; }

                        float push = (minDist - dist) * 0.5f;
                        pPos.X += nx * push;
                        pPos.Z += nz * push;
                        box.Position.X -= nx * push;
                        box.Position.Z -= nz * push;
                        box.Object3D?.SetPosition(box.Position.X, box.Position.Y, box.Position.Z);
                        box.Physics.Velocity.X += nx * push * 3f;
                        box.Physics.Velocity.Z += nz * push * 3f;
                        playerPushed = true;
                    }

                    // Spheres vs player
                    for (int si = 0; si < _physicsSpheres.Count; si++)
                    {
                        var sphere = _physicsSpheres[si];
                        if (!sphere.Active) continue;

                        float dx = pPos.X - sphere.Position.X;
                        float dz = pPos.Z - sphere.Position.Z;
                        float distSq = dx * dx + dz * dz;

                        // Quick Y overlap check (capsule segment vs sphere)
                        float sphereMaxY = sphere.Position.Y + sphere.Radius;
                        float sphereMinY = sphere.Position.Y - sphere.Radius;
                        float overlapY = MathF.Min(pSegMax, sphereMaxY) - MathF.Max(pSegMin, sphereMinY);
                        if (overlapY <= 0.001f) continue;

                        float minDist = pRadius + sphere.Radius;
                        if (distSq >= minDist * minDist) continue;

                        float dist = MathF.Sqrt(distSq);
                        float nx, nz;
                        if (dist > 1e-4f) { nx = dx / dist; nz = dz / dist; }
                        else { nx = 1f; nz = 0f; }

                        float push = (minDist - dist) * 0.5f;
                        pPos.X += nx * push;
                        pPos.Z += nz * push;
                        sphere.Position.X -= nx * push;
                        sphere.Position.Z -= nz * push;
                        sphere.Object3D?.SetPosition(sphere.Position.X, sphere.Position.Y, sphere.Position.Z);
                        sphere.Physics.Velocity.X += nx * push * 3f;
                        sphere.Physics.Velocity.Z += nz * push * 3f;
                        playerPushed = true;
                    }

                    if (playerPushed)
                    {
                        float terrainY = _gameTerrainChunk.GetHeightAt(pPos.X, pPos.Z);
                        pPos.Y = terrainY;
                    }
                    playerAgent.Position = pPos;
                }

                //  COLLISION: push NPC out of static objects (always runs) 
                var allObjs = _objectManager.GetObjects();
"@

$newContent = $content.Replace($anchorText, $insertText)

if ($newContent -eq $content) {
    Write-Host "ERROR: anchorText not found!"
    exit 1
}

Set-Content -Path $path -Value $newContent -NoNewline
Write-Host "SUCCESS: Player collision inserted!"
