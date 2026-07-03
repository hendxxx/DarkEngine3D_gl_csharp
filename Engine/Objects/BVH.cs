using System.Numerics;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    /// <summary>
    /// Bounding Volume Hierarchy (BVH) for precise mesh-based collision detection.
    /// Divides mesh triangles into a tree structure for efficient ray and sphere queries.
    /// </summary>
    public class BVH
    {
        public class Node
        {
            public AABB Bounds;
            public Node? Left;
            public Node? Right;
            public int[]? TriangleIndices;  // Leaf node: actual triangle indices
            public bool IsLeaf => TriangleIndices != null;
        }

        public Node? Root { get; private set; }
        private Vector3[]? _vertices;
        private int[]? _indices;
        private int _maxTrianglesPerNode = 4;

        private const float Epsilon = 0.0001f;

        /// <summary>
        /// Build BVH from mesh vertices and indices.
        /// </summary>
        public void Build(Vector3[] vertices, int[] indices)
        {
            _vertices = vertices;
            _indices = indices;

            if (indices.Length == 0)
            {
                Root = null;
                return;
            }

            // Create list of triangle indices to partition
            int[] triangleIndices = new int[indices.Length / 3];
            for (int i = 0; i < triangleIndices.Length; i++)
                triangleIndices[i] = i;

            Root = BuildNode(triangleIndices, 0);
        }

        private Node BuildNode(int[] triangleIndices, int depth)
        {
            var node = new Node();

            // Compute AABB for this set of triangles
            node.Bounds = ComputeTriangleBounds(triangleIndices);

            // Check if this should be a leaf
            if (triangleIndices.Length <= _maxTrianglesPerNode)
            {
                node.TriangleIndices = triangleIndices;
                return node;
            }

            // Find split axis (longest axis of bounding box)
            Vector3 extents = node.Bounds.Max - node.Bounds.Min;
            int splitAxis = 0;
            if (extents.Y > extents.X) splitAxis = 1;
            if (extents.Z > extents[splitAxis]) splitAxis = 2;

            // Compute centroid and split position
            float splitPos = 0;
            switch (splitAxis)
            {
                case 0: splitPos = (node.Bounds.Min.X + node.Bounds.Max.X) * 0.5f; break;
                case 1: splitPos = (node.Bounds.Min.Y + node.Bounds.Max.Y) * 0.5f; break;
                case 2: splitPos = (node.Bounds.Min.Z + node.Bounds.Max.Z) * 0.5f; break;
            }

            // Partition triangles
            int leftCount = 0;
            for (int i = 0; i < triangleIndices.Length; i++)
            {
                float centroid = GetTriangleCentroid(triangleIndices[i])[splitAxis];
                if (centroid < splitPos)
                {
                    int tmp = triangleIndices[i];
                    triangleIndices[i] = triangleIndices[leftCount];
                    triangleIndices[leftCount] = tmp;
                    leftCount++;
                }
            }

            // Handle degenerate case
            if (leftCount == 0 || leftCount == triangleIndices.Length)
            {
                // Poor split, make this a leaf
                node.TriangleIndices = triangleIndices;
                return node;
            }

            // Recursively build children
            node.Left = BuildNode(triangleIndices[..leftCount], depth + 1);
            node.Right = BuildNode(triangleIndices[leftCount..], depth + 1);

            return node;
        }

        private AABB ComputeTriangleBounds(int[] triangleIndices)
        {
            if (triangleIndices.Length == 0)
                return new AABB(Vector3.Zero, Vector3.Zero);

            Vector3 min = Vector3.One * float.MaxValue;
            Vector3 max = Vector3.One * float.MinValue;

            foreach (int triIdx in triangleIndices)
            {
                int idx0 = _indices![triIdx * 3];
                int idx1 = _indices[triIdx * 3 + 1];
                int idx2 = _indices[triIdx * 3 + 2];

                Vector3 v0 = _vertices![idx0];
                Vector3 v1 = _vertices[idx1];
                Vector3 v2 = _vertices[idx2];

                min = Vector3.Min(min, Vector3.Min(v0, Vector3.Min(v1, v2)));
                max = Vector3.Max(max, Vector3.Max(v0, Vector3.Max(v1, v2)));
            }

            return new AABB(min, max);
        }

        private Vector3 GetTriangleCentroid(int triangleIndex)
        {
            int idx0 = _indices![triangleIndex * 3];
            int idx1 = _indices[triangleIndex * 3 + 1];
            int idx2 = _indices[triangleIndex * 3 + 2];

            Vector3 v0 = _vertices![idx0];
            Vector3 v1 = _vertices[idx1];
            Vector3 v2 = _vertices[idx2];

            return (v0 + v1 + v2) * (1f / 3f);
        }

        /// <summary>
        /// Check if a sphere overlaps with the mesh (BVH traversal).
        /// </summary>
        public bool SphereOverlaps(Vector3 sphereCenter, float radius)
        {
            if (Root == null) return false;
            return SphereOverlapsNode(Root, sphereCenter, radius);
        }

        private bool SphereOverlapsNode(Node node, Vector3 sphereCenter, float radius)
        {
            // Check AABB overlap
            float closestX = Math.Clamp(sphereCenter.X, node.Bounds.Min.X, node.Bounds.Max.X);
            float closestY = Math.Clamp(sphereCenter.Y, node.Bounds.Min.Y, node.Bounds.Max.Y);
            float closestZ = Math.Clamp(sphereCenter.Z, node.Bounds.Min.Z, node.Bounds.Max.Z);

            float dx = sphereCenter.X - closestX;
            float dy = sphereCenter.Y - closestY;
            float dz = sphereCenter.Z - closestZ;
            float distSq = dx * dx + dy * dy + dz * dz;

            if (distSq > radius * radius)
                return false;

            // Leaf node: check triangle collisions
            if (node.IsLeaf)
            {
                foreach (int triIdx in node.TriangleIndices!)
                {
                    if (SphereTriangleOverlap(sphereCenter, radius, triIdx))
                        return true;
                }
                return false;
            }

            // Internal node: traverse closer child first for faster early-exit
            float dLeft = float.MaxValue;
            float dRight = float.MaxValue;

            if (node.Left != null)
            {
                float cx2 = Math.Clamp(sphereCenter.X, node.Left.Bounds.Min.X, node.Left.Bounds.Max.X);
                float cy2 = Math.Clamp(sphereCenter.Y, node.Left.Bounds.Min.Y, node.Left.Bounds.Max.Y);
                float cz2 = Math.Clamp(sphereCenter.Z, node.Left.Bounds.Min.Z, node.Left.Bounds.Max.Z);
                float dx2 = sphereCenter.X - cx2;
                float dy2 = sphereCenter.Y - cy2;
                float dz2 = sphereCenter.Z - cz2;
                dLeft = dx2 * dx2 + dy2 * dy2 + dz2 * dz2;
            }

            if (node.Right != null)
            {
                float cx2 = Math.Clamp(sphereCenter.X, node.Right.Bounds.Min.X, node.Right.Bounds.Max.X);
                float cy2 = Math.Clamp(sphereCenter.Y, node.Right.Bounds.Min.Y, node.Right.Bounds.Max.Y);
                float cz2 = Math.Clamp(sphereCenter.Z, node.Right.Bounds.Min.Z, node.Right.Bounds.Max.Z);
                float dx2 = sphereCenter.X - cx2;
                float dy2 = sphereCenter.Y - cy2;
                float dz2 = sphereCenter.Z - cz2;
                dRight = dx2 * dx2 + dy2 * dy2 + dz2 * dz2;
            }

            if (dLeft <= dRight)
            {
                if (node.Left != null && SphereOverlapsNode(node.Left, sphereCenter, radius))
                    return true;
                if (node.Right != null && SphereOverlapsNode(node.Right, sphereCenter, radius))
                    return true;
            }
            else
            {
                if (node.Right != null && SphereOverlapsNode(node.Right, sphereCenter, radius))
                    return true;
                if (node.Left != null && SphereOverlapsNode(node.Left, sphereCenter, radius))
                    return true;
            }

            return false;
        }

        private bool SphereTriangleOverlap(Vector3 sphereCenter, float radius, int triangleIndex)
        {
            int idx0 = _indices![triangleIndex * 3];
            int idx1 = _indices[triangleIndex * 3 + 1];
            int idx2 = _indices[triangleIndex * 3 + 2];

            Vector3 v0 = _vertices![idx0];
            Vector3 v1 = _vertices[idx1];
            Vector3 v2 = _vertices[idx2];

            // Closest point on triangle to sphere center
            Vector3 closest = ClosestPointOnTriangle(sphereCenter, v0, v1, v2);
            float dist = Vector3.Distance(sphereCenter, closest);

            return dist <= radius;
        }

        /// <summary>
        /// Find closest point on triangle to a given point.
        /// </summary>
        private Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;

            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);

            if (d1 <= 0f && d2 <= 0f)
                return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);

            if (d3 >= 0f && d4 <= d3)
                return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);

            if (d6 >= 0f && d5 <= d6)
                return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float v = d2 / (d2 - d6);
                return a + v * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float v = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + v * (c - b);
            }

            float denom = 1f / (va + vb + vc);
            float v1 = vb * denom;
            float v2 = vc * denom;
            return a + v1 * ab + v2 * ac;
        }

        /// <summary>
        /// Get the closest point on the mesh to a sphere center (with push direction).
        /// </summary>
        /// <param name="sphereCenter">Query position (usually foot or camera position).</param>
        /// <param name="maxSearchRadius">Limit search to triangles within this radius. Default unlimited.</param>
        public Vector3 GetClosestPointOnMesh(Vector3 sphereCenter, float maxSearchRadius = float.MaxValue)
        {
            if (Root == null) return sphereCenter;
            float maxDistSq = maxSearchRadius >= float.MaxValue * 0.5f
                ? float.MaxValue
                : maxSearchRadius * maxSearchRadius;
            return GetClosestPointInNode(Root, sphereCenter, maxDistSq);
        }

        private Vector3 GetClosestPointInNode(Node node, Vector3 sphereCenter, float maxDistSq = float.MaxValue)
        {
            Vector3 closest = sphereCenter;
            float minDist = float.MaxValue;

            void CheckTriangle(int triIdx)
            {
                int idx0 = _indices![triIdx * 3];
                int idx1 = _indices[triIdx * 3 + 1];
                int idx2 = _indices[triIdx * 3 + 2];

                Vector3 v0 = _vertices![idx0];
                Vector3 v1 = _vertices[idx1];
                Vector3 v2 = _vertices[idx2];

                Vector3 pt = ClosestPointOnTriangle(sphereCenter, v0, v1, v2);
                float dist = Vector3.DistanceSquared(sphereCenter, pt);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = pt;
                }
            }

            if (node.IsLeaf)
            {
                foreach (int triIdx in node.TriangleIndices!)
                    CheckTriangle(triIdx);
            }
            else
            {
                // Check both children but prioritize closer one
                float distLeft = float.MaxValue;
                float distRight = float.MaxValue;

                if (node.Left != null)
                {
                    float closestX = Math.Clamp(sphereCenter.X, node.Left.Bounds.Min.X, node.Left.Bounds.Max.X);
                    float closestY = Math.Clamp(sphereCenter.Y, node.Left.Bounds.Min.Y, node.Left.Bounds.Max.Y);
                    float closestZ = Math.Clamp(sphereCenter.Z, node.Left.Bounds.Min.Z, node.Left.Bounds.Max.Z);
                    float dx = sphereCenter.X - closestX;
                    float dy = sphereCenter.Y - closestY;
                    float dz = sphereCenter.Z - closestZ;
                    distLeft = dx * dx + dy * dy + dz * dz;
                }

                if (node.Right != null)
                {
                    float closestX = Math.Clamp(sphereCenter.X, node.Right.Bounds.Min.X, node.Right.Bounds.Max.X);
                    float closestY = Math.Clamp(sphereCenter.Y, node.Right.Bounds.Min.Y, node.Right.Bounds.Max.Y);
                    float closestZ = Math.Clamp(sphereCenter.Z, node.Right.Bounds.Min.Z, node.Right.Bounds.Max.Z);
                    float dx = sphereCenter.X - closestX;
                    float dy = sphereCenter.Y - closestY;
                    float dz = sphereCenter.Z - closestZ;
                    distRight = dx * dx + dy * dy + dz * dz;
                }

                // Traverse closer child first
                // AFTER first child, skip second child if its AABB is farther than
                // the closest point already found (second-child early-out).
                if (distLeft <= distRight)
                {
                    if (node.Left != null)
                    {
                        Vector3 leftPt = GetClosestPointInNode(node.Left, sphereCenter, maxDistSq);
                        float d = Vector3.DistanceSquared(sphereCenter, leftPt);
                        if (d < minDist)
                        {
                            minDist = d;
                            closest = leftPt;
                        }
                    }
                    // Second-child early-out: skip right if its AABB is >= minDist
                    if (node.Right != null && distRight < minDist)
                    {
                        Vector3 rightPt = GetClosestPointInNode(node.Right, sphereCenter, maxDistSq);
                        float d = Vector3.DistanceSquared(sphereCenter, rightPt);
                        if (d < minDist)
                        {
                            minDist = d;
                            closest = rightPt;
                        }
                    }
                }
                else
                {
                    if (node.Right != null)
                    {
                        Vector3 rightPt = GetClosestPointInNode(node.Right, sphereCenter, maxDistSq);
                        float d = Vector3.DistanceSquared(sphereCenter, rightPt);
                        if (d < minDist)
                        {
                            minDist = d;
                            closest = rightPt;
                        }
                    }
                    // Second-child early-out: skip left if its AABB is >= minDist
                    if (node.Left != null && distLeft < minDist)
                    {
                        Vector3 leftPt = GetClosestPointInNode(node.Left, sphereCenter, maxDistSq);
                        float d = Vector3.DistanceSquared(sphereCenter, leftPt);
                        if (d < minDist)
                        {
                            minDist = d;
                            closest = leftPt;
                        }
                    }
                }
            }

            return closest;
        }

        private List<AABB>? _cachedLeafAABBs = null;

        /// <summary>
        /// Collect AABBs from leaf nodes for use as fine-grained occlusion occluders.
        /// Filters out tiny AABBs (diagonal < minSize) and limits the total count.
        /// This gives mesh-accurate occlusion instead of a single large bounding box.
        /// Leaf AABBs are cached on first call (BVH is static).
        /// </summary>
        /// <param name="minSize">Minimum diagonal size to include (filters out tiny details).</param>
        /// <param name="maxCount">Maximum number of AABBs to collect.</param>
        /// <returns>List of leaf node AABBs.</returns>
        public List<AABB> GetLeafAABBs(float minSize = 0.5f, int maxCount = 128)
        {
            // Return cached result on subsequent calls (BVH is static)
            if (_cachedLeafAABBs != null)
                return _cachedLeafAABBs;

            // Collect ALL qualifying leaf AABBs first (avoids depth-first bias),
            // then truncate to maxCount
            var allLeaves = new List<AABB>();
            if (Root == null) return allLeaves;
            CollectLeafAABBs(Root, allLeaves, minSize);

            if (allLeaves.Count > maxCount)
                _cachedLeafAABBs = allLeaves.GetRange(0, maxCount);
            else
                _cachedLeafAABBs = allLeaves;

            return _cachedLeafAABBs;
        }

        /// <summary>
        /// Collect leaf AABBs using breadth-first (level-order) traversal for uniform spatial coverage.
        /// This prevents all collected AABBs from clustering in one region (which happens with
        /// depth-first traversal in a large BVH). With BFS, leaves from ALL regions of the tree
        /// are collected evenly, giving better occlusion coverage for the full extent of large objects.
        /// </summary>
        private static void CollectLeafAABBs(Node node, List<AABB> result, float minSize)
        {
            // BFS using Queue — ensures uniform spatial coverage across the entire tree.
            var queue = new Queue<Node>();
            queue.Enqueue(node);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                if (current.IsLeaf)
                {
                    Vector3 ext = current.Bounds.Max - current.Bounds.Min;
                    if (ext.LengthSquared() >= minSize * minSize)
                        result.Add(current.Bounds);
                }
                else
                {
                    if (current.Left != null)
                        queue.Enqueue(current.Left);
                    if (current.Right != null)
                        queue.Enqueue(current.Right);
                }
            }
        }

        /// <summary>
        /// Check if a ray intersects any triangle in the BVH within maxDist.
        /// Uses Möller-Trumbore ray-triangle intersection.
        /// Returns true if the ray hits any triangle before maxDist.
        /// </summary>
        public bool RayIntersects(Vector3 origin, Vector3 dir, float maxDist)
        {
            if (Root == null) return false;
            return RayIntersectsNode(Root, origin, dir, maxDist);
        }

        private bool RayIntersectsNode(Node node, Vector3 origin, Vector3 dir, float maxDist)
        {
            // Check node AABB first
            float tBox;
            if (!RayIntersectsAABB(origin, dir, node.Bounds, out tBox))
                return false;
            if (tBox > maxDist)
                return false;

            if (node.IsLeaf)
            {
                foreach (int triIdx in node.TriangleIndices!)
                {
                    int idx0 = _indices![triIdx * 3];
                    int idx1 = _indices[triIdx * 3 + 1];
                    int idx2 = _indices[triIdx * 3 + 2];

                    Vector3 v0 = _vertices![idx0];
                    Vector3 v1 = _vertices[idx1];
                    Vector3 v2 = _vertices[idx2];

                    float t;
                    if (RayIntersectsTriangle(origin, dir, v0, v1, v2, out t) && t > 0.001f && t < maxDist)
                        return true;
                }
                return false;
            }

            // Internal: traverse both children
            // Check left first (any order is fine since we just need true/false)
            if (node.Left != null && RayIntersectsNode(node.Left, origin, dir, maxDist))
                return true;
            if (node.Right != null && RayIntersectsNode(node.Right, origin, dir, maxDist))
                return true;

            return false;
        }

        /// <summary>Möller-Trumbore ray-triangle intersection.</summary>
        private static bool RayIntersectsTriangle(Vector3 origin, Vector3 dir,
            Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0f;
            const float epsilon = 1e-7f;

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(dir, edge2);
            float det = Vector3.Dot(edge1, h);

            if (MathF.Abs(det) < epsilon)
                return false;

            float invDet = 1f / det;
            Vector3 s = origin - v0;
            float u = Vector3.Dot(s, h) * invDet;

            if (u < 0f || u > 1f)
                return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(dir, q) * invDet;

            if (v < 0f || u + v > 1f)
                return false;

            t = Vector3.Dot(edge2, q) * invDet;
            return t >= epsilon;
        }

        /// <summary>Ray-AABB intersection (slabs method).</summary>
        private static bool RayIntersectsAABB(Vector3 origin, Vector3 dir, AABB box, out float t)
        {
            t = 0f;
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
            return true;
        }

        /// <summary>
        /// Get the closest point on the mesh, but only considering triangles that
        /// actually overlap the sphere. This prevents wrong push directions at
        /// object edges where the absolute closest triangle might be on a different
        /// face (back side) rather than the colliding face.
        /// </summary>
        public Vector3 GetClosestOverlappingPoint(Vector3 center, float radius)
        {
            if (Root == null) return center;
            float bestDistSq = float.MaxValue;
            return GetClosestOverlappingInNode(Root, center, radius, ref bestDistSq);
        }

        private Vector3 GetClosestOverlappingInNode(Node node, Vector3 center, float radius, ref float bestDistSq)
        {
            Vector3 bestPt = center;

            // AABB-sphere check first
            float closestX = Math.Clamp(center.X, node.Bounds.Min.X, node.Bounds.Max.X);
            float closestY = Math.Clamp(center.Y, node.Bounds.Min.Y, node.Bounds.Max.Y);
            float closestZ = Math.Clamp(center.Z, node.Bounds.Min.Z, node.Bounds.Max.Z);
            float dx = center.X - closestX;
            float dy = center.Y - closestY;
            float dz = center.Z - closestZ;
            float distSq = dx * dx + dy * dy + dz * dz;

            // If node is farther than current best, skip entirely
            if (distSq >= bestDistSq)
                return bestPt;

            if (node.IsLeaf)
            {
                // Leaf: check each triangle for sphere overlap
                foreach (int triIdx in node.TriangleIndices!)
                {
                    if (!SphereTriangleOverlap(center, radius, triIdx))
                        continue;

                    int idx0 = _indices![triIdx * 3];
                    int idx1 = _indices[triIdx * 3 + 1];
                    int idx2 = _indices[triIdx * 3 + 2];

                    Vector3 pt = ClosestPointOnTriangle(center, _vertices![idx0], _vertices[idx1], _vertices[idx2]);
                    float d = Vector3.DistanceSquared(center, pt);
                    if (d < bestDistSq)
                    {
                        bestDistSq = d;
                        bestPt = pt;
                    }
                }
                return bestPt;
            }

            // Internal: traverse both children
            if (node.Left != null)
            {
                Vector3 leftPt = GetClosestOverlappingInNode(node.Left, center, radius, ref bestDistSq);
                float d = Vector3.DistanceSquared(center, leftPt);
                // CRITICAL: d > 0f prevents degenerate case where child returned
                // 'center' (no overlapping triangles found), which would set
                // bestDistSq = 0 and cause ALL subsequent nodes to be skipped.
                if (d < bestDistSq && d > 0f)
                {
                    bestDistSq = d;
                    bestPt = leftPt;
                }
            }
            if (node.Right != null)
            {
                Vector3 rightPt = GetClosestOverlappingInNode(node.Right, center, radius, ref bestDistSq);
                float d = Vector3.DistanceSquared(center, rightPt);
                if (d < bestDistSq && d > 0f)
                {
                    bestDistSq = d;
                    bestPt = rightPt;
                }
            }

            return bestPt;
        }

        /// <summary>
        /// Get all triangle vertices from the mesh for debug visualization.
        /// Returns arrays of vertex positions for line drawing.
        /// Used by DebugVisualizer to render wireframe of collision mesh.
        /// </summary>
        public List<Vector3> GetTriangleLineVertices()
        {
            var lineVertices = new List<Vector3>();

            if (_vertices == null || _indices == null || _vertices.Length == 0)
                return lineVertices;

            // Draw each triangle as 3 line segments (triangle outline)
            for (int triIdx = 0; triIdx < _indices.Length / 3; triIdx++)
            {
                int i0 = _indices[triIdx * 3];
                int i1 = _indices[triIdx * 3 + 1];
                int i2 = _indices[triIdx * 3 + 2];

                var v0 = _vertices[i0];
                var v1 = _vertices[i1];
                var v2 = _vertices[i2];

                // Edge 0-1
                lineVertices.Add(v0);
                lineVertices.Add(v1);

                // Edge 1-2
                lineVertices.Add(v1);
                lineVertices.Add(v2);

                // Edge 2-0
                lineVertices.Add(v2);
                lineVertices.Add(v0);
            }

            return lineVertices;
        }
    }
}
