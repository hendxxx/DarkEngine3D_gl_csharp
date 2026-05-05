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
    public unsafe class TerrainInifite
    {
        public const int SIZE = 16;
        public uint VAO, VBO;
        private int _vertexCount;

        private uint shaderProgram;
        private int modelLocation; 

        public TerrainInifite(  )
        {

        }
        public void Generate(uint _shaderProgram, int chunkX, int chunkZ)
        {
            List<Vertex> vertices = new List<Vertex>();

            for (int z = 0; z < SIZE; z++)
            {
                for (int x = 0; x < SIZE; x++)
                {
                    float xPos = x + (chunkX * SIZE);
                    float zPos = z + (chunkZ * SIZE);

                    // Membuat satu petak (Quad) yang terdiri dari 2 segitiga (6 vertex)
                    AddQuad(vertices, xPos, zPos);
                }
            }
            modelLocation = GL.GetUniformLocation(_shaderProgram, "model");
            shaderProgram = _shaderProgram;
            _vertexCount = vertices.Count;
            SetupGPUResources(vertices.ToArray());
        } 

        private void AddQuad(List<Vertex> vertices, float x, float z)
        {
            // Ambil ketinggian untuk tiap sudut petak
            //float h00 = Noise.GetHeight(x, z);
            //float h10 = Noise.GetHeight(x + 1, z);
            //float h01 = Noise.GetHeight(x, z + 1);
            //float h11 = Noise.GetHeight(x + 1, z + 1);
            float h00 = 0; // Contoh datar
            float h10 = 0; // Contoh datar
            float h01 = 0; // Contoh datar
            float h11 = 0; // Contoh datar

            // Warna berdasarkan ketinggian (Contoh sederhana: semakin tinggi semakin putih/salju)
            Vector3 color = new Vector3(0.2f, 0.5f, 0.2f); // Hijau Rumput

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

        public void Draw()
        {
            Matrix4x4 terrainModel = Matrix4x4.Identity;
            unsafe
            {
                // TIMPA matriks model terakhir dengan matriks identity agar terrain kembali ke (0,0,0)
                GL.UniformMatrix4fv(modelLocation, 1, false, (float*)&terrainModel);
            }
            GL.BindVertexArray(VAO);
            GL.DrawArrays(0x0004, 0, _vertexCount); // GL_TRIANGLES
            GL.BindVertexArray(0); // <--- PENTING: Lepaskan VAO terrain
        }
    }

    public class TerrainManager
    {

        private int modelLocation;

        private Dictionary<(int x, int z), TerrainInifite> _chunks = new();
        private int _renderDistance = 4; // Jumlah chunk ke segala arah (9x9 chunk)

        public void Update(uint shaderProgram,  Vector3 playerPos)
        {
            modelLocation = GL.GetUniformLocation(shaderProgram, "model");

            // 1. Hitung koordinat chunk tempat pemain berdiri
            int currentChunkX = (int)MathF.Floor(playerPos.X / TerrainInifite.SIZE);
            int currentChunkZ = (int)MathF.Floor(playerPos.Z / TerrainInifite.SIZE);

            // 2. Load chunk baru yang masuk dalam radius pandang
            for (int z = -_renderDistance; z <= _renderDistance; z++)
            {
                for (int x = -_renderDistance; x <= _renderDistance; x++)
                {
                    var coord = (currentChunkX + x, currentChunkZ + z);
                    if (!_chunks.ContainsKey(coord))
                    {
                        var newChunk = new TerrainInifite();
                        newChunk.Generate(shaderProgram, coord.Item1, coord.Item2);
                        _chunks.Add(coord, newChunk);
                    }
                }
            }


            Render(modelLocation);
            // 3. (Opsional) Unload chunk yang terlalu jauh untuk menghemat VRAM
            // Anda bisa melakukan loop pada _chunks dan menghapus yang jaraknya > _renderDistance + 1
        }

        public void Render(int modelLocation)
        {
            foreach (var entry in _chunks)
            {
                // PENTING: Pindahkan posisi render tiap chunk ke koordinat dunianya
                Matrix4x4 model = Matrix4x4.CreateTranslation(0, 0, 0);
                // Karena Generate() kita sudah menghitung posisi world di vertex, 
                // model matrix cukup Identity. 
                // Tapi jika Generate() pakai koordinat lokal (0-16), gunakan:
                // Matrix4x4.CreateTranslation(entry.Key.x * TerrainChunk.SIZE, 0, entry.Key.z * TerrainChunk.SIZE);

                unsafe
                {
                    float[] modelArray = new float[16]
                    {
                        model.M11, model.M12, model.M13, model.M14,
                        model.M21, model.M22, model.M23, model.M24,
                        model.M31, model.M32, model.M33, model.M34,
                        model.M41, model.M42, model.M43, model.M44
                    };
                    fixed (float* pModel = modelArray)
                    {
                        GL.UniformMatrix4fv(modelLocation, 1, false, pModel);
                    }
                }   
                entry.Value.Draw();
            }
        }
    }


}
