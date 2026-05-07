using System;
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
        }

        // Tunables
        private const float MoverRadius     = 1.5f;
        private const float MoverHalfHeight = 1.0f;
        private const float WanderSpeed     = 5.0f;
        private const float ChaseSpeed      = 12.0f;   // faster than walk (10) — player must run to escape
        private const float ChaseStartRange = 30.0f;   // detection: spot the player at this radius
        private const float ChaseStopRange  = 50.0f;   // hysteresis: must escape past this to break aggro
        private const float PlayerRadius    = 0.7f;
        private const float PlayerDamage    = 12f;     // HP per bump
        private const float PlayerHitCooldownDuration = 1.0f;
        private const float FlashDuration   = 0.4f;
        private const float CubeSide        = 2.0f;    // visual size

        private readonly Mover[] _movers;
        private readonly Random _rng;
        private uint _vao, _vbo;
        private int _vertexCount;
        private uint _shaderProgram;
        private int _modelLocation;
        private float _playerHitCooldown = 0f;

        public MovingObjects(int count, int seed = 12345)
        {
            _shaderProgram = Shader.GetShaderProgram();
            _modelLocation = GL.GetUniformLocation(_shaderProgram, "model");
            BuildCube();

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
                    WanderTimer = NextWanderInterval()
                };
                _movers[i].Position.Y = TerrainChunk.GetHeightAt(x, z) + MoverHalfHeight;
            }
        }

        // Random 2–5 second interval between wander direction changes.
        private float NextWanderInterval() => 2.0f + (float)_rng.NextDouble() * 3.0f;

        // Unit cube (side = 1) with per-face normals, one solid color.
        private void BuildCube()
        {
            float h = 0.5f;
            float r = 1.0f, g = 0.45f, b = 0.1f; // orange

            Vertex V(float x, float y, float z, float nx, float ny, float nz)
                => new Vertex(x, y, z, nx, ny, nz, r, g, b);

            Vertex[] v = new Vertex[]
            {
                // +Z (front)
                V(-h,-h, h, 0,0, 1), V( h,-h, h, 0,0, 1), V( h, h, h, 0,0, 1),
                V(-h,-h, h, 0,0, 1), V( h, h, h, 0,0, 1), V(-h, h, h, 0,0, 1),
                // -Z (back)
                V( h,-h,-h, 0,0,-1), V(-h,-h,-h, 0,0,-1), V(-h, h,-h, 0,0,-1),
                V( h,-h,-h, 0,0,-1), V(-h, h,-h, 0,0,-1), V( h, h,-h, 0,0,-1),
                // +X (right)
                V( h,-h, h, 1,0, 0), V( h,-h,-h, 1,0, 0), V( h, h,-h, 1,0, 0),
                V( h,-h, h, 1,0, 0), V( h, h,-h, 1,0, 0), V( h, h, h, 1,0, 0),
                // -X (left)
                V(-h,-h,-h,-1,0, 0), V(-h,-h, h,-1,0, 0), V(-h, h, h,-1,0, 0),
                V(-h,-h,-h,-1,0, 0), V(-h, h, h,-1,0, 0), V(-h, h,-h,-1,0, 0),
                // +Y (top)
                V(-h, h, h, 0,1, 0), V( h, h, h, 0,1, 0), V( h, h,-h, 0,1, 0),
                V(-h, h, h, 0,1, 0), V( h, h,-h, 0,1, 0), V(-h, h,-h, 0,1, 0),
                // -Y (bottom)
                V(-h,-h,-h, 0,-1,0), V( h,-h,-h, 0,-1,0), V( h,-h, h, 0,-1,0),
                V(-h,-h,-h, 0,-1,0), V( h,-h, h, 0,-1,0), V(-h,-h, h, 0,-1,0),
            };

            _vertexCount = v.Length;

            fixed (uint* p = &_vao) GL.GenVertexArrays(1, p);
            fixed (uint* p = &_vbo) GL.GenBuffers(1, p);

            GL.BindVertexArray(_vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _vbo);
            fixed (void* ptr = v)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(v.Length * sizeof(Vertex)), ptr, Const.GL_STATIC_DRAW);
            }

            int stride = sizeof(Vertex);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)sizeof(Vector3));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, Const.GL_FLOAT, false, stride, (void*)(sizeof(Vector3) * 2));
            GL.BindVertexArray(0);
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

                m.Position.Y = TerrainChunk.GetHeightAt(m.Position.X, m.Position.Z) + MoverHalfHeight;
                if (m.HitFlashTimer > 0f) m.HitFlashTimer -= dt;
            }

            // --- Mover ↔ Mover collisions (XZ sphere–sphere) ---
            for (int i = 0; i < _movers.Length; i++)
            {
                for (int j = i + 1; j < _movers.Length; j++)
                {
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
            OpenGL.EnableFaceCulling(false); // skip winding concerns for the cube
            GL.BindVertexArray(_vao);

            for (int i = 0; i < _movers.Length; i++)
            {
                ref Mover m = ref _movers[i];

                // Bump animation: scale up briefly when flashing.
                // Chasing cubes are also slightly larger so the player can see who's locked on.
                float t = MathF.Max(0f, m.HitFlashTimer / FlashDuration);
                float chaseBoost = m.IsChasing ? 1.15f : 1.0f;
                float scale = CubeSide * chaseBoost * (1.0f + t * 0.4f);

                Matrix4x4 model = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(m.Position);
                GL.UniformMatrix4fv(_modelLocation, 1, false, (float*)&model);
                GL.DrawArrays(Const.GL_TRIANGLES, 0, _vertexCount);
            }

            GL.BindVertexArray(0);
            OpenGL.EnableFaceCulling(true);
        }

        public void Dispose()
        {
            if (_vao != 0) { fixed (uint* p = &_vao) GL.DeleteVertexArrays(1, p); _vao = 0; }
            if (_vbo != 0) { fixed (uint* p = &_vbo) GL.DeleteBuffers(1, p); _vbo = 0; }
            GC.SuppressFinalize(this);
        }
    }
}
