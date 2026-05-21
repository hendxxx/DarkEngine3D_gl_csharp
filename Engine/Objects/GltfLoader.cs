using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using static DarkEngine3D_gl_csharp.Engine.Objects.SkinnedVertex;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
        public int Parent = -1; // filled during parse
        public int Skin = -1;
    }

    public class GltfAnimationSampler
    {
        public float[] Input = [];     // times
        public float[] Output = [];    // flattened output values
        public int OutputStride = 0;   // components per key (3 = vec3, 4 = quat)
        public string Interpolation = "LINEAR";
    }

    public class GltfAnimationChannel
    {
        public int SamplerIndex;
        public int TargetNode;
        public string Path = ""; // "translation" | "rotation" | "scale"
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
        public int[] Joints = [];                 // index node tulang
        public Matrix4x4[] InverseBindMatrices = [];
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
            data.Skins = ParseSkins(root); 
            
            data.Nodes = ParseNodes(root);
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
                    var texCoord = attrs.TryGetProperty("TEXCOORD_0", out var uEl) ? RVec2(uEl.GetInt32()) : new Vector2[positions.Length];
                    var joints = attrs.TryGetProperty("JOINTS_0", out var jointsProp)
                        ? ReadJoints(jointsProp.GetInt32(), _bin)
                        : new Vector4[positions.Length];

                    var weights = attrs.TryGetProperty("WEIGHTS_0", out var weightsProp)
                        ? ReadWeights(weightsProp.GetInt32(), _bin)
                        : new Vector4[positions.Length];

                    for (int i = 0; i < weights.Length; i++)
                    {
                        float sum =
                            weights[i].X +
                            weights[i].Y +
                            weights[i].Z +
                            weights[i].W;

                        if (sum > 0.00001f)
                            weights[i] /= sum;
                        else
                            weights[i] = new Vector4(1, 0, 0, 0); // fallback
                    } 

                    var verts = new SkinnedVertex[positions.Length];
                    for (int i = 0; i < positions.Length; i++)
                    {
                        verts[i].Position = positions[i];
                        verts[i].Normal = normals[i];
                        verts[i].TexCoord = texCoord[i];
                        verts[i].BoneWeights = weights[i];

                        verts[i].BoneIds.X = (int)joints[i].X;
                        verts[i].BoneIds.Y = (int)joints[i].Y;
                        verts[i].BoneIds.Z = (int)joints[i].Z;
                        verts[i].BoneIds.W = (int)joints[i].W;
                    }


                    // Tambahkan debug print di sini
                    for (int i = 0; i < Math.Min(20, verts.Length); i++)
                    {
                        Console.WriteLine(
                            $"v{i} BoneIds = {verts[i].BoneIds.X}, {verts[i].BoneIds.Y}, {verts[i].BoneIds.Z}, {verts[i].BoneIds.W}"
                        );
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

        private Vector4[] ReadJoints(int accessorIndex, byte[] bin)
        {
            var acc = _accs[accessorIndex];
            var view = _bvs[acc.BufView];

            int count = acc.Count;
            Vector4[] result = new Vector4[count];

            int stride = view.stride > 0
                ? view.stride
                : (acc.CompType == 5121 ? 4 : 8);

            int baseOffset = view.off + acc.ByteOffset;

            for (int i = 0; i < count; i++)
            {
                int pos = baseOffset + i * stride;

                if (acc.CompType == 5121) // UNSIGNED_BYTE
                {
                    result[i] = new Vector4(
                        bin[pos + 0],
                        bin[pos + 1],
                        bin[pos + 2],
                        bin[pos + 3]
                    );
                }
                else if (acc.CompType == 5123) // UNSIGNED_SHORT
                {
                    result[i] = new Vector4(
                        BitConverter.ToUInt16(bin, pos + 0),
                        BitConverter.ToUInt16(bin, pos + 2),
                        BitConverter.ToUInt16(bin, pos + 4),
                        BitConverter.ToUInt16(bin, pos + 6)
                    );
                }
                else
                {
                    throw new Exception("Unsupported JOINTS_0 componentType");
                }
            }

            return result;
        }

        private Vector4[] ReadWeights(int accessorIndex, byte[] bin)
        {
            var acc = _accs[accessorIndex];
            var view = _bvs[acc.BufView];

            int count = acc.Count;
            Vector4[] result = new Vector4[count];

            int stride = view.stride > 0
             ? view.stride
             : acc.CompType switch
             {
                 5126 => 16, // FLOAT
                 5121 => 4,  // UBYTE
                 5123 => 8,  // USHORT
                 _ => throw new Exception($"Unsupported WEIGHTS_0 componentType: {acc.CompType}")
             };

            int baseOffset = view.off + acc.ByteOffset;

            for (int i = 0; i < count; i++)
            {
                int pos = baseOffset + i * stride;

                if (acc.CompType == 5126) // FLOAT
                {
                    result[i] = new Vector4(
                        BitConverter.ToSingle(bin, pos + 0),
                        BitConverter.ToSingle(bin, pos + 4),
                        BitConverter.ToSingle(bin, pos + 8),
                        BitConverter.ToSingle(bin, pos + 12)
                    );  
                }
                else if (acc.CompType == 5121) // UNSIGNED_BYTE normalized
                {
                    result[i] = new Vector4(
                        bin[pos + 0] / 255f,
                        bin[pos + 1] / 255f,
                        bin[pos + 2] / 255f,
                        bin[pos + 3] / 255f
                    );
                }
                else // UNSIGNED_SHORT normalized
                {
                    result[i] = new Vector4(
                        BitConverter.ToUInt16(bin, pos + 0) / 65535f,
                        BitConverter.ToUInt16(bin, pos + 2) / 65535f,
                        BitConverter.ToUInt16(bin, pos + 4) / 65535f,
                        BitConverter.ToUInt16(bin, pos + 6) / 65535f
                    );
                }
            }

            // normalize safety
            for (int i = 0; i < result.Length; i++)
            {
                float sum = result[i].X + result[i].Y + result[i].Z + result[i].W;
                if (sum > 0.00001f)
                    result[i] /= sum;
                else
                    result[i] = new Vector4(1, 0, 0, 0);
            }

            return result;
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

                // ============================
                // PARSE NODE LOCAL TRANSFORM
                // ============================
                if (nd.TryGetProperty("matrix", out var matProp) && matProp.ValueKind == JsonValueKind.Array)
                {
                    // GLTF matrix = COLUMN MAJOR
                    float[] mat = new float[16];
                    int k = 0;
                    foreach (var v in matProp.EnumerateArray())
                        mat[k++] = v.GetSingle();

                    // Convert to C# row-major
                    var matrix = new Matrix4x4(
                        mat[0], mat[1], mat[2], mat[3],
                        mat[4], mat[5], mat[6], mat[7],
                        mat[8], mat[9], mat[10], mat[11],
                        mat[12], mat[13], mat[14], mat[15]
                    );

                    node.LocalMatrix = Matrix4x4.Transpose(matrix);   // WAJIB
                }
                else
                {
                    // TRS
                    Vector3 t = Vector3.Zero;
                    Quaternion r = Quaternion.Identity;
                    Vector3 s = Vector3.One;

                    if (nd.TryGetProperty("translation", out var tProp))
                    {
                        var arr = tProp.EnumerateArray().ToArray();
                        t = new Vector3(arr[0].GetSingle(), arr[1].GetSingle(), arr[2].GetSingle());
                    }

                    if (nd.TryGetProperty("rotation", out var rProp))
                    {
                        var arr = rProp.EnumerateArray().ToArray();
                        r = new Quaternion(arr[0].GetSingle(), arr[1].GetSingle(), arr[2].GetSingle(), arr[3].GetSingle());
                        r = Quaternion.Normalize(r);   // WAJIB
                    }

                    if (nd.TryGetProperty("scale", out var sProp))
                    {
                        var arr = sProp.EnumerateArray().ToArray();
                        s = new Vector3(arr[0].GetSingle(), arr[1].GetSingle(), arr[2].GetSingle());
                    }

                    // GLTF ORDER: T * R * S  (WAJIB)
                    node.LocalMatrix =
                        Matrix4x4.CreateScale(s);

                    node.LocalMatrix =
                        Matrix4x4.CreateFromQuaternion(r) * node.LocalMatrix;

                    node.LocalMatrix =
                        Matrix4x4.CreateTranslation(t) * node.LocalMatrix;
                }


                if (nd.TryGetProperty("skin", out var skinProp))
                    node.Skin = skinProp.GetInt32();
                else
                    node.Skin = -1;

                list.Add(node);
            }
            return [..list];
        }
        private Matrix4x4[] ReadMatrix4x4Accessor(int accessorIndex, byte[] bin)
        {
            var acc = _accs[accessorIndex];
            var view = _bvs[acc.BufView];

            int count = acc.Count;
            Matrix4x4[] result = new Matrix4x4[count];

            int stride = view.stride > 0 ? view.stride : 64; // 16 float * 4 bytes
            int offset = view.off + acc.ByteOffset;

            for (int i = 0; i < count; i++)
            {
                int pos = offset + i * stride;

                var m = new Matrix4x4(
                    BitConverter.ToSingle(bin, pos + 0),
                    BitConverter.ToSingle(bin, pos + 4),
                    BitConverter.ToSingle(bin, pos + 8),
                    BitConverter.ToSingle(bin, pos + 12),

                    BitConverter.ToSingle(bin, pos + 16),
                    BitConverter.ToSingle(bin, pos + 20),
                    BitConverter.ToSingle(bin, pos + 24),
                    BitConverter.ToSingle(bin, pos + 28),

                    BitConverter.ToSingle(bin, pos + 32),
                    BitConverter.ToSingle(bin, pos + 36),
                    BitConverter.ToSingle(bin, pos + 40),
                    BitConverter.ToSingle(bin, pos + 44),

                    BitConverter.ToSingle(bin, pos + 48),
                    BitConverter.ToSingle(bin, pos + 52),
                    BitConverter.ToSingle(bin, pos + 56),
                    BitConverter.ToSingle(bin, pos + 60)
                );

                // ⭐ WAJIB
                m = Matrix4x4.Transpose(m);
                result[i] = m;
            }

            return result;
        }

        private GltfSkin[] ParseSkins(JsonElement root )
        {
            if (!root.TryGetProperty("skins", out var el))
                return [];

            var list = new List<GltfSkin>();

            foreach (var skinJson in el.EnumerateArray())
            {
                var skin = new GltfSkin();

                // -------------------------
                // JOINTS
                // -------------------------
                if (skinJson.TryGetProperty("joints", out var jointsProp))
                {
                    int count = jointsProp.GetArrayLength();
                    skin.Joints = new int[count];

                    int idx = 0;
                    foreach (var j in jointsProp.EnumerateArray())
                    {
                        skin.Joints[idx++] = j.GetInt32();
                    }
                }
                else
                {
                    skin.Joints = [];
                } 

                // -------------------------
                // INVERSE BIND MATRICES
                // -------------------------
                if (skinJson.TryGetProperty("inverseBindMatrices", out var ibmProp))
                {
                    int accessorIndex = ibmProp.GetInt32();
                    skin.InverseBindMatrices = ReadMatrix4x4Accessor(accessorIndex, _bin);
                }
                else
                {
                    // default: identity
                    skin.InverseBindMatrices = new Matrix4x4[skin.Joints.Length];
                    for (int i = 0; i < skin.InverseBindMatrices.Length; i++)
                        skin.InverseBindMatrices[i] = Matrix4x4.Identity;
                } 
                list.Add(skin);
            }

            return [.. list];
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
                            // unsupported output type => skip
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
            for (int j = 0; j < r.Length; j++)
            { int o = j * 12; r[j] = new Vector3(ToF(sp, o), ToF(sp, o + 4), ToF(sp, o + 8)); }
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

        private float[] RFloat(int i)
        {
            var acc = _accs[i]; var sp = Span(i);
            var r = new float[acc.Count];
            for (int j = 0; j < r.Length; j++)
            { int o = j * 4; r[j] = ToF(sp, o); }
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

        private static float ToF(Span<byte> sp, int o) => BitConverter.ToSingle(sp[o..]);
        private static uint R32(byte[] b, int o) => BitConverter.ToUInt32(b, o);
    }
}
