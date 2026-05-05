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
    public unsafe class TerrainChunk : IDisposable
    {

        public uint VAO, VBO;
        private int _vertexCount;

        // Kita gunakan List sementara saat Generate, lalu upload ke Native Memory
        public void Generate(int worldStartX, int worldStartZ)
        {
            List<Vertex> vertices = new List<Vertex>();
            int size = 16;

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
            SetupGPUResources(vertices.ToArray());
        }

        private void AddQuad(List<Vertex> vertices, float x, float z)
        {
            // Ambil ketinggian dari fungsi Noise
            //float h00 = Noise.GetHeight(x, z);
            //float h10 = Noise.GetHeight(x + 1, z);
            //float h01 = Noise.GetHeight(x, z + 1);
            //float h11 = Noise.GetHeight(x + 1, z + 1);
            float h00 = 0; // Contoh datar
            float h10 = 0; // Contoh datar
            float h01 = 0; // Contoh datar
            float h11 = 0; // Contoh datar

            // Warna hijau rumput (bisa dimodifikasi berdasarkan ketinggian)
            Vector3 color = new Vector3(0.1f, 0.4f, 0.1f);

            // Segitiga 1
            vertices.Add(new Vertex(x, h00, z, color.X, color.Y, color.Z));
            vertices.Add(new Vertex(x + 1, h10, z, color.X, color.Y, color.Z));
            vertices.Add(new Vertex(x, h01, z + 1, color.X, color.Y, color.Z));

            // Segitiga 2
            vertices.Add(new Vertex(x + 1, h10, z, color.X, color.Y, color.Z));
            vertices.Add(new Vertex(x + 1, h11, z + 1, color.X, color.Y, color.Z));
            vertices.Add(new Vertex(x, h01, z + 1, color.X, color.Y, color.Z));
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
            int stride = sizeof(Vertex);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, 0x1406, false, stride, (void*)0);

            // Atribut 1: Warna (vec3)
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, 0x1406, false, stride, (void*)sizeof(Vector3));

            // Unbind untuk keamanan
            GL.BindVertexArray(0);
        }

        public void Draw()
        {
            if (VAO == 0) return;
            GL.BindVertexArray(VAO);
            GL.DrawArrays(0x0004, 0, _vertexCount); // GL_TRIANGLES
        }

        public void Dispose()
        {
            // Penting: Hapus resource di GPU saat chunk tidak lagi digunakan
            fixed (uint* pVao = &VAO) GL.DeleteVertexArrays(1, pVao);
            fixed (uint* pVbo = &VBO) GL.DeleteBuffers(1, pVbo);
        }
    }


}
