using DarkEngine3D_gl_csharp.Engine.Libs;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    public class InstanceBatch : IDisposable
    {
        public uint VAO;
        public uint VBO_Instance;
        public int MeshIndex;
        public int LODLevel;
        public List<Matrix4x4> Instances = new();
        public int UploadedCount = 0;
        public int MaxInstances = 0;
        public bool Dirty = true;

        public InstanceBatch(uint vao, int meshIndex, int lodLevel)
        {
            VAO = vao;
            MeshIndex = meshIndex;
            LODLevel = lodLevel;

            unsafe
            {
                // Create instance VBO
                uint vbo;
                GL.GenBuffers(1, &vbo);
                VBO_Instance = vbo;

                // Bind VAO and configure instance attributes (locations 3,4,5,6)
                GL.BindVertexArray(VAO);
                GL.BindBuffer(Const.GL_ARRAY_BUFFER, VBO_Instance);

                uint sizeOfVec4 = (uint)(4 * sizeof(float));
                uint sizeOfMat4 = (uint)(16 * sizeof(float));

                // location 3
                GL.EnableVertexAttribArray(3);
                GL.VertexAttribPointer(3, 4, Const.GL_FLOAT, false, (int)sizeOfMat4, (void*)0);
                GL.VertexAttribDivisor(3, 1);

                // location 4
                GL.EnableVertexAttribArray(4);
                GL.VertexAttribPointer(4, 4, Const.GL_FLOAT, false, (int)sizeOfMat4, (void*)sizeOfVec4);
                GL.VertexAttribDivisor(4, 1);

                // location 5
                GL.EnableVertexAttribArray(5);
                GL.VertexAttribPointer(5, 4, Const.GL_FLOAT, false, (int)sizeOfMat4, (void*)(2 * sizeOfVec4));
                GL.VertexAttribDivisor(5, 1);

                // location 6
                GL.EnableVertexAttribArray(6);
                GL.VertexAttribPointer(6, 4, Const.GL_FLOAT, false, (int)sizeOfMat4, (void*)(3 * sizeOfVec4));
                GL.VertexAttribDivisor(6, 1);

                GL.BindVertexArray(0);
                GL.BindBuffer(Const.GL_ARRAY_BUFFER, 0);
            }
        }

        public void UploadData()
        {
            if (!Dirty) return;

            if (Instances.Count == 0)
            {
                UploadedCount = 0;
                Dirty = false;
                return;
            }

            unsafe
            {
                GL.BindBuffer(Const.GL_ARRAY_BUFFER, VBO_Instance);
                
                int requiredSize = Instances.Count * 16 * sizeof(float);

                if (Instances.Count > MaxInstances)
                {
                    // Allocate new buffer with some extra capacity to avoid frequent reallocations
                    MaxInstances = (int)(Instances.Count * 1.5f);
                    int newSize = MaxInstances * 16 * sizeof(float);
                    
                    fixed (Matrix4x4* ptr = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Instances))
                    {
                        GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)newSize, ptr, Const.GL_DYNAMIC_DRAW);
                    }
                }
                else
                {
                    // Update existing buffer
                    fixed (Matrix4x4* ptr = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Instances))
                    {
                        GL.BufferSubData(Const.GL_ARRAY_BUFFER, 0, (nuint)requiredSize, ptr);
                    }
                }

                GL.BindBuffer(Const.GL_ARRAY_BUFFER, 0);
            }

            UploadedCount = Instances.Count;
            Dirty = false;
        }

        public void Dispose()
        {
            unsafe
            {
                uint vbo = VBO_Instance;
                GL.DeleteBuffers(1, &vbo);
            }
        }
    }
}
