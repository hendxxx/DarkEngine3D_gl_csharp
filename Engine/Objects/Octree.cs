using System.Numerics;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    /// <summary>
    /// Octree spatial partitioning untuk hierarchical frustum culling dan occlusion culling.
    /// Leaf nodes berisi object indices yang berada dalam bounds node.
    /// Non-leaf nodes tidak menyimpan object — hanya sebagai spatial divider.
    /// </summary>
    public class Octree
    {
        public class Node
        {
            public AABB Bounds;
            public Node[]? Children;  // 8 children, null = leaf
            public List<int>? ObjectIndices; // Hanya di leaf node
            public bool IsLeaf => Children == null;
        }

        public Node? Root { get; private set; }
        private int _maxObjectsPerLeaf = 8;

        // ── Per-frame statistics ──
        public static int TotalNodesVisited { get; private set; }
        public static int TotalNodesFrustumCulled { get; private set; }
        public static int TotalNodesOcclusionCulled { get; private set; }
        public static void ResetFrameStats()
        {
            TotalNodesVisited = 0;
            TotalNodesFrustumCulled = 0;
            TotalNodesOcclusionCulled = 0;
        }

        /// <summary>
        /// Build octree dari daftar object bounds + indices.
        /// worldBounds: total world extent yang dipartisi.
        /// minLeafSize: ukuran minimum leaf (≈16m).
        /// </summary>
        public void Build(List<(AABB bounds, int index)> objects, AABB worldBounds, float minLeafSize = 16f)
        {
            // Auto-calculate max depth so leaf size ≈ minLeafSize
            float maxExtent = MathF.Max(
                worldBounds.Max.X - worldBounds.Min.X,
                MathF.Max(worldBounds.Max.Y - worldBounds.Min.Y, worldBounds.Max.Z - worldBounds.Min.Z)
            );
            if (maxExtent < 0.001f) { Root = null; return; }
            int maxDepth = Math.Max(1, (int)MathF.Ceiling(MathF.Log(maxExtent / minLeafSize, 2f)));

            var indices = new List<int>(objects.Count);
            for (int i = 0; i < objects.Count; i++)
                indices.Add(i);

            Root = BuildNode(objects, indices, worldBounds, 0, maxDepth);
        }

        private Node? BuildNode(List<(AABB bounds, int index)> objects, List<int> indices, AABB bounds, int depth, int maxDepth)
        {
            if (indices.Count == 0)
                return null;

            var node = new Node { Bounds = bounds };

            // Leaf: jika sudah max depth atau object count <= threshold
            if (depth >= maxDepth || indices.Count <= _maxObjectsPerLeaf)
            {
                node.ObjectIndices = new List<int>(indices);
                return node;
            }

            // Split into 8 children
            Vector3 half = (bounds.Max - bounds.Min) * 0.5f;
            Vector3 center = (bounds.Min + bounds.Max) * 0.5f;

            var childIndices = new List<int>[8];
            for (int i = 0; i < 8; i++)
                childIndices[i] = new List<int>();

            foreach (int idx in indices)
            {
                var objBounds = objects[idx].bounds;
                Vector3 objCenter = (objBounds.Min + objBounds.Max) * 0.5f;

                int cx = objCenter.X >= center.X ? 1 : 0;
                int cy = objCenter.Y >= center.Y ? 1 : 0;
                int cz = objCenter.Z >= center.Z ? 1 : 0;
                int childIdx = (cx << 2) | (cy << 1) | cz;

                childIndices[childIdx].Add(idx);
            }

            var children = new Node[8];
            bool anyChild = false;

            for (int i = 0; i < 8; i++)
            {
                if (childIndices[i].Count == 0)
                {
                    children[i] = null!;
                    continue;
                }

                // Compute child bounds
                float minX = (i & 4) != 0 ? center.X : bounds.Min.X;
                float maxX = (i & 4) != 0 ? bounds.Max.X : center.X;
                float minY = (i & 2) != 0 ? center.Y : bounds.Min.Y;
                float maxY = (i & 2) != 0 ? bounds.Max.Y : center.Y;
                float minZ = (i & 1) != 0 ? center.Z : bounds.Min.Z;
                float maxZ = (i & 1) != 0 ? bounds.Max.Z : center.Z;
                var childBounds = new AABB(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));

                children[i] = BuildNode(objects, childIndices[i], childBounds, depth + 1, maxDepth);
                if (children[i] != null)
                    anyChild = true;
            }

            if (!anyChild)
            {
                // Fallback: make this a leaf
                node.ObjectIndices = new List<int>(indices);
                return node;
            }

            node.Children = children;
            return node;
        }

        /// <summary>
        /// Walk octree, collect indices of objects in nodes that pass frustum test.
        /// Early-outs: skip entire subtree if node AABB is outside frustum.
        /// </summary>
        public void QueryFrustum(Plane[] frustum, List<int> visibleIndices)
        {
            visibleIndices.Clear();
            if (Root == null) return;
            QueryFrustumNode(Root, frustum, visibleIndices);
        }

        private static void QueryFrustumNode(Node node, Plane[] frustum, List<int> result)
        {
            TotalNodesVisited++;

            // Test node AABB against frustum
            if (!IsAABBInFrustum(frustum, node.Bounds))
            {
                TotalNodesFrustumCulled++;
                return; // Entire subtree culled
            }

            if (node.IsLeaf)
            {
                if (node.ObjectIndices != null)
                    result.AddRange(node.ObjectIndices);
                return;
            }

            for (int i = 0; i < 8; i++)
            {
                if (node.Children![i] != null)
                    QueryFrustumNode(node.Children[i], frustum, result);
            }
        }

        /// <summary>
        /// Walk octree, collect indices of objects in nodes that pass BOTH frustum AND occlusion test.
        /// Early-outs at node level: if node AABB is occluded, skip entire subtree.
        /// </summary>
        /// <param name="cameraPos">Camera position for occlusion rays.</param>
        /// <param name="aabbOccluders">AABB occluders (from OcclusionCulling or HiZOcc).</param>
        /// <param name="meshOccluders">Mesh occluders (BVH list).</param>
        /// <param name="occludeeBVHFunc">Function to get self-BVH for a given object index (for self-occlusion skip), or null.</param>
        /// <param name="useHiZ">True if HiZ mode is active.</param>
        /// <param name="hizOcc">HiZOcc instance (for terrain occlusion).</param>
        public void QueryOccluded(
            Plane[] frustum,
            Vector3 cameraPos,
            List<Helpers.ObjectHelpers.AABB>? aabbOccluders,
            List<BVH>? meshOccluders,
            Func<int, BVH?>? occludeeBVHFunc,
            List<int> resultIndices)
        {
            resultIndices.Clear();
            if (Root == null) return;
            QueryOccludedNode(Root, frustum, cameraPos, aabbOccluders, meshOccluders, occludeeBVHFunc, resultIndices);
        }

        private static void QueryOccludedNode(
            Node node, Plane[] frustum, Vector3 cameraPos,
            List<Helpers.ObjectHelpers.AABB>? aabbOccluders,
            List<BVH>? meshOccluders,
            Func<int, BVH?>? occludeeBVHFunc,
            List<int> result)
        {
            TotalNodesVisited++;

            // Frustum test first (fast reject)
            if (!IsAABBInFrustum(frustum, node.Bounds))
            {
                TotalNodesFrustumCulled++;
                return;
            }

            // Occlusion test against occluders
            if ((aabbOccluders != null || meshOccluders != null) && IsNodeOccluded(node.Bounds, cameraPos, aabbOccluders, meshOccluders))
            {
                TotalNodesOcclusionCulled++;
                return; // Entire node occluded
            }

            if (node.IsLeaf)
            {
                if (node.ObjectIndices != null)
                {
                    // Untuk leaf, test individual object dengan self-occlusion skip
                    foreach (int objIdx in node.ObjectIndices)
                    {
                        bool occluded = false;
                        if (meshOccluders != null && meshOccluders.Count > 0 && occludeeBVHFunc != null)
                        {
                            var selfBVH = occludeeBVHFunc(objIdx);
                            occluded = IsPointOccluded(
                                GetObjectCenter(node, objIdx),
                                cameraPos,
                                aabbOccluders,
                                meshOccluders,
                                selfBVH
                            );
                        }
                        if (!occluded)
                            result.Add(objIdx);
                    }
                }
                return;
            }

            // Recurse into visible children
            for (int i = 0; i < 8; i++)
            {
                if (node.Children![i] != null)
                    QueryOccludedNode(node.Children[i], frustum, cameraPos, aabbOccluders, meshOccluders, occludeeBVHFunc, result);
            }
        }

        private static bool IsNodeOccluded(
            AABB nodeBounds, Vector3 cameraPos,
            List<Helpers.ObjectHelpers.AABB>? aabbOccluders,
            List<BVH>? meshOccluders)
        {
            // Test 8 corners of node AABB against occluders
            Span<Vector3> corners = stackalloc Vector3[8]
            {
                new(nodeBounds.Min.X, nodeBounds.Min.Y, nodeBounds.Min.Z),
                new(nodeBounds.Max.X, nodeBounds.Min.Y, nodeBounds.Min.Z),
                new(nodeBounds.Max.X, nodeBounds.Max.Y, nodeBounds.Min.Z),
                new(nodeBounds.Min.X, nodeBounds.Max.Y, nodeBounds.Min.Z),
                new(nodeBounds.Min.X, nodeBounds.Min.Y, nodeBounds.Max.Z),
                new(nodeBounds.Max.X, nodeBounds.Min.Y, nodeBounds.Max.Z),
                new(nodeBounds.Max.X, nodeBounds.Max.Y, nodeBounds.Max.Z),
                new(nodeBounds.Min.X, nodeBounds.Max.Y, nodeBounds.Max.Z),
            };

            for (int ci = 0; ci < 8; ci++)
            {
                bool cornerOccluded = false;

                // Check AABB occluders (without self — nodes don't have self-BVH)
                if (aabbOccluders != null)
                {
                    for (int bi = 0; bi < aabbOccluders.Count; bi++)
                    {
                        if (RayIntersectsAABBSimple(cameraPos, corners[ci], aabbOccluders[bi], out float t))
                        {
                            float objDist = Vector3.Distance(cameraPos, corners[ci]);
                            if (t > 0.001f && t < objDist)
                            {
                                cornerOccluded = true;
                                break;
                            }
                        }
                    }
                }

                // Check mesh occluders
                // Skip occluders whose root AABB fully contains the node bounds
                // (container mesh should not occlude objects inside it)
                if (!cornerOccluded && meshOccluders != null)
                {
                    Vector3 dir = corners[ci] - cameraPos;
                    float objDist = dir.Length();
                    if (objDist < 0.001f) return false;
                    dir /= objDist;

                    for (int mi = 0; mi < meshOccluders.Count; mi++)
                    {
                        if (meshOccluders[mi].Root != null &&
                            IsAABBContained(nodeBounds, meshOccluders[mi].Root!.Bounds))
                            continue;
                        if (meshOccluders[mi].RayIntersects(cameraPos, dir, objDist))
                        {
                            cornerOccluded = true;
                            break;
                        }
                    }
                }

                if (!cornerOccluded)
                    return false; // Found a visible corner → node is visible
            }

            return true; // All corners occluded
        }

        private static bool IsPointOccluded(
            Vector3 target, Vector3 cameraPos,
            List<Helpers.ObjectHelpers.AABB>? aabbOccluders,
            List<BVH>? meshOccluders,
            BVH? selfBVH)
        {
            Vector3 dir = target - cameraPos;
            float objDist = dir.Length();
            if (objDist < 0.001f) return false;
            dir /= objDist;

            // Check AABB occluders
            if (aabbOccluders != null)
            {
                for (int bi = 0; bi < aabbOccluders.Count; bi++)
                {
                    if (RayIntersectsAABBSimple(cameraPos, target, aabbOccluders[bi], out float t))
                    {
                        if (t > 0.001f && t < objDist)
                            return true;
                    }
                }
            }

            // Check mesh occluders (skip self + skip container occluders)
            if (meshOccluders != null)
            {
                for (int mi = 0; mi < meshOccluders.Count; mi++)
                {
                    if (meshOccluders[mi] == selfBVH) continue;
                    // Skip if object's BVH bounds are inside the occluder's root AABB
                    // (container mesh should not occlude objects inside it)
                    if (selfBVH != null && selfBVH.Root != null &&
                        meshOccluders[mi].Root != null &&
                        IsAABBContained(selfBVH.Root.Bounds, meshOccluders[mi].Root.Bounds))
                        continue;
                    if (meshOccluders[mi].RayIntersects(cameraPos, dir, objDist))
                        return true;
                }
            }

            return false;
        }

        private static Vector3 GetObjectCenter(Node node, int objIdx)
        {
            // Use node bounds center as approximation (we don't have per-object bounds here)
            return (node.Bounds.Min + node.Bounds.Max) * 0.5f;
        }

        /// <summary>
        /// Check if an AABB is fully contained within another AABB.
        /// Used to detect when an object/node is inside a "container" mesh
        /// so the container mesh doesn't occlude the contained object.
        /// </summary>
        private static bool IsAABBContained(AABB inner, AABB outer)
        {
            return inner.Min.X >= outer.Min.X &&
                   inner.Min.Y >= outer.Min.Y &&
                   inner.Min.Z >= outer.Min.Z &&
                   inner.Max.X <= outer.Max.X &&
                   inner.Max.Y <= outer.Max.Y &&
                   inner.Max.Z <= outer.Max.Z;
        }

        /// <summary>
        /// Simple ray-AABB intersection — returns entry distance t.
        /// Doesn't handle camera-inside-occluder (not needed for node-level tests).
        /// </summary>
        private static bool RayIntersectsAABBSimple(Vector3 origin, Vector3 target, Helpers.ObjectHelpers.AABB box, out float t)
        {
            t = 0f;
            Vector3 dir = target - origin;
            float maxDist = dir.Length();
            if (maxDist < 0.001f) return false;
            dir /= maxDist;

            float tmin = 0f;
            float tmax = float.MaxValue;

            for (int axis = 0; axis < 3; axis++)
            {
                float originComp = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
                float dirComp = axis == 0 ? dir.X : axis == 1 ? dir.Y : dir.Z;
                float minComp = axis == 0 ? box.Min.X : axis == 1 ? box.Min.Y : box.Min.Z;
                float maxComp = axis == 0 ? box.Max.X : axis == 1 ? box.Max.Y : box.Max.Z;

                if (MathF.Abs(dirComp) < 1e-7f)
                {
                    if (originComp < minComp || originComp > maxComp)
                        return false;
                }
                else
                {
                    float invD = 1.0f / dirComp;
                    float t1 = (minComp - originComp) * invD;
                    float t2 = (maxComp - originComp) * invD;
                    if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                    tmin = MathF.Max(tmin, t1);
                    tmax = MathF.Min(tmax, t2);
                    if (tmin > tmax) return false;
                }
            }

            t = tmin;
            return t >= 0f && t < maxDist;
        }

        /// <summary>
        /// Frustum-AABB test (copy dari StaticObjectManager.IsAABBInFrustum untuk independensi).
        /// </summary>
        private static bool IsAABBInFrustum(Plane[] planes, AABB aabb, float margin = 3f)
        {
            for (int i = 0; i < 6; i++)
            {
                Vector3 pv = new(
                    planes[i].Normal.X >= 0 ? aabb.Max.X : aabb.Min.X,
                    planes[i].Normal.Y >= 0 ? aabb.Max.Y : aabb.Min.Y,
                    planes[i].Normal.Z >= 0 ? aabb.Max.Z : aabb.Min.Z
                );
                float d = Vector3.Dot(planes[i].Normal, pv) + planes[i].D;
                if (d < -margin)
                    return false;
            }
            return true;
        }
    }
}
