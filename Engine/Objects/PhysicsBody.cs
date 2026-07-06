using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    /// <summary>
    /// Physics body component for characters.
    /// Handles velocity, gravity, and ground state for proper physics simulation.
    /// </summary>
    public struct PhysicsBody
    {
        /// <summary>Current 3D velocity of the character (world units/sec).</summary>
        public Vector3 Velocity;

        /// <summary>Whether the character is currently on the ground.</summary>
        public bool IsGrounded;

        /// <summary>Multiplier applied to gravity (1.0 = normal gravity).</summary>
        public float GravityScale;

        /// <summary>Whether gravity affects this body.</summary>
        public bool UseGravity;

        /// <summary>Gravity acceleration constant (world units/s²). Negative = downward.</summary>
        public const float GravityAccel = -25f;

        /// <summary>Create default physics body with gravity enabled.</summary>
        public PhysicsBody()
        {
            Velocity = Vector3.Zero;
            IsGrounded = true;
            GravityScale = 1f;
            UseGravity = true;
        }

        /// <summary>Reset all physics state.</summary>
        public void Reset()
        {
            Velocity = Vector3.Zero;
            IsGrounded = true;
            GravityScale = 1f;
            UseGravity = true;
        }
    }
}
