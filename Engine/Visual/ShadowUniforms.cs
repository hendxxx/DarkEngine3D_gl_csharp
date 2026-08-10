using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Libs;
using System;
using System.Collections.Generic;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>
    /// Caches uniform locations per program and uploads the live <see cref="ShadowSettings"/>
    /// bias / blend values to the shaders that consume them. Called once per frame from the
    /// existing per-frame uniform setup of each render path. Any path that does not call the
    /// matching upload simply keeps the shader's GLSL default initializer — the pre-panel
    /// value — so wiring is purely additive.
    /// </summary>
    public static class ShadowUniforms
    {
        private static readonly Dictionary<(uint, string), int> _locs = new();

        private static int Loc(uint program, string name)
        {
            var key = (program, name);
            if (_locs.TryGetValue(key, out int loc)) return loc;

            loc = GL.GetUniformLocation(program, name);
            _locs[key] = loc;
            return loc;
        }

        /// <summary>Upload the per-cascade ortho depth range (zFar − zNear, world units) as
        /// u_DepthRange and the per-cascade world texel size as u_TexelWorld. Together the
        /// fragment shaders derive a bias whose WORLD offset is proportional to the local
        /// texel size (constant texel count at every distance), so far cascades neither
        /// vanish (bias too big in NDC × huge range) nor acne (bias sub-texel).
        /// Falls back to vec3(1) when no CSM has updated yet, which keeps the old behavior.</summary>
        private static void UploadCascadeScales(uint program)
        {
            int dr = Loc(program, "u_DepthRange");
            if (dr >= 0)
            {
                GL.Uniform3f(dr,
                    CSM.LastDepthRanges[0],
                    CSM.LastDepthRanges[1],
                    CSM.LastDepthRanges[2]);
            }
            int tw = Loc(program, "u_TexelWorld");
            if (tw >= 0)
            {
                GL.Uniform3f(tw,
                    CSM.LastTexelWorld[0],
                    CSM.LastTexelWorld[1],
                    CSM.LastTexelWorld[2]);
            }
            int mb = Loc(program, "u_MaxWorldBias");
            if (mb >= 0)
            {
                GL.Uniform3f(mb,
                    ShadowSettings.MaxWorldBias[0],
                    ShadowSettings.MaxWorldBias[1],
                    ShadowSettings.MaxWorldBias[2]);
            }
            int oa = Loc(program, "u_CascadeOverlayAlpha");
            if (oa >= 0)
            {
                GL.Uniform1f(oa, ShadowSettings.CascadeOverlayAlpha);
            }
        }

        /// <summary>Upload u_ConstantBias / u_SlopeBias / u_MinBias / u_BlendRange
        /// (fragment_shader + terrainEditor_fragment).</summary>
        public static void UploadMain(uint program)
        {
            int c = Loc(program, "u_ConstantBias");
            int s = Loc(program, "u_SlopeBias");
            int m = Loc(program, "u_MinBias");
            int b = Loc(program, "u_BlendRange");
            if (c >= 0) GL.Uniform1f(c, ShadowSettings.ConstantBias);
            if (s >= 0) GL.Uniform1f(s, ShadowSettings.SlopeBias);
            if (m >= 0) GL.Uniform1f(m, ShadowSettings.MinBias);
            if (b >= 0) GL.Uniform1f(b, ShadowSettings.BlendRange);
            UploadCascadeScales(program);
        }

        /// <summary>Upload u_ConstantBias / u_SlopeBias / u_MinBias / u_BlendRange
        /// (gltf_fragment — separate tuning values).</summary>
        public static void UploadGltf(uint program)
        {
            int c = Loc(program, "u_ConstantBias");
            int s = Loc(program, "u_SlopeBias");
            int m = Loc(program, "u_MinBias");
            int b = Loc(program, "u_BlendRange");
            if (c >= 0) GL.Uniform1f(c, ShadowSettings.GltfConstantBias);
            if (s >= 0) GL.Uniform1f(s, ShadowSettings.GltfSlopeBias);
            if (m >= 0) GL.Uniform1f(m, ShadowSettings.GltfMinBias);
            if (b >= 0) GL.Uniform1f(b, ShadowSettings.BlendRange);
            UploadCascadeScales(program);
        }

        /// <summary>Upload u_NormalBias from the current ShadowSettings.NormalBias
        /// (all shadow-casting vertex shaders). Scaled to the active cascade's texel size.</summary>
        public static void UploadNormalBias(uint program)
        {
            UploadNormalBias(program, ShadowSettings.NormalBias);
        }

        /// <summary>Upload a specific u_NormalBias value, scaled to the ACTIVE cascade's
        /// texel ratio (texel_cascade / texel_0, set by CSM.BindFramebuffer). The extrusion
        /// happens in world space, so a fixed value would be a fraction of a texel in the far
        /// cascades (their texels are 10-40× larger) — scaling keeps the WORLD extrusion a
        /// constant texel count at every distance, matching the texel-proportional fragment
        /// bias. Used by the terrain shadow pass too, which passes a size-boosted base value
        /// (big heightmaps need more). Default ActiveCascadeIndex=0 / LastTexelWorld=1 → no
        /// scaling (backward compatible).</summary>
        public static void UploadNormalBias(uint program, float value)
        {
            int n = Loc(program, "u_NormalBias");
            if (n < 0) return;

            // Precomputed per-frame texel ratio (CSM.UpdateMatrices) — floored at 1 and
            // clamped at 128 there, so a cascade never gets less extrusion than cascade 0.
            int active = Math.Clamp(CSM.ActiveCascadeIndex, 0, CSM.LastTexelScale.Length - 1);
            GL.Uniform1f(n, value * CSM.LastTexelScale[active]);
        }

        /// <summary>Upload the normal bias for a terrain shadow pass, boosted by how much
        /// larger the terrain is than the baseline (25-unit footprint / 30-unit height —
        /// the editor terrain defaults). Big heightmap surfaces cover far more world space
        /// per shadow-map texel than unit-sized primitives, so the standard 0.02 extrusion
        /// is not enough to keep steep slopes acne-free. Clamped so pathological sizes
        /// can't peter-pan. Shared by the editor terrain (EditorTerrainMesh) and the game
        /// terrain (TerrainChunk) so both stay consistent.</summary>
        public static void UploadTerrainNormalBias(uint program, float footprint, float height)
        {
            float boost = MathF.Min(
                MathF.Max(1f, MathF.Max(footprint / 25f, height / 30f)), 6f);
            UploadNormalBias(program, ShadowSettings.NormalBias * boost);
        }
    }
}
