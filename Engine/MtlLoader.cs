using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine
{
    // Parsed material definition from a .mtl file.
    public class MaterialDef
    {
        public string Name = "";
        public Vector3 DiffuseColor = new(0.8f, 0.8f, 0.8f); // Kd
        public string? DiffuseMapPath;                       // map_Kd (relative to mtl file)
    }

    // Minimal Wavefront .mtl parser. Reads `newmtl`, `Kd`, `map_Kd` only.
    // Returns Dictionary<materialName, MaterialDef>. Missing file → empty dict.
    public static class MtlLoader
    {
        public static Dictionary<string, MaterialDef> Load(string mtlPath)
        {
            var dict = new Dictionary<string, MaterialDef>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(mtlPath) || !File.Exists(mtlPath)) return dict;

            string baseDir = Path.GetDirectoryName(mtlPath) ?? ".";
            CultureInfo inv = CultureInfo.InvariantCulture;
            MaterialDef? current = null;

            foreach (string raw in File.ReadAllLines(mtlPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                string[] tok = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length == 0) continue;

                switch (tok[0].ToLowerInvariant())
                {
                    case "newmtl":
                        current = new MaterialDef { Name = tok.Length > 1 ? tok[1] : "" };
                        if (current.Name.Length > 0) dict[current.Name] = current;
                        break;

                    case "kd":
                        if (current != null && tok.Length >= 4)
                            current.DiffuseColor = new Vector3(
                                float.Parse(tok[1], inv),
                                float.Parse(tok[2], inv),
                                float.Parse(tok[3], inv));
                        break;

                    case "map_kd":
                        if (current != null && tok.Length >= 2)
                        {
                            // Some MTL files put "-options value" before the path; the path is the last arg.
                            string mapName = tok[tok.Length - 1];
                            current.DiffuseMapPath = Path.Combine(baseDir, mapName);
                        }
                        break;
                }
            }
            return dict;
        }
    }
}
