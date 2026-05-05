using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static DarkEngine3D_gl_csharp.Engine.Helpers;

namespace DarkEngine3D_gl_csharp.Engine
{
    public class Terrain
    {
        public const int MAP_SIZE = 256; // 256x256 agar pas dengan pembagian 16
        public const int CHUNK_SIZE = 16;
        private static int chunksPerSide = MAP_SIZE / CHUNK_SIZE; // 16x16 chunk

        private TerrainChunk[,] worldMap;

        private static Plane[] planes;

        private uint shaderProgram;
        private int modelLocation;

        public void Init(uint _shaderProgram)
        {
            worldMap = new TerrainChunk[chunksPerSide, chunksPerSide];
            
            int halfMapSize = (chunksPerSide * CHUNK_SIZE) / 2;

            for (int z = 0; z < chunksPerSide; z++)
            {
                for (int x = 0; x < chunksPerSide; x++)
                {
                    worldMap[x, z] = new TerrainChunk();

                    // Kirim offset agar (0,0) ada di tengah
                    // Contoh: Jika map 256, maka offset x dan z dimulai dari -128
                    int offsetX = (x * CHUNK_SIZE) - halfMapSize;
                    int offsetZ = (z * CHUNK_SIZE) - halfMapSize;

                    // Kita ubah parameter Generate agar menerima koordinat dunia langsung
                    worldMap[x, z].Generate(CHUNK_SIZE, offsetX, offsetZ);
                }
            }

            modelLocation = GL.GetUniformLocation(_shaderProgram, "model");
            shaderProgram = _shaderProgram;
        }

        public int GetMapSize()
        {
            return MAP_SIZE;
        }

        public int GetChunkSize()
        {
            return CHUNK_SIZE;
        }

        public int Render( Camera camera, float aspect)
        {
            int totalTriangles = 0;
 
            // 1. Hitung Matriks Gabungan (View * Projection)
            Matrix4x4 vp = camera.GetViewMatrix() * camera.GetProjectionMatrix(aspect);

            planes = ExtractPlanes(vp);

            for (int z = 0; z < chunksPerSide; z++)
            {
                for (int x = 0; x < chunksPerSide; x++)
                {
                    // 2. Cek apakah chunk terlihat oleh kamera
                    if (IsChunkInFrustum(x, z, planes))
                    {
                        Matrix4x4 model = Matrix4x4.Identity;
                        unsafe
                        {
                            GL.UniformMatrix4fv(modelLocation, 1, false, (float*)&model);
                        }

                        worldMap[x, z].Draw();
                        totalTriangles += (CHUNK_SIZE * CHUNK_SIZE * 2);
                    }
                }
            }
            return totalTriangles;
        }

        private static bool IsChunkInFrustum(int chunkIndexX, int chunkIndexZ, Plane[] planes)
        {
            int halfMapSize = (chunksPerSide * CHUNK_SIZE) / 2;

            // Hitung posisi kotak berdasarkan koordinat baru
            float minX = (chunkIndexX * CHUNK_SIZE) - halfMapSize;
            float maxX = minX + CHUNK_SIZE;
            float minZ = (chunkIndexZ * CHUNK_SIZE) - halfMapSize;
            float maxZ = minZ + CHUNK_SIZE;
            float minY = -0.0f;
            float maxY = 50.0f;

            foreach (var p in planes)
            {
                // Cari titik kotak yang paling searah dengan normal bidang (p-vertex)
                float px = (p.Normal.X >= 0) ? maxX : minX;
                float py = (p.Normal.Y >= 0) ? maxY : minY;
                float pz = (p.Normal.Z >= 0) ? maxZ : minZ;

                // Jika titik yang paling 'dalam' saja masih di luar bidang, maka chunk pasti di luar
                if (Vector3.Dot(p.Normal, new Vector3(px, py, pz)) + p.D < -100.0f)
                {
                    return false;
                }
            }
            return true;
        }
        public Vector3[] GetFrustumCorners(Matrix4x4 view, Matrix4x4 proj)
        {
            // Kalikan View lalu Projection (Urutan Row-Major .NET)
            Matrix4x4 viewProj = view * proj;
            if (!Matrix4x4.Invert(viewProj, out Matrix4x4 invVP)) return new Vector3[8];

            Vector3[] corners = new Vector3[8];
            // 8 titik sudut NDC (-1 sampai 1)
            Vector3[] ndc = {
                new(-1, -1, -1), new( 1, -1, -1), new( 1,  1, -1), new(-1,  1, -1), // Near
                new(-1, -1,  1), new( 1, -1,  1), new( 1,  1,  1), new(-1,  1,  1)  // Far
            };

            for (int i = 0; i < 8; i++)
            {
                Vector4 worldPos = Vector4.Transform(ndc[i], invVP);
                corners[i] = new Vector3(worldPos.X / worldPos.W, worldPos.Y / worldPos.W, worldPos.Z / worldPos.W);
            }
            return corners;
        }


        private Plane[] ExtractPlanes(Matrix4x4 vp)
        {
            var planes = new Plane[6];

            // Left
            planes[0] = Plane.Normalize(new Plane(vp.M14 + vp.M11, vp.M24 + vp.M21, vp.M34 + vp.M31, vp.M44 + vp.M41));
            // Right
            planes[1] = Plane.Normalize(new Plane(vp.M14 - vp.M11, vp.M24 - vp.M21, vp.M34 - vp.M31, vp.M44 - vp.M41));
            // Bottom
            planes[2] = Plane.Normalize(new Plane(vp.M14 + vp.M12, vp.M24 + vp.M22, vp.M34 + vp.M32, vp.M44 + vp.M42));
            // Top
            planes[3] = Plane.Normalize(new Plane(vp.M14 - vp.M12, vp.M24 - vp.M22, vp.M34 - vp.M32, vp.M44 - vp.M42));
            // Near
            planes[4] = Plane.Normalize(new Plane(vp.M13, vp.M23, vp.M33, vp.M43));
            // Far
            planes[5] = Plane.Normalize(new Plane(vp.M14 - vp.M13, vp.M24 - vp.M23, vp.M34 - vp.M33, vp.M44 - vp.M43));

            return planes;
        }

        public static unsafe void DrawLine(Vector3 start, Vector3 end, uint shader, int vLoc, int pLoc, Camera camera, float aspect)
        {
            float[] lineData = { start.X, start.Y, start.Z, end.X, end.Y, end.Z };
            uint vao, vbo;

            GL.GenVertexArrays(1, &vao);
            GL.GenBuffers(1, &vbo);
            GL.BindVertexArray(vao);
            GL.BindBuffer(0x8892, vbo); // GL_ARRAY_BUFFER

            fixed (void* p = lineData)
            {
                GL.BufferData(0x8892, (nuint)(sizeof(float) * 6), p, 0x88E4); // GL_STATIC_DRAW
            }

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, 0x1406, false, 0, (void*)0); // 0x1406 = GL_FLOAT

            // Update Kamera khusus untuk shader garis
            Matrix4x4 view = camera.GetViewMatrix();
            Matrix4x4 proj = camera.GetProjectionMatrix(aspect);
            GL.UniformMatrix4fv(vLoc, 1, false, (float*)&view);
            GL.UniformMatrix4fv(pLoc, 1, false, (float*)&proj);

            GL.DrawArrays(0x0001, 0, 2); // 0x0001 = GL_LINES

            // Cleanup agar tidak memory leak
            GL.DeleteVertexArrays(1, &vao);
            GL.DeleteBuffers(1, &vbo);
        }


        public unsafe void RenderFrustumDebug(Vector3[] c, uint shader, int vLoc, int pLoc, Camera camera, float aspect)
        {
            // R, G, B untuk warna merah murni
            float r = 1.0f; float g = 0.0f; float b = 0.0f;

            // Susun 24 vertex (12 pasang garis). Format: X, Y, Z, R, G, B
            float[] lineVerticesWithColor = {
                // Near Plane (Kotak Depan)
                c[0].X, c[0].Y, c[0].Z, r, g, b,  c[1].X, c[1].Y, c[1].Z, r, g, b,
                c[1].X, c[1].Y, c[1].Z, r, g, b,  c[2].X, c[2].Y, c[2].Z, r, g, b,
                c[2].X, c[2].Y, c[2].Z, r, g, b,  c[3].X, c[3].Y, c[3].Z, r, g, b,
                c[3].X, c[3].Y, c[3].Z, r, g, b,  c[0].X, c[0].Y, c[0].Z, r, g, b,

                // Far Plane (Kotak Belakang)
                c[4].X, c[4].Y, c[4].Z, r, g, b,  c[5].X, c[5].Y, c[5].Z, r, g, b,
                c[5].X, c[5].Y, c[5].Z, r, g, b,  c[6].X, c[6].Y, c[6].Z, r, g, b,
                c[6].X, c[6].Y, c[6].Z, r, g, b,  c[7].X, c[7].Y, c[7].Z, r, g, b,
                c[7].X, c[7].Y, c[7].Z, r, g, b,  c[4].X, c[4].Y, c[4].Z, r, g, b,

                // Garis Penghubung (Near ke Far)
                c[0].X, c[0].Y, c[0].Z, r, g, b,  c[4].X, c[4].Y, c[4].Z, r, g, b,
                c[1].X, c[1].Y, c[1].Z, r, g, b,  c[5].X, c[5].Y, c[5].Z, r, g, b,
                c[2].X, c[2].Y, c[2].Z, r, g, b,  c[6].X, c[6].Y, c[6].Z, r, g, b,
                c[3].X, c[3].Y, c[3].Z, r, g, b,  c[7].X, c[7].Y, c[7].Z, r, g, b
            };

            uint vao, vbo;
            GL.GenVertexArrays(1, &vao);
            GL.GenBuffers(1, &vbo);

            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

            fixed (void* ptr = lineVerticesWithColor)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(lineVerticesWithColor.Length * sizeof(float)), ptr, Const.GL_STATIC_DRAW);
            }

            // Ukuran satu vertex utuh sekarang adalah 6 float (3 posisi + 3 warna) = 24 bytes
            int stride = 6 * sizeof(float);

            // Atribut 0: Posisi (X, Y, Z)
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);

            // Atribut 1: Warna (R, G, B) -> Menyuntikkan warna merah agar shader terrain tidak membaca warna hitam
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));

            // Gunakan shader utama terrain Anda agar pasti tervisualisasi
            GL.UseProgram(shader);

            // Kirim matriks kamera (Gunakan transpose = true untuk format .NET Matrix)
            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 p = camera.GetProjectionMatrix(aspect);
            GL.UniformMatrix4fv(vLoc, 1, true, (float*)&v);
            GL.UniformMatrix4fv(pLoc, 1, true, (float*)&p);

            // Gambar 24 titik vertex sebagai barisan garis murni
            GL.DrawArrays(Const.GL_LINES, 0, 24);

            // Bersihkan kembali objek penampung di GPU agar tidak terjadi memory leak
            GL.DeleteVertexArrays(1, &vao);
            GL.DeleteBuffers(1, &vbo);
        }


    }
}
