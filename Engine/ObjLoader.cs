using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine
{
    // One sub-mesh of a loaded OBJ — all triangles that share a single material.
    public class MeshGroup
    {
        public string MaterialName = "";
        public List<Vertex> Vertices = new();
    }

    // Result of loading an OBJ together with its accompanying .mtl (if present).
    public class ObjMesh
    {
        public List<MeshGroup> Groups = new();
        public Dictionary<string, MaterialDef> Materials = new();
        public string? MtlPath;
    }

    // Wavefront .obj loader. Supports:
    //   v  x y z         — vertex position
    //   vt u v           — UV (used for texturing)
    //   vn x y z         — vertex normal (auto-normalized)
    //   f  ... ...       — face: tokens are "v", "v/t", "v/t/n", or "v//n". Fan-triangulated.
    //   mtllib file.mtl  — sibling .mtl is parsed for materials
    //   usemtl name      — switches the current MeshGroup to one tagged with that material
    public static class ObjLoader
    {
        // Multi-material load. Returns mesh groups + parsed materials.
        public static ObjMesh Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"OBJ file not found: {path}");

            Console.Write($"Loading {Path.GetFileName(path)} ... ");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var result = new ObjMesh();
            string baseDir = Path.GetDirectoryName(path) ?? ".";

            List<Vector3> positions = new();
            List<Vector3> normals   = new();
            List<Vector2> uvs       = new();

            CultureInfo inv = CultureInfo.InvariantCulture;

            // Active material → current group lookup (one group per distinct material name).
            string currentMat = "";
            Dictionary<string, MeshGroup> groupByName = new(StringComparer.OrdinalIgnoreCase);

            MeshGroup GetOrCreateGroup(string name)
            {
                if (!groupByName.TryGetValue(name, out var g))
                {
                    g = new MeshGroup { MaterialName = name };
                    groupByName[name] = g;
                    result.Groups.Add(g);
                }
                return g;
            }
            // Default group for faces appearing before any usemtl directive.
            GetOrCreateGroup("");

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                string[] tok = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length == 0) continue;

                switch (tok[0])
                {
                    case "v":
                        positions.Add(new Vector3(
                            float.Parse(tok[1], inv),
                            float.Parse(tok[2], inv),
                            float.Parse(tok[3], inv)));
                        break;

                    case "vt":
                        // Most OBJs only have U/V; some have a 3rd W, ignored.
                        uvs.Add(new Vector2(
                            float.Parse(tok[1], inv),
                            tok.Length > 2 ? float.Parse(tok[2], inv) : 0f));
                        break;

                    case "vn":
                        normals.Add(Vector3.Normalize(new Vector3(
                            float.Parse(tok[1], inv),
                            float.Parse(tok[2], inv),
                            float.Parse(tok[3], inv))));
                        break;

                    case "mtllib":
                        if (tok.Length > 1)
                        {
                            string mtlName = string.Join(' ', tok, 1, tok.Length - 1);
                            string mtlPath = Path.Combine(baseDir, mtlName);
                            result.MtlPath = mtlPath;
                            foreach (var kv in MtlLoader.Load(mtlPath))
                                result.Materials[kv.Key] = kv.Value;
                        }
                        break;

                    case "usemtl":
                        currentMat = tok.Length > 1 ? string.Join(' ', tok, 1, tok.Length - 1) : "";
                        GetOrCreateGroup(currentMat);
                        break;

                    case "f":
                    {
                        var output = GetOrCreateGroup(currentMat).Vertices;
                        int n = tok.Length - 1;
                        int[] vi = new int[n];
                        int[] ti = new int[n];
                        int[] ni = new int[n];

                        for (int i = 0; i < n; i++)
                        {
                            string[] parts = tok[i + 1].Split('/');
                            int v = int.Parse(parts[0], inv);
                            vi[i] = v > 0 ? v - 1 : positions.Count + v;

                            ti[i] = -1;
                            if (parts.Length >= 2 && parts[1].Length > 0)
                            {
                                int tt = int.Parse(parts[1], inv);
                                ti[i] = tt > 0 ? tt - 1 : uvs.Count + tt;
                            }

                            ni[i] = -1;
                            if (parts.Length >= 3 && parts[2].Length > 0)
                            {
                                int nn = int.Parse(parts[2], inv);
                                ni[i] = nn > 0 ? nn - 1 : normals.Count + nn;
                            }
                        }

                        // Fan-triangulate.
                        for (int i = 1; i < n - 1; i++)
                        {
                            int a = 0, b = i, c = i + 1;
                            Vector3 p0 = positions[vi[a]];
                            Vector3 p1 = positions[vi[b]];
                            Vector3 p2 = positions[vi[c]];

                            Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0);
                            if (fn.LengthSquared() > 1e-12f) fn = Vector3.Normalize(fn);
                            else fn = Vector3.UnitY;

                            Vector3 n0 = ni[a] >= 0 ? normals[ni[a]] : fn;
                            Vector3 n1 = ni[b] >= 0 ? normals[ni[b]] : fn;
                            Vector3 n2 = ni[c] >= 0 ? normals[ni[c]] : fn;

                            Vector2 uv0 = ti[a] >= 0 && ti[a] < uvs.Count ? uvs[ti[a]] : Vector2.Zero;
                            Vector2 uv1 = ti[b] >= 0 && ti[b] < uvs.Count ? uvs[ti[b]] : Vector2.Zero;
                            Vector2 uv2 = ti[c] >= 0 && ti[c] < uvs.Count ? uvs[ti[c]] : Vector2.Zero;

                            output.Add(new Vertex(p0.X, p0.Y, p0.Z, n0.X, n0.Y, n0.Z, 1f, 1f, 1f, uv0.X, uv0.Y));
                            output.Add(new Vertex(p1.X, p1.Y, p1.Z, n1.X, n1.Y, n1.Z, 1f, 1f, 1f, uv1.X, uv1.Y));
                            output.Add(new Vertex(p2.X, p2.Y, p2.Z, n2.X, n2.Y, n2.Z, 1f, 1f, 1f, uv2.X, uv2.Y));
                        }
                        break;
                    }
                }
            }

            // Drop any empty groups.
            result.Groups.RemoveAll(g => g.Vertices.Count == 0);

            int totalTris = 0;
            foreach (var g in result.Groups) totalTris += g.Vertices.Count / 3;

            sw.Stop();
            Console.WriteLine($"{totalTris} tris ({result.Groups.Count} group(s)) in {sw.Elapsed.TotalSeconds:F2}s");
            return result;
        }

        // Backward-compat path used by MovingObjects / Projectiles for single-color loads.
        // Flattens all groups into one Vertex[] and overrides the per-vertex color with `color`.
        public static Vertex[] LoadAsTriangleList(string path, Vector3 color)
        {
            ObjMesh mesh = Load(path);
            int total = 0;
            foreach (var g in mesh.Groups) total += g.Vertices.Count;
            Vertex[] flat = new Vertex[total];
            int idx = 0;
            foreach (var g in mesh.Groups)
            {
                foreach (var v in g.Vertices)
                {
                    Vertex nv = v;
                    nv.Color = color;
                    flat[idx++] = nv;
                }
            }
            return flat;
        }
    }
}
