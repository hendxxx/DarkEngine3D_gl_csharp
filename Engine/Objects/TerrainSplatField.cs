using System.IO;
using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Libs;

namespace DarkEngine3D_gl_csharp.Engine.Objects;

/// <summary>Splat paint operation applied to the RGBA weight field.</summary>
public enum SplatBrushMode
{
    /// <summary>Add weight for the selected layer (the others give way, sum stays 1).</summary>
    Paint = 0,
    /// <summary>Remove weight from the selected layer (others renormalize up).</summary>
    Erase = 1,
    /// <summary>Relax weight differences toward the local average (softens paint edges).</summary>
    Smooth = 2,
}

/// <summary>Live brush-session settings for splat painting. The Terrain panel edits
/// these; the viewport paints through them while a stroke is held. Lives on
/// <see cref="EditorObject.SplatBrush"/> — null = not painting.</summary>
public sealed class SplatBrushSession
{
    /// <summary>Current paint operation (Paint/Erase/Smooth).</summary>
    public SplatBrushMode Mode;
    /// <summary>Target texture layer 0..3 the Paint/Erase ops modify.</summary>
    public int Layer;
    /// <summary>Brush radius in world units.</summary>
    public float Radius = 5f;
    /// <summary>Application speed (fraction of full weight per second at the core).</summary>
    public float Strength = 1.5f;
    /// <summary>Falloff softness: 1 = fully soft (smoothstep to the rim), 0 = hard disc.</summary>
    public float Hardness = 0.5f;
}

/// <summary>
/// CPU terrain SPLAT weight field (256² RGBA, weights sum to 1 per texel) — the
/// texture-blend counterpart of <see cref="TerrainHeightfield"/>. One RGBA channel
/// per texture layer; the fragment shader blends 4 albedo/PBR layer sets by these
/// weights. Brush strokes mutate this CPU buffer which uploads to a live GL_RGBA8
/// texture bound at unit 10, exactly like the sculpt field replaces unit 15.
///
/// Two weight sources COMBINE in the shader (max-blend):
///   • manual brush paint (this field), and
///   • AUTO height bands — smoothstep bands over the sculpted elevation, recomputed
///     CPU-side from the SAME <see cref="TerrainHeightfield"/> the vertex stage
///     displaces with, so valley floors pick layer 0 and peaks pick the top layer.
/// Manual paint always wins over the bands.
///
/// Persistence is FILE-BASED like the sculpt: painting bakes the field to a 32-bit
/// RGBA TGA under <c>Artifacts/Terrain/&lt;scene&gt;/&lt;object&gt;_splat.tga</c> and the
/// path is stored on the scene object, so painted textures save/load with the scene.
/// </summary>
public sealed class TerrainSplatField
{
    /// <summary>Fixed splat resolution (256² × 4 floats ≈ 1 MB CPU, 256 KB GPU).</summary>
    public const int Res = 256;

    private readonly float[] _w = new float[Res * Res * 4];  // RGBA weights, 0..1
    private readonly byte[] _upload = new byte[Res * Res * 4];
    private uint _tex;

    // Dirty rectangle accumulated since the last FlushTexture (texel bounds).
    private int _minX = int.MaxValue, _maxX = int.MinValue;
    private int _minZ = int.MaxValue, _maxZ = int.MinValue;

    // Smooth-mode guard: repeated stamps within one drag frame would re-average
    // cumulatively (same trick as the heightfield's Smooth/Flatten guard).
    private int _avgStamp = -1;
    private float _avgStampValue;

    // ── Per-stroke undo/redo (snapshot = 8-bit RGBA copy, 256 KB each) ──
    public const int MaxHistory = 40;
    private readonly List<byte[]> _undoStrokes = new();
    private readonly List<byte[]> _redoStrokes = new();
    private bool _strokeOpen;

    public int UndoDepth => _undoStrokes.Count;
    public int RedoDepth => _redoStrokes.Count;

    /// <summary>Call at stroke START (mouse press) — the snapshot is taken lazily
    /// by the first stamp that mutates the field (no-op strokes cost nothing).</summary>
    public void BeginStroke()
    {
        _strokeOpen = false;
        StrokeTexels = 0;
    }

    private byte[] SnapshotRGBA()
    {
        var b = new byte[Res * Res * 4];
        for (int i = 0; i < b.Length; i++)
            b[i] = (byte)(Math.Clamp(_w[i], 0f, 1f) * 255f + 0.5f);
        return b;
    }

    private void Restore(byte[] b)
    {
        for (int i = 0; i < b.Length; i++)
            _w[i] = b[i] / 255f;
        MarkWholeDirty();
        NeedsBake = true;
    }

    private void MarkWholeDirty()
    {
        _minX = 0; _maxX = Res - 1;
        _minZ = 0; _maxZ = Res - 1;
    }

    /// <summary>Revert the last stroke (Ctrl+Z). Caller must FlushTexture().</summary>
    public bool Undo()
    {
        if (_undoStrokes.Count == 0) return false;
        _redoStrokes.Add(SnapshotRGBA());
        Restore(_undoStrokes[^1]);
        _undoStrokes.RemoveAt(_undoStrokes.Count - 1);
        return true;
    }

    /// <summary>Re-apply the last undone stroke (Ctrl+Y / Ctrl+Shift+Z).</summary>
    public bool Redo()
    {
        if (_redoStrokes.Count == 0) return false;
        _undoStrokes.Add(SnapshotRGBA());
        if (_undoStrokes.Count > MaxHistory) _undoStrokes.RemoveAt(0);
        Restore(_redoStrokes[^1]);
        _redoStrokes.RemoveAt(_redoStrokes.Count - 1);
        return true;
    }

    /// <summary>True once ANY brush stamp has modified this field.</summary>
    public bool HasAnyEdits { get; private set; }

    /// <summary>Texels mutated during the CURRENT stroke (reset in BeginStroke,
    /// accumulated per stamp) — stroke-end telemetry: 0 = the stamp never landed
    /// (pick/coord problem), large = the field IS written (any invisibility is
    /// a GPU/gate problem).</summary>
    public int StrokeTexels { get; private set; }

    /// <summary>True when the height-band auto weights were computed over the
    /// sculpted elevation (splat is ACTIVE even without manual paint).</summary>
    public bool HasBands { get; private set; }

    /// <summary>Edits accumulated since the last finished stroke — only then does
    /// the stroke-end path flush + bake.</summary>
    public bool NeedsBake { get; private set; }

    /// <summary>Clear the per-stroke bake flag (after a successful TGA bake).</summary>
    public void MarkBaked() => NeedsBake = false;

    /// <summary>Frame stamp of the last stroke this field confirmed (bake-once guard).</summary>
    public int LastStrokeStamp { get; set; } = -1;

    private TerrainSplatField() { }

    /// <summary>An empty field: every texel = layer 0 full weight (sums to 1).
    /// Used when painting starts without a splat file or height bands.</summary>
    public static TerrainSplatField CreateDefault()
    {
        var f = new TerrainSplatField();
        for (int i = 0; i < Res * Res; i++)
            f._w[i * 4] = 1f;
        return f;
    }

    /// <summary>Decode a 32-bit RGBA splat TGA/PNG (weights per channel) into the
    /// field. Channels are renormalized per texel so the sum is 1 (a fully black
    /// texel — no stored weights — becomes layer 0). Null on failure.</summary>
    public static TerrainSplatField? FromFile(string resolvedPath)
    {
        try
        {
            using var fs = File.OpenRead(resolvedPath);
            var img = StbImageSharp.ImageResult.FromStream(fs, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if (img.Width <= 0 || img.Height <= 0 || img.Data == null)
                return null;

            var field = new TerrainSplatField();
            int srcComp = (int)img.SourceComp;
            for (int z = 0; z < Res; z++)
            {
                int sy = Math.Clamp(z * img.Height / Res, 0, img.Height - 1);
                int rowBase = sy * img.Width;
                for (int x = 0; x < Res; x++)
                {
                    int sx = Math.Clamp(x * img.Width / Res, 0, img.Width - 1);
                    int o = (rowBase + sx) * srcComp;
                    float r = img.Data[o] / 255f;
                    float g = srcComp > 1 ? img.Data[o + 1] / 255f : 0f;
                    float b = srcComp > 2 ? img.Data[o + 2] / 255f : 0f;
                    float a = srcComp > 3 ? img.Data[o + 3] / 255f : 0f;
                    float sum = r + g + b + a;
                    int idx = (z * Res + x) * 4;
                    if (sum > 0.001f)
                    {
                        field._w[idx] = r / sum;
                        field._w[idx + 1] = g / sum;
                        field._w[idx + 2] = b / sum;
                        field._w[idx + 3] = a / sum;
                    }
                    else
                    {
                        field._w[idx] = 1f;   // empty texel → layer 0
                    }
                }
            }
            field.HasAnyEdits = true;   // a loaded splat IS a paint (drives the gates)
            return field;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TerrainSplat] Failed to decode '{resolvedPath}': {ex.Message}");
            return null;
        }
    }

    // ── GPU texture (GL_RGBA8, live-updated per brush stroke) ──

    private uint EnsureTexture()
    {
        if (_tex != 0) return _tex;
        unsafe
        {
            uint t = 0;
            GL.GenTextures(1, &t);
            _tex = t;
            UploadWhole();
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_REPEAT);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_REPEAT);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
        }
        return _tex;
    }

    private void UploadWhole()
    {
        for (int i = 0; i < Res * Res; i++)
        {
            _upload[i * 4] = (byte)(Math.Clamp(_w[i * 4], 0f, 1f) * 255f + 0.5f);
            _upload[i * 4 + 1] = (byte)(Math.Clamp(_w[i * 4 + 1], 0f, 1f) * 255f + 0.5f);
            _upload[i * 4 + 2] = (byte)(Math.Clamp(_w[i * 4 + 2], 0f, 1f) * 255f + 0.5f);
            _upload[i * 4 + 3] = (byte)(Math.Clamp(_w[i * 4 + 3], 0f, 1f) * 255f + 0.5f);
        }
        unsafe
        {
            fixed (byte* p = _upload)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA8, Res, Res, 0,
                    Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, p);
            }
        }
    }

    /// <summary>Upload the accumulated dirty region (or the whole field when bands
    /// were recomputed) to the GPU. Call after each brush application.</summary>
    public bool FlushTexture()
    {
        if (_minZ > _maxZ) return false;
        uint tex = EnsureTexture();
        unsafe
        {
            if (_minX <= 0 && _maxX >= Res - 1 && _minZ <= 0 && _maxZ >= Res - 1)
            {
                UploadWhole();
            }
            else
            {
                int w = _maxX - _minX + 1, h = _maxZ - _minZ + 1;
                // Refresh the byte mirror for the dirty rect from the float field
                // FIRST — _upload is otherwise only written on texture creation,
                // so stamps after the first would upload STALE bytes (invisible
                // paint). Same fix as TerrainHeightfield.FlushTexture.
                for (int z = _minZ; z <= _maxZ; z++)
                    for (int x = _minX; x <= _maxX; x++)
                    {
                        int idx = (z * Res + x) * 4;
                        for (int c = 0; c < 4; c++)
                            _upload[idx + c] = (byte)(Math.Clamp(_w[idx + c], 0f, 1f) * 255f + 0.5f);
                    }
                // Serialize the sub-rectangle rows contiguously (TexSubImage2D wants
                // a packed block; the main buffer is row-pitched RGBA).
                byte[] buf = new byte[w * h * 4];
                for (int z = 0; z < h; z++)
                    for (int x = 0; x < w; x++)
                        for (int c = 0; c < 4; c++)
                            buf[(z * w + x) * 4 + c] = _upload[((_minZ + z) * Res + _minX + x) * 4 + c];
                fixed (byte* p = buf)
                {
                    GL.BindTexture(Const.GL_TEXTURE_2D, tex);
                    GL.TexSubImage2D(Const.GL_TEXTURE_2D, 0, _minX, _minZ, w, h,
                        Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, p);
                }
            }
        }
        _minX = int.MaxValue; _maxX = int.MinValue;
        _minZ = int.MaxValue; _maxZ = int.MinValue;
        return true;
    }

    public void DisposeTexture()
    {
        if (_tex != 0)
        {
            unsafe
            {
                uint t = _tex;
                GL.DeleteTextures(1, &t);
            }
            _tex = 0;
        }
    }

    /// <summary>Fraction of texels where layers 1-3 carry more than half the weight
    /// (0 = uniformly layer 0 — nothing painted).</summary>
    public float PaintCoverage()
    {
        int painted = 0;
        for (int i = 0; i < Res * Res; i++)
        {
            int idx = i * 4;
            if (_w[idx + 1] + _w[idx + 2] + _w[idx + 3] > 0.5f) painted++;
        }
        return painted / (float)(Res * Res);
    }

    /// <summary>Live GPU texture id (0 until the first flush after an edit).</summary>
    public uint GpuTexture => _tex;

    // ── CPU sampling ──

    /// <summary>Bilinear RGBA weight sample in mesh UV (0..1) — used by the debug
    /// weight-map overlay and tests. Weights sum to 1 per texel by construction.</summary>
    public Vector4 SampleWeights(float u, float v)
    {
        float fx = Math.Clamp(u, 0f, 1f) * Res - 0.5f;
        float fy = Math.Clamp(v, 0f, 1f) * Res - 0.5f;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float ax = fx - x0, ay = fy - y0;
        var acc = Vector4.Zero;
        for (int dz = 0; dz <= 1; dz++)
        {
            for (int dx = 0; dx <= 1; dx++)
            {
                int tx = Math.Clamp(x0 + dx, 0, Res - 1);
                int tz = Math.Clamp(y0 + dz, 0, Res - 1);
                float w = (dx == 0 ? 1f - ax : ax) * (dz == 0 ? 1f - ay : ay);
                int idx = (tz * Res + tx) * 4;
                acc += new Vector4(_w[idx], _w[idx + 1], _w[idx + 2], _w[idx + 3]) * w;
            }
        }
        return acc;
    }

    // ── AUTO height bands (weights from the sculpted elevation) ──

    /// <summary>Recompute AUTO layer weights from the sculpted elevation field —
    /// the "driven by terrain height" half of the splat system. Each layer i owns a
    /// world-elevation band (lo[i]..hi[i]); its weight = smoothstep ramp into the
    /// band minus a smoothstep ramp out (a soft plateau), scaled by the layer's
    /// per-layer presence flag passed as cap[i] (0 = no texture in that slot — the
    /// band must not show white).</summary>
    /// <param name="hf">The BASE elevation field (the same one the vertex stage's
    /// base term displaces with). Null clears the bands (manual paint remains).</param>
    /// <param name="delta">Optional ADDITIVE sculpt delta (0.5-neutral) — the bands
    /// must follow the COMBINED sculpted surface: base + (delta−0.5)·2·amp.</param>
    /// <param name="deltaAmp">World amplitude of the delta term.</param>
    /// <param name="baseHeight">TerrainBaseHeight — raw × this = world elevation.</param>
    /// <param name="offset">TerrainHeightOffset (world units, geometry-only shift).</param>
    /// <param name="tilingX">Terrain elevation tiling X — band sampling MUST map
    /// the elevation through the SAME tiling the vertex stage displaces with, or
    /// the weights miss the geometry they are supposed to follow.</param>
    /// <param name="tilingY">Terrain elevation tiling Y.</param>
    /// <param name="bands">Per layer: X = band low edge, Y = band high edge (world).</param>
    /// <param name="caps">Per layer 0/1: texture present in the layer slot.</param>
    /// <param name="feather">Band edge softness in world units (0 = hard edge).</param>
    /// <param name="layerCount">Layers 1..4 that take part (the rest stay weight-0).</param>
    public void ComputeHeightBands(TerrainHeightfield? hf, float baseHeight, float offset,
        float tilingX, float tilingY, ReadOnlySpan<Vector2> bands, ReadOnlySpan<float> caps,
        float feather, int layerCount, TerrainHeightfield? delta = null, float deltaAmp = 0f)
    {
        HasBands = hf != null;
        if (hf == null) return;
        float bh = Math.Clamp(baseHeight, 0f, 500f);
        float fe = Math.Max(feather, 0.001f);
        int layers = Math.Clamp(layerCount, 1, 4);
        for (int z = 0; z < Res; z++)
        {
            for (int x = 0; x < Res; x++)
            {
                // RAW base elevation (the unit-15 read, through the terrain's OWN
                // tiling) + the additive sculpt delta → world — mirrors the vertex
                // stage's displaceWorld (base + delta terms) exactly.
                float elev = hf.SampleBilinear((x + 0.5f) / Res, (z + 0.5f) / Res,
                    Math.Clamp(tilingX, 0.01f, 100f), Math.Clamp(tilingY, 0.01f, 100f))
                    * bh + offset;
                if (delta != null && deltaAmp > 0f)
                    elev += (delta.SampleBilinear((x + 0.5f) / Res, (z + 0.5f) / Res,
                        Math.Clamp(tilingX, 0.01f, 100f), Math.Clamp(tilingY, 0.01f, 100f)) - 0.5f)
                        * 2f * deltaAmp;
                var band = Vector4.Zero;
                float best = 0f;
                for (int l = 0; l < layers; l++)
                {
                    if (caps[l] <= 0f) continue;
                    float lo = bands[l].X, hi = bands[l].Y;
                    // Rising ramp over [lo, lo+feather], falling ramp over [hi-feather, hi].
                    float wIn = Math.Clamp((elev - lo) / fe, 0f, 1f);
                    float wOut = Math.Clamp((hi - elev) / fe, 0f, 1f);
                    wIn = wIn * wIn * (3f - 2f * wIn);
                    wOut = wOut * wOut * (3f - 2f * wOut);
                    float w = Math.Clamp(wIn * wOut, 0f, 1f);
                    if (w > best) best = w;   // strongest band owns the texel (sum ≤ 1)
                    band[l] = w;
                }
                // Normalize the winning band(s) so weights sum to 1 (shader-side this
                // keeps the blend energy-conserving; a fully-uncovered texel → layer 0).
                float sum = band.X + band.Y + band.Z + band.W;
                int idx = (z * Res + x) * 4;
                if (sum > 0.001f)
                {
                    _w[idx] = band.X / sum;
                    _w[idx + 1] = band.Y / sum;
                    _w[idx + 2] = band.Z / sum;
                    _w[idx + 3] = band.W / sum;
                }
                else
                {
                    _w[idx] = 1f;
                    _w[idx + 1] = _w[idx + 2] = _w[idx + 3] = 0f;
                }
            }
        }
        MarkWholeDirty();
        NeedsBake = true;
    }

    // ── Brush ops ──

    /// <summary>Apply one brush stamp. Coordinates are PLANE-LOCAL world units
    /// (same convention as <see cref="TerrainHeightfield.ApplyBrush"/> — origin at
    /// the plane center, the plane spans spanX × spanZ). Stamps are frame-batched
    /// via <paramref name="frameStamp"/> (Smooth averages once per stamp).</summary>
    public void ApplyBrush(float localX, float localZ, float spanX, float spanZ,
        SplatBrushSession b, float frameDt, int frameStamp)
    {
        if (b.Radius <= 0f || spanX <= 0f || spanZ <= 0f) return;
        int layer = Math.Clamp(b.Layer, 0, 3);
        float dt = Math.Clamp(frameDt, 0f, 0.05f);

        int rx = (int)MathF.Ceiling(b.Radius / spanX * Res) + 1;
        int rz = (int)MathF.Ceiling(b.Radius / spanZ * Res) + 1;
        float cx = (localX / spanX + 0.5f) * Res - 0.5f;
        float cz = (localZ / spanZ + 0.5f) * Res - 0.5f;
        int tx0 = Math.Clamp((int)cx - rx, 0, Res - 1);
        int tx1 = Math.Clamp((int)cx + rx + 1, 0, Res - 1);
        int tz0 = Math.Clamp((int)cz - rz, 0, Res - 1);
        int tz1 = Math.Clamp((int)cz + rz + 1, 0, Res - 1);
        if (tx1 < tx0 || tz1 < tz0) return;

        // Lazily take the PRE-STROKE snapshot on the first mutating stamp.
        if (!_strokeOpen)
        {
            _undoStrokes.Add(SnapshotRGBA());
            if (_undoStrokes.Count > MaxHistory) _undoStrokes.RemoveAt(0);
            _redoStrokes.Clear();
            _strokeOpen = true;
        }

        float inner = Math.Clamp(b.Hardness, 0f, 1f);

        // Smooth-mode target: the average of the SELECTED layer over the WHOLE
        // footprint — one value per stamp (guard: repeated stamps within one drag
        // frame would otherwise re-average cumulatively).
        if (b.Mode == SplatBrushMode.Smooth && frameStamp != _avgStamp)
        {
            float sum = 0f; int n = 0;
            for (int z = tz0; z <= tz1; z++)
                for (int x = tx0; x <= tx1; x++)
                {
                    float dxw2 = (x - cx) / Res * spanX;
                    float dzw2 = (z - cz) / Res * spanZ;
                    if (dxw2 * dxw2 + dzw2 * dzw2 <= b.Radius * b.Radius)
                    {
                        sum += _w[(z * Res + x) * 4 + layer];
                        n++;
                    }
                }
            _avgStampValue = n > 0 ? sum / n : 0f;
            _avgStamp = frameStamp;
        }

        for (int z = tz0; z <= tz1; z++)
        {
            for (int x = tx0; x <= tx1; x++)
            {
                // Distance from the BRUSH CENTER (cx/cz are texel units — the same
                // fix as TerrainHeightfield; the old edge/center mix made every
                // stamp miss its own window).
                float dxw = (x - cx) / Res * spanX;
                float dzw = (z - cz) / Res * spanZ;
                float d2 = dxw * dxw + dzw * dzw;
                if (d2 > b.Radius * b.Radius) continue;
                float dist = MathF.Sqrt(d2);
                float nrm = dist / b.Radius;
                float falloff = nrm <= inner
                    ? 1f
                    : 1f - SmoothStep01((nrm - inner) / MathF.Max(1e-4f, 1f - inner));

                int idx = (z * Res + x) * 4;
                float l = _w[idx + layer];
                float rate = Math.Clamp(b.Strength * falloff * dt, 0f, 1f);
                if (rate <= 0f) continue;

                if (b.Mode == SplatBrushMode.Smooth)
                {
                    _w[idx + layer] = Math.Clamp(l + (_avgStampValue - l) * rate, 0f, 1f);
                }
                else if (b.Mode == SplatBrushMode.Erase)
                {
                    _w[idx + layer] = Math.Clamp(l * (1f - rate), 0f, 1f);
                }
                else // Paint: push the layer toward full weight
                {
                    _w[idx + layer] = Math.Clamp(l + (1f - l) * rate, 0f, 1f);
                }

                // Renormalize the OTHER channels so the texel always sums to 1:
                // paint steals proportionally, erase gives back proportionally.
                float newL = _w[idx + layer];
                float otherSum = _w[idx] + _w[idx + 1] + _w[idx + 2] + _w[idx + 3] - newL;
                float rest = 1f - newL;
                if (otherSum > 0.001f)
                {
                    float f = rest / otherSum;
                    for (int c = 0; c < 4; c++)
                        if (c != layer) _w[idx + c] *= f;
                }
                else
                {
                    // Nothing else to give (all others 0) — the first non-painted
                    // channel takes the remainder so the texel never sums < 1.
                    for (int c = 0; c < 4; c++)
                        if (c != layer) { _w[idx + c] = rest; break; }
                }

                MarkDirty(x, z);
                NeedsBake = true;
                StrokeTexels++;
            }
        }
        HasAnyEdits = true;
    }

    private static float SmoothStep01(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private void MarkDirty(int x, int z)
    {
        if (x < _minX) _minX = x;
        if (x > _maxX) _maxX = x;
        if (z < _minZ) _minZ = z;
        if (z > _maxZ) _maxZ = z;
    }

    // ── Persistence: bake the RGBA field to a 32-bit TGA ──

    /// <summary>Write the whole field as an uncompressed 32-bit RGBA TGA
    /// (image type 2, 32 bpp). Rows are flipped so row 0 = TOP, matching the
    /// grayscale <see cref="TerrainHeightfield.BakeToTga"/> convention.</summary>
    public bool BakeToTga(string fullPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var fs = File.Create(fullPath);
            using var bw = new BinaryWriter(fs);
            // 18-byte uncompressed TRUECOLOR TGA header (type 2, 32bpp, 8 alpha bits).
            bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)2);
            bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((short)Res);   // width
            bw.Write((short)Res);   // height
            bw.Write((byte)32);     // bits per pixel
            bw.Write((byte)8);      // descriptor: 8 alpha bits, bottom-left origin
            for (int z = Res - 1; z >= 0; z--)                 // row 0 = top (flip)
                for (int x = 0; x < Res; x++)
                {
                    int idx = (z * Res + x) * 4;
                    // TGA stores BGRA; stb decodes back to RGBA on load.
                    bw.Write((byte)(Math.Clamp(_w[idx + 2], 0f, 1f) * 255f + 0.5f));
                    bw.Write((byte)(Math.Clamp(_w[idx + 1], 0f, 1f) * 255f + 0.5f));
                    bw.Write((byte)(Math.Clamp(_w[idx], 0f, 1f) * 255f + 0.5f));
                    bw.Write((byte)(Math.Clamp(_w[idx + 3], 0f, 1f) * 255f + 0.5f));
                }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TerrainSplat] Bake failed '{fullPath}': {ex.Message}");
            return false;
        }
    }
}
