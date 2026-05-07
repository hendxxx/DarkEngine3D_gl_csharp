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
    public class TerrainChunk : IDisposable
    {
        private static int _MAP_SIZE = 256; // 256x256 agar pas dengan pembagian 16
        private static int _CHUNK_SIZE = 16;
        private static int _chunksPerSide = MAP_SIZE / CHUNK_SIZE; // 16x16 chunk

        public static int MAP_SIZE
        {
            get => _MAP_SIZE;
            private set => _MAP_SIZE = value;
        }

        public static int CHUNK_SIZE
        {
            get => _CHUNK_SIZE;
            private set => _CHUNK_SIZE = value;
        }

        public static int ChunksPerSide
        {
            get => _chunksPerSide;
            private set => _chunksPerSide = MAP_SIZE / CHUNK_SIZE;
        }
        private static TerrainChunkData[,]? worldMap = null;

        private static Plane[]? planes = null;

        private uint shaderProgram;
        private int modelLocation;

        // Line shader + uniform locations untuk menggambar kotak
        private uint lineShaderProgram;
        private int lineViewLocation;
        private int lineProjLocation;

        // Frozen frustum storage (populated when user presses P)
        private Vector3[]? frozenCorners = null;
        private Plane[]? frozenPlanes = null;

        private bool highlightFrustumMatches = false;

        // Cached debug buffers (shared, created on first use)
        private static uint debugVao = 0;
        private static uint debugVbo = 0;
        private static readonly object debugBufferLock = new object();

        public TerrainChunk(string imagePath)
        {
            Init(imagePath);
        }

        public void Init(string imagePath)
        {
            // Dispose existing world map contents (avoid GPU resource leak on re-init)
            if (worldMap != null)
            {
                for (int z = 0; z < worldMap.GetLength(1); z++)
                {
                    for (int x = 0; x < worldMap.GetLength(0); x++)
                    {
                        var c = worldMap[x, z];
                        if (c != null)
                        {
                            c.Dispose();
                            worldMap[x, z] = null!;
                        }
                    }
                }
                worldMap = null;
            }
            MapLoader mapLoader = new MapLoader(imagePath);

            MAP_SIZE = mapLoader.Width;
            CHUNK_SIZE = MAP_SIZE/16;

            uint _shaderProgram = Shader.GetShaderProgram();

            worldMap = new TerrainChunkData[ChunksPerSide, ChunksPerSide];

            int halfMapSize = (ChunksPerSide * CHUNK_SIZE) / 2;

            for (int z = 0; z < ChunksPerSide; z++)
            {
                for (int x = 0; x < ChunksPerSide; x++)
                {
                    worldMap[x, z] = new TerrainChunkData();

                    int offsetX = (x * CHUNK_SIZE) - halfMapSize;
                    int offsetZ = (z * CHUNK_SIZE) - halfMapSize;

                    worldMap[x, z].Generate(CHUNK_SIZE, offsetX, offsetZ, mapLoader);
                }
            }

            modelLocation = GL.GetUniformLocation(_shaderProgram, "model");
            shaderProgram = _shaderProgram;

            // Ambil program shader garis dan lokasi uniform view/projection-nya
            lineShaderProgram = Shader.GetLineShaderProgram();
            lineViewLocation = GL.GetUniformLocation(lineShaderProgram, "view");
            lineProjLocation = GL.GetUniformLocation(lineShaderProgram, "projection");
        }

        // Called by Keyboard when user presses P
        public void SetFrozenFrustumCorners(Vector3[] corners)
        {
            frozenCorners = corners;
            frozenPlanes = BuildPlanesFromCorners(corners);
        }

        public void ClearFrozenFrustumCorners()
        {
            frozenCorners = null;
            frozenPlanes = null;
        }

        public static int GetMapSize()
        {
            return MAP_SIZE;
        }

        public static int GetChunkSize()
        {
            return CHUNK_SIZE;
        }

        public void SetHighlightFrustumMatches(bool enabled)
        {
            highlightFrustumMatches = enabled;
        }

        public Plane[]? GetFrozenPlanes()
        {
            return frozenPlanes;
        }

        public int Render( Camera camera, float aspect, Plane[]? frozenPlanes )
        {
            int totalTriangles = 0;

            // 1. Hitung Matriks Gabungan (View * Projection)
            Matrix4x4 vp = camera.GetViewMatrix() * Camera.GetProjectionMatrix(camera.GetAspect(), camera.foV, camera.nearDist, camera.farDist);

            planes = ExtractPlanes(vp);

            bool usingFrozen = frozenPlanes != null;

            for (int z = 0; z < ChunksPerSide; z++)
            {
                for (int x = 0; x < ChunksPerSide; x++)
                {
                    // 2. Cek apakah chunk terlihat oleh kamera (tetap digunakan untuk mesh render)
                    bool inside = IsChunkInFrustum(x, z, planes);

                    if (inside)
                    {
                        Matrix4x4 model = Matrix4x4.Identity;
                        unsafe
                        {
                            GL.UniformMatrix4fv(modelLocation, 1, false, (float*)&model);
                        }

                        worldMap?[x, z]?.Draw();

                        totalTriangles += (CHUNK_SIZE * CHUNK_SIZE * 2);
                    }

                    // Determine "insideFrozen" only when frozen frustum exists
                    if (usingFrozen)
                    {
                        bool insideFrozen = IsAABBInsideFrustum(frozenPlanes, x, z);

                        // Draw bounding box:
                        // - If frozen frustum exists: blue = insideFrozen, yellow = outsideFrozen
                        // - If no frozen frustum: yellow if outside camera frustum, blue if inside
                        DrawChunkBoundingBox(x, z, usingFrozen, insideFrozen, inside, camera, aspect);
                    }

                }
            }

            // If frozen frustum exists, draw the frozen frustum wireframe
            if (frozenCorners != null)
            {
                GL.UseProgram(lineShaderProgram);
                GL.Disable(Const.GL_DEPTH_TEST);

                Matrix4x4 v = camera.GetViewMatrix();
                Matrix4x4 p = Camera.GetProjectionMatrix(camera.GetAspect(), camera.foV, camera.nearDist, camera.farDist);
                unsafe
                {
                    GL.UniformMatrix4fv(lineViewLocation, 1, true, (float*)&v);
                    GL.UniformMatrix4fv(lineProjLocation, 1, true, (float*)&p);
                }
                RenderFrustumDebug(frozenCorners, lineShaderProgram, lineViewLocation, lineProjLocation, camera);
                GL.Enable(Const.GL_DEPTH_TEST);

            }

            return totalTriangles;
        }

        // Build planes from 8 frustum corners (order: 0..3 near, 4..7 far)
        private static Plane[]? BuildPlanesFromCorners(Vector3[] c)
        {
            if (c == null || c.Length < 8) return null;

            var planes = new Plane[6];

            // Create helper to make a plane from three points
            static Plane MakePlane(Vector3 a, Vector3 b, Vector3 d)
            {
                var n = Vector3.Normalize(Vector3.Cross(b - a, d - a));
                float D = -Vector3.Dot(n, a);
                return Plane.Normalize(new Plane(n, D));
            }

            // Use triangles that define each face (orientation doesn't matter for our inside-test)
            planes[0] = MakePlane(c[1], c[2], c[6]); // Right
            planes[1] = MakePlane(c[3], c[0], c[4]); // Left
            planes[2] = MakePlane(c[0], c[1], c[5]); // Bottom
            planes[3] = MakePlane(c[2], c[3], c[7]); // Top
            planes[4] = MakePlane(c[0], c[3], c[2]); // Near
            planes[5] = MakePlane(c[5], c[6], c[7]); // Far

            return planes;
        }

        // Test AABB (chunk) against frustum planes.
        // Return true if AABB is at least partially inside (i.e. NOT completely outside any plane).
        private static bool IsAABBInsideFrustum(Plane[]? frustumPlanes, int chunkIndexX, int chunkIndexZ)
        {
            if (frustumPlanes == null) return true;

            int halfMapSize = (ChunksPerSide * CHUNK_SIZE) / 2;
            float minX = (chunkIndexX * CHUNK_SIZE) - halfMapSize;
            float maxX = minX + CHUNK_SIZE;
            float minZ = (chunkIndexZ * CHUNK_SIZE) - halfMapSize;
            float maxZ = minZ + CHUNK_SIZE;
            float minY = -0.0f;
            float maxY = 1.0f;

            // eight corners of chunk AABB
            Vector3[] corners = new Vector3[]
            {
                new(minX, minY, minZ),
                new(maxX, minY, minZ),
                new(maxX, maxY, minZ),
                new(minX, maxY, minZ),
                new(minX, minY, maxZ),
                new(maxX, minY, maxZ),
                new(maxX, maxY, maxZ),
                new(minX, maxY, maxZ)
            };

            foreach (var pl in frustumPlanes)
            {
                // compute maximum signed distance of AABB corners to plane
                float maxDist = float.NegativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    float d = Vector3.Dot(pl.Normal, corners[i]) + pl.D;
                    if (d > maxDist) maxDist = d;
                }

                // if the maximum distance is < 0 => all corners are on 'negative' side => completely outside
                if (maxDist < 0.0f) return false;
            }

            return true;
        }

        private static bool IsChunkInFrustum(int chunkIndexX, int chunkIndexZ, Plane[] planes)
        {
            int halfMapSize = (ChunksPerSide * CHUNK_SIZE) / 2;

            float minX = (chunkIndexX * CHUNK_SIZE) - halfMapSize;
            float maxX = minX + CHUNK_SIZE;
            float minZ = (chunkIndexZ * CHUNK_SIZE) - halfMapSize;
            float maxZ = minZ + CHUNK_SIZE;
            float minY = -0.0f;
            float maxY = 1.0f;

            foreach (var p in planes)
            {
                float px = (p.Normal.X >= 0) ? maxX : minX;
                float py = (p.Normal.Y >= 0) ? maxY : minY;
                float pz = (p.Normal.Z >= 0) ? maxZ : minZ;

                if (Vector3.Dot(p.Normal, new Vector3(px, py, pz)) + p.D < -100.0f)
                {
                    return false;
                }
            }
            return true;
        }

        public static unsafe void DrawLine(Vector3 start, Vector3 end, uint shader, int vLoc, int pLoc, Camera camera, float aspect)
        {
            // Ensure shader program is active before uploading uniforms
            GL.UseProgram(shader);

            float[] lineData = new float[] { start.X, start.Y, start.Z, end.X, end.Y, end.Z };
            uint vao, vbo;

            lock (debugBufferLock)
            {
                if (debugVao == 0 || debugVbo == 0)
                {
                    fixed (uint* pVao = &debugVao) GL.GenVertexArrays(1, pVao);
                    fixed (uint* pVbo = &debugVbo) GL.GenBuffers(1, pVbo);
                }
                vao = debugVao;
                vbo = debugVbo;
            }

            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

            fixed (void* p = lineData)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(sizeof(float) * lineData.Length), p, Const.GL_DYNAMIC_DRAW);
            }

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 0, (void*)0);

            Matrix4x4 view = camera.GetViewMatrix();
            Matrix4x4 proj = Camera.GetProjectionMatrix(camera.GetAspect(), camera.foV, camera.nearDist, camera.farDist);
            unsafe
            {
                // Use transpose = true for System.Numerics memory layout with these GL bindings
                GL.UniformMatrix4fv(vLoc, 1, true, (float*)&view);
                GL.UniformMatrix4fv(pLoc, 1, true, (float*)&proj);
            }

            GL.DrawArrays(Const.GL_LINES, 0, 2);

            GL.BindVertexArray(0);
        }

        public unsafe void RenderFrustumDebug(Vector3[] c, uint shader, int vLoc, int pLoc, Camera camera)
        {
            float r = 1.0f; float g = 0.0f; float b = 0.0f;
            // Normal dummy (menghadap atas) agar lighting shader tidak menghasilkan warna hitam
            float nx = 0.0f; float ny = 1.0f; float nz = 0.0f;

            // Gunakan format 9 float per titik: Pos(3), Normal(3), Color(3)
            float[] lineData = new float[]
            {
                // Near Plane
                c[0].X, c[0].Y, c[0].Z, nx, ny, nz, r, g, b,  c[1].X, c[1].Y, c[1].Z, nx, ny, nz, r, g, b,
                c[1].X, c[1].Y, c[1].Z, nx, ny, nz, r, g, b,  c[2].X, c[2].Y, c[2].Z, nx, ny, nz, r, g, b,
                c[2].X, c[2].Y, c[2].Z, nx, ny, nz, r, g, b,  c[3].X, c[3].Y, c[3].Z, nx, ny, nz, r, g, b,
                c[3].X, c[3].Y, c[3].Z, nx, ny, nz, r, g, b,  c[0].X, c[0].Y, c[0].Z, nx, ny, nz, r, g, b,
                // Far Plane
                c[4].X, c[4].Y, c[4].Z, nx, ny, nz, r, g, b,  c[5].X, c[5].Y, c[5].Z, nx, ny, nz, r, g, b,
                c[5].X, c[5].Y, c[5].Z, nx, ny, nz, r, g, b,  c[6].X, c[6].Y, c[6].Z, nx, ny, nz, r, g, b,
                c[6].X, c[6].Y, c[6].Z, nx, ny, nz, r, g, b,  c[7].X, c[7].Y, c[7].Z, nx, ny, nz, r, g, b,
                c[7].X, c[7].Y, c[7].Z, nx, ny, nz, r, g, b,  c[4].X, c[4].Y, c[4].Z, nx, ny, nz, r, g, b,
                // Bridge
                c[0].X, c[0].Y, c[0].Z, nx, ny, nz, r, g, b,  c[4].X, c[4].Y, c[4].Z, nx, ny, nz, r, g, b,
                c[1].X, c[1].Y, c[1].Z, nx, ny, nz, r, g, b,  c[5].X, c[5].Y, c[5].Z, nx, ny, nz, r, g, b,
                c[2].X, c[2].Y, c[2].Z, nx, ny, nz, r, g, b,  c[6].X, c[6].Y, c[6].Z, nx, ny, nz, r, g, b,
                c[3].X, c[3].Y, c[3].Z, nx, ny, nz, r, g, b,  c[7].X, c[7].Y, c[7].Z, nx, ny, nz, r, g, b
            };

            uint vao, vbo;
            lock (debugBufferLock)
            {
                if (debugVao == 0 || debugVbo == 0)
                {
                    fixed (uint* pVao = &debugVao) GL.GenVertexArrays(1, pVao);
                    fixed (uint* pVbo = &debugVbo) GL.GenBuffers(1, pVbo);
                }
                vao = debugVao;
                vbo = debugVbo;
            }

            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

            fixed (void* ptr = lineData)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(lineData.Length * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);
            }

            int stride = 9 * sizeof(float); // 36 bytes

            // Atribut 0: Posisi
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);

            // Atribut 1: Normal (Sangat penting agar shader tidak hitam)
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));

            // Atribut 2: Warna
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, Const.GL_FLOAT, false, stride, (void*)(6 * sizeof(float)));

            GL.UseProgram(shader);

            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 p = Camera.GetProjectionMatrix(camera.GetAspect(), camera.foV, camera.nearDist, camera.farDist);

            // Pastikan transpose = true untuk System.Numerics
            GL.UniformMatrix4fv(vLoc, 1, true, (float*)&v);
            GL.UniformMatrix4fv(pLoc, 1, true, (float*)&p);

            GL.DrawArrays(Const.GL_LINES, 0, 24);

            GL.BindVertexArray(0);
        }


        // Menambahkan method untuk menggambar kotak wireframe per chunk
        private unsafe void DrawChunkBoundingBox(int chunkIndexX, int chunkIndexZ, bool usingFrozen, bool insideFrozen, bool insideCamera, Camera camera, float aspect)
        {
            int halfMapSize = (ChunksPerSide * CHUNK_SIZE) / 2;

            float minX = (chunkIndexX * CHUNK_SIZE) - halfMapSize;
            float maxX = minX + CHUNK_SIZE;
            float minZ = (chunkIndexZ * CHUNK_SIZE) - halfMapSize;
            float maxZ = minZ + CHUNK_SIZE;

            // Gunakan MinY/MaxY asli dari TerrainData agar kotak pas membungkus bukit
            float minY = -5.0f;
            float maxY = 5.0f ;

            // try to use actual chunk values if available
            if (worldMap != null)
            {
                var chunk = worldMap[chunkIndexX, chunkIndexZ];
                if (chunk != null)
                {
                    minY = MathF.Min(minY, chunk.MinY);
                    maxY = MathF.Max(maxY, chunk.MaxY);
                }
            }

            // 8 Titik Sudut
            Vector3[] c = new Vector3[]
            {
                new(minX, minY, minZ), new(maxX, minY, minZ), new(maxX, maxY, minZ), new(minX, maxY, minZ),
                new(minX, minY, maxZ), new(maxX, minY, maxZ),  new(maxX, maxY, maxZ), new(minX, maxY, maxZ)
            };

            float r = 1.0f; float g = 0.0f; float b = 0.0f;
            // --- LOGIKA WARNA VIA UNIFORM ---
            Vector3 finalColor;
            if (usingFrozen)
                finalColor = insideFrozen ? new Vector3(0, 0, 1) : new Vector3(1, 1, 0); // Blue / Yellow
            else
                finalColor = insideCamera ? new Vector3(0, 0, 1) : new Vector3(1, 1, 0); // Blue / Yellow

            r= finalColor.X; g = finalColor.Y; b = finalColor.Z;


            // Normal dummy (menghadap atas) agar lighting shader tidak menghasilkan warna hitam
            float nx = 0.0f; float ny = 1.0f; float nz = 0.0f;

            // Gunakan format 9 float per titik: Pos(3), Normal(3), Color(3)
            float[] lineData = new float[]
            {
                // Near Plane
                c[0].X, c[0].Y, c[0].Z, nx, ny, nz, r, g, b,  c[1].X, c[1].Y, c[1].Z, nx, ny, nz, r, g, b,
                c[1].X, c[1].Y, c[1].Z, nx, ny, nz, r, g, b,  c[2].X, c[2].Y, c[2].Z, nx, ny, nz, r, g, b,
                c[2].X, c[2].Y, c[2].Z, nx, ny, nz, r, g, b,  c[3].X, c[3].Y, c[3].Z, nx, ny, nz, r, g, b,
                c[3].X, c[3].Y, c[3].Z, nx, ny, nz, r, g, b,  c[0].X, c[0].Y, c[0].Z, nx, ny, nz, r, g, b,
                // Far Plane
                c[4].X, c[4].Y, c[4].Z, nx, ny, nz, r, g, b,  c[5].X, c[5].Y, c[5].Z, nx, ny, nz, r, g, b,
                c[5].X, c[5].Y, c[5].Z, nx, ny, nz, r, g, b,  c[6].X, c[6].Y, c[6].Z, nx, ny, nz, r, g, b,
                c[6].X, c[6].Y, c[6].Z, nx, ny, nz, r, g, b,  c[7].X, c[7].Y, c[7].Z, nx, ny, nz, r, g, b,
                c[7].X, c[7].Y, c[7].Z, nx, ny, nz, r, g, b,  c[4].X, c[4].Y, c[4].Z, nx, ny, nz, r, g, b,
                // Bridge
                c[0].X, c[0].Y, c[0].Z, nx, ny, nz, r, g, b,  c[4].X, c[4].Y, c[4].Z, nx, ny, nz, r, g, b,
                c[1].X, c[1].Y, c[1].Z, nx, ny, nz, r, g, b,  c[5].X, c[5].Y, c[5].Z, nx, ny, nz, r, g, b,
                c[2].X, c[2].Y, c[2].Z, nx, ny, nz, r, g, b,  c[6].X, c[6].Y, c[6].Z, nx, ny, nz, r, g, b,
                c[3].X, c[3].Y, c[3].Z, nx, ny, nz, r, g, b,  c[7].X, c[7].Y, c[7].Z, nx, ny, nz, r, g, b
            };

            lock (debugBufferLock)
            {
                if (debugVao == 0 || debugVbo == 0)
                {
                    fixed (uint* pVao = &debugVao) GL.GenVertexArrays(1, pVao);
                    fixed (uint* pVbo = &debugVbo) GL.GenBuffers(1, pVbo);
                }
            }

            GL.BindVertexArray(debugVao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, debugVbo);

            fixed (void* ptr = lineData)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(lineData.Length * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);
            }

            int stride = 9 * sizeof(float); // 36 bytes

            // Atribut 0: Posisi
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);

            // Atribut 1: Normal (Sangat penting agar shader tidak hitam)
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));

            // Atribut 2: Warna
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, Const.GL_FLOAT, false, stride, (void*)(6 * sizeof(float)));

            GL.UseProgram(lineShaderProgram);

            int lineColorLocation = GL.GetUniformLocation(lineShaderProgram, "lineColor");

            // Pastikan Anda memuat glUniform3f ke interop GL 
            GL.Uniform3f(lineColorLocation, finalColor.X, finalColor.Y, finalColor.Z);

            // Kirim Matriks
            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 p = Camera.GetProjectionMatrix(camera.GetAspect(), camera.foV, camera.nearDist, camera.farDist);
            unsafe
            {
                GL.UniformMatrix4fv(lineViewLocation, 1, true, (float*)&v);
                GL.UniformMatrix4fv(lineProjLocation, 1, true, (float*)&p);
            }

            GL.DrawArrays(Const.GL_LINES, 0, 24);

            GL.BindVertexArray(0);
        }


        private static Plane[] ExtractPlanes(Matrix4x4 vp)
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

        public static Vector3[] GetFrustumCorners(Matrix4x4 view, Matrix4x4 proj)
        {
            // Kalikan View lalu Projection (Urutan Row-Major .NET)
            Matrix4x4 viewProj = view * proj;
            if (!Matrix4x4.Invert(viewProj, out Matrix4x4 invVP)) return new Vector3[8];

            Vector3[] corners = new Vector3[8];
            // 8 titik sudut NDC (-1 sampai 1)
            Vector3[] value = new Vector3[]
            {
                new(-1, -1, -1),
                new(1, -1, -1),
                new(1, 1, -1),
                new(-1, 1, -1), // Near
                new(-1, -1, 1),
                new(1, -1, 1),
                new(1, 1, 1),
                new(-1, 1, 1)  // Far
            };
            Vector3[] ndc = value;

            for (int i = 0; i < 8; i++)
            {
                Vector4 worldPos = Vector4.Transform(ndc[i], invVP);
                corners[i] = new Vector3(worldPos.X / worldPos.W, worldPos.Y / worldPos.W, worldPos.Z / worldPos.W);
            }
            return corners;
        }

        public void Dispose()
        {
            // Dispose all chunk data and free debug GL buffers
            if (worldMap != null)
            {
                for (int z = 0; z < worldMap.GetLength(1); z++)
                {
                    for (int x = 0; x < worldMap.GetLength(0); x++)
                    {
                        var c = worldMap[x, z];
                        if (c != null)
                        {
                            c.Dispose();
                            worldMap[x, z] = null!;
                        }
                    }
                }
                worldMap = null;
            }

            lock (debugBufferLock)
            {
                if (debugVao != 0)
                {
                    unsafe
                    {
                        fixed (uint* p = &debugVao) GL.DeleteVertexArrays(1, p);
                    }
                    debugVao = 0;
                }
                if (debugVbo != 0)
                {
                    unsafe
                    {
                        fixed (uint* p = &debugVbo) GL.DeleteBuffers(1, p);
                    }
                    debugVbo = 0;
                }
            }

            GC.SuppressFinalize(this);
        }
    }
}
