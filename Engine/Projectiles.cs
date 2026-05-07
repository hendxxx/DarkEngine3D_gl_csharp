using System;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine
{
    // Pool of player-thrown stones. Each stone is a small grey cube that flies under gravity,
    // damages the first live enemy it touches, and despawns on terrain hit / lifetime expiry.
    public unsafe class Projectiles : IDisposable
    {
        private struct Stone
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Lifetime;
            public bool Active;
        }

        private const int Capacity = 32;
        private const float StoneVisualSize = 0.5f; // visual cube side in world units

        private readonly Stone[] _pool = new Stone[Capacity];
        private uint _vao, _vbo;
        private int _vertexCount;
        private uint _shaderProgram;
        private int _modelLocation;

        public Projectiles()
        {
            _shaderProgram = Shader.GetShaderProgram();
            _modelLocation = GL.GetUniformLocation(_shaderProgram, "model");
            BuildCube();
        }

        // Spawn a stone; ignored silently if the pool is full.
        public void Spawn(Vector3 position, Vector3 velocity)
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].Active)
                {
                    _pool[i] = new Stone
                    {
                        Position = position,
                        Velocity = velocity,
                        Lifetime = 0f,
                        Active = true
                    };
                    return;
                }
            }
        }

        public void Update(float dt, MovingObjects movers)
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].Active) continue;
                ref Stone s = ref _pool[i];

                s.Velocity.Y -= Const.STONE_GRAVITY * dt;
                s.Position += s.Velocity * dt;
                s.Lifetime += dt;

                // Hit terrain
                float groundY = TerrainChunk.GetHeightAt(s.Position.X, s.Position.Z);
                if (s.Position.Y <= groundY)
                {
                    s.Active = false;
                    continue;
                }

                // Hit any live enemy
                if (movers.HitProjectile(s.Position, Const.STONE_RADIUS, Const.STONE_DAMAGE))
                {
                    s.Active = false;
                    continue;
                }

                if (s.Lifetime > Const.STONE_LIFETIME) s.Active = false;
            }
        }

        public void Draw()
        {
            GL.UseProgram(_shaderProgram);
            OpenGL.EnableFaceCulling(false);
            GL.BindVertexArray(_vao);

            for (int i = 0; i < _pool.Length; i++)
            {
                if (!_pool[i].Active) continue;
                ref Stone s = ref _pool[i];

                Matrix4x4 model =
                    Matrix4x4.CreateScale(StoneVisualSize) *
                    Matrix4x4.CreateTranslation(s.Position);
                GL.UniformMatrix4fv(_modelLocation, 1, false, (float*)&model);
                GL.DrawArrays(Const.GL_TRIANGLES, 0, _vertexCount);
            }

            GL.BindVertexArray(0);
            OpenGL.EnableFaceCulling(true);
        }

        // Shared 1×1×1 grey cube — same pattern as the old MovingObjects cube.
        private void BuildCube()
        {
            float h = 0.5f;
            float r = 0.45f, g = 0.45f, b = 0.40f;
            Vertex V(float x, float y, float z, float nx, float ny, float nz)
                => new Vertex(x, y, z, nx, ny, nz, r, g, b);

            Vertex[] v = new Vertex[]
            {
                V(-h,-h, h, 0,0, 1), V( h,-h, h, 0,0, 1), V( h, h, h, 0,0, 1),
                V(-h,-h, h, 0,0, 1), V( h, h, h, 0,0, 1), V(-h, h, h, 0,0, 1),
                V( h,-h,-h, 0,0,-1), V(-h,-h,-h, 0,0,-1), V(-h, h,-h, 0,0,-1),
                V( h,-h,-h, 0,0,-1), V(-h, h,-h, 0,0,-1), V( h, h,-h, 0,0,-1),
                V( h,-h, h, 1,0, 0), V( h,-h,-h, 1,0, 0), V( h, h,-h, 1,0, 0),
                V( h,-h, h, 1,0, 0), V( h, h,-h, 1,0, 0), V( h, h, h, 1,0, 0),
                V(-h,-h,-h,-1,0, 0), V(-h,-h, h,-1,0, 0), V(-h, h, h,-1,0, 0),
                V(-h,-h,-h,-1,0, 0), V(-h, h, h,-1,0, 0), V(-h, h,-h,-1,0, 0),
                V(-h, h, h, 0,1, 0), V( h, h, h, 0,1, 0), V( h, h,-h, 0,1, 0),
                V(-h, h, h, 0,1, 0), V( h, h,-h, 0,1, 0), V(-h, h,-h, 0,1, 0),
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
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 2, Const.GL_FLOAT, false, stride, (void*)(sizeof(Vector3) * 3));
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            if (_vao != 0) { fixed (uint* p = &_vao) GL.DeleteVertexArrays(1, p); _vao = 0; }
            if (_vbo != 0) { fixed (uint* p = &_vbo) GL.DeleteBuffers(1, p); _vbo = 0; }
            GC.SuppressFinalize(this);
        }
    }
}
