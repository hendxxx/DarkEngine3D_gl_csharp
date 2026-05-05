using System.Reflection;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine
{
    // Definisikan class GL untuk menampung function pointers
    public static unsafe class GL
    { 
        // Simpan alamat mentahnya
        public static IntPtr ClearColorPtr;
        public static IntPtr ClearPtr;
        public static IntPtr GenBuffersPtr;
        public static IntPtr BindBufferPtr;
        public static IntPtr BufferDataPtr; 
        public static IntPtr GetUniformLocationPtr;
        public static IntPtr UniformMatrix4fvPtr;
        public static IntPtr CreateShaderPtr, ShaderSourcePtr, CompileShaderPtr;
        public static IntPtr CreateProgramPtr, AttachShaderPtr, LinkProgramPtr, UseProgramPtr, DeleteShaderPtr;
        public static IntPtr VertexAttribPointerPtr;
        public static IntPtr EnableVertexAttribArrayPtr;
        public static IntPtr DrawArraysPtr;

        public static IntPtr GenVertexArraysPtr;
        public static IntPtr BindVertexArrayPtr;
        public static IntPtr EnablePtr;

        public static IntPtr PolygonModePtr;

        public static IntPtr DeleteVertexArraysPtr;
        public static IntPtr DeleteBuffersPtr;

        // Buat properti pembungkus agar pemanggilan tetap bersih
        public static void ClearColor(float r, float g, float b, float a)
            => ((delegate* unmanaged[Cdecl]<float, float, float, float, void>)ClearColorPtr)(r, g, b, a);

        public static void Clear(uint mask)
            => ((delegate* unmanaged[Cdecl]<uint, void>)ClearPtr)(mask);
        public static void GenBuffers(int n, uint* buffers)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)GenBuffersPtr)(n, buffers);
         

        public static void BindBuffer(uint target, uint buffer)
            => ((delegate* unmanaged[Cdecl]<uint, uint, void>)BindBufferPtr)(target, buffer);

        public static void BufferData(uint target, nuint size, void* data, uint usage)
            => ((delegate* unmanaged[Cdecl]<uint, nuint, void*, uint, void>)BufferDataPtr)(target, size, data, usage);
         
        public static int GetUniformLocation(uint program, string name)
        {
            var ptr = (delegate* unmanaged[Cdecl]<uint, byte*, int>)GetUniformLocationPtr;
            // Konversi string C# ke C-String (UTF8 + Null Terminator)
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name + "\0");
            fixed (byte* namePtr = nameBytes)
            {
                return ptr(program, namePtr);
            }
        }

        public static void UniformMatrix4fv(int location, int count, bool transpose, float* value)
            => ((delegate* unmanaged[Cdecl]<int, int, byte, float*, void>)UniformMatrix4fvPtr)(location, count, (byte)(transpose ? 1 : 0), value);

        public static uint CreateShader(uint type)
            => ((delegate* unmanaged[Cdecl]<uint, uint>)CreateShaderPtr)(type);

        public static void ShaderSource(uint shader, string source)
        {
            var ptr = (delegate* unmanaged[Cdecl]<uint, int, byte**, int*, void>)ShaderSourcePtr;
            // Konversi string C# ke C-style string array
            byte* sPtr = (byte*)Marshal.StringToHGlobalAnsi(source);
            byte** sPtrArray = &sPtr;
            ptr(shader, 1, sPtrArray, null);
            Marshal.FreeHGlobal((nint)sPtr);
        }

        public static void CompileShader(uint shader)
            => ((delegate* unmanaged[Cdecl]<uint, void>)CompileShaderPtr)(shader);

        public static uint CreateProgram()
            => ((delegate* unmanaged[Cdecl]<uint>)CreateProgramPtr)();

        public static void AttachShader(uint program, uint shader)
            => ((delegate* unmanaged[Cdecl]<uint, uint, void>)AttachShaderPtr)(program, shader);

        public static void LinkProgram(uint program)
            => ((delegate* unmanaged[Cdecl]<uint, void>)LinkProgramPtr)(program);

        public static void UseProgram(uint program)
            => ((delegate* unmanaged[Cdecl]<uint, void>)UseProgramPtr)(program);

        public static void DeleteShader(uint shader)
            => ((delegate* unmanaged[Cdecl]<uint, void>)DeleteShaderPtr)(shader);

        public static void VertexAttribPointer(uint index, int size, uint type, bool normalized, int stride, void* pointer)
            => ((delegate* unmanaged[Cdecl]<uint, int, uint, byte, int, void*, void>)VertexAttribPointerPtr)(index, size, type, (byte)(normalized ? 1 : 0), stride, pointer);

        public static void EnableVertexAttribArray(uint index)
            => ((delegate* unmanaged[Cdecl]<uint, void>)EnableVertexAttribArrayPtr)(index);

        public static void DrawArrays(uint mode, int first, int count)
            => ((delegate* unmanaged[Cdecl]<uint, int, int, void>)DrawArraysPtr)(mode, first, count);


        public static delegate* unmanaged[Cdecl]<IntPtr, int, int> glfwGetKey;

        public static unsafe uint GenBuffer()
        {
            uint id;
            GenBuffers(1, &id);
            return id;
        }

        public static void GenVertexArrays(int n, uint* arrays)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)GenVertexArraysPtr)(n, arrays);

        public static void BindVertexArray(uint array)
            => ((delegate* unmanaged[Cdecl]<uint, void>)BindVertexArrayPtr)(array);

        public static void Enable(uint cap)
            => ((delegate* unmanaged[Cdecl]<uint, void>)EnablePtr)(cap);

        public static void PolygonMode(uint face, uint mode)
            => ((delegate* unmanaged[Cdecl]<uint, uint, void>)PolygonModePtr)(face, mode);

        public static void DeleteVertexArrays(int n, uint* arrays)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)DeleteVertexArraysPtr)(n, arrays);

        public static void DeleteBuffers(int n, uint* buffers)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)DeleteBuffersPtr)(n, buffers);

    }

    public static class ApiLoader
    {
        public static unsafe void LoadFunctions(nint libraryHandle)
        {
            // Ambil semua field dari class GL di atas
            var fields = typeof(GL).GetFields(BindingFlags.Public | BindingFlags.Static);

            foreach (var field in fields)
            {
                // Contoh: mencari "glClearColor" di DLL
                string funcName = "gl" + field.Name;
                if (NativeLibrary.TryGetExport(libraryHandle, funcName, out nint address))
                {
                    // Masukkan alamat fungsi ke dalam pointer
                    // Catatan: field.SetValue tidak bisa langsung untuk function pointer murni.
                    // Untuk template sederhana ini, kita gunakan Marshal sebagai jembatan:
                    Marshal.GetDelegateForFunctionPointer(address, GetDelegateTypeForField(field.Name));
                    // (Opsi terbaik tetap menggunakan source generator, tapi ini cara termudah untuk start)
                }
            }
        }

        // Helper sederhana untuk mencocokkan tipe (hanya untuk testing awal)
        private static Type GetDelegateTypeForField(string name) => name switch
        { 
            "ClearColor" => typeof(ClearColorDelegate),
            "Clear" => typeof(ClearDelegate),
            "GenBuffers" => typeof(GenBuffersDelegate),
            "BindBuffer" => typeof(BindBufferDelegate),
            "BufferData" => typeof(BufferDataDelegate),
            "UniformMatrix4fv" => typeof(UniformMatrix4fvDelegate), 
            "GetUniformLocation" => typeof(GetUniformLocationDelegate),
            "CreateShader" => typeof(CreateShaderDelegate),
            "ShaderSource" => typeof(ShaderSourceDelegate),
            "CompileShader" => typeof(CompileShaderDelegate),
            "CreateProgram" => typeof(CreateProgramDelegate),
            "AttachShader" => typeof(AttachShaderDelegate),
            "LinkProgram" => typeof(LinkProgramDelegate),
            "UseProgram" => typeof(UseProgramDelegate),
            "DeleteShader" => typeof(DeleteShaderDelegate),
            "VertexAttribPointer" => typeof(VertexAttribPointerDelegate),
            "EnableVertexAttribArray" => typeof(EnableVertexAttribArrayDelegate),
            "DrawArrays" => typeof(DrawArraysDelegate),
            "GenVertexArrays" => typeof(GenVertexArraysDelegate),
            "BindVertexArray" => typeof(BindVertexArrayDelegate),
            "Enable" => typeof(EnableDelegate),
            "PolygonMode" => typeof(PolygonModeDelegate),
            "DeleteVertexArrays" => typeof(DeleteVertexArraysDelegate),
            "DeleteBuffers" => typeof(DeleteBuffersDelegate),
            _ => throw new NotImplementedException()
        };
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    public delegate nint wglGetProcAddressDelegate(string name);

    // Definisikan delegasi pendukung
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ClearColorDelegate(float r, float g, float b, float a);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ClearDelegate(uint mask);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GenBuffersDelegate(int n, uint* buffers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void BindBufferDelegate(uint target, uint buffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]  
    public unsafe delegate void BufferDataDelegate(uint target, nuint size, void* data, uint usage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void UniformMatrix4fvDelegate(int location, int count, bool transpose, float* value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GetUniformLocationDelegate(uint program, string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void CreateShaderDelegate(uint type);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void ShaderSourceDelegate(uint shader, string source);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void CompileShaderDelegate(uint shader);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void CreateProgramDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void AttachShaderDelegate(uint program, uint shader);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void LinkProgramDelegate(uint program);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void UseProgramDelegate(uint program);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void DeleteShaderDelegate(uint shader);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void VertexAttribPointerDelegate(uint index, int size, uint type, bool normalized, int stride, void* pointer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void EnableVertexAttribArrayDelegate(uint index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void DrawArraysDelegate(uint mode, int first, int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void GenVertexArraysDelegate(int n, uint* arrays);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void BindVertexArrayDelegate(uint array);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void EnableDelegate(uint cap);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void PolygonModeDelegate(uint face, uint mode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void DeleteVertexArraysDelegate(int n, uint* arrays);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public unsafe delegate void DeleteBuffersDelegate(int n, uint* buffers);
}
