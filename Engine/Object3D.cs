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
            glfwGetKey = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)NativeLibrary.GetExport(glfwLib, "glfwGetKey");

            ShaderProgram = Shader.GetShaderProgram(); 
            Generate(ShaderProgram);
            SetPosition(x, y, z);   
        }
        public void SetPosition(float x, float y, float z)
        {
            // Hanya menyimpan posisi; lokasi uniform sudah dicache di Generate()
            trianglePosition = new Vector3(x,y,z); // Posisi awal segitiga
        }
        public Vector3 GetPosition()
        {
            return trianglePosition;
        }

        public void Generate(uint shaderProgram) {

            Vertex[] vertices = new Vertex[] {
                // 1. Kiri Bawah
                new Vertex(-0.5f, -0.5f, 0.0f,  0.0f, 0.0f, 1.0f,  1.0f, 0.0f, 0.0f), 
    
                // 2. Atas (Puncak) - Ditukar ke posisi kedua
                new Vertex( 0.0f,  0.5f, 0.0f,  0.0f, 0.0f, 1.0f,  0.0f, 0.0f, 1.0f),
    
                // 3. Kanan Bawah - Ditukar ke posisi ketiga
                new Vertex( 0.5f, -0.5f, 0.0f,  0.0f, 0.0f, 1.0f,  0.0f, 1.0f, 0.0f)
            };

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

            // Cache lokasi uniform 'model' sekali saja (shader program sudah aktif di loop sebelum Draw)
            modelLocation = GL.GetUniformLocation(shaderProgram, "model");
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
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, 0x1406, false, stride, (void*)(sizeof(Vector3) * 2));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 2, 0x1406, false, stride, (void*)(sizeof(Vector3) * 3));
        }

        public void Draw(float deltaTime, nint window,float _moveSpeed)
        {
            OpenGL.EnableFaceCulling(false);
            // If Shift is held, boost the movement speed for the object as well
            bool shiftPressed = glfwGetKey(window, Const.GLFW_KEY_LEFT_SHIFT) == Const.GLFW_PRESS
                                || glfwGetKey(window, Const.GLFW_KEY_RIGHT_SHIFT) == Const.GLFW_PRESS;

            float moveSpeed = _moveSpeed * deltaTime * (shiftPressed ? Const.SHIFT_SPEED_MULTIPLIER : 1.0f);

            // Contoh kontrol: Panah atau IJKL untuk gerak segitiga
            if (glfwGetKey(window, Const.GLFW_KEY_I) == Const.GLFW_PRESS) trianglePosition.Y += moveSpeed; // I (Atas)
            if (glfwGetKey(window, Const.GLFW_KEY_K) == Const.GLFW_PRESS) trianglePosition.Y -= moveSpeed; // K (Bawah)
            if (glfwGetKey(window, Const.GLFW_KEY_J) == Const.GLFW_PRESS) trianglePosition.X -= moveSpeed; // J (Kiri)
            if (glfwGetKey(window, Const.GLFW_KEY_L) == Const.GLFW_PRESS) trianglePosition.X += moveSpeed; // L (Kanan)

            // Set posisi (cukup update trianglePosition; uniform sudah dicache)
            // SetPosition(trianglePosition.X, trianglePosition.Y, trianglePosition.Z);

            Matrix4x4 modelMatrix = Matrix4x4.CreateTranslation(trianglePosition);
            GL.UniformMatrix4fv(modelLocation, 1, false, (float*)&modelMatrix); 

            // GAMBAR!
            GL.BindVertexArray(VAO);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, _vertexCount); // 0x0004 = GL_TRIANGLES
            GL.BindVertexArray(0); // <--- PENTING: Lepaskan VAO TerrainChunk

            OpenGL.EnableFaceCulling(true);
        }
    }
}
