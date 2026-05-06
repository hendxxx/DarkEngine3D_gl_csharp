using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine
{ 
    public unsafe class Camera
    {
        public Vector3 Position = new(0, 0, 0);
        public Vector3 Front = new(0, 0, 0);
        public Vector3 Up = Vector3.UnitY;

        // Tambahkan variabel rotasi
        public float Yaw = 0.0f; // Menghadap ke depan (sumbu -Z)
        public float Pitch = 0.0f;

        public float foV ;
        public float nearDist;
        public float farDist;
        public float aspect;

        public Camera(float x, float y, float z, float _aspect, float _foV, float _nearDist, float _farDist)
        { 
            aspect = _aspect;
            foV = _foV;
            nearDist = _nearDist;
            farDist = _farDist; 

            Init(x, y, z);
        }
        public void Init(float x, float y, float z)
        {
            Position = new(x,y,z);
            Front = new(0, 0, -1);
            Up = Vector3.UnitY;

            // Tambahkan variabel rotasi
            Yaw = -90.0f; // Menghadap ke depan (sumbu -Z)
            Pitch = 0.0f;
        }
        public void UpdateVectors()
        {
            // Matematika untuk mengubah Yaw/Pitch menjadi vektor arah (Front)
            Vector3 direction;
            direction.X = MathF.Cos(Helpers.OGLMath.ToRadians(Yaw)) * MathF.Cos(Helpers.OGLMath.ToRadians(Pitch));
            direction.Y = MathF.Sin(Helpers.OGLMath.ToRadians(Pitch));
            direction.Z = MathF.Sin(Helpers.OGLMath.ToRadians(Yaw)) * MathF.Cos(Helpers.OGLMath.ToRadians(Pitch));
            Front = Vector3.Normalize(direction);
        }
        public float GetAspect() => aspect;

        public Matrix4x4 GetViewMatrix() => Matrix4x4.CreateLookAt(Position, Position + Front, Up);
        public static Matrix4x4 GetProjectionMatrix(float aspect, float foV, float nearDist, float farDist) => Matrix4x4.CreatePerspectiveFieldOfView(foV, aspect, nearDist, farDist);

        public void SetViewAndProjection(  int viewLocation, int projectionLocation)
        {
            
            Matrix4x4 view = GetViewMatrix();
            Matrix4x4 projection = GetProjectionMatrix(aspect, foV, nearDist, farDist);
            unsafe
            {
                // Mengambil pointer dari matriks C# dan mengirimnya ke GPU
                GL.UniformMatrix4fv(viewLocation, 1, false, (float*)&view);
                GL.UniformMatrix4fv(projectionLocation, 1, false, (float*)&projection);
            }
        }
}
}
