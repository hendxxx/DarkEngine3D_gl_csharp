using DarkEngine3D_gl_csharp.Engine.Libs;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    // ===========================================================================
    //  AABB Collision Box
    // ===========================================================================
    public struct AABB
    {
        public Vector3 Min, Max;

        public AABB(Vector3 min, Vector3 max) { Min = min; Max = max; }

        public readonly bool Intersects(AABB other) =>
            Min.X <= other.Max.X && Max.X >= other.Min.X &&
            Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
            Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

        public readonly AABB ToWorld(Vector3 worldPos, float scale = 1f)
        {
            var wMin = Min * scale + worldPos;
            var wMax = Max * scale + worldPos;
            return new AABB(wMin, wMax);
        }

        public static AABB FromVertices(SkinnedVertex[] verts)
        {
            if (verts.Length == 0) return new AABB(Vector3.Zero, Vector3.Zero);
            var mn = verts[0].Position;
            var mx = verts[0].Position;
            foreach (var v in verts)
            {
                mn = Vector3.Min(mn, v.Position);
                mx = Vector3.Max(mx, v.Position);
            }
            return new AABB(mn, mx);
        }
    }

    // ===========================================================================
    //  MeshGpu — per-primitive GPU buffers
    // ===========================================================================
    public struct MeshGpu
    {
        public uint VAO, VBO, EBO;
        public int  VertexCount;
        public int  IndexCount;
    }

    // ===========================================================================
    //  GltfModelGpuData — shared GPU data (Flyweight pattern)
    // ===========================================================================
    public unsafe class GltfModelGpuData
    {
        public readonly GltfData Data;
        public readonly MeshGpu[] Meshes;
        public readonly AABB LocalAABB;

        public GltfModelGpuData(GltfData data)
        {
            Data   = data;
            Meshes = new MeshGpu[data.Meshes.Length];
            UploadToGpu(data);
            LocalAABB = data.Meshes.Length > 0
                ? AABB.FromVertices(data.Meshes[0].Vertices)
                : new AABB(Vector3.Zero, Vector3.One);
        }

        private void UploadToGpu(GltfData data)
        {
            int stride = Marshal.SizeOf<SkinnedVertex>(); // 32 bytes

            for (int m = 0; m < data.Meshes.Length; m++)
            {
                var mesh = data.Meshes[m];

                uint vao, vbo, ebo = 0;
                GL.GenVertexArrays(1, &vao);
                GL.GenBuffers(1, &vbo);
                GL.BindVertexArray(vao);

                // Upload vertices
                GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
                fixed (SkinnedVertex* ptr = mesh.Vertices)
                    GL.BufferData(Const.GL_ARRAY_BUFFER,
                        (nuint)(mesh.Vertices.Length * stride),
                        ptr, Const.GL_STATIC_DRAW);

                // Upload indices
                if (mesh.Indices.Length > 0)
                {
                    GL.GenBuffers(1, &ebo);
                    GL.BindBuffer(Const.GL_ELEMENT_ARRAY_BUFFER, ebo);
                    fixed (uint* iptr = mesh.Indices)
                        GL.BufferData(Const.GL_ELEMENT_ARRAY_BUFFER,
                            (nuint)(mesh.Indices.Length * sizeof(uint)),
                            iptr, Const.GL_STATIC_DRAW);
                }

                // Vertex attributes
                // loc 0: Position (vec3) offset 0
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);

                // loc 1: Normal (vec3) offset 12
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)12);

                // loc 2: UV (vec2) offset 24
                GL.EnableVertexAttribArray(2);
                GL.VertexAttribPointer(2, 2, Const.GL_FLOAT, false, stride, (void*)24);

                GL.BindVertexArray(0);

                Meshes[m] = new MeshGpu
                {
                    VAO = vao, VBO = vbo, EBO = ebo,
                    VertexCount = mesh.Vertices.Length,
                    IndexCount  = mesh.Indices.Length
                };

                Console.WriteLine($"  [GltfGPU] Mesh[{m}] VAO={vao} verts={mesh.Vertices.Length} idx={mesh.Indices.Length} stride={stride}");
            }

            Console.WriteLine($"[GltfGPU] AABB min={LocalAABB.Min} max={LocalAABB.Max}");
        }

        public void Dispose()
        {
            foreach (var m in Meshes)
            {
                uint vao = m.VAO, vbo = m.VBO, ebo = m.EBO;
                GL.DeleteVertexArrays(1, &vao);
                GL.DeleteBuffers(1, &vbo);
                if (ebo != 0) GL.DeleteBuffers(1, &ebo);
            }
        }
    }

    // ===========================================================================
    //  GltfObject — satu instance (posisi, orientasi, scale)
    // ===========================================================================
    public unsafe class GltfObject
    {
        public readonly GltfModelGpuData GpuData;

        public Vector3    Position;
        public Quaternion Rotation;
        public float      Scale = 1f;

        public AABB LocalAABB => GpuData.LocalAABB;
        public AABB WorldAABB => LocalAABB.ToWorld(Position, Scale);

        public GltfObject(GltfModelGpuData gpuData, Vector3 position, Quaternion rotation, float scale = 1f)
        {
            GpuData  = gpuData;
            Position = position;
            Rotation = rotation;
            Scale    = scale;
        }

        public void SetFacing(float yawDegrees)
            => Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f);

        /// <summary>Tidak ada animasi — Update kosong, reserved untuk masa depan.</summary>
        public void Update(float dt) { }

        /// <summary>Kirim draw call ke GPU.</summary>
        public void Draw(int modelLoc)
        {
            var modelMat = Matrix4x4.CreateScale(Scale)
                         * Matrix4x4.CreateFromQuaternion(Rotation)
                         * Matrix4x4.CreateTranslation(Position);

            GL.UniformMatrix4fv(modelLoc, 1, false, (float*)Unsafe.AsPointer(ref modelMat));

            foreach (var mesh in GpuData.Meshes)
            {
                GL.BindVertexArray(mesh.VAO);
                if (mesh.IndexCount > 0)
                    GL.DrawElements(Const.GL_TRIANGLES, mesh.IndexCount, Const.GL_UNSIGNED_INT, null);
                else
                    GL.DrawArrays(Const.GL_TRIANGLES, 0, mesh.VertexCount);
            }
            GL.BindVertexArray(0);
        }
    }
}
