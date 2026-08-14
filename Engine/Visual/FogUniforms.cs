using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Libs;
using System.Collections.Generic;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>Uploads the live <see cref="FogSettings"/> (enable, mode, color, density,
    /// start/end, height) to any fog-capable program. Mirrors the ShadowUniforms pattern:
    /// uniform locations are cached per program, and paths that never call this simply keep
    /// the GLSL default initializers, so wiring is purely additive. Callers replace their
    /// old useFog/fogColor upload with a single <see cref="UploadMain"/> call.</summary>
    public static class FogUniforms
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

        /// <summary>Upload the fog state to the given program. The fog color is the sky/
        /// horizon color (from the Lights instance) unless the user picked a manual color
        /// (FogSettings.UseSkyColor = false).</summary>
        public static void UploadMain(uint program, Lights light)
        {
            if (program == 0) return;

            Vector3 color = FogSettings.UseSkyColor ? light.FogColor : FogSettings.Color;

            int use = Loc(program, "useFog");
            if (use >= 0) GL.Uniform1i(use, FogSettings.Enabled ? 1 : 0);

            int col = Loc(program, "fogColor");
            if (col >= 0) GL.Uniform3f(col, color.X, color.Y, color.Z);

            int mode = Loc(program, "u_fogMode");
            if (mode >= 0) GL.Uniform1i(mode, FogSettings.Mode);

            int density = Loc(program, "u_fogDensity");
            if (density >= 0) GL.Uniform1f(density, FogSettings.Density);

            int start = Loc(program, "u_fogStart");
            if (start >= 0) GL.Uniform1f(start, FogSettings.StartDistance);

            int end = Loc(program, "u_fogEnd");
            if (end >= 0) GL.Uniform1f(end, FogSettings.EndDistance);

            int height = Loc(program, "u_fogHeight");
            if (height >= 0) GL.Uniform1f(height, FogSettings.Height);

            int heightRange = Loc(program, "u_fogHeightRange");
            if (heightRange >= 0) GL.Uniform1f(heightRange, FogSettings.HeightRange);
        }
    }
}
