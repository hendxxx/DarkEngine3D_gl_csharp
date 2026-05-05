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
         
        public Camera(float x, float y, float z)
        { 
            Init(x,y,z);
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
            direction.X = MathF.Cos(Helpers.Math.ToRadians(Yaw)) * MathF.Cos(Helpers.Math.ToRadians(Pitch));
            direction.Y = MathF.Sin(Helpers.Math.ToRadians(Pitch));
            direction.Z = MathF.Sin(Helpers.Math.ToRadians(Yaw)) * MathF.Cos(Helpers.Math.ToRadians(Pitch));
            Front = Vector3.Normalize(direction);
        }

        public Matrix4x4 GetViewMatrix() => Matrix4x4.CreateLookAt(Position, Position + Front, Up);
        public Matrix4x4 GetProjectionMatrix(float aspect) => Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, aspect, 0.01f, 1000.0f);

        public void SetViewAndProjection(int Width, int Height, int viewLocation, int projectionLocation)
        {
            //Cameraaa and .. action..
            Matrix4x4 view = GetViewMatrix();
            Matrix4x4 projection = GetProjectionMatrix((float)Width / Height);
            unsafe
            {
                // Mengambil pointer dari matriks C# dan mengirimnya ke GPU
                GL.UniformMatrix4fv(viewLocation, 1, false, (float*)&view);
                GL.UniformMatrix4fv(projectionLocation, 1, false, (float*)&projection);
            }
        }
}
}
