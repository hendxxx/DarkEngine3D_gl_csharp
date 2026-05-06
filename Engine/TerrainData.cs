using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static DarkEngine3D_gl_csharp.Engine.Helpers;

namespace DarkEngine3D_gl_csharp.Engine
{
    public unsafe class TerrainChunkData : IDisposable
    {

        public uint VAO, VBO;
        private int _vertexCount;
        public float MinY { get; private set; } = float.MaxValue;
        public float MaxY { get; private set; } = float.MinValue;

        // Kita gunakan List sementara saat Generate, lalu upload ke Native Memory
        public void Generate(int ChunkSize,int worldStartX, int worldStartZ)
        {
            List<Vertex> vertices = [];
            int size = ChunkSize;
            
            MinY = float.MaxValue;
            MaxY = float.MinValue;

            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Langsung gunakan worldStartX sebagai dasar
                    float xWorld = x + worldStartX;
                    float zWorld = z + worldStartZ;

                    AddQuad(vertices, xWorld, zWorld);
                }
            }

            _vertexCount = vertices.Count;
            SetupGPUResources([.. vertices]);
        }

        private static void AddQuad(List<Vertex> vertices, float x, float z)
        {
            // 1. Dapatkan Ketinggian (Y)
            float h00 = Noise.GetHeight(x, z);          // Kiri Bawah
            float h10 = Noise.GetHeight(x + 1, z);      // Kanan Bawah
            float h01 = Noise.GetHeight(x, z + 1);      // Kiri Atas
            float h11 = Noise.GetHeight(x + 1, z + 1);  // Kanan Atas

            //float h00 = 0;
            //float h10 = 0;
            //float h01 = 0;
            //float h11 = 0;

            // 2. Hitung normal (Default awal)
            Vector3 n00, n10, n01, n11;

            // Cek jika semua ketinggian adalah 0
            if (h00 == 0 && h10 == 0 && h01 == 0 && h11 == 0)
            {
                n00 = new Vector3(0, 1, 0);
                n10 = new Vector3(0, 1, 0);
                n01 = new Vector3(0, 1, 0);
                n11 = new Vector3(0, 1, 0);
            }
            else
            {
                // Jika tidak nol, gunakan fungsi hitung normal yang asli
                n00 = Helpers.CalculateNormal(x, z);
                n10 = Helpers.CalculateNormal(x + 1, z);
                n01 = Helpers.CalculateNormal(x, z + 1);
                n11 = Helpers.CalculateNormal(x + 1, z + 1);
            }

            Vector3 color = new(0.1f, 0.5f, 0.1f); // Hijau

            // --- SEGITIGA 1 ---
            // Titik: Kiri Bawah -> Kanan Bawah -> Kanan Atas
            vertices.Add(new Vertex(x, h00, z, n00.X, n00.Y, n00.Z, color.X, color.Y, color.Z));
            vertices.Add(new Vertex(x + 1, h10, z, n10.X, n10.Y, n10.Z, color.X, color.Y, color.Z));
            vertices.Add(new Vertex(x + 1, h11, z + 1, n11.X, n11.Y, n11.Z, color.X, color.Y, color.Z));

            // --- SEGITIGA 2 ---
            // Titik: Kiri Bawah -> Kanan Atas -> Kiri Atas
            vertices.Add(new Vertex(x, h00, z, n00.X, n00.Y, n00.Z, color.X, color.Y, color.Z));
            vertices.Add(new Vertex(x + 1, h11, z + 1, n11.X, n11.Y, n11.Z, color.X, color.Y, color.Z));
            vertices.Add(new Vertex(x, h01, z + 1, n01.X, n01.Y, n01.Z, color.X, color.Y, color.Z));
        }

       

        private void SetupGPUResources(Vertex[] data)
        {
            // Pastikan fungsi ini sudah dimuat di ApiLoader
            fixed (uint* pVao = &VAO) GL.GenVertexArrays(1, pVao);
            fixed (uint* pVbo = &VBO) GL.GenBuffers(1, pVbo);

            GL.BindVertexArray(VAO);
            GL.BindBuffer(0x8892, VBO); // GL_ARRAY_BUFFER

            fixed (void* ptr = data)
            {
                GL.BufferData(0x8892, (nuint)(data.Length * sizeof(Vertex)), ptr, 0x88E4); // GL_STATIC_DRAW
            }

            // Atribut 0: Posisi (vec3)
            // Setup Attributes (Posisi & Warna)
            int stride = sizeof(Vertex);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, 0x1406, false, stride, (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, 0x1406, false, stride, (void*)sizeof(Vector3));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, 0x1406, false, stride, (void*)(sizeof(Vector3) * 2));

            
        }

        public void Draw()
        {
            if (VAO == 0) return;
            GL.BindVertexArray(VAO);
            GL.DrawArrays(0x0004, 0, _vertexCount); // GL_TRIANGLES
            // Unbind untuk keamanan
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            // Penting: Hapus resource di GPU saat chunk tidak lagi digunakan
            fixed (uint* pVao = &VAO) GL.DeleteVertexArrays(1, pVao);
            fixed (uint* pVbo = &VBO) GL.DeleteBuffers(1, pVbo);
            GC.SuppressFinalize(this);
        }
    }


}
