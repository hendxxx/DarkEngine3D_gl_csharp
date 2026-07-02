using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using DarkEngine3D_gl_csharp.Engine.Visual;
using StbImageSharp;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.Helpers
{
    public static class ScaleHelpers
    {
        public static float Normalize(float scale, float baseScale, float minMul, float maxMul)
        {
            float normalized = scale / baseScale;

            if (normalized < 1.0f)
                return OGLMath.Lerp(minMul, 1.0f, normalized);

            return OGLMath.Lerp(1.0f, maxMul, normalized - 1.0f);
        }
    }
    public struct Matrix3x3(
        float m11, float m12, float m13,
        float m21, float m22, float m23,
        float m31, float m32, float m33)
        {
            public float M11 = m11, M12 = m12, M13 = m13;
            public float M21 = m21, M22 = m22, M23 = m23;
            public float M31 = m31, M32 = m32, M33 = m33;
    }
    public static class OGLMath
    {
        public static float Lerp(float a, float b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return a + (b - a) * t;
        }
        public static float ToRadians(float degrees) => degrees * (MathF.PI / 180.0f);

        public static float LerpAngle(float a, float b, float t)
        {
            float diff = ((b - a + 540f) % 360f) - 180f;
            return a + diff * t;
        } 
        public static float Repeat(float t, float length)
        {
            return t - MathF.Floor(t / length) * length;
        }

        public static float DeltaAngle(float current, float target)
        {
            float delta = Repeat((target - current + 180f), 360f) - 180f;
            return delta;
        }

        public static float SmoothDampAngle(
            float current,
            float target,
            ref float velocity,
            float smoothTime,
            float deltaTime)
        {
            float num = DeltaAngle(current, target);
            target = current + num;

            float omega = 2f / smoothTime;
            float x = omega * deltaTime;
            float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);

            float change = current - target;
            float temp = (velocity + omega * change) * deltaTime;
            velocity = (velocity - omega * temp) * exp;

            float result = target + (change + temp) * exp;
            return result;
        }

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
      
    }

    
    public class ShaderHelpers
    {

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            x = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
            return x * x * (3 - 2 * x);
        }

        public static float ComputeTargetExposure(Vector3 viewDir, Vector3 sunDir, float tMalam)
        {
            float sunFacing = MathF.Max(Vector3.Dot(viewDir, sunDir), 0.0f);

            float a = OGLMath.Lerp(1.6f, 0.55f, sunFacing);
            float b = OGLMath.Lerp(1.0f, 1.8f, tMalam);
            return a * b;
        }

        public static bool RaycastSun(float haloRadius, Camera cam, Lights lights, TerrainChunk terrain)
        {
            Vector3 origin = cam.Position;
            Vector3 sunDir = Vector3.Normalize(lights.SunDir);

            // radius sudut matahari (harus sama dengan shader)
            float angularRadius = haloRadius;

            // buat basis koordinat untuk offset
            Vector3 up = Math.Abs(sunDir.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
            Vector3 right = Vector3.Normalize(Vector3.Cross(up, sunDir));
            Vector3 sunUp = Vector3.Cross(sunDir, right);

            // 5 arah raycast
            Vector3[] dirs = new Vector3[]
            {
                sunDir, // pusat
                Vector3.Normalize(sunDir + sunUp * angularRadius),     // atas
                Vector3.Normalize(sunDir - sunUp * angularRadius),     // bawah
                Vector3.Normalize(sunDir + right * angularRadius),     // kanan
                Vector3.Normalize(sunDir - right * angularRadius),     // kiri
            };

            // cek semua titik
            foreach (var dir in dirs)
            {
                if (!RaycastSingle(origin, dir, terrain))
                    return false; // masih ada bagian matahari yang terlihat
            }

            return true; // seluruh matahari tertutup terrain
        }

        public static bool RaycastSingle(Vector3 origin, Vector3 dir, TerrainChunk terrain)
        {
            float maxDistance = 30000f;
            float step = 5f;

            float prevDiff = float.MaxValue;

            for (float d = 0; d < maxDistance; d += step)
            {
                Vector3 p = origin + dir * d;
                float terrainHeight = terrain.GetHeightAt(p.X, p.Z);
                float diff = p.Y - terrainHeight;

                if (diff < 0)
                    return true; // ray menabrak terrain

                if (diff < 0 && prevDiff > 0)
                {
                    float t = prevDiff / (prevDiff - diff);
                    float hitDist = (d - step) + t * step;
                    if (hitDist > 0)
                        return true;
                }

                prevDiff = diff;

                step = OGLMath.Lerp(5f, 50f, d / maxDistance);
            }

            return false;
        }


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
        public static uint LoadShaderFromString(string vertexString, string fragmentString)
        { 

            // Compile vertex shader
            uint vs = GL.CreateShader(Const.GL_VERTEX_SHADER);
            GL.ShaderSource(vs, vertexString);
            GL.CompileShader(vs);
            CheckShader(vs, $"Vertex Shader from string");

            // Compile fragment shader
            uint fs = GL.CreateShader(Const.GL_FRAGMENT_SHADER);
            GL.ShaderSource(fs, fragmentString);
            GL.CompileShader(fs);
            CheckShader(fs, $"Fragment Shader from string");

            // Link program
            uint program = GL.CreateProgram();
            GL.AttachShader(program, vs);
            GL.AttachShader(program, fs);
            GL.LinkProgram(program);
            CheckProgram(program, $"Shader Program (from srting)");

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

        private static uint _shadowStaticAlphaShaderProgram = 0;
        public static uint GetShadowStaticAlphaShaderProgram()
        {
            if (_shadowStaticAlphaShaderProgram == 0)
            {
                _shadowStaticAlphaShaderProgram = LoadShader(
                    @"DarkEngine3D\Shaders\shadow_static_vertex.glsl",
                    @"DarkEngine3D\Shaders\shadow_static_alpha_fragment.glsl"
                );
            }
            return _shadowStaticAlphaShaderProgram;
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

            /// <summary>
            /// Transform all 8 corners through scale → rotation → translation,
            /// then compute the new axis-aligned min/max.
            /// </summary>
            public readonly AABB ToWorld(Vector3 worldPos, float scale, Quaternion rotation)
            {
                var rotMat = Matrix4x4.CreateFromQuaternion(rotation);
                Vector3 mn = new(float.PositiveInfinity);
                Vector3 mx = new(float.NegativeInfinity);

                // 8 corners of the local AABB
                Span<Vector3> corners =
                [
                    new(Min.X, Min.Y, Min.Z),
                    new(Max.X, Min.Y, Min.Z),
                    new(Max.X, Max.Y, Min.Z),
                    new(Min.X, Max.Y, Min.Z),
                    new(Min.X, Min.Y, Max.Z),
                    new(Max.X, Min.Y, Max.Z),
                    new(Max.X, Max.Y, Max.Z),
                    new(Min.X, Max.Y, Max.Z),
                ];

                for (int i = 0; i < 8; i++)
                {
                    var wp = Vector3.Transform(corners[i] * scale, rotMat) + worldPos;
                    mn = Vector3.Min(mn, wp);
                    mx = Vector3.Max(mx, wp);
                }

                return new AABB(mn, mx);
            }

            /// <summary>
            /// Transform all 8 corners by a full 4×4 matrix (row-vector),
            /// then compute the new axis-aligned min/max.
            /// </summary>
            public readonly AABB Transform(Matrix4x4 mat)
            {
                Vector3 mn = new(float.PositiveInfinity);
                Vector3 mx = new(float.NegativeInfinity);

                Span<Vector3> corners =
                [
                    new(Min.X, Min.Y, Min.Z),
                    new(Max.X, Min.Y, Min.Z),
                    new(Max.X, Max.Y, Min.Z),
                    new(Min.X, Max.Y, Min.Z),
                    new(Min.X, Min.Y, Max.Z),
                    new(Max.X, Min.Y, Max.Z),
                    new(Max.X, Max.Y, Max.Z),
                    new(Min.X, Min.Y, Max.Z),
                ];

                for (int i = 0; i < 8; i++)
                {
                    var wp = Vector3.Transform(corners[i], mat);
                    mn = Vector3.Min(mn, wp);
                    mx = Vector3.Max(mx, wp);
                }

                return new AABB(mn, mx);
            }

            public readonly bool Contains(Vector3 point) =>
                point.X >= Min.X && point.X <= Max.X &&
                point.Y >= Min.Y && point.Y <= Max.Y &&
                point.Z >= Min.Z && point.Z <= Max.Z;

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

        /// Returns Identity if nodeIdx is invalid.
        /// </summary>
        public static Matrix4x4 GetNodeWorldMatrix(GltfNode[] nodes, int nodeIdx)
        {
            if (nodes == null || nodeIdx < 0 || nodeIdx >= nodes.Length)
                return Matrix4x4.Identity;

            // Collect chain from node up to root
            int count = 0;
            int idx = nodeIdx;
            while (idx >= 0 && idx < nodes.Length)
            {
                count++;
                idx = nodes[idx].Parent;
                if (count > 128) break; // safety
            }

            var chain = new int[count];
            idx = nodeIdx;
            for (int i = count - 1; i >= 0; i--)
            {
                chain[i] = idx;
                idx = nodes[idx].Parent;
            }

            // Multiply from root down to node (row-vector: root first, then children)
            Matrix4x4 result = Matrix4x4.Identity;
            for (int i = 0; i < count; i++)
                result = result * nodes[chain[i]].LocalMatrix;

            return result;
        }

        // ===========================================================================
        //  MeshMaterialGpu — material parameters on the GPU
        // ===========================================================================
        public struct MeshMaterialGpu
        {
            // Base color
            public Vector4 BaseColorFactor;
            public uint BaseColorTextureID;
            public bool HasBaseColorTexture;
            
            // PBR values
            public float MetallicFactor;
            public float RoughnessFactor;
            public uint MetallicRoughnessTextureID;
            public bool HasMetallicRoughnessTexture;
            
            // Normal mapping
            public uint NormalTextureID;
            public bool HasNormalTexture;
            public float NormalScale;
            
            // AO
            public uint OcclusionTextureID;
            public bool HasOcclusionTexture;
            public float OcclusionStrength;
            
            // Emissive
            public Vector3 EmissiveFactor;
            public uint EmissiveTextureID;
            public bool HasEmissiveTexture;
            
            // Other
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


            public GltfModelGpuData(GltfData data, bool useNodeHierarchy = false)
            {
                Data = data;

            // Generate simplified LOD meshes via vertex clustering
            GenerateSimplifiedLODs(data);

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

                    // Propagate node indices to auto-generated LOD variants
                    for (int mi = 0; mi < data.Meshes.Length; mi++)
                    {
                        if (MeshToNode[mi] >= 0) continue;
                        string? name = data.Meshes[mi].Name;
                        if (string.IsNullOrEmpty(name)) continue;
                        int lodIdx = name.LastIndexOf("_LOD", StringComparison.OrdinalIgnoreCase);
                        if (lodIdx < 0) continue;
                        string baseLookup = name[..lodIdx];
                        for (int bi = 0; bi < data.Meshes.Length; bi++)
                        {
                            if (bi == mi) continue;
                            if (string.Equals(data.Meshes[bi].Name, baseLookup, StringComparison.OrdinalIgnoreCase))
                            {
                                MeshToNode[mi] = MeshToNode[bi];
                                break;
                            }
                        }
                    }

                }

                // Compute local AABB from ALL meshes (not just the first one)
                // Terapkan node transform agar AABB sesuai dengan visual rendering
                if (data.Meshes.Length > 0)
                {
                    Vector3 mn = new(float.PositiveInfinity);
                    Vector3 mx = new(float.NegativeInfinity);
                    for (int mi = 0; mi < data.Meshes.Length; mi++)
                    {
                        // Cari node transform untuk mesh ini
                        int nodeIdx = (MeshToNode != null && mi < MeshToNode.Length) ? MeshToNode[mi] : -1;
                        Matrix4x4 nodeMat = Matrix4x4.Identity;
                        if (nodeIdx >= 0 && data.Nodes != null && nodeIdx < data.Nodes.Length)
                            nodeMat = useNodeHierarchy
                                    ? GetNodeWorldMatrix(data.Nodes, nodeIdx)
                                    : data.Nodes[nodeIdx].LocalMatrix;

                        var verts = data.Meshes[mi].Vertices;
                        if (verts == null || verts.Length == 0) continue;
                        for (int vi = 0; vi < verts.Length; vi++)
                        {
                            Vector3 modelLocal = Vector3.Transform(verts[vi].Position, nodeMat);
                            mn = Vector3.Min(mn, modelLocal);
                            mx = Vector3.Max(mx, modelLocal);
                        }
                    }
                    LocalAABB = new AABB(mn, mx);
                }
                else
                    LocalAABB = new AABB(Vector3.Zero, Vector3.One);
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
                        // Base color
                        BaseColorFactor = Vector4.One,
                        BaseColorTextureID = 0,
                        HasBaseColorTexture = false,
                        
                        // PBR defaults
                        MetallicFactor = 1.0f,
                        RoughnessFactor = 1.0f,
                        MetallicRoughnessTextureID = 0,
                        HasMetallicRoughnessTexture = false,
                        
                        // Normal mapping defaults
                        NormalTextureID = 0,
                        HasNormalTexture = false,
                        NormalScale = 1.0f,
                        
                        // Occlusion defaults
                        OcclusionTextureID = 0,
                        HasOcclusionTexture = false,
                        OcclusionStrength = 1.0f,
                        
                        // Emissive defaults
                        EmissiveFactor = Vector3.Zero,
                        EmissiveTextureID = 0,
                        HasEmissiveTexture = false,
                        
                        // Other
                        DoubleSided = false
                    };

                    if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < data.Materials.Length)
                    {
                        var mat = data.Materials[mesh.MaterialIndex];
                        
                        // Base color
                        matGpu.BaseColorFactor = mat.BaseColorFactor;
                        if (mat.BaseColorTextureIndex >= 0 && mat.BaseColorTextureIndex < TextureIDs.Length)
                        {
                            matGpu.BaseColorTextureID = TextureIDs[mat.BaseColorTextureIndex];
                            matGpu.HasBaseColorTexture = matGpu.BaseColorTextureID != 0;
                        }
                        
                        // PBR metallic & roughness
                        matGpu.MetallicFactor = mat.MetallicFactor;
                        matGpu.RoughnessFactor = mat.RoughnessFactor;
                        if (mat.MetallicRoughnessTextureIndex >= 0 && mat.MetallicRoughnessTextureIndex < TextureIDs.Length)
                        {
                            matGpu.MetallicRoughnessTextureID = TextureIDs[mat.MetallicRoughnessTextureIndex];
                            matGpu.HasMetallicRoughnessTexture = matGpu.MetallicRoughnessTextureID != 0;
                        }
                        
                        // Normal mapping
                        matGpu.NormalScale = mat.NormalScale;
                        if (mat.NormalTextureIndex >= 0 && mat.NormalTextureIndex < TextureIDs.Length)
                        {
                            matGpu.NormalTextureID = TextureIDs[mat.NormalTextureIndex];
                            matGpu.HasNormalTexture = matGpu.NormalTextureID != 0;
                        }
                        
                        // Occlusion
                        matGpu.OcclusionStrength = mat.OcclusionStrength;
                        if (mat.OcclusionTextureIndex >= 0 && mat.OcclusionTextureIndex < TextureIDs.Length)
                        {
                            matGpu.OcclusionTextureID = TextureIDs[mat.OcclusionTextureIndex];
                            matGpu.HasOcclusionTexture = matGpu.OcclusionTextureID != 0;
                        }
                        
                        // Emissive
                        matGpu.EmissiveFactor = mat.EmissiveFactor;
                        if (mat.EmissiveTextureIndex >= 0 && mat.EmissiveTextureIndex < TextureIDs.Length)
                        {
                            matGpu.EmissiveTextureID = TextureIDs[mat.EmissiveTextureIndex];
                            matGpu.HasEmissiveTexture = matGpu.EmissiveTextureID != 0;
                        }
                        
                        // Other
                        matGpu.DoubleSided = mat.DoubleSided;
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

            // ────────────────────────────────────────────────────────────────
            //  Vertex-Clustering LOD Generation (auto LOD2 & LOD3)
            // ────────────────────────────────────────────────────────────────

            private static void SimplifySkinned(SkinnedVertex[] verts, uint[] indices, float cellSize,
                out SkinnedVertex[] outVerts, out uint[] outIndices)
            {
                if (verts.Length == 0 || indices.Length == 0)
                {
                    outVerts = verts;
                    outIndices = indices;
                    return;
                }

                // 1. Cluster: grid key -> list of vertex indices
                var clusters = new Dictionary<(int, int, int), List<int>>();
                for (int i = 0; i < verts.Length; i++)
                {
                    ref var v = ref verts[i];
                    var key = (
                        (int)MathF.Floor(v.Position.X / cellSize),
                        (int)MathF.Floor(v.Position.Y / cellSize),
                        (int)MathF.Floor(v.Position.Z / cellSize)
                    );
                    if (!clusters.TryGetValue(key, out var list))
                    {
                        list = new List<int>();
                        clusters[key] = list;
                    }
                    list.Add(i);
                }

                // 2. Create averaged vertex per cluster
                var newVerts = new List<SkinnedVertex>();
                var clusterToNewIdx = new Dictionary<(int, int, int), int>();

                foreach (var kv in clusters)
                {
                    var list = kv.Value;
                    Vector3 pos = Vector3.Zero;
                    Vector3 nrm = Vector3.Zero;
                    Vector2 uv = Vector2.Zero;
                    Vector4 weights = Vector4.Zero;

                    // Bone indices: pick from vertex with highest total weight
                    int bestVi = list[0];
                    float bestWeightSum = verts[bestVi].BoneWeights.X
                                        + verts[bestVi].BoneWeights.Y
                                        + verts[bestVi].BoneWeights.Z
                                        + verts[bestVi].BoneWeights.W;

                    foreach (int vi in list)
                    {
                        pos += verts[vi].Position;
                        nrm += verts[vi].Normal;
                        uv  += verts[vi].TexCoord;
                        weights += verts[vi].BoneWeights;

                        float ws = verts[vi].BoneWeights.X + verts[vi].BoneWeights.Y
                                 + verts[vi].BoneWeights.Z + verts[vi].BoneWeights.W;
                        if (ws > bestWeightSum)
                        {
                            bestWeightSum = ws;
                            bestVi = vi;
                        }
                    }

                    float inv = 1.0f / list.Count;
                    pos *= inv;
                    nrm = Vector3.Normalize(nrm * inv);
                    uv  *= inv;
                    weights *= inv;

                    // Re-normalize bone weights to sum ~1
                    float tw = weights.X + weights.Y + weights.Z + weights.W;
                    if (tw > 1e-6f) weights /= tw;

                    int newIdx = newVerts.Count;
                    newVerts.Add(new SkinnedVertex(pos, nrm, uv, weights, verts[bestVi].BoneIds));
                    clusterToNewIdx[kv.Key] = newIdx;
                }

                // 3. Rebuild indices, skipping degenerate triangles
                var newIndices = new List<uint>();
                for (int i = 0; i < indices.Length; i += 3)
                {
                    uint i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];

                    var k0 = (
                        (int)MathF.Floor(verts[i0].Position.X / cellSize),
                        (int)MathF.Floor(verts[i0].Position.Y / cellSize),
                        (int)MathF.Floor(verts[i0].Position.Z / cellSize));
                    var k1 = (
                        (int)MathF.Floor(verts[i1].Position.X / cellSize),
                        (int)MathF.Floor(verts[i1].Position.Y / cellSize),
                        (int)MathF.Floor(verts[i1].Position.Z / cellSize));
                    var k2 = (
                        (int)MathF.Floor(verts[i2].Position.X / cellSize),
                        (int)MathF.Floor(verts[i2].Position.Y / cellSize),
                        (int)MathF.Floor(verts[i2].Position.Z / cellSize));

                    int a = clusterToNewIdx[k0];
                    int b = clusterToNewIdx[k1];
                    int c = clusterToNewIdx[k2];

                    if (a == b || b == c || c == a) continue;

                    newIndices.Add((uint)a);
                    newIndices.Add((uint)b);
                    newIndices.Add((uint)c);
                }

                outVerts = [.. newVerts];
                outIndices = [.. newIndices];
            }

            /// <summary>
            /// For each mesh that doesn't already have _LOD2 / _LOD3 variants,
            /// generate simplified versions via vertex clustering and append to data.Meshes.
            /// </summary>
            private static void GenerateSimplifiedLODs(GltfData data)
            {
                if (data.Meshes == null || data.Meshes.Length == 0) return;

                // Collect existing LOD-named mesh names for overlap detection
                var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < data.Meshes.Length; i++)
                    if (!string.IsNullOrEmpty(data.Meshes[i].Name))
                        existingNames.Add(data.Meshes[i].Name);

                var newMeshes = new List<GltfMeshData>();

                for (int mi = 0; mi < data.Meshes.Length; mi++)
                {
                    var mesh = data.Meshes[mi];
                    if (mesh.Vertices.Length < 3 || mesh.Indices.Length < 3) continue;

                    string baseName = string.IsNullOrEmpty(mesh.Name) ? $"mesh_{mi}" : mesh.Name;

                    // Skip meshes that already have LOD1/2/3 variants (built-in GLB LOD)
                    // _LOD0 meshes are NOT skipped — they get auto-generated LOD2/3 via vertex clustering.
                    if (System.Text.RegularExpressions.Regex.IsMatch(baseName, @"_LOD[1-3]",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        continue;

                    // Skip skinned meshes (characters) — GltfObject.Draw renders ALL meshes,
                    // not LOD-selected groups. Auto-generated LODs would draw on top of original.
                    bool isSkinned = false;
                    for (int svi = 0; svi < mesh.Vertices.Length && !isSkinned; svi++)
                    {
                        ref var sv = ref mesh.Vertices[svi];
                        // Non-skinned mesh has weights = (1,0,0,0) from GltfLoader default
                        if (sv.BoneWeights.X < 0.99f || sv.BoneWeights.Y > 0.01f ||
                            sv.BoneWeights.Z > 0.01f || sv.BoneWeights.W > 0.01f)
                            isSkinned = true;
                    }
                    if (isSkinned) continue;

                    // Compute AABB diagonal for cell size scaling
                    Vector3 mn = mesh.Vertices[0].Position;
                    Vector3 mx = mesh.Vertices[0].Position;
                    for (int vi = 1; vi < mesh.Vertices.Length; vi++)
                    {
                        mn = Vector3.Min(mn, mesh.Vertices[vi].Position);
                        mx = Vector3.Max(mx, mesh.Vertices[vi].Position);
                    }
                    float diag = (mx - mn).Length();
                    if (diag < 1e-6f) continue;

                    // Generate LOD2 and LOD3
                    float[] cellSizes = [diag * 0.03f, diag * 0.08f];
                    string[] suffixes = ["_LOD2", "_LOD3"];

                    for (int li = 0; li < 2; li++)
                    {
                        string lodName = baseName + suffixes[li];
                        if (existingNames.Contains(lodName)) continue;

                        SimplifySkinned(mesh.Vertices, mesh.Indices, cellSizes[li],
                            out var simplifiedVerts, out var simplifiedIndices);

                        // Skip if simplification produced no valid triangles
                        if (simplifiedIndices.Length < 3) continue;

                        newMeshes.Add(new GltfMeshData
                        {
                            Name = lodName,
                            Vertices = simplifiedVerts,
                            Indices = simplifiedIndices,
                            MaterialIndex = mesh.MaterialIndex
                        });

                        existingNames.Add(lodName);
                    }
                }

                if (newMeshes.Count > 0)
                {
                    int oldLen = data.Meshes.Length;
                    Array.Resize(ref data.Meshes, oldLen + newMeshes.Count);
                    for (int i = 0; i < newMeshes.Count; i++)
                        data.Meshes[oldLen + i] = newMeshes[i];
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
