using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DarkEngine3D_gl_csharp.Engine
{
    public unsafe class Object3D
    {
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int> glfwGetKey;

        private uint _shaderProgram;
        public uint ShaderProgram
        {
            get => _shaderProgram;
            private set => _shaderProgram = value;
        }


        private int modelLocation;
        private Vector3 trianglePosition;

        public uint VAO, VBO;
        private int _vertexCount;
        public Object3D(nint glfwLib ,  float x, float y, float z)
        {
            ShaderProgram = Shader.GetShaderProgram();

            glfwGetKey = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)NativeLibrary.GetExport(glfwLib, "glfwGetKey");
             
            Generate(ShaderProgram);
            SetPosition(x, y, z);   
        }
        public void SetPosition(float x, float y, float z)
        {

            modelLocation = GL.GetUniformLocation(ShaderProgram, "model");
            trianglePosition = new Vector3(x,y,z); // Posisi awal segitiga
        }
        public Vector3 GetPosition()
        {
            return trianglePosition;
        }

        public void Generate(uint shaderProgram) {
            // Buat Segitiga Sederhana
            // Data Segitiga
            Vertex[] vertices = [
                new (-0.5f, -0.5f, 0.0f,  1.0f, 0.0f, 0.0f),
                new ( 0.5f, -0.5f, 0.0f,  0.0f, 1.0f, 0.0f),
                new ( 0.0f,  0.5f, 0.0f,  0.0f, 0.0f, 1.0f)
            ];

            // Buat VBO di GPU
            uint vbo;
            GL.GenBuffers(1, &vbo);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

            // Kirim data menggunakan Span (Zero-copy)
            fixed (void* ptr = vertices)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(vertices.Length * sizeof(Vertex)), ptr, Const.GL_STATIC_DRAW);
            }

            _vertexCount = vertices.Length;
            SetupGPUResources(vertices);

        }

        private void SetupGPUResources(Vertex[] data)
        {
            // Pastikan Anda sudah mem-binding fungsi VAO (GenVertexArrays, BindVertexArray) di class GL
            fixed (uint* pVao = &VAO) GL.GenVertexArrays(1, pVao);
            fixed (uint* pVbo = &VBO) GL.GenBuffers(1, pVbo);

            GL.BindVertexArray(VAO);
            GL.BindBuffer(0x8892, VBO); // GL_ARRAY_BUFFER

            fixed (void* ptr = data)
            {
                GL.BufferData(0x8892, (nuint)(data.Length * sizeof(Vertex)), ptr, 0x88E4); // GL_STATIC_DRAW
            }

            // Setup Attributes (Posisi & Warna)
            int stride = sizeof(Vertex);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, 0x1406, false, stride, (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, 0x1406, false, stride, (void*)sizeof(Vector3));
        }

        public void Draw(float deltaTime, nint window)
        {

            float moveSpeed = 5.0f * deltaTime;

            // Contoh kontrol: Panah atau IJKL untuk gerak segitiga
            if (glfwGetKey(window, 73) == 1) trianglePosition.Y += moveSpeed; // I (Atas)
            if (glfwGetKey(window, 75) == 1) trianglePosition.Y -= moveSpeed; // K (Bawah)
            if (glfwGetKey(window, 74) == 1) trianglePosition.X -= moveSpeed; // J (Kiri)
            if (glfwGetKey(window, 76) == 1) trianglePosition.X += moveSpeed; // L (Kanan)

            SetPosition(trianglePosition.X, trianglePosition.Y, trianglePosition.Z);

            Matrix4x4 modelMatrix = Matrix4x4.CreateTranslation(trianglePosition);
            GL.UniformMatrix4fv(modelLocation, 1, true, (float*)&modelMatrix); 

            // GAMBAR!
            GL.BindVertexArray(VAO);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, _vertexCount); // 0x0004 = GL_TRIANGLES
            GL.BindVertexArray(0); // <--- PENTING: Lepaskan VAO terrain
        }
    }
}
