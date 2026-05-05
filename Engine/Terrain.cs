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
                    worldMap[x, z].Generate(offsetX, offsetZ);
                }
            }

            modelLocation = GL.GetUniformLocation(_shaderProgram, "model");
            shaderProgram = _shaderProgram;
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
            float minY = -500.0f;
            float maxY = 500.0f;

            foreach (var p in planes)
            {
                // Cari titik kotak yang paling searah dengan normal bidang (p-vertex)
                float px = (p.Normal.X >= 0) ? maxX : minX;
                float py = (p.Normal.Y >= 0) ? maxY : minY;
                float pz = (p.Normal.Z >= 0) ? maxZ : minZ;

                // Jika titik yang paling 'dalam' saja masih di luar bidang, maka chunk pasti di luar
                if (Vector3.Dot(p.Normal, new Vector3(px, py, pz)) + p.D < -20.0f)
                {
                    return false;
                }
            }
            return true;
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
    }
}
