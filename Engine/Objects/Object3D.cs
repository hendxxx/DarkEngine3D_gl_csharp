using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;
using System.Runtime.InteropServices;
using DarkEngine3D_gl_csharp.Engine.Utils;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    public unsafe class Object3D
    {
       

        private uint _shaderProgram;
        public uint ShaderProgram
        {
            get => _shaderProgram;
            private set => _shaderProgram = value;
        }

        private int modelLocation;
        private Vector3 objectPosition;
        private Vector3[] localVertices = [];
        private Matrix4x4 modelMatrix;

        public uint VAO, VBO;
        private int _vertexCount;
        public int VertexCount => _vertexCount;
        private int useTexture;

        // Flag agar Draw() tidak timpa modelMatrix kalo sudah di-set via UpdateModelMatriC
        private bool _useCustomModelMatrix = false;

        // NEW: joints for skinning (safe default)
        private Matrix4x4[] joints = Array.Empty<Matrix4x4>();

        // NEW: semaphore example for thread-safety if needed
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public Object3D(  float x, float y, float z)
        {
             

            ShaderProgram = Shader.GetShaderProgram();
            GL.UseProgram(ShaderProgram);
            SetPosition(x, y, z);
        }

        public void SetPosition(float x, float y, float z)
        {
            objectPosition = new Vector3(x, y, z);
        }
        public Vector3 GetPosition() => objectPosition;

        public void Generate(uint shaderProgram, Vertex[] vertices)
        {
            useTexture = GL.GetUniformLocation(shaderProgram, "useTexture");

            // SIMPAN POSISI LOCAL-SPACE
            localVertices = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                localVertices[i] = vertices[i].Position;

            _vertexCount = vertices.Length;

            SetupGPUResources(vertices);

            modelLocation = GL.GetUniformLocation(shaderProgram, "model");
        }

        public static Vertex[] CreateBoxVertices(float sx, float sy, float sz, Vector3 color, int tess = 1)
        {
            float hx = sx * 0.5f;
            float hy = sy * 0.5f;
            float hz = sz * 0.5f;

            Vector3 p0 = new(-hx, -hy, -hz);
            Vector3 p1 = new(hx, -hy, -hz);
            Vector3 p2 = new(hx, hy, -hz);
            Vector3 p3 = new(-hx, hy, -hz);
            Vector3 p4 = new(-hx, -hy, hz);
            Vector3 p5 = new(hx, -hy, hz);
            Vector3 p6 = new(hx, hy, hz);
            Vector3 p7 = new(-hx, hy, hz);

            Vertex V(Vector3 pos, Vector3 normal, Vector3 col, float u, float v) =>
                new Vertex(pos.X, pos.Y, pos.Z, normal.X, normal.Y, normal.Z, col.X, col.Y, col.Z, u, v);

            var verts = new List<Vertex>(36);

            // tess > 1 → subdivide each face into tess×tess quads (vertex-displacement
            // needs interior vertices; a 2-triangle face cannot move). The grid walks
            // the face corner-to-corner with the SAME corner UV mapping as the flat
            // quads below and keeps the exact CCW winding order per cell
            // (bl, tl, br) + (tl, tr, br) so culling behavior is identical.
            void Face(Vector3 o, Vector3 du, Vector3 dv, int n)
            {
                for (int iz = 0; iz < n; iz++)
                    for (int ix = 0; ix < n; ix++)
                    {
                        float u0 = (float)ix / n, u1 = (float)(ix + 1) / n;
                        float v0 = (float)iz / n, v1 = (float)(iz + 1) / n;
                        Vector3 bl = o + du * u0 + dv * v0;
                        Vector3 br = o + du * u1 + dv * v0;
                        Vector3 tl = o + du * u0 + dv * v1;
                        Vector3 tr = o + du * u1 + dv * v1;
                        var nrm = Vector3.Normalize(Vector3.Cross(dv, du));
                        verts.Add(V(bl, nrm, color, u0, v0));
                        verts.Add(V(tl, nrm, color, u0, v1));
                        verts.Add(V(br, nrm, color, u1, v0));
                        verts.Add(V(tl, nrm, color, u0, v1));
                        verts.Add(V(tr, nrm, color, u1, v1));
                        verts.Add(V(br, nrm, color, u1, v0));
                    }
            }

            // NOTE: All faces use CCW winding (front faces point outward) so that
            // back-face culling works correctly with the default GL_CCW front-face convention.
            if (tess > 1)
            {
                // du/dv per face walk the SAME corner/UV layout as the flat quads below;
                // the (bl, tl, br) triangle order makes cross(dv, du) the face normal —
                // outward for every face listed (verified against nBack/nFront/…).
                Face(p0, p1 - p0, p3 - p0, tess);   // back  (−Z out)
                Face(p5, p4 - p5, p6 - p5, tess);   // front (+Z out)
                Face(p4, p0 - p4, p7 - p4, tess);   // left  (−X out)
                // RIGHT: dv MUST be p2−p1 (pure +Y). p6−p1 is the face DIAGONAL
                // (0,2hy,2hz) — the corner walk then lands tr at p5+p6−p1 =
                // (hx, hy, 3hz), one half-depth BEYOND the front face: the right side
                // stretched into a skewed parallelogram hanging off the box (visible
                // at tess ≥ 2; tess = 1 uses the flat quads below and hid it).
                Face(p1, p5 - p1, p2 - p1, tess);   // right (+X out)
                Face(p4, p5 - p4, p0 - p4, tess);   // bottom (−Y out)
                Face(p3, p2 - p3, p7 - p3, tess);   // top (+Y out)
                return verts.ToArray();
            }

            Vector3 nBack = new(0, 0, -1);
            verts.AddRange(new[] {
                V(p0,nBack,color,0,0), V(p2,nBack,color,1,1), V(p1,nBack,color,1,0),
                V(p0,nBack,color,0,0), V(p3,nBack,color,0,1), V(p2,nBack,color,1,1)
            });

            Vector3 nFront = new(0, 0, 1);
            verts.AddRange(new[] {
                V(p5,nFront,color,0,0), V(p7,nFront,color,1,1), V(p4,nFront,color,1,0),
                V(p5,nFront,color,0,0), V(p6,nFront,color,0,1), V(p7,nFront,color,1,1)
            });

            Vector3 nLeft = new(-1, 0, 0);
            verts.AddRange(new[] {
                V(p4,nLeft,color,0,0), V(p3,nLeft,color,1,1), V(p0,nLeft,color,1,0),
                V(p4,nLeft,color,0,0), V(p7,nLeft,color,0,1), V(p3,nLeft,color,1,1)
            });

            Vector3 nRight = new(1, 0, 0);
            verts.AddRange(new[] {
                V(p1,nRight,color,0,0), V(p6,nRight,color,1,1), V(p5,nRight,color,1,0),
                V(p1,nRight,color,0,0), V(p2,nRight,color,0,1), V(p6,nRight,color,1,1)
            });

            Vector3 nBottom = new(0, -1, 0);
            verts.AddRange(new[] {
                V(p4,nBottom,color,0,0), V(p1,nBottom,color,1,1), V(p5,nBottom,color,1,0),
                V(p4,nBottom,color,0,0), V(p0,nBottom,color,0,1), V(p1,nBottom,color,1,1)
            });

            Vector3 nTop = new(0, 1, 0);
            verts.AddRange(new[] {
                V(p3,nTop,color,0,0), V(p6,nTop,color,1,1), V(p2,nTop,color,1,0),
                V(p3,nTop,color,0,0), V(p7,nTop,color,0,1), V(p6,nTop,color,1,1)
            });

            return verts.ToArray();
        }

        public static Object3D CreateBoxObject( float sx, float sy, float sz, float px, float py, float pz, Vector3 color)
        {
            var obj = new Object3D( 0, 0, 0);
            Vertex[] boxVerts = CreateBoxVertices(sx, sy, sz, color);
            obj.Generate(obj.ShaderProgram, boxVerts);
            obj.SetPosition(px, py, pz);
            obj.modelMatrix = Matrix4x4.CreateTranslation(new Vector3(px, py, pz));
            return obj;
        }

        /// <summary>
        /// Create simple sphere vertices with proper normals for lighting.
        /// Uses indexed triangle approach - no degenerate poles.
        /// </summary>
        public static Vertex[] CreateSphereVertices(float radius, Vector3 color, int slices = 12, int stacks = 8)
        {
            var verts = new List<Vertex>();

            for (int i = 0; i <= stacks; i++)
            {
                float stackAngle = MathF.PI * 0.5f - i * MathF.PI / stacks;
                float xy = radius * MathF.Cos(stackAngle);
                float z = radius * MathF.Sin(stackAngle);

                for (int j = 0; j <= slices; j++)
                {
                    float sliceAngle = j * 2f * MathF.PI / slices;
                    float x = xy * MathF.Cos(sliceAngle);
                    float y = xy * MathF.Sin(sliceAngle);

                    Vector3 pos = new(x, y, z);
                    Vector3 normal = Vector3.Normalize(pos);
                    float u = (float)j / slices;
                    float v = (float)i / stacks;

                    verts.Add(new Vertex(pos.X, pos.Y, pos.Z, normal.X, normal.Y, normal.Z, color.X, color.Y, color.Z, u, v));
                }
            }

            var triangles = new List<Vertex>();
            for (int i = 0; i < stacks; i++)
            {
                for (int j = 0; j < slices; j++)
                {
                    int first = i * (slices + 1) + j;
                    int second = first + slices + 1;

                    triangles.Add(verts[first]);
                    triangles.Add(verts[second]);
                    triangles.Add(verts[first + 1]);

                    triangles.Add(verts[second]);
                    triangles.Add(verts[second + 1]);
                    triangles.Add(verts[first + 1]);
                }
            }

            return triangles.ToArray();
        }

        private static Vertex MakeV(Vector3 pos, Vector3 color) =>
            new Vertex(pos.X, pos.Y, pos.Z, pos.X, pos.Y, pos.Z, color.X, color.Y, color.Z, 0f, 0f);

        public static Object3D CreateSphereObject(float radius, float px, float py, float pz, Vector3 color)
        {
            var obj = new Object3D(0, 0, 0);
            Vertex[] sphereVerts = CreateSphereVertices(radius, color);
            obj.Generate(obj.ShaderProgram, sphereVerts);
            obj.SetPosition(px, py, pz);
            return obj;
        }

        /// <summary>
        /// Icosphere: start from a regular icosahedron (20 triangles) and subdivide each
        /// triangle by splitting every edge, producing 4 new triangles per original face.
        /// New vertex positions are normalized back to the sphere surface so the shape stays
        /// round. Subdivision depth = <paramref name="depth"/>: 1 = base icosahedron, 2 =
        /// 80 triangles, 3 = 320, and so on (4x growth per depth). Icospheres have no poles
        /// and distribute vertices evenly, so they stay rounder than a UV sphere at the same
        /// vertex count. Depth 0..6 is reasonable; higher values grow fast.</summary>
        public static Vertex[] CreateIcoSphereVertices(float radius, Vector3 color, int depth)
        {
            depth = Math.Clamp(depth, 0, 6);
            // Base icosahedron vertices (unit sphere). 12 vertices, 20 faces.
            const float tau = 1.6180339887498948482f;
            var verts = new Vertex[12];
            verts[0] = MakeV(new Vector3(-1f, tau, 0f), color);
            verts[1] = MakeV(new Vector3(1f, tau, 0f), color);
            verts[2] = MakeV(new Vector3(-1f, -tau, 0f), color);
            verts[3] = MakeV(new Vector3(1f, -tau, 0f), color);
            verts[4] = MakeV(new Vector3(0f, -1f, tau), color);
            verts[5] = MakeV(new Vector3(0f, 1f, tau), color);
            verts[6] = MakeV(new Vector3(0f, -1f, -tau), color);
            verts[7] = MakeV(new Vector3(0f, 1f, -tau), color);
            verts[8] = MakeV(new Vector3(tau, 0f, -1f), color);
            verts[9] = MakeV(new Vector3(tau, 0f, 1f), color);
            verts[10] = MakeV(new Vector3(-tau, 0f, -1f), color);
            verts[11] = MakeV(new Vector3(-tau, 0f, 1f), color);
            for (int i = 0; i < verts.Length; i++) verts[i].Position = Vector3.Normalize(verts[i].Position);
            // 20 faces of the icosahedron (vertex triplets).
            var faces = new (int a, int b, int c)[]
            {
                (0, 4, 1), (0, 9, 4), (9, 5, 4), (4, 5, 11), (5, 10, 11), (3, 10, 5),
                (2, 3, 10), (2, 8, 3), (2, 7, 8), (7, 11, 8), (7, 6, 11), (6, 0, 11),
                (6, 10, 7), (6, 1, 0), (1, 6, 10), (1, 10, 11), (1, 11, 4), (2, 7, 6),
                (0, 8, 9), (8, 2, 9)
            };
            for (int d = 0; d < depth; d++)
            {
                var nextFaces = new List<(int a, int b, int c)>(faces.Length * 4);
                var edgeMid = new Dictionary<(int a, int b), int>(faces.Length * 3);
                int vertCount = verts.Length;
                foreach (var f in faces)
                {
                    int ab = GetOrAddMid(edgeMid, verts, ref vertCount, f.a, f.b, color);
                    int bc = GetOrAddMid(edgeMid, verts, ref vertCount, f.b, f.c, color);
                    int ca = GetOrAddMid(edgeMid, verts, ref vertCount, f.c, f.a, color);
                    nextFaces.Add((f.a, ab, ca));
                    nextFaces.Add((f.b, bc, ab));
                    nextFaces.Add((f.c, ca, bc));
                    nextFaces.Add((ab, bc, ca));
                }
                faces = [.. nextFaces];
            }
            var tris = new List<Vertex>(faces.Length * 3);
            foreach (var f in faces)
            {
                tris.Add(verts[f.a]);
                tris.Add(verts[f.b]);
                tris.Add(verts[f.c]);
            }
            return tris.ToArray();

            static int GetOrAddMid(Dictionary<(int a, int b), int> m, Vertex[] v, ref int count, int a, int b, Vector3 color)
            {
                if (a > b) (a, b) = (b, a);
                if (m.TryGetValue((a, b), out var mid)) return mid;
                mid = count++;
                var pos = Vector3.Normalize((v[a].Position + v[b].Position) * 0.5f);
                Array.Resize(ref v, v.Length + 1);
                v[v.Length - 1] = MakeV(pos, color);
                m[(a, b)] = mid;
                return mid;
            }
        }

        /// <summary>
        /// Cube sphere: a unit cube (6 faces) whose vertices are normalized onto the
        /// sphere surface. Each face is a tess x tess grid of quads (2 tris each), so the
        /// total triangle count is 6 * tess * tess * 2. Lower tess values keep visibly
        /// faceted but rounder faces than a UV sphere of similar vertex count; poles are a
        /// 4-vertex seam rather than a single-vertex pinching.</summary>
        public static Vertex[] CreateCubeSphereVertices(float radius, Vector3 color, int tess)
        {
            if (tess < 1) tess = 1;
            Vector3 posOnCube(int ix, int iz, bool px, bool py, bool pz)
            {
                float u = ix / (float)tess - 0.5f;
                float v = iz / (float)tess - 0.5f;
                if (px) return new Vector3(0.5f, u, v);
                if (py) return new Vector3(u, 0.5f, v);
                return new Vector3(u, v, 0.5f);
            }
            Vertex V(Vector3 p, float u, float v)
            {
                var n = Vector3.Normalize(p);
                p = n * radius;
                return new Vertex(p.X, p.Y, p.Z, n.X, n.Y, n.Z, color.X, color.Y, color.Z, u, v);
            }
            var outv = new List<Vertex>(6 * tess * tess * 6);
            foreach (var (px, py, pz) in new[]
                {
                    (true, false, false), (false, true, false), (false, false, true),
                    (true, true, false), (true, false, true), (false, true, true)
                })
            {
                for (int iz = 0; iz < tess; iz++)
                    for (int ix = 0; ix < tess; ix++)
                    {
                        float u0 = (float)ix / tess, u1 = (float)(ix + 1) / tess;
                        float v0 = (float)iz / tess, v1 = (float)(iz + 1) / tess;
                        var p00 = V(posOnCube(ix, iz, px, py, pz), u0, v0);
                        var p10 = V(posOnCube(ix + 1, iz, px, py, pz), u1, v0);
                        var p01 = V(posOnCube(ix, iz + 1, px, py, pz), u0, v1);
                        var p11 = V(posOnCube(ix + 1, iz + 1, px, py, pz), u1, v1);
                        outv.Add(p00); outv.Add(p10); outv.Add(p01);
                        outv.Add(p10); outv.Add(p11); outv.Add(p01);
                    }
            }
            return outv.ToArray();
        }

        /// <summary>
        /// Create a flat plane (ground quad) vertices with proper normals for lighting.
        /// The plane lies in XZ plane at Y=0, facing up.
        /// </summary>
        public static Vertex[] CreatePlaneVertices(float sizeX, float sizeZ, Vector3 color, int segmentsX = 1, int segmentsZ = 1)
        {
            var verts = new List<Vertex>();

            float hx = sizeX * 0.5f;
            float hz = sizeZ * 0.5f;
            Vector3 normal = Vector3.UnitY;

            for (int iz = 0; iz < segmentsZ; iz++)
            {
                for (int ix = 0; ix < segmentsX; ix++)
                {
                    float x0 = -hx + (ix / (float)segmentsX) * sizeX;
                    float x1 = -hx + ((ix + 1) / (float)segmentsX) * sizeX;
                    float z0 = -hz + (iz / (float)segmentsZ) * sizeZ;
                    float z1 = -hz + ((iz + 1) / (float)segmentsZ) * sizeZ;

                    float u0 = (float)ix / segmentsX;
                    float u1 = (float)(ix + 1) / segmentsX;
                    float v0 = (float)iz / segmentsZ;
                    float v1 = (float)(iz + 1) / segmentsZ;

                    // First triangle (CCW when viewed from above +Y so the plane is front-facing)
                    verts.Add(new Vertex(x0, 0, z0, normal.X, normal.Y, normal.Z, color.X, color.Y, color.Z, u0, v0));
                    verts.Add(new Vertex(x1, 0, z1, normal.X, normal.Y, normal.Z, color.X, color.Y, color.Z, u1, v1));
                    verts.Add(new Vertex(x1, 0, z0, normal.X, normal.Y, normal.Z, color.X, color.Y, color.Z, u1, v0));

                    // Second triangle
                    verts.Add(new Vertex(x0, 0, z0, normal.X, normal.Y, normal.Z, color.X, color.Y, color.Z, u0, v0));
                    verts.Add(new Vertex(x0, 0, z1, normal.X, normal.Y, normal.Z, color.X, color.Y, color.Z, u0, v1));
                    verts.Add(new Vertex(x1, 0, z1, normal.X, normal.Y, normal.Z, color.X, color.Y, color.Z, u1, v1));
                }
            }

            return [.. verts];
        }

        public static Object3D CreatePlaneObject(float sizeX, float sizeZ, float px, float py, float pz, Vector3 color)
        {
            var obj = new Object3D(0, 0, 0);
            Vertex[] planeVerts = CreatePlaneVertices(sizeX, sizeZ, color);
            obj.Generate(obj.ShaderProgram, planeVerts);
            obj.SetPosition(px, py, pz);
            obj.modelMatrix = Matrix4x4.CreateTranslation(new Vector3(px, py, pz));
            return obj;
        }

        public static Object3D[] SpawnFourRandomBigBoxes(int seed = 12345, float areaRadius = 60f)
        {
            var rng = new Random(seed);
            var boxes = new Object3D[4];
            for (int i = 0; i < 4; i++)
            {
                float sx = rng.NextFloat(8.0f, 18.0f);
                float sy = rng.NextFloat(6.0f, 14.0f);
                float sz = rng.NextFloat(8.0f, 18.0f);

                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float dist = rng.NextFloat(areaRadius * 0.2f, areaRadius);
                float px = MathF.Cos(angle) * dist;
                float pz = MathF.Sin(angle) * dist;
                Vector3 color = new Vector3(0.4f + 0.15f * i, 0.2f + 0.1f * (3 - i), 0.3f);

                boxes[i] = CreateBoxObject(sx, sy, sz, px, sy * 0.5f, pz, color);
            }
            return boxes;
        }
        private void SetupGPUResources(Vertex[] data)
        {
            fixed (uint* pVao = &VAO) GL.GenVertexArrays(1, pVao);
            fixed (uint* pVbo = &VBO) GL.GenBuffers(1, pVbo);

            GL.BindVertexArray(VAO);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, VBO);

            fixed (void* ptr = data)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(data.Length * sizeof(Vertex)), ptr, Const.GL_STATIC_DRAW);
            }

            int stride = sizeof(Vertex);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)sizeof(Vector3));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, Const.GL_FLOAT, false, stride, (void*)(sizeof(Vector3) * 2));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 2, Const.GL_FLOAT, false, stride, (void*)(sizeof(Vector3) * 3));
        }

        public void Draw(float deltaTime, nint window, float _moveSpeed)
        {
            // Culling state is applied by the scene's RenderProperties — do not override it here.
            if (!_useCustomModelMatrix)
                modelMatrix = Matrix4x4.CreateTranslation(objectPosition);
            _useCustomModelMatrix = false; // reset setelah dipakai

            fixed (float* ptr = &modelMatrix.M11)
            {
                GL.UniformMatrix4fv(modelLocation, 1, false, ptr);
            }

            GL.Uniform1i(useTexture, 0);

            GL.BindVertexArray(VAO);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, _vertexCount);
            GL.BindVertexArray(0);
        }

        public void RenderShadow(Camera camera, CSM csm, int cascadeIndex, uint shadowShader, int modelLoc)
        {
            GL.UseProgram(shadowShader);

            // Live normal-bias tuning (Shadow Settings panel) — game primitive shadows.
            Visual.ShadowUniforms.UploadNormalBias(shadowShader);

            // Culling state is set by the CSM shadow pass — do not override it here.

            fixed (float* ptr = &modelMatrix.M11)
            {
                GL.UniformMatrix4fv(modelLoc, 1, false, ptr);
            }

            // Upload the inverse-transpose (normal matrix) so the shadow shader's normal-bias
            // extrusion stays correct under non-uniform scale / rotation. Without it the
            // extrusion follows the raw model-space normal, pushing some faces INTO the
            // shadow map — the dark stripes that follow the geometry.
            Visual.ShadowUniforms.UploadShadowNormalMatrix(shadowShader, modelMatrix);

            Plane[]? orthoPlanes = CSM.BuildPlanesFromCorners(csm.OrthoCorners[cascadeIndex]);

            if (!IsObjectInsideOrthoFrustum(orthoPlanes)) return;

            GL.BindVertexArray(VAO);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, _vertexCount);
            GL.BindVertexArray(0);
        }

        private bool IsObjectInsideOrthoFrustum(Plane[]? planes)
        {
            if (planes == null) return true;

            Vector3[] world = new Vector3[localVertices.Length];
            for (int i = 0; i < localVertices.Length; i++)
                world[i] = Vector3.Transform(localVertices[i], modelMatrix);

            Vector3 min = world[0];
            Vector3 max = world[0];

            for (int i = 1; i < world.Length; i++)
            {
                min = Vector3.Min(min, world[i]);
                max = Vector3.Max(max, world[i]);
            }

            Vector3[] corners =
            {
                new(min.X, min.Y, min.Z),
                new(max.X, min.Y, min.Z),
                new(max.X, max.Y, min.Z),
                new(min.X, max.Y, min.Z),

                new(min.X, min.Y, max.Z),
                new(max.X, min.Y, max.Z),
                new(max.X, max.Y, max.Z),
                new(min.X, max.Y, max.Z),
            };

            foreach (var p in planes)
            {
                float maxDist = float.NegativeInfinity;

                for (int i = 0; i < 8; i++)
                {
                    float d = Vector3.Dot(p.Normal, corners[i]) + p.D;
                    if (d > maxDist) maxDist = d;
                }

                if (maxDist < 0.0f)
                    return false;
            }

            return true;
        }

        /// <summary>Compute world-space AABB dari local vertices + position.</summary>
        public Helpers.ObjectHelpers.AABB GetWorldAABB()
        {
            if (localVertices == null || localVertices.Length == 0)
                return new Helpers.ObjectHelpers.AABB(objectPosition, objectPosition);
            var mn = localVertices[0] + objectPosition;
            var mx = localVertices[0] + objectPosition;
            for (int i = 1; i < localVertices.Length; i++)
            {
                var wp = localVertices[i] + objectPosition;
                mn = Vector3.Min(mn, wp);
                mx = Vector3.Max(mx, wp);
            }
            return new Helpers.ObjectHelpers.AABB(mn, mx);
        }

        // NEW: safe setter for joints
        public void SetJoints(Matrix4x4[] newJoints)
        {
            joints = newJoints ?? Array.Empty<Matrix4x4>();
        }

         public void UpdateModelMatriC(Matrix4x4 newModel)
        {
            modelMatrix = newModel;
            _useCustomModelMatrix = true;
        }
    }
}
