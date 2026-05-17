using System.Numerics;
using System.Text;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    // ===========================================================================
    //  glTF 2.0 Data Model
    // ===========================================================================
    public class GltfData
    {
        public GltfMeshData[] Meshes = [];
        public GltfSkin? Skin;
        public GltfAnimation[] Animations = [];
        public byte[][] ImageData = [];
        /// <summary>Node children map: nodeIndex → list of child nodeIndices</summary>
        public Dictionary<int, int[]> NodeChildren = [];
        /// <summary>Node local transform: nodeIndex → TRS matrix (Identity if not in any animation)</summary>
        public Matrix4x4[] NodeRestPose = [];
    }

    public class GltfMeshData
    {
        public string Name = "";
        public SkinnedVertex[] Vertices = [];
        public uint[] Indices = [];
    }

    public class GltfSkin
    {
        public int[] Joints = [];
        public Matrix4x4[] InvBindMats = [];
    }

    public class GltfAnimation
    {
        public string Name = "";
        public float Duration;
        public GltfAnimChannel[] Channels = [];
    }

    public class GltfAnimChannel
    {
        public int JointIndex;
        public AnimPath Path;
        public float[] Times = [];
        public Vector3[]? ValueV3;
        public Quaternion[]? ValueQ;
    }

    public enum AnimPath { Translation, Rotation, Scale }

    // ===========================================================================
    //  GLB Parser — pure C#, zero external dependencies
    // ===========================================================================
    public class GltfLoader
    {
        private byte[] _bin = [];
        private (int off, int len, int stride)[] _bvs = [];
        private Accessor[] _accs = [];

        // -----------------------------------------------------------------------
        public static GltfData Load(string path)
        {
            var raw = File.ReadAllBytes(path);
            return new GltfLoader().ParseGlb(raw);
        }

        // -----------------------------------------------------------------------
        private GltfData ParseGlb(byte[] raw)
        {
            if (R32(raw, 0) != 0x46546C67u) throw new Exception("Bukan file GLB valid!");

            uint jsonLen = R32(raw, 12);
            if (R32(raw, 16) != 0x4E4F534Au) throw new Exception("Chunk JSON tidak ditemukan!");
            string json = Encoding.UTF8.GetString(raw, 20, (int)jsonLen);

            // Cari chunk BIN
            int binOff = 20 + (int)jsonLen;
            if (binOff + 8 <= raw.Length && R32(raw, binOff + 4) == 0x004E4942u)
            {
                uint binLen = R32(raw, binOff);
                _bin = new byte[binLen];
                Array.Copy(raw, binOff + 8, _bin, 0, (int)binLen);
            }

            return ParseJson(json);
        }

        // -----------------------------------------------------------------------
        private GltfData ParseJson(string jsonStr)
        {
            var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;
            var data = new GltfData();

            _bvs = ParseBufferViews(root);
            _accs = ParseAccessors(root);

            data.ImageData = ParseImages(root);
            data.Meshes = ParseMeshes(root);
            data.Skin = ParseSkins(root);
            data.Animations = ParseAnimations(root, data.Skin);
            ParseNodes(root, data);

            return data;
        }

        // ========================= BUFFER VIEWS =================================
        private (int off, int len, int stride)[] ParseBufferViews(JsonElement root)
        {
            if (!root.TryGetProperty("bufferViews", out var el)) return [];
            var arr = new (int, int, int)[el.GetArrayLength()];
            int i = 0;
            foreach (var bv in el.EnumerateArray())
                arr[i++] = (
                    bv.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0,
                    bv.GetProperty("byteLength").GetInt32(),
                    bv.TryGetProperty("byteStride", out var s) ? s.GetInt32() : 0);
            return arr;
        }

        // ========================= ACCESSORS ====================================
        private record struct Accessor(int BV, int ByteOffset, int Count, int CompType, string Type);

        private Accessor[] ParseAccessors(JsonElement root)
        {
            if (!root.TryGetProperty("accessors", out var el)) return [];
            var arr = new Accessor[el.GetArrayLength()];
            int i = 0;
            foreach (var ac in el.EnumerateArray())
                arr[i++] = new Accessor(
                    ac.TryGetProperty("bufferView", out var bv) ? bv.GetInt32() : 0,
                    ac.TryGetProperty("byteOffset", out var off) ? off.GetInt32() : 0,
                    ac.GetProperty("count").GetInt32(),
                    ac.GetProperty("componentType").GetInt32(),
                    ac.GetProperty("type").GetString()!);
            return arr;
        }

        // ========================= IMAGES =======================================
        private byte[][] ParseImages(JsonElement root)
        {
            if (!root.TryGetProperty("images", out var el)) return [];
            var arr = new byte[el.GetArrayLength()][];
            int i = 0;
            foreach (var img in el.EnumerateArray())
            {
                if (img.TryGetProperty("bufferView", out var bvIdx))
                {
                    var (off, len, _) = _bvs[bvIdx.GetInt32()];
                    arr[i] = _bin.AsSpan(off, len).ToArray();
                }
                i++;
            }
            return arr;
        }

        // ========================= MESHES =======================================
        private GltfMeshData[] ParseMeshes(JsonElement root)
        {
            if (!root.TryGetProperty("meshes", out var meshesEl)) return [];
            var list = new List<GltfMeshData>();

            foreach (var mesh in meshesEl.EnumerateArray())
            {
                string name = mesh.TryGetProperty("name", out var nm) ? nm.GetString()! : "mesh";

                foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
                {
                    var attrs = prim.GetProperty("attributes");

                    var positions = RVec3(attrs.GetProperty("POSITION").GetInt32());
                    var normals = attrs.TryGetProperty("NORMAL", out var nEl) ? RVec3(nEl.GetInt32()) : new Vector3[positions.Length];
                    var uvs = attrs.TryGetProperty("TEXCOORD_0", out var uEl) ? RVec2(uEl.GetInt32()) : new Vector2[positions.Length];

                    var boneIds = new Vector4[positions.Length];
                    var boneWts = new Vector4[positions.Length];
                    if (attrs.TryGetProperty("JOINTS_0", out var jEl) &&
                        attrs.TryGetProperty("WEIGHTS_0", out var wEl))
                    {
                        boneIds = RJoints(jEl.GetInt32());
                        boneWts = RVec4(wEl.GetInt32());
                    }

                    var verts = new SkinnedVertex[positions.Length];
                    for (int i = 0; i < positions.Length; i++)
                        verts[i] = new SkinnedVertex(positions[i], normals[i], uvs[i], boneWts[i], boneIds[i]);

                    uint[] indices = prim.TryGetProperty("indices", out var idxEl) ? RUInt(idxEl.GetInt32()) : [];

                    list.Add(new GltfMeshData { Name = name, Vertices = verts, Indices = indices });
                }
            }
            return list.ToArray();
        }

        // ========================= SKINS ========================================
        private GltfSkin? ParseSkins(JsonElement root)
        {
            if (!root.TryGetProperty("skins", out var el) || el.GetArrayLength() == 0) return null;
            var sk = el[0];

            var jointArr = sk.GetProperty("joints");
            int[] joints = new int[jointArr.GetArrayLength()];
            for (int i = 0; i < joints.Length; i++) joints[i] = jointArr[i].GetInt32();

            Matrix4x4[] ibm = joints.Length > 0
                ? (sk.TryGetProperty("inverseBindMatrices", out var ibmEl) ? RMat4(ibmEl.GetInt32()) : FillIdentity(joints.Length))
                : [];

            return new GltfSkin { Joints = joints, InvBindMats = ibm };
        }

        // ========================= NODES ========================================
        private static void ParseNodes(JsonElement root, GltfData data)
        {
            if (!root.TryGetProperty("nodes", out var nodesEl)) return;

            int count = nodesEl.GetArrayLength();
            data.NodeRestPose = new Matrix4x4[count];
            data.NodeChildren = new Dictionary<int, int[]>(count);

            int ni = 0;
            foreach (var node in nodesEl.EnumerateArray())
            {
                // Rest-pose TRS
                Vector3 t = Vector3.Zero;
                Quaternion r = Quaternion.Identity;
                Vector3 s = Vector3.One;

                if (node.TryGetProperty("translation", out var tEl))
                    t = new Vector3(tEl[0].GetSingle(), tEl[1].GetSingle(), tEl[2].GetSingle());
                if (node.TryGetProperty("rotation", out var rEl))
                    r = new Quaternion(rEl[0].GetSingle(), rEl[1].GetSingle(), rEl[2].GetSingle(), rEl[3].GetSingle());
                if (node.TryGetProperty("scale", out var sEl))
                    s = new Vector3(sEl[0].GetSingle(), sEl[1].GetSingle(), sEl[2].GetSingle());

                data.NodeRestPose[ni] =
                    Matrix4x4.CreateTranslation(t) *
                    Matrix4x4.CreateFromQuaternion(r) *
                    Matrix4x4.CreateScale(s);


                // Children
                if (node.TryGetProperty("children", out var chEl))
                {
                    var ch = new int[chEl.GetArrayLength()];
                    for (int c = 0; c < ch.Length; c++) ch[c] = chEl[c].GetInt32();
                    data.NodeChildren[ni] = ch;
                }

                ni++;
            }
        }

        // ========================= ANIMATIONS ===================================
        private GltfAnimation[] ParseAnimations(JsonElement root, GltfSkin? skin)
        {
            if (!root.TryGetProperty("animations", out var el)) return [];

            // node → joint index map
            var n2j = new Dictionary<int, int>();
            if (skin != null)
                for (int i = 0; i < skin.Joints.Length; i++)
                    n2j[skin.Joints[i]] = i;

            var result = new GltfAnimation[el.GetArrayLength()];
            int ai = 0;

            foreach (var anim in el.EnumerateArray())
            {
                var ga = new GltfAnimation();
                ga.Name = anim.TryGetProperty("name", out var nm) ? nm.GetString()! : $"anim{ai}";

                var samps = anim.GetProperty("samplers");
                var chans = anim.GetProperty("channels");

                // Preload sampler input times
                var sampTimes = new float[samps.GetArrayLength()][];
                int si = 0;
                foreach (var s in samps.EnumerateArray())
                    sampTimes[si++] = RFloat(s.GetProperty("input").GetInt32());

                float maxT = 0f;
                var channels = new List<GltfAnimChannel>();

                foreach (var ch in chans.EnumerateArray())
                {
                    int sampIdx = ch.GetProperty("sampler").GetInt32();
                    var target = ch.GetProperty("target");
                    if (!target.TryGetProperty("node", out var nodeEl)) continue;
                    int nodeIdx = nodeEl.GetInt32();
                    if (!n2j.TryGetValue(nodeIdx, out int jIdx)) continue;

                    string pathStr = target.GetProperty("path").GetString()!;
                    var samp = samps[sampIdx];
                    int outIdx = samp.GetProperty("output").GetInt32();

                    var gc = new GltfAnimChannel { JointIndex = jIdx, Times = sampTimes[sampIdx] };
                    float localMax = gc.Times.Length > 0 ? gc.Times[^1] : 0f;
                    if (localMax > maxT) maxT = localMax;

                    switch (pathStr)
                    {
                        case "translation": gc.Path = AnimPath.Translation; gc.ValueV3 = RVec3(outIdx); break;
                        case "rotation": gc.Path = AnimPath.Rotation; gc.ValueQ = RQuat(outIdx); break;
                        case "scale": gc.Path = AnimPath.Scale; gc.ValueV3 = RVec3(outIdx); break;
                    }
                    channels.Add(gc);
                }

                ga.Channels = [..channels];
                ga.Duration = maxT;
                result[ai++] = ga;
            }

            return result;
        }

        // ===========================================================================
        //  Accessor Readers (instance methods → dapat akses _bin dan _bvs langsung)
        // ===========================================================================
        private Span<byte> Span(int accIdx)
        {
            var acc = _accs[accIdx];
            var (off, _, _) = _bvs[acc.BV];
            return _bin.AsSpan(off + acc.ByteOffset);
        }

        private float[] RFloat(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new float[acc.Count];
            for (int j = 0; j < r.Length; j++) r[j] = BitConverter.ToSingle(sp[(j * 4)..]);
            return r;
        }

        private Vector3[] RVec3(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new Vector3[acc.Count];
            for (int j = 0; j < r.Length; j++)
            { int o = j * 12; r[j] = new Vector3(ToF(sp, o), ToF(sp, o + 4), ToF(sp, o + 8)); }
            return r;
        }

        private Vector2[] RVec2(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new Vector2[acc.Count];
            for (int j = 0; j < r.Length; j++)
            { int o = j * 8; r[j] = new Vector2(ToF(sp, o), ToF(sp, o + 4)); }
            return r;
        }

        private Vector4[] RVec4(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new Vector4[acc.Count];
            for (int j = 0; j < r.Length; j++)
            { int o = j * 16; r[j] = new Vector4(ToF(sp, o), ToF(sp, o + 4), ToF(sp, o + 8), ToF(sp, o + 12)); }
            return r;
        }

        private Quaternion[] RQuat(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new Quaternion[acc.Count];
            for (int j = 0; j < r.Length; j++)
            { int o = j * 16; r[j] = new Quaternion(ToF(sp, o), ToF(sp, o + 4), ToF(sp, o + 8), ToF(sp, o + 12)); }
            return r;
        }

        private Matrix4x4[] RMat4(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new Matrix4x4[acc.Count];
            for (int j = 0; j < acc.Count; j++)
            {
                int o = j * 64;
                var m = new Matrix4x4(
                    ToF(sp, o), ToF(sp, o + 4), ToF(sp, o + 8), ToF(sp, o + 12),
                    ToF(sp, o + 16), ToF(sp, o + 20), ToF(sp, o + 24), ToF(sp, o + 28),
                    ToF(sp, o + 32), ToF(sp, o + 36), ToF(sp, o + 40), ToF(sp, o + 44),
                    ToF(sp, o + 48), ToF(sp, o + 52), ToF(sp, o + 56), ToF(sp, o + 60));
                r[j] = Matrix4x4.Transpose(m);
            }
            return r;
        }

        private uint[] RUInt(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new uint[acc.Count];
            int ct = acc.CompType;
            int stride = ct == 5121 ? 1 : ct == 5123 ? 2 : 4;
            for (int j = 0; j < r.Length; j++)
            {
                int o = j * stride;
                r[j] = ct switch { 5121 => sp[o], 5123 => BitConverter.ToUInt16(sp[o..]), _ => BitConverter.ToUInt32(sp[o..]) };
            }
            return r;
        }

        private Vector4[] RJoints(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new Vector4[acc.Count];
            int ct = acc.CompType;
            int stride = ct == 5121 ? 4 : 8;
            for (int j = 0; j < r.Length; j++)
            {
                int o = j * stride;
                r[j] = ct == 5121
                    ? new Vector4(sp[o], sp[o + 1], sp[o + 2], sp[o + 3])
                    : new Vector4(
                        BitConverter.ToUInt16(sp[o..]),
                        BitConverter.ToUInt16(sp[(o + 2)..]),
                        BitConverter.ToUInt16(sp[(o + 4)..]),
                        BitConverter.ToUInt16(sp[(o + 6)..]));
            }
            return r;
        }

        // ---- micro helpers ----
        private static float ToF(Span<byte> sp, int o) => BitConverter.ToSingle(sp[o..]);
        private static uint R32(byte[] b, int o) => BitConverter.ToUInt32(b, o);
        private static Matrix4x4[] FillIdentity(int n)
        {
            var m = new Matrix4x4[n];
            for (int i = 0; i < n; i++) m[i] = Matrix4x4.Identity;
            return m;
        }
    }
}
