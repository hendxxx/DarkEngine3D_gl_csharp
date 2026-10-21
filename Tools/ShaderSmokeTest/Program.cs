using System.Runtime.InteropServices;

// ══════════════════════════════════════════════════════════════════════
// SHADER SMOKE TEST — compiles the production shaders on a REAL GL
// context (hidden GLFW window, opengl32 + wglGetProcAddress — the same
// path the engine uses). A GLSL file that never compiled only shows up
// as a "[SHADER COMPILE ERROR]" console line + program == 0 at runtime
// (silent feature death, no build error) — this harness makes that a
// failing exit code. Run from the REPOSITORY ROOT so "Artifacts/..."
// paths resolve:  dotnet run --project Tools/ShaderSmokeTest
// ══════════════════════════════════════════════════════════════════════

// ── GLFW (glfw3.dll resolves from the harness exe directory) ──
[DllImport("glfw3.dll")] static extern int glfwInit();
[DllImport("glfw3.dll")] static extern void glfwTerminate();
[DllImport("glfw3.dll")] static extern void glfwDefaultWindowHints();
[DllImport("glfw3.dll")] static extern void glfwWindowHint(int hint, int value);
[DllImport("glfw3.dll")] static extern nint glfwCreateWindow(int w, int h, string title, nint monitor, nint share);
[DllImport("glfw3.dll")] static extern void glfwMakeContextCurrent(nint window);
[DllImport("glfw3.dll")] static extern void glfwDestroyWindow(nint window);

// ── GL loader (opengl32): wglGetProcAddress for 3.3 entries, direct export
//    for GL 1.1 functions (wglGetProcAddress does NOT serve those). ──
[DllImport("opengl32.dll", EntryPoint = "wglGetProcAddress")] static extern nint wglGetProc(string name);
[DllImport("opengl32.dll")] static unsafe extern void glGetIntegerv(int pname, int* data);
[DllImport("opengl32.dll")] static unsafe extern void glGenTextures(int n, uint* textures);
[DllImport("opengl32.dll")] static extern void glBindTexture(uint target, uint texture);
[DllImport("opengl32.dll")] static unsafe extern void glTexImage2D(uint target, int level, int internalFormat,
    int width, int height, int border, uint format, uint type, void* data);
[DllImport("opengl32.dll")] static extern void glTexParameteri(uint target, uint pname, int param);
[DllImport("opengl32.dll")] static extern void glDrawArrays(uint mode, int first, int count);
[DllImport("opengl32.dll")] static unsafe extern void glReadPixels(int x, int y, int w, int h, uint format, uint type, void* data);
[DllImport("opengl32.dll")] static extern void glViewport(int x, int y, int w, int h);
[DllImport("opengl32.dll")] static extern void glClearColor(float r, float g, float b, float a);
[DllImport("opengl32.dll")] static extern void glClear(uint mask);
[DllImport("opengl32.dll")] static extern void glFinish();

const int GLFW_VISIBLE = 0x00020004;
const int GLFW_CONTEXT_VERSION_MAJOR = 0x00022002;
const int GLFW_CONTEXT_VERSION_MINOR = 0x00022003;

// ── GL delegate instances (assigned after the context is current) ──
glCreateShaderDel glCreateShader;
glShaderSourceDel glShaderSource;
glCompileShaderDel glCompileShader;
glGetShaderivDel glGetShaderiv;
glGetShaderInfoLogDel glGetShaderInfoLog;
glCreateProgramDel glCreateProgram;
glAttachShaderDel glAttachShader;
glLinkProgramDel glLinkProgram;
glGetProgramivDel glGetProgramiv;
glGetProgramInfoLogDel glGetProgramInfoLog;
glDeleteShaderDel glDeleteShader;
glGetUniformLocationDel glGetUniformLocation;
glGenVertexArraysDel glGenVertexArrays;
glBindVertexArrayDel glBindVertexArray;
glUseProgramDel glUseProgram;
glActiveTextureDel glActiveTexture;
glUniform1iDel glUniform1i;
glUniform1fDel glUniform1f;
glUniform2fDel glUniform2f;
glUniform3fDel glUniform3f;
glUniform4fDel glUniform4f;

static T GetFn<T>(string name) where T : Delegate
{
    nint addr = wglGetProc(name);
    if (addr == nint.Zero)
        throw new InvalidOperationException($"wglGetProcAddress('{name}') returned NULL — wrong GL context version?");
    return Marshal.GetDelegateForFunctionPointer<T>(addr);
}

// ── Compile a vertex+fragment pair; returns the linked program (0 on failure). ──
unsafe uint Compile(string vertexPath, string fragmentPath, out string error)
    => CompilePrefixed(vertexPath, fragmentPath, null, out error);

// Variant support: inject a #define prefix after the fragment #version line
// (mirrors ShaderHelpers.LoadShader's fragmentPrefix parameter).
unsafe uint CompilePrefixed(string vertexPath, string fragmentPath, string? fragmentPrefix, out string error)
    => CompileFromSources(File.ReadAllText(vertexPath), File.ReadAllText(fragmentPath), fragmentPrefix, out error);

unsafe uint CompileFromSources(string vsSrc, string fsSrc, string? fragmentPrefix, out string error)
{
    error = "";
    uint vs = glCreateShader(0x8B31); // GL_VERTEX_SHADER
    uint fs = glCreateShader(0x8B30); // GL_FRAGMENT_SHADER
    if (!string.IsNullOrEmpty(fragmentPrefix))
    {
        int vIdx = fsSrc.IndexOf("#version");
        int lineEnd = vIdx >= 0 ? fsSrc.IndexOf('\n', vIdx) : -1;
        fsSrc = lineEnd >= 0
            ? fsSrc.Substring(0, lineEnd + 1) + fragmentPrefix + fsSrc.Substring(lineEnd + 1)
            : fsSrc + "\n" + fragmentPrefix;
    }
    string vsErr = CompileOne(vs, vsSrc);
    string fsErr = CompileOne(fs, fsSrc);
    if (vsErr.Length > 0 || fsErr.Length > 0)
    {
        error = vsErr + fsErr;
        return 0;
    }
    uint prog = glCreateProgram();
    glAttachShader(prog, vs);
    glAttachShader(prog, fs);
    glLinkProgram(prog);
    int ok = 0;
    glGetProgramiv(prog, 0x8B82, &ok); // GL_LINK_STATUS
    if (ok == 0)
    {
        var buf = new byte[8192];
        fixed (byte* p = buf)
        {
            glGetProgramInfoLog(prog, buf.Length, null, p);
            error = "LINK: " + Marshal.PtrToStringAnsi((nint)p);
        }
    }
    glDeleteShader(vs);
    glDeleteShader(fs);
    return ok != 0 ? prog : 0;
}

unsafe string CompileOne(uint shader, string source)
{
    byte[] src = System.Text.Encoding.UTF8.GetBytes(source);
    fixed (byte* p = src)
    {
        byte* ptr = p;
        glShaderSource(shader, 1, &ptr, null);
        glCompileShader(shader);
        int ok = 0;
        glGetShaderiv(shader, 0x8B81, &ok); // GL_COMPILE_STATUS
        if (ok == 0)
        {
            var buf = new byte[8192];
            fixed (byte* q = buf)
            {
                glGetShaderInfoLog(shader, buf.Length, null, q);
                return "COMPILE: " + Marshal.PtrToStringAnsi((nint)q);
            }
        }
    }
    return "";
}

// ── Behavioral render check — slope based texturing (Rastertek t14) ──
// Renders ONE pixel through the REAL splat fragment shader with a known surface:
//   Normal = normalize(0, 0.6, 0.8)  →  slope = 1 − 0.6 = 0.40  (reference units)
//   weight map = 100% layer 0; layer-0 albedo = GREEN, layer-1 = RED, layer-2 = BLUE
//   tiers OFF                        → GREEN
//   tier 1 · layer 1 · 18°..58°      → RED-dominant (the range covers slope 0.40)
//   tier 1 · 70°..89°                → GREEN again (0.40 sits below the range)
//   tier 1 gentle (red) + tier 2 steep (blue, ~93% at 0.40) → BLUE — the steeper
//   tier is applied LAST and takes the share the gentle tier had, which is what
//   makes grass → dirt → rock tiering real instead of one texture everywhere.
// Proves the u_slopeRule* table reaches the blend, the ranges gate it, and tiers
// compose in steepness order.
unsafe int SlopeRenderCheck()
{
    static float Deg(float d) => 1f - MathF.Cos(d * MathF.PI / 180f);
    string testVs = @"#version 400 core
out vec3 FragPos;
out vec3 Normal;
out vec3 ObjColor;
out vec2 TexCoord;
out float viewDepth;
void main()
{
    vec2 p = vec2((gl_VertexID == 1) ? 3.0 : -1.0, (gl_VertexID == 2) ? 3.0 : -1.0);
    gl_Position = vec4(p, 0.0, 1.0);
    FragPos = vec3(0.0);
    Normal = normalize(vec3(0.0, 0.6, 0.8));
    ObjColor = vec3(1.0);
    TexCoord = vec2(0.5, 0.5);
    viewDepth = 1.0;
}";
    uint prog = CompileFromSources(testVs, File.ReadAllText("Artifacts/shaders/objectPbr_fragment.glsl"),
        "#define TERRAIN_SPLAT 1\n", out string err);
    if (prog == 0)
    {
        Console.WriteLine("[SMOKE] FAIL: slope render test program\n" + err);
        return 1;
    }

    uint vao = 0;
    glGenVertexArrays(1, &vao);
    glBindVertexArray(vao);

    uint Tex(byte r, byte g, byte b, byte a)
    {
        uint t = 0;
        glGenTextures(1, &t);
        glBindTexture(0x0DE1, t);
        byte* px = stackalloc byte[4];
        px[0] = r; px[1] = g; px[2] = b; px[3] = a;
        glTexImage2D(0x0DE1, 0, 0x8058 /*RGBA8*/, 1, 1, 0, 0x1908, 0x1401, px);
        glTexParameteri(0x0DE1, 0x2801 /*MIN_FILTER*/, 0x2600 /*NEAREST*/);
        glTexParameteri(0x0DE1, 0x2800 /*MAG_FILTER*/, 0x2600);
        glTexParameteri(0x0DE1, 0x2802 /*WRAP_S*/, 0x812F /*CLAMP_TO_EDGE*/);
        glTexParameteri(0x0DE1, 0x2803 /*WRAP_T*/, 0x812F);
        return t;
    }
    uint baseTex = Tex(0, 255, 0, 255);    // layer 0 albedo = GREEN
    uint slopeTex = Tex(255, 0, 0, 255);   // layer 1 albedo = RED
    uint steepTex = Tex(40, 90, 255, 255); // layer 2 albedo = BLUE (steep tier)
    uint weightTex = Tex(255, 0, 0, 0);    // 100% layer 0 base weights

    glActiveTexture(0x84C0 + 0); glBindTexture(0x0DE1, baseTex);
    glActiveTexture(0x84C0 + 1); glBindTexture(0x0DE1, weightTex);
    glActiveTexture(0x84C0 + 2); glBindTexture(0x0DE1, baseTex);
    glActiveTexture(0x84C0 + 3); glBindTexture(0x0DE1, slopeTex);
    glActiveTexture(0x84C0 + 4); glBindTexture(0x0DE1, steepTex);
    glActiveTexture(0x84C0 + 0);

    glUseProgram(prog);
    void S1i(string n, int v) { int l = glGetUniformLocation(prog, n); if (l >= 0) glUniform1i(l, v); }
    void S1f(string n, float v) { int l = glGetUniformLocation(prog, n); if (l >= 0) glUniform1f(l, v); }
    void S2f(string n, float x, float y) { int l = glGetUniformLocation(prog, n); if (l >= 0) glUniform2f(l, x, y); }
    void S3f(string n, float x, float y, float z) { int l = glGetUniformLocation(prog, n); if (l >= 0) glUniform3f(l, x, y, z); }
    void S4f(string n, float x, float y, float z, float w) { int l = glGetUniformLocation(prog, n); if (l >= 0) glUniform4f(l, x, y, z, w); }

    S1i("albedoMap", 0);
    S1i("useAlbedo", 1); S1i("useNormal", 0); S1i("useMetallic", 0); S1i("useRoughness", 0);
    S1i("useAo", 0); S1i("useHeight", 0); S1i("useEmission", 0);
    S3f("u_albedoTuning", 1f, 1f, 1f); S3f("u_normalTuning", 1f, 0f, 0f);
    S3f("u_metallicTuning", 0f, 0f, 0f); S3f("u_roughnessTuning", 1f, 0f, 0f);
    S3f("u_aoTuning", 1f, 0f, 0f);
    S3f("lightColor", 1f, 1f, 1f); S3f("sunDir", 0f, 1f, 0f);
    S3f("viewPos", 0f, 0f, 5f); S3f("fogColor", 0f, 0f, 0f);
    S1i("useFog", 0); S3f("shadowDir", 0f, 1f, 0f);
    for (int i = 0; i < 3; i++) S1f($"cascadeEnds[{i}]", 0f);
    for (int i = 0; i < 7; i++) S2f($"u_uvScale[{i}]", 1f, 1f);
    S1i("u_splatActive", 1);
    S1i("u_splatWeights", 1);
    S1i("u_splatAlbedo[0]", 2); S1i("u_splatAlbedo[1]", 3); S1i("u_splatAlbedo[2]", 4);
    S4f("u_splatHasAlbedo", 1f, 1f, 1f, 0f);
    S4f("u_splatHasNormal", 0f, 0f, 0f, 0f);
    S4f("u_splatHasMetal", 0f, 0f, 0f, 0f);
    S4f("u_splatHasRough", 0f, 0f, 0f, 0f);
    S4f("u_splatHasAo", 0f, 0f, 0f, 0f);
    S4f("u_splatHasHeight", 0f, 0f, 0f, 0f);
    for (int i = 0; i < 4; i++) S3f($"u_splatTint[{i}]", 1f, 1f, 1f);
    for (int i = 0; i < 4; i++) S1f($"u_splatLayerTiling[{i}]", 1f);
    S1f("u_splatNormalStr", 1f); S1f("u_splatDetail", 0f);
    S1f("u_randomTiling", 0f); S1f("parallaxScale", 0f);
    S1i("u_slopeActive", 0); S1i("u_slopeRuleCount", 0);
    S4f("u_slopeRuleLayer", 1f, 2f, 3f, 3f);
    S4f("u_slopeRuleStart", 0.18f, 0.40f, 0.62f, 1f);
    S4f("u_slopeRuleEnd", 0.58f, 0.70f, 0.85f, 1f);
    S4f("u_slopeRuleTiling", 1f, 1f, 1f, 1f);
    S1i("u_slopeOverride", 0); S1f("u_slopeFloor", 0.25f); S1i("u_slopeDebug", 0);

    glViewport(0, 0, 16, 16);
    glClearColor(0f, 0f, 1f, 1f);   // BLUE = nothing drawn
    int fails = 0;
    (string Label, int Count, float S0, float E0, float S1, float E1, string Want)[] cases =
    [
        ("all tiers OFF", 0, 0.18f, 0.58f, 0f, 1f, "green"),
        ("tier 1 · layer 1 · 18°..58°", 1, Deg(18f), Deg(58f), 0f, 1f, "red"),
        ("tier 1 · 70°..89° (above the surface)", 1, Deg(70f), Deg(89f), 0f, 1f, "green"),
        // Gentle tier (layer 1 = red) is fully on at slope 0.40; the STEEP tier
        // (layer 2 = blue, 0.30..0.42) is ~93% there and is applied last → it wins.
        ("two tiers · steep tier wins", 2, 0.10f, 0.30f, 0.30f, 0.42f, "blue"),
    ];
    byte[] buf = new byte[4];
    foreach (var c in cases)
    {
        S1i("u_slopeActive", c.Count > 0 ? 1 : 0);
        S1i("u_slopeRuleCount", c.Count);
        S4f("u_slopeRuleStart", c.S0, c.S1, 0.62f, 1f);
        S4f("u_slopeRuleEnd", c.E0, c.E1, 0.85f, 1f);
        glClear(0x4000 /*COLOR_BUFFER_BIT*/);
        glDrawArrays(0x0004 /*TRIANGLES*/, 0, 3);
        glFinish();
        fixed (byte* px = buf) glReadPixels(0, 0, 1, 1, 0x1908, 0x1401, px);
        int r = buf[0], g = buf[1], b = buf[2];
        bool ok = c.Want switch
        {
            "red" => r > g && r > 100,
            "blue" => b > r && b > 100,
            _ => g > r && g > 100,
        };
        Console.WriteLine($"[SMOKE] {(ok ? "PASS" : "FAIL")}: slope render — {c.Label} → rgb({r},{g},{b})");
        if (!ok) fails++;
    }
    glUseProgram(0);
    return fails;
}

// ── Main ──
if (glfwInit() == 0)
{
    Console.WriteLine("[SMOKE] FAIL: glfwInit failed");
    return 1;
}

glfwDefaultWindowHints();
glfwWindowHint(GLFW_VISIBLE, 0);
glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);

nint win = glfwCreateWindow(64, 64, "shader smoke", nint.Zero, nint.Zero);
if (win == nint.Zero)
{
    Console.WriteLine("[SMOKE] FAIL: window/context creation failed");
    glfwTerminate();
    return 1;
}
glfwMakeContextCurrent(win);

glCreateShader = GetFn<glCreateShaderDel>("glCreateShader");
glShaderSource = GetFn<glShaderSourceDel>("glShaderSource");
glCompileShader = GetFn<glCompileShaderDel>("glCompileShader");
glGetShaderiv = GetFn<glGetShaderivDel>("glGetShaderiv");
glGetShaderInfoLog = GetFn<glGetShaderInfoLogDel>("glGetShaderInfoLog");
glCreateProgram = GetFn<glCreateProgramDel>("glCreateProgram");
glAttachShader = GetFn<glAttachShaderDel>("glAttachShader");
glLinkProgram = GetFn<glLinkProgramDel>("glLinkProgram");
glGetProgramiv = GetFn<glGetProgramivDel>("glGetProgramiv");
glGetProgramInfoLog = GetFn<glGetProgramInfoLogDel>("glGetProgramInfoLog");
glDeleteShader = GetFn<glDeleteShaderDel>("glDeleteShader");
glGetUniformLocation = GetFn<glGetUniformLocationDel>("glGetUniformLocation");
glGenVertexArrays = GetFn<glGenVertexArraysDel>("glGenVertexArrays");
glBindVertexArray = GetFn<glBindVertexArrayDel>("glBindVertexArray");
glUseProgram = GetFn<glUseProgramDel>("glUseProgram");
glActiveTexture = GetFn<glActiveTextureDel>("glActiveTexture");
glUniform1i = GetFn<glUniform1iDel>("glUniform1i");
glUniform1f = GetFn<glUniform1fDel>("glUniform1f");
glUniform2f = GetFn<glUniform2fDel>("glUniform2f");
glUniform3f = GetFn<glUniform3fDel>("glUniform3f");
glUniform4f = GetFn<glUniform4fDel>("glUniform4f");

int glMajor = 0, glMinor = 0;
unsafe
{
    glGetIntegerv(0x821B, &glMajor); // GL_MAJOR_VERSION
    glGetIntegerv(0x821C, &glMinor); // GL_MINOR_VERSION
}
Console.WriteLine($"[SMOKE] GL context {glMajor}.{glMinor}");
int maxTexUnits = 0;
unsafe { glGetIntegerv(0x8872, &maxTexUnits); }   // GL_MAX_TEXTURE_IMAGE_UNITS
Console.WriteLine($"[SMOKE] GL_MAX_TEXTURE_IMAGE_UNITS = {maxTexUnits}");

string e1 = "", e2 = "", e4 = "", e5 = "";
(uint Program, string Name, string Err)[] checks =
[
    (Compile("Artifacts/shaders/vertex_shader.glsl", "Artifacts/shaders/objectPbr_fragment.glsl", out e1), "objectPbr", e1),
    (Compile("Artifacts/shaders/pbrDisplace_vertex.glsl", "Artifacts/shaders/objectPbr_fragment.glsl", out e2), "objectPbr (displaced)", e2),
    // TERRAIN_SPLAT variant — prefix injection AFTER #version, same as Shader.cs.
    (CompilePrefixed("Artifacts/shaders/vertex_shader.glsl", "Artifacts/shaders/objectPbr_fragment.glsl", "#define TERRAIN_SPLAT 1\n", out e4), "objectPbr (splat)", e4),
    (CompilePrefixed("Artifacts/shaders/pbrDisplace_vertex.glsl", "Artifacts/shaders/objectPbr_fragment.glsl", "#define TERRAIN_SPLAT 1\n", out e5), "objectPbr (splat displaced)", e5),
];

int failures = 0;
foreach (var (program, name, err) in checks)
{
    Console.WriteLine($"[SMOKE] {name}: program = {program}");
    if (err.Length > 0) Console.WriteLine(err.TrimEnd());
    if (program == 0)
    {
        Console.WriteLine($"[SMOKE] FAIL: {name}");
        failures++;
    }
}

// ── SLOPE BASED TEXTURING uniforms (splat variants only) ──
// A mistyped uniform name resolves to -1 and the upload is silently skipped — the
// slope layer would simply never appear. Fail loudly here instead.
string[] slopeUniforms =
[
    "u_slopeActive", "u_slopeRuleCount", "u_slopeRuleLayer",
    "u_slopeRuleStart", "u_slopeRuleEnd", "u_slopeRuleTiling",
    "u_slopeOverride", "u_slopeFloor", "u_slopeDebug",
];
foreach (var (program, name, _) in checks)
{
    if (program == 0 || !name.Contains("splat")) continue;
    foreach (string u in slopeUniforms)
    {
        int loc = glGetUniformLocation(program, u);
        if (loc < 0)
        {
            Console.WriteLine($"[SMOKE] FAIL: {name} has no uniform {u}");
            failures++;
        }
    }
}

// ── BEHAVIORAL CHECK: slope based texturing actually blends (one real pixel) ──
failures += SlopeRenderCheck();

glfwDestroyWindow(win);
glfwTerminate();
Console.WriteLine(failures == 0 ? "[SMOKE] ALL OK" : $"[SMOKE] {failures} FAILURE(S)");
return failures == 0 ? 0 : 1;

// ── GL delegate types MUST come after the top-level statements (C# order rule). ──
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint glCreateShaderDel(int type);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] unsafe delegate void glShaderSourceDel(uint shader, int count, byte** strings, nint* lengths);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void glCompileShaderDel(uint shader);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] unsafe delegate void glGetShaderivDel(uint shader, int pname, int* param);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] unsafe delegate void glGetShaderInfoLogDel(uint shader, int bufSize, nint* length, byte* infoLog);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint glCreateProgramDel();
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void glAttachShaderDel(uint program, uint shader);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void glLinkProgramDel(uint program);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] unsafe delegate void glGetProgramivDel(uint program, int pname, int* param);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] unsafe delegate void glGetProgramInfoLogDel(uint program, int bufSize, nint* length, byte* infoLog);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void glDeleteShaderDel(uint shader);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int glGetUniformLocationDel(uint program, [MarshalAs(UnmanagedType.LPStr)] string name);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] unsafe delegate void glGenVertexArraysDel(int n, uint* arrays);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void glBindVertexArrayDel(uint array);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void glUseProgramDel(uint program);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void glActiveTextureDel(int texture);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void glUniform1iDel(int location, int v);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void glUniform1fDel(int location, float v);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void glUniform2fDel(int location, float x, float y);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void glUniform3fDel(int location, float x, float y, float z);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void glUniform4fDel(int location, float x, float y, float z, float w);
