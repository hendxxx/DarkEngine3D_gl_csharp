using System;
using System.Collections.Generic;
using System.IO;
using DarkEngine3D_gl_csharp.Engine.Helpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    /// <summary>
    /// Auto-detects sibling PBR maps next to an albedo/base-color texture by filename
    /// convention: the file is split on '_' / '-' and each token is matched against a
    /// known list per map type (normal/nor/nrm, metallic/metal/met, rough, ao/occlusion,
    /// height/heightmap/disp/displacement, emissive/emission/glow). Un-decodable formats
    /// (.psd/.tif/.tiff) are skipped. Used by the PBR Material panel so a user can load
    /// just the albedo and get the whole PBR chain wired automatically.
    /// </summary>
    public static class PbrMapDiscovery
    {
        public const int MapTypes = 7;

        // Index 0 = albedo (the base file itself, never auto-replaced).
        public static readonly string[][] Tokens =
        [
            [],                                              // 0 albedo
            ["normal", "nor", "nrm"],                        // 1 normal
            ["metallic", "metal", "met"],                    // 2 metallic
            ["rough", "roughness"],                          // 3 roughness
            ["ao", "occlusion", "ambient"],                  // 4 ambient occlusion
            ["height", "heightmap", "disp", "displacement"], // 5 height / displacement
            ["emissive", "emission", "emiss", "glow"],       // 6 emission
        ];

        private static readonly HashSet<string> SkipExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".psd", ".tif", ".tiff",
        };

        /// <summary>Resolve sibling maps for the given albedo path. Returns a 7-element
        /// array where index 0 = the albedo path itself (resolved) and indexes 1..6 are the
        /// matched normal/metallic/roughness/ao/height/emission files ("" = none found).
        /// Returns null when the albedo file does not exist.</summary>
        public static string[]? FindMaps(string albedoPath)
        {
            if (string.IsNullOrEmpty(albedoPath)) return null;
            string resolved = PathHelpers.Resolve(albedoPath);
            if (!File.Exists(resolved)) return null;

            string[] result = new string[MapTypes];
            result[0] = resolved;

            string dir = Path.GetDirectoryName(resolved) ?? "";
            string stem = Path.GetFileNameWithoutExtension(resolved);
            var stemTokens = SplitTokens(stem);
            if (stemTokens.Count == 0) return result;

            string[] files;
            try { files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly); }
            catch { return result; }

            foreach (var file in files)
            {
                string ext = Path.GetExtension(file);
                if (SkipExts.Contains(ext)) continue;
                if (string.Equals(Path.GetFileName(file), Path.GetFileName(resolved), StringComparison.OrdinalIgnoreCase))
                    continue;

                var tokens = SplitTokens(Path.GetFileNameWithoutExtension(file));
                if (tokens.Count == 0) continue;

                for (int t = 1; t < MapTypes; t++)
                {
                    if (!string.IsNullOrEmpty(result[t])) continue;
                    foreach (var tok in tokens)
                    {
                        if (Array.IndexOf(Tokens[t], tok) >= 0)
                        {
                            result[t] = file;
                            break;
                        }
                    }
                }
            }
            return result;
        }

        private static List<string> SplitTokens(string name)
        {
            var list = new List<string>();
            foreach (var part in name.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.Trim();
                if (p.Length > 0) list.Add(p.ToLowerInvariant());
            }
            return list;
        }
    }
}
