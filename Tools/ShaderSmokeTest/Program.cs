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

static T GetFn<T>(string name) where T : Delegate
{
    nint addr = wglGetProc(name);
    if (addr == nint.Zero)
        throw new InvalidOperationException($"wglGetProcAddress('{name}') returned NULL — wrong GL context version?");
    return Marshal.GetDelegateForFunctionPointer<T>(addr);
}

// ── Compile a vertex+fragment pair; returns the linked program (0 on failure). ──
unsafe uint Compile(string vertexPath, string fragmentPath, out string error)
{
    error = "";
    uint vs = glCreateShader(0x8B31); // GL_VERTEX_SHADER
    uint fs = glCreateShader(0x8B30); // GL_FRAGMENT_SHADER
    string vsErr = CompileOne(vs, File.ReadAllText(vertexPath));
    string fsErr = CompileOne(fs, File.ReadAllText(fragmentPath));
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

int glMajor = 0, glMinor = 0;
unsafe
{
    glGetIntegerv(0x821B, &glMajor); // GL_MAJOR_VERSION
    glGetIntegerv(0x821C, &glMinor); // GL_MINOR_VERSION
}
Console.WriteLine($"[SMOKE] GL context {glMajor}.{glMinor}");

string e1 = "", e2 = "", e3 = "";
(uint Program, string Name, string Err)[] checks =
[
    (Compile("Artifacts/shaders/vertex_shader.glsl", "Artifacts/shaders/objectPbrSplat_fragment.glsl", out e1), "objectPbrSplat (flat)", e1),
    (Compile("Artifacts/shaders/pbrDisplace_vertex.glsl", "Artifacts/shaders/objectPbrSplat_fragment.glsl", out e2), "objectPbrSplat (displaced)", e2),
    (Compile("Artifacts/shaders/vertex_shader.glsl", "Artifacts/shaders/objectPbr_fragment.glsl", out e3), "objectPbr", e3),
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
