using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;
using System.Text;
namespace DarkEngine3D_gl_csharp.Engine.Terrains
{
    public class TerrainChunk : IDisposable
    {

        // Global LOD level (1..3). Higher = more subdivisions. 1 = 128x128, 2 = 256x256, 3 = 512x512 (per chunk).
        // LOD mapping implemented in GetSubdivisionsForLOD
        public static int GlobalLODLevel { get; set; } = 1;
        public static int MapSize { get; set; } = 256;
        public static int ChunkSize { get; set; } = (int)Math.Sqrt(MapSize);
        public static int ChunksPerSide { get; set; } = MapSize / ChunkSize;
        public static float TerrainScale { get; set; } = 1.0f;  // Tambahkan ini
        public static float HeightScale { get; set; } = 80.0f;  // Tambahkan ini

        // Cached value — updated whenever ChunksPerSide or ChunkSize changes (in Init)
        private static int _halfMapSize = (MapSize / ChunkSize * ChunkSize) / 2;

        private static TerrainData[,]? worldMap = null;

        private static readonly Plane[] planes = new Plane[6];
        private static int cachedTotalMapTriangles = 0;

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
        private static readonly Lock debugBufferLock = new();

        // Event untuk melacak progress loading
        public static event Action<float>? OnLoadProgress;

        private MapLoader mapLoader = null!;
        private readonly Texture[] terrainTextures = [];

        public TerrainChunk(string imagePath, Texture[] _terrainTextures)
        {
            terrainTextures = _terrainTextures;
            Init(imagePath);
        }

        private static int GetSubdivisionsForLOD(int lod)
        {
            return lod switch
            {
                4 => 6, // super High detail (6x6 grid)
                3 => 4, // High detail (4x4 grid)
                2 => 2, // Medium detail (2x2 grid)
                1 => 1, // Low detail (1x1 grid)
                _ => 1
            };
        }

        public void Init(string imagePath)
        {
            // Dispose existing world map contents
            if (worldMap != null)
            {
                for (int x = 0; x < worldMap.GetLength(0); x++)
                {
                    for (int z = 0; z < worldMap.GetLength(1); z++)
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

            // Emit 0% progress sebelum mulai load
            OnLoadProgress?.Invoke(0f);
            // Load terrain dengan scale & height 
            mapLoader = new MapLoader(imagePath);

            MapLoader.TerrainScale = TerrainScale;
            MapLoader.HeightScale = HeightScale;


            // Emit 25% progress
            OnLoadProgress?.Invoke(0.25f);

            // Update static MapSize dari image
            MapSize = mapLoader.Width;

            ChunksPerSide = MapSize / ChunkSize;
            _halfMapSize = (ChunksPerSide * ChunkSize) / 2;

            uint _shaderProgram = Shader.GetShaderProgram();
            worldMap = new TerrainData[ChunksPerSide, ChunksPerSide];

            int halfMapSize = (ChunksPerSide * ChunkSize) / 2;
            int subdivisions = GetSubdivisionsForLOD(GlobalLODLevel);

            // Emit 30% progress
            OnLoadProgress?.Invoke(0.30f);

            int totalChunks = ChunksPerSide * ChunksPerSide;
            int processedChunks = 0;

            for (int x = 0; x < ChunksPerSide; x++)
            {
                for (int z = 0; z < ChunksPerSide; z++)
                {
                    worldMap[x, z] = new TerrainData();

                    int offsetX = (x * ChunkSize) - halfMapSize;
                    int offsetZ = (z * ChunkSize) - halfMapSize;

                    worldMap[x, z].Generate(ChunkSize, offsetX, offsetZ, mapLoader, terrainTextures);

                    processedChunks++;

                    // Progress dari 30% hingga 95%
                    float progress = 0.30f + (processedChunks / (float)totalChunks) * 0.65f;
                    OnLoadProgress?.Invoke(progress);
                }
            }

            modelLocation = GL.GetUniformLocation(_shaderProgram, "model");
            shaderProgram = _shaderProgram;

            lineShaderProgram = Shader.GetLineShaderProgram();
            lineViewLocation = GL.GetUniformLocation(lineShaderProgram, "view");
            lineProjLocation = GL.GetUniformLocation(lineShaderProgram, "projection");

            // Hitung total map triangles sekali untuk di-cache
            cachedTotalMapTriangles = 0;
            for (int x = 0; x < ChunksPerSide; x++)
            {
                for (int z = 0; z < ChunksPerSide; z++)
                {
                    if (worldMap[x, z] != null) cachedTotalMapTriangles += worldMap[x, z].TriangleCount;
                }
            }

            // Print metrics
            //PrintTerrainMetrics();

            // Emit 100% progress
            OnLoadProgress?.Invoke(1.0f);
        }

        /// <summary>
        /// Cetak statistik terrain ke console dan file untuk measurement
        /// </summary>
        private static void PrintTerrainMetrics()
        {
            // Hitung total vertices dan triangles
            int totalVertices = 0;
            int totalTriangles = 0;
            float minHeightGlobal = float.MaxValue;
            float maxHeightGlobal = float.MinValue;

            if (worldMap != null)
            {
                for (int x = 0; x < ChunksPerSide; x++)
                {
                    for (int z = 0; z < ChunksPerSide; z++)
                    {
                        var chunk = worldMap[x, z];
                        if (chunk != null)
                        {
                            totalVertices += chunk.VertexCount;
                            totalTriangles += chunk.TriangleCount;
                            minHeightGlobal = MathF.Min(minHeightGlobal, chunk.MinY);
                            maxHeightGlobal = MathF.Max(maxHeightGlobal, chunk.MaxY);
                        }
                    }
                }
            }

            // Hitung world size
            float worldWidth = MapSize * TerrainScale;
            float worldHeight = MapSize * TerrainScale;
            float mapArea = worldWidth * worldHeight;
            float heightRange = maxHeightGlobal - minHeightGlobal;

            // Format output
            var metrics = new StringBuilder();
            metrics.AppendLine("═══════════════════════════════════════════════════════");
            metrics.AppendLine("             🎮 TERRAIN METRICS REPORT 🎮");
            metrics.AppendLine("═══════════════════════════════════════════════════════");
            metrics.AppendLine();

            metrics.AppendLine("📐 MAP DIMENSIONS:");
            metrics.AppendLine($"  • MapSize (pixels):        {MapSize} × {MapSize}");
            metrics.AppendLine($"  • TerrainScale:            {TerrainScale}");
            metrics.AppendLine($"  • World Size:              {worldWidth:F1} × {worldHeight:F1} units");
            metrics.AppendLine($"  • Total Area:              {mapArea:F0} square units");
            metrics.AppendLine();

            metrics.AppendLine("📊 MESH DATA:");
            metrics.AppendLine($"  • ChunkSize:               {ChunkSize} × {ChunkSize} pixels");
            metrics.AppendLine($"  • ChunksPerSide:           {ChunksPerSide} × {ChunksPerSide}");
            metrics.AppendLine($"  • Total Chunks:            {ChunksPerSide * ChunksPerSide}");
            metrics.AppendLine($"  • Total Vertices:          {totalVertices:N0}");
            metrics.AppendLine($"  • Total Triangles:         {totalTriangles:N0}");
            metrics.AppendLine($"  • Avg Vertices/Chunk:      {totalVertices / (ChunksPerSide * ChunksPerSide):F0}");
            metrics.AppendLine();

            metrics.AppendLine("🏔️  ELEVATION DATA:");
            metrics.AppendLine($"  • Min Height:              {minHeightGlobal:F2} units");
            metrics.AppendLine($"  • Max Height:              {maxHeightGlobal:F2} units");
            metrics.AppendLine($"  • Height Range:            {heightRange:F2} units");
            metrics.AppendLine($"  • Aspect Ratio (H:W):      1:{(worldWidth / heightRange):F2}");
            metrics.AppendLine();

            metrics.AppendLine("🎲 LOD & DETAIL:");
            metrics.AppendLine($"  • GlobalLODLevel:          {GlobalLODLevel}");
            metrics.AppendLine($"  • Subdivisions/LOD:        {GetSubdivisionsForLOD(GlobalLODLevel)}");
            metrics.AppendLine($"  • Vertex Density:          {(totalVertices / mapArea):F4} verts/unit²");
            metrics.AppendLine();

            metrics.AppendLine("═══════════════════════════════════════════════════════");

            string report = metrics.ToString();

            // Print ke console
            Console.WriteLine(report);
            System.Diagnostics.Debug.WriteLine(report);

            // Simpan ke file
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "terrain_metrics.txt");
                File.WriteAllText(logPath, report);
                Console.WriteLine($"✅ Metrics saved to: {logPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to save metrics: {ex.Message}");
            }
        }

        // Called by Keyboard when user presses P
        public void SetFrozenFrustumCorners(Vector3[] corners)
        {
            frozenCorners = corners;
            frozenPlanes = CSM.BuildPlanesFromCorners(corners);
        }

        public void ClearFrozenFrustumCorners()
        {
            frozenCorners = null;
            frozenPlanes = null;
        }

        // Expose MapLoader so external systems (main loop) can clamp camera before rendering
        public MapLoader GetMapLoader()
        {
            return mapLoader;
        }

        public static int GetMapSize()
        {
            return MapSize;
        }

        public static int GetChunkSize()
        {
            return ChunkSize;
        }

        public void SetHighlightFrustumMatches(bool enabled)
        {
            highlightFrustumMatches = enabled;
        }

        public Plane[]? GetFrozenPlanes()
        {
            return frozenPlanes;
        }

        //private float exposureState = 1.0f;
        public int Render(Camera camera, Plane[]? frozenPlanes)
        {
            int totalTriangles = 0;

            GL.UseProgram(shaderProgram);
            
            // 1. Hitung Matriks Gabungan (View * Projection)
            Matrix4x4 vp = camera.GetViewMatrix() * camera.GetProjectionMatrix();

            ExtractPlanes(vp, planes);

            bool usingFrozen = frozenPlanes != null;

            for (int x = 0; x < ChunksPerSide; x++)
            {
                for (int z = 0; z < ChunksPerSide; z++)
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

                        if (worldMap?[x, z] != null)
                        {
                            // CDLOD: Calculate distance from camera to chunk center (Scaled to World Space)
                            float chunkCenterX = ((x * ChunkSize) - _halfMapSize + (ChunkSize * 0.5f)) * TerrainScale;
                            float chunkCenterZ = ((z * ChunkSize) - _halfMapSize + (ChunkSize * 0.5f)) * TerrainScale;
                            float chunkCenterY = (worldMap[x, z].MinY + worldMap[x, z].MaxY) * 0.5f;

                            Vector3 chunkCenter = new(chunkCenterX, chunkCenterY, chunkCenterZ);
                            float distance = Vector3.Distance(camera.Position, chunkCenter);
                            float scaledChunkSize = ChunkSize * TerrainScale;

                            // Determine LOD level based on distance (Optimized for FPS)
                            int lodIndex;
                            if (distance > scaledChunkSize * 4.5f) lodIndex = 3;
                            else if (distance > scaledChunkSize * 2.2f) lodIndex = 2;
                            else if (distance > scaledChunkSize * 1.0f) lodIndex = 1;
                            else lodIndex = 0;
                              
                            worldMap[x, z].Draw( lodIndex); 

                            // Estimate actual triangles rendered based on LOD
                            int chunkTriangles = worldMap[x, z].TriangleCount;
                            if (lodIndex == 1) chunkTriangles /= 4;
                            if (lodIndex == 2) chunkTriangles /= 16;
                            if (lodIndex == 3) chunkTriangles /= 64;

                            totalTriangles += chunkTriangles;
                        }
                    }

                    // Determine "insideFrozen" only when frozen frustum exists
                    if (usingFrozen)
                    {
                        bool insideFrozen = IsAABBInsideFrustum(frozenPlanes, x, z);

                        // Draw bounding box:
                        // - If frozen frustum exists: blue = insideFrozen, yellow = outsideFrozen
                        // - If no frozen frustum: yellow if outside camera frustum, blue if inside
                        DrawChunkBoundingBox(x, z, usingFrozen, insideFrozen, inside, camera, camera.GetAspect() );
                    }

                }
            }

            // If frozen frustum exists, draw the frozen frustum wireframe
            if (frozenCorners != null)
            {
                GL.UseProgram(lineShaderProgram);
                GL.Disable(Const.GL_DEPTH_TEST);

                Matrix4x4 v = camera.GetViewMatrix();
                Matrix4x4 p = camera.GetProjectionMatrix();
                unsafe
                {
                    GL.UniformMatrix4fv(lineViewLocation, 1, true, (float*)&v);
                    GL.UniformMatrix4fv(lineProjLocation, 1, true, (float*)&p);
                }
                RenderFrustumDebug(frozenCorners, lineShaderProgram, lineViewLocation, lineProjLocation, camera);
                GL.Enable(Const.GL_DEPTH_TEST);

            }

            camera.ClampToTerrain(mapLoader, deltaTime);
            return totalTriangles;
        }

        public void RenderShadow(Camera camera, CSM csm, int cascadeIndex, uint shadowShader, int modelLoc)
        {
            GL.UseProgram(shadowShader); 

            // Model matrix (identity)
            Matrix4x4 model = Matrix4x4.Identity;
            unsafe
            {
                GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&model);
            }

            // Ambil frustum ortho cascade ini
            Plane[]? orthoPlanes = CSM.BuildPlanesFromCorners(csm.OrthoCorners[cascadeIndex]);

            float scaledChunkSize = ChunkSize * TerrainScale;

            for (int x = 0; x < ChunksPerSide; x++)
            {
                for (int z = 0; z < ChunksPerSide; z++)
                {
                    if (worldMap?[x, z] == null)
                        continue;

                    // CULLING PAKAI ORTHO FRUSTUM (bukan kamera)
                    if (!IsAABBInsideFrustumWorld(orthoPlanes, x, z))
                        continue;

                    // LOD pakai jarak horizontal (lebih stabil untuk shadow)
                    float chunkCenterX = ((x * ChunkSize) - _halfMapSize + (ChunkSize * 0.5f)) * TerrainScale;
                    float chunkCenterZ = ((z * ChunkSize) - _halfMapSize + (ChunkSize * 0.5f)) * TerrainScale;
                    float chunkCenterY = (worldMap[x, z].MinY + worldMap[x, z].MaxY) * 0.5f;

                    Vector2 camXZ = new(camera.Position.X, camera.Position.Z);
                    Vector2 chunkXZ = new(chunkCenterX, chunkCenterZ);

                    float distance = Vector2.Distance(camXZ, chunkXZ);
                    float d = distance / scaledChunkSize;

                    int lodIndex;
                    if (d > 16.0f) lodIndex = 3;
                    else if (d > 11.0f) lodIndex = 2;
                    else if (d > 5.0f) lodIndex = 1;
                    else lodIndex = 0;

                    worldMap[x, z].Draw(lodIndex, false);
                }
            } 
        }

        private static bool IsAABBInsideFrustumWorld(Plane[]? frustumPlanes, int chunkIndexX, int chunkIndexZ)
        {
            if (frustumPlanes == null) return true;

            float minX = ((chunkIndexX * ChunkSize) - _halfMapSize) * TerrainScale;
            float maxX = minX + ChunkSize * TerrainScale;
            float minZ = ((chunkIndexZ * ChunkSize) - _halfMapSize) * TerrainScale;
            float maxZ = minZ + ChunkSize * TerrainScale;

            float minY = -MapSize*2;
            float maxY = MapSize*2;

            if (worldMap != null)
            {
                var chunk = worldMap[chunkIndexX, chunkIndexZ];
                if (chunk != null)
                {
                    minY = MathF.Min(minY, chunk.MinY);
                    maxY = MathF.Max(maxY, chunk.MaxY);
                }
            }

            Span<Vector3> corners =
            [
                new(minX, minY, minZ),
                new(maxX, minY, minZ),
                new(maxX, maxY, minZ),
                new(minX, maxY, minZ),

                new(minX, minY, maxZ),
                new(maxX, minY, maxZ),
                new(maxX, maxY, maxZ),
                new(minX, maxY, maxZ),
            ];

            foreach (var pl in frustumPlanes)
            {
                float maxDist = float.NegativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    float d = Vector3.Dot(pl.Normal, corners[i]) + pl.D;
                    if (d > maxDist) maxDist = d;
                }

                if (maxDist < 0.0f)
                    return false;
            }

            return true;
        }



        
        public static Plane[] ExtractFrustumPlanes(Matrix4x4 m)
        {
            Plane[] planes = new Plane[6];

            // Left
            planes[0] = new Plane(
                m.M14 + m.M11,
                m.M24 + m.M21,
                m.M34 + m.M31,
                m.M44 + m.M41);

            // Right
            planes[1] = new Plane(
                m.M14 - m.M11,
                m.M24 - m.M21,
                m.M34 - m.M31,
                m.M44 - m.M41);

            // Bottom
            planes[2] = new Plane(
                m.M14 + m.M12,
                m.M24 + m.M22,
                m.M34 + m.M32,
                m.M44 + m.M42);

            // Top
            planes[3] = new Plane(
                m.M14 - m.M12,
                m.M24 - m.M22,
                m.M34 - m.M32,
                m.M44 - m.M42);

            // Near
            planes[4] = new Plane(
                m.M13,
                m.M23,
                m.M33,
                m.M43);

            // Far
            planes[5] = new Plane(
                m.M14 - m.M13,
                m.M24 - m.M23,
                m.M34 - m.M33,
                m.M44 - m.M43);

            // normalize
            for (int i = 0; i < 6; i++)
                planes[i] = Plane.Normalize(planes[i]);

            return planes;
        }
        // Test AABB (chunk) against frustum planes.
        // Return true if AABB is at least partially inside (i.e. NOT completely outside any plane).
        private static bool IsAABBInsideFrustum(Plane[]? frustumPlanes, int chunkIndexX, int chunkIndexZ)
        {
            if (frustumPlanes == null) return true;

            float minX = (chunkIndexX * ChunkSize) - _halfMapSize;
            float maxX = minX + ChunkSize;
            float minZ = (chunkIndexZ * ChunkSize) - _halfMapSize;
            float maxZ = minZ + ChunkSize;
            float minY = -0.0f;
            float maxY = 1.0f;

            // Use stackalloc to avoid per-frame heap allocation
            Span<Vector3> corners =
            [
                new(minX, minY, minZ),
                new(maxX, minY, minZ),
                new(maxX, maxY, minZ),
                new(minX, maxY, minZ),
                new(minX, minY, maxZ),
                new(maxX, minY, maxZ),
                new(maxX, maxY, maxZ),
                new(minX, maxY, maxZ),
            ];
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
                const float bias = 5.0f; // coba 2 – 10

                if (maxDist < -bias)
                    return false;
            }

            return true;
        }

        private static bool IsChunkInFrustum(int chunkIndexX, int chunkIndexZ, Plane[] planes)
        {
            float minX = (chunkIndexX * ChunkSize) - _halfMapSize;
            float maxX = minX + ChunkSize;
            float minZ = (chunkIndexZ * ChunkSize) - _halfMapSize;
            float maxZ = minZ + ChunkSize;
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

            float[] lineData = [start.X, start.Y, start.Z, end.X, end.Y, end.Z];
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
            Matrix4x4 proj = camera.GetProjectionMatrix();
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
            float[] lineData =
            [
                // Near
                c[0].X, c[0].Y, c[0].Z,  c[1].X, c[1].Y, c[1].Z,
                c[1].X, c[1].Y, c[1].Z,  c[2].X, c[2].Y, c[2].Z,
                c[2].X, c[2].Y, c[2].Z,  c[3].X, c[3].Y, c[3].Z,
                c[3].X, c[3].Y, c[3].Z,  c[0].X, c[0].Y, c[0].Z,

                // Far
                c[4].X, c[4].Y, c[4].Z,  c[5].X, c[5].Y, c[5].Z,
                c[5].X, c[5].Y, c[5].Z,  c[6].X, c[6].Y, c[6].Z,
                c[6].X, c[6].Y, c[6].Z,  c[7].X, c[7].Y, c[7].Z,
                c[7].X, c[7].Y, c[7].Z,  c[4].X, c[4].Y, c[4].Z,

                // Bridge
                c[0].X, c[0].Y, c[0].Z,  c[4].X, c[4].Y, c[4].Z,
                c[1].X, c[1].Y, c[1].Z,  c[5].X, c[5].Y, c[5].Z,
                c[2].X, c[2].Y, c[2].Z,  c[6].X, c[6].Y, c[6].Z,
                c[3].X, c[3].Y, c[3].Z,  c[7].X, c[7].Y, c[7].Z,
            ];

            // Init VAO/VBO
            lock (debugBufferLock)
            {
                if (debugVao == 0 || debugVbo == 0)
                {
                    fixed (uint* pVao = &debugVao) GL.GenVertexArrays(1, pVao);
                    fixed (uint* pVbo = &debugVbo) GL.GenBuffers(1, pVbo);
                }
            }

            GL.UseProgram(shader);

            // Set warna frustum
            int colorLoc = GL.GetUniformLocation(shader, "lineColor");
            GL.Uniform3f(colorLoc, 1.0f, 0.0f, 0.0f); // merah

            // Upload matrices
            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 p = camera.GetProjectionMatrix();

            GL.UniformMatrix4fv(vLoc, 1, false, (float*)&v);
            GL.UniformMatrix4fv(pLoc, 1, false, (float*)&p);

            // Upload vertex data
            GL.BindVertexArray(debugVao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, debugVbo);

            fixed (void* ptr = lineData)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(lineData.Length * sizeof(float)), ptr, Const.GL_DYNAMIC_DRAW);

            // Posisi saja
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);

            GL.DrawArrays(Const.GL_LINES, 0, 24);

            GL.BindVertexArray(0);
        }



        private unsafe void DrawChunkBoundingBox( int chunkIndexX, int chunkIndexZ,  bool usingFrozen, bool insideFrozen, bool insideCamera,  Camera camera, float aspect)
        {
            // Samakan dengan world-space AABB yang dipakai di culling
            float minX = ((chunkIndexX * ChunkSize) - _halfMapSize) * TerrainScale;
            float maxX = minX + ChunkSize * TerrainScale;
            float minZ = ((chunkIndexZ * ChunkSize) - _halfMapSize) * TerrainScale;
            float maxZ = minZ + ChunkSize * TerrainScale;

            float minY = -5f;
            float maxY = 5f;

            if (worldMap != null)
            {
                var chunk = worldMap[chunkIndexX, chunkIndexZ];
                if (chunk != null)
                {
                    minY = MathF.Min(minY, chunk.MinY);
                    maxY = MathF.Max(maxY, chunk.MaxY);
                }
            }

            // Optional padding biar box debug lebih gampang dilihat
            float yPadding = 2.0f;
            minY -= yPadding;
            maxY += yPadding;

            Vector3[] c =
            [
                new(minX, minY, minZ), new(maxX, minY, minZ),
                new(maxX, maxY, minZ), new(minX, maxY, minZ),

                new(minX, minY, maxZ), new(maxX, minY, maxZ),
                new(maxX, maxY, maxZ), new(minX, maxY, maxZ)
            ];

            Vector3 finalColor =
                usingFrozen
                ? (insideFrozen ? new Vector3(0, 0, 1) : new Vector3(1, 1, 0))
                : (insideCamera ? new Vector3(0, 0, 1) : new Vector3(1, 1, 0));

            float[] lineData =
            [
                // Near
                c[0].X, c[0].Y, c[0].Z,  c[1].X, c[1].Y, c[1].Z,
                c[1].X, c[1].Y, c[1].Z,  c[2].X, c[2].Y, c[2].Z,
                c[2].X, c[2].Y, c[2].Z,  c[3].X, c[3].Y, c[3].Z,
                c[3].X, c[3].Y, c[3].Z,  c[0].X, c[0].Y, c[0].Z,

                // Far
                c[4].X, c[4].Y, c[4].Z,  c[5].X, c[5].Y, c[5].Z,
                c[5].X, c[5].Y, c[5].Z,  c[6].X, c[6].Y, c[6].Z,
                c[6].X, c[6].Y, c[6].Z,  c[7].X, c[7].Y, c[7].Z,
                c[7].X, c[7].Y, c[7].Z,  c[4].X, c[4].Y, c[4].Z,

                // Bridge
                c[0].X, c[0].Y, c[0].Z,  c[4].X, c[4].Y, c[4].Z,
                c[1].X, c[1].Y, c[1].Z,  c[5].X, c[5].Y, c[5].Z,
                c[2].X, c[2].Y, c[2].Z,  c[6].X, c[6].Y, c[6].Z,
                c[3].X, c[3].Y, c[3].Z,  c[7].X, c[7].Y, c[7].Z,
            ];

            lock (debugBufferLock)
            {
                if (debugVao == 0 || debugVbo == 0)
                {
                    fixed (uint* pVao = &debugVao) GL.GenVertexArrays(1, pVao);
                    fixed (uint* pVbo = &debugVbo) GL.GenBuffers(1, pVbo);
                }
            }

            GL.UseProgram(lineShaderProgram);

            int colorLoc = GL.GetUniformLocation(lineShaderProgram, "lineColor");
            GL.Uniform3f(colorLoc, finalColor.X, finalColor.Y, finalColor.Z);

            Matrix4x4 v = camera.GetViewMatrix();
            Matrix4x4 p = camera.GetProjectionMatrix();

            GL.UniformMatrix4fv(lineViewLocation, 1, false, (float*)&v);
            GL.UniformMatrix4fv(lineProjLocation, 1, false, (float*)&p);

            GL.BindVertexArray(debugVao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, debugVbo);

            fixed (void* ptr = lineData)
            {
                GL.BufferData(
                    Const.GL_ARRAY_BUFFER,
                    (nuint)(lineData.Length * sizeof(float)),
                    ptr,
                    Const.GL_DYNAMIC_DRAW
                );
            }

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, 3 * sizeof(float), (void*)0);

            GL.DrawArrays(Const.GL_LINES, 0, 24);

            GL.BindVertexArray(0);

            // Kembalikan shader terrain
            GL.UseProgram(Shader.GetShaderProgram());
        }


        private static void ExtractPlanes(Matrix4x4 vp, Plane[] planes)
        {
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
        }

        public static Vector3[] GetFrustumCorners(Matrix4x4 view, Matrix4x4 proj)
        {
            // Kalikan View lalu Projection (Urutan Row-Major .NET)
            Matrix4x4 viewProj = view * proj;
            if (!Matrix4x4.Invert(viewProj, out Matrix4x4 invVP)) return new Vector3[8];

            Vector3[] corners = new Vector3[8];
            // 8 titik sudut NDC (-1 sampai 1)
            Vector3[] value =
            [
                new(-1, -1, -1),
                new(1, -1, -1),
                new(1, 1, -1),
                new(-1, 1, -1), // Near
                new(-1, -1, 1),
                new(1, -1, 1),
                new(1, 1, 1),
                new(-1, 1, 1)  // Far
            ];
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
                for (int x = 0; x < worldMap.GetLength(0); x++)
                {
                    for (int z = 0; z < worldMap.GetLength(1); z++)
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

        public static int GetTotalMapTriangles()
        {
            return cachedTotalMapTriangles;
        }

        /// <summary>Dapatkan tinggi terrain di posisi world X,Z (untuk snap object ke terrain).</summary>
        public float GetHeightAt(float worldX, float worldZ)
        {
            return mapLoader?.GetHeightInterpolated(worldX, worldZ) ?? 0f;
        }
    }
}
