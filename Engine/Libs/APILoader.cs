using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace DarkEngine3D_gl_csharp.Engine.Libs
{
    // Definisikan class GL untuk menampung function pointers
    public static unsafe class GL
    {
        // Simpan alamat mentahnya
        internal static IntPtr ClearColorPtr;
        internal static IntPtr ClearPtr;
        internal static IntPtr GenBuffersPtr;
        internal static IntPtr BindBufferPtr;
        internal static IntPtr BufferDataPtr;
        internal static IntPtr GetUniformLocationPtr;
        internal static IntPtr GetUniformfvPtr;
        internal static IntPtr UniformMatrix4fvPtr;
        internal static IntPtr UniformMatrix3fvPtr;
        internal static IntPtr CreateShaderPtr, ShaderSourcePtr, CompileShaderPtr;
        internal static IntPtr CreateProgramPtr, AttachShaderPtr, LinkProgramPtr, UseProgramPtr, DeleteShaderPtr;
        internal static IntPtr VertexAttribPointerPtr;
        internal static IntPtr VertexAttribIPointerPtr;
        internal static IntPtr ReadPixelsPtr;
        internal static IntPtr GetStringPtr;

        internal static IntPtr EnableVertexAttribArrayPtr;
        internal static IntPtr DisableVertexAttribArrayPtr;
        internal static IntPtr DrawArraysPtr;
        internal static IntPtr GenVertexArraysPtr; 
        internal static IntPtr BindVertexArrayPtr;
        internal static IntPtr EnablePtr;
        internal static IntPtr DisablePtr;
        internal static IntPtr PolygonModePtr;
        internal static IntPtr DeleteVertexArraysPtr;
        internal static IntPtr DeleteBuffersPtr;
        internal static IntPtr CullFacePtr;
        internal static IntPtr FrontFacePtr;
        internal static IntPtr Uniform3fPtr;
        internal static IntPtr Uniform2fPtr;
        internal static IntPtr ViewportPtr;
        internal static IntPtr GenTexturesPtr;
        internal static IntPtr DeleteTexturesPtr = IntPtr.Zero;
        internal static IntPtr BindTexturePtr;
        internal static IntPtr TexImage2DPtr;
        internal static IntPtr TexParameteriPtr;
        internal static IntPtr GenerateMipmapPtr;
        internal static IntPtr ActiveTexturePtr;
        internal static IntPtr DrawElementsPtr;
        internal static IntPtr Uniform1iPtr = IntPtr.Zero;
        internal static IntPtr Uniform1fPtr = IntPtr.Zero;
        internal static IntPtr Uniform4fPtr = IntPtr.Zero;

        internal static IntPtr TexParameterfPtr = IntPtr.Zero;
        internal static IntPtr TexParameterfvPtr = IntPtr.Zero;
        internal static IntPtr BlendFuncPtr = IntPtr.Zero;
        internal static IntPtr PixelStorePtr = IntPtr.Zero;
        internal static IntPtr BufferSubDataPtr = IntPtr.Zero;
        internal static IntPtr PolygonOffsetPtr = IntPtr.Zero;

        internal static IntPtr GetShaderivPtr = IntPtr.Zero;
        internal static IntPtr GetShaderInfoLogPtr = IntPtr.Zero;
        internal static IntPtr GetProgramivPtr = IntPtr.Zero;
        internal static IntPtr GetProgramInfoLogPtr = IntPtr.Zero;
        internal static IntPtr GetErrorPtr = IntPtr.Zero;
        
        internal static IntPtr GenFramebuffersPtr = IntPtr.Zero;
        internal static IntPtr BindFramebufferPtr = IntPtr.Zero;
        internal static IntPtr FramebufferTexture2DPtr = IntPtr.Zero;
        internal static IntPtr DrawBuffersPtr = IntPtr.Zero;
        internal static IntPtr CheckFramebufferStatusPtr = IntPtr.Zero;
        internal static IntPtr DepthMaskPtr = IntPtr.Zero;
        internal static IntPtr DeleteFramebuffersPtr = IntPtr.Zero;
        internal static IntPtr GenRenderbuffersPtr = IntPtr.Zero;
        internal static IntPtr BindRenderbufferPtr = IntPtr.Zero;
        internal static IntPtr RenderbufferStoragePtr = IntPtr.Zero;
        internal static IntPtr FramebufferRenderbufferPtr = IntPtr.Zero;
        internal static IntPtr DepthFuncPtr = IntPtr.Zero;

        // Buat properti pembungkus agar pemanggilan tetap bersih

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void ReadPixels(
            int x, int y,
            int width, int height,
            uint format, uint type,
            float*  data)
        {
            if (ReadPixelsPtr == 0)
                throw new Exception("glReadPixels not loaded!");

            ((delegate* unmanaged[Stdcall]<int, int, int, int, uint, uint, float*, void>)ReadPixelsPtr)
                (x, y, width, height, format, type, data);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe float GetUniformfv(uint program, int location)
        {
            if (GetUniformfvPtr == 0)
                throw new Exception("glGetUniformfv not loaded!");

            float value = 0f;

            ((delegate* unmanaged[Stdcall]<uint, int, float*, void>)GetUniformfvPtr)
                (program, location, &value);

            return value;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetString(uint name)
        {
            // Tanda tangan pointer fungsi: menerima uint, mengembalikan byte*
            var ptr = (delegate* unmanaged[Cdecl]<uint, byte*>)GetStringPtr;

            // Panggil fungsi native OpenGL langsung ke driver GPU
            byte* nativeStringPtr = ptr(name);

            // Validasi jika driver mengembalikan pointer kosong (null)
            if (nativeStringPtr == null)
            {
                return string.Empty;
            }

            // Konversi pointer teks ANSI/UTF8 (Null-Terminated) menjadi string C#
            return Marshal.PtrToStringAnsi((IntPtr)nativeStringPtr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DepthFunc(uint function)
            => ((delegate* unmanaged[Cdecl]<uint, void>)DepthFuncPtr)(function);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void TexParameterfv(uint target, uint pname, float[] param)
            => ((delegate* unmanaged[Cdecl]<uint, uint, float[], void>)TexParameterfvPtr)(target, pname, param);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UniformMatrix3fv(int location, int count, bool transpose, float* value)
            => ((delegate* unmanaged[Cdecl]<int, int, byte, float*, void>)UniformMatrix3fvPtr)(location, count, (byte)(transpose ? 1 : 0), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BindRenderbuffer(uint target, uint renderbuffer)
            => ((delegate* unmanaged[Cdecl]<uint, uint, void>)BindRenderbufferPtr)(target, renderbuffer);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RenderbufferStorage(uint target, uint internalformat, int width, int height)
            => ((delegate* unmanaged[Cdecl]<uint, uint, int, int, void>)RenderbufferStoragePtr)(target, internalformat, width, height);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FramebufferRenderbuffer(uint target, uint attachment, uint renderbuffertarget, uint renderbuffer)
            => ((delegate* unmanaged[Cdecl]<uint, uint, uint, uint, void>)FramebufferRenderbufferPtr)(target, attachment, renderbuffertarget, renderbuffer);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GenRenderbuffers(int n, uint* renderbuffers)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)GenRenderbuffersPtr)(n, renderbuffers);   

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DeleteFramebuffers(int n, uint* framebuffers)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)DeleteFramebuffersPtr)(n, framebuffers);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DepthMask(bool flag)
            => ((delegate* unmanaged[Cdecl]<byte, void>)DepthMaskPtr)((byte)(flag ? 1 : 0));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CheckFramebufferStatus(uint target)
            => ((delegate* unmanaged[Cdecl]<uint, int>)CheckFramebufferStatusPtr)(target);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GenFramebuffers(int n, uint* framebuffers)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)GenFramebuffersPtr)(n, framebuffers);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BindFramebuffer(uint target, uint framebuffer)
            => ((delegate* unmanaged[Cdecl]<uint, uint, void>)BindFramebufferPtr)(target, framebuffer);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FramebufferTexture2D(uint target, uint attachment, uint textarget, uint texture, int level)
            => ((delegate* unmanaged[Cdecl]<uint, uint, uint, uint, int, void>)FramebufferTexture2DPtr)(target, attachment, textarget, texture, level);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DrawBuffers(int n, uint* bufs)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)DrawBuffersPtr)(n, bufs); 

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearColor(float r, float g, float b, float a)
            => ((delegate* unmanaged[Cdecl]<float, float, float, float, void>)ClearColorPtr)(r, g, b, a);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Clear(uint mask)
            => ((delegate* unmanaged[Cdecl]<uint, void>)ClearPtr)(mask);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GenBuffers(int n, uint* buffers)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)GenBuffersPtr)(n, buffers);
         
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BindBuffer(uint target, uint buffer)
            => ((delegate* unmanaged[Cdecl]<uint, uint, void>)BindBufferPtr)(target, buffer);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BufferData(uint target, nuint size, void* data, uint usage)
            => ((delegate* unmanaged[Cdecl]<uint, nuint, void*, uint, void>)BufferDataPtr)(target, size, data, usage);

        public static int GetUniformLocation(uint program, string name)
        {
            var ptr = (delegate* unmanaged[Cdecl]<uint, byte*, int>)GetUniformLocationPtr;

            // Efisiensi: Hindari alokasi byte[] & string baru (name + "\0")
            // Gunakan stackalloc untuk string kecil agar tidak membebani Garbage Collector
            int byteCount = System.Text.Encoding.UTF8.GetByteCount(name);
            Span<byte> buffer = byteCount < 256 ? stackalloc byte[byteCount + 1] : new byte[byteCount + 1];
            System.Text.Encoding.UTF8.GetBytes(name, buffer);
            buffer[byteCount] = 0; // Null terminator (\0)

            fixed (byte* namePtr = buffer)
            {
                return ptr(program, namePtr);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UniformMatrix4fv(int location, int count, bool transpose, float* value)
            => ((delegate* unmanaged[Cdecl]<int, int, byte, float*, void>)UniformMatrix4fvPtr)(location, count, (byte)(transpose ? 1 : 0), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint CreateShader(uint type)
            => ((delegate* unmanaged[Cdecl]<uint, uint>)CreateShaderPtr)(type);

        public static void ShaderSource(uint shader, string source)
        {
            var ptr = (delegate* unmanaged[Cdecl]<uint, int, byte**, int*, void>)ShaderSourcePtr;

            // Efisiensi: Jangan membuat string alokasi ganda (source + "\0")
            int byteCount = System.Text.Encoding.UTF8.GetByteCount(source);
            byte[] sourceBytes = new byte[byteCount + 1];
            System.Text.Encoding.UTF8.GetBytes(source, 0, source.Length, sourceBytes, 0);
            sourceBytes[byteCount] = 0; // Null terminator (\0)

            fixed (byte* pSource = sourceBytes)
            {
                byte** pSourcePointerToPointer = &pSource;
                ptr(shader, 1, pSourcePointerToPointer, null);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CompileShader(uint shader)
            => ((delegate* unmanaged[Cdecl]<uint, void>)CompileShaderPtr)(shader);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint CreateProgram()
            => ((delegate* unmanaged[Cdecl]<uint>)CreateProgramPtr)();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AttachShader(uint program, uint shader)
            => ((delegate* unmanaged[Cdecl]<uint, uint, void>)AttachShaderPtr)(program, shader);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LinkProgram(uint program)
            => ((delegate* unmanaged[Cdecl]<uint, void>)LinkProgramPtr)(program);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UseProgram(uint program)
            => ((delegate* unmanaged[Cdecl]<uint, void>)UseProgramPtr)(program);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DeleteShader(uint shader)
            => ((delegate* unmanaged[Cdecl]<uint, void>)DeleteShaderPtr)(shader);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void VertexAttribPointer(uint index, int size, uint type, bool normalized, int stride, void* pointer)
            => ((delegate* unmanaged[Cdecl]<uint, int, uint, byte, int, void*, void>)VertexAttribPointerPtr)(index, size, type, (byte)(normalized ? 1 : 0), stride, pointer);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void VertexAttribIPointer(uint index, int size, uint type, int stride, void* pointer)
            => ((delegate* unmanaged[Cdecl]<uint, int, uint, int, void*, void>)VertexAttribIPointerPtr)(index, size, type, stride, pointer);

        public static void EnableVertexAttribArray(uint index)
            => ((delegate* unmanaged[Cdecl]<uint, void>)EnableVertexAttribArrayPtr)(index);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DisableVertexAttribArray(uint index)
            => ((delegate* unmanaged[Cdecl]<uint, void>)DisableVertexAttribArrayPtr)(index);

       

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DrawElements(uint mode, int count, uint type, void* indices)
            => ((delegate* unmanaged[Cdecl]<uint, int, uint, void*, void>)DrawElementsPtr)(mode, count, type, indices);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GenVertexArrays(int n, uint* arrays)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)GenVertexArraysPtr)(n, arrays);

       
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BindVertexArray(uint array)
            => ((delegate* unmanaged[Cdecl]<uint, void>)BindVertexArrayPtr)(array);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Enable(uint cap)
            => ((delegate* unmanaged[Cdecl]<uint, void>)EnablePtr)(cap);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Disable(uint cap)
            => ((delegate* unmanaged[Cdecl]<uint, void>)DisablePtr)(cap);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PolygonMode(uint face, uint mode)
            => ((delegate* unmanaged[Cdecl]<uint, uint, void>)PolygonModePtr)(face, mode);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DeleteVertexArrays(int n, uint* arrays)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)DeleteVertexArraysPtr)(n, arrays);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DeleteBuffers(int n, uint* buffers)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)DeleteBuffersPtr)(n, buffers);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CullFace(uint mode)
            => ((delegate* unmanaged[Cdecl]<uint, void>)CullFacePtr)(mode);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void FrontFace(uint mode)
            => ((delegate* unmanaged[Cdecl]<uint, void>)FrontFacePtr)(mode);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Uniform3f(int location, float v0, float v1, float v2)
            => ((delegate* unmanaged[Cdecl]<int, float, float, float, void>)Uniform3fPtr)(location, v0, v1, v2);

         
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GenTextures(int n, uint* textures)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)GenTexturesPtr)(n, textures);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DeleteTextures(int n, uint* textures)
            => ((delegate* unmanaged[Cdecl]<int, uint*, void>)DeleteTexturesPtr)(n, textures);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BindTexture(uint target, uint texture)
            => ((delegate* unmanaged[Cdecl]<uint, uint, void>)BindTexturePtr)(target, texture);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void TexImage2D(uint target, int level, int internalFormat, int width, int height, int border, uint format, uint type, void* data)
            => ((delegate* unmanaged[Cdecl]<uint, int, int, int, int, int, uint, uint, void*, void>)TexImage2DPtr)(target, level, internalFormat, width, height, border, format, type, data);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void TexParameteri(uint target, uint pname, int param)
            => ((delegate* unmanaged[Cdecl]<uint, uint, int, void>)TexParameteriPtr)(target, pname, param);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GenerateMipmap(uint target)
            => ((delegate* unmanaged[Cdecl]<uint, void>)GenerateMipmapPtr)(target);
         
          
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Uniform2f(int location, float v0, float v1)
            => ((delegate* unmanaged[Cdecl]<int, float, float, void>)Uniform2fPtr)(location, v0, v1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Uniform4f(int location, float v0, float v1, float v2, float v3)
            => ((delegate* unmanaged[Cdecl]<int, float, float, float, float, void>)Uniform4fPtr)(location, v0, v1, v2, v3);
         
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void TexParameterf(uint target, uint pname, float param)
            => ((delegate* unmanaged[Cdecl]<uint, uint, float, void>)TexParameterfPtr)(target, pname, param);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BlendFunc(uint sfactor, uint dfactor)
            => ((delegate* unmanaged[Cdecl]<uint, uint, void>)BlendFuncPtr)(sfactor, dfactor);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PixelStore(uint pname, int param)
            => ((delegate* unmanaged[Cdecl]<uint, int, void>)PixelStorePtr)(pname, param);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BufferSubData(uint target, nuint offset, nuint size, void* data)
            => ((delegate* unmanaged[Cdecl]<uint, nuint, nuint, void*, void>)BufferSubDataPtr)(target, offset, size, data);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void PolygonOffset(float factor, float units)
            => ((delegate* unmanaged[Cdecl]<float, float, void>)PolygonOffsetPtr)(factor, units);

        /// <summary>Expose raw ptr for glVertexAttribIPointer (integer attributes).</summary>
        public static IntPtr GetVertexAttribIPointerFn() => VertexAttribIPointerPtr;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetShaderiv(uint shader, uint pname, int* param)
            => ((delegate* unmanaged[Cdecl]<uint, uint, int*, void>)GetShaderivPtr)(shader, pname, param);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetProgramiv(uint program, uint pname, int* param)
            => ((delegate* unmanaged[Cdecl]<uint, uint, int*, void>)GetProgramivPtr)(program, pname, param);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Viewport(int x, int y, int width, int height)
        {
            if (ViewportPtr == IntPtr.Zero) throw new InvalidOperationException("glViewport not loaded");
            ((delegate* unmanaged[Cdecl]<int, int, int, int, void>)ViewportPtr)(x, y, width, height);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DrawArrays(uint mode, int first, int count)
        {
            if (DrawArraysPtr == IntPtr.Zero) throw new InvalidOperationException("glDrawArrays not loaded");
            ((delegate* unmanaged[Cdecl]<uint, int, int, void>)DrawArraysPtr)(mode, first, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ActiveTexture(uint texture)
        {
            if (ActiveTexturePtr == IntPtr.Zero) throw new InvalidOperationException("glActiveTexture not loaded");
            ((delegate* unmanaged[Cdecl]<uint, void>)ActiveTexturePtr)(texture);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Uniform1i(int location, int value)
        {
            if (Uniform1iPtr == IntPtr.Zero) throw new InvalidOperationException("glUniform1i not loaded");
            ((delegate* unmanaged[Cdecl]<int, int, void>)Uniform1iPtr)(location, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Uniform1f(int location, float value)
        {
            if (Uniform1fPtr == IntPtr.Zero) throw new InvalidOperationException("glUniform1f not loaded");
            ((delegate* unmanaged[Cdecl]<int, float, void>)Uniform1fPtr)(location, value);
        }

        public unsafe static string GetShaderInfoLog(uint shader)
        {
            if (GetShaderInfoLogPtr == IntPtr.Zero) return "(GetShaderInfoLog not loaded)";
            int status = 0;
            GetShaderiv(shader,Const.GL_COMPILE_STATUS  , &status);
            if (status == 1) return "";
            int len = 0;
            GetShaderiv(shader, Const.GL_INFO_LOG_LENGTH  , &len);
            if (len <= 0) return "(no log)";
            byte[] buf = new byte[len];
            fixed (byte* pb = buf)
                ((delegate* unmanaged[Cdecl]<uint, int, int*, byte*, void>)GetShaderInfoLogPtr)(shader, len, null, pb);
            return System.Text.Encoding.UTF8.GetString(buf, 0, len - 1);
        }

        public unsafe static string GetProgramInfoLog(uint program)
        {
            if (GetProgramInfoLogPtr == IntPtr.Zero) return "(GetProgramInfoLog not loaded)";
            int status = 0;
            GetProgramiv(program, Const.GL_LINK_STATUS, &status);
            if (status == 1) return "";
            int len = 0;
            GetProgramiv(program, Const.GL_INFO_LOG_LENGTH, &len);
            if (len <= 0) return "(no log)";
            byte[] buf = new byte[len];
            fixed (byte* pb = buf)
                ((delegate* unmanaged[Cdecl]<uint, int, int*, byte*, void>)GetProgramInfoLogPtr)(program, len, null, pb);
            return System.Text.Encoding.UTF8.GetString(buf, 0, len - 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetError()
            => GetErrorPtr != IntPtr.Zero
                ? ((delegate* unmanaged[Cdecl]<uint>)GetErrorPtr)()
                : 0u;
    }

    public static class ApiLoader
    {
        public static unsafe void LoadFunctions(nint libraryHandle)
        {
            // BUG FIX: Karena pointer di GL sekarang 'internal', kita harus pakai BindingFlags.NonPublic
            var fields = typeof(GL).GetFields(BindingFlags.NonPublic | BindingFlags.Static);

            // Ambil pointer fungsi wglGetProcAddress sekali saja, tidak di dalam loop
            var wglAddr = Marshal.GetDelegateForFunctionPointer<wglGetProcAddressDelegate>(
                NativeLibrary.GetExport(libraryHandle, "wglGetProcAddress")
            );

            foreach (var field in fields)
            {
                string baseName = field.Name.Replace("Ptr", ""); // e.g. "GenFramebuffers"
                string[] candidates = new string[] {
                    "gl" + baseName,
                    "gl" + baseName + "ARB",
                    "gl" + baseName + "EXT"
                };

                nint address = nint.Zero;
                foreach (var name in candidates)
                {
                    address = wglAddr(name);
                    if (address == nint.Zero || address == 1 || address == 2 || address == -1)
                    {
                        NativeLibrary.TryGetExport(libraryHandle, name, out address);
                    }
                    if (address != nint.Zero) break;
                }

                if (address != nint.Zero)
                    field.SetValue(null, address);
                else
                    Console.WriteLine($"WARNING: Failed to load function pointer for {string.Join("/", candidates)}");
                 
                }
            }
        }
     

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    public delegate nint wglGetProcAddressDelegate(string name);
}
