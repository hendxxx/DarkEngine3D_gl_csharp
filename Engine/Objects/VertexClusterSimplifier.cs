using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    public class VertexClusterSimplifier
    {
        public struct Vertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 UV;
        }

        public class MeshData
        {
            public List<Vertex> Vertices = new();
            public List<int> Indices = new();
        }

        public static MeshData Simplify(MeshData mesh, float cellSize)
        {
            var clusters = new Dictionary<(int, int, int), List<int>>();

            // 1. Masukkan vertex ke cluster
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                var key = (
                    (int)MathF.Floor(v.Position.X / cellSize),
                    (int)MathF.Floor(v.Position.Y / cellSize),
                    (int)MathF.Floor(v.Position.Z / cellSize)
                );

                if (!clusters.ContainsKey(key))
                    clusters[key] = new List<int>();

                clusters[key].Add(i);
            }

            // 2. Buat vertex baru per cluster
            var newVertices = new List<Vertex>();
            var clusterMap = new Dictionary<(int, int, int), int>();

            foreach (var kv in clusters)
            {
                var list = kv.Value;

                Vector3 pos = Vector3.Zero;
                Vector3 normal = Vector3.Zero;
                Vector2 uv = Vector2.Zero;

                foreach (int idx in list)
                {
                    pos += mesh.Vertices[idx].Position;
                    normal += mesh.Vertices[idx].Normal;
                    uv += mesh.Vertices[idx].UV;
                }

                pos /= list.Count;
                normal = Vector3.Normalize(normal / list.Count);
                uv /= list.Count;

                int newIndex = newVertices.Count;
                newVertices.Add(new Vertex { Position = pos, Normal = normal, UV = uv });

                clusterMap[kv.Key] = newIndex;
            }

            // 3. Rebuild triangle
            var newIndices = new List<int>();

            for (int i = 0; i < mesh.Indices.Count; i += 3)
            {
                int i0 = mesh.Indices[i];
                int i1 = mesh.Indices[i + 1];
                int i2 = mesh.Indices[i + 2];

                var v0 = mesh.Vertices[i0];
                var v1 = mesh.Vertices[i1];
                var v2 = mesh.Vertices[i2];

                var k0 = ((int)MathF.Floor(v0.Position.X / cellSize),
                          (int)MathF.Floor(v0.Position.Y / cellSize),
                          (int)MathF.Floor(v0.Position.Z / cellSize));

                var k1 = ((int)MathF.Floor(v1.Position.X / cellSize),
                          (int)MathF.Floor(v1.Position.Y / cellSize),
                          (int)MathF.Floor(v1.Position.Z / cellSize));

                var k2 = ((int)MathF.Floor(v2.Position.X / cellSize),
                          (int)MathF.Floor(v2.Position.Y / cellSize),
                          (int)MathF.Floor(v2.Position.Z / cellSize));

                int a = clusterMap[k0];
                int b = clusterMap[k1];
                int c = clusterMap[k2];

                // skip degenerate triangles
                if (a != b && b != c && c != a)
                {
                    newIndices.Add(a);
                    newIndices.Add(b);
                    newIndices.Add(c);
                }
            }

            return new MeshData
            {
                Vertices = newVertices,
                Indices = newIndices
            };
        }
    }

}
