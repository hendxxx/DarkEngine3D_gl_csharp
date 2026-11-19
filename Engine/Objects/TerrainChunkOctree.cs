using System.Numerics;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects;

internal static class TerrainChunkOctree
{
    internal sealed class Node
    {
        internal AABB Bounds { get; }
        internal int ChunkIndex { get; }
        internal int ChunkCount { get; }
        internal Node?[] Children { get; } = new Node?[8];

        internal Node(AABB bounds, int chunkIndex, int chunkCount)
        {
            Bounds = bounds;
            ChunkIndex = chunkIndex;
            ChunkCount = chunkCount;
        }
    }

    internal static Node? Build(IReadOnlyList<AABB> chunkBounds, int chunksPerSide)
    {
        if (chunksPerSide < 1 || chunkBounds.Count != chunksPerSide * chunksPerSide) return null;
        return BuildNode(chunkBounds, chunksPerSide, 0, 0, chunksPerSide, chunksPerSide);
    }

    private static Node BuildNode(IReadOnlyList<AABB> chunkBounds, int side, int x0, int z0, int x1, int z1)
    {
        var bounds = chunkBounds[z0 * side + x0];
        for (int z = z0; z < z1; z++)
            for (int x = x0; x < x1; x++)
                bounds = Union(bounds, chunkBounds[z * side + x]);

        int width = x1 - x0, depth = z1 - z0;
        int chunkCount = width * depth;
        var node = new Node(bounds, chunkCount == 1 ? z0 * side + x0 : -1, chunkCount);
        if (chunkCount == 1) return node;

        int xMid = x0 + (width + 1) / 2;
        int zMid = z0 + (depth + 1) / 2;
        for (int zHalf = 0; zHalf < (depth > 1 ? 2 : 1); zHalf++)
            for (int xHalf = 0; xHalf < (width > 1 ? 2 : 1); xHalf++)
            {
                int childX0 = xHalf == 0 ? x0 : xMid;
                int childX1 = xHalf == 0 && width > 1 ? xMid : x1;
                int childZ0 = zHalf == 0 ? z0 : zMid;
                int childZ1 = zHalf == 0 && depth > 1 ? zMid : z1;

                // Heightfields occupy the local Y=0 split plane, so the sparse octree
                // uses the upper Y bit consistently and subdivides the useful XZ axes.
                int slot = 2 | (xHalf << 0) | (zHalf << 2);
                node.Children[slot] = BuildNode(chunkBounds, side, childX0, childZ0, childX1, childZ1);
            }
        return node;
    }

    private static AABB Union(AABB a, AABB b) => new(
        Vector3.Min(a.Min, b.Min), Vector3.Max(a.Max, b.Max));
}