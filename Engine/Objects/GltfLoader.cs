using System.Numerics;
using System.Text;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    // ===========================================================================
    //  glTF 2.0 Data Model — static mesh only (no animation, no skinning)
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

    public class GltfData
    {
        public GltfMeshData[] Meshes = [];
        public GltfMaterial[] Materials = [];
        public GltfImage[] Images = [];
        public GltfTexture[] Textures = [];
    }

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

                    var verts = new SkinnedVertex[positions.Length];
                    for (int i = 0; i < positions.Length; i++)
                        verts[i] = new SkinnedVertex(positions[i], normals[i], uvs[i]);

                    uint[] indices = prim.TryGetProperty("indices", out var idxEl)
                        ? RIndices(idxEl.GetInt32())
                        : [];

                    int matIdx = prim.TryGetProperty("material", out var matEl) ? matEl.GetInt32() : -1;

                    list.Add(new GltfMeshData { Name = meshName, Vertices = verts, Indices = indices, MaterialIndex = matIdx });
                }
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
