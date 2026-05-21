using DarkEngine3D_gl_csharp.Engine.Libs;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using StbImageSharp;

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
        public int  VertexCount;
        public int  IndexCount;
        public MeshMaterialGpu Material;
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
            Data   = data;
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
                    VAO = vao, VBO = vbo, EBO = ebo,
                    VertexCount = mesh.Vertices.Length,
                    IndexCount  = mesh.Indices.Length,
                    Material = matGpu
                };

                Console.WriteLine($"  [GltfGPU] Mesh[{m}] VAO={vao} verts={mesh.Vertices.Length} idx={mesh.Indices.Length} stride={stride} hasTex={matGpu.HasTexture} color={matGpu.BaseColorFactor}");
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

            if (TextureIDs.Length > 0)
            {
                fixed (uint* pTex = TextureIDs)
                {
                    GL.DeleteTextures(TextureIDs.Length, pTex);
                }
            }
        }
    }

    // =========================================================================== 
    //  GltfObject — satu instance (posisi, orientasi, scale)
    //  Extended: per-instance animation player sampling node TRS
    // ===========================================================================
    public unsafe class GltfObject
    {
        public readonly GltfModelGpuData GpuData;

        public Vector3    Position;
        public Quaternion Rotation;
        public float      Scale = 1f;

        public AABB LocalAABB => GpuData.LocalAABB;
        public AABB WorldAABB => LocalAABB.ToWorld(Position, Scale);

        // animation player state
        private bool _hasAnimations = false;
        private int  _currentAnim = -1;
        private float _animTime = 0f;
        private float _animDuration = 0f;

        // per-node matrices (local + global)
        private Matrix4x4[] _nodeLocal;
        private Matrix4x4[] _nodeGlobal;

        public GltfObject(GltfModelGpuData gpuData, Vector3 position, Quaternion rotation, float scale = 1f)
        {
            GpuData  = gpuData;
            Position = position;
            Rotation = rotation;
            Scale    = scale;

            var data = GpuData.Data;
            int nCount = data.Nodes?.Length ?? 0;
            _nodeLocal = new Matrix4x4[nCount];
            _nodeGlobal = new Matrix4x4[nCount];

            for (int i = 0; i < nCount; i++)
            {
                _nodeLocal[i] = data.Nodes[i].LocalMatrix;
                _nodeGlobal[i] = data.Nodes[i].LocalMatrix;
            }

            if (data.Animations != null && data.Animations.Length > 0)
            {
                _hasAnimations = true;
                // pick "idle" candidate using safe heuristics
                string[] hacks = new[] { "idle", "stand", "rest", "wait", "idle_", "idle-" };
                int found = -1;
                for (int i = 0; i < data.Animations.Length; i++)
                {
                    var nm = (data.Animations[i].Name ?? "").ToLowerInvariant();
                    foreach (var h in hacks)
                        if (!string.IsNullOrEmpty(nm) && nm.Contains(h))
                        {
                            found = i; break;
                        }
                    if (found >= 0) break;
                }

                // fallback to first animation if none matched
                _currentAnim = found >= 0 ? found : 0;
                _animDuration = data.Animations[_currentAnim].Duration;
                _animTime = 0f;

                var chosenName = data.Animations[_currentAnim].Name ?? $"anim#{_currentAnim}";
                Console.WriteLine($"[GltfObject] Animation selected for object: index={_currentAnim} name='{chosenName}' duration={_animDuration:F3}s");
            }
        }

        public void SetFacing(float yawDegrees)
            => Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f);

        // Update per-instance animation & procedural fallback
        public void Update(float dt)
        {
            // advance animation time
            if (_hasAnimations && _currentAnim >= 0)
            {
                _animTime += dt;
                var anim = GpuData.Data.Animations[_currentAnim];
                float dur = MathF.Max(0.0001f, anim.Duration);
                // wrap
                if (_animTime > dur) _animTime %= dur;

                // reset local transforms to base
                for (int ni = 0; ni < _nodeLocal.Length; ni++)
                    _nodeLocal[ni] = GpuData.Data.Nodes[ni].LocalMatrix;

                // apply channels
                foreach (var ch in anim.Channels)
                {
                    if (ch.TargetNode < 0 || ch.TargetNode >= _nodeLocal.Length) continue;
                    var sampler = anim.Samplers[ch.SamplerIndex];
                    if (sampler == null || sampler.Input == null || sampler.Input.Length == 0) continue;

                    // find keyframe interval (linear)
                    int idx = Array.BinarySearch(sampler.Input, _animTime);
                    if (idx < 0) idx = ~idx;
                    int i0 = Math.Max(0, idx - 1);
                    int i1 = Math.Min(sampler.Input.Length - 1, idx);

                    float t0 = sampler.Input[i0];
                    float t1 = sampler.Input[i1];
                    float localT = (t1 - t0) <= 1e-6f ? 0f : ((_animTime - t0) / (t1 - t0));
                    localT = Math.Clamp(localT, 0f, 1f);

                    // sample output
                    if (sampler.OutputStride == 3 && (ch.Path == "translation" || ch.Path == "scale"))
                    {
                        int off0 = i0 * 3;
                        int off1 = i1 * 3;
                        var v0 = new Vector3(sampler.Output[off0 + 0], sampler.Output[off0 + 1], sampler.Output[off0 + 2]);
                        var v1 = new Vector3(sampler.Output[off1 + 0], sampler.Output[off1 + 1], sampler.Output[off1 + 2]);
                        var v = Vector3.Lerp(v0, v1, localT);
                        // decompose existing local to preserve other components
                        DecomposeLocalAndApply(ch.Path, ch.TargetNode, v);
                    }
                    else if (sampler.OutputStride == 4 && ch.Path == "rotation")
                    {
                        int off0 = i0 * 4;
                        int off1 = i1 * 4;
                        var q0 = new Quaternion(sampler.Output[off0 + 0], sampler.Output[off0 + 1], sampler.Output[off0 + 2], sampler.Output[off0 + 3]);
                        var q1 = new Quaternion(sampler.Output[off1 + 0], sampler.Output[off1 + 1], sampler.Output[off1 + 2], sampler.Output[off1 + 3]);
                        var q = Quaternion.Slerp(q0, q1, localT);
                        DecomposeLocalAndApply(ch.Path, ch.TargetNode, q);
                    }
                    // other output types ignored for now
                }

                // compute global node matrices by hierarchy
                for (int i = 0; i < _nodeGlobal.Length; i++) _nodeGlobal[i] = Matrix4x4.Identity;
                for (int i = 0; i < _nodeLocal.Length; i++)
                {
                    if (GpuData.Data.Nodes[i].Parent == -1)
                        ComputeGlobalRec(i, Matrix4x4.Identity);
                }
            }
            else
            {
                // no animation: keep node local==default; compute globals
                for (int i = 0; i < _nodeLocal.Length; i++) _nodeLocal[i] = GpuData.Data.Nodes[i].LocalMatrix;
                for (int i = 0; i < _nodeGlobal.Length; i++) _nodeGlobal[i] = Matrix4x4.Identity;
                for (int i = 0; i < _nodeLocal.Length; i++)
                {
                    if (GpuData.Data.Nodes[i].Parent == -1)
                        ComputeGlobalRec(i, Matrix4x4.Identity);
                }
            }
        }

        // helper: decompose current local matrix, replace translation/rotation/scale component and recompose
        private void DecomposeLocalAndApply(string path, int nodeIdx, Vector3 vec)
        {
            var m = GpuData.Data.Nodes[nodeIdx].LocalMatrix;
            // try decompose previous local (but we use current _nodeLocal as base)
            Matrix4x4.Decompose(_nodeLocal[nodeIdx], out var sc, out var rot, out var trans);
            if (path == "translation")
            {
                trans = vec;
            }
            else if (path == "scale")
            {
                sc = vec;
            }
            _nodeLocal[nodeIdx] = Matrix4x4.CreateScale(sc) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(trans);
        }

        private void DecomposeLocalAndApply(string path, int nodeIdx, Quaternion quat)
        {
            Matrix4x4.Decompose(_nodeLocal[nodeIdx], out var sc, out var rot, out var trans);
            if (path == "rotation")
            {
                rot = quat;
            }
            _nodeLocal[nodeIdx] = Matrix4x4.CreateScale(sc) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(trans);
        }

        private void ComputeGlobalRec(int idx, Matrix4x4 parent)
        {
            var local = _nodeLocal[idx];
            // Correct order: parent * local
            var global = parent * local;
            _nodeGlobal[idx] = global;
            var children = GpuData.Data.Nodes[idx].Children;
            foreach (var c in children)
            {
                if (c >= 0 && c < _nodeLocal.Length)
                    ComputeGlobalRec(c, global);
            }
        }

        // Allow external code (ObjectManager) to update the base position after snapping to terrain
        public void SetBasePosition(Vector3 pos)
        {
            Position = pos;
        }

        /// <summary>Kirim draw call ke GPU. Applies per-mesh model matrix (object * nodeGlobal).</summary>
        public void Draw(int modelLoc, int baseColorFactorLoc, int useAlbedoLoc, int albedoMapLoc)
        {
            var objMat = Matrix4x4.CreateScale(Scale)
                         * Matrix4x4.CreateFromQuaternion(Rotation)
                         * Matrix4x4.CreateTranslation(Position);

            for (int mi = 0; mi < GpuData.Meshes.Length; mi++)
            {
                var mesh = GpuData.Meshes[mi];

                // compute model matrix for this mesh: object transform * nodeGlobal (if any)
                Matrix4x4 modelMat = objMat;
                int nodeIdx = -1;
                if (GpuData.MeshToNode != null && mi < GpuData.MeshToNode.Length) nodeIdx = GpuData.MeshToNode[mi];
                if (nodeIdx >= 0 && nodeIdx < _nodeGlobal.Length)
                {
                    // FIX: apply object transform first, then node-global (correct world transform)
                    modelMat = objMat * _nodeGlobal[nodeIdx];
                }

                GL.UniformMatrix4fv(modelLoc, 1, false, (float*)Unsafe.AsPointer(ref modelMat));

                // Material properties
                if (baseColorFactorLoc != -1)
                {
                    var factor = mesh.Material.BaseColorFactor;
                    GL.Uniform4f(baseColorFactorLoc, factor.X, factor.Y, factor.Z, factor.W);
                }

                if (mesh.Material.HasTexture && mesh.Material.TextureID != 0)
                {
                    GL.ActiveTexture(Const.GL_TEXTURE0);
                    GL.BindTexture(Const.GL_TEXTURE_2D, mesh.Material.TextureID);
                    if (useAlbedoLoc != -1) GL.Uniform1i(useAlbedoLoc, 1);
                    if (albedoMapLoc != -1) GL.Uniform1i(albedoMapLoc, 0); // texture unit 0
                }
                else
                {
                    if (useAlbedoLoc != -1) GL.Uniform1i(useAlbedoLoc, 0);
                }

                if (mesh.Material.DoubleSided)
                {
                    GL.Disable(Const.GL_CULL_FACE);
                }
                else
                {
                    GL.Enable(Const.GL_CULL_FACE);
                }

                GL.BindVertexArray(mesh.VAO);
                if (mesh.IndexCount > 0)
                    GL.DrawElements(Const.GL_TRIANGLES, mesh.IndexCount, Const.GL_UNSIGNED_INT, null);
                else
                    GL.DrawArrays(Const.GL_TRIANGLES, 0, mesh.VertexCount);
            }

            GL.BindVertexArray(0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.Enable(Const.GL_CULL_FACE); // restore backface culling default
        }

        // compute a conservative world-space AABB by transforming mesh vertices with current node/global matrices
        public AABB ComputeWorldAABB()
        {
            // fallback if no meshes
            if (GpuData.Meshes == null || GpuData.Meshes.Length == 0)
                return LocalAABB.ToWorld(Position, Scale);

            var inf = float.PositiveInfinity;
            var ninf = float.NegativeInfinity;
            Vector3 mn = new Vector3(inf, inf, inf);
            Vector3 mx = new Vector3(ninf, ninf, ninf);

            var objMat = Matrix4x4.CreateScale(Scale)
                         * Matrix4x4.CreateFromQuaternion(Rotation)
                         * Matrix4x4.CreateTranslation(Position);

            for (int mi = 0; mi < GpuData.Meshes.Length; mi++)
            {
                int nodeIdx = -1;
                if (GpuData.MeshToNode != null && mi < GpuData.MeshToNode.Length) nodeIdx = GpuData.MeshToNode[mi];

                Matrix4x4 modelMat = objMat;
                if (nodeIdx >= 0 && _nodeGlobal != null && nodeIdx < _nodeGlobal.Length)
                    modelMat = objMat * _nodeGlobal[nodeIdx];

                var verts = GpuData.Data.Meshes[mi].Vertices;
                if (verts == null || verts.Length == 0) continue;

                for (int vi = 0; vi < verts.Length; vi++)
                {
                    var v = verts[vi].Position;
                    var wp = Vector3.Transform(v, modelMat);
                    mn = Vector3.Min(mn, wp);
                    mx = Vector3.Max(mx, wp);
                }
            }

            // if still infinite (no verts), fallback to local AABB
            if (float.IsPositiveInfinity(mn.X))
                return LocalAABB.ToWorld(Position, Scale);

            return new AABB(mn, mx);
        }

        // Align object's base so lowest point sits on terrain at object's X,Z
        public void AlignToTerrain(DarkEngine3D_gl_csharp.Engine.Terrains.TerrainChunk terrain)
        {
            // compute current world AABB (updates should have been applied via Update)
            var aabb = ComputeWorldAABB();

            // sample terrain at object's horizontal position (use Position.X,Z)
            float terrainY = terrain.GetHeightAt(Position.X, Position.Z);

            float currentMinY = aabb.Min.Y;
            float delta = terrainY - currentMinY;

            // shift object vertically by delta
            Position = new Vector3(Position.X, Position.Y + delta, Position.Z);
        }
    }
}
