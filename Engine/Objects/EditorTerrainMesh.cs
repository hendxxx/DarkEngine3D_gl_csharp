using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using StbImageSharp;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Objects;

/// <summary>
/// GPU terrain mesh used by editor Plane objects when advanced terrain is enabled.
/// Loads a heightmap (.raw 8-bit or any image), builds a grid mesh whose height comes
/// from the heightmap, and renders it with the editor terrain shader that blends up to
/// 4 custom layers (air, dirt, grass, snow) by height + slope.
/// Each EditorObject owns its own instance so every plane can have different settings.
/// </summary>
public unsafe class EditorTerrainMesh : IDisposable
{
    // ── GPU resources ──
    /// <summary>One entry per chunk (chunksPerSide × chunksPerSide). Each chunk is an
    /// independent grid mesh rendered as quads through an index buffer (4 unique corner
    /// vertices per cell + 6 indices = 2 triangles), so the whole terrain is quad-based
    /// while staying compatible with the core-profile shaders.</summary>
    private readonly List<ChunkGpu> _chunks = [];
    private bool _gpuReady = false;

    /// <summary>GPU handles + index count of one chunk sub-mesh.</summary>
    private struct ChunkGpu
    {
        public uint Vao;
        public uint Vbo;
        public uint Ebo;
        public int IndexCount;
    }

    // ── Heightmap data (normalized 0..1) ──
    private float[] _heights = [];
    private int _hmWidth = 2;
    private int _hmHeight = 2;

    // ── Last Generate() params — reused to rebuild the mesh after brush edits ──
    private int _lastChunkSize = 32;
    private int _lastChunksPerSide = 1;
    private float _lastHeightScale = 30f;
    private float _lastFootprintX = 25f;
    private float _lastFootprintZ = 25f;

    /// <summary>True once the user painted on this mesh (so the painted data can be
    /// persisted to the scene file and to a .raw via SaveHeightmapFile).</summary>
    public bool IsModified { get; private set; } = false;

    // ── Layer textures (index 0=air, 1=dirt, 2=grass, 3=snow) ──
    private readonly uint[] _layerTextures = new uint[4];
    private readonly string[] _layerPaths = new string[4];

    // ── Manual layer paint (splat/control map) ──
    /// <summary>Default splat resolution per side (independent of mesh chunk size).</summary>
    private const int DefaultSplatSize = 128;
    /// <summary>RGBA8 splat weights (R=air, G=dirt, B=grass, A=snow). All-zero = use the
    /// automatic height+slope texturing. Painted with the 🎨 brush tool.</summary>
    private byte[] _splat = new byte[DefaultSplatSize * DefaultSplatSize * 4];
    /// <summary>Actual splat resolution of <see cref="_splat"/> (persisted with the blob).</summary>
    private int _splatDim = DefaultSplatSize;
    private uint _splatTex = 0;
    /// <summary>True once the user painted/cleared layers (drives usePaintMask + persistence).</summary>
    public bool SplatModified { get; private set; } = false;
    private static readonly Vector3[] FallbackColors =
    [
        new(0.15f, 0.45f, 0.75f), // air / water (blue)
        new(0.50f, 0.38f, 0.25f), // dirt (brown)
        new(0.30f, 0.60f, 0.25f), // grass (green)
        new(0.92f, 0.94f, 0.98f), // snow (white)
    ];

    // ── Editor terrain shader (lazy init, shared) ──
    private static uint _program = 0;
    private static int _modelLoc = -1, _viewLoc = -1, _projLoc = -1;
    private static int _sunDirLoc = -1, _lightColorLoc = -1, _viewPosLoc = -1;
    private static int _useFogLoc = -1, _fogColorLoc = -1;
    private static int _heightScaleLoc = -1, _layerLevelsLoc = -1;
    private static int _slopeThresholdLoc = -1, _texTilingLoc = -1, _useStochasticSamplingLoc = -1;
    private static int _usePaintMaskLoc = -1, _tex4Loc = -1;
    private static int _showHeatmapLoc = -1;
    private static int _showContoursLoc = -1;
    private static readonly int[] _texLocs = new int[4];
    // ── CSM shadow uniforms (editor viewport) ──
    private static int _shadowFilterLoc = -1, _shadowDirLoc = -1;
    private static int _showCSMCascadeColorLoc = -1;
    private static int _shadowMap0Loc = -1, _shadowMap1Loc = -1, _shadowMap2Loc = -1;
    private static int _lightSpace0Loc = -1, _lightSpace1Loc = -1, _lightSpace2Loc = -1;
    private static int _cascadeEnds0Loc = -1, _cascadeEnds1Loc = -1, _cascadeEnds2Loc = -1;
    // Cloud shadow uniforms
    private static int _cloudAltLoc = -1, _cloudSpeedLoc = -1, _cloudDetailLoc = -1;
    private static int _cloudErosionLoc = -1, _cloudShadowStrLoc = -1, _cloudsEnabledLoc = -1;
    private static int _cloudScaleLoc = -1, _cloudWeatherLoc = -1, _timeCloudLoc = -1;
    private static float _cloudTimeAccum = 0f;

    /// <summary>True once a valid heightmap + mesh have been generated.</summary>
    public bool IsReady => _gpuReady;

    /// <summary>Number of triangles in the generated mesh (all chunks combined).</summary>
    public int TriangleCount
    {
        get
        {
            int total = 0;
            foreach (var c in _chunks) total += c.IndexCount;
            return total / 3;
        }
    }

    /// <summary>Load a heightmap file (.raw 8-bit or any stb-supported image).</summary>
    public bool LoadHeightmap(string path)
    {
        // Resolve relative heightmap paths against the exe folder.
        path = DarkEngine3D_gl_csharp.Engine.Helpers.PathHelpers.Resolve(path);

        _heights = [];
        _hmWidth = 2;
        _hmHeight = 2;
        IsModified = false;
        // Empty path = a valid "flat" heightmap (height 0 everywhere). The terrain mesh
        // is still built so layer textures, painting and brushes all work on a flat plane.
        if (string.IsNullOrEmpty(path))
            return true;
        if (!File.Exists(path))
            return false;

        try
        {
            if (path.EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
            {
                byte[] raw = File.ReadAllBytes(path);
                int size = (int)MathF.Sqrt(raw.Length);
                if (size < 2) return false;
                _hmWidth = size;
                _hmHeight = size;
                _heights = new float[size * size];
                for (int i = 0; i < _heights.Length && i < raw.Length; i++)
                    _heights[i] = raw[i] / 255f;
                return true;
            }

            byte[] bytes = File.ReadAllBytes(path);
            var img = ImageResult.FromMemory(bytes, ColorComponents.Grey);
            if (img.Width < 2 || img.Height < 2 || img.Data == null || img.Data.Length == 0)
                return false;
            _hmWidth = img.Width;
            _hmHeight = img.Height;
            _heights = new float[img.Width * img.Height];
            for (int i = 0; i < _heights.Length && i < img.Data.Length; i++)
                _heights[i] = img.Data[i] / 255f;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EditorTerrain] Failed to load heightmap '{path}': {ex.Message}");
            _heights = [];
            _hmWidth = 2;
            _hmHeight = 2;
            return false;
        }
    }

    /// <summary>Bilinear-sample the heightmap (uv 0..1, clamped).</summary>
    private float Sample(float u, float v)
    {
        if (_heights.Length == 0) return 0f;
        u = Math.Clamp(u, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);
        float px = u * (_hmWidth - 1);
        float py = v * (_hmHeight - 1);
        int x0 = (int)px, y0 = (int)py;
        int x1 = Math.Min(x0 + 1, _hmWidth - 1);
        int y1 = Math.Min(y0 + 1, _hmHeight - 1);
        float fx = px - x0, fy = py - y0;
        float h00 = _heights[y0 * _hmWidth + x0];
        float h10 = _heights[y0 * _hmWidth + x1];
        float h01 = _heights[y1 * _hmWidth + x0];
        float h11 = _heights[y1 * _hmWidth + x1];
        return (h00 * (1 - fx) + h10 * fx) * (1 - fy)
             + (h01 * (1 - fx) + h11 * fx) * fy;
    }

    /// <summary>
    /// Generate the terrain as a <paramref name="chunksPerSide"/> × <paramref name="chunksPerSide"/>
    /// grid of chunk sub-meshes, each chunk a grid of <paramref name="chunkSize"/> ×
    /// <paramref name="chunkSize"/> quads. Every quad is stored as 4 unique corner vertices
    /// + a 6-index element buffer (2 triangles), so the terrain is quad-based while staying
    /// compatible with the core-profile shaders. More chunks = more sub-meshes, and a bigger
    /// chunkSize = more quads/triangles per chunk — both make the terrain more detailed.
    /// Local XZ range is [-0.5, 0.5] (matching the plane's local space); Y holds the heightmap
    /// height × <paramref name="heightScale"/> in WORLD units. The draw pass applies a model
    /// matrix of Scale(X, 1, Z) × rotation × translation so XZ stretch comes from the object's
    /// Scale while Y keeps real world height.
    /// </summary>
    public void Generate(int chunkSize, int chunksPerSide, float heightScale, float footprintX, float footprintZ)
    {
        // Tear down any previous GPU buffers.
        foreach (var c in _chunks)
        {
            if (c.Vao != 0) { uint v = c.Vao; GL.DeleteVertexArrays(1, &v); }
            if (c.Vbo != 0) { uint b = c.Vbo; GL.DeleteBuffers(1, &b); }
            if (c.Ebo != 0) { uint b = c.Ebo; GL.DeleteBuffers(1, &b); }
        }
        _chunks.Clear();
        _gpuReady = false;

        chunkSize = Math.Clamp(chunkSize, 4, 256);
        chunksPerSide = Math.Clamp(chunksPerSide, 1, 128);
        if (footprintX < 0.01f) footprintX = 1f;
        if (footprintZ < 0.01f) footprintZ = 1f;

        // Remember the params so PaintHeight can rebuild the mesh after editing heights.
        _lastChunkSize = chunkSize;
        _lastChunksPerSide = chunksPerSide;
        _lastHeightScale = heightScale;
        _lastFootprintX = footprintX;
        _lastFootprintZ = footprintZ;

        int n = chunkSize;         // quads per chunk side
        int cs = chunksPerSide;    // chunks per terrain side
        int vs = n + 1;            // vertices per chunk side
        float cellLocal = 1f / (cs * n); // local-space size of one grid step
        float cellX = footprintX * cellLocal; // world size of one grid step in X
        float cellZ = footprintZ * cellLocal; // world size of one grid step in Z

        for (int cz = 0; cz < cs; cz++)
        {
            for (int cx = 0; cx < cs; cx++)
            {
                // ── Smooth per-vertex normals for THIS chunk ──
                // One normal per GRID VERTEX, computed with central differences of the
                // heightmap (2 grid cells wide; forward/backward difference at the chunk
                // borders). Neighboring quads share their corner normals, so lighting and
                // shadow shading stay continuous. Normals are expressed in LOCAL mesh space
                // (XZ in [-0.5, 0.5], Y in world units); the vertex shader's inverse-transpose
                // (Scale(X,1,Z) × rotation) turns them back into the true world slope.
                var gridNormals = new Vector3[vs * vs];
                for (int vz = 0; vz < vs; vz++)
                {
                    float v = (cz + (float)vz / n) / cs;
                    float vLo = Math.Max(0f, (cz + Math.Max(0, vz - 1) / (float)n) / cs);
                    float vHi = Math.Min(1f, (cz + Math.Min(n, vz + 1) / (float)n) / cs);
                    int stepZ = (vz > 0 && vz < n) ? 2 : 1;
                    for (int vx = 0; vx < vs; vx++)
                    {
                        float u = (cx + (float)vx / n) / cs;
                        float uLo = Math.Max(0f, (cx + Math.Max(0, vx - 1) / (float)n) / cs);
                        float uHi = Math.Min(1f, (cx + Math.Min(n, vx + 1) / (float)n) / cs);
                        int stepX = (vx > 0 && vx < n) ? 2 : 1;

                        float hL = Sample(uLo, v) * heightScale;
                        float hR = Sample(uHi, v) * heightScale;
                        float hD = Sample(u, vLo) * heightScale;
                        float hU = Sample(u, vHi) * heightScale;

                        // World slope (dy/dx_world) → local slope (× footprint, since local X
                        // spans [-0.5, 0.5] over footprintX world units). Sign: the surface
                        // tangent is (1, dh/dlx, 0), so the normal (nx, 1, nz) must satisfy
                        // nx = -dh/dlx — hence LEFT minus RIGHT (hL - hR), NOT right minus left.
                        float gx = (hL - hR) / (stepX * cellX) * footprintX;
                        float gz = (hD - hU) / (stepZ * cellZ) * footprintZ;
                        var nrm = Vector3.Normalize(new Vector3(gx, 1f, gz));
                        if (nrm == Vector3.Zero || !float.IsFinite(nrm.Y)) nrm = Vector3.UnitY;
                        gridNormals[vz * vs + vx] = nrm;
                    }
                }

                // ── Build the chunk's quad grid: 4 unique vertices per cell + 6 indices ──
                var verts = new List<Vertex>(vs * vs);
                var indices = new List<uint>(n * n * 6);
                for (int iz = 0; iz <= n; iz++)
                {
                    for (int ix = 0; ix <= n; ix++)
                    {
                        // Local position of this grid vertex within the whole terrain.
                        float u = (cx + (float)ix / n) / cs;
                        float v = (cz + (float)iz / n) / cs;
                        float lx = u - 0.5f;
                        float lz = v - 0.5f;
                        float h = Sample(u, v) * heightScale;

                        // Smooth per-vertex normal from the precomputed chunk grid.
                        Vector3 nm = gridNormals[iz * vs + ix];
                        verts.Add(new Vertex(lx, h, lz, nm.X, nm.Y, nm.Z, 1, 1, 1, lx, lz));
                    }
                }

                for (int iz = 0; iz < n; iz++)
                {
                    for (int ix = 0; ix < n; ix++)
                    {
                        int i00 = iz * vs + ix;
                        int i10 = iz * vs + (ix + 1);
                        int i11 = (iz + 1) * vs + (ix + 1);
                        int i01 = (iz + 1) * vs + ix;

                        // Two triangles per quad, CCW when viewed from above (+Y) — identical
                        // to Object3D.CreatePlaneVertices so back-face culling (the scene
                        // default is CullMode.Back + FrontFaceWinding.CCW) keeps the terrain
                        // front-facing. Tri 1: n00 → n11 → n10 | Tri 2: n00 → n01 → n11
                        indices.Add((uint)i00); indices.Add((uint)i11); indices.Add((uint)i10);
                        indices.Add((uint)i00); indices.Add((uint)i01); indices.Add((uint)i11);
                    }
                }

                // ── Upload this chunk's GPU resources ──
                uint vao = 0, vbo = 0, ebo = 0;
                GL.GenVertexArrays(1, &vao);
                GL.GenBuffers(1, &vbo);
                GL.GenBuffers(1, &ebo);
                GL.BindVertexArray(vao);
                GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
                fixed (void* ptr = verts.ToArray())
                {
                    // DYNAMIC because brush painting rebuilds this buffer on every stamp.
                    GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(verts.Count * sizeof(Vertex)), ptr, Const.GL_DYNAMIC_DRAW);
                }
                GL.BindBuffer(Const.GL_ELEMENT_ARRAY_BUFFER, ebo);
                fixed (void* ptr = indices.ToArray())
                {
                    GL.BufferData(Const.GL_ELEMENT_ARRAY_BUFFER, (nuint)(indices.Count * sizeof(uint)), ptr, Const.GL_STATIC_DRAW);
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
                GL.BindVertexArray(0);

                _chunks.Add(new ChunkGpu { Vao = vao, Vbo = vbo, Ebo = ebo, IndexCount = indices.Count });
            }
        }

        _gpuReady = true;
    }

    /// <summary>Height of the terrain surface at a LOCAL point (X/Z in [-0.5, 0.5])
    /// in the mesh's Y units (world units, since heightScale is applied).</summary>
    public float SampleLocalHeight(float localX, float localZ)
    {
        if (_heights.Length == 0) return 0f;
        return Sample(localX + 0.5f, localZ + 0.5f) * Math.Max(1f, _lastHeightScale);
    }

    /// <summary>
    /// Paint a soft brush stamp into the heightmap and rebuild the GPU mesh immediately
    /// (real-time raise/lower while the user drags). Coordinates are LOCAL space
    /// (X/Z in [-0.5, 0.5], radius in local units where 0.5 spans half the footprint).
    /// <paramref name="deltaNorm"/> is the height delta in NORMALIZED units (world delta
    /// divided by heightScale). Negative = lower, positive = raise.
    /// </summary>
    public void PaintHeight(float localX, float localZ, float radiusLocal, float deltaNorm, float softness, int falloffCurve)
    {
        if (_heights.Length == 0 || _hmWidth < 2 || _hmHeight < 2) return;
        if (Math.Abs(deltaNorm) < 1e-6f || radiusLocal <= 0f) return;

        float cu = Math.Clamp(localX + 0.5f, 0f, 1f);
        float cv = Math.Clamp(localZ + 0.5f, 0f, 1f);
        float r = Math.Max(1e-4f, radiusLocal);

        // Only scan the pixels touched by the brush (bounding box of the circle).
        int x0 = Math.Max(0, (int)((cu - r) * (_hmWidth - 1)));
        int x1 = Math.Min(_hmWidth - 1, (int)MathF.Ceiling((cu + r) * (_hmWidth - 1)));
        int y0 = Math.Max(0, (int)((cv - r) * (_hmHeight - 1)));
        int y1 = Math.Min(_hmHeight - 1, (int)MathF.Ceiling((cv + r) * (_hmHeight - 1)));

        float invW = _hmWidth > 1 ? 1f / (_hmWidth - 1) : 1f;
        float invH = _hmHeight > 1 ? 1f / (_hmHeight - 1) : 1f;
        for (int y = y0; y <= y1; y++)
        {
            float v = y * invH;
            float dv = v - cv;
            for (int x = x0; x <= x1; x++)
            {
                float u = x * invW;
                float du = u - cu;
                float d = MathF.Sqrt(du * du + dv * dv);
                if (d >= r) continue;

                float w = BrushWeight(d, r, softness, falloffCurve);
                int idx = y * _hmWidth + x;
                _heights[idx] = Math.Clamp(_heights[idx] + deltaNorm * w, 0f, 1f);
            }
        }

        IsModified = true;
        Generate(_lastChunkSize, _lastChunksPerSide, _lastHeightScale, _lastFootprintX, _lastFootprintZ);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Brush falloff curves (Unreal-style brush falloff selection)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Normalized falloff curve value at distance <paramref name="t"/> (0 = center,
    /// 1 = edge): 0=Linear, 1=Smooth, 2=Sharp, 3=Spherical, 4=Soft.</summary>
    private static float CurveWeight(float t, int curve)
    {
        t = Math.Clamp(t, 0f, 1f);
        switch (curve)
        {
            case 0: return 1f - t;                                        // Linear
            case 2: return (1f - t) * (1f - t);                           // Sharp
            case 3: return MathF.Sqrt(MathF.Max(0f, 1f - t * t));         // Spherical
            case 4: { float s = 1f - t; return s * s * (1f + 2f * t); }   // Soft (smoothstep)
            default: return 1f - t * t;                                   // Smooth
        }
    }

    /// <summary>Combined brush weight for a pixel at distance <paramref name="d"/> from the
    /// stamp center (radius <paramref name="r"/>). <paramref name="softness"/> 0 = hard
    /// edge (a step), 1 = the raw falloff curve. The power remap keeps the weight
    /// continuous — it always reaches 0 exactly at the brush boundary, so stamps never
    /// build a raised rim around the edge.</summary>
    private static float BrushWeight(float d, float r, float softness, int curve)
    {
        if (d >= r) return 0f;
        float c = CurveWeight(d / r, curve);
        return MathF.Pow(MathF.Max(1e-4f, c), 1f / (0.02f + Math.Clamp(softness, 0f, 1f)));
    }

    /// <summary>Normalized (0..1) heightmap height at a LOCAL point (X/Z in [-0.5, 0.5]).
    /// Used by the ⏹ flatten brush to capture the target height of the first stamp.</summary>
    public float SampleLocalHeightNorm(float localX, float localZ)
    {
        if (_heights.Length == 0) return 0f;
        return Sample(localX + 0.5f, localZ + 0.5f);
    }

    /// <summary>
    /// Smooth one brush stamp: each affected heightmap pixel is blended toward the average
    /// of its neighborhood (single-pass blur on a snapshot, so no directional smearing).
    /// <paramref name="strength"/> 0..1 is the blend amount per stamp. Coordinates are
    /// LOCAL space, same convention as <see cref="PaintHeight"/>.
    /// </summary>
    public void SmoothHeight(float localX, float localZ, float radiusLocal, float strength, int falloffCurve)
    {
        if (_heights.Length == 0 || _hmWidth < 2 || _hmHeight < 2) return;
        if (strength <= 0f || radiusLocal <= 0f) return;

        float cu = Math.Clamp(localX + 0.5f, 0f, 1f);
        float cv = Math.Clamp(localZ + 0.5f, 0f, 1f);
        float r = Math.Max(1e-4f, radiusLocal);
        float st = Math.Clamp(strength, 0f, 1f);

        int x0 = Math.Max(0, (int)((cu - r) * (_hmWidth - 1)));
        int x1 = Math.Min(_hmWidth - 1, (int)MathF.Ceiling((cu + r) * (_hmWidth - 1)));
        int y0 = Math.Max(0, (int)((cv - r) * (_hmHeight - 1)));
        int y1 = Math.Min(_hmHeight - 1, (int)MathF.Ceiling((cv + r) * (_hmHeight - 1)));

        // Kernel radius grows slightly with the brush so big brushes smooth faster.
        int k = Math.Clamp((int)MathF.Round(r * MathF.Max(_hmWidth, _hmHeight) * 0.12f), 1, 3);

        float invW = _hmWidth > 1 ? 1f / (_hmWidth - 1) : 1f;
        float invH = _hmHeight > 1 ? 1f / (_hmHeight - 1) : 1f;

        // Work on a real snapshot: every pixel averages the ORIGINAL neighborhood (no
        // in-place propagation, which would smear the blur along the scan direction).
        float[] src = (float[])_heights.Clone();

        for (int y = y0; y <= y1; y++)
        {
            float v = y * invH;
            float dv = v - cv;
            for (int x = x0; x <= x1; x++)
            {
                float u = x * invW;
                float du = u - cu;
                float d = MathF.Sqrt(du * du + dv * dv);
                if (d >= r) continue;

                float w = BrushWeight(d, r, 1f, falloffCurve);

                float sum = 0f;
                int count = 0;
                for (int oy = -k; oy <= k; oy++)
                {
                    int sy = Math.Clamp(y + oy, 0, _hmHeight - 1);
                    for (int ox = -k; ox <= k; ox++)
                    {
                        int sx = Math.Clamp(x + ox, 0, _hmWidth - 1);
                        sum += src[sy * _hmWidth + sx];
                        count++;
                    }
                }
                float avg = sum / count;

                int idx = y * _hmWidth + x;
                _heights[idx] = Math.Clamp(src[idx] + (avg - src[idx]) * st * w, 0f, 1f);
            }
        }

        IsModified = true;
        Generate(_lastChunkSize, _lastChunksPerSide, _lastHeightScale, _lastFootprintX, _lastFootprintZ);
    }

    /// <summary>
    /// Flatten one brush stamp: each affected pixel is blended toward <paramref name="targetNorm"/>
    /// (a normalized 0..1 height captured from the first stamp of the stroke, like Unreal's
    /// flatten tool). <paramref name="strength"/> 0..1 is the blend amount per stamp.
    /// </summary>
    public void FlattenHeight(float localX, float localZ, float radiusLocal, float targetNorm, float strength, int falloffCurve)
    {
        if (_heights.Length == 0 || _hmWidth < 2 || _hmHeight < 2) return;
        if (strength <= 0f || radiusLocal <= 0f) return;

        targetNorm = Math.Clamp(targetNorm, 0f, 1f);
        float cu = Math.Clamp(localX + 0.5f, 0f, 1f);
        float cv = Math.Clamp(localZ + 0.5f, 0f, 1f);
        float r = Math.Max(1e-4f, radiusLocal);
        float st = Math.Clamp(strength, 0f, 1f);

        int x0 = Math.Max(0, (int)((cu - r) * (_hmWidth - 1)));
        int x1 = Math.Min(_hmWidth - 1, (int)MathF.Ceiling((cu + r) * (_hmWidth - 1)));
        int y0 = Math.Max(0, (int)((cv - r) * (_hmHeight - 1)));
        int y1 = Math.Min(_hmHeight - 1, (int)MathF.Ceiling((cv + r) * (_hmHeight - 1)));

        float invW = _hmWidth > 1 ? 1f / (_hmWidth - 1) : 1f;
        float invH = _hmHeight > 1 ? 1f / (_hmHeight - 1) : 1f;

        float[] src = (float[])_heights.Clone();
        for (int y = y0; y <= y1; y++)
        {
            float v = y * invH;
            float dv = v - cv;
            for (int x = x0; x <= x1; x++)
            {
                float u = x * invW;
                float du = u - cu;
                float d = MathF.Sqrt(du * du + dv * dv);
                if (d >= r) continue;

                float w = BrushWeight(d, r, 1f, falloffCurve);
                int idx = y * _hmWidth + x;
                _heights[idx] = Math.Clamp(src[idx] + (targetNorm - src[idx]) * st * w, 0f, 1f);
            }
        }

        IsModified = true;
        Generate(_lastChunkSize, _lastChunksPerSide, _lastHeightScale, _lastFootprintX, _lastFootprintZ);
    }

    /// <summary>Snapshot the current height data (for undo).</summary>
    public float[] GetHeightSnapshot() => _heights.Length > 0 ? (float[])_heights.Clone() : [];

    /// <summary>Restore a height snapshot (undo/redo) and rebuild the mesh.</summary>
    public void RestoreHeightSnapshot(float[] heights)
    {
        if (heights == null || heights.Length == 0) return;
        if (heights.Length != _hmWidth * _hmHeight) return;
        _heights = (float[])heights.Clone();
        IsModified = true;
        Generate(_lastChunkSize, _lastChunksPerSide, _lastHeightScale, _lastFootprintX, _lastFootprintZ);
    }

    /// <summary>Return the current (painted) heightmap as a compact binary blob:
    /// [4B width][4B height][w*h bytes 0..255]. Null when the mesh has no user edits.</summary>
    public byte[]? GetModifiedRaw()
    {
        if (!IsModified || _heights.Length == 0) return null;
        var data = new byte[8 + _heights.Length];
        BitConverter.GetBytes(_hmWidth).CopyTo(data, 0);
        BitConverter.GetBytes(_hmHeight).CopyTo(data, 4);
        for (int i = 0; i < _heights.Length; i++)
            data[8 + i] = (byte)Math.Clamp((int)(_heights[i] * 255f), 0, 255);
        return data;
    }

    /// <summary>Restore a blob produced by <see cref="GetModifiedRaw"/> (scene load / rebuild).</summary>
    public void RestoreModifiedRaw(byte[] data)
    {
        if (data == null || data.Length < 9) return;
        int w = BitConverter.ToInt32(data, 0);
        int h = BitConverter.ToInt32(data, 4);
        if (w < 2 || h < 2 || w > 8192 || h > 8192 || 8 + w * h != data.Length) return;
        _hmWidth = w;
        _hmHeight = h;
        _heights = new float[w * h];
        for (int i = 0; i < _heights.Length; i++)
            _heights[i] = data[8 + i] / 255f;
        IsModified = true;
        Generate(_lastChunkSize, _lastChunksPerSide, _lastHeightScale, _lastFootprintX, _lastFootprintZ);
    }

    /// <summary>Write the current (painted) heights back to a .raw heightmap file.</summary>
    public bool SaveHeightmapFile(string path)
    {
        if (string.IsNullOrEmpty(path) || _heights.Length == 0) return false;
        try
        {
            var bytes = new byte[_heights.Length];
            for (int i = 0; i < _heights.Length; i++)
                bytes[i] = (byte)Math.Clamp((int)(_heights[i] * 255f), 0, 255);
            File.WriteAllBytes(path, bytes);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EditorTerrain] Failed to save heightmap '{path}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Set the 4 layer texture paths (air, dirt, grass, snow). Missing/empty paths
    /// fall back to a solid color texture so the layer still renders distinctly.</summary>
    public void SetLayerTextures(string air, string dirt, string grass, string snow)
    {
        // Resolve relative texture paths against the exe folder.
        string[] paths =
        [
            DarkEngine3D_gl_csharp.Engine.Helpers.PathHelpers.Resolve(air),
            DarkEngine3D_gl_csharp.Engine.Helpers.PathHelpers.Resolve(dirt),
            DarkEngine3D_gl_csharp.Engine.Helpers.PathHelpers.Resolve(grass),
            DarkEngine3D_gl_csharp.Engine.Helpers.PathHelpers.Resolve(snow),
        ];
        for (int i = 0; i < 4; i++)
        {
            if (_layerPaths[i] == paths[i]) continue;
            _layerPaths[i] = paths[i];
            if (_layerTextures[i] != 0)
            {
                uint t = _layerTextures[i];
                GL.DeleteTextures(1, &t);
                _layerTextures[i] = 0;
            }
            _layerTextures[i] = LoadLayerTexture(paths[i], FallbackColors[i]);
        }
    }

    private static uint LoadLayerTexture(string path, Vector3 fallback)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try { return new Texture(path).ID; }
            catch { /* fall through to solid */ }
        }
        return CreateSolidTexture(fallback);
    }

    /// <summary>Re-apply the owner's per-layer <see cref="TextureSettings"/> (min/mag
    /// filter, mipmapping, anisotropy, wrapping) to each loaded layer texture and the splat
    /// mask. <paramref name="perLayer"/> has one entry per layer (air, dirt, grass, snow);
    /// the splat mask keeps CLAMP_TO_EDGE (it is a paint-weight map, not a tileable
    /// texture) — only its filters are changed. Called from EditorObject when the
    /// settings change or after a rebuild loads fresh textures.</summary>
    public void ApplyTextureSettings(Libs.TextureSettings[] perLayer)
    {
        if (perLayer == null) return;
        for (int i = 0; i < _layerTextures.Length && i < perLayer.Length; i++)
        {
            if (_layerTextures[i] == 0 || perLayer[i] == null) continue;
            perLayer[i].Apply(_layerTextures[i]);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        }
        if (_splatTex != 0 && perLayer.Length > 0 && perLayer[0] != null)
        {
            GL.BindTexture(Const.GL_TEXTURE_2D, _splatTex);
            bool nearest = perLayer[0].MagFilterGL == Const.GL_NEAREST;
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)(nearest ? Const.GL_NEAREST : Const.GL_LINEAR));
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)perLayer[0].MagFilterGL);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        }
    }

    /// <summary>Create a tiny 1×1 solid-color texture (used when a layer has no image).</summary>
    private static unsafe uint CreateSolidTexture(Vector3 color)
    {
        uint id;
        GL.GenTextures(1, &id);
        GL.BindTexture(Const.GL_TEXTURE_2D, id);
        byte[] px =
        [
            (byte)(Math.Clamp(color.X, 0f, 1f) * 255f),
            (byte)(Math.Clamp(color.Y, 0f, 1f) * 255f),
            (byte)(Math.Clamp(color.Z, 0f, 1f) * 255f),
            255,
        ];
        fixed (byte* p = px)
        {
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, 1, 1, 0,
                          Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, p);
        }
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_REPEAT);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_REPEAT);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
        GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        return id;
    }

    /// <summary>Ensure the editor terrain shader is loaded and cache its uniform locations.</summary>
    private static void EnsureShader()
    {
        if (_program != 0) return;
        _program = Shader.GetEditorTerrainShaderProgram();
        if (_program == 0) return;

        _modelLoc = GL.GetUniformLocation(_program, "model");
        _viewLoc = GL.GetUniformLocation(_program, "view");
        _projLoc = GL.GetUniformLocation(_program, "projection");
        _sunDirLoc = GL.GetUniformLocation(_program, "sunDir");
        _lightColorLoc = GL.GetUniformLocation(_program, "lightColor");
        _viewPosLoc = GL.GetUniformLocation(_program, "viewPos");
        _useFogLoc = GL.GetUniformLocation(_program, "useFog");
        _fogColorLoc = GL.GetUniformLocation(_program, "fogColor");
        _heightScaleLoc = GL.GetUniformLocation(_program, "heightScale");
        _layerLevelsLoc = GL.GetUniformLocation(_program, "layerLevels");
        _slopeThresholdLoc = GL.GetUniformLocation(_program, "slopeThreshold");
        _texTilingLoc = GL.GetUniformLocation(_program, "texTiling");
        _useStochasticSamplingLoc = GL.GetUniformLocation(_program, "useStochasticSampling");
        _usePaintMaskLoc = GL.GetUniformLocation(_program, "usePaintMask");
        _showHeatmapLoc = GL.GetUniformLocation(_program, "showHeatmap");
        _showContoursLoc = GL.GetUniformLocation(_program, "showContours");
        _tex4Loc = GL.GetUniformLocation(_program, "tex4");
        for (int i = 0; i < 4; i++)
            _texLocs[i] = GL.GetUniformLocation(_program, $"tex{i}");

        // CSM shadow uniforms
        _shadowFilterLoc = GL.GetUniformLocation(_program, "shadowFilterMode");
        _shadowDirLoc = GL.GetUniformLocation(_program, "shadowDir");
        _showCSMCascadeColorLoc = GL.GetUniformLocation(_program, "showCSMCascadeColor");
        _shadowMap0Loc = GL.GetUniformLocation(_program, "shadowMap0");
        _shadowMap1Loc = GL.GetUniformLocation(_program, "shadowMap1");
        _shadowMap2Loc = GL.GetUniformLocation(_program, "shadowMap2");
        _lightSpace0Loc = GL.GetUniformLocation(_program, "lightSpaceMatrices[0]");
        _lightSpace1Loc = GL.GetUniformLocation(_program, "lightSpaceMatrices[1]");
        _lightSpace2Loc = GL.GetUniformLocation(_program, "lightSpaceMatrices[2]");
        _cascadeEnds0Loc = GL.GetUniformLocation(_program, "cascadeEnds[0]");
        _cascadeEnds1Loc = GL.GetUniformLocation(_program, "cascadeEnds[1]");
        _cascadeEnds2Loc = GL.GetUniformLocation(_program, "cascadeEnds[2]");
        // Cloud shadow uniform locations
        _cloudAltLoc = GL.GetUniformLocation(_program, "cloudAltitude");
        _cloudSpeedLoc = GL.GetUniformLocation(_program, "cloudSpeed");
        _cloudDetailLoc = GL.GetUniformLocation(_program, "cloudDetail");
        _cloudErosionLoc = GL.GetUniformLocation(_program, "cloudErosion");
        _cloudShadowStrLoc = GL.GetUniformLocation(_program, "cloudShadowStrength");
        _cloudsEnabledLoc = GL.GetUniformLocation(_program, "cloudsEnabled");
        _cloudScaleLoc = GL.GetUniformLocation(_program, "cloudScale");
        _cloudWeatherLoc = GL.GetUniformLocation(_program, "weatherMode");
        _timeCloudLoc = GL.GetUniformLocation(_program, "timeCloud");
    }

    /// <summary>Render the terrain mesh with the editor terrain shader. When
    /// <paramref name="csm"/> is provided the terrain receives CSM shadows from the
    /// editor viewport shadow pass (bound to texture units 6/7/8).</summary>
    public void Draw(Matrix4x4 model, Camera camera, Lights light, EditorObject owner, CSM? csm = null)
    {
        if (!_gpuReady) return;
        EnsureShader();
        if (_program == 0) return;

        GL.UseProgram(_program);

        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix();
        GL.UniformMatrix4fv(_modelLoc, 1, false, (float*)&model);
        GL.UniformMatrix4fv(_viewLoc, 1, false, (float*)&view);
        GL.UniformMatrix4fv(_projLoc, 1, false, (float*)&proj);

        GL.Uniform3f(_sunDirLoc, light.SunDir.X, light.SunDir.Y, light.SunDir.Z);
        GL.Uniform3f(_lightColorLoc, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
        GL.Uniform3f(_viewPosLoc, camera.Position.X, camera.Position.Y, camera.Position.Z);

        // ── Fog (enable, mode, color, density, start/end, height — Config.FogSettings) ──
        Visual.FogUniforms.UploadMain(_program, light);
        if (_showCSMCascadeColorLoc >= 0)
            GL.Uniform1i(_showCSMCascadeColorLoc, Inputs.Keyboard.GetshowCSMCascadeColor() ? 1 : 0);

        // ── Local point/spot lights (from editor Light markers) ──
        light.UploadLocalLights(_program);

        // Live shadow bias / blend tuning (Shadow Settings panel) — terrain-editor shader
        // uses the same uniform names as the main shader.
        Visual.ShadowUniforms.UploadMain(_program);

        // ── CSM shadow uniforms (viewport shadow pass → units 6/7/8) ──
        if (csm != null)
        {
            if (_shadowFilterLoc >= 0) GL.Uniform1i(_shadowFilterLoc, Inputs.Keyboard.GetIsHardShadow());
            if (_shadowDirLoc >= 0) GL.Uniform3f(_shadowDirLoc, light.ShadowDirStable.X, light.ShadowDirStable.Y, light.ShadowDirStable.Z);

            unsafe
            {
                fixed (float* p0 = &csm.LightSpaceMatrices[0].M11)
                    GL.UniformMatrix4fv(_lightSpace0Loc, 1, false, p0);
                fixed (float* p1 = &csm.LightSpaceMatrices[1].M11)
                    GL.UniformMatrix4fv(_lightSpace1Loc, 1, false, p1);
                fixed (float* p2 = &csm.LightSpaceMatrices[2].M11)
                    GL.UniformMatrix4fv(_lightSpace2Loc, 1, false, p2);
            }
            GL.Uniform1f(_cascadeEnds0Loc, csm.CascadeEnds[0]);
            GL.Uniform1f(_cascadeEnds1Loc, csm.CascadeEnds[1]);
            GL.Uniform1f(_cascadeEnds2Loc, csm.CascadeEnds[2]);

            GL.Uniform1i(_shadowMap0Loc, 6);
            GL.Uniform1i(_shadowMap1Loc, 7);
            GL.Uniform1i(_shadowMap2Loc, 8);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 6);
            GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[0]);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 7);
            GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[1]);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 8);
            GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[2]);
            GL.ActiveTexture(Const.GL_TEXTURE0);
        }

        // ── Upload cloud shadow uniforms ──
        var skyClouds = EditorObject.ActiveSkySettings?.Clouds;
        if (skyClouds != null)
        {
            GL.Uniform1f(_cloudAltLoc, skyClouds.Altitude);
            GL.Uniform1f(_cloudSpeedLoc, skyClouds.Speed);
            GL.Uniform1f(_cloudDetailLoc, skyClouds.Detail);
            GL.Uniform1f(_cloudErosionLoc, skyClouds.Erosion);
            GL.Uniform1f(_cloudShadowStrLoc, skyClouds.ShadowStrength);
            GL.Uniform1f(_cloudScaleLoc, skyClouds.CloudScale);
            GL.Uniform1f(_cloudsEnabledLoc, skyClouds.Enabled ? 1f : 0f);
            float weatherVal = Inputs.Keyboard.GetCurrentWeather();
            GL.Uniform1f(_cloudWeatherLoc, weatherVal);
            _cloudTimeAccum += 0.016f; // approximate 60fps increment
            GL.Uniform1f(_timeCloudLoc, _cloudTimeAccum);
        }
        else
        {
            GL.Uniform1f(_cloudsEnabledLoc, 0f);
        }

        float heightScale = Math.Max(1f, owner.TerrainHeightScale);
        GL.Uniform3f(_heightScaleLoc, heightScale, 0f, 0f);
        GL.Uniform4f(_layerLevelsLoc,
            owner.TerrainLayerAirTop,
            owner.TerrainLayerDirtTop,
            owner.TerrainLayerGrassTop,
            owner.TerrainLayerSnowTop);
        GL.Uniform1f(_slopeThresholdLoc, Math.Clamp(owner.TerrainSlopeThreshold, 0.02f, 0.98f));
        GL.Uniform1f(_texTilingLoc, Math.Max(0.01f, owner.TerrainTexTiling));
        GL.Uniform1i(_useStochasticSamplingLoc, owner.TerrainUseStochasticSampling ? 1 : 0);

        uint[] units = [Const.GL_TEXTURE0, Const.GL_TEXTURE1, Const.GL_TEXTURE2, Const.GL_TEXTURE3];
        for (int i = 0; i < 4; i++)
        {
            GL.ActiveTexture(units[i]);
            GL.BindTexture(Const.GL_TEXTURE_2D, _layerTextures[i]);
            GL.Uniform1i(_texLocs[i], i);
        }

        // ── Manual layer-paint splat map (unit 4) ──
        // Create + upload only once (first use); later mutations re-upload via
        // EnsureSplatTexture() so a static splat never costs a per-frame 64KB upload.
        if (_splatTex == 0)
            EnsureSplatTexture();
        GL.Uniform1i(_usePaintMaskLoc, SplatModified ? 1 : 0);
        GL.Uniform1i(_showHeatmapLoc, owner.TerrainShowHeatmap ? 1 : 0);
        GL.Uniform1i(_showContoursLoc, owner.TerrainShowContours ? 1 : 0);
        GL.ActiveTexture(Const.GL_TEXTURE4);
        GL.BindTexture(Const.GL_TEXTURE_2D, _splatTex);
        GL.Uniform1i(_tex4Loc, 4);

        // Draw every chunk as quads (2 triangles each, via the index buffer).
        foreach (var c in _chunks)
        {
            GL.BindVertexArray(c.Vao);
            GL.DrawElements(Const.GL_TRIANGLES, c.IndexCount, Const.GL_UNSIGNED_INT, null);
        }
        GL.BindVertexArray(0);

        GL.ActiveTexture(Const.GL_TEXTURE0);
    }

    /// <summary>Render the terrain mesh into a CSM shadow map (depth-only pass). Uses the
    /// shared static shadow shader — the terrain VAO's layout (position at 0, normal at 1)
    /// matches shadow_vertex.glsl exactly. The model matrix keeps XZ from the object's
    /// Scale while Y stays real world height (same as the main draw pass).</summary>
    public void RenderShadow(Matrix4x4 model, CSM csm, int cascadeIndex)
    {
        if (!_gpuReady || _chunks.Count == 0) return;

        uint shadowShader = Shader.GetShadowShaderProgram();
        GL.UseProgram(shadowShader);

        // Live normal-bias tuning (Shadow Settings panel) — static shadow shader. A
        // heightmap terrain is a huge ground surface (default 25×25 footprint with up to
        // 30 units of relief), so its shadow-map texels cover far more world space than
        // unit-sized editor primitives — the standard extrusion is not enough to keep
        // steep slopes acne-free. Boost by how much larger this terrain is than the
        // baseline; default-sized terrains keep the consistent base value.
        float sx = new Vector3(model.M11, model.M21, model.M31).Length();
        float sz = new Vector3(model.M13, model.M23, model.M33).Length();
        float footprint = MathF.Max(sx, sz);
        float boost = MathF.Min(
            MathF.Max(1f, MathF.Max(footprint / 25f, _lastHeightScale / 30f)), 6f);
        // The extrusion now happens in WORLD space (after the model transform, see
        // shadow_vertex.glsl), so u_NormalBias is directly in world units — no footprint
        // division needed. A fixed world offset applies uniformly to XZ and Y, matching
        // the game terrain's identity-model behavior.
        Visual.ShadowUniforms.UploadNormalBias(shadowShader,
            ShadowSettings.NormalBias * boost);

        var lightSpace = csm.LightSpaceMatrices[cascadeIndex];
        GL.UniformMatrix4fv(GL.GetUniformLocation(shadowShader, "model"), 1, false, (float*)&model);
        GL.UniformMatrix4fv(GL.GetUniformLocation(shadowShader, "lightSpaceMatrix"), 1, false, (float*)&lightSpace);

        // The terrain's model matrix carries non-uniform XZ scale (footprint) and rotation,
        // so the shadow shader's normal-bias extrusion needs the inverse-transpose to stay
        // on the true surface normal — otherwise faces push INTO the shadow map.
        Visual.ShadowUniforms.UploadShadowNormalMatrix(shadowShader, model);

        // Draw every chunk as quads (2 triangles each, via the index buffer).
        foreach (var c in _chunks)
        {
            GL.BindVertexArray(c.Vao);
            GL.DrawElements(Const.GL_TRIANGLES, c.IndexCount, Const.GL_UNSIGNED_INT, null);
        }
        GL.BindVertexArray(0);

        // Restore the standard normal bias so subsequent objects drawn in this cascade pass
        // don't inherit the terrain's boosted value (the shadow program is shared and the
        // uniform value persists between draws).
        Visual.ShadowUniforms.UploadNormalBias(shadowShader);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Manual layer paint (splat map) — 🎨 brush
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Create (or recreate) the splat texture on the GPU and upload the current
    /// splat bytes. Lazily called on first draw / first paint.</summary>
    private void EnsureSplatTexture()
    {
        if (_splatTex != 0)
        {
            // Re-upload the current data (full overwrite is fine: 128² RGBA = 64KB).
            GL.BindTexture(Const.GL_TEXTURE_2D, _splatTex);
            fixed (byte* p = _splat)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, _splatDim, _splatDim, 0,
                              Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, p);
            }
            return;
        }

        uint id;
        GL.GenTextures(1, &id);
        _splatTex = id;
        GL.BindTexture(Const.GL_TEXTURE_2D, _splatTex);
        fixed (byte* p = _splat)
        {
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, _splatDim, _splatDim, 0,
                          Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, p);
        }
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
        GL.BindTexture(Const.GL_TEXTURE_2D, 0);
    }

    /// <summary>
    /// Paint one brush stamp into the splat map. <paramref name="layerIndex"/> is 0..3
    /// (air/dirt/grass/snow). <paramref name="strength"/> is the weight added per stamp
    /// (0..1); when <paramref name="erase"/> is true the same strength decays ALL painted
    /// weights back toward zero (reverting to automatic height+slope layers).
    /// Coordinates are LOCAL space (X/Z in [-0.5, 0.5], radius in local units).
    /// </summary>
    public void PaintLayer(float localX, float localZ, float radiusLocal, int layerIndex, float strength, float softness, int falloffCurve, bool erase)
    {
        if (_splat.Length == 0) return;
        layerIndex = Math.Clamp(layerIndex, 0, 3);
        float r = Math.Max(1e-4f, radiusLocal);
        float addAmt = Math.Clamp(strength, 0f, 1f) * 255f;
        float decay = Math.Max(0f, 1f - Math.Clamp(strength, 0f, 1f) * 1.2f);

        // Local [-0.5, 0.5] → texel space.
        float scale = _splatDim - 1;
        float cx = (localX + 0.5f) * scale;
        float cz = (localZ + 0.5f) * scale;
        float tr = r * scale;

        int x0 = Math.Max(0, (int)(cx - tr));
        int x1 = Math.Min(_splatDim - 1, (int)MathF.Ceiling(cx + tr));
        int z0 = Math.Max(0, (int)(cz - tr));
        int z1 = Math.Min(_splatDim - 1, (int)MathF.Ceiling(cz + tr));

        bool changed = false;
        for (int z = z0; z <= z1; z++)
        {
            float dz = z - cz;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - cx;
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d >= tr) continue;
                float w = BrushWeight(d, tr, softness, falloffCurve);

                int idx = (z * _splatDim + x) * 4;
                if (erase)
                {
                    float f = MathF.Pow(decay, w);
                    for (int c = 0; c < 4; c++)
                    {
                        byte old = _splat[idx + c];
                        byte nv = (byte)(old * f);
                        if (nv != old) changed = true;
                        _splat[idx + c] = nv;
                    }
                }
                else
                {
                    int v = _splat[idx + layerIndex] + (int)(addAmt * w);
                    byte nv = (byte)Math.Min(255, v);
                    if (nv != _splat[idx + layerIndex]) changed = true;
                    _splat[idx + layerIndex] = nv;
                }
            }
        }

        // Only mark the terrain as painted when something actually changed — an eraser
        // stroke on a clean splat must not flag the terrain (or bloat the scene file).
        if (!changed) return;
        SplatModified = true;
        EnsureSplatTexture();
    }

    /// <summary>Clear ALL manual layer paint (back to automatic height+slope texturing).</summary>
    public void ClearSplatPaint()
    {
        if (_splat.Length == 0) return;
        Array.Clear(_splat);
        SplatModified = false;
        if (_splatTex != 0)
            EnsureSplatTexture();
    }

    /// <summary>Snapshot the splat bytes (undo support).</summary>
    public byte[] GetSplatSnapshot() => _splat.Length > 0 ? (byte[])_splat.Clone() : [];

    /// <summary>Restore a splat snapshot (undo/redo) and upload it.</summary>
    public void RestoreSplatSnapshot(byte[] splat)
    {
        if (splat == null || splat.Length != _splat.Length) return;
        _splat = (byte[])splat.Clone();
        SplatModified = true;
        EnsureSplatTexture();
    }

    /// <summary>Serialized splat blob: [4B size][size*size*4 RGBA bytes]. Null when clean.</summary>
    public byte[]? GetModifiedSplatRaw()
    {
        if (!SplatModified || _splat.Length == 0) return null;
        var data = new byte[4 + _splat.Length];
        BitConverter.GetBytes(_splatDim).CopyTo(data, 0);
        _splat.CopyTo(data, 4);
        return data;
    }

    /// <summary>Restore a splat blob produced by <see cref="GetModifiedSplatRaw"/>.</summary>
    public void RestoreModifiedSplatRaw(byte[] data)
    {
        if (data == null || data.Length < 8) return;
        int size = BitConverter.ToInt32(data, 0);
        if (size < 2 || size > 1024 || 4 + size * size * 4 != data.Length) return;
        _splatDim = size;
        _splat = new byte[size * size * 4];
        Array.Copy(data, 4, _splat, 0, _splat.Length);
        SplatModified = true;
        if (_splatTex != 0)
            EnsureSplatTexture();
    }

    public void Dispose()
    {
        foreach (var c in _chunks)
        {
            if (c.Vao != 0) { uint v = c.Vao; GL.DeleteVertexArrays(1, &v); }
            if (c.Vbo != 0) { uint b = c.Vbo; GL.DeleteBuffers(1, &b); }
            if (c.Ebo != 0) { uint b = c.Ebo; GL.DeleteBuffers(1, &b); }
        }
        _chunks.Clear();
        if (_splatTex != 0)
        {
            uint t = _splatTex;
            GL.DeleteTextures(1, &t);
            _splatTex = 0;
        }
        for (int i = 0; i < 4; i++)
        {
            if (_layerTextures[i] != 0)
            {
                uint t = _layerTextures[i];
                GL.DeleteTextures(1, &t);
                _layerTextures[i] = 0;
            }
            _layerPaths[i] = "";
        }
        _gpuReady = false;
        GC.SuppressFinalize(this);
    }
}
