using System;

namespace DarkEngine3D_gl_csharp.Engine.Config
{
    /// <summary>
    /// Single source of truth for the shadow filter modes. The mode number is the value sent
    /// to the shaders as the <c>shadowFilterMode</c> uniform, so this table, the HUD/settings
    /// UI list and the GLSL dispatch must all agree. "PCF 16" used to be listed twice (mode 0
    /// and mode 2 both ran poisson16 at 5 texels); it is one mode now and the rest shifted down.
    /// </summary>
    public static class ShadowFilterModes
    {
        /// <summary>Display names indexed by mode number (uniform value 0..<see cref="Count"/>-1).</summary>
        public static readonly string[] Names =
        [
            "PCF 16",       // 0 — default: poisson 16 @ 5 texels
            "Hard Shadow",  // 1
            "PCF 16 Soft",  // 2 — poisson 16 @ 10 texels
            "PCF 32",       // 3
            "PCF 32 Soft",  // 4
            "PCSS 16",      // 5
            "PCSS 16 Soft", // 6
            "PCSS 32",      // 7
            "PCSS 32 Soft", // 8
        ];

        public const int Count = 9;

        public static string NameOf(int mode) => Names[Math.Clamp(mode, 0, Count - 1)];

        public static int Normalize(int mode) => Math.Clamp(mode, 0, Count - 1);

        /// <summary>Map a mode saved before the duplicate was removed (0-9, "PCF 16" at both
        /// 0 and 2) onto the current table. Only for values read from data that predates
        /// <see cref="SettingsData.ShadowFilterSchema"/> — never apply it to a mode that is
        /// already in the current numbering.</summary>
        public static int FromLegacy(int legacy) => legacy switch
        {
            0 or 2 => 0,
            1 => 1,
            3 => 2,
            4 => 3,
            5 => 4,
            6 => 5,
            7 => 6,
            8 => 7,
            9 => 8,
            _ => 0,
        };
    }
}
