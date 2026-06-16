using DarkEngine3D_gl_csharp.Engine.Libs;
using System.Numerics;
using System.Runtime.CompilerServices;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{ 
    // ===========================================================================
    //  GltfObject — per-instance skeletal animation player.
    //
    //  Animation model:
    //    * A model can carry its own (internal) clips, and external clips can be
    //      merged in from an animation-only glTF via ApplyExternalAnimation().
    //    * On load the object auto-selects an "idle" clip (or the first clip) and
    //      loops it seamlessly.
    //    * Play(name) crossfades from the current clip to the requested one over a
    //      blend duration, producing a smooth transition (e.g. idle <-> walk).
    //
    //  All matrices use the System.Numerics row-vector convention (translation in
    //  M41..M43); node globals are computed as local * parentGlobal and skinning
    //  joint matrices as inverseBind * jointGlobal.
    // ===========================================================================
    public unsafe class GltfObject
    {
        public readonly GltfModelGpuData GpuData;
        public bool IsVisible = true;
        public bool IsPlayer = false;
        public bool IsStatic = false;
        public int AnimLOD = 0; // 0=full, 1=mid, 2=freeze, 3=skip
        private int _lodFrameCounter = 0;

        public Vector3    Position;
        public Quaternion Rotation;
        public float      Scale = 1f;

        public float      PlaybackSpeed = 1f;

        public AABB LocalAABB => GpuData.LocalAABB;
        public AABB WorldAABB => LocalAABB.ToWorld(Position, Scale);

        // Mesh visibility control (for 1st person camera mode)
        private readonly HashSet<int> _hiddenMeshIndices = [];

        // ---- node hierarchy working buffers ----
        private Matrix4x4[] _nodeLocal     = [];
        private Matrix4x4[] _nodeGlobal    = [];
        private Matrix4x4[] _jointMatrices = [];

        // ---- animation clips (channels remapped to THIS model's node indices) ----
        private readonly List<GltfAnimation> _clips = [];
        private readonly Dictionary<string, int> _clipByName = new(StringComparer.OrdinalIgnoreCase);

        // ---- playback state machine (with crossfade blending) ----
        private int   _curClip   = -1;
        private float _curTime   = 0f;
        private int   _prevClip  = -1;
        private float _prevTime  = 0f;
        private float _blend     = 1f;
        private float _blendRate = 0f;

        // one-shot playback
        private bool  _curLoop      = true;
        private int   _returnClip   = -1;
        private float _returnBlend  = 0.2f;

        // ---- pose scratch buffers ----
        private NodeTransform[] _basePose = [];
        private NodeTransform[] _poseCur  = [];
        private NodeTransform[] _posePrev = [];
        private NodeTransform[] _poseOut  = [];

        public string CurrentClipName =>
            (_curClip >= 0 && _curClip < _clips.Count) ? (_clips[_curClip].Name ?? $"#{_curClip}") : "(none)";

        public bool HasAnimations => _clips.Count > 0;

        // True while a one-shot clip (e.g. a punch) is mid-play and hasn't recovered.
        public bool IsPlayingOneShot => !_curLoop;

        public GltfObject(GltfModelGpuData gpuData, Vector3 position, Quaternion rotation, float scale = 1f)
        {
            GpuData  = gpuData;
            Position = position;
            Rotation = rotation;
            Scale    = scale;

            var nodes = GpuData.Data.Nodes ?? [];
            AllocateBuffers(nodes);

            // Register the model's own (internal) animations. Their channels already
            // reference this model's node indices, so no remapping is required.
            if (GpuData.Data.Animations != null)
                foreach (var anim in GpuData.Data.Animations)
                    RegisterClip(anim);

            // Default to an idle clip (or the first clip) so the model is animated
            // and looping the moment it is loaded.
            int def = FindClip("idle", "stand", "rest", "wait");
            if (def < 0 && _clips.Count > 0) def = 0;
            if (def >= 0)
            {
                _curClip = def; _curTime = 0f; _blend = 1f; _prevClip = -1;
                //Console.WriteLine($"[GltfObject] Default clip '{_clips[def].Name}' dur={_clips[def].Duration:F2}s ({_clips.Count} clips total)");
            }
        }

        // -----------------------------------------------------------------------
        //  Public animation API
        // -----------------------------------------------------------------------

        public void SetFacing(float yawDegrees)
            => Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDegrees * MathF.PI / 180f);

        public void HideMeshByNodeName(string nodeName)
        {
            // Find meshes associated with this node and hide them
            if (GpuData.Data.Nodes == null) return;
            for (int i = 0; i < GpuData.Data.Nodes.Length; i++)
            {
                var node = GpuData.Data.Nodes[i];
                if (node.Name != null && node.Name.Contains(nodeName, StringComparison.OrdinalIgnoreCase))
                {
                    // Find meshes for this node
                    if (GpuData.MeshToNode != null)
                    {
                        for (int mi = 0; mi < GpuData.MeshToNode.Length; mi++)
                        {
                            if (GpuData.MeshToNode[mi] == i)
                                _hiddenMeshIndices.Add(mi);
                        }
                    }
                }
            }
        }

        public void ShowMeshByNodeName(string nodeName)
        {
            // Show meshes associated with this node
            if (GpuData.Data.Nodes == null) return;
            for (int i = 0; i < GpuData.Data.Nodes.Length; i++)
            {
                var node = GpuData.Data.Nodes[i];
                if (node.Name != null && node.Name.Contains(nodeName, StringComparison.OrdinalIgnoreCase))
                {
                    // Find meshes for this node
                    if (GpuData.MeshToNode != null)
                    {
                        for (int mi = 0; mi < GpuData.MeshToNode.Length; mi++)
                        {
                            if (GpuData.MeshToNode[mi] == i)
                                _hiddenMeshIndices.Remove(mi);
                        }
                    }
                }
            }
        }

        public void HideAllMeshes()
        {
            _hiddenMeshIndices.Clear();
            for (int i = 0; i < GpuData.Meshes.Length; i++)
                _hiddenMeshIndices.Add(i);
        }

        public void ShowAllMeshes()
        {
            _hiddenMeshIndices.Clear();
        }

        public IReadOnlyList<string> GetClipNames()
        {
            var names = new List<string>(_clips.Count);
            foreach (var c in _clips) names.Add(c.Name ?? "");
            return names;
        }

        public bool HasClip(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return _clipByName.ContainsKey(name) || FindClip(name.ToLowerInvariant()) >= 0;
        }

        public float GetClipDuration(string name)
        {
            int idx = ResolveClip(name);
            return idx >= 0 ? _clips[idx].Duration : 0f;
        }

        private int ResolveClip(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            return _clipByName.TryGetValue(name, out int idx) ? idx : FindClip(name.ToLowerInvariant());
        }

        // Crossfade to the named clip and loop it. Accepts an exact name or a
        // case-insensitive substring (so "walk" matches "Armature|walk").

        public bool IsPlaying(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return string.Equals(CurrentClipName, name, StringComparison.OrdinalIgnoreCase);
        }


        public bool Play(string name, float blendTime = 0.25f)
        {
            int idx = ResolveClip(name);
            return idx >= 0 && PlayIndex(idx, blendTime, loop: true);
        }

        // Play the named clip ONCE, then crossfade back to returnTo (looping). Used
        // for attacks (punch/jab/hook) that should fire and recover to a stance.
        public bool PlayOnce(string name, string returnTo, float blendTime = 0.15f)
        {
            int idx = ResolveClip(name);
            if (idx < 0) return false;
            int ret = ResolveClip(returnTo);
            if (!PlayIndex(idx, blendTime, loop: false)) return false;
            _returnClip  = ret;
            _returnBlend = blendTime;
            return true;
        }

        public bool PlayIndex(int idx, float blendTime = 0.25f, bool loop = true)
        {
            if (idx < 0 || idx >= _clips.Count) return false;
            if (idx == _curClip && _curLoop && loop) return true;   // already looping this clip

            if (blendTime > 0f && _curClip >= 0)
            {
                _prevClip  = _curClip;
                _prevTime  = _curTime;
                _blend     = 0f;
                _blendRate = 1f / blendTime;
            }
            else
            {
                _prevClip  = -1;
                _blend     = 1f;
                _blendRate = 0f;
            }
            _curClip    = idx;
            _curTime    = 0f;
            _curLoop    = loop;
            _returnClip = -1;
            return true;
        }

        // Apply animations from an external (animation-only) glTF file to this model.
        //
        // Bones are matched by *normalized* name (the Mixamo namespace prefix such as
        // "mixamorig:" / "mixamorig8:" is stripped), so a clip authored on one rig can
        // drive a different-but-topologically-identical rig. The two rigs can use
        // completely different bone-local axis conventions (e.g. one has every bone
        // world-aligned at bind, the other bone-aligned), so rotations are retargeted
        // in WORLD space: each source bone's world rotation-delta from its bind is
        // reapplied on the target bone's bind, then converted back to the target's
        // local frame. Translation/scale stay at the target's bind values (the
        // character keeps its own proportions and animates in place).
        // clipNameOverride: when set, the merged clip(s) are named after it (the file
        // name) instead of the embedded animation name — so a converted Mixamo file
        // like "jab.glb" becomes a clip called "jab" regardless of its internal name.
        // retargetRoot: also transfer the Hips (root) WORLD translation, so e.g. a
        // dying clip's fall actually lowers the body to the ground instead of the
        // rotation-only pose floating at hip height.
        public void ApplyExternalAnimation(GltfData animData, string? clipNameOverride = null, bool retargetRoot = false)
        {
            if (animData?.Animations == null || animData.Animations.Length == 0)
            {
                Console.WriteLine("[GltfObject] No animations in external file.");
                return;
            }

            var dstNodes = GpuData.Data.Nodes ?? [];
            var srcNodes = animData.Nodes ?? [];
            if (dstNodes.Length == 0 || srcNodes.Length == 0) return;

            // normalized bone name -> destination node index
            var dstByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < dstNodes.Length; i++)
            {
                var nm = NormalizeBoneName(dstNodes[i].Name);
                if (!string.IsNullOrEmpty(nm)) dstByName[nm] = i;
            }

            // source node -> destination node (-1 if unmatched)
            var srcToDst = new int[srcNodes.Length];
            for (int i = 0; i < srcNodes.Length; i++)
                srcToDst[i] = dstByName.TryGetValue(NormalizeBoneName(srcNodes[i].Name), out int di) ? di : -1;

            // Global bind orientations of both skeletons.
            var srcBindLocal = new Quaternion[srcNodes.Length];
            for (int i = 0; i < srcNodes.Length; i++) srcBindLocal[i] = Quaternion.Normalize(srcNodes[i].BaseRotation);
            var dstBindLocal = new Quaternion[dstNodes.Length];
            for (int i = 0; i < dstNodes.Length; i++) dstBindLocal[i] = Quaternion.Normalize(dstNodes[i].BaseRotation);
            var srcGlobalBind = ComputeGlobalRotations(srcNodes, srcBindLocal);
            var dstGlobalBind = ComputeGlobalRotations(dstNodes, dstBindLocal);
            var invSrcGlobalBind = new Quaternion[srcNodes.Length];
            for (int i = 0; i < srcNodes.Length; i++) invSrcGlobalBind[i] = Quaternion.Conjugate(srcGlobalBind[i]);

            // Root-motion retarget setup (Hips world translation).
            int srcHips = -1, dstHips = -1;
            Matrix4x4 invHipsParentGlobal = Matrix4x4.Identity;
            Vector3 srcHipsWorldBind = Vector3.Zero, dstHipsBindT = Vector3.Zero;
            float rootScale = 1f;   // target/source hip-height ratio (scales the fall to the target's size)
            if (retargetRoot)
            {
                for (int i = 0; i < srcNodes.Length; i++)
                    if (NormalizeBoneName(srcNodes[i].Name).Equals("Hips", StringComparison.OrdinalIgnoreCase)) { srcHips = i; break; }
                if (srcHips >= 0) dstHips = srcToDst[srcHips];
                if (srcHips >= 0 && dstHips >= 0)
                {
                    var srcBindMat = new Matrix4x4[srcNodes.Length];
                    for (int i = 0; i < srcNodes.Length; i++) srcBindMat[i] = srcNodes[i].LocalMatrix;
                    var srcBindGlobal = ComputeGlobalMatrices(srcNodes, srcBindMat);
                    srcHipsWorldBind = new Vector3(srcBindGlobal[srcHips].M41, srcBindGlobal[srcHips].M42, srcBindGlobal[srcHips].M43);

                    var dstBindMat = new Matrix4x4[dstNodes.Length];
                    for (int i = 0; i < dstNodes.Length; i++) dstBindMat[i] = dstNodes[i].LocalMatrix;
                    var dstBindGlobal = ComputeGlobalMatrices(dstNodes, dstBindMat);
                    int dp = dstNodes[dstHips].Parent;
                    Matrix4x4 parentGlobal = (dp >= 0 && dp < dstBindGlobal.Length) ? dstBindGlobal[dp] : Matrix4x4.Identity;
                    Matrix4x4.Invert(parentGlobal, out invHipsParentGlobal);
                    dstHipsBindT = dstNodes[dstHips].BaseTranslation;

                    var dstHipsWorldBind = new Vector3(dstBindGlobal[dstHips].M41, dstBindGlobal[dstHips].M42, dstBindGlobal[dstHips].M43);
                    rootScale = MathF.Abs(srcHipsWorldBind.Y) > 1e-3f ? dstHipsWorldBind.Y / srcHipsWorldBind.Y : 1f;
                }
                else retargetRoot = false;
            }

            int totalMatched = 0;
            int clipOrdinal = 0;
            foreach (var src in animData.Animations)
            {
                // Per source node: its rotation sampler in this clip (null if not animated).
                var srcRot = new GltfAnimationSampler?[srcNodes.Length];
                var timeSet = new SortedSet<float>();
                foreach (var ch in src.Channels)
                {
                    if (ch.Path != "rotation" || ch.TargetNode < 0 || ch.TargetNode >= srcNodes.Length) continue;
                    if (ch.SamplerIndex < 0 || ch.SamplerIndex >= src.Samplers.Length) continue;
                    var s = src.Samplers[ch.SamplerIndex];
                    if (s?.Input == null || s.OutputStride != 4) continue;
                    srcRot[ch.TargetNode] = s;
                    foreach (var t in s.Input) timeSet.Add(t);
                }
                if (timeSet.Count == 0) continue;
                var times = new float[timeSet.Count];
                timeSet.CopyTo(times);

                // Destination nodes that will receive baked rotation tracks.
                var matchedDst = new List<int>();
                var seenDst = new HashSet<int>();
                for (int si = 0; si < srcNodes.Length; si++)
                    if (srcRot[si] != null && srcToDst[si] >= 0 && seenDst.Add(srcToDst[si]))
                        matchedDst.Add(srcToDst[si]);
                if (matchedDst.Count == 0) continue;

                var baked = new Dictionary<int, float[]>();
                foreach (var di in matchedDst) baked[di] = new float[times.Length * 4];

                // Optional Hips world-translation track for this clip.
                GltfAnimationSampler? hipsTransSampler = null;
                if (retargetRoot && srcHips >= 0)
                    foreach (var ch in src.Channels)
                        if (ch.Path == "translation" && ch.TargetNode == srcHips
                            && ch.SamplerIndex >= 0 && ch.SamplerIndex < src.Samplers.Length)
                        {
                            var s = src.Samplers[ch.SamplerIndex];
                            if (s?.Input != null && s.OutputStride == 3) { hipsTransSampler = s; break; }
                        }
                float[]? hipsOut    = (retargetRoot && srcHips >= 0 && dstHips >= 0) ? new float[times.Length * 3] : null;
                var      srcLocalMat = hipsOut != null ? new Matrix4x4[srcNodes.Length] : null;

                var srcLocal = new Quaternion[srcNodes.Length];
                for (int f = 0; f < times.Length; f++)
                {
                    float t = times[f];

                    // Sample source local rotations (bind where not animated), then FK.
                    for (int si = 0; si < srcNodes.Length; si++)
                        srcLocal[si] = srcRot[si] != null ? SampleQuat(srcRot[si]!, t) : srcBindLocal[si];
                    var srcGlobal = ComputeGlobalRotations(srcNodes, srcLocal);

                    // Track the Hips world position to retarget its fall onto the target.
                    if (hipsOut != null)
                    {
                        for (int si = 0; si < srcNodes.Length; si++)
                            srcLocalMat![si] = Matrix4x4.CreateScale(srcNodes[si].BaseScale)
                                             * Matrix4x4.CreateFromQuaternion(srcLocal[si])
                                             * Matrix4x4.CreateTranslation(si == srcHips && hipsTransSampler != null
                                                   ? SampleVec3(hipsTransSampler, t) : srcNodes[si].BaseTranslation);
                        var srcGM = ComputeGlobalMatrices(srcNodes, srcLocalMat!);
                        var hw = new Vector3(srcGM[srcHips].M41, srcGM[srcHips].M42, srcGM[srcHips].M43);
                        var worldDelta = (hw - srcHipsWorldBind) * rootScale;   // scale the fall to the target's size
                        var localDelta = Vector3.TransformNormal(worldDelta, invHipsParentGlobal);
                        var lt = dstHipsBindT + localDelta;
                        hipsOut[f * 3] = lt.X; hipsOut[f * 3 + 1] = lt.Y; hipsOut[f * 3 + 2] = lt.Z;
                    }

                    // Target globals: bind, with each matched bone driven by the source
                    // bone's world-space delta.
                    var dstGlobal = (Quaternion[])dstGlobalBind.Clone();
                    for (int si = 0; si < srcNodes.Length; si++)
                    {
                        int di = srcToDst[si];
                        if (di < 0 || srcRot[si] == null) continue;
                        var worldDelta = srcGlobal[si] * invSrcGlobalBind[si];
                        dstGlobal[di] = Quaternion.Normalize(worldDelta * dstGlobalBind[di]);
                    }

                    // Convert each matched bone's target global back to a local rotation.
                    foreach (var di in matchedDst)
                    {
                        int p = dstNodes[di].Parent;
                        var pg = (p >= 0 && p < dstGlobal.Length) ? dstGlobal[p] : Quaternion.Identity;
                        var loc = Quaternion.Normalize(Quaternion.Conjugate(pg) * dstGlobal[di]);
                        var o = baked[di];
                        o[f * 4] = loc.X; o[f * 4 + 1] = loc.Y; o[f * 4 + 2] = loc.Z; o[f * 4 + 3] = loc.W;
                    }
                }

                var newSamplers = new List<GltfAnimationSampler>();
                var newChannels = new List<GltfAnimationChannel>();
                foreach (var di in matchedDst)
                {
                    int si = newSamplers.Count;
                    newSamplers.Add(new GltfAnimationSampler { Input = times, Output = baked[di], OutputStride = 4, Interpolation = "LINEAR" });
                    newChannels.Add(new GltfAnimationChannel { SamplerIndex = si, TargetNode = di, Path = "rotation" });
                }
                if (hipsOut != null && dstHips >= 0)
                {
                    int si = newSamplers.Count;
                    newSamplers.Add(new GltfAnimationSampler { Input = times, Output = hipsOut, OutputStride = 3, Interpolation = "LINEAR" });
                    newChannels.Add(new GltfAnimationChannel { SamplerIndex = si, TargetNode = dstHips, Path = "translation" });
                }
                totalMatched += newChannels.Count;
                string clipName = clipNameOverride == null
                    ? (src.Name ?? "")
                    : (animData.Animations.Length > 1 ? $"{clipNameOverride}{clipOrdinal}" : clipNameOverride);
                clipOrdinal++;
                RegisterClip(new GltfAnimation
                {
                    Name = clipName, Duration = src.Duration,
                    Samplers = [.. newSamplers], Channels = [.. newChannels]
                });
            }

            // Prefer an idle clip from the freshly added external set.
            int idle = FindClip("idle", "stand", "rest", "wait");
            if (idle >= 0) PlayIndex(idle, 0f);
            else if (_curClip < 0 && _clips.Count > 0) PlayIndex(0, 0f);

            //Console.WriteLine($"[GltfObject] External animation applied: {_clips.Count} clips total, {totalMatched} bones retargeted, current='{CurrentClipName}'");
        }

        // Strip a Mixamo-style namespace prefix ("mixamorig:", "mixamorig8:", …) so
        // bones can be matched across rigs exported in different sessions.
        private static string NormalizeBoneName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int c = name.IndexOf(':');
            return c >= 0 ? name[(c + 1)..] : name;
        }

        // Forward-kinematics for rotations only: global[i] = parentGlobal * local[i].
        private static Quaternion[] ComputeGlobalRotations(GltfNode[] nodes, Quaternion[] local)
        {
            var g = new Quaternion[nodes.Length];
            for (int i = 0; i < g.Length; i++) g[i] = Quaternion.Identity;

            void Rec(int idx, Quaternion parent)
            {
                var gg = Quaternion.Normalize(parent * local[idx]);
                g[idx] = gg;
                foreach (var c in nodes[idx].Children)
                    if (c >= 0 && c < nodes.Length) Rec(c, gg);
            }
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i].Parent == -1) Rec(i, Quaternion.Identity);
            return g;
        }

        // Full-transform FK: global[i] = local[i] * parentGlobal (row-vector).
        private static Matrix4x4[] ComputeGlobalMatrices(GltfNode[] nodes, Matrix4x4[] local)
        {
            var g = new Matrix4x4[nodes.Length];
            for (int i = 0; i < g.Length; i++) g[i] = Matrix4x4.Identity;

            void Rec(int idx, Matrix4x4 parent)
            {
                var gg = local[idx] * parent;
                g[idx] = gg;
                foreach (var c in nodes[idx].Children)
                    if (c >= 0 && c < nodes.Length) Rec(c, gg);
            }
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i].Parent == -1) Rec(i, Matrix4x4.Identity);
            return g;
        }

        private static Vector3 SampleVec3(GltfAnimationSampler s, float time)
        {
            var inp = s.Input;
            int idx = Array.BinarySearch(inp, time);
            if (idx < 0) idx = ~idx;
            int i0 = Math.Max(0, idx - 1), i1 = Math.Min(inp.Length - 1, idx);
            float t0 = inp[i0], t1 = inp[i1];
            float lt = (t1 - t0) <= 1e-6f ? 0f : Math.Clamp((time - t0) / (t1 - t0), 0f, 1f);
            var a = new Vector3(s.Output[i0 * 3], s.Output[i0 * 3 + 1], s.Output[i0 * 3 + 2]);
            var b = new Vector3(s.Output[i1 * 3], s.Output[i1 * 3 + 1], s.Output[i1 * 3 + 2]);
            return Vector3.Lerp(a, b, lt);
        }

        private static Quaternion SampleQuat(GltfAnimationSampler s, float time)
        {
            var inp = s.Input;
            int idx = Array.BinarySearch(inp, time);
            if (idx < 0) idx = ~idx;
            int i0 = Math.Max(0, idx - 1), i1 = Math.Min(inp.Length - 1, idx);
            float t0 = inp[i0], t1 = inp[i1];
            float lt = (t1 - t0) <= 1e-6f ? 0f : Math.Clamp((time - t0) / (t1 - t0), 0f, 1f);
            var q0 = new Quaternion(s.Output[i0 * 4], s.Output[i0 * 4 + 1], s.Output[i0 * 4 + 2], s.Output[i0 * 4 + 3]);
            var q1 = new Quaternion(s.Output[i1 * 4], s.Output[i1 * 4 + 1], s.Output[i1 * 4 + 2], s.Output[i1 * 4 + 3]);
            return Quaternion.Normalize(Quaternion.Slerp(q0, q1, lt));
        }

        // -----------------------------------------------------------------------
        //  Per-frame update: advance + blend clips, then rebuild the skeleton.
        // -----------------------------------------------------------------------
        public void Update(float dt)
        {
            // ============================================
            // 0. SKIP TOTAL JIKA TIDAK TERLIHAT (LOD3)
            // ============================================
            if (!IsVisible || AnimLOD == 3)
                return;

            // ============================================
            // 1. LOD2 — FREEZE POSE (tidak update animasi)
            // ============================================
            if (AnimLOD == 2)
                return;

            // ============================================
            // 2. LOD1 — UPDATE SETIAP 3 FRAME
            // ============================================
            if (AnimLOD == 1)
            {
                _lodFrameCounter++;
                if (_lodFrameCounter % 3 != 0)
                    return;
            }

            // ============================================
            // 3. FULL ANIMATION (LOD0)
            // ============================================
            var nodes = GpuData.Data.Nodes ?? [];
            EnsureBuffers(nodes);

            float adt = dt * MathF.Max(0f, PlaybackSpeed * Scale);

            if (_curClip >= 0 && _curClip < _clips.Count)
            {
                var cur = _clips[_curClip];
                float dur = MathF.Max(0.0001f, cur.Duration);

                if (_curLoop)
                {
                    _curTime = Advance(_curTime, adt, dur);
                }
                else
                {
                    _curTime += adt;
                    if (_curTime >= dur)
                    {
                        _curTime = dur;
                        if (_returnClip >= 0 && _returnClip != _curClip)
                        {
                            PlayIndex(_returnClip, _returnBlend, loop: true);
                            cur = _clips[_curClip];
                        }
                    }
                }

                bool blending = _blend < 1f && _prevClip >= 0 && _prevClip < _clips.Count;
                if (blending)
                {
                    _prevTime = Advance(_prevTime, adt, _clips[_prevClip].Duration);
                    _blend += _blendRate * dt;
                    if (_blend >= 1f) { _blend = 1f; _prevClip = -1; blending = false; }
                }

                SamplePose(cur, _curTime, _poseCur);

                if (blending)
                {
                    SamplePose(_clips[_prevClip], _prevTime, _posePrev);
                    for (int i = 0; i < _poseOut.Length; i++)
                        _poseOut[i] = BlendTransform(_posePrev[i], _poseCur[i], _blend);
                }
                else
                {
                    Array.Copy(_poseCur, _poseOut, _poseOut.Length);
                }

                for (int i = 0; i < _nodeLocal.Length; i++)
                    _nodeLocal[i] = ComposeTRS(_poseOut[i]);
            }
            else
            {
                for (int i = 0; i < _nodeLocal.Length; i++)
                    _nodeLocal[i] = nodes[i].LocalMatrix;
            }

            // FK
            for (int i = 0; i < _nodeGlobal.Length; i++) _nodeGlobal[i] = Matrix4x4.Identity;
            for (int i = 0; i < _nodeLocal.Length; i++)
                if (nodes[i].Parent == -1)
                    ComputeGlobalRec(i, Matrix4x4.Identity);

            ComputeJointMatricesIfNeeded();
        }


        public Matrix4x4[] GetJointMatrices() => _jointMatrices;

        // -----------------------------------------------------------------------
        //  Internal helpers
        // -----------------------------------------------------------------------

        private void RegisterClip(GltfAnimation anim)
        {
            if (anim?.Channels == null || anim.Channels.Length == 0) return;
            int idx = _clips.Count;
            _clips.Add(anim);
            string name = string.IsNullOrEmpty(anim.Name) ? $"clip{idx}" : anim.Name;
            _clipByName[name] = idx;
        }

        // First clip whose (lower-cased) name contains any of the given keys.
        private int FindClip(params string[] keys)
        {
            for (int i = 0; i < _clips.Count; i++)
            {
                var nm = (_clips[i].Name ?? "").ToLowerInvariant();
                if (nm.Length == 0) continue;
                foreach (var k in keys)
                    if (!string.IsNullOrEmpty(k) && nm.Contains(k)) return i;
            }
            return -1;
        }

        private static float Advance(float time, float dt, float duration)
        {
            float dur = MathF.Max(0.0001f, duration);
            time += dt;
            if (time >= dur) time %= dur;   // seamless loop
            if (time < 0f) time = 0f;
            return time;
        }

        private static NodeTransform BlendTransform(in NodeTransform a, in NodeTransform b, float w)
            => new()
            {
                T = Vector3.Lerp(a.T, b.T, w),
                R = Quaternion.Slerp(a.R, b.R, w),
                S = Vector3.Lerp(a.S, b.S, w)
            };

        private static Matrix4x4 ComposeTRS(in NodeTransform n)
            => Matrix4x4.CreateScale(n.S)
             * Matrix4x4.CreateFromQuaternion(n.R)
             * Matrix4x4.CreateTranslation(n.T);

        // Sample a clip at the given time into pose[], starting from the bind pose
        // and overriding only the channels the clip animates.
        private void SamplePose(GltfAnimation clip, float time, NodeTransform[] pose)
        {
            Array.Copy(_basePose, pose, _basePose.Length);
            if (clip?.Channels == null) return;

            foreach (var ch in clip.Channels)
            {
                if (ch.TargetNode < 0 || ch.TargetNode >= pose.Length) continue;
                if (ch.SamplerIndex < 0 || ch.SamplerIndex >= clip.Samplers.Length) continue;
                var sampler = clip.Samplers[ch.SamplerIndex];
                if (sampler?.Input == null || sampler.Input.Length == 0) continue;

                FindKeyframes(sampler.Input, time, out int i0, out int i1, out float lt);

                if (sampler.OutputStride == 3 && (ch.Path == "translation" || ch.Path == "scale"))
                {
                    int o0 = i0 * 3, o1 = i1 * 3;
                    var v0 = new Vector3(sampler.Output[o0], sampler.Output[o0 + 1], sampler.Output[o0 + 2]);
                    var v1 = new Vector3(sampler.Output[o1], sampler.Output[o1 + 1], sampler.Output[o1 + 2]);
                    var v = Vector3.Lerp(v0, v1, lt);
                    if (ch.Path == "translation") pose[ch.TargetNode].T = v;
                    else                          pose[ch.TargetNode].S = v;
                }
                else if (sampler.OutputStride == 4 && ch.Path == "rotation")
                {
                    int o0 = i0 * 4, o1 = i1 * 4;
                    var q0 = new Quaternion(sampler.Output[o0], sampler.Output[o0 + 1], sampler.Output[o0 + 2], sampler.Output[o0 + 3]);
                    var q1 = new Quaternion(sampler.Output[o1], sampler.Output[o1 + 1], sampler.Output[o1 + 2], sampler.Output[o1 + 3]);
                    pose[ch.TargetNode].R = Quaternion.Normalize(Quaternion.Slerp(q0, q1, lt));
                }
            }
        }

        private static void FindKeyframes(float[] input, float time, out int i0, out int i1, out float lt)
        {
            int idx = Array.BinarySearch(input, time);
            if (idx < 0) idx = ~idx;
            i0 = Math.Max(0, idx - 1);
            i1 = Math.Min(input.Length - 1, idx);
            float t0 = input[i0], t1 = input[i1];
            lt = (t1 - t0) <= 1e-6f ? 0f : Math.Clamp((time - t0) / (t1 - t0), 0f, 1f);
        }

        private void ComputeGlobalRec(int idx, Matrix4x4 parentGlobal)
        {
            // Row-vector: child first, then parent  ->  global = local * parentGlobal
            var global = _nodeLocal[idx] * parentGlobal;
            _nodeGlobal[idx] = global;
            foreach (var c in GpuData.Data.Nodes[idx].Children)
                if (c >= 0 && c < _nodeLocal.Length)
                    ComputeGlobalRec(c, global);
        }

        private void ComputeJointMatricesIfNeeded()
        {
            var skins = GpuData.Data.Skins ?? [];
            if (skins.Length == 0) { _jointMatrices = []; return; }

            var skin = skins[0]; // most rigs have a single skin
            int jointCount = skin.Joints?.Length ?? 0;
            if (jointCount == 0) { _jointMatrices = []; return; }

            if (_jointMatrices.Length != jointCount)
                _jointMatrices = new Matrix4x4[jointCount];

            for (int i = 0; i < jointCount; i++)
            {
                int node = skin.Joints?[i] ?? -1;
                if (node < 0 || node >= _nodeGlobal.Length) { _jointMatrices[i] = Matrix4x4.Identity; continue; }

                var invBind = (skin.InverseBindMatrices != null && i < skin.InverseBindMatrices.Length)
                    ? skin.InverseBindMatrices[i]
                    : Matrix4x4.Identity;

                // Row-vector skinning matrix: v * (invBind * jointGlobal)
                _jointMatrices[i] = invBind * _nodeGlobal[node];
            }
        }

        private void AllocateBuffers(GltfNode[] nodes)
        {
            int n = nodes.Length;
            _nodeLocal  = new Matrix4x4[n];
            _nodeGlobal = new Matrix4x4[n];
            _basePose   = new NodeTransform[n];
            _poseCur    = new NodeTransform[n];
            _posePrev   = new NodeTransform[n];
            _poseOut    = new NodeTransform[n];

            for (int i = 0; i < n; i++)
            {
                _nodeLocal[i]  = nodes[i].LocalMatrix;
                _nodeGlobal[i] = nodes[i].LocalMatrix;
                _basePose[i]   = new NodeTransform
                {
                    T = nodes[i].BaseTranslation,
                    R = nodes[i].BaseRotation,
                    S = nodes[i].BaseScale
                };
            }
        }

        private void EnsureBuffers(GltfNode[] nodes)
        {
            if (_nodeLocal != null && _nodeLocal.Length == nodes.Length) return;
            AllocateBuffers(nodes);
        }

        // -----------------------------------------------------------------------
        //  World-space helpers (positioning / terrain snapping)
        // -----------------------------------------------------------------------

        public void SetBasePosition(Vector3 pos) => Position = pos;

        // Conservative world AABB using the current node globals (used by snapping).
        public AABB ComputeWorldAABB()
        {
            if (GpuData.Data.Meshes == null || GpuData.Data.Meshes.Length == 0)
                return LocalAABB.ToWorld(Position, Scale);

            Vector3 mn = new(float.PositiveInfinity);
            Vector3 mx = new(float.NegativeInfinity);

            var objMat = Matrix4x4.CreateScale(Scale)
                         * Matrix4x4.CreateFromQuaternion(Rotation)
                         * Matrix4x4.CreateTranslation(Position);

            bool isSkinned = _jointMatrices != null && _jointMatrices.Length > 0;

            for (int mi = 0; mi < GpuData.Meshes.Length; mi++)
            {
                Matrix4x4 modelMat = objMat;
                if (!isSkinned)
                {
                    int nodeIdx = (GpuData.MeshToNode != null && mi < GpuData.MeshToNode.Length) ? GpuData.MeshToNode[mi] : -1;
                    if (nodeIdx >= 0 && _nodeGlobal != null && nodeIdx < _nodeGlobal.Length)
                        modelMat = _nodeGlobal[nodeIdx] * objMat;   // node-to-world, then object transform
                }

                var verts = GpuData.Data.Meshes[mi].Vertices;
                if (verts == null || verts.Length == 0) continue;

                for (int vi = 0; vi < verts.Length; vi++)
                {
                    var wp = Vector3.Transform(verts[vi].Position, modelMat);
                    mn = Vector3.Min(mn, wp);
                    mx = Vector3.Max(mx, wp);
                }
            }

            if (float.IsPositiveInfinity(mn.X))
                return LocalAABB.ToWorld(Position, Scale);

            return new AABB(mn, mx);
        }

        public void AlignToTerrain(DarkEngine3D_gl_csharp.Engine.Terrains.TerrainChunk terrain)
        {
            var aabb = ComputeWorldAABB();
            float terrainY = terrain.GetHeightAt(Position.X, Position.Z);
            float delta = terrainY - aabb.Min.Y;
            Position = new Vector3(Position.X, Position.Y + delta, Position.Z);
        }

        // -----------------------------------------------------------------------
        //  Draw
        // -----------------------------------------------------------------------
        public void Draw(int modelLoc, int baseColorFactorLoc, int useAlbedoLoc, int albedoMapLoc)
        {
            var objMat = Matrix4x4.CreateScale(Scale)
                         * Matrix4x4.CreateFromQuaternion(Rotation)
                         * Matrix4x4.CreateTranslation(Position);
             
            bool isSkinned = _jointMatrices != null && _jointMatrices.Length > 0;
            OpenGL.EnableFaceCulling(true);
            for (int mi = 0; mi < GpuData.Meshes.Length; mi++)
            {
                // Skip hidden meshes (for 1st person camera mode)
                if (_hiddenMeshIndices.Contains(mi))
                    continue;

                var mesh = GpuData.Meshes[mi];

                Matrix4x4 modelMat = objMat;
                if (!isSkinned)
                {
                    int nodeIdx = (GpuData.MeshToNode != null && mi < GpuData.MeshToNode.Length) ? GpuData.MeshToNode[mi] : -1;
                    if (nodeIdx >= 0 && _nodeGlobal != null && nodeIdx < _nodeGlobal.Length)
                        modelMat = _nodeGlobal[nodeIdx] * objMat;
                }

                GL.UniformMatrix4fv(modelLoc, 1, false, (float*)Unsafe.AsPointer(ref modelMat));

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
                    if (albedoMapLoc != -1) GL.Uniform1i(albedoMapLoc, 0);
                }
                else
                {
                    if (useAlbedoLoc != -1) GL.Uniform1i(useAlbedoLoc, 0);
                }

                GL.BindVertexArray(mesh.VAO);
                if (mesh.IndexCount > 0) GL.DrawElements(Const.GL_TRIANGLES, mesh.IndexCount, Const.GL_UNSIGNED_INT, null);
                else GL.DrawArrays(Const.GL_TRIANGLES, 0, mesh.VertexCount);
            }
            OpenGL.EnableFaceCulling(false);
            GL.BindVertexArray(0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);  
        }

        public void DrawShadow(int modelLoc, int jointsLoc)
        {
            // LOD3 = fully skipped, no shadow either
            if (!IsVisible || AnimLOD == 3)
                return;

            var objMat = Matrix4x4.CreateScale(Scale)
                         * Matrix4x4.CreateFromQuaternion(Rotation)
                         * Matrix4x4.CreateTranslation(Position);

            bool isSkinned = _jointMatrices != null && _jointMatrices.Length > 0;

            if (isSkinned && jointsLoc >= 0)
            {
                // For LOD2 (frozen animation), _jointMatrices may be stale/default.
                // We still upload whatever we have — it produces a valid (possibly
                // T-pose) shadow silhouette that is never cut off at the feet.
                fixed (Matrix4x4* p = &_jointMatrices[0])
                    GL.UniformMatrix4fv(jointsLoc, _jointMatrices.Length, false, (float*)p);
            }

            // Disable face-culling for shadow pass so we don't lose back faces
            // on low-poly LOD silhouettes (avoids cut-off feet artefact).
             
            for (int mi = 0; mi < GpuData.Meshes.Length; mi++)
            {
                var mesh = GpuData.Meshes[mi];

                Matrix4x4 modelMat = objMat;
                if (!isSkinned)
                {
                    int nodeIdx = (GpuData.MeshToNode != null && mi < GpuData.MeshToNode.Length) ? GpuData.MeshToNode[mi] : -1;
                    if (nodeIdx >= 0 && _nodeGlobal != null && nodeIdx < _nodeGlobal.Length)
                        modelMat = _nodeGlobal[nodeIdx] * objMat;
                }

                GL.UniformMatrix4fv(modelLoc, 1, false, (float*)Unsafe.AsPointer(ref modelMat));

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
