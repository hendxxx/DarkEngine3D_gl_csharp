using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Terrains;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    // ===========================================================================
    //  AnimationState — satu instance per GltfObject, support blending
    // ===========================================================================
    public class AnimationState
    {
        public int   CurrentAnim  = 0;
        public int   TargetAnim   = -1;   // -1 = tidak blending
        public float CurrentTime  = 0f;
        public float TargetTime   = 0f;
        public float BlendFactor  = 0f;   // 0 = full current, 1 = full target
        public float BlendSpeed   = 3f;   // satuan per detik (seberapa cepat blend)
        public bool  Loop         = true;

        /// <summary>Mulai blend ke animasi lain.</summary>
        public void BlendTo(int animIndex, float speed = 3f)
        {
            if (animIndex == CurrentAnim && TargetAnim < 0) return;
            TargetAnim   = animIndex;
            TargetTime   = 0f;
            BlendFactor  = 0f;
            BlendSpeed   = speed;
        }

        public void Update(float dt, GltfAnimation[] anims)
        {
            if (anims.Length == 0) return;

            CurrentTime += dt;
            float dur = anims[CurrentAnim].Duration;
            if (dur > 0f)
            {
                if (Loop) CurrentTime = CurrentTime % dur;
                else       CurrentTime = Math.Min(CurrentTime, dur);
            }

            if (TargetAnim >= 0)
            {
                TargetTime  += dt;
                BlendFactor += dt * BlendSpeed;

                float tdur = anims[TargetAnim].Duration;
                if (tdur > 0f && Loop) TargetTime = TargetTime % tdur;

                if (BlendFactor >= 1f)
                {
                    // Selesai blend → target jadi current
                    CurrentAnim  = TargetAnim;
                    CurrentTime  = TargetTime;
                    BlendFactor  = 0f;
                    TargetAnim   = -1;
                }
            }
        }
    }

    // ===========================================================================
    //  AABB Collision Box
    // ===========================================================================
    public struct AABB
    {
        public Vector3 Min, Max;

        public AABB(Vector3 min, Vector3 max) { Min = min; Max = max; }

        /// <summary>Cek apakah dua AABB bersinggungan.</summary>
        public readonly bool Intersects(AABB other) =>
            Min.X <= other.Max.X && Max.X >= other.Min.X &&
            Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
            Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

        /// <summary>Transform AABB ke world space (berdasarkan posisi & scale).</summary>
        public readonly AABB ToWorld(Vector3 worldPos, float scale = 1f)
        {
            var wMin = Min * scale + worldPos;
            var wMax = Max * scale + worldPos;
            return new AABB(wMin, wMax);
        }

        /// <summary>Hitung dari vertices (local space).</summary>
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
    //  GltfObject — satu instance dari model glTF (posisi, orientasi, animasi)
    // ===========================================================================
    public unsafe class GltfObject
    {
        // ---- Shared GPU resources (dibagi oleh semua instance yang pakai model sama) ----
        public readonly GltfModelGpuData GpuData;

        // ---- Transform per instance ----
        public Vector3    Position;
        public Quaternion Rotation;
        public float      Scale = 1f;

        // ---- Collision ----
        public AABB LocalAABB;   // local space
        public AABB WorldAABB => LocalAABB.ToWorld(Position, Scale);

        // ---- Animation ----
        public AnimationState AnimState = new();

        // ---- Cached joint matrices (dihitung tiap frame, dikirim ke shader) ----
        private Matrix4x4[] _jointMats;

        public GltfObject(GltfModelGpuData gpuData, Vector3 position, Quaternion rotation, float scale = 1f)
        {
            GpuData     = gpuData;
            Position    = position;
            Rotation    = rotation;
            Scale       = scale;
            LocalAABB   = gpuData.LocalAABB;

            int jointCount = gpuData.Data.Skin?.Joints.Length ?? 1;
            _jointMats     = new Matrix4x4[jointCount];
            for (int i = 0; i < _jointMats.Length; i++)
                _jointMats[i] = Matrix4x4.Identity;
        }

        // -----------------------------------------------------------------------
        /// <summary>Arahkan objek ke kanan (0°), kiri (180°), dst.</summary>
        public void SetFacing(float yawDegrees)
        {
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f);
        }

        // -----------------------------------------------------------------------
        public void Update(float dt)
        {
            if (GpuData.Data.Animations.Length == 0) return;

            AnimState.Update(dt, GpuData.Data.Animations);
            ComputeJointMatrices();
        }

        // -----------------------------------------------------------------------
        /// <summary>Hitung joint matrices dengan animation blending.</summary>
        private void ComputeJointMatrices()
        {
            var skin = GpuData.Data.Skin;
            if (skin == null) return;

            var data   = GpuData.Data;
            var animA  = data.Animations[AnimState.CurrentAnim];
            bool blend = AnimState.TargetAnim >= 0;
            var animB  = blend ? data.Animations[AnimState.TargetAnim] : animA;
            float bFac = blend ? Math.Clamp(AnimState.BlendFactor, 0f, 1f) : 0f;

            // ── 1. Local TRS per node — start dari rest pose ──
            int nc = data.NodeRestPose.Length;
            var local = new Matrix4x4[nc];
            for (int i = 0; i < nc; i++) local[i] = data.NodeRestPose[i];

            ApplyAnimation(animA, AnimState.CurrentTime, data, local, 1f - bFac);
            if (blend) ApplyAnimation(animB, AnimState.TargetTime, data, local, bFac);

            // ── 2. Global transforms via recursive DFS ──
            var global = (Matrix4x4[])local.Clone();

            // Cari semua nodes yang merupakan children (bukan root)
            var isChild = new HashSet<int>();
            foreach (var kv in data.NodeChildren)
                foreach (int ci in kv.Value) isChild.Add(ci);

            // Root = node yang tidak ada di isChild set
            void Traverse(int ni, Matrix4x4 parentGlobal)
            {
                global[ni] = Matrix4x4.Multiply(parentGlobal, local[ni]);
                if (data.NodeChildren.TryGetValue(ni, out var children))
                    foreach (int ci in children)
                        if (ci < nc) Traverse(ci, global[ni]);
            }

            for (int ni = 0; ni < nc; ni++)
                if (!isChild.Contains(ni))
                    Traverse(ni, Matrix4x4.Identity);


            // ── 3. glTF spec: jointMatrix[j] = global[joints[j]] * invBindMatrix[j] ──
            for (int j = 0; j < skin.Joints.Length && j < _jointMats.Length; j++)
            {
                int ni = skin.Joints[j];
                var g  = ni < nc ? global[ni] : Matrix4x4.Identity;
                _jointMats[j] = j < skin.InvBindMats.Length
                    ? Matrix4x4.Multiply(g, skin.InvBindMats[j])
                    : g;
            }
        }


        private static void ApplyAnimation(GltfAnimation anim, float t, GltfData data, Matrix4x4[] localMat, float weight)
        {
            if (weight <= 0f) return;
            int nodeCount = localMat.Length;

            foreach (var ch in anim.Channels)
            {
                // ch.JointIndex = joint slot index → map ke node index via skin.Joints
                int nodeIdx = data.Skin != null && ch.JointIndex < data.Skin.Joints.Length
                    ? data.Skin.Joints[ch.JointIndex]
                    : ch.JointIndex;
                if (nodeIdx >= nodeCount) continue;

                int k0 = FindKeyframe(ch.Times, t);
                int k1 = Math.Min(k0 + 1, ch.Times.Length - 1);
                float alpha = 0f;
                if (k0 != k1)
                {
                    float dtt = ch.Times[k1] - ch.Times[k0];
                    if (dtt > 0f) alpha = Math.Clamp((t - ch.Times[k0]) / dtt, 0f, 1f);
                }

                Matrix4x4.Decompose(localMat[nodeIdx], out var cS, out var cR, out var cT);

                switch (ch.Path)
                {
                    case AnimPath.Translation when ch.ValueV3 != null:
                        var tr = Vector3.Lerp(ch.ValueV3[k0], ch.ValueV3[k1], alpha);
                        var fT = weight < 1f ? Vector3.Lerp(cT, tr, weight) : tr;
                        localMat[nodeIdx] = BuildTRS(fT, cR, cS);
                        break;
                    case AnimPath.Rotation when ch.ValueQ != null:
                        var q  = Quaternion.Slerp(ch.ValueQ[k0], ch.ValueQ[k1], alpha);
                        var fQ = weight < 1f ? Quaternion.Slerp(cR, q, weight) : q;
                        localMat[nodeIdx] = BuildTRS(cT, fQ, cS);
                        break;
                    case AnimPath.Scale when ch.ValueV3 != null:
                        var sc = Vector3.Lerp(ch.ValueV3[k0], ch.ValueV3[k1], alpha);
                        var fS = weight < 1f ? Vector3.Lerp(cS, sc, weight) : sc;
                        localMat[nodeIdx] = BuildTRS(cT, cR, fS);
                        break;
                }
            }
        }


        private static int FindKeyframe(float[] times, float t)
        {
            if (t <= times[0]) return 0;
            for (int i = 1; i < times.Length; i++)
                if (t < times[i]) return i - 1;
            return times.Length - 1;
        }

        private static Matrix4x4 BuildTRS(Vector3 t, Quaternion r, Vector3 s) =>
            Matrix4x4.CreateTranslation(t) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateScale(s);


        // -----------------------------------------------------------------------
        /// <summary>Kirim draw call untuk object ini (bind ke shader yang sudah aktif).</summary>
        public void Draw(int modelLoc, int jointMatsLoc, int numJointsLoc)
        {
            // Model matrix = TRS world transform
            var modelMat = Matrix4x4.CreateScale(Scale)
                         * Matrix4x4.CreateFromQuaternion(Rotation)
                         * Matrix4x4.CreateTranslation(Position);

            // Match terrain shader convention (transpose=false)
            GL.UniformMatrix4fv(modelLoc, 1, false, (float*)Unsafe.AsPointer(ref modelMat));


            // Kirim joint matrices (skeleton)
            int jCount = _jointMats.Length;
            GL.Uniform1i(numJointsLoc, jCount);
            if (jCount > 0)
            {
                GL.UniformMatrix4fv(jointMatsLoc, jCount, false,
                    (float*)Unsafe.AsPointer(ref _jointMats[0]));
            }


            // Draw semua mesh primitif
            foreach (var mesh in GpuData.Meshes)
            {
                GL.BindVertexArray(mesh.VAO);
                if (mesh.IndexCount > 0)
                    GL.DrawElements(Const.GL_TRIANGLES, mesh.IndexCount, Const.GL_UNSIGNED_INT, null);
                else
                    GL.DrawArrays(Const.GL_TRIANGLES, 0, mesh.VertexCount);
                GL.BindVertexArray(0);
            }
        }
    }

    // ===========================================================================
    //  GltfModelGpuData — GPU buffers, dibagi oleh semua instance (flyweight)
    // ===========================================================================
    public unsafe class GltfModelGpuData
    {
        public struct MeshGpu { public uint VAO, VBO, EBO; public int VertexCount, IndexCount; }

        public readonly GltfData    Data;
        public readonly MeshGpu[]   Meshes;
        public readonly AABB        LocalAABB;

        public GltfModelGpuData(GltfData data)
        {
            Data   = data;
            Meshes = new MeshGpu[data.Meshes.Length];

            // Hitung AABB dari semua mesh
            var allVerts = data.Meshes.SelectMany(m => m.Vertices).ToArray();
            LocalAABB = AABB.FromVertices(allVerts);

            int stride = sizeof(SkinnedVertex);

            for (int m = 0; m < data.Meshes.Length; m++)
            {
                var mesh = data.Meshes[m];
                uint vao = 0, vbo = 0, ebo = 0;

                GL.GenVertexArrays(1, &vao);
                GL.GenBuffers(1, &vbo);
                GL.GenBuffers(1, &ebo);

                GL.BindVertexArray(vao);

                // Upload vertex data
                GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
                fixed (void* pv = mesh.Vertices)
                    GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(mesh.Vertices.Length * stride), pv, Const.GL_STATIC_DRAW);

                // Upload index data
                if (mesh.Indices.Length > 0)
                {
                    GL.BindBuffer(0x8893u /* GL_ELEMENT_ARRAY_BUFFER */, ebo);
                    fixed (void* pi = mesh.Indices)
                        GL.BufferData(0x8893u, (nuint)(mesh.Indices.Length * sizeof(uint)), pi, Const.GL_STATIC_DRAW);
                }

                // Vertex attribute pointers — harus cocok dengan SkinnedVertex layout
                // loc 0: Position (vec3)
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);

                // loc 1: Normal (vec3)
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(1, 3, Const.GL_FLOAT, false, stride, (void*)12);

                // loc 2: UV (vec2)
                GL.EnableVertexAttribArray(2);
                GL.VertexAttribPointer(2, 2, Const.GL_FLOAT, false, stride, (void*)24);

                // loc 3: BoneWeights (vec4)
                GL.EnableVertexAttribArray(3);
                GL.VertexAttribPointer(3, 4, Const.GL_FLOAT, false, stride, (void*)32);

                // loc 4: BoneIds (vec4 float — cast to int in shader)
                GL.EnableVertexAttribArray(4);
                GL.VertexAttribPointer(4, 4, Const.GL_FLOAT, false, stride, (void*)48);

                GL.BindVertexArray(0);

                Meshes[m] = new MeshGpu
                {
                    VAO = vao, VBO = vbo, EBO = ebo,
                    VertexCount = mesh.Vertices.Length,
                    IndexCount  = mesh.Indices.Length
                };

                Console.WriteLine($"  [GltfGPU] Mesh[{m}] VAO={vao} VBO={vbo} verts={mesh.Vertices.Length} idx={mesh.Indices.Length} stride={stride}");
            }

            Console.WriteLine($"[GltfGPU] AABB local: min={LocalAABB.Min} max={LocalAABB.Max}");
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
}
