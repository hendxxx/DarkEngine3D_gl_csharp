using System;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    // ===========================================================================
    //  glTF 2.0 Data Model — extended with Nodes & Animations (TRS sampling)
    // ===========================================================================
    public class GltfMaterial
    {
        public string Name = "";
        public Vector4 BaseColorFactor = Vector4.One;
        public int TextureIndex = -1;
        public bool DoubleSided = false;
    }

    public class GltfImage
    {
        public byte[] Data = [];
        public string MimeType = "";
        public string Uri = "";
    }

    public class GltfTexture
    {
        public int ImageIndex = -1;
    }

    public class GltfMeshData
    {
        public string Name = "";
        public SkinnedVertex[] Vertices = [];
        public uint[] Indices = [];
        public int MaterialIndex = -1;
    }

    public class GltfNode
    {
        public string Name = "";
        public int Mesh = -1;
        public int[] Children = [];
        public Matrix4x4 LocalMatrix = Matrix4x4.Identity;
        public int Parent = -1;
    }

    public class GltfAnimationSampler
    {
        public float[] Input = [];
        public float[] Output = [];
        public int OutputStride = 0;
        public string Interpolation = "LINEAR";
    }

    public class GltfAnimationChannel
    {
        public int SamplerIndex;
        public int TargetNode;
        public string Path = "";
    }

    public class GltfAnimation
    {
        public string Name = "";
        public GltfAnimationSampler[] Samplers = [];
        public GltfAnimationChannel[] Channels = [];
        public float Duration = 0f;
    }

    public class GltfSkin
    {
        public int[] Joints = [];
        public Matrix4x4[] InverseBindMatrices = [];
        public int SkeletonRoot = -1;
    }

    public class GltfData
    {
        public GltfMeshData[] Meshes = [];
        public GltfMaterial[] Materials = [];
        public GltfImage[] Images = [];
        public GltfTexture[] Textures = [];
        public GltfNode[] Nodes = [];
        public GltfAnimation[] Animations = [];
        public GltfSkin[] Skins = [];
    }

    // ===========================================================================
    //  GLB Parser — pure C#, zero external dependencies
    //  Extended: parse nodes + animations (TRS)
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
            string baseDir = Path.GetDirectoryName(path) ?? "";
            return new GltfLoader().ParseGlb(raw, baseDir);
        }

        // -----------------------------------------------------------------------
        private GltfData ParseGlb(byte[] raw, string baseDir)
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
            else
            {
                _bin = [];
            }

            return ParseJson(json, baseDir);
        }

        // -----------------------------------------------------------------------
        private GltfData ParseJson(string jsonStr, string baseDir)
        {
            var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;
            var data = new GltfData();

            _bvs = ParseBufferViews(root);
            _accs = ParseAccessors(root);

            data.Images = ParseImages(root, baseDir);
            data.Textures = ParseTextures(root);
            data.Materials = ParseMaterials(root);
            data.Meshes = ParseMeshes(root);
            data.Nodes = ParseNodes(root);
            data.Skins = ParseSkins(root);
            data.Animations = ParseAnimations(root);

            // establish parent links
            for (int i = 0; i < data.Nodes.Length; i++)
            {
                var n = data.Nodes[i];
                foreach (var c in n.Children)
                {
                    if (c >= 0 && c < data.Nodes.Length) data.Nodes[c].Parent = i;
                }
            }

            return data;
        }

        // ========================= BUFFER VIEWS =================================
        private (int off, int len, int stride)[] ParseBufferViews(JsonElement root)
        {
            if (!root.TryGetProperty("bufferViews", out var el)) return [];
            var arr = new (int, int, int)[el.GetArrayLength()];
            int i = 0;
            foreach (var bv in el.EnumerateArray())
            {
                int off = bv.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
                int len = bv.GetProperty("byteLength").GetInt32();
                int stride = bv.TryGetProperty("byteStride", out var s) ? s.GetInt32() : 0;
                arr[i++] = (off, len, stride);
            }
            return arr;
        }

        // ========================= ACCESSORS ====================================
        private record Accessor(int BufView, int ByteOffset, int Count, int CompType, string Type);

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
        private GltfImage[] ParseImages(JsonElement root, string baseDir)
        {
            if (!root.TryGetProperty("images", out var el)) return [];
            var list = new List<GltfImage>();
            foreach (var img in el.EnumerateArray())
            {
                var image = new GltfImage();
                if (img.TryGetProperty("mimeType", out var mimeProp))
                    image.MimeType = mimeProp.GetString() ?? "";

                if (img.TryGetProperty("bufferView", out var bvProp))
                {
                    int bvIdx = bvProp.GetInt32();
                    var (off, len, _) = _bvs[bvIdx];
                    image.Data = new byte[len];
                    Array.Copy(_bin, off, image.Data, 0, len);
                }
                else if (img.TryGetProperty("uri", out var uriProp))
                {
                    string uri = uriProp.GetString() ?? "";
                    image.Uri = uri;
                    if (uri.StartsWith("data:"))
                    {
                        int commaIdx = uri.IndexOf(',');
                        if (commaIdx >= 0)
                        {
                            string base64Data = uri.Substring(commaIdx + 1);
                            image.Data = Convert.FromBase64String(base64Data);
                        }
                    }
                    else if (!string.IsNullOrEmpty(baseDir))
                    {
                        string imgPath = Path.Combine(baseDir, uri);
                        if (File.Exists(imgPath))
                        {
                            image.Data = File.ReadAllBytes(imgPath);
                        }
                    }
                }
                list.Add(image);
            }
            return [..list];
        }

        // ========================= TEXTURES =====================================
        private GltfTexture[] ParseTextures(JsonElement root)
        {
            if (!root.TryGetProperty("textures", out var el)) return [];
            var list = new List<GltfTexture>();
            foreach (var tex in el.EnumerateArray())
            {
                var texture = new GltfTexture();
                if (tex.TryGetProperty("source", out var srcProp))
                {
                    texture.ImageIndex = srcProp.GetInt32();
                }
                list.Add(texture);
            }
            return [..list];
        }

        // ========================= MATERIALS ====================================
        private GltfMaterial[] ParseMaterials(JsonElement root)
        {
            if (!root.TryGetProperty("materials", out var el)) return [];
            var list = new List<GltfMaterial>();
            foreach (var mat in el.EnumerateArray())
            {
                var material = new GltfMaterial();
                if (mat.TryGetProperty("name", out var nameProp))
                    material.Name = nameProp.GetString() ?? "";

                if (mat.TryGetProperty("pbrMetallicRoughness", out var pbr))
                {
                    if (pbr.TryGetProperty("baseColorFactor", out var factorProp) && factorProp.ValueKind == JsonValueKind.Array)
                    {
                        float r = 1f, g = 1f, b = 1f, a = 1f;
                        int idx = 0;
                        foreach (var val in factorProp.EnumerateArray())
                        {
                            if (idx == 0) r = val.GetSingle();
                            else if (idx == 1) g = val.GetSingle();
                            else if (idx == 2) b = val.GetSingle();
                            else if (idx == 3) a = val.GetSingle();
                            idx++;
                        }
                        material.BaseColorFactor = new Vector4(r, g, b, a);
                    }

                    if (pbr.TryGetProperty("baseColorTexture", out var texProp))
                    {
                        if (texProp.TryGetProperty("index", out var indexProp))
                        {
                            material.TextureIndex = indexProp.GetInt32();
                        }
                    }
                }

                if (mat.TryGetProperty("doubleSided", out var dsProp))
                {
                    material.DoubleSided = dsProp.GetBoolean();
                }

                list.Add(material);
            }
            return [..list];
        }

        // ========================= MESHES =======================================
        private GltfMeshData[] ParseMeshes(JsonElement root)
        {
            if (!root.TryGetProperty("meshes", out var el)) return [];
            var list = new List<GltfMeshData>();

            foreach (var mesh in el.EnumerateArray())
            {
                string meshName = mesh.TryGetProperty("name", out var nm) ? nm.GetString()! : "";
                foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
                {
                    var attrs = prim.GetProperty("attributes");

                    var positions = RVec3(attrs.GetProperty("POSITION").GetInt32());
                    var normals = attrs.TryGetProperty("NORMAL", out var nEl) ? RVec3(nEl.GetInt32()) : new Vector3[positions.Length];
                    var uvs = attrs.TryGetProperty("TEXCOORD_0", out var uEl) ? RVec2(uEl.GetInt32()) : new Vector2[positions.Length];

                    SkinnedVertex.BoneIndex4[] joints = null;
                    Vector4[] weights = null;
                    if (attrs.TryGetProperty("JOINTS_0", out var jEl))
                    {
                        joints = RJoints(jEl.GetInt32());
                    }
                    if (attrs.TryGetProperty("WEIGHTS_0", out var wEl))
                    {
                        var ws = RVec4(wEl.GetInt32());
                        weights = new Vector4[ws.Length];
                        for (int k = 0; k < ws.Length; k++) weights[k] = ws[k];
                    }

                    var verts = new SkinnedVertex[positions.Length];
                    for (int i = 0; i < positions.Length; i++)
                    {
                        var bw = weights != null ? weights[i] : new Vector4(1, 0, 0, 0);
                        var bj = joints != null ? joints[i] : new SkinnedVertex.BoneIndex4 { X = 0, Y = 0, Z = 0, W = 0 };
                        verts[i] = new SkinnedVertex(positions[i], normals[i], uvs[i], bw, bj);
                        
                        // Debug: log first 3 vertices
                        if (i < 3)
                        {
                            Console.WriteLine($"[ParseMeshes] Vert[{i}] pos={positions[i]} joints=({bj.X},{bj.Y},{bj.Z},{bj.W}) weights={bw}");
                        }
                    }

                    uint[] indices = prim.TryGetProperty("indices", out var idxEl)
                        ? RIndices(idxEl.GetInt32())
                        : [];

                    int matIdx = prim.TryGetProperty("material", out var matEl) ? matEl.GetInt32() : -1;

                    list.Add(new GltfMeshData { Name = meshName, Vertices = verts, Indices = indices, MaterialIndex = matIdx });
                }
            }
            return [..list];
        }

        // ========================= NODES ========================================
        private GltfNode[] ParseNodes(JsonElement root)
        {
            if (!root.TryGetProperty("nodes", out var el)) return [];
            var list = new List<GltfNode>();
            foreach (var nd in el.EnumerateArray())
            {
                var node = new GltfNode();
                if (nd.TryGetProperty("name", out var nm)) node.Name = nm.GetString() ?? "";
                if (nd.TryGetProperty("mesh", out var m)) node.Mesh = m.GetInt32();
                if (nd.TryGetProperty("children", out var ch))
                {
                    var ids = new List<int>();
                    foreach (var c in ch.EnumerateArray()) ids.Add(c.GetInt32());
                    node.Children = ids.ToArray();
                }

                // matrix or TRS
                if (nd.TryGetProperty("matrix", out var matProp) && matProp.ValueKind == JsonValueKind.Array)
                {
                    // glTF stores matrices in column-major order. Convert to C# Matrix4x4 (row-major)
                    float[] mm = new float[16];
                    int i = 0;
                    foreach (var v in matProp.EnumerateArray()) mm[i++] = v.GetSingle();

                    float m00 = mm[0]; float m10 = mm[1]; float m20 = mm[2]; float m30 = mm[3];
                    float m01 = mm[4]; float m11 = mm[5]; float m21 = mm[6]; float m31 = mm[7];
                    float m02 = mm[8]; float m12 = mm[9]; float m22 = mm[10]; float m32 = mm[11];
                    float m03 = mm[12]; float m13 = mm[13]; float m23 = mm[14]; float m33 = mm[15];

                    node.LocalMatrix = new Matrix4x4(
                        m00, m01, m02, m03,
                        m10, m11, m12, m13,
                        m20, m21, m22, m23,
                        m30, m31, m32, m33);
                }
                else
                {
                    Vector3 t = Vector3.Zero;
                    Quaternion r = Quaternion.Identity;
                    Vector3 s = Vector3.One;
                    if (nd.TryGetProperty("translation", out var tProp))
                    {
                        var arr = tProp.EnumerateArray().ToArray();
                        if (arr.Length >= 3) t = new Vector3(arr[0].GetSingle(), arr[1].GetSingle(), arr[2].GetSingle());
                    }
                    if (nd.TryGetProperty("rotation", out var rProp))
                    {
                        var arr = rProp.EnumerateArray().ToArray();
                        if (arr.Length >= 4) r = new Quaternion(arr[0].GetSingle(), arr[1].GetSingle(), arr[2].GetSingle(), arr[3].GetSingle());
                    }
                    if (nd.TryGetProperty("scale", out var sProp))
                    {
                        var arr = sProp.EnumerateArray().ToArray();
                        if (arr.Length >= 3) s = new Vector3(arr[0].GetSingle(), arr[1].GetSingle(), arr[2].GetSingle());
                    }
                    // Compose as Translation * Rotation * Scale (glTF TRS order)
                    node.LocalMatrix = Matrix4x4.CreateTranslation(t) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateScale(s);
                }
                list.Add(node);
            }
            return [..list];
        }

        // ========================= SKINS ========================================
        private GltfSkin[] ParseSkins(JsonElement root)
        {
            if (!root.TryGetProperty("skins", out var el)) return [];
            var list = new List<GltfSkin>();
            foreach (var s in el.EnumerateArray())
            {
                var skin = new GltfSkin();
                if (s.TryGetProperty("skeleton", out var sr)) skin.SkeletonRoot = sr.GetInt32();
                if (s.TryGetProperty("joints", out var jarr))
                {
                    var joints = new List<int>();
                    foreach (var ji in jarr.EnumerateArray()) joints.Add(ji.GetInt32());
                    skin.Joints = joints.ToArray();
                }
                if (s.TryGetProperty("inverseBindMatrices", out var ibm))
                {
                    int accIdx = ibm.GetInt32();
                    // Debug: print accessor + bufferview metadata for inverseBindMatrices
                    if (accIdx >= 0 && accIdx < _accs.Length)
                    {
                        var acc = _accs[accIdx];
                        var bv = _bvs[acc.BufView];
                        Console.WriteLine($"[ParseSkins] inverseBind accessor idx={accIdx} bufView={acc.BufView} byteOffset={acc.ByteOffset} count={acc.Count} compType={acc.CompType} type={acc.Type}");
                        Console.WriteLine($"[ParseSkins] bufferView off={bv.off} len={bv.len} stride={bv.stride}");
                        // print raw bytes of first matrix (up to 64 bytes)
                        try
                        {
                            var sp = Span(accIdx);
                            int bytes = Math.Min(sp.Length, 64);
                            var sb = new System.Text.StringBuilder();
                            for (int bi = 0; bi < bytes; bi++) sb.AppendFormat("{0:X2}", sp[bi]);
                            Console.WriteLine($"[ParseSkins] inverseBind rawBytes={sb}\n");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ParseSkins] failed to dump raw bytes: {ex.Message}");
                        }
                    }

                    skin.InverseBindMatrices = RMat4(accIdx);
                    // Debug: print first inverse bind matrix elements
                    if (skin.InverseBindMatrices != null && skin.InverseBindMatrices.Length > 0)
                    {
                        var m = skin.InverseBindMatrices[0];
                        Console.WriteLine($"[ParseSkins] InvBind[0] M11={m.M11:F6} M12={m.M12:F6} M13={m.M13:F6} M14={m.M14:F6}");
                        Console.WriteLine($"[ParseSkins] InvBind[0] M21={m.M21:F6} M22={m.M22:F6} M23={m.M23:F6} M24={m.M24:F6}");
                        Console.WriteLine($"[ParseSkins] InvBind[0] M31={m.M31:F6} M32={m.M32:F6} M33={m.M33:F6} M34={m.M34:F6}");
                        Console.WriteLine($"[ParseSkins] InvBind[0] M41={m.M41:F6} M42={m.M42:F6} M43={m.M43:F6} M44={m.M44:F6}");
                    }
                }
                list.Add(skin);
            }
            return [..list];
        }

        // ========================= ANIMATIONS ===================================
        private GltfAnimation[] ParseAnimations(JsonElement root)
        {
            if (!root.TryGetProperty("animations", out var el)) return [];
            var list = new List<GltfAnimation>();

            foreach (var animEl in el.EnumerateArray())
            {
                var anim = new GltfAnimation();
                if (animEl.TryGetProperty("name", out var n)) anim.Name = n.GetString() ?? "";

                // samplers
                var samplers = new List<GltfAnimationSampler>();
                if (animEl.TryGetProperty("samplers", out var samArr))
                {
                    foreach (var sEl in samArr.EnumerateArray())
                    {
                        int inputIdx = sEl.GetProperty("input").GetInt32();
                        int outputIdx = sEl.GetProperty("output").GetInt32();
                        string interp = sEl.TryGetProperty("interpolation", out var ip) ? ip.GetString() ?? "LINEAR" : "LINEAR";

                        var aIn = _accs[inputIdx];
                        var aOut = _accs[outputIdx];

                        var sampler = new GltfAnimationSampler
                        {
                            Input = RFloat(inputIdx),
                            Interpolation = interp
                        };

                        if (aOut.Type == "VEC3")
                        {
                            sampler.OutputStride = 3;
                            var vecs = RVec3(outputIdx);
                            sampler.Output = new float[vecs.Length * 3];
                            for (int i = 0; i < vecs.Length; i++)
                            {
                                sampler.Output[i * 3 + 0] = vecs[i].X;
                                sampler.Output[i * 3 + 1] = vecs[i].Y;
                                sampler.Output[i * 3 + 2] = vecs[i].Z;
                            }
                        }
                        else if (aOut.Type == "VEC4")
                        {
                            sampler.OutputStride = 4;
                            var vecs = RVec4(outputIdx);
                            sampler.Output = new float[vecs.Length * 4];
                            for (int i = 0; i < vecs.Length; i++)
                            {
                                sampler.Output[i * 4 + 0] = vecs[i].X;
                                sampler.Output[i * 4 + 1] = vecs[i].Y;
                                sampler.Output[i * 4 + 2] = vecs[i].Z;
                                sampler.Output[i * 4 + 3] = vecs[i].W;
                            }
                        }
                        else if (aOut.Type == "SCALAR")
                        {
                            sampler.OutputStride = 1;
                            var f = RFloat(outputIdx);
                            sampler.Output = f;
                        }
                        else
                        {
                            sampler.OutputStride = 0;
                            sampler.Output = [];
                        }

                        samplers.Add(sampler);
                    }
                }

                // channels
                var channels = new List<GltfAnimationChannel>();
                if (animEl.TryGetProperty("channels", out var chArr))
                {
                    foreach (var chEl in chArr.EnumerateArray())
                    {
                        int samplerIndex = chEl.GetProperty("sampler").GetInt32();
                        var target = chEl.GetProperty("target");
                        int nodeIdx = target.TryGetProperty("node", out var nidx) ? nidx.GetInt32() : -1;
                        string path = target.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";

                        channels.Add(new GltfAnimationChannel { SamplerIndex = samplerIndex, TargetNode = nodeIdx, Path = path });
                    }
                }

                anim.Samplers = samplers.ToArray();
                anim.Channels = channels.ToArray();

                // compute duration
                float maxT = 0f;
                foreach (var s in anim.Samplers)
                {
                    if (s.Input != null && s.Input.Length > 0)
                    {
                        float last = s.Input[^1];
                        if (last > maxT) maxT = last;
                    }
                }
                anim.Duration = maxT;

                list.Add(anim);
            }

            return [..list];
        }

        // ========================= BINARY READERS ===============================
        private Span<byte> Span(int accIdx)
        {
            var acc = _accs[accIdx];
            var (off, len, _) = _bvs[acc.BufView];
            return _bin.AsSpan(off + acc.ByteOffset, len - acc.ByteOffset);
        }

        private Vector3[] RVec3(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new Vector3[acc.Count];
            var bv = _bvs[acc.BufView];
            int step = bv.stride != 0 ? bv.stride : 12;
            for (int j = 0; j < r.Length; j++)
            { int o = j * step; r[j] = new Vector3(ToF(sp, o), ToF(sp, o + 4), ToF(sp, o + 8)); }
            return r;
        }

        private Vector2[] RVec2(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new Vector2[acc.Count];
            var bv = _bvs[acc.BufView];
            int step = bv.stride != 0 ? bv.stride : 8;
            for (int j = 0; j < r.Length; j++)
            { int o = j * step; r[j] = new Vector2(ToF(sp, o), ToF(sp, o + 4)); }
            return r;
        }

        private Vector4[] RVec4(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new Vector4[acc.Count];
            var bv = _bvs[acc.BufView];
            int step = bv.stride != 0 ? bv.stride : 16;
            for (int j = 0; j < r.Length; j++)
            { int o = j * step; r[j] = new Vector4(ToF(sp, o), ToF(sp, o + 4), ToF(sp, o + 8), ToF(sp, o + 12)); }
            return r;
        }

        private float[] RFloat(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new float[acc.Count];
            var bv = _bvs[acc.BufView];
            int step = bv.stride != 0 ? bv.stride : 4;
            for (int j = 0; j < acc.Count; j++)
            { int o = j * step; r[j] = ToF(sp, o); }
            return r;
        }

        private uint[] RIndices(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new uint[acc.Count];
            int ct = acc.CompType;
            for (int j = 0; j < acc.Count; j++)
                r[j] = ct == 5123 ? BitConverter.ToUInt16(sp[(j * 2)..])
                     : ct == 5125 ? BitConverter.ToUInt32(sp[(j * 4)..])
                     : (uint)sp[j];
            return r;
        }

        private Matrix4x4[] RMat4(int accIdx)
        {
            var acc = _accs[accIdx];
            var sp = Span(accIdx);
            var r = new Matrix4x4[acc.Count];
            var bv = _bvs[acc.BufView];
            int step = bv.stride != 0 ? bv.stride : 16 * 4;
            for (int j = 0; j < r.Length; j++)
            {
                int o = j * step;
                // glTF stores matrices in column-major order, read columns
                float m00 = ToF(sp, o + 0);   // col0
                float m10 = ToF(sp, o + 4);
                float m20 = ToF(sp, o + 8);
                float m30 = ToF(sp, o + 12);

                float m01 = ToF(sp, o + 16);  // col1
                float m11 = ToF(sp, o + 20);
                float m21 = ToF(sp, o + 24);
                float m31 = ToF(sp, o + 28);

                float m02 = ToF(sp, o + 32);  // col2
                float m12 = ToF(sp, o + 36);
                float m22 = ToF(sp, o + 40);
                float m32 = ToF(sp, o + 44);

                float m03 = ToF(sp, o + 48);  // col3
                float m13 = ToF(sp, o + 52);
                float m23 = ToF(sp, o + 56);
                float m33 = ToF(sp, o + 60);

                // Convert to row-major Matrix4x4
                r[j] = new Matrix4x4(
                    m00, m01, m02, m03,
                    m10, m11, m12, m13,
                    m20, m21, m22, m23,
                    m30, m31, m32, m33);
            }
            return r;
        }

        // Read joint indices accessor (JOINTS_0). supports UBYTE/USHORT/UINT
        private SkinnedVertex.BoneIndex4[] RJoints(int accIdx)
        {
            var acc = _accs[accIdx];
            var sp = Span(accIdx);
            var r = new SkinnedVertex.BoneIndex4[acc.Count];
            int ct = acc.CompType;
            var bv = _bvs[acc.BufView];
            int step;
            if (ct == 5121) step = bv.stride != 0 ? bv.stride : 4; // UBYTE VEC4
            else if (ct == 5123) step = bv.stride != 0 ? bv.stride : 8; // USHORT VEC4
            else step = bv.stride != 0 ? bv.stride : 16; // UINT VEC4

            for (int i = 0; i < acc.Count; i++)
            {
                int baseOff = i * step;
                if (ct == 5121) // UBYTE
                {
                    r[i] = new SkinnedVertex.BoneIndex4
                    {
                        X = sp[baseOff],
                        Y = sp[baseOff + 1],
                        Z = sp[baseOff + 2],
                        W = sp[baseOff + 3]
                    };
                }
                else if (ct == 5123) // USHORT
                {
                    r[i] = new SkinnedVertex.BoneIndex4
                    {
                        X = BitConverter.ToUInt16(sp.Slice(baseOff, 2)),
                        Y = BitConverter.ToUInt16(sp.Slice(baseOff + 2, 2)),
                        Z = BitConverter.ToUInt16(sp.Slice(baseOff + 4, 2)),
                        W = BitConverter.ToUInt16(sp.Slice(baseOff + 6, 2))
                    };
                }
                else // UINT
                {
                    r[i] = new SkinnedVertex.BoneIndex4
                    {
                        X = (int)BitConverter.ToUInt32(sp.Slice(baseOff, 4)),
                        Y = (int)BitConverter.ToUInt32(sp.Slice(baseOff + 4, 4)),
                        Z = (int)BitConverter.ToUInt32(sp.Slice(baseOff + 8, 4)),
                        W = (int)BitConverter.ToUInt32(sp.Slice(baseOff + 12, 4))
                    };
                }
            }
            return r;
        }

        private static float ToF(Span<byte> sp, int o) => BitConverter.ToSingle(sp[o..]);
        private static uint R32(byte[] b, int o) => BitConverter.ToUInt32(b, o);
    }
}
