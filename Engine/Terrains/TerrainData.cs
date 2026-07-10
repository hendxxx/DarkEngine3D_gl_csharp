using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Terrains
{
    public unsafe class TerrainData : IDisposable
    {

        public const int NUM_LODS = 4;
        public uint[] VAOs = new uint[NUM_LODS];
        public uint[] VBOs = new uint[NUM_LODS];
        private readonly int[] _vertexCounts = new int[NUM_LODS];

        // Vertical drop applied to skirt bottom vertices. Must be deeper than the worst
        // expected height difference at an LOD boundary so cracks stay hidden.
        // Dynamically scaled by HeightScale — computed in Generate().
        private static float SkirtDepth = 0.05f;

        public float MinY { get; private set; } = float.MaxValue;
        public float MaxY { get; private set; } = float.MinValue;

        // Analytical LOD 0 triangle count — set during Generate, independent of upload timing.
        // Used as a fallback so stats are correct while the background LOD upload is still pending.
        public int MaxTriangleCount { get; private set; }

        // Expose counts for rendering / stats based on highest detail LOD.
        // Fall back to MaxTriangleCount until the LOD 0 buffer is uploaded.
        public int VertexCount => _vertexCounts[0] > 0 ? _vertexCounts[0] : MaxTriangleCount * 3;
        public int TriangleCount => _vertexCounts[0] > 0 ? _vertexCounts[0] / 3 : MaxTriangleCount;

        private Texture[] TerrainTex = [];

        static int tex0Loc;
        static int tex1Loc;
        static int tex2Loc;
        static int tex3Loc;
        static int tex4Loc;
        static int tex5Loc;
        static int heightScaleLoc;
        static int showLODColorLoc;
        static int showCSMCascadeColorLoc;
        static int lodLevelLoc;

        public static int GetTexLoc(int index)
        {
            return index switch
            {
                0 => tex0Loc,
                1 => tex1Loc,
                2 => tex2Loc,
                3 => tex3Loc,
                4 => tex4Loc,
                5 => tex5Loc,
                _ => tex0Loc
            };
        }
        public static int GetHeightScaleLoc()
        {
            return heightScaleLoc;
        }

        public static int GetShowLODColorLoc() => showLODColorLoc;
        public static int GetshowCSMCascadeColorLoc() => showCSMCascadeColorLoc;
        public static int GetLodLevelLoc() => lodLevelLoc;

        // Kita gunakan List sementara saat Generate, lalu upload ke Native Memory
        // subdivisions: number of subdivisions per edge inside each unit quad.
        // subdivisions = 1 => original (one quad = 2 triangles)
        // subdivisions = 2 => each unit quad split into 2x2 small quads => 8 triangles per original quad
        public void Generate(int ChunkSize, int worldStartX, int worldStartZ, MapLoader mapLoader, Texture[] _terrainTextures)
        {
            uint shaderProgram = Shader.GetShaderProgram();

            tex0Loc = GL.GetUniformLocation(shaderProgram, "tex0");
            tex1Loc = GL.GetUniformLocation(shaderProgram, "tex1");
            tex2Loc = GL.GetUniformLocation(shaderProgram, "tex2");
            tex3Loc = GL.GetUniformLocation(shaderProgram, "tex3");
            tex4Loc = GL.GetUniformLocation(shaderProgram, "tex4");
            tex5Loc = GL.GetUniformLocation(shaderProgram, "tex5");
            heightScaleLoc = GL.GetUniformLocation(shaderProgram, "heightScale");
            showLODColorLoc = GL.GetUniformLocation(shaderProgram, "showLODColor");
            showCSMCascadeColorLoc = GL.GetUniformLocation(shaderProgram, "showCSMCascadeColor");
            lodLevelLoc = GL.GetUniformLocation(shaderProgram, "lodLevel");

            TerrainTex = _terrainTextures;
            int size = ChunkSize;

            MinY = float.MaxValue;
            MaxY = float.MinValue;

            // Set skirt depth proportional to height scale so it's deep enough to hide
            // LOD seam gaps even on steep terrain.
            // CRITICAL: Increased multiplier (0.15f instead of 0.05f) to handle inter-chunk LOD mismatches.
            // This ensures skirts hide cracks between chunks at vastly different subdivision levels.
            SkirtDepth = Math.Max(2.0f, MapLoader.HeightScale * 0.15f);

            int lodToLoadFirst = NUM_LODS-1;
            // Reduced subdivisions: LOD0 uses 3x3 (was 4x4), keeping higher LODs the same.
            // Combined with adaptive tessellation (flat cells use fewer subdivisions),
            // this drastically reduces terrain triangle count while preserving visual detail.
            int[] subdivsLOD = [ 3, 2, 1, 1  ];

            // Each cell at LOD 0 is split into subdivisions^2 quads = subdivisions^2 * 2 triangles.
            int subLOD0 = subdivsLOD[0];
            MaxTriangleCount = size * size * subLOD0 * subLOD0 * 2;

            // ── Pre-compute flatness cache with crack-prevention relaxation ──
            // All cells get terrain-based flatness. Boundary cells always use full
            // subdivisions (isBoundary in GetAdaptiveSubdivision), but their isFlat
            // is based on actual terrain so they don't infect interior cells.
            //
            // The relaxation pass prevents cracks by propagating non-flat status:
            // if a cell is rough, its neighbors also use full detail. This cascades
            // outward so adjacent cells always have matching edge subdivisions.
            //
            // Boundary-adjacent cells (x=1, size-2, z=1, size-2) are NOT forced to
            // full detail — doing so would infect the entire chunk via relaxation
            // propagation, killing all simplification. On flat terrain the height
            // mismatch at the boundary-adjacent edge is sub-threshold (< 0.64 units)
            // and invisible. Skirts handle inter-chunk LOD seam gaps.
            //
            bool[,] isFlat = new bool[size, size];
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                    isFlat[x, z] = IsCellFlat(mapLoader, x + worldStartX, z + worldStartZ);

            // Relaxation: propagate non-flat status to neighbors so adjacent edges match.
            // ENHANCED: Also mark boundary-adjacent cells (1 cell away from edge) as "must use full detail"
            // to prevent visible cracks between chunks at different LODs.
            bool changed;
            do {
                changed = false;
                for (int z = 0; z < size; z++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        // Boundary-adjacent cells: mark as non-flat to force full detail
                        // These are critical for preventing inter-chunk seams
                        bool isBoundaryAdjacent = (x == 1 || x == size - 2 || z == 1 || z == size - 2) &&
                                                   !(x == 0 || x == size - 1 || z == 0 || z == size - 1);

                        if (isFlat[x, z])
                        {
                            // Check neighbors
                            bool hasNonFlatNeighbor = (x > 0 && !isFlat[x - 1, z]) ||
                                                     (x < size - 1 && !isFlat[x + 1, z]) ||
                                                     (z > 0 && !isFlat[x, z - 1]) ||
                                                     (z < size - 1 && !isFlat[x, z + 1]);

                            if (hasNonFlatNeighbor || isBoundaryAdjacent)
                            {
                                isFlat[x, z] = false;
                                changed = true;
                            }
                        }
                    }
                }
            } while (changed);

            // 1. LOAD LOD 3 + SKIRT SECARA INSTAN
            List<Vertex> vertices = [];
            int subdivisions = subdivsLOD[lodToLoadFirst];

            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isBoundary = (x == 0 || x == size - 1 || z == 0 || z == size - 1);
                    int actualSub = GetAdaptiveSubdivision(subdivisions, isBoundary, x, z, isFlat, size, subdivisions);
                    AddQuadFan(vertices, x + worldStartX, z + worldStartZ, mapLoader, actualSub);
                }
            }
            _vertexCounts[lodToLoadFirst] = vertices.Count;
            SetupGPUResourcesLOD(lodToLoadFirst, [.. vertices]);

            Vertex[] skirtVerts = BuildSkirtVertices(size, worldStartX, worldStartZ, mapLoader, subdivisions, subdivsLOD[0]);
            _skirtVertexCounts[lodToLoadFirst] = skirtVerts.Length;
            SetupSkirtGPUResourcesLOD(lodToLoadFirst, skirtVerts);

            // 2. LOAD LOD LAINNYA DI BACKGROUND
            bool[,] bgIsFlat = isFlat; // Capture for background thread (immutable after this point)
            Task.Run(() =>
            {
                for (int lod = 0; lod < NUM_LODS; lod++)
                {
                    if (lod == lodToLoadFirst) continue;

                    List<Vertex> lodVertices = [];
                    int sub = subdivsLOD[lod];

                    for (int z = 0; z < size; z++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            bool isBoundary = (x == 0 || x == size - 1 || z == 0 || z == size - 1);
                            int actualSub = GetAdaptiveSubdivision(sub, isBoundary, x, z, bgIsFlat, size, sub);
                            AddQuadFan(lodVertices, x + worldStartX, z + worldStartZ, mapLoader, actualSub);
                        }
                    }

                    // Skirt uses the HIGHEST subdivision (LOD0) to ensure it covers all adjacent chunk details
                    // This prevents cracks between chunks at different LODs by guaranteeing edge coverage
                    Vertex[] lodSkirt = BuildSkirtVertices(size, worldStartX, worldStartZ, mapLoader, sub, subdivsLOD[0]);

                    lock (pendingUploads)
                    {
                        pendingUploads[lod] = [.. lodVertices];
                        pendingSkirtUploads[lod] = lodSkirt;
                    }
                }
            });
        }

        private readonly Dictionary<int, Vertex[]> pendingUploads = [];
        private readonly Dictionary<int, Vertex[]> pendingSkirtUploads = [];
        private readonly int[] _skirtVertexCounts = new int[NUM_LODS];
        public uint[] SkirtVAOs = new uint[NUM_LODS];
        public uint[] SkirtVBOs = new uint[NUM_LODS];

        private static Vertex[] BuildSkirtVertices(int chunkSize, int worldStartX, int worldStartZ, MapLoader mapLoader, int subdivisions, int maxSubdivision)
        {
            float terrainScale = MapLoader.TerrainScale;
            float tilingFactor = 1.0f;
            // Use the MAX subdivision to ensure skirt covers all possible adjacent chunk densities
            float step = 1.0f / maxSubdivision;
            int segCount = chunkSize * maxSubdivision;

            // 4 edges * segCount segments * 6 vertices per quad
            List<Vertex> verts = new(4 * segCount * 6);

            void AddSegment(float ax, float az, float bx, float bz)
            {
                float axS = ax * terrainScale, azS = az * terrainScale;
                float bxS = bx * terrainScale, bzS = bz * terrainScale;

                float ha = mapLoader.GetHeightInterpolated(axS, azS);
                float hb = mapLoader.GetHeightInterpolated(bxS, bzS);
                float haB = ha - SkirtDepth;
                float hbB = hb - SkirtDepth;

                Vector3 na = CalculateNormalFromMap(mapLoader, axS, azS);
                Vector3 nb = CalculateNormalFromMap(mapLoader, bxS, bzS);
                Vector3 ca = mapLoader.GetColor(axS, azS);
                Vector3 cb = mapLoader.GetColor(bxS, bzS);

                Vertex top0 = new(axS, ha, azS, na.X, na.Y, na.Z, ca.X, ca.Y, ca.Z, axS * tilingFactor, azS * tilingFactor);
                Vertex top1 = new(bxS, hb, bzS, nb.X, nb.Y, nb.Z, cb.X, cb.Y, cb.Z, bxS * tilingFactor, bzS * tilingFactor);
                Vertex bot0 = new(axS, haB, azS, na.X, na.Y, na.Z, ca.X, ca.Y, ca.Z, axS * tilingFactor, azS * tilingFactor);
                Vertex bot1 = new(bxS, hbB, bzS, nb.X, nb.Y, nb.Z, cb.X, cb.Y, cb.Z, bxS * tilingFactor, bzS * tilingFactor);

                // Two triangles per skirt quad. Culling is disabled when drawing, so winding doesn't matter.
                verts.Add(top0); verts.Add(bot0); verts.Add(top1);
                verts.Add(top1); verts.Add(bot0); verts.Add(bot1);
            }

            // South edge (z = worldStartZ)
            for (int i = 0; i < segCount; i++)
            {
                float a = worldStartX + i * step;
                float b = worldStartX + (i + 1) * step;
                AddSegment(a, worldStartZ, b, worldStartZ);
            }
            // North edge (z = worldStartZ + chunkSize)
            for (int i = 0; i < segCount; i++)
            {
                float a = worldStartX + i * step;
                float b = worldStartX + (i + 1) * step;
                AddSegment(a, worldStartZ + chunkSize, b, worldStartZ + chunkSize);
            }
            // West edge (x = worldStartX)
            for (int i = 0; i < segCount; i++)
            {
                float a = worldStartZ + i * step;
                float b = worldStartZ + (i + 1) * step;
                AddSegment(worldStartX, a, worldStartX, b);
            }
            // East edge (x = worldStartX + chunkSize)
            for (int i = 0; i < segCount; i++)
            {
                float a = worldStartZ + i * step;
                float b = worldStartZ + (i + 1) * step;
                AddSegment(worldStartX + chunkSize, a, worldStartX + chunkSize, b);
            }

            return [.. verts];
        }
        private void AddQuadFan(List<Vertex> vertices, float x, float z, MapLoader mapLoader, int subdivisions)
        {
            float step = 1.0f / subdivisions;
            float terrainScale = MapLoader.TerrainScale;
            float tilingFactor = 1.0f;

            for (int iz = 0; iz < subdivisions; iz++)
            {
                for (int ix = 0; ix < subdivisions; ix++)
                {
                    float sx = x + ix * step;
                    float sz = z + iz * step;
                    float sx1 = x + (ix + 1) * step;
                    float sz1 = z + (iz + 1) * step;

                    // Skala koordinat horizontal untuk dunia nyata
                    float sxScaled = sx * terrainScale;
                    float szScaled = sz * terrainScale;
                    float sx1Scaled = sx1 * terrainScale;
                    float sz1Scaled = sz1 * terrainScale;

                    // Sample tinggi menggunakan koordinat skala dunia (Memanggil GetHeight baru yang stabil)
                    float h00 = mapLoader.GetHeightInterpolated(sxScaled, szScaled);
                    float h10 = mapLoader.GetHeightInterpolated(sx1Scaled, szScaled);
                    float h01 = mapLoader.GetHeightInterpolated(sxScaled, sz1Scaled);
                    float h11 = mapLoader.GetHeightInterpolated(sx1Scaled, sz1Scaled);

                    // Perhitungan normal untuk pencahayaan tepi yang halus
                    Vector3 n00 = CalculateNormalFromMap(mapLoader, sxScaled, szScaled);
                    Vector3 n10 = CalculateNormalFromMap(mapLoader, sx1Scaled, szScaled);
                    Vector3 n01 = CalculateNormalFromMap(mapLoader, sxScaled, sz1Scaled);
                    Vector3 n11 = CalculateNormalFromMap(mapLoader, sx1Scaled, sz1Scaled);

                    Vector3 c00 = mapLoader.GetColor(sxScaled, szScaled);
                    Vector3 c10 = mapLoader.GetColor(sx1Scaled, szScaled);
                    Vector3 c01 = mapLoader.GetColor(sxScaled, sz1Scaled);
                    Vector3 c11 = mapLoader.GetColor(sx1Scaled, sz1Scaled);

                    MinY = MathF.Min(MinY, MathF.Min(MathF.Min(h00, h10), MathF.Min(h01, h11)));
                    MaxY = MathF.Max(MaxY, MathF.Max(MathF.Max(h00, h10), MathF.Max(h01, h11)));

                    // Buat vertex untuk 4 pojok kotak
                    Vertex v00 = new(sxScaled, h00, szScaled, n00.X, n00.Y, n00.Z, c00.X, c00.Y, c00.Z, sxScaled * tilingFactor, szScaled * tilingFactor);
                    Vertex v10 = new(sx1Scaled, h10, szScaled, n10.X, n10.Y, n10.Z, c10.X, c10.Y, c10.Z, sx1Scaled * tilingFactor, szScaled * tilingFactor);
                    Vertex v11 = new(sx1Scaled, h11, sz1Scaled, n11.X, n11.Y, n11.Z, c11.X, c11.Y, c11.Z, sx1Scaled * tilingFactor, sz1Scaled * tilingFactor);
                    Vertex v01 = new(sxScaled, h01, sz1Scaled, n01.X, n01.Y, n01.Z, c01.X, c01.Y, c01.Z, sxScaled * tilingFactor, sz1Scaled * tilingFactor);

                    // Susun menjadi 2 segitiga standar (GL_TRIANGLES) yang membentuk satu kotak mulus
                    // Segitiga Pertama (Bawah-Kiri)
                    vertices.Add(v00); vertices.Add(v10); vertices.Add(v01);
                    // Segitiga Kedua (Atas-Kanan)
                    vertices.Add(v10); vertices.Add(v11); vertices.Add(v01);
                }
            }
        }


        /// <summary>
        /// Check if a single cell is flat enough to be simplified to 1 quad.
        /// Samples 5 heights (4 corners + center) and checks height range + center deviation.
        /// </summary>
        private static bool IsCellFlat(MapLoader mapLoader, float cellX, float cellZ)
        {
            float scale = MapLoader.TerrainScale;
            float hScale = MapLoader.HeightScale;

            float sx = cellX * scale;
            float sz = cellZ * scale;
            float sx1 = (cellX + 1) * scale;
            float sz1 = (cellZ + 1) * scale;

            float h00 = mapLoader.GetHeightInterpolated(sx, sz);
            float h10 = mapLoader.GetHeightInterpolated(sx1, sz);
            float h01 = mapLoader.GetHeightInterpolated(sx, sz1);
            float h11 = mapLoader.GetHeightInterpolated(sx1, sz1);
            float hc = mapLoader.GetHeightInterpolated((sx + sx1) * 0.5f, (sz + sz1) * 0.5f);

            float minH = MathF.Min(MathF.Min(h00, h10), MathF.Min(h01, h11));
            float maxH = MathF.Max(MathF.Max(h00, h10), MathF.Max(h01, h11));
            minH = MathF.Min(minH, hc);
            maxH = MathF.Max(maxH, hc);
            float range = maxH - minH;

            float cornerAvg = (h00 + h10 + h01 + h11) * 0.25f;
            float centerDeviation = MathF.Abs(hc - cornerAvg);

            float flatThreshold = hScale * 0.008f;
            float bumpThreshold = hScale * 0.003f;

            return range < flatThreshold && centerDeviation < bumpThreshold;
        }

        /// <summary>
        /// Helper to determine if a cell position should use full subdivision detail
        /// based on its neighbors' subdivision requirements.
        /// This prevents cracks at boundaries between simplified and detailed meshes.
        /// </summary>
        private static bool ShouldUseDetailForNeighbors(int localX, int localZ, int chunkSize, bool[,] isFlatCache)
        {
            // Check all 4 adjacent neighbors (not diagonal)
            // If any neighbor is non-flat, this cell should use full detail for edge matching
            int[][] neighbors = [
                [localX - 1, localZ],  // West
                [localX + 1, localZ],  // East
                [localX, localZ - 1],  // South
                [localX, localZ + 1]   // North
            ];

            foreach (var neighbor in neighbors)
            {
                int nx = neighbor[0];
                int nz = neighbor[1];

                // Skip boundary cells (they always use full detail anyway)
                if (nx < 0 || nx >= chunkSize || nz < 0 || nz >= chunkSize)
                    continue;

                // If neighbor is non-flat (rough), use full detail to match edges
                if (!isFlatCache[nx, nz])
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Adaptive subdivision with crack prevention.
        /// Boundary cells always use full subdivisions (match skirt & neighbor chunks).
        /// Interior cells use the relaxed isFlat cache — the relaxation pass ensures
        /// that any cell adjacent to a non-flat cell also uses full detail,
        /// so adjacent edges always match.
        /// 
        /// CRACK FIX: If a cell is simplified (1 subdivision) but neighbors use higher
        /// subdivisions, force this cell to match the neighbor's density.
        /// This ensures adjacent mesh edges have the same vertex density.
        /// </summary>
        private static int GetAdaptiveSubdivision(int maxSub, bool isBoundary,
            int localX, int localZ, bool[,] isFlatCache, int chunkSize, int baseSubdivisions)
        {
            if (isBoundary || maxSub <= 1)
                return maxSub;

            // Check if this cell is flat
            if (isFlatCache[localX, localZ])
            {
                // If this flat cell has rough neighbors, use full subdivisions to match edges
                if (ShouldUseDetailForNeighbors(localX, localZ, chunkSize, isFlatCache))
                    return maxSub;

                // Safe to simplify: all neighbors are also flat (or will be forced to detail)
                return 1;
            }

            // Cell is rough, use full subdivisions
            return maxSub;
        }

        private static void AddQuad(List<Vertex> vertices, float x, float z, MapLoader mapLoader, int subdivisions)
        {
            float step = 1.0f / subdivisions;
            float terrainScale = MapLoader.TerrainScale;
            float tilingFactor = 1.0f;

            for (int iz = 0; iz < subdivisions; iz++)
            {
                for (int ix = 0; ix < subdivisions; ix++)
                {
                    float sx = x + ix * step;
                    float sz = z + iz * step;
                    float sx1 = x + (ix + 1) * step;
                    float sz1 = z + (iz + 1) * step;

                    float sxS = sx * terrainScale;
                    float szS = sz * terrainScale;
                    float sx1S = sx1 * terrainScale;
                    float sz1S = sz1 * terrainScale;

                    float h00 = mapLoader.GetHeightInterpolated(sxS, szS);
                    float h10 = mapLoader.GetHeightInterpolated(sx1S, szS);
                    float h01 = mapLoader.GetHeightInterpolated(sxS, sz1S);
                    float h11 = mapLoader.GetHeightInterpolated(sx1S, sz1S);

                    Vector3 n00 = CalculateNormalFromMap(mapLoader, sxS, szS);
                    Vector3 n10 = CalculateNormalFromMap(mapLoader, sx1S, szS);
                    Vector3 n01 = CalculateNormalFromMap(mapLoader, sxS, sz1S);
                    Vector3 n11 = CalculateNormalFromMap(mapLoader, sx1S, sz1S);

                    Vector3 c00 = mapLoader.GetColor(sxS, szS);
                    Vector3 c10 = mapLoader.GetColor(sx1S, szS);
                    Vector3 c01 = mapLoader.GetColor(sxS, sz1S);
                    Vector3 c11 = mapLoader.GetColor(sx1S, sz1S);

                    Vertex v00 = new(sxS, h00, szS, n00.X, n00.Y, n00.Z, c00.X, c00.Y, c00.Z, sx * tilingFactor, sz * tilingFactor);
                    Vertex v10 = new(sx1S, h10, szS, n10.X, n10.Y, n10.Z, c10.X, c10.Y, c10.Z, sx1 * tilingFactor, sz * tilingFactor);
                    Vertex v11 = new(sx1S, h11, sz1S, n11.X, n11.Y, n11.Z, c11.X, c11.Y, c11.Z, sx1 * tilingFactor, sz1 * tilingFactor);
                    Vertex v01 = new(sxS, h01, sz1S, n01.X, n01.Y, n01.Z, c01.X, c01.Y, c01.Z, sx * tilingFactor, sz1 * tilingFactor);

                    // Standard 2 triangles per quad
                    vertices.Add(v00); vertices.Add(v10); vertices.Add(v11);
                    vertices.Add(v00); vertices.Add(v11); vertices.Add(v01);
                }
            }
        }

        // Skirt built via BuildSkirtVertices() — drops SkirtDepth units below each
        // chunk edge to hide LOD seam gaps between adjacent chunks at different LODs.
        // Face culling is disabled when drawing skirts (winding is inconsistent).
        //
        // Compute normal sampling the same height source (mapLoader)
        private static Vector3 CalculateNormalFromMap(MapLoader mapLoader, float xScaled, float zScaled)
        {
            // Jarak sampel terkecil di dunia nyata adalah 1 unit Voxel / Skala Terrain
            float step = MapLoader.TerrainScale;

            // Ambil 4 sampel ketinggian di sekeliling koordinat saat ini
            float hL = mapLoader.GetHeightInterpolated(xScaled - step, zScaled);
            float hR = mapLoader.GetHeightInterpolated(xScaled + step, zScaled);
            float hD = mapLoader.GetHeightInterpolated(xScaled, zScaled - step);
            float hU = mapLoader.GetHeightInterpolated(xScaled, zScaled + step);

            float deltaX = (hL - hR);
            float deltaZ = (hD - hU);

            Vector3 normal = new(deltaX, 2.0f, deltaZ);
            return Vector3.Normalize(normal);
        }



        private void SetupGPUResourcesLOD(int lod, Vertex[] data)
        {
            // Pastikan fungsi ini sudah dimuat di ApiLoader
            fixed (uint* pVao = &VAOs[lod]) GL.GenVertexArrays(1, pVao);
            fixed (uint* pVbo = &VBOs[lod]) GL.GenBuffers(1, pVbo);

            GL.BindVertexArray(VAOs[lod]);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, VBOs[lod]); // GL_ARRAY_BUFFER

            fixed (void* ptr = data)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(data.Length * sizeof(Vertex)), ptr, Const.GL_STATIC_DRAW); // GL_STATIC_DRAW
            }

            // Atribut 0: Posisi (vec3)
            // Setup Attributes (Posisi & Warna)
            int stride = sizeof(Vertex);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)sizeof(Vector3));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, Const.GL_FLOAT, false, stride, (void*)(sizeof(Vector3) * 2));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 2, Const.GL_FLOAT, false, stride, (void*)(sizeof(Vector3) * 3));
        }
        public void Draw( int lodIndex = 0, bool drawSkirt = true)
        {
            // 1. CEK PENDING UPLOADS (Mesh + Skirt)
            if (pendingUploads.Count > 0 || pendingSkirtUploads.Count > 0)
            {
                lock (pendingUploads)
                {
                    if (pendingUploads.TryGetValue(lodIndex, out Vertex[]? data))
                    {
                        _vertexCounts[lodIndex] = data.Length;
                        SetupGPUResourcesLOD(lodIndex, data);
                        pendingUploads.Remove(lodIndex);
                    }
                    if (pendingSkirtUploads.TryGetValue(lodIndex, out Vertex[]? skirtData))
                    {
                        _skirtVertexCounts[lodIndex] = skirtData.Length;
                        SetupSkirtGPUResourcesLOD(lodIndex, skirtData);
                        pendingSkirtUploads.Remove(lodIndex);
                    }
                }
            }

            int actualLod = lodIndex;
            if (VAOs[actualLod] == 0) actualLod = NUM_LODS - 1;

            actualLod = Math.Clamp(actualLod, 0, NUM_LODS - 1);

            int heightScaleLoc = GetHeightScaleLoc();
            if (heightScaleLoc != -1)
            {
                GL.Uniform3f(heightScaleLoc, MapLoader.HeightScale, 0f, 0f);
            }

            int showLODColorLoc = GetShowLODColorLoc();
            if (showLODColorLoc != -1)
            {
                GL.Uniform1i(showLODColorLoc, Keyboard.GetShowLODColor() ? 1 : 0);
            }

            int showCSMCascadeColorLoc = GetshowCSMCascadeColorLoc();
            if (showCSMCascadeColorLoc != -1)
            {
                GL.Uniform1i(showCSMCascadeColorLoc, Keyboard.GetshowCSMCascadeColor() ? 1 : 0);
            }



            int lodLevelLoc = GetLodLevelLoc();
            if (lodLevelLoc != -1)
            {
                GL.Uniform1i(lodLevelLoc, actualLod);
            }

            uint[] textureUnits = [ Const.GL_TEXTURE0, Const.GL_TEXTURE1, Const.GL_TEXTURE2, Const.GL_TEXTURE3, Const.GL_TEXTURE4, Const.GL_TEXTURE5 ];
            for (int i = 0; i < TerrainTex.Length && i < 6; i++)
            {
                GL.ActiveTexture(textureUnits[i]);
                GL.BindTexture(Const.GL_TEXTURE_2D, TerrainTex[i].ID);
                GL.Uniform1i(GetTexLoc(i), i);
            }

            int useTextureLoc = GL.GetUniformLocation(Shader.GetShaderProgram(), "useTexture");
          
            GL.Uniform1i(useTextureLoc, 1);
            GL.Enable(Const.GL_POLYGON_OFFSET_FILL);
            GL.PolygonOffset(2.0f, 4.0f);
            // Draw Mesh
            if (VAOs[actualLod] != 0) {


                GL.BindVertexArray(VAOs[actualLod]);
                GL.DrawArrays(Const.GL_TRIANGLES, 0, _vertexCounts[actualLod]);
            }

            // Draw Skirt to hide LOD seams between neighboring chunks.
            // Culling disabled because skirt winding is not enforced.
            if (drawSkirt && SkirtVAOs[actualLod] != 0 && _skirtVertexCounts[actualLod] > 0  )
            {
                
                OpenGL.EnableFaceCulling(false,true); 
                GL.BindVertexArray(SkirtVAOs[actualLod]);
                GL.DrawArrays(Const.GL_TRIANGLES, 0, _skirtVertexCounts[actualLod]); 
                OpenGL.EnableFaceCulling(true,false);

            }
            GL.PolygonOffset(0.0f, 0.0f);
            GL.Disable(Const.GL_POLYGON_OFFSET_FILL);
            GL.BindVertexArray(0);
        }

        private void SetupSkirtGPUResourcesLOD(int lod, Vertex[] data)
        {
            if (SkirtVAOs[lod] == 0) {
                fixed (uint* pVao = &SkirtVAOs[lod]) GL.GenVertexArrays(1, pVao);
                fixed (uint* pVbo = &SkirtVBOs[lod]) GL.GenBuffers(1, pVbo);
            }

            GL.BindVertexArray(SkirtVAOs[lod]);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, SkirtVBOs[lod]);

            fixed (void* ptr = data)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(data.Length * sizeof(Vertex)), ptr, Const.GL_STATIC_DRAW);
            }

            int stride = sizeof(Vertex);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)sizeof(Vector3));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, Const.GL_FLOAT, false, stride, (void*)(sizeof(Vector3) * 2));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 2, Const.GL_FLOAT, false, stride, (void*)(sizeof(Vector3) * 3));
        }

        public void Dispose()
        {
            // Penting: Hapus resource di GPU saat chunk tidak lagi digunakan
            for (int lod = 0; lod < NUM_LODS; lod++)
            {
                if (VAOs[lod] != 0)
                {
                    fixed (uint* pVao = &VAOs[lod]) GL.DeleteVertexArrays(1, pVao);
                    VAOs[lod] = 0;
                }
                if (VBOs[lod] != 0)
                {
                    fixed (uint* pVbo = &VBOs[lod]) GL.DeleteBuffers(1, pVbo);
                    VBOs[lod] = 0;
                }
                if (SkirtVAOs[lod] != 0)
                {
                    fixed (uint* pVao = &SkirtVAOs[lod]) GL.DeleteVertexArrays(1, pVao);
                    SkirtVAOs[lod] = 0;
                }
                if (SkirtVBOs[lod] != 0)
                {
                    fixed (uint* pVbo = &SkirtVBOs[lod]) GL.DeleteBuffers(1, pVbo);
                    SkirtVBOs[lod] = 0;
                }
                _vertexCounts[lod] = 0;
                _skirtVertexCounts[lod] = 0;
            }
            GC.SuppressFinalize(this);
        }
    }
}
