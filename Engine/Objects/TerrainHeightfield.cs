using System.IO;
using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Libs;
using StbImageSharp;

namespace DarkEngine3D_gl_csharp.Engine.Objects;

/// <summary>Brush operation applied to the terrain heightfield.</summary>
public enum TerrainBrushMode
{
    /// <summary>Push heights up (white-ward) under the brush.</summary>
    Raise = 0,
    /// <summary>Push heights down (black-ward) under the brush.</summary>
    Lower = 1,
    /// <summary>Relax heights toward the 3×3 local average (erode noise/terracing).</summary>
    Smooth = 2,
    /// <summary>Level heights toward the average of the whole brush footprint.</summary>
    Flatten = 3,
}

/// <summary>Live brush-session settings for terrain sculpting. The PBR panel
/// edits these; the viewport paints through them while a stroke is held.
/// Lives on <see cref="EditorObject.SculptBrush"/> — null = NOT sculpting.</summary>
public sealed class TerrainBrushSession
{
    /// <summary>Current brush operation (Raise/Lower/Smooth/Flatten).</summary>
    public TerrainBrushMode Mode;
    /// <summary>Brush radius in world units (the painted footprint on the plane).</summary>
    public float Radius = 5f;
    /// <summary>Application speed: fraction of the full 0..1 height range per
    /// second at the falloff core (1.5 ≈ black→white in ~0.7 s of holding).</summary>
    public float Strength = 1.5f;
    /// <summary>Falloff softness: 1 = fully soft (smoothstep to the rim),
    /// 0 = hard-edged core (uniform inside the radius).</summary>
    public float Hardness = 0.5f;
}

/// <summary>
/// CPU terrain heightfield for BRUSH SCULPTING (512² grayscale, values 0..1).
/// The plane's dedicated elevation image is decoded ONCE; brush strokes mutate
/// this CPU buffer which is uploaded to a live GL_R8 texture that REPLACES the
/// unit-15 bind — so the displaced vertex shader, POM fallback and CPU picking
/// all see the sculpted surface with NO shader changes.
///
/// The texture is marked CLAMP_TO_EDGE + LINEAR and built from RED rows in the
/// same order <see cref="Object3D.CreatePlaneVertices"/> emits mesh UVs
/// (row v = world -Z .. +Z), matching the GPU's vertex-stage sampling.
///
/// Persistence is FILE-BASED: finishing a stroke bakes the whole field to a
/// TGA (R8 grayscale, lossless) under <c>Artifacts/Terrain/&lt;scene&gt;/&lt;object&gt;.tga</c>
/// and points <see cref="EditorObject.TerrainHeightPath"/> at it, so sculpted
/// terrain saves/loads with the scene like any other elevation image.
/// </summary>
public sealed class TerrainHeightfield
{
    /// <summary>Fixed sculpt resolution (512² floats ≈ 1 MB CPU, 256 KB GPU).</summary>
    public const int Res = 512;
    public const float BytesToHeight = 1f / 255f;

    private readonly float[] _h = new float[Res * Res];
    private readonly byte[] _upload = new byte[Res * Res];
    private uint _tex;

    // Dirty rectangle accumulated since the last FlushTexture (texel bounds).
    private int _minX = int.MaxValue, _maxX = int.MinValue;
    private int _minZ = int.MaxValue, _maxZ = int.MinValue;

    // Averaging-stamp guard: Smooth/Flatten read the whole footprint each stamp,
    // so repeated stamps within one drag frame would re-average cumulatively.
    private int _avgStamp = -1;
    private float _avgStampValue;

    // ── Per-stroke undo/redo (snapshot = 8-bit field copy, 256 KB each) ──
    /// <summary>Max remembered strokes (40 × 256 KB ≈ 10 MB worst case).</summary>
    public const int MaxHistory = 40;
    private readonly List<byte[]> _undoStrokes = new();   // pre-stroke states (last = most recent)
    private readonly List<byte[]> _redoStrokes = new();   // undone states (last = next redo)
    private bool _strokeOpen;   // a snapshot for the current stroke has been taken

    /// <summary>How many strokes Ctrl+Z can still revert.</summary>
    public int UndoDepth => _undoStrokes.Count;
    /// <summary>How many undone strokes Ctrl+Y can re-apply.</summary>
    public int RedoDepth => _redoStrokes.Count;

    /// <summary>Call at stroke START (mouse press) — the pre-stroke snapshot is
    /// actually taken lazily by the first stamp that MUTATES the field, so
    /// no-op strokes (brush never touched the terrain) cost nothing.</summary>
    public void BeginStroke() => _strokeOpen = false;

    private byte[] Snapshot8()
    {
        var b = new byte[Res * Res];
        for (int i = 0; i < b.Length; i++)
            b[i] = (byte)(Math.Clamp(_h[i], 0f, 1f) * 255f + 0.5f);
        return b;
    }

    private void Restore(byte[] b)
    {
        for (int i = 0; i < b.Length; i++)
            _h[i] = b[i] * BytesToHeight;
        MarkWholeDirty();
        NeedsBake = true;   // persistence must follow the restored state
    }

    private void MarkWholeDirty()
    {
        _minX = 0; _maxX = Res - 1;
        _minZ = 0; _maxZ = Res - 1;
    }

    /// <summary>Revert the last stroke (Ctrl+Z). The current state moves to the
    /// redo stack; the restored field must be FLUSHed by the caller.</summary>
    public bool Undo()
    {
        if (_undoStrokes.Count == 0) return false;
        _redoStrokes.Add(Snapshot8());
        Restore(_undoStrokes[^1]);
        _undoStrokes.RemoveAt(_undoStrokes.Count - 1);
        return true;
    }

    /// <summary>Re-apply the last undone stroke (Ctrl+Y / Ctrl+Shift+Z).</summary>
    public bool Redo()
    {
        if (_redoStrokes.Count == 0) return false;
        _undoStrokes.Add(Snapshot8());
        if (_undoStrokes.Count > MaxHistory) _undoStrokes.RemoveAt(0);
        Restore(_redoStrokes[^1]);
        _redoStrokes.RemoveAt(_redoStrokes.Count - 1);
        return true;
    }

    /// <summary>True once ANY brush stamp has modified this field since the last
    /// bake (drives the displaced-path gates AND the per-stroke bake decision).</summary>
    public bool HasAnyEdits { get; private set; }

    /// <summary>True when edits accumulated since the last finished stroke — only
    /// then does <c>EndSculptStroke</c> flush + bake (stroke-end frames run even
    /// when the brush never touched the field).</summary>
    public bool NeedsBake { get; private set; }

    /// <summary>Clear the per-stroke bake flag (called after a successful TGA bake).</summary>
    public void MarkBaked() => NeedsBake = false;

    /// <summary>Frame stamp of the last stroke this field confirmed (bake-once
    /// guard — <see cref="EditorObject.EndSculptStroke"/> runs on several frames
    /// after the final stamp).</summary>
    public int LastStrokeStamp { get; set; } = -1;

    private TerrainHeightfield() { }

    /// <summary>Decode any StbImageSharp-supported image (PNG/JPG/BMP/TGA) into a
    /// heightfield (RED channel, 0..1). Nearest-neighbor resample to 512².</summary>
    public static TerrainHeightfield? FromImage(string resolvedPath)
    {
        try
        {
            using var fs = File.OpenRead(resolvedPath);
            var img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlue);
            if (img.Width <= 0 || img.Height <= 0 || img.Data == null)
                return null;

            var field = new TerrainHeightfield();
            for (int z = 0; z < Res; z++)
            {
                int sy = Math.Clamp(z * img.Height / Res, 0, img.Height - 1);
                int rowBase = sy * img.Width;
                for (int x = 0; x < Res; x++)
                {
                    int sx = Math.Clamp(x * img.Width / Res, 0, img.Width - 1);
                    field._h[z * Res + x] = img.Data[(rowBase + sx) * (int)img.SourceComp] * BytesToHeight;
                }
            }
            return field;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TerrainSculpt] Failed to decode '{resolvedPath}': {ex.Message}");
            return null;
        }
    }

    // ── GPU texture (GL_R8, live-updated per brush stroke) ──

    /// <summary>Create the R8 texture lazily (first flush after the first edit).</summary>
    private uint EnsureTexture()
    {
        if (_tex != 0) return _tex;
        unsafe
        {
            uint t = 0;
            GL.GenTextures(1, &t);
            _tex = t;
            GL.BindTexture(Const.GL_TEXTURE_2D, _tex);
            for (int z = 0; z < Res; z++)
                for (int x = 0; x < Res; x++)
                    _upload[z * Res + x] = (byte)(Math.Clamp(_h[z * Res + x], 0f, 1f) * 255f + 0.5f);
            fixed (byte* p = _upload)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_R8, Res, Res, 0,
                    Const.GL_RED, Const.GL_UNSIGNED_BYTE, p);
            }
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_REPEAT);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_REPEAT);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
        }
        return _tex;
    }

    /// <summary>Upload the accumulated dirty region to the GPU. Call after each
    /// brush application; cheap (only the touched rectangle moves).</summary>
    public bool FlushTexture()
    {
        if (_minZ > _maxZ) return false;
        uint tex = EnsureTexture();
        int w = _maxX - _minX + 1, h = _maxZ - _minZ + 1;
        unsafe
        {
            fixed (byte* p = _upload)
            {
                GL.BindTexture(Const.GL_TEXTURE_2D, tex);
                GL.TexSubImage2D(Const.GL_TEXTURE_2D, 0, _minX, _minZ, w, h,
                    Const.GL_RED, Const.GL_UNSIGNED_BYTE, p + (_minZ * Res + _minX));
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

    // ── CPU sampling (mirrors the vertex shader's unit-15 RAW read) ──

    public float Sample(int tx, int tz)
    {
        tx = Math.Clamp(tx, 0, Res - 1);
        tz = Math.Clamp(tz, 0, Res - 1);
        return _h[tz * Res + tx];
    }

    /// <summary>Bilinear sample in mesh UV (0..1), wrapping per the terrain
    /// elevation's OWN tiling — exactly how the vertex stage maps uvT.</summary>
    public float SampleBilinear(float u, float v, float tilingX, float tilingY)
    {
        float s = u * tilingX; s -= MathF.Floor(s);
        float t = v * tilingY; t -= MathF.Floor(t);
        float fx = s * Res - 0.5f, fy = t * Res - 0.5f;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float ax = fx - x0, ay = fy - y0;
        float h00 = Sample(x0, y0), h10 = Sample(x0 + 1, y0);
        float h01 = Sample(x0, y0 + 1), h11 = Sample(x0 + 1, y0 + 1);
        float top = h00 + (h10 - h00) * ax;
        float bot = h01 + (h11 - h01) * ax;
        return top + (bot - top) * ay;
    }

    // ── Brush ops ──

    /// <summary>Apply one brush stamp. Coordinates are PLANE-LOCAL world units
    /// (origin at plane center, X right / Z "up" in mesh-UV space), the plane
    /// spans spanX × spanZ. <paramref name="frameStamp"/> must be constant within
    /// one render/input frame (Smooth/Flatten average once per stamp).</summary>
    public void ApplyBrush(float localX, float localZ, float spanX, float spanZ,
        float radius, float strength, float hardness, TerrainBrushMode mode,
        float frameDt, int frameStamp)
    {
        if (radius <= 0f || spanX <= 0f || spanZ <= 0f) return;
        float dt = Math.Clamp(frameDt, 0f, 0.05f);

        // Texel window covered by the circular world-space brush.
        int rx = (int)MathF.Ceiling(radius / spanX * Res) + 1;
        int rz = (int)MathF.Ceiling(radius / spanZ * Res) + 1;
        float cx = (localX / spanX + 0.5f) * Res - 0.5f;   // brush center in texel space
        float cz = (localZ / spanZ + 0.5f) * Res - 0.5f;
        int tx0 = Math.Clamp((int)cx - rx, 0, Res - 1);
        int tx1 = Math.Clamp((int)cx + rx + 1, 0, Res - 1);
        int tz0 = Math.Clamp((int)cz - rz, 0, Res - 1);
        int tz1 = Math.Clamp((int)cz + rz + 1, 0, Res - 1);
        if (tx1 < tx0 || tz1 < tz0) return;

        // Lazily take the PRE-STROKE snapshot on the first mutating stamp of a
        // stroke; a new stroke also invalidates the redo stack (standard undo).
        if (!_strokeOpen)
        {
            _undoStrokes.Add(Snapshot8());
            if (_undoStrokes.Count > MaxHistory) _undoStrokes.RemoveAt(0);
            _redoStrokes.Clear();
            _strokeOpen = true;
        }

        // Average of the whole footprint (Flatten) — one value per stamp.
        if (mode == TerrainBrushMode.Flatten)
        {
            if (frameStamp != _avgStamp)
            {
                float sum = 0f; int n = 0;
                for (int z = tz0; z <= tz1; z++)
                    for (int x = tx0; x <= tx1; x++)
                    {
                        float dxw = (x + 0.5f) / Res * spanX - localX;
                        float dzw = (z + 0.5f) / Res * spanZ - localZ;
                        if (dxw * dxw + dzw * dzw <= radius * radius) { sum += _h[z * Res + x]; n++; }
                    }
                _avgStampValue = n > 0 ? sum / n : 0f;
                _avgStamp = frameStamp;
            }
        }

        // Softness falloff: 1 in the inner (hardness) core → 0 at the rim.
        float inner = Math.Clamp(hardness, 0f, 1f);

        for (int z = tz0; z <= tz1; z++)
        {
            for (int x = tx0; x <= tx1; x++)
            {
                float dxw = (x + 0.5f) / Res * spanX - localX;
                float dzw = (z + 0.5f) / Res * spanZ - localZ;
                // Squared-distance reject BEFORE the sqrt — a 100-unit brush on a
                // 100-unit plane spans the whole 512² window, so the inner loop is
                // the hottest path in the editor while painting.
                float d2 = dxw * dxw + dzw * dzw;
                if (d2 > radius * radius) continue;
                float dist = MathF.Sqrt(d2);
                float nrm = dist / radius;
                float falloff = nrm <= inner
                    ? 1f
                    : 1f - SmoothStep01((nrm - inner) / MathF.Max(1e-4f, 1f - inner));

                int idx = z * Res + x;
                float hv = _h[idx];
                float target;
                float rate;
                switch (mode)
                {
                    case TerrainBrushMode.Raise:
                        target = 1f; rate = strength * falloff * dt; break;
                    case TerrainBrushMode.Lower:
                        target = 0f; rate = strength * falloff * dt; break;
                    case TerrainBrushMode.Smooth:
                        float avg = (Sample(x - 1, z) + Sample(x + 1, z) +
                                     Sample(x, z - 1) + Sample(x, z + 1) +
                                     hv) / 5f;
                        target = avg;
                        rate = Math.Clamp(strength * 8f * falloff * dt, 0f, 1f);
                        break;
                    case TerrainBrushMode.Flatten:
                        target = _avgStampValue;
                        rate = Math.Clamp(strength * 8f * falloff * dt, 0f, 1f);
                        break;
                    default:
                        continue;
                }
                _h[idx] = Math.Clamp(hv + (target - hv) * rate, 0f, 1f);
                MarkDirty(x, z);
                NeedsBake = true;
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

    /// <summary>Live GPU texture id (0 until the first flush after an edit).</summary>
    public uint GpuTexture => _tex;

    // ── CPU picking (ray-march the sculpted surface) ──

    /// <summary>Ray-march the displaced surface exactly like the vertex shader's
    /// BASE term renders it: RAW elevation × baseHeight + world offset. The PBR
    /// height DETAIL term (a second image) is intentionally not decoded on the
    /// CPU — its amplitude is typically a fraction of the base, so picking stays
    /// within a brush-radius of the true surface for editor use.
    /// <paramref name="origin"/>/<paramref name="dir"/> are world-space; the
    /// object's world matrix maps plane-local ⇄ world. Coarse march with binary
    /// refinement — cheap and robust.</summary>
    public bool TryRaycast(Matrix4x4 world, float baseHeight, float worldOffset,
        float boundExtent, float tilingX, float tilingY,
        Vector3 origin, Vector3 dir, out Vector3 hit)
    {
        hit = default;
        if (!Matrix4x4.Invert(world, out Matrix4x4 inv)) return false;
        float extent = MathF.Max(0.001f, boundExtent);

        // Hoisted per-call constants (the old version re-derived the scale-column
        // norms inside the elevation closure on EVERY sample — 60 fps × hundreds
        // of samples of pure sqrt waste).
        float sx = MathF.Sqrt(world.M11 * world.M11 + world.M12 * world.M12 + world.M13 * world.M13);
        float sz = MathF.Sqrt(world.M31 * world.M31 + world.M32 * world.M32 + world.M33 * world.M33);
        float invSx = 1f / MathF.Max(1e-4f, sx);
        float invSz = 1f / MathF.Max(1e-4f, sz);
        float baseH = Math.Clamp(baseHeight, 0f, 500f);

        // Ray in PLANE-LOCAL space, ONCE. Because TransformNormal is linear,
        // world point origin + dir*t maps to l0 + ld*t — the march below works
        // with plain vector mads, no per-step matrix multiply.
        Vector3 l0 = Vector3.Transform(origin, inv);
        Vector3 ld = Vector3.TransformNormal(dir, inv);

        // Analytic slab clip against the terrain's local AABB (X/Z = ±half size,
        // Y = ±extent around 0 — boundExtent already includes |offset|). A ray that
        // misses the box returns IMMEDIATELY; a hit marches only inside [t0, t1]
        // instead of the old 0..4000 blind walk (8,000 iterations per frame).
        float tEnter = 0f, tExit = 4000f;
        if (!ClipSlab(l0.X, ld.X, -sx * 0.5f, sx * 0.5f, ref tEnter, ref tExit) ||
            !ClipSlab(l0.Z, ld.Z, -sz * 0.5f, sz * 0.5f, ref tEnter, ref tExit) ||
            !ClipSlab(l0.Y, ld.Y, -extent, extent, ref tEnter, ref tExit))
            return false;
        if (tEnter >= tExit) return false;

        // World elevation at plane-local (x, z) — the BASE term only.
        float Elev(float lx, float lz) =>
            SampleBilinear(lx * invSx + 0.5f, lz * invSz + 0.5f, tilingX, tilingY)
            * baseH + worldOffset;

        // Cap the march length: with a very long window the step grows so the
        // loop stays bounded (~512 samples max) — picking cost is O(1) per frame.
        float step = MathF.Max(0.5f, extent * 0.05f);
        step = MathF.Max(step, (tExit - tEnter) / 512f);

        float prevRel = float.PositiveInfinity;
        bool hasPrev = false;
        float prevT = tEnter;
        for (float t = tEnter + step; t <= tExit; t += step)
        {
            Vector3 lp = l0 + ld * t;
            float surfY = Elev(lp.X, lp.Z);
            float rel = lp.Y - surfY;   // >0 above surface
            if (hasPrev && prevRel > 0f && rel <= 0f)
            {
                // Crossed the surface between prevT and t — binary refine (10 iters).
                float lo = prevT, hi = t;
                for (int i = 0; i < 10; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    Vector3 mp = l0 + ld * mid;
                    float r = mp.Y - Elev(mp.X, mp.Z);
                    if (r > 0f) lo = mid; else hi = mid;
                }
                float ht = (lo + hi) * 0.5f;
                Vector3 lhp = l0 + ld * ht;
                hit = origin + dir * ht;
                // Plane-local X/Z of the hit — the brush center for painting.
                // (CPU mirror reads the RAW field; the u_dispStrength reshape moves
                // the surface at most half a strength step — sub-texel for editor use.)
                _lastHitLocalXZ = new Vector2(lhp.X, lhp.Z);
                return true;
            }
            prevRel = rel;
            prevT = t;
            hasPrev = true;
        }
        return false;
    }

    /// <summary>Clip the [tEnter, tExit] window to where the local ray stays inside
    /// the [min, max] slab on ONE axis. False when the ray runs parallel outside
    /// the slab (a guaranteed miss — the caller stops immediately).</summary>
    private static bool ClipSlab(float o, float d, float min, float max, ref float tEnter, ref float tExit)
    {
        if (MathF.Abs(d) < 1e-6f)
            return o >= min && o <= max;
        float ta = (min - o) / d;
        float tb = (max - o) / d;
        if (ta > tb) (ta, tb) = (tb, ta);
        if (ta > tEnter) tEnter = ta;
        if (tb < tExit) tExit = tb;
        return tEnter <= tExit;
    }

    /// <summary>Plane-local X/Z (world units from the plane center) of the last
    /// successful <see cref="TryRaycast"/> — the brush center for painting.</summary>
    public Vector2 LastHitLocalXZ => _lastHitLocalXZ;
    private Vector2 _lastHitLocalXZ;

    // ── Persistence: bake the sculpted field to an R8 TGA ──

    /// <summary>Write the whole field as a grayscale (8-bit, R8) TGA — lossless,
    /// no external writer dependency (StbImageSharp only decodes). Rows are
    /// flipped so row 0 = TOP (standard TGA orientation).</summary>
    public bool BakeToTga(string fullPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var fs = File.Create(fullPath);
            using var bw = new BinaryWriter(fs);
            // 18-byte uncompressed grayscale TGA header.
            bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)3);
            bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((short)Res);   // width  (little-endian, standard TGA)
            bw.Write((short)Res);   // height
            bw.Write((byte)8); bw.Write((byte)0);
            for (int z = Res - 1; z >= 0; z--)                 // row 0 = top (flip)
                for (int x = 0; x < Res; x++)
                    bw.Write((byte)(Math.Clamp(_h[z * Res + x], 0f, 1f) * 255f + 0.5f));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TerrainSculpt] Bake failed '{fullPath}': {ex.Message}");
            return false;
        }
    }
}
