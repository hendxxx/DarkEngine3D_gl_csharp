using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    public static class GltfMeshSimplifier
    {
        public static void Simplify(
            float[] positions,
            float[] normals,
            float[] uvs,          // boleh null
            int[] indices,
            float cellSize,
            out float[] outPositions,
            out float[] outNormals,
            out float[] outUVs,
            out int[] outIndices)
        {
            int vertexCount = positions.Length / 3;

            // 1. cluster key -> list of vertex indices
            var clusters = new Dictionary<(int, int, int), List<int>>();

            for (int i = 0; i < vertexCount; i++)
            {
                float x = positions[i * 3 + 0];
                float y = positions[i * 3 + 1];
                float z = positions[i * 3 + 2];

                var key = (
                    (int)MathF.Floor(x / cellSize),
                    (int)MathF.Floor(y / cellSize),
                    (int)MathF.Floor(z / cellSize)
                );

                if (!clusters.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    clusters[key] = list;
                }

                list.Add(i);
            }

            // 2. buat vertex baru per cluster
            var newPositions = new List<Vector3>();
            var newNormals = new List<Vector3>();
            var newUVs = new List<Vector2>();
            var clusterToNewIndex = new Dictionary<(int, int, int), int>();

            bool hasUV = uvs != null && uvs.Length == vertexCount * 2;

            foreach (var kv in clusters)
            {
                var list = kv.Value;

                Vector3 pos = Vector3.Zero;
                Vector3 nrm = Vector3.Zero;
                Vector2 uv = Vector2.Zero;

                foreach (int vi in list)
                {
                    pos.X += positions[vi * 3 + 0];
                    pos.Y += positions[vi * 3 + 1];
                    pos.Z += positions[vi * 3 + 2];

                    if (normals != null && normals.Length == vertexCount * 3)
                    {
                        nrm.X += normals[vi * 3 + 0];
                        nrm.Y += normals[vi * 3 + 1];
                        nrm.Z += normals[vi * 3 + 2];
                    }

                    if (hasUV)
                    {
                        uv.X += uvs[vi * 2 + 0];
                        uv.Y += uvs[vi * 2 + 1];
                    }
                }

                float inv = 1.0f / list.Count;
                pos *= inv;
                if (nrm != Vector3.Zero)
                    nrm = Vector3.Normalize(nrm * inv);
                if (hasUV)
                    uv *= inv;

                int newIndex = newPositions.Count;
                newPositions.Add(pos);
                newNormals.Add(nrm);
                if (hasUV) newUVs.Add(uv);

                clusterToNewIndex[kv.Key] = newIndex;
            }

            // 3. rebuild index
            var newIndices = new List<int>();

            for (int i = 0; i < indices.Length; i += 3)
            {
                int i0 = indices[i + 0];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];

                // ambil posisi lama
                Vector3 p0 = new(
                    positions[i0 * 3 + 0],
                    positions[i0 * 3 + 1],
                    positions[i0 * 3 + 2]);

                Vector3 p1 = new(
                    positions[i1 * 3 + 0],
                    positions[i1 * 3 + 1],
                    positions[i1 * 3 + 2]);

                Vector3 p2 = new(
                    positions[i2 * 3 + 0],
                    positions[i2 * 3 + 1],
                    positions[i2 * 3 + 2]);

                var k0 = (
                    (int)MathF.Floor(p0.X / cellSize),
                    (int)MathF.Floor(p0.Y / cellSize),
                    (int)MathF.Floor(p0.Z / cellSize));

                var k1 = (
                    (int)MathF.Floor(p1.X / cellSize),
                    (int)MathF.Floor(p1.Y / cellSize),
                    (int)MathF.Floor(p1.Z / cellSize));

                var k2 = (
                    (int)MathF.Floor(p2.X / cellSize),
                    (int)MathF.Floor(p2.Y / cellSize),
                    (int)MathF.Floor(p2.Z / cellSize));

                int a = clusterToNewIndex[k0];
                int b = clusterToNewIndex[k1];
                int c = clusterToNewIndex[k2];

                // skip triangle degenerate
                if (a == b || b == c || c == a)
                    continue;

                newIndices.Add(a);
                newIndices.Add(b);
                newIndices.Add(c);
            }

            // 4. convert ke array glTF-style
            outPositions = new float[newPositions.Count * 3];
            outNormals = new float[newNormals.Count * 3];
            outUVs = hasUV ? new float[newUVs.Count * 2] : Array.Empty<float>();
            outIndices = newIndices.ToArray();

            for (int i = 0; i < newPositions.Count; i++)
            {
                outPositions[i * 3 + 0] = newPositions[i].X;
                outPositions[i * 3 + 1] = newPositions[i].Y;
                outPositions[i * 3 + 2] = newPositions[i].Z;

                outNormals[i * 3 + 0] = newNormals[i].X;
                outNormals[i * 3 + 1] = newNormals[i].Y;
                outNormals[i * 3 + 2] = newNormals[i].Z;
            }

            if (hasUV)
            {
                for (int i = 0; i < newUVs.Count; i++)
                {
                    outUVs[i * 2 + 0] = newUVs[i].X;
                    outUVs[i * 2 + 1] = newUVs[i].Y;
                }
            }
        }
    }

}
