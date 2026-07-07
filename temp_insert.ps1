$path = "Engine\Scene\GameScene.cs"
$content = Get-Content $path -Raw

$anchorText = @"
        /// <summary>Update all active physics cubes (gravity, terrain collision).</summary>
        private void UpdatePhysicsBoxes(float dt)
        {
            if (_gameTerrainChunk == null) return;
            for (int i = _physicsBoxes.Count - 1; i >= 0; i--)
            {
                _physicsBoxes[i].Update(dt, _gameTerrainChunk);
            }
        }

"@

$insertText = @"
        /// <summary>Update all active physics spheres (gravity, terrain collision).</summary>
        private void UpdatePhysicsSpheres(float dt)
        {
            if (_gameTerrainChunk == null) return;
            for (int i = _physicsSpheres.Count - 1; i >= 0; i--)
            {
                _physicsSpheres[i].Update(dt, _gameTerrainChunk);
            }
        }

        /// <summary>Resolve collisions among physics objects (box-box, box-sphere, sphere-sphere).</summary>
        private void ResolvePhysicsObjectCollisions()
        {
            for (int i = 0; i < _physicsBoxes.Count; i++)
                for (int j = i + 1; j < _physicsBoxes.Count; j++)
                    ResolveBoxBoxCollision(_physicsBoxes[i], _physicsBoxes[j]);

            for (int i = 0; i < _physicsBoxes.Count; i++)
                for (int j = 0; j < _physicsSpheres.Count; j++)
                    ResolveBoxSphereCollision(_physicsBoxes[i], _physicsSpheres[j]);

            for (int i = 0; i < _physicsSpheres.Count; i++)
                for (int j = i + 1; j < _physicsSpheres.Count; j++)
                    ResolveSphereSphereCollision(_physicsSpheres[i], _physicsSpheres[j]);
        }

        private static void ResolveBoxBoxCollision(PhysicsBox a, PhysicsBox b)
        {
            if (!a.Active || !b.Active) return;

            float aMinX = a.Position.X - a.HalfWidth;
            float aMaxX = a.Position.X + a.HalfWidth;
            float aMinY = a.Position.Y - a.HalfHeight;
            float aMaxY = a.Position.Y + a.HalfHeight;
            float aMinZ = a.Position.Z - a.HalfDepth;
            float aMaxZ = a.Position.Z + a.HalfDepth;

            float bMinX = b.Position.X - b.HalfWidth;
            float bMaxX = b.Position.X + b.HalfWidth;
            float bMinY = b.Position.Y - b.HalfHeight;
            float bMaxY = b.Position.Y + b.HalfHeight;
            float bMinZ = b.Position.Z - b.HalfDepth;
            float bMaxZ = b.Position.Z + b.HalfDepth;

            if (aMaxX <= bMinX || aMinX >= bMaxX) return;
            if (aMaxY <= bMinY || aMinY >= bMaxY) return;
            if (aMaxZ <= bMinZ || aMinZ >= bMaxZ) return;

            float overlapX = MathF.Min(aMaxX - bMinX, bMaxX - aMinX);
            float overlapY = MathF.Min(aMaxY - bMinY, bMaxY - aMinY);
            float overlapZ = MathF.Min(aMaxZ - bMinZ, bMaxZ - aMinZ);

            Vector3 pushDir;
            float pushDist;
            if (overlapX < overlapY && overlapX < overlapZ)
            {
                pushDist = overlapX * 0.5f;
                pushDir = new Vector3(a.Position.X < b.Position.X ? -1f : 1f, 0f, 0f);
            }
            else if (overlapY < overlapX && overlapY < overlapZ)
            {
                pushDist = overlapY * 0.5f;
                pushDir = new Vector3(0f, a.Position.Y < b.Position.Y ? -1f : 1f, 0f);
            }
            else
            {
                pushDist = overlapZ * 0.5f;
                pushDir = new Vector3(0f, 0f, a.Position.Z < b.Position.Z ? -1f : 1f);
            }

            a.Position += pushDir * pushDist;
            b.Position -= pushDir * pushDist;

            Vector3 relVel = a.Physics.Velocity - b.Physics.Velocity;
            float relVelAlongNormal = Vector3.Dot(relVel, pushDir);
            if (relVelAlongNormal > 0f)
            {
                float impulse = relVelAlongNormal * 0.5f;
                a.Physics.Velocity -= pushDir * impulse;
                b.Physics.Velocity += pushDir * impulse;
                a.Physics.Velocity *= 0.8f;
                b.Physics.Velocity *= 0.8f;
            }

            a.Object3D?.SetPosition(a.Position.X, a.Position.Y, a.Position.Z);
            b.Object3D?.SetPosition(b.Position.X, b.Position.Y, b.Position.Z);
        }

        private static void ResolveBoxSphereCollision(PhysicsBox box, PhysicsSphere sphere)
        {
            if (!box.Active || !sphere.Active) return;

            float closestX = Math.Clamp(sphere.Position.X, box.Position.X - box.HalfWidth, box.Position.X + box.HalfWidth);
            float closestY = Math.Clamp(sphere.Position.Y, box.Position.Y - box.HalfHeight, box.Position.Y + box.HalfHeight);
            float closestZ = Math.Clamp(sphere.Position.Z, box.Position.Z - box.HalfDepth, box.Position.Z + box.HalfDepth);

            float dx = sphere.Position.X - closestX;
            float dy = sphere.Position.Y - closestY;
            float dz = sphere.Position.Z - closestZ;
            float distSq = dx * dx + dy * dy + dz * dz;

            if (distSq >= sphere.Radius * sphere.Radius) return;

            float dist = MathF.Sqrt(distSq);
            if (dist < 0.0001f)
            {
                float ex = MathF.Min(sphere.Position.X - (box.Position.X - box.HalfWidth), (box.Position.X + box.HalfWidth) - sphere.Position.X);
                float ey = MathF.Min(sphere.Position.Y - (box.Position.Y - box.HalfHeight), (box.Position.Y + box.HalfHeight) - sphere.Position.Y);
                float ez = MathF.Min(sphere.Position.Z - (box.Position.Z - box.HalfDepth), (box.Position.Z + box.HalfDepth) - sphere.Position.Z);

                Vector3 pushDir;
                float pushDist;
                if (ex <= ey && ex <= ez)
                {
                    pushDist = sphere.Radius + ex;
                    pushDir = new Vector3(sphere.Position.X <= box.Position.X ? -1f : 1f, 0f, 0f);
                }
                else if (ey <= ez)
                {
                    pushDist = sphere.Radius + ey;
                    pushDir = new Vector3(0f, sphere.Position.Y <= box.Position.Y ? -1f : 1f, 0f);
                }
                else
                {
                    pushDist = sphere.Radius + ez;
                    pushDir = new Vector3(0f, 0f, sphere.Position.Z <= box.Position.Z ? -1f : 1f);
                }

                sphere.Position += pushDir * pushDist * 0.5f;
                box.Position -= pushDir * pushDist * 0.5f;
            }
            else
            {
                float penetration = sphere.Radius - dist;
                Vector3 pushDir = new(dx / dist, dy / dist, dz / dist);

                sphere.Position += pushDir * penetration * 0.5f;
                box.Position -= pushDir * penetration * 0.5f;

                Vector3 relVel = sphere.Physics.Velocity - box.Physics.Velocity;
                float relVelAlongNormal = Vector3.Dot(relVel, pushDir);
                if (relVelAlongNormal > 0f)
                {
                    float impulse = relVelAlongNormal * 0.5f;
                    sphere.Physics.Velocity -= pushDir * impulse;
                    box.Physics.Velocity += pushDir * impulse;
                    sphere.Physics.Velocity *= 0.8f;
                    box.Physics.Velocity *= 0.8f;
                }
            }

            sphere.Object3D?.SetPosition(sphere.Position.X, sphere.Position.Y, sphere.Position.Z);
            box.Object3D?.SetPosition(box.Position.X, box.Position.Y, box.Position.Z);
        }

        private static void ResolveSphereSphereCollision(PhysicsSphere a, PhysicsSphere b)
        {
            if (!a.Active || !b.Active) return;

            float dx = a.Position.X - b.Position.X;
            float dy = a.Position.Y - b.Position.Y;
            float dz = a.Position.Z - b.Position.Z;
            float distSq = dx * dx + dy * dy + dz * dz;
            float minDist = a.Radius + b.Radius;

            if (distSq >= minDist * minDist) return;

            float dist = MathF.Sqrt(distSq);
            if (dist < 0.0001f)
            {
                float pushDist = minDist * 0.5f;
                a.Position.X += pushDist;
                b.Position.X -= pushDist;
                a.Object3D?.SetPosition(a.Position.X, a.Position.Y, a.Position.Z);
                b.Object3D?.SetPosition(b.Position.X, b.Position.Y, b.Position.Z);
                return;
            }

            float penetration = minDist - dist;
            Vector3 pushDir = new(dx / dist, dy / dist, dz / dist);

            a.Position += pushDir * penetration * 0.5f;
            b.Position -= pushDir * penetration * 0.5f;

            Vector3 relVel = a.Physics.Velocity - b.Physics.Velocity;
            float relVelAlongNormal = Vector3.Dot(relVel, pushDir);
            if (relVelAlongNormal > 0f)
            {
                float impulse = relVelAlongNormal * 0.5f;
                a.Physics.Velocity -= pushDir * impulse;
                b.Physics.Velocity += pushDir * impulse;
                a.Physics.Velocity *= 0.8f;
                b.Physics.Velocity *= 0.8f;
            }

            a.Object3D?.SetPosition(a.Position.X, a.Position.Y, a.Position.Z);
            b.Object3D?.SetPosition(b.Position.X, b.Position.Y, b.Position.Z);
        }

        private void SpawnPhysicsSpheres()
        {
            if (_gameTerrainChunk == null) return;
            var rng = new Random();
            for (int i = 0; i < PhysicsSphereCount; i++)
            {
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float dist = rng.NextFloat(5f, PhysicsSphereSpawnRadius);
                float px = MathF.Cos(angle) * dist;
                float pz = MathF.Sin(angle) * dist;
                float terrainY = _gameTerrainChunk.GetHeightAt(px, pz);
                float py = terrainY + PhysicsSphereSpawnHeight;

                float radius = rng.NextFloat(0.3f, 1.5f);
                var color = new Vector3(
                    rng.NextFloat(0.2f, 1.0f),
                    rng.NextFloat(0.2f, 1.0f),
                    rng.NextFloat(0.2f, 1.0f)
                );

                var sphere = new PhysicsSphere(
                    new Vector3(px, py, pz),
                    radius,
                    color
                );
                _physicsSpheres.Add(sphere);
            }
            Console.WriteLine($"[GameScene] Spawned {_physicsSpheres.Count} physics spheres.");
        }

"@

$newContent = $content.Replace($anchorText, $anchorText + $insertText)

if ($newContent -eq $content) {
    Write-Host "ERROR: Could not find anchor text!"
    exit 1
}

Set-Content -Path $path -Value $newContent -NoNewline
Write-Host "SUCCESS: Methods inserted!"
