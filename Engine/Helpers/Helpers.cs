using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using StbImageSharp;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.Helpers
{
    public struct Matrix3x3(
        float m11, float m12, float m13,
        float m21, float m22, float m23,
        float m31, float m32, float m33)
        {
            public float M11 = m11, M12 = m12, M13 = m13;
            public float M21 = m21, M22 = m22, M23 = m23;
            public float M31 = m31, M32 = m32, M33 = m33;
        }
    public class TerrainsHelpers
    {
        // Ini untuk Noice
        public static Vector3 CalculateNormalForNoice(float x, float z)
        {
            float off = 0.1f;
            float hL = Noise.GetHeight((x - off), z);
            float hR = Noise.GetHeight((x + off), z);
            float hD = Noise.GetHeight(x, (z - off));
            float hU = Noise.GetHeight(x,  (z + off));

            // Semakin curam tanah, semakin kuat bayangannya
            Vector3 normal = new(hL - hR, 2.0f * off, hD - hU);
            return Vector3.Normalize(normal);
        }
        public static class OGLMath
        {
            public static float ToRadians(float degrees) => degrees * (MathF.PI / 180.0f);
        }
         
    }
    public class ShadeerHelpers
    {
        public static uint LoadShader(string vertexPath, string fragmentPath)
        {
            string vSource = File.ReadAllText(vertexPath);
            string fSource = File.ReadAllText(fragmentPath);

            // Compile vertex shader
            uint vs = GL.CreateShader(Const.GL_VERTEX_SHADER);
            GL.ShaderSource(vs, vSource);
            GL.CompileShader(vs);
            CheckShader(vs, $"Vertex Shader ({vertexPath})");

            // Compile fragment shader
            uint fs = GL.CreateShader(Const.GL_FRAGMENT_SHADER);
            GL.ShaderSource(fs, fSource);
            GL.CompileShader(fs);
            CheckShader(fs, $"Fragment Shader ({fragmentPath})");

            // Link program
            uint program = GL.CreateProgram();
            GL.AttachShader(program, vs);
            GL.AttachShader(program, fs);
            GL.LinkProgram(program);
            CheckProgram(program, $"Shader Program ({vertexPath} + {fragmentPath})");

            // Optional: delete shader objects after linking
            GL.DeleteShader(vs);
            GL.DeleteShader(fs); 

            return program;
        }
        public static void CheckShader(uint shader, string name)
        {
            int status = 0;

            unsafe
            {
                GL.GetShaderiv(shader, Const.GL_COMPILE_STATUS, &status);
            }

            if (status == 0)
            {
                string log = GL.GetShaderInfoLog(shader);
                Console.WriteLine($"[SHADER COMPILE ERROR] {name}\n{log}");
            }
        }

        public static void CheckProgram(uint program, string name)
        {
            int status = 0;

            unsafe
            {
                GL.GetProgramiv(program, Const.GL_LINK_STATUS, &status);
            }

            if (status == 0)
            {
                string log = GL.GetProgramInfoLog(program);
                Console.WriteLine($"[PROGRAM LINK ERROR] {name}\n{log}");
            }
        }
    }
    public class ObjectHelpers
    {
        // ===========================================================================
        //  AABB Collision Box
        // ===========================================================================
        public struct AABB(Vector3 min, Vector3 max)
        {
            public Vector3 Min = min, Max = max;

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
        //  MeshMaterialGpu — material parameters on the GPU
        // ===========================================================================
        public struct MeshMaterialGpu
        {
            public Vector4 BaseColorFactor;
            public uint TextureID;
            public bool HasTexture;
            public bool DoubleSided;
        }

        // ===========================================================================
        //  MeshGpu — per-primitive GPU buffers
        // ===========================================================================
        public struct MeshGpu
        {
            public uint VAO, VBO, EBO;
            public int VertexCount;
            public int IndexCount;
            public MeshMaterialGpu Material;
        }

        // ===========================================================================
        //  NodeTransform — per-node TRS used while sampling/blending animations.
        //  (System.Numerics row-vector convention: composed as Scale * Rotation * Translation)
        // ===========================================================================
        public struct NodeTransform
        {
            public Vector3 T;
            public Quaternion R;
            public Vector3 S;
        }

        // ===========================================================================
        //  GltfModelGpuData — shared GPU data (Flyweight pattern)
        //  Extended: map meshes to nodes (if nodes parsed)
        // ===========================================================================
        public unsafe class GltfModelGpuData
        {
            public readonly GltfData Data;
            public readonly MeshGpu[] Meshes;
            public readonly AABB LocalAABB;
            public readonly uint[] TextureIDs;

            // map mesh index -> node index (-1 if none)
            public readonly int[] MeshToNode;

            public GltfModelGpuData(GltfData data)
            {
                Data = data;
                TextureIDs = UploadTextures(data);
                Meshes = new MeshGpu[data.Meshes.Length];
                MeshToNode = new int[data.Meshes.Length];
                for (int i = 0; i < MeshToNode.Length; i++) MeshToNode[i] = -1;

                UploadToGpu(data);

                // fill mesh->node mapping (best-effort)
                if (data.Nodes != null)
                {
                    for (int ni = 0; ni < data.Nodes.Length; ni++)
                    {
                        var n = data.Nodes[ni];
                        if (n.Mesh >= 0 && n.Mesh < MeshToNode.Length)
                            MeshToNode[n.Mesh] = ni;
                    }
                }

                LocalAABB = data.Meshes.Length > 0
                    ? AABB.FromVertices(data.Meshes[0].Vertices)
                    : new AABB(Vector3.Zero, Vector3.One);
            }

            private uint[] UploadTextures(GltfData data)
            {
                if (data.Textures.Length == 0 || data.Images.Length == 0) return [];
                var ids = new uint[data.Textures.Length];
                for (int i = 0; i < data.Textures.Length; i++)
                {
                    var tex = data.Textures[i];
                    if (tex.ImageIndex < 0 || tex.ImageIndex >= data.Images.Length) continue;
                    var img = data.Images[tex.ImageIndex];
                    if (img.Data == null || img.Data.Length == 0) continue;

                    ids[i] = CreateTextureFromBytes(img.Data);
                }
                return ids;
            }

            private uint CreateTextureFromBytes(byte[] bytes)
            {
                uint textureID;
                GL.GenTextures(1, &textureID);
                GL.BindTexture(Const.GL_TEXTURE_2D, textureID);

                using var stream = new MemoryStream(bytes);
                var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

                fixed (byte* ptr = image.Data)
                {
                    GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                                  image.Width, image.Height, 0,
                                  Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, ptr);
                }

                GL.GenerateMipmap(Const.GL_TEXTURE_2D);
                GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAX_ANISOTROPY, 4.0f);

                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_REPEAT);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_REPEAT);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR_MIPMAP_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);

                GL.BindTexture(Const.GL_TEXTURE_2D, 0);
                return textureID;
            }

            private void UploadToGpu(GltfData data)
            {
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
                            (nuint)(mesh.Vertices.Length * sizeof(SkinnedVertex)),
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
                    int stride = Marshal.SizeOf<SkinnedVertex>(); // 64 bytes: pos(12) + normal(12) + uv(8) + weights(16) + joints(16)

                    // loc 0: Position (vec3) offset 0
                    GL.EnableVertexAttribArray(0);
                    GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);

                    // loc 1: Normal (vec3) offset 12
                    GL.EnableVertexAttribArray(1);
                    GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)12);

                    // loc 2: TexCoord (vec2) offset 24
                    GL.EnableVertexAttribArray(2);
                    GL.VertexAttribPointer(2, 2, Const.GL_FLOAT, false, stride, (void*)24);

                    // loc 3: Bone Weights (vec4 float) offset 32
                    GL.EnableVertexAttribArray(3);
                    GL.VertexAttribPointer(3, 4, Const.GL_FLOAT, false, stride, (void*)32);

                    // loc 4: Bone Indices (ivec4 int) offset 48
                    GL.EnableVertexAttribArray(4);
                    GL.VertexAttribIPointer(4, 4, Const.GL_INT, stride, (void*)48);

                    GL.BindVertexArray(0);

                    // Setup material values for this primitive
                    var matGpu = new MeshMaterialGpu
                    {
                        BaseColorFactor = Vector4.One,
                        TextureID = 0,
                        HasTexture = false,
                        DoubleSided = false
                    };

                    if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < data.Materials.Length)
                    {
                        var mat = data.Materials[mesh.MaterialIndex];
                        matGpu.BaseColorFactor = mat.BaseColorFactor;
                        matGpu.DoubleSided = mat.DoubleSided;
                        if (mat.TextureIndex >= 0 && mat.TextureIndex < TextureIDs.Length)
                        {
                            matGpu.TextureID = TextureIDs[mat.TextureIndex];
                            matGpu.HasTexture = matGpu.TextureID != 0;
                        }
                    }

                    Meshes[m] = new MeshGpu
                    {
                        VAO = vao,
                        VBO = vbo,
                        EBO = ebo,
                        VertexCount = mesh.Vertices.Length,
                        IndexCount = mesh.Indices.Length,
                        Material = matGpu
                    };

                    //Console.WriteLine($"  [GltfGPU] Mesh[{m}] VAO={vao} verts={mesh.Vertices.Length} idx={mesh.Indices.Length} hasTex={matGpu.HasTexture}");
                }
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

                if (TextureIDs.Length > 0)
                {
                    fixed (uint* pTex = TextureIDs)
                    {
                        GL.DeleteTextures(TextureIDs.Length, pTex);
                    }
                }
            }
        }
    }
    
}
