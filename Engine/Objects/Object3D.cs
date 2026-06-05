using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System.Numerics;
using System.Runtime.InteropServices;
using DarkEngine3D_gl_csharp.Engine.Utils;
using DarkEngine3D_gl_csharp.Engine.Terrains; // for RandomExtensions.NextFloat

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
        private Vector3[] localVertices;
        private Matrix4x4 modelMatrix;

        public uint VAO, VBO;
        private int _vertexCount;
        private int useTexture;

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

        public static Vertex[] CreateBoxVertices(float sx, float sy, float sz, Vector3 color)
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

            Vector3 nBack = new(0, 0, -1);
            verts.AddRange(new[] {
                V(p0,nBack,color,0,0), V(p1,nBack,color,1,0), V(p2,nBack,color,1,1),
                V(p0,nBack,color,0,0), V(p2,nBack,color,1,1), V(p3,nBack,color,0,1)
            });

            Vector3 nFront = new(0, 0, 1);
            verts.AddRange(new[] {
                V(p5,nFront,color,0,0), V(p4,nFront,color,1,0), V(p7,nFront,color,1,1),
                V(p5,nFront,color,0,0), V(p7,nFront,color,1,1), V(p6,nFront,color,0,1)
            });

            Vector3 nLeft = new(-1, 0, 0);
            verts.AddRange(new[] {
                V(p4,nLeft,color,0,0), V(p0,nLeft,color,1,0), V(p3,nLeft,color,1,1),
                V(p4,nLeft,color,0,0), V(p3,nLeft,color,1,1), V(p7,nLeft,color,0,1)
            });

            Vector3 nRight = new(1, 0, 0);
            verts.AddRange(new[] {
                V(p1,nRight,color,0,0), V(p5,nRight,color,1,0), V(p6,nRight,color,1,1),
                V(p1,nRight,color,0,0), V(p6,nRight,color,1,1), V(p2,nRight,color,0,1)
            });

            Vector3 nBottom = new(0, -1, 0);
            verts.AddRange(new[] {
                V(p4,nBottom,color,0,0), V(p5,nBottom,color,1,0), V(p1,nBottom,color,1,1),
                V(p4,nBottom,color,0,0), V(p1,nBottom,color,1,1), V(p0,nBottom,color,0,1)
            });

            Vector3 nTop = new(0, 1, 0);
            verts.AddRange(new[] {
                V(p3,nTop,color,0,0), V(p2,nTop,color,1,0), V(p6,nTop,color,1,1),
                V(p3,nTop,color,0,0), V(p6,nTop,color,1,1), V(p7,nTop,color,0,1)
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

        public static Object3D[] SpawnFourRandomBigBoxes( TerrainChunk terrain, int seed = 12345, float areaRadius = 60f)
        {
            var rng = new Random(seed);
            var boxes = new Object3D[4];
            for (int i = 0; i < 4; i++)
            {
                // use extension NextFloat from RandomExtensions or replace with inline expression
                float sx = rng.NextFloat(8.0f, 18.0f);
                float sy = rng.NextFloat(6.0f, 14.0f);
                float sz = rng.NextFloat(8.0f, 18.0f);

                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                float dist = rng.NextFloat(areaRadius * 0.2f, areaRadius);
                float px = MathF.Cos(angle) * dist;
                float pz = MathF.Sin(angle) * dist;
                float py = sy * 0.5f;

                Vector3 color = new Vector3(0.4f + 0.15f * i, 0.2f + 0.1f * (3 - i), 0.3f);

                boxes[i] = CreateBoxObject( sx, sy, sz, px, py, pz, color);

                AlignToTerrain(boxes[i], terrain);
            }
            return boxes;
        }
        public static  void AlignToTerrain(Object3D object3d, TerrainChunk terrain)
        { 
            float terrainY = terrain.GetHeightAt(object3d.GetPosition().X, object3d.GetPosition().Z);
            object3d.SetPosition(object3d.GetPosition().X, terrainY*2, object3d.GetPosition().Z);
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
        }

        public void Draw(float deltaTime, nint window, float _moveSpeed)
        {
            OpenGL.EnableFaceCulling(false);
             
            modelMatrix = Matrix4x4.CreateTranslation(objectPosition);

            fixed (float* ptr = &modelMatrix.M11)
            {
                GL.UniformMatrix4fv(modelLocation, 1, false, ptr);
            }

            GL.Uniform1i(useTexture, 0);

            GL.BindVertexArray(VAO);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, _vertexCount);
            GL.BindVertexArray(0);

            OpenGL.EnableFaceCulling(true);
        }

        public void RenderShadow(Camera camera, CSM csm, int cascadeIndex, uint shadowShader, int modelLoc)
        {
            GL.UseProgram(shadowShader);
            OpenGL.EnableFaceCulling(false);

            fixed (float* ptr = &modelMatrix.M11)
            {
                GL.UniformMatrix4fv(modelLoc, 1, false, ptr);
            }

            Plane[]? orthoPlanes = CSM.BuildPlanesFromCorners(csm.OrthoCorners[cascadeIndex]);

            if (!IsObjectInsideOrthoFrustum(orthoPlanes)) return;

            GL.BindVertexArray(VAO);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, _vertexCount);
            GL.BindVertexArray(0);

            OpenGL.EnableFaceCulling(true);
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

        // NEW: safe setter for joints
        public void SetJoints(Matrix4x4[] newJoints)
        {
            joints = newJoints ?? Array.Empty<Matrix4x4>();
        }

         public void  UpdateModelMatriC(Matrix4x4 newModel)
        { 
                modelMatrix = newModel;
             
        }
    }
}
