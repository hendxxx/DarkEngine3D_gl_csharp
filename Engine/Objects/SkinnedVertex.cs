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
        public Vector2 TexCoord;    // location 2 | offset 24 | 8 bytes
        public Vector4 BoneWeights;   // 16 bytes
        public BoneIndex4 BoneIds;       // 16 bytes

        public SkinnedVertex(Vector3 pos, Vector3 normal, Vector2 texcoord, Vector4 boneWeights, BoneIndex4 boneIds)
        {
            Position = pos;
            Normal   = normal;
            TexCoord = texcoord;
            BoneWeights = boneWeights;
            BoneIds = boneIds;
        }

        public struct BoneIndex4
        {
            public int X;
            public int Y;
            public int Z;
            public int W;
        }
    }
}
