using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    /// <summary>
    /// Self-contained 3D Quickhull algorithm.
    /// Takes a set of 3D points and computes the convex hull as a triangle mesh.
    /// </summary>
    public static class Quickhull
    {
        private const float Eps = 1e-9f;
        private const float FaceDistEps = 1e-7f;

        /// <summary>
        /// Compute the convex hull of a set of 3D points.
        /// Callers should deduplicate points before calling (see BuildConvexHullBVHForObject).
        /// Returns hull triangles as parallel arrays of vertices and indices.
        /// If fewer than 4 non-coplanar points are provided, returns null.
        /// </summary>
        public static (Vector3[] vertices, int[] indices)? ComputeConvexHull(List<Vector3> inputPoints)
        {
            // Caller is expected to deduplicate points.
            if (inputPoints.Count < 4)
                return null;

            var points = inputPoints.ToArray();

            // --- Step 1: Find initial tetrahedron ---
            if (!FindInitialTetrahedron(points, out int[] tetraIndices))
                return null; // All points are coplanar or collinear

            // --- Step 2: Build initial face list ---
            var faces = new List<Face>();
            // Tetrahedron has 4 faces (each a triangle)
            int[] t = tetraIndices;
            // Face opposite vertex 0: (1,2,3)
            faces.Add(new Face(t[1], t[2], t[3]));
            // Face opposite vertex 1: (0,2,3) -- need correct winding
            faces.Add(new Face(t[0], t[3], t[2])); // reversed to face outward
            // Face opposite vertex 2: (0,1,3)
            faces.Add(new Face(t[0], t[1], t[3]));
            // Face opposite vertex 3: (0,1,2)
            faces.Add(new Face(t[0], t[2], t[1])); // reversed to face outward

            // Fix face normals to point outward from tetrahedron center
            Vector3 tetCenter = (points[t[0]] + points[t[1]] + points[t[2]] + points[t[3]]) * 0.25f;
            foreach (var face in faces)
            {
                face.ComputePlane(points);
                if (face.DistanceToPlane(tetCenter) > 0f)
                    face.Flip();
            }

            // --- Step 3: Distribute all points to face outside sets ---
            var allPointFlags = new bool[points.Length]; // true = already assigned to a face
            // The tetra vertices are already on the hull
            foreach (int ti in t) allPointFlags[ti] = true;

            foreach (var face in faces)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    if (allPointFlags[i]) continue;
                    float dist = face.DistanceToPlane(points[i]);
                    if (dist > FaceDistEps)
                    {
                        face.OutsideSet.Add(i);
                        allPointFlags[i] = true;
                    }
                }
            }

            // Check for points inside the tetrahedron that aren't on any face
            // They remain unassigned and will never become part of the hull

            // --- Step 4: Main iteration ---
            // Process faces with non-empty outside sets
            var horizonEdges = new HashSet<Edge>();
            var visibleFaces = new List<Face>();
            var newFaces = new List<Face>();
            int iteration = 0;
            const int maxIterations = 10000; // safety limit

            while (true)
            {
                // Find face with farthest outside point
                Face? bestFace = null;
                float bestDist = -1f;
                foreach (var face in faces)
                {
                    if (face.OutsideSet.Count == 0) continue;
                    // The farthest point from the face plane
                    float maxDist = -1f;
                    foreach (int pi in face.OutsideSet)
                    {
                        float d = face.DistanceToPlane(points[pi]);
                        if (d > maxDist) maxDist = d;
                    }
                    if (maxDist > bestDist)
                    {
                        bestDist = maxDist;
                        bestFace = face;
                    }
                }

                if (bestFace == null || bestDist < FaceDistEps)
                    break; // Done — no more points outside the hull

                if (++iteration > maxIterations)
                    break; // Safety

                // Find the farthest point (eye) from the best face
                int eyeIndex = -1;
                float eyeDist = -1f;
                foreach (int pi in bestFace.OutsideSet)
                {
                    float d = bestFace.DistanceToPlane(points[pi]);
                    if (d > eyeDist)
                    {
                        eyeDist = d;
                        eyeIndex = pi;
                    }
                }
                if (eyeIndex < 0) break;

                Vector3 eye = points[eyeIndex];

                // Find all visible faces from the eye point
                visibleFaces.Clear();
                FindVisibleFaces(faces, points, eye, visibleFaces);

                if (visibleFaces.Count == 0)
                    break; // Should not happen

                // Find horizon edges (edges shared by a visible and non-visible face)
                horizonEdges.Clear();
                FindHorizonEdges(faces, visibleFaces, horizonEdges);

                // Remove visible faces
                foreach (var vf in visibleFaces)
                {
                    faces.Remove(vf);
                }

                // Create new faces from horizon edges + eye point
                newFaces.Clear();
                foreach (var edge in horizonEdges)
                {
                    var newFace = new Face(edge.A, edge.B, eyeIndex);
                    // Orient away from the visible faces (which were removed)
                    // The new face should have the same winding as the visible face that shared this edge
                    newFace.ComputePlane(points);
                    // Ensure it faces outward
                    var adjFace = FindAdjacentFace(faces, edge);
                    if (adjFace != null)
                    {
                        // The adjacent (non-visible) face's normal should point outward.
                        // The new face should point outward too.
                        Vector3 edgeMid = (points[edge.A] + points[edge.B]) * 0.5f;
                        Vector3 outDir = adjFace.Normal; // non-visible face normal
                        Vector3 newFaceCenter = (points[newFace.V0] + points[newFace.V1] + points[newFace.V2]) * (1f/3f);
                        Vector3 toCenter = newFaceCenter - edgeMid;
                        // The new face normal should point away from the eye relative to the edge
                        Vector3 toEye = eye - edgeMid;
                        float sideEye = Vector3.Dot(newFace.Normal, toEye);
                        if (sideEye > 0)
                            newFace.Flip();
                    }
                    else
                    {
                        // No adjacent face — orient away from the visible hull center
                        float side = newFace.DistanceToPlane(tetCenter);
                        if (side > 0)
                            newFace.Flip();
                    }
                    newFaces.Add(newFace);
                }

                // Distribute orphaned points (from removed visible faces) to new faces
                var allOrphans = new HashSet<int>();
                foreach (var vf in visibleFaces)
                {
                    foreach (int pi in vf.OutsideSet)
                    {
                        if (pi != eyeIndex)
                            allOrphans.Add(pi);
                    }
                }

                foreach (var nf in newFaces)
                {
                    // The eye point is on the hull — mark it
                    // Distribute orphans
                    var toRemove = new List<int>();
                    foreach (int pi in allOrphans)
                    {
                        float dist = nf.DistanceToPlane(points[pi]);
                        if (dist > FaceDistEps)
                        {
                            nf.OutsideSet.Add(pi);
                            toRemove.Add(pi);
                        }
                    }
                    foreach (int pi in toRemove)
                        allOrphans.Remove(pi);
                }

                // Add new faces
                faces.AddRange(newFaces);
            }

            // --- Step 5: Extract hull triangles ---
            // Build vertex set from all face vertices
            var hullVerts = new HashSet<int>();
            foreach (var face in faces)
            {
                hullVerts.Add(face.V0);
                hullVerts.Add(face.V1);
                hullVerts.Add(face.V2);
            }

            if (hullVerts.Count < 3)
                return null;

            // Create vertex array and index buffer
            var hullVertList = hullVerts.ToList();
            var hullVertArr = new Vector3[hullVertList.Count];
            var hullIdxArr = new int[faces.Count * 3];

            // Map old indices to new compact indices
            var indexMap = new Dictionary<int, int>();
            for (int i = 0; i < hullVertList.Count; i++)
            {
                hullVertArr[i] = points[hullVertList[i]];
                indexMap[hullVertList[i]] = i;
            }

            int outIdx = 0;
            foreach (var face in faces)
            {
                hullIdxArr[outIdx++] = indexMap[face.V0];
                hullIdxArr[outIdx++] = indexMap[face.V1];
                hullIdxArr[outIdx++] = indexMap[face.V2];
            }

            return (hullVertArr, hullIdxArr);
        }

        /// <summary>Find the initial tetrahedron from extreme points.</summary>
        private static bool FindInitialTetrahedron(Vector3[] points, out int[] tetra)
        {
            tetra = new int[4];

            // Find extreme points along each axis
            int minX = 0, maxX = 0, minY = 0, maxY = 0, minZ = 0, maxZ = 0;
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i].X < points[minX].X) minX = i;
                if (points[i].X > points[maxX].X) maxX = i;
                if (points[i].Y < points[minY].Y) minY = i;
                if (points[i].Y > points[maxY].Y) maxY = i;
                if (points[i].Z < points[minZ].Z) minZ = i;
                if (points[i].Z > points[maxZ].Z) maxZ = i;
            }

            // Pick the pair with largest distance as first edge
            float dX = Vector3.DistanceSquared(points[minX], points[maxX]);
            float dY = Vector3.DistanceSquared(points[minY], points[maxY]);
            float dZ = Vector3.DistanceSquared(points[minZ], points[maxZ]);
            
            int e0, e1;
            if (dX >= dY && dX >= dZ) { e0 = minX; e1 = maxX; }
            else if (dY >= dZ) { e0 = minY; e1 = maxY; }
            else { e0 = minZ; e1 = maxZ; }

            if (Vector3.DistanceSquared(points[e0], points[e1]) < Eps)
                return false; // All points are coincident

            // Find farthest point from line e0-e1
            int third = -1;
            float maxDist2 = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                if (i == e0 || i == e1) continue;
                float d = PointLineDistSq(points[i], points[e0], points[e1]);
                if (d > maxDist2)
                {
                    maxDist2 = d;
                    third = i;
                }
            }

            if (third < 0 || maxDist2 < Eps)
                return false; // All points are collinear

            // Find farthest point from plane e0,e1,third
            int fourth = -1;
            maxDist2 = 0f;
            Vector3 planeNormal = Vector3.Normalize(Vector3.Cross(points[e1] - points[e0], points[third] - points[e0]));
            for (int i = 0; i < points.Length; i++)
            {
                if (i == e0 || i == e1 || i == third) continue;
                float d = MathF.Abs(Vector3.Dot(points[i] - points[e0], planeNormal));
                if (d > maxDist2)
                {
                    maxDist2 = d;
                    fourth = i;
                }
            }

            if (fourth < 0 || maxDist2 < Eps)
                return false; // All points are coplanar

            tetra[0] = e0;
            tetra[1] = e1;
            tetra[2] = third;
            tetra[3] = fourth;
            return true;
        }

        private static float PointLineDistSq(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            Vector3 ap = p - a;
            float t = Vector3.Dot(ap, ab) / Vector3.Dot(ab, ab);
            t = Math.Clamp(t, 0f, 1f);
            Vector3 closest = a + t * ab;
            return Vector3.DistanceSquared(p, closest);
        }

        /// <summary>Find all faces visible from the given point.</summary>
        private static void FindVisibleFaces(List<Face> faces, Vector3[] points, Vector3 eye, List<Face> visible)
        {
            foreach (var face in faces)
            {
                float dist = face.DistanceToPlane(eye);
                if (dist > FaceDistEps)
                    visible.Add(face);
            }
        }

        /// <summary>Find horizon edges: edges shared by a visible and non-visible face.</summary>
        private static void FindHorizonEdges(List<Face> allFaces, List<Face> visibleFaces, HashSet<Edge> horizon)
        {
            var visibleEdgeCounts = new Dictionary<Edge, int>();

            foreach (var face in visibleFaces)
            {
                CountEdge(visibleEdgeCounts, face.V0, face.V1);
                CountEdge(visibleEdgeCounts, face.V1, face.V2);
                CountEdge(visibleEdgeCounts, face.V2, face.V0);
            }

            // An edge is on the horizon if it appears in exactly 1 visible face
            foreach (var kv in visibleEdgeCounts)
            {
                if (kv.Value == 1)
                    horizon.Add(kv.Key);
            }
        }

        private static void CountEdge(Dictionary<Edge, int> dict, int a, int b)
        {
            var edge = new Edge(a, b);
            dict.TryGetValue(edge, out int count);
            dict[edge] = count + 1;
        }

        /// <summary>Find a face that shares the given edge (excluding visible faces).</summary>
        private static Face? FindAdjacentFace(List<Face> faces, Edge edge)
        {
            foreach (var face in faces)
            {
                if (face.HasEdge(edge.A, edge.B))
                    return face;
            }
            return null;
        }

        // ── Data structures ──

        private struct Edge
        {
            public readonly int A, B;
            public Edge(int a, int b)
            {
                if (a <= b) { A = a; B = b; }
                else { A = b; B = a; }
            }

            public override bool Equals(object? obj) =>
                obj is Edge e && A == e.A && B == e.B;

            public override int GetHashCode() => A * 31 + B;
        }

        private class Face
        {
            public int V0, V1, V2;
            public Vector3 Normal;
            public float D; // Plane: Normal · P + D = 0
            public List<int> OutsideSet = new();

            public Face(int v0, int v1, int v2)
            {
                V0 = v0; V1 = v1; V2 = v2;
            }

            public void ComputePlane(Vector3[] points)
            {
                Vector3 a = points[V0];
                Vector3 b = points[V1];
                Vector3 c = points[V2];
                Normal = Vector3.Cross(b - a, c - a);
                float len = Normal.Length();
                if (len > Eps)
                    Normal /= len;
                D = -Vector3.Dot(Normal, a);
            }

            /// <summary>Signed distance from point to plane. Positive = outside.</summary>
            public float DistanceToPlane(Vector3 p) =>
                Vector3.Dot(Normal, p) + D;

            /// <summary>Flip the face (reverse winding and normal).</summary>
            public void Flip()
            {
                (V1, V2) = (V2, V1);
                Normal = -Normal;
                D = -D;
            }

            /// <summary>Check if this face shares the given edge.</summary>
            public bool HasEdge(int a, int b)
            {
                return (V0 == a && V1 == b) || (V1 == a && V2 == b) || (V2 == a && V0 == b) ||
                       (V0 == b && V1 == a) || (V1 == b && V2 == a) || (V2 == b && V0 == a);
            }
        }
    }
}
