using System;
using System.IO;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine
{
    public unsafe class MovingObjects : IDisposable
    {
        private struct Mover
        {
            public Vector3 Position;
            public Vector3 Velocity;     // XZ velocity; Y is set from terrain each frame
            public float Radius;
            public float HitFlashTimer;  // > 0 while a "bump" animation is active
            public bool IsChasing;       // true → AI is locked on the player
            public float WanderTimer;    // counts down; on expiry, pick a new random direction
            public float YawDegrees;     // facing direction (derived from velocity)

            public float HP;             // current HP; <= 0 → dies
            public bool IsDead;          // true once killed; AI + collisions skipped
            public float DeathTimer;     // counts UP from 0 during the fall-over animation
        }

        // Tunables
        private const float MoverRadius     = 0.6f;
        private const float MoverYOffset    = 0.0f;    // anime-character feet at local y≈0
        private const float ModelScale      = 0.65f;   // anime-character is ~3.04u tall → ~2m
        private const float WanderSpeed     = 5.0f;
        private const float ChaseSpeed      = 12.0f;   // faster than walk (10)
        private const float ChaseStartRange = 30.0f;
        private const float ChaseStopRange  = 50.0f;
        private const float PlayerRadius    = 0.7f;
        private const float PlayerDamage    = 12f;
        private const float PlayerHitCooldownDuration = 1.0f;
        private const float FlashDuration   = 0.4f;
        // If the model ends up looking the wrong way, flip this to 180.
        private const float ModelYawOffsetDeg = 0.0f;

        // One sub-mesh of the enemy model — all triangles sharing a single material.
        private class MeshSegment
        {
            public uint Vao, Vbo;
            public int VertexCount;
            public Texture? DiffuseTex;
        }

        private readonly Mover[] _movers;
        private readonly Random _rng;
        private readonly System.Collections.Generic.List<MeshSegment> _segments = new();
        private readonly System.Collections.Generic.Dictionary<string, Texture> _textureCache = new(StringComparer.OrdinalIgnoreCase);
        private uint _shaderProgram;
        private int _modelLocation;
        private int _useTextureLocation;
        private int _diffuseTexLocation;
        private int _alphaCutoffLocation;
        private float _playerHitCooldown = 0f;

        public MovingObjects(int count, int seed = 12345)
        {
            _shaderProgram        = Shader.GetShaderProgram();
            _modelLocation        = GL.GetUniformLocation(_shaderProgram, "model");
            _useTextureLocation   = GL.GetUniformLocation(_shaderProgram, "useTexture");
            _diffuseTexLocation   = GL.GetUniformLocation(_shaderProgram, "diffuseTex");
            _alphaCutoffLocation  = GL.GetUniformLocation(_shaderProgram, "alphaCutoff");
            LoadDennisMesh();

            _rng = new Random(seed);
            float half = TerrainChunk.GetHalfMapSize();
            _movers = new Mover[count];

            for (int i = 0; i < count; i++)
            {
                float x = (float)(_rng.NextDouble() * 2 - 1) * (half - 4f);
                float z = (float)(_rng.NextDouble() * 2 - 1) * (half - 4f);
                float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                _movers[i] = new Mover
                {
                    Position = new Vector3(x, 0, z),
                    Velocity = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * WanderSpeed,
                    Radius = MoverRadius,
                    HitFlashTimer = 0f,
                    IsChasing = false,
                    WanderTimer = NextWanderInterval(),
                    YawDegrees = MathF.Atan2(MathF.Cos(angle), MathF.Sin(angle)) * 180f / MathF.PI,
                    HP = Const.ENEMY_MAX_HP,
                    IsDead = false,
                    DeathTimer = 0f
                };
                _movers[i].Position.Y = TerrainChunk.GetHeightAt(x, z) + MoverYOffset;
            }
        }

        // Random 2–5 second interval between wander direction changes.
        private float NextWanderInterval() => 2.0f + (float)_rng.NextDouble() * 3.0f;

        // Load the enemy mesh once into multi-material segments. All Movers share these VBOs.
        // Each material in the OBJ becomes a MeshSegment with its own VAO, color, and (optional) texture.
        private void LoadDennisMesh()
        {
            Vector3 fallback = new(0.85f, 0.55f, 0.65f); // used only when MTL has no Kd
            ObjMesh mesh = ObjLoader.Load("Artifacts/Models/anime-character.obj");

            foreach (MeshGroup mg in mesh.Groups)
            {
                if (mg.Vertices.Count == 0) continue;

                Vector3 color = fallback;
                Texture? tex = null;
                if (mesh.Materials.TryGetValue(mg.MaterialName, out MaterialDef? mat) && mat != null)
                {
                    color = mat.DiffuseColor;
                    if (!string.IsNullOrEmpty(mat.DiffuseMapPath) && File.Exists(mat.DiffuseMapPath))
                    {
                        if (!_textureCache.TryGetValue(mat.DiffuseMapPath, out tex))
                        {
                            try { tex = new Texture(mat.DiffuseMapPath); _textureCache[mat.DiffuseMapPath] = tex; }
                            catch (Exception ex) { Console.WriteLine($"  [tex fail] {mat.DiffuseMapPath}: {ex.Message}"); tex = null; }
                        }
                    }
                }

                // Stamp the segment color into per-vertex Color so the no-texture path lights correctly.
                Vertex[] verts = new Vertex[mg.Vertices.Count];
                for (int i = 0; i < verts.Length; i++)
                {
                    Vertex v = mg.Vertices[i];
                    v.Color = color;
                    verts[i] = v;
                }

                MeshSegment seg = new() { VertexCount = verts.Length, DiffuseTex = tex };

                fixed (uint* p = &seg.Vao) GL.GenVertexArrays(1, p);
                fixed (uint* p = &seg.Vbo) GL.GenBuffers(1, p);

                GL.BindVertexArray(seg.Vao);
                GL.BindBuffer(Const.GL_ARRAY_BUFFER, seg.Vbo);
                fixed (void* ptr = verts)
                {
                    GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(verts.Length * sizeof(Vertex)), ptr, Const.GL_STATIC_DRAW);
                }
                int stride = sizeof(Vertex);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)sizeof(Vector3));
                GL.EnableVertexAttribArray(2);
                GL.VertexAttribPointer(2, 3, Const.GL_FLOAT, false, stride, (void*)(sizeof(Vector3) * 2));
                GL.EnableVertexAttribArray(3);
                GL.VertexAttribPointer(3, 2, Const.GL_FLOAT, false, stride, (void*)(sizeof(Vector3) * 3));
                GL.BindVertexArray(0);

                _segments.Add(seg);
            }

            int textured = 0;
            foreach (var s in _segments) if (s.DiffuseTex != null) textured++;
            Console.WriteLine($"  Enemy mesh: {_segments.Count} segment(s), {textured} textured.");
        }

        public void Update(float dt, Camera player)
        {
            if (_playerHitCooldown > 0f) _playerHitCooldown -= dt;

            float half = TerrainChunk.GetHalfMapSize();
            float px = player.Position.X, pz = player.Position.Z;

            // --- AI + Move + bounce off map edges ---
            for (int i = 0; i < _movers.Length; i++)
            {
                ref Mover m = ref _movers[i];

                // Dead enemies don't move, don't chase, don't collide. Just tick the fall-over animation.
                if (m.IsDead)
                {
                    m.DeathTimer += dt;
                    continue;
                }

                // Distance to player on the XZ plane.
                float ddx = px - m.Position.X;
                float ddz = pz - m.Position.Z;
                float distSq = ddx * ddx + ddz * ddz;

                // State transitions with hysteresis: easy to start chasing, harder to lose interest.
                if (m.IsChasing)
                {
                    if (distSq > ChaseStopRange * ChaseStopRange)
                    {
                        m.IsChasing = false;
                        // Pick a fresh wander direction so the cube doesn't keep flying toward last-known location.
                        float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                        m.Velocity.X = MathF.Cos(angle) * WanderSpeed;
                        m.Velocity.Z = MathF.Sin(angle) * WanderSpeed;
                        m.WanderTimer = NextWanderInterval();
                    }
                }
                else
                {
                    if (distSq < ChaseStartRange * ChaseStartRange)
                        m.IsChasing = true;
                }

                // Drive velocity from current state.
                if (m.IsChasing && distSq > 1e-4f)
                {
                    float dist = MathF.Sqrt(distSq);
                    m.Velocity.X = ddx / dist * ChaseSpeed;
                    m.Velocity.Z = ddz / dist * ChaseSpeed;
                }
                else if (!m.IsChasing)
                {
                    m.WanderTimer -= dt;
                    if (m.WanderTimer <= 0f)
                    {
                        float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                        m.Velocity.X = MathF.Cos(angle) * WanderSpeed;
                        m.Velocity.Z = MathF.Sin(angle) * WanderSpeed;
                        m.WanderTimer = NextWanderInterval();
                    }
                }

                // Move.
                m.Position.X += m.Velocity.X * dt;
                m.Position.Z += m.Velocity.Z * dt;

                // Edge handling: always clamp position. Reflect velocity only while wandering —
                // chasers re-target the player next frame anyway, no need to bounce them off walls.
                if (m.Position.X < -half + m.Radius)
                {
                    m.Position.X = -half + m.Radius;
                    if (!m.IsChasing) { m.Velocity.X = -m.Velocity.X; m.HitFlashTimer = FlashDuration; }
                }
                if (m.Position.X > half - m.Radius)
                {
                    m.Position.X = half - m.Radius;
                    if (!m.IsChasing) { m.Velocity.X = -m.Velocity.X; m.HitFlashTimer = FlashDuration; }
                }
                if (m.Position.Z < -half + m.Radius)
                {
                    m.Position.Z = -half + m.Radius;
                    if (!m.IsChasing) { m.Velocity.Z = -m.Velocity.Z; m.HitFlashTimer = FlashDuration; }
                }
                if (m.Position.Z > half - m.Radius)
                {
                    m.Position.Z = half - m.Radius;
                    if (!m.IsChasing) { m.Velocity.Z = -m.Velocity.Z; m.HitFlashTimer = FlashDuration; }
                }

                m.Position.Y = TerrainChunk.GetHeightAt(m.Position.X, m.Position.Z) + MoverYOffset;

                // Face the way we're moving (skip the update if velocity is essentially zero so yaw doesn't snap to noise)
                if (m.Velocity.X * m.Velocity.X + m.Velocity.Z * m.Velocity.Z > 1e-3f)
                {
                    m.YawDegrees = MathF.Atan2(m.Velocity.X, m.Velocity.Z) * 180f / MathF.PI;
                }

                if (m.HitFlashTimer > 0f) m.HitFlashTimer -= dt;
            }

            // --- Mover ↔ Mover collisions (XZ sphere–sphere) ---
            for (int i = 0; i < _movers.Length; i++)
            {
                if (_movers[i].IsDead) continue;
                for (int j = i + 1; j < _movers.Length; j++)
                {
                    if (_movers[j].IsDead) continue;
                    ref Mover a = ref _movers[i];
                    ref Mover b = ref _movers[j];
                    float dx = b.Position.X - a.Position.X;
                    float dz = b.Position.Z - a.Position.Z;
                    float distSq = dx * dx + dz * dz;
                    float minDist = a.Radius + b.Radius;
                    if (distSq < minDist * minDist && distSq > 1e-4f)
                    {
                        float dist = MathF.Sqrt(distSq);
                        float nx = dx / dist, nz = dz / dist;
                        float overlap = minDist - dist;
                        // Separate
                        a.Position.X -= nx * overlap * 0.5f;
                        a.Position.Z -= nz * overlap * 0.5f;
                        b.Position.X += nx * overlap * 0.5f;
                        b.Position.Z += nz * overlap * 0.5f;
                        // Reflect normal-component velocities (equal mass swap)
                        float vAn = a.Velocity.X * nx + a.Velocity.Z * nz;
                        float vBn = b.Velocity.X * nx + b.Velocity.Z * nz;
                        float diff = vBn - vAn;
                        a.Velocity.X += diff * nx;
                        a.Velocity.Z += diff * nz;
                        b.Velocity.X -= diff * nx;
                        b.Velocity.Z -= diff * nz;

                        a.HitFlashTimer = FlashDuration;
                        b.HitFlashTimer = FlashDuration;
                    }
                }
            }

            // --- Mover ↔ Player collision (damage + push) ---
            for (int i = 0; i < _movers.Length; i++)
            {
                ref Mover m = ref _movers[i];
                if (m.IsDead) continue;
                float dx = m.Position.X - px;
                float dz = m.Position.Z - pz;
                float distSq = dx * dx + dz * dz;
                float minDist = m.Radius + PlayerRadius;
                if (distSq < minDist * minDist && distSq > 1e-4f)
                {
                    float dist = MathF.Sqrt(distSq);
                    float nx = dx / dist, nz = dz / dist;
                    float overlap = minDist - dist;

                    // Push player back, push mover away
                    float newPx = player.Position.X - nx * overlap * 0.5f;
                    float newPz = player.Position.Z - nz * overlap * 0.5f;
                    newPx = Math.Clamp(newPx, -half, half);
                    newPz = Math.Clamp(newPz, -half, half);
                    player.Position = new Vector3(newPx, player.Position.Y, newPz);
                    m.Position.X += nx * overlap * 0.5f;
                    m.Position.Z += nz * overlap * 0.5f;

                    // Bounce mover off the player
                    float vmn = m.Velocity.X * nx + m.Velocity.Z * nz;
                    m.Velocity.X -= 2f * vmn * nx;
                    m.Velocity.Z -= 2f * vmn * nz;
                    m.HitFlashTimer = FlashDuration;

                    // Damage on cooldown so a single contact frame doesn't drain HP instantly
                    if (_playerHitCooldown <= 0f && UIRenderer.CurrentHP > 0)
                    {
                        UIRenderer.CurrentHP = (int)MathF.Max(0f, UIRenderer.CurrentHP - PlayerDamage);
                        _playerHitCooldown = PlayerHitCooldownDuration;
                        UIRenderer.HitFlashTimer = FlashDuration;
                    }
                }
            }
        }

        public void Draw()
        {
            GL.UseProgram(_shaderProgram);
            OpenGL.EnableFaceCulling(false);

            // Sampler unit + alpha cutoff are constant across all draw calls here.
            if (_diffuseTexLocation  >= 0) GL.Uniform1i(_diffuseTexLocation, 0);
            if (_alphaCutoffLocation >= 0) GL.Uniform1f(_alphaCutoffLocation, 0.5f);

            for (int i = 0; i < _movers.Length; i++)
            {
                ref Mover m = ref _movers[i];

                // Bump animation: scale up briefly when flashing.
                // Chasing characters are also slightly larger so the player can see who's locked on.
                float t = MathF.Max(0f, m.HitFlashTimer / FlashDuration);
                float chaseBoost = !m.IsDead && m.IsChasing ? 1.15f : 1.0f;
                float scale = ModelScale * chaseBoost * (1.0f + t * 0.4f);
                float yawRad = (m.YawDegrees + ModelYawOffsetDeg) * MathF.PI / 180f;

                // Death animation: tilt forward (rotate around X) from 0 to 90° over the death duration.
                float deathPitch = 0f;
                if (m.IsDead)
                {
                    float dt2 = MathF.Min(1f, m.DeathTimer / Const.ENEMY_DEATH_DURATION);
                    deathPitch = -dt2 * (MathF.PI * 0.5f);
                }

                Matrix4x4 model =
                    Matrix4x4.CreateScale(scale) *
                    Matrix4x4.CreateRotationX(deathPitch) *
                    Matrix4x4.CreateRotationY(yawRad) *
                    Matrix4x4.CreateTranslation(m.Position);
                GL.UniformMatrix4fv(_modelLocation, 1, false, (float*)&model);

                // Draw each material segment with its own texture (or the fallback color).
                foreach (MeshSegment seg in _segments)
                {
                    bool hasTex = seg.DiffuseTex != null;
                    if (_useTextureLocation >= 0) GL.Uniform1i(_useTextureLocation, hasTex ? 1 : 0);
                    if (hasTex) seg.DiffuseTex!.Bind(0);

                    GL.BindVertexArray(seg.Vao);
                    GL.DrawArrays(Const.GL_TRIANGLES, 0, seg.VertexCount);
                }
            }

            // Reset texture state so other renderers (terrain, cubes) keep using the vertex-color path.
            if (_useTextureLocation >= 0) GL.Uniform1i(_useTextureLocation, 0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.BindVertexArray(0);
            OpenGL.EnableFaceCulling(true);
        }

        // Damage the first live mover whose collision sphere contains `point`.
        // Returns true if a mover was hit (so the projectile knows to despawn).
        public bool HitProjectile(Vector3 point, float radius, float damage)
        {
            for (int i = 0; i < _movers.Length; i++)
            {
                ref Mover m = ref _movers[i];
                if (m.IsDead) continue;
                float dx = m.Position.X - point.X;
                float dy = m.Position.Y - point.Y;
                float dz = m.Position.Z - point.Z;
                float r = m.Radius + radius;
                if (dx * dx + dy * dy + dz * dz <= r * r)
                {
                    DamageMover(ref m, damage);
                    return true;
                }
            }
            return false;
        }

        // Cone hit-test in the XZ plane: damage every live mover within `range` of `from`
        // whose direction is within `coneDot` (cos of half-angle) of `forward`.
        public int HitMelee(Vector3 from, Vector3 forward, float range, float coneDot, float damage)
        {
            int hits = 0;
            // Flatten forward to XZ + normalize for the angular test.
            float fx = forward.X, fz = forward.Z;
            float fl = MathF.Sqrt(fx * fx + fz * fz);
            if (fl < 1e-4f) return 0;
            fx /= fl; fz /= fl;

            for (int i = 0; i < _movers.Length; i++)
            {
                ref Mover m = ref _movers[i];
                if (m.IsDead) continue;
                float dx = m.Position.X - from.X;
                float dz = m.Position.Z - from.Z;
                float distSq = dx * dx + dz * dz;
                float reach = range + m.Radius;
                if (distSq > reach * reach) continue;
                float dist = MathF.Sqrt(distSq);
                if (dist < 1e-4f) { DamageMover(ref m, damage); hits++; continue; }
                float dot = (dx * fx + dz * fz) / dist;
                if (dot >= coneDot)
                {
                    DamageMover(ref m, damage);
                    hits++;
                }
            }
            return hits;
        }

        private static void DamageMover(ref Mover m, float damage)
        {
            m.HP -= damage;
            m.HitFlashTimer = FlashDuration;
            if (m.HP <= 0f && !m.IsDead)
            {
                m.HP = 0f;
                m.IsDead = true;
                m.DeathTimer = 0f;
                // Freeze velocity so a dying enemy doesn't keep sliding.
                m.Velocity = Vector3.Zero;
            }
        }

        public void Dispose()
        {
            foreach (MeshSegment seg in _segments)
            {
                if (seg.Vao != 0) { fixed (uint* p = &seg.Vao) GL.DeleteVertexArrays(1, p); seg.Vao = 0; }
                if (seg.Vbo != 0) { fixed (uint* p = &seg.Vbo) GL.DeleteBuffers(1, p); seg.Vbo = 0; }
            }
            _segments.Clear();
            foreach (var t in _textureCache.Values) t.Dispose();
            _textureCache.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
