using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Collections.ObjectModel;
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
        private const float SkirtDepth = 0.25f;

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
            lodLevelLoc = GL.GetUniformLocation(shaderProgram, "lodLevel");

            TerrainTex = _terrainTextures;
            int size = ChunkSize;

            MinY = float.MaxValue;
            MaxY = float.MinValue;

            int lodToLoadFirst = NUM_LODS-1;
            int[] subdivsLOD = [ 4, 3, 2, 1  ];

            // Each cell at LOD 0 is split into subdivisions^2 quads = subdivisions^2 * 2 triangles.
            int subLOD0 = subdivsLOD[0];
            MaxTriangleCount = size * size * subLOD0 * subLOD0 * 2;

            // 1. LOAD LOD 3 + SKIRT SECARA INSTAN
            List<Vertex> vertices = [];
            int subdivisions = subdivsLOD[lodToLoadFirst];

            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    AddQuadFan(vertices, x + worldStartX, z + worldStartZ, mapLoader, subdivisions);
                }
            }
            _vertexCounts[lodToLoadFirst] = vertices.Count;
            SetupGPUResourcesLOD(lodToLoadFirst, [.. vertices]);

            Vertex[] skirtVerts = BuildSkirtVertices(size, worldStartX, worldStartZ, mapLoader, subdivisions);
            _skirtVertexCounts[lodToLoadFirst] = skirtVerts.Length;
            SetupSkirtGPUResourcesLOD(lodToLoadFirst, skirtVerts);

            // 2. LOAD LOD LAINNYA DI BACKGROUND
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
                            AddQuadFan(lodVertices, x + worldStartX, z + worldStartZ, mapLoader, sub);
                        }
                    }

                    Vertex[] lodSkirt = BuildSkirtVertices(size, worldStartX, worldStartZ, mapLoader, sub);

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

        private static Vertex[] BuildSkirtVertices(int chunkSize, int worldStartX, int worldStartZ, MapLoader mapLoader, int subdivisions)
        {
            float terrainScale = MapLoader.TerrainScale;
            float tilingFactor = 1.0f;
            float step = 1.0f / subdivisions;
            int segCount = chunkSize * subdivisions;

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

        // Hapus GenerateSkirts dan semua referensi skirt agar bagian bawah terrain rapi
        // Jika ingin skirt, harus diimplementasikan dengan teknik stitching yang benar, 
        // tapi untuk sekarang kita bersihkan agar tidak ada dinding vertikal yang aneh.


        // compute normal sampling the same height source (mapLoader)
        private static Vector3 CalculateNormalFromMap(MapLoader mapLoader, float xScaled, float zScaled)
        {
            // Jarak sampel terkecil di dunia nyata adalah 1 unit Voxel / Skala Terrain
            float step = MapLoader.TerrainScale;

            // Ambil 4 sampel ketinggian di sekeliling koordinat saat ini
            float hL = mapLoader.GetHeightInterpolated(xScaled - step, zScaled); // Kiri (Left)
            float hR = mapLoader.GetHeightInterpolated(xScaled + step, zScaled); // Kanan (Right)
            float hD = mapLoader.GetHeightInterpolated(xScaled, zScaled - step); // Bawah (Down)
            float hU = mapLoader.GetHeightInterpolated(xScaled, zScaled + step); // Atas (Up)

            // Hitung gradien kemiringan (Kemiringan = Perubahan Tinggi / Perubahan Jarak)
            // Ingat: Jarak dari Kiri ke Kanan adalah 2 * step
            float deltaX = (hL - hR);
            float deltaZ = (hD - hU);

            // Bentuk vektor normal mentah. 
            // Angka 2.0f mewakili jarak horizontal (2 * step) yang sudah disetarakan skalanya.
            Vector3 normal = new(deltaX, 2.0f, deltaZ);

            // WAJIB: Lakukan normalisasi agar panjang vektor kembali menjadi 1.0f sebelum dikirim ke GPU
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

        public void Draw(int lodIndex = 0)
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

            // Draw Mesh
            if (VAOs[actualLod] != 0) {
                GL.BindVertexArray(VAOs[actualLod]);
                GL.DrawArrays(Const.GL_TRIANGLES, 0, _vertexCounts[actualLod]);
            }

            // Draw Skirt to hide LOD seams between neighboring chunks.
            // Culling disabled because skirt winding is not enforced.
            if (SkirtVAOs[actualLod] != 0 && _skirtVertexCounts[actualLod] > 0  )
            {
                OpenGL.EnableFaceCulling(false,true); 
                GL.BindVertexArray(SkirtVAOs[actualLod]);
                GL.DrawArrays(Const.GL_TRIANGLES, 0, _skirtVertexCounts[actualLod]); 
                OpenGL.EnableFaceCulling(true,false);

            }

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
