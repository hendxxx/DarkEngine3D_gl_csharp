namespace DarkEngine3D_gl_csharp.Engine.Libs
{
    /// <summary>Common filtering presets — quick way to set min/mag + mipmapping + anisotropy
    /// in one pick (the "Common Filtering Methods" group).</summary>
    public enum TexFilterPreset
    {
        Nearest,          // crisp pixels, no filtering
        Bilinear,         // linear min+mag, no mipmaps
        Trilinear,        // linear mipmap min, linear mag, 1× aniso
        Anisotropic2x,
        Anisotropic4x,
        Anisotropic8x,
        Anisotropic16x,
    }

    /// <summary>Minification filters (GL_TEXTURE_MIN_FILTER).</summary>
    public enum TexMinFilter
    {
        Nearest,
        Linear,
        NearestMipmapNearest,
        LinearMipmapNearest,
        NearestMipmapLinear,
        LinearMipmapLinear,
    }

    /// <summary>Magnification filters (GL_TEXTURE_MAG_FILTER).</summary>
    public enum TexMagFilter
    {
        Nearest,
        Linear,
    }

    /// <summary>Texture coordinate wrapping (GL_TEXTURE_WRAP_S / _T).</summary>
    public enum TexWrap
    {
        Repeat,
        MirroredRepeat,
        ClampToEdge,
        ClampToBorder,
    }

    /// <summary>Per-texture sampling settings: filtering (min/mag), mipmapping & advanced
    /// filters (anisotropy, LOD bias), common presets, wrapping and UV tiling/offset.
    /// <see cref="Apply"/> pushes the whole set onto a bound texture; the same object is
    /// shared by the simple texture, the PBR maps and the terrain layers of an object.</summary>
    public class TextureSettings
    {
        public const float MaxAnisotropy = 16f;

        // ── Common Filtering Methods (preset drives min/mag/aniso/mipmap) ──
        public TexFilterPreset FilterPreset = TexFilterPreset.Trilinear;

        // ── Minification / Magnification ──
        public TexMinFilter MinFilter = TexMinFilter.LinearMipmapLinear;
        public TexMagFilter MagFilter = TexMagFilter.Linear;

        // ── Mipmapping & Advanced Filters ──
        public bool GenerateMipmaps = true;
        public float MipmapBias = 0f;
        public float Anisotropy = 4f;

        // ── Texture Wrapping ──
        public TexWrap WrapS = TexWrap.Repeat;
        public TexWrap WrapT = TexWrap.Repeat;

        // ── Tiling & Offset (UV transform: uv * Tiling + Offset) ──
        public float TilingX = 1f;
        public float TilingY = 1f;
        public float OffsetX = 0f;
        public float OffsetY = 0f;

        /// <summary>When true, the tiling/offset were randomized (Random Tiling toggle) —
        /// breaks up the repeating tile pattern on large surfaces. Toggling off restores
        /// tiling 1×1 / offset 0.</summary>
        public bool RandomTiling = false;

        public static readonly string[] PresetNames =
            ["Nearest", "Bilinear", "Trilinear", "Anisotropic 2x", "Anisotropic 4x", "Anisotropic 8x", "Anisotropic 16x"];

        public static readonly string[] MinFilterNames =
            ["Nearest", "Linear", "Nearest Mipmap Nearest", "Linear Mipmap Nearest", "Nearest Mipmap Linear", "Linear Mipmap Linear"];

        public static readonly string[] MagFilterNames = ["Nearest", "Linear"];

        public static readonly string[] WrapNames = ["Repeat", "Mirrored Repeat", "Clamp to Edge", "Clamp to Border"];

        // ── GL mapping ──

        public uint MinFilterGL => MinFilter switch
        {
            TexMinFilter.Nearest => Const.GL_NEAREST,
            TexMinFilter.Linear => Const.GL_LINEAR,
            TexMinFilter.NearestMipmapNearest => Const.GL_NEAREST_MIPMAP_NEAREST,
            TexMinFilter.LinearMipmapNearest => Const.GL_LINEAR_MIPMAP_NEAREST,
            TexMinFilter.NearestMipmapLinear => Const.GL_NEAREST_MIPMAP_LINEAR,
            _ => Const.GL_LINEAR_MIPMAP_LINEAR,
        };

        public uint MagFilterGL => MagFilter == TexMagFilter.Nearest ? Const.GL_NEAREST : Const.GL_LINEAR;

        public uint WrapSGL => WrapS switch
        {
            TexWrap.MirroredRepeat => Const.GL_MIRRORED_REPEAT,
            TexWrap.ClampToEdge => Const.GL_CLAMP_TO_EDGE,
            TexWrap.ClampToBorder => Const.GL_CLAMP_TO_BORDER,
            _ => Const.GL_REPEAT,
        };

        public uint WrapTGL => WrapT switch
        {
            TexWrap.MirroredRepeat => Const.GL_MIRRORED_REPEAT,
            TexWrap.ClampToEdge => Const.GL_CLAMP_TO_EDGE,
            TexWrap.ClampToBorder => Const.GL_CLAMP_TO_BORDER,
            _ => Const.GL_REPEAT,
        };

        /// <summary>Apply the preset to the min/mag filters, mipmapping and anisotropy.</summary>
        public void ApplyPreset()
        {
            switch (FilterPreset)
            {
                case TexFilterPreset.Nearest:
                    MinFilter = TexMinFilter.Nearest;
                    MagFilter = TexMagFilter.Nearest;
                    Anisotropy = 1f;
                    break;
                case TexFilterPreset.Bilinear:
                    MinFilter = TexMinFilter.Linear;
                    MagFilter = TexMagFilter.Linear;
                    Anisotropy = 1f;
                    break;
                case TexFilterPreset.Anisotropic2x:
                    MinFilter = TexMinFilter.LinearMipmapLinear;
                    MagFilter = TexMagFilter.Linear;
                    Anisotropy = 2f;
                    break;
                case TexFilterPreset.Anisotropic4x:
                    MinFilter = TexMinFilter.LinearMipmapLinear;
                    MagFilter = TexMagFilter.Linear;
                    Anisotropy = 4f;
                    break;
                case TexFilterPreset.Anisotropic8x:
                    MinFilter = TexMinFilter.LinearMipmapLinear;
                    MagFilter = TexMagFilter.Linear;
                    Anisotropy = 8f;
                    break;
                case TexFilterPreset.Anisotropic16x:
                    MinFilter = TexMinFilter.LinearMipmapLinear;
                    MagFilter = TexMagFilter.Linear;
                    Anisotropy = MaxAnisotropy;
                    break;
                default: // Trilinear
                    MinFilter = TexMinFilter.LinearMipmapLinear;
                    MagFilter = TexMagFilter.Linear;
                    Anisotropy = 1f;
                    break;
            }
        }

        /// <summary>Push every sampling parameter onto the given 2D texture (GL state is
        /// left with the texture bound — caller decides whether to unbind).</summary>
        public unsafe void Apply(uint texId)
        {
            if (texId == 0) return;

            GL.BindTexture(Const.GL_TEXTURE_2D, texId);

            // Min filter: a mipmap-based filter needs mipmaps; when generation is off,
            // fall back to a plain filter so sampling never hits an incomplete texture.
            uint min = MinFilterGL;
            bool mipBased = min == Const.GL_NEAREST_MIPMAP_NEAREST || min == Const.GL_LINEAR_MIPMAP_NEAREST
                || min == Const.GL_NEAREST_MIPMAP_LINEAR || min == Const.GL_LINEAR_MIPMAP_LINEAR;
            if (!GenerateMipmaps && mipBased)
                min = MagFilter == TexMagFilter.Nearest ? Const.GL_NEAREST : Const.GL_LINEAR;

            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)min);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)MagFilterGL);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)WrapSGL);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)WrapTGL);

            if (Anisotropy > 1f)
                GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAX_ANISOTROPY, Math.Clamp(Anisotropy, 1f, MaxAnisotropy));
            if (MipmapBias != 0f)
                GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_LOD_BIAS, MipmapBias);

            if (GenerateMipmaps)
                GL.GenerateMipmap(Const.GL_TEXTURE_2D);
        }

        /// <summary>Fill tiling/offset with random values (Random Tiling toggle ON).
        /// Tiling is varied per axis so the same texture no longer tiles in a perfect grid;
        /// offset is jittered by one tile so the pattern starts at a random point.</summary>
        public void ApplyRandomTiling()
        {
            var rng = Random.Shared;
            TilingX = 1f + (float)rng.NextDouble() * 7f;            // 1..8
            TilingY = 1f + (float)rng.NextDouble() * 7f;
            OffsetX = (float)rng.NextDouble();                       // 0..1 (one tile)
            OffsetY = (float)rng.NextDouble();
            RandomTiling = true;
        }

        /// <summary>Restore neutral tiling/offset (Random Tiling toggle OFF).</summary>
        public void ResetTiling()
        {
            TilingX = 1f;
            TilingY = 1f;
            OffsetX = 0f;
            OffsetY = 0f;
            RandomTiling = false;
        }

        public TextureSettings Clone()
        {
            return new TextureSettings
            {
                FilterPreset = FilterPreset,
                MinFilter = MinFilter,
                MagFilter = MagFilter,
                GenerateMipmaps = GenerateMipmaps,
                MipmapBias = MipmapBias,
                Anisotropy = Anisotropy,
                WrapS = WrapS,
                WrapT = WrapT,
                TilingX = TilingX,
                TilingY = TilingY,
                OffsetX = OffsetX,
                OffsetY = OffsetY,
                RandomTiling = RandomTiling,
            };
        }
    }

    /// <summary>Serializable snapshot of <see cref="TextureSettings"/> stored in
    /// <see cref="Scene.EditorObjectData"/> (System.Text.Json camelCase). Old scene files
    /// lack the property → null → the loader falls back to defaults.</summary>
    public class TextureSettingsData
    {
        public int FilterPreset { get; set; } = (int)TexFilterPreset.Trilinear;
        public int MinFilter { get; set; } = (int)TexMinFilter.LinearMipmapLinear;
        public int MagFilter { get; set; } = (int)TexMagFilter.Linear;
        public bool GenerateMipmaps { get; set; } = true;
        public float MipmapBias { get; set; }
        public float Anisotropy { get; set; } = 4f;
        public int WrapS { get; set; }
        public int WrapT { get; set; }
        public float TilingX { get; set; } = 1f;
        public float TilingY { get; set; } = 1f;
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        public bool RandomTiling { get; set; }

        public static TextureSettingsData FromSettings(TextureSettings s)
        {
            return new TextureSettingsData
            {
                FilterPreset = (int)s.FilterPreset,
                MinFilter = (int)s.MinFilter,
                MagFilter = (int)s.MagFilter,
                GenerateMipmaps = s.GenerateMipmaps,
                MipmapBias = s.MipmapBias,
                Anisotropy = s.Anisotropy,
                WrapS = (int)s.WrapS,
                WrapT = (int)s.WrapT,
                TilingX = s.TilingX,
                TilingY = s.TilingY,
                OffsetX = s.OffsetX,
                OffsetY = s.OffsetY,
                RandomTiling = s.RandomTiling,
            };
        }

        public static TextureSettings ToSettings(TextureSettingsData d)
        {
            var s = new TextureSettings
            {
                FilterPreset = (TexFilterPreset)Math.Clamp(d.FilterPreset, 0, (int)TexFilterPreset.Anisotropic16x),
                MinFilter = (TexMinFilter)Math.Clamp(d.MinFilter, 0, (int)TexMinFilter.LinearMipmapLinear),
                MagFilter = (TexMagFilter)Math.Clamp(d.MagFilter, 0, (int)TexMagFilter.Linear),
                GenerateMipmaps = d.GenerateMipmaps,
                MipmapBias = d.MipmapBias,
                Anisotropy = Math.Clamp(d.Anisotropy, 1f, TextureSettings.MaxAnisotropy),
                WrapS = (TexWrap)Math.Clamp(d.WrapS, 0, (int)TexWrap.ClampToBorder),
                WrapT = (TexWrap)Math.Clamp(d.WrapT, 0, (int)TexWrap.ClampToBorder),
                TilingX = d.TilingX,
                TilingY = d.TilingY,
                OffsetX = d.OffsetX,
                OffsetY = d.OffsetY,
                RandomTiling = d.RandomTiling,
            };
            return s;
        }
    }
}
