using System.Reflection;
using System.Text.Json;

namespace DarkEngine3D_gl_csharp.Engine.Helpers;

/// <summary>
/// Binary object cache ("poor-man's protobuf") for large JSON assets — speeds up
/// loading by skipping the System.Text.Json PARSE on warm loads.
///
/// How it works: after a JSON file is written, the in-memory object graph is also
/// serialized into a compact binary sidecar <c>&lt;file&gt;.bin</c> next to it (in a
/// <c>.cache/</c> sub-folder). The next load tries the sidecar FIRST: header magic +
/// version + (length, FNV-1a content hash, mtime) stamp of the JSON — any mismatch
/// (JSON edited/re-saved elsewhere) silently falls back to the normal JSON parse.
///
/// SAFETY CONTRACT (fail-open by construction):
///   1. The JSON file remains the single source of truth — the cache is an
///      acceleration only. ANY problem reading/decoding the cache (missing, stale,
///      truncated, corrupted, unsupported member type) → return null → caller does
///      the normal JSON parse. Loading can NEVER break because of the cache.
///   2. Serialization is fully deterministic (no reflection results change between
///      write and read inside one exe build; member lists are cached per type).
///   3. Unsupported member types throw on the WRITE side → the cache file is simply
///      not produced (JSON-only behavior, exactly like before).
///
/// Wire format v1: [u32 magic "DBIN"][u8 version][i64 jsonLen][u64 jsonHash]
/// [i64 jsonMtimeTicks][payload: typed member tree] — payload written with
/// BinaryWriter in declared-type order (classes: cached public get+set properties
/// with [JsonIgnore] skipped; structs: public instance fields — covers Vector2/3/4,
/// ValueTuple; collections: List&lt;T&gt;/T[]/string-keyed Dictionary; enums as i32).
/// </summary>
public static class BinaryObjectCache
{
    private const uint Magic = 0x4E494244; // 'D''B''I''N' (little-endian read)
    private const byte Version = 1;

    private sealed record Members(FieldInfo[] Fields, PropertyInfo[] Props, bool IsClass);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Members> _members = new();

    private static bool _enabled = true;
    /// <summary>Global kill switch (settings can wire this later; ON by default).</summary>
    public static void SetEnabled(bool on) => _enabled = on;
    public static bool Enabled => _enabled;

    /// <summary>Telemetry of the LAST TryLoad call — true when the binary sidecar was
    /// actually consumed (for the "[BinCache] hit/miss" log lines).</summary>
    public static bool LastLoadWasBinary { get; private set; }

    // ═══════════════════════ Read ═══════════════════════

    /// <summary>Try to load the object graph from the binary sidecar of
    /// <paramref name="jsonPath"/>. Returns null when the cache is missing/stale/
    /// unreadable — the caller then parses the JSON as usual. NEVER throws.</summary>
    public static T? TryLoad<T>(string jsonPath) where T : class
    {
        LastLoadWasBinary = false;
        if (!_enabled) return null;
        try
        {
            if (!File.Exists(jsonPath)) return null;
            string cachePath = CachePath(jsonPath);
            if (!File.Exists(cachePath)) return null;
            var stamp = Stamp(jsonPath);
            if (stamp is null) return null;

            using var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt32() != Magic || br.ReadByte() != Version) return null;
            // Root type guard: a sidecar written for another root type (SceneAsset vs
            // SceneManifest share one file) must never be decoded as this type — the
            // member ORDER would mismatch and produce silent garbage.
            if (br.ReadString() != typeof(T).Name) return null;
            if (br.ReadInt64() != stamp.Value.len) return null;
            if (br.ReadUInt64() != stamp.Value.hash) return null;
            if (br.ReadInt64() != stamp.Value.ticks) return null;

            var result = ReadValue(br, typeof(T)) as T;
            if (result is null) return null;
            LastLoadWasBinary = true;
            return result;
        }
        catch (Exception ex)
        {
            // Corrupt/truncated/foreign cache → JSON fallback (status quo ante).
            Console.WriteLine($"[BinCache] {Path.GetFileName(jsonPath)}: cache invalid ({ex.GetType().Name}: {ex.Message}) — parsing JSON");
            return null;
        }
    }

    // ═══════════════════════ Write ═══════════════════════

    /// <summary>Write the binary sidecar for a JSON file that was JUST written with
    /// this exact object graph (stamp is computed from the JSON on disk, so a later
    /// JSON edit automatically invalidates the cache). Never throws; failures only
    /// mean the next load takes the JSON path.</summary>
    public static void TryWrite<T>(string jsonPath, T value) where T : class
    {
        if (!_enabled || value is null) return;
        try
        {
            if (!File.Exists(jsonPath)) return;
            var stamp = Stamp(jsonPath);
            if (stamp is null) return;

            string cachePath = CachePath(jsonPath);
            string? dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = cachePath + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Magic);
                bw.Write(Version);
                bw.Write(typeof(T).Name);
                bw.Write(stamp.Value.len);
                bw.Write(stamp.Value.hash);
                bw.Write(stamp.Value.ticks);
                WriteValue(bw, value, typeof(T));
            }
            File.Move(tmp, cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BinCache] write skipped for {Path.GetFileName(jsonPath)}: {ex.Message}");
        }
    }

    // ═══════════════════════ Stamped identity of the source JSON ═══════════════════════

    private static (long len, ulong hash, long ticks)? Stamp(string jsonPath)
    {
        try
        {
            var fi = new FileInfo(jsonPath);
            ulong hash = 14695981039346656037UL; // FNV-1a 64
            Span<byte> buf = stackalloc byte[8192];
            using var fs = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            int n;
            while ((n = fs.Read(buf)) > 0)
                for (int i = 0; i < n; i++) { hash ^= buf[i]; hash *= 1099511628211UL; }
            return (fi.Length, hash, fi.LastWriteTimeUtc.Ticks);
        }
        catch { return null; }
    }

    private static string CachePath(string jsonPath) =>
        Path.Combine(Path.GetDirectoryName(jsonPath) ?? ".", ".cache", Path.GetFileName(jsonPath) + ".bin");

    // ═══════════════════════ Typed value tree (declared-type driven) ═══════════════════════

    private const byte TagNull = 0, TagFalse = 1, TagTrue = 2, TagByte = 3, TagSByte = 4,
        TagShort = 5, TagUShort = 6, TagInt = 7, TagUInt = 8, TagLong = 9, TagULong = 10,
        TagFloat = 11, TagDouble = 12, TagDecimal = 13, TagChar = 14, TagString = 15,
        TagEnum = 16, TagObject = 17, TagStruct = 18, TagList = 19, TagArray = 20, TagDict = 21;

    private static Members GetMembers(Type t)
    {
        if (_members.TryGetValue(t, out var m)) return m;
        bool isClass = !t.IsValueType;
        var props = isClass
            ? t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                            && p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() == null)
                .OrderBy(p => p.Name, StringComparer.Ordinal).ToArray()
            : Array.Empty<PropertyInfo>();
        var fields = !isClass
            ? t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly && !f.IsLiteral)
                .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray()
            : Array.Empty<FieldInfo>();
        m = new Members(fields, props, isClass);
        _members[t] = m;
        return m;
    }

    private static void WriteValue(BinaryWriter w, object? value, Type declared)
    {
        // Nullable<T> unwrap.
        var underlying = Nullable.GetUnderlyingType(declared);
        if (underlying != null)
        {
            if (value is null) { w.Write(TagNull); return; }
            WriteValue(w, value, underlying);
            return;
        }

        switch (value)
        {
            case null when !declared.IsValueType: w.Write(TagNull); return;
            case null: w.Write(TagNull); return;
            case bool b: w.Write(b ? TagTrue : TagFalse); return;
            case byte v: w.Write(TagByte); w.Write(v); return;
            case sbyte v: w.Write(TagSByte); w.Write(v); return;
            case short v: w.Write(TagShort); w.Write(v); return;
            case ushort v: w.Write(TagUShort); w.Write(v); return;
            case int v: w.Write(TagInt); w.Write(v); return;
            case uint v: w.Write(TagUInt); w.Write(v); return;
            case long v: w.Write(TagLong); w.Write(v); return;
            case ulong v: w.Write(TagULong); w.Write(v); return;
            case float v: w.Write(TagFloat); w.Write(v); return;
            case double v: w.Write(TagDouble); w.Write(v); return;
            case decimal v: w.Write(TagDecimal); w.Write(v); return;
            case char v: w.Write(TagChar); w.Write(v); return;
            case string s: w.Write(TagString); w.Write(s); return;
        }

        if (declared.IsEnum) { w.Write(TagEnum); w.Write(Convert.ToInt32(value)); return; }

        // Collections first (string is handled above).
        if (value is System.Collections.IDictionary dict)
        {
            if (dict.Keys.Cast<object?>().Any(k => k is not string))
                throw new NotSupportedException($"non-string dict keys on {declared.Name}");
            w.Write(TagDict);
            w.Write(dict.Count);
            foreach (System.Collections.DictionaryEntry e in dict)
            {
                w.Write((string)e.Key!);
                WriteValue(w, e.Value, declared.GetGenericArguments()[1]);
            }
            return;
        }
        if (value is System.Collections.IEnumerable en and not string)
        {
            var elemType = declared.IsArray ? declared.GetElementType()! : declared.GetGenericArguments()[0];
            var items = en.Cast<object?>().ToList();
            w.Write(declared.IsArray ? TagArray : TagList);
            w.Write(items.Count);
            foreach (var it in items) WriteValue(w, it, elemType);
            return;
        }

        var members = GetMembers(declared);
        if (members.IsClass)
        {
            w.Write(TagObject);
            foreach (var p in members.Props)
                WriteValue(w, p.GetValue(value), p.PropertyType);
        }
        else
        {
            // Struct: public instance fields (Vector2/3/4 X/Y/Z, ValueTuple Item1..).
            // A struct with NO public fields would round-trip to default(T) — silent
            // data loss is NEVER acceptable, so refuse to cache that graph instead.
            if (members.Fields.Length == 0)
                throw new NotSupportedException($"struct {declared.Name} has no public fields");
            w.Write(TagStruct);
            foreach (var f in members.Fields)
                WriteValue(w, f.GetValue(value), f.FieldType);
        }
    }

    private static object? ReadValue(BinaryReader r, Type declared)
    {
        var underlying = Nullable.GetUnderlyingType(declared);
        if (underlying != null)
        {
            // Nullable<T>: a written value NEVER starts with TagNull (tags 1..21), so
            // a leading TagNull byte unambiguously means null. Position-restore (not
            // PeekChar — that is decoder-dependent) keeps the stream math exact.
            long pos = r.BaseStream.Position;
            byte tag = r.ReadByte();
            if (tag == TagNull) return null;
            r.BaseStream.Position = pos; // not null → re-read as the value
            return ReadValue(r, underlying);
        }

        byte t = r.ReadByte();
        switch (t)
        {
            case TagNull: return null;
            case TagFalse: return false;
            case TagTrue: return true;
            case TagByte: return r.ReadByte();
            case TagSByte: return r.ReadSByte();
            case TagShort: return r.ReadInt16();
            case TagUShort: return r.ReadUInt16();
            case TagInt: return r.ReadInt32();
            case TagUInt: return r.ReadUInt32();
            case TagLong: return r.ReadInt64();
            case TagULong: return r.ReadUInt64();
            case TagFloat: return r.ReadSingle();
            case TagDouble: return r.ReadDouble();
            case TagDecimal: return r.ReadDecimal();
            case TagChar: return r.ReadChar();
            case TagString: return r.ReadString();
            case TagEnum: return Enum.ToObject(declared.IsEnum ? declared : typeof(int), r.ReadInt32());
            case TagDict:
            {
                var vType = declared.GetGenericArguments()[1];
                var dict = (System.Collections.IDictionary)Activator.CreateInstance(declared)!;
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    string key = r.ReadString();
                    dict[key] = ReadValue(r, vType);
                }
                return dict;
            }
            case TagList or TagArray:
            {
                var elemType = declared.IsArray ? declared.GetElementType()! : declared.GetGenericArguments()[0];
                int n = r.ReadInt32();
                var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elemType))!;
                for (int i = 0; i < n; i++) list.Add(ReadValue(r, elemType));
                if (t == TagArray && declared.IsArray)
                {
                    var arr = Array.CreateInstance(elemType, n);
                    list.CopyTo(arr, 0);
                    return arr;
                }
                // Declared type may be List<T>, T[] (handled above), or any
                // IEnumerable<T> with an (IEnumerable<T>) ctor (HashSet/Queue/Stack/…).
                if (declared.IsAssignableFrom(list.GetType())) return list;
                try { return Activator.CreateInstance(declared, list)!; }
                catch { throw new InvalidDataException($"cannot materialize {declared.Name}"); }
            }
            case TagObject:
            {
                var members = GetMembers(declared);
                var obj = Activator.CreateInstance(declared, nonPublic: true)
                          ?? throw new InvalidOperationException($"no ctor for {declared.Name}");
                foreach (var p in members.Props)
                    p.SetValue(obj, ReadValue(r, p.PropertyType));
                return obj;
            }
            case TagStruct:
            {
                var members = GetMembers(declared);
                var obj = Activator.CreateInstance(declared)!;
                foreach (var f in members.Fields)
                    f.SetValue(obj, ReadValue(r, f.FieldType));
                return obj;
            }
            default:
                throw new InvalidDataException($"unknown tag {t}");
        }
    }
}
