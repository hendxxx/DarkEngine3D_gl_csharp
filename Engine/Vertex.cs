using System.Numerics;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex(float x, float y, float z, float nx, float ny, float nz, float r, float g, float b)
    {
        public Vector3 Position = new(x, y, z);
        public Vector3 Normal = new(nx,ny, nz); // Normal default menghadap ke atas (0, 0, 1)
        public Vector3 Color = new(r, g, b);
    }
     
}
