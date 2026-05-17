using System.Numerics;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    /// <summary>
    /// Vertex layout untuk static glTF mesh (tanpa skinning).
    /// Stride = 32 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SkinnedVertex
    {
        public Vector3 Position;    // location 0 | offset 0  | 12 bytes
        public Vector3 Normal;      // location 1 | offset 12 | 12 bytes
        public Vector2 UV;          // location 2 | offset 24 | 8 bytes

        public SkinnedVertex(Vector3 pos, Vector3 normal, Vector2 uv)
        {
            Position = pos;
            Normal   = normal;
            UV       = uv;
        }
    }
}
