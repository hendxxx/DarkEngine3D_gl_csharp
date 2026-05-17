using System.Numerics;
using System.Runtime.InteropServices;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    /// <summary>
    /// Vertex dengan dukungan skinning (maksimal 4 bone per vertex).
    /// Layout Sequential agar cocok langsung dengan OpenGL attribute pointers.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SkinnedVertex
    {
        public Vector3 Position;    // location 0  | offset 0
        public Vector3 Normal;      // location 1  | offset 12
        public Vector2 UV;          // location 2  | offset 24
        public Vector4 BoneWeights; // location 3  | offset 32
        public Vector4 BoneIds;     // location 4  | offset 48  — stored as float, cast to int in shader

        public SkinnedVertex(Vector3 pos, Vector3 normal, Vector2 uv, Vector4 boneWeights, Vector4 boneIds)
        {
            Position    = pos;
            Normal      = normal;
            UV          = uv;
            BoneWeights = boneWeights;
            BoneIds     = boneIds;
        }
    }
}
