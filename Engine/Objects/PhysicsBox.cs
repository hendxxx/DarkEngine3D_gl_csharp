using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    /// <summary>
    /// A colored box with physics — falls due to gravity, lands on terrain.
    /// Uses Object3D for rendering and PhysicsBody for velocity/gravity simulation.
    /// </summary>
    public unsafe class PhysicsBox : IDisposable
    {
        public Object3D? Object3D;
        public PhysicsBody Physics;
        public Vector3 Position;
        public Vector3 Size;
        public Vector3 BoxColor;
        public bool Active = true;

        public float HalfHeight => Size.Y * 0.5f;
        public float HalfWidth => Size.X * 0.5f;
        public float HalfDepth => Size.Z * 0.5f;

        // Bounce coefficient: 0 = no bounce (stop), 0.3 = small bounce
        private const float BounceCoeff = 0f;
        // Ground friction applied on landing
        private const float GroundFriction = 0.95f;

        public PhysicsBox(Vector3 position, Vector3 size, Vector3 color)
        {
            Position = position;
            Size = size;
            BoxColor = color;

            // Create the renderable box Object3D
            Vertex[] verts = Object3D.CreateBoxVertices(size.X, size.Y, size.Z, color);
            Object3D = new Object3D(position.X, position.Y, position.Z);
            Object3D.Generate(Object3D.ShaderProgram, verts);
            Object3D.SetPosition(position.X, position.Y, position.Z);

            // Initialize physics
            Physics = new PhysicsBody
            {
                Velocity = Vector3.Zero,
                IsGrounded = false,
                GravityScale = 1f,
                UseGravity = true
            };
        }

        /// <summary>Apply gravity, update velocity, and resolve terrain collision.</summary>
        public void Update(float dt, TerrainChunk terrain)
        {
            if (!Active) return;

            // Apply gravity
            if (Physics.UseGravity)
            {
                Physics.Velocity.Y += PhysicsBody.GravityAccel * Physics.GravityScale * dt;
            }

            // Simple air drag for more natural falling feel
            Physics.Velocity.X *= 0.995f;
            Physics.Velocity.Z *= 0.995f;

            // Apply velocity to position
            Position += Physics.Velocity * dt;

            // Ground collision: check box bottom against terrain height
            float terrainY = terrain.GetHeightAt(Position.X, Position.Z);
            float boxBottom = Position.Y - HalfHeight;

            if (boxBottom <= terrainY)
            {
                // Landed on terrain
                Position.Y = terrainY + HalfHeight;

                if (BounceCoeff > 0f && MathF.Abs(Physics.Velocity.Y) > 2f)
                {
                    // Small bounce
                    Physics.Velocity.Y = -Physics.Velocity.Y * BounceCoeff;
                }
                else
                {
                    Physics.Velocity.Y = 0f;
                }

                Physics.IsGrounded = true;

                // Friction on ground
                Physics.Velocity.X *= GroundFriction;
                Physics.Velocity.Z *= GroundFriction;
            }
            else
            {
                Physics.IsGrounded = false;
            }

            // Sync Object3D position for rendering
            Object3D?.SetPosition(Position.X, Position.Y, Position.Z);
        }

        /// <summary>Draw the box using the main shader program.</summary>
        public void Draw()
        {
            if (!Active || Object3D == null) return;
            Object3D.Draw(0, nint.Zero, 0);
        }

        public void Dispose()
        {
            Object3D = null;
        }
    }
}
