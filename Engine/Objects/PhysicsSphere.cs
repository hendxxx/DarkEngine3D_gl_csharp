using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    /// <summary>
    /// A colored sphere with physics — falls due to gravity, lands on terrain.
    /// Uses Object3D for rendering and PhysicsBody for velocity/gravity simulation.
    /// </summary>
    public unsafe class PhysicsSphere : IDisposable
    {
        public Object3D? Object3D;
        public PhysicsBody Physics;
        public Vector3 Position;
        public float Radius;
        public Vector3 SphereColor;
        public bool Active = true;
        public bool IsVisible = true;

        // Cached world-space AABB untuk OC & frustum culling
        public Helpers.ObjectHelpers.AABB CachedWorldAABB;

        // Collision & Occlusion flags — mirip StaticObject
        public bool IsOccluder = true;
        public bool IsCollidable = true;
        public CollisionType ColType = CollisionType.Sphere;
        public bool CastShadow = true;

        // Rolling rotation (radians)
        public float RotationX, RotationZ;

        // Bounce coefficient: 0.5 = moderate bounce
        private const float BounceCoeff = 0.5f;
        // Ground friction applied on landing
        private const float GroundFriction = 0.95f;

        public PhysicsSphere(Vector3 position, float radius, Vector3 color)
        {
            Position = position;
            Radius = radius;
            SphereColor = color;

            // Create the renderable sphere Object3D
            Vertex[] verts = Object3D.CreateSphereVertices(radius, color);
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

            // Ground collision: check sphere bottom against terrain height
            float terrainY = terrain.GetHeightAt(Position.X, Position.Z);
            float sphereBottom = Position.Y - Radius;

            if (sphereBottom <= terrainY)
            {
                // Landed on terrain
                Position.Y = terrainY + Radius;

                // Always bounce with coefficient
                if (MathF.Abs(Physics.Velocity.Y) > 0.5f)
                {
                    Physics.Velocity.Y = -Physics.Velocity.Y * BounceCoeff;
                    // Reduce horizontal speed slightly on bounce
                    Physics.Velocity.X *= 0.9f;
                    Physics.Velocity.Z *= 0.9f;
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

            // Rolling rotation based on horizontal velocity
            float speed = MathF.Sqrt(Physics.Velocity.X * Physics.Velocity.X + Physics.Velocity.Z * Physics.Velocity.Z);
            if (speed > 0.01f && Physics.IsGrounded)
            {
                // Rotate sphere in direction of movement (roll = distance / radius)
                float dist = speed * dt;
                RotationX += (Physics.Velocity.Z / speed) * dist / Radius;
                RotationZ -= (Physics.Velocity.X / speed) * dist / Radius;
            }

            // Sync Object3D position + rotation for rendering
            Object3D?.SetPosition(Position.X, Position.Y, Position.Z);

            // Update cached AABB
            Vector3 half = new(Radius, Radius, Radius);
            CachedWorldAABB = new Helpers.ObjectHelpers.AABB(Position - half, Position + half);
        }

        /// <summary>Draw the sphere using the main shader program.</summary>
        public void Draw()
        {
            if (!Active || Object3D == null) return;
            // Apply rolling rotation to the model matrix before drawing
            if (Object3D != null)
            {
                var rotMat = Matrix4x4.CreateRotationX(RotationX) * Matrix4x4.CreateRotationZ(RotationZ);
                var transMat = Matrix4x4.CreateTranslation(Position);
                Object3D.UpdateModelMatriC(rotMat * transMat);
            }
            Object3D.Draw(0, nint.Zero, 0);
        }

        public void Dispose()
        {
            Object3D = null;
        }
    }
}
