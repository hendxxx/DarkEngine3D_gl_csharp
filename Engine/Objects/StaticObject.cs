using DarkEngine3D_gl_csharp.Engine.Libs;
using System.Numerics;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    public unsafe class StaticObject
    {
        public readonly GltfModelGpuData GpuData;

        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale = 1f;

        public bool IsVisible = true;

        public AABB LocalAABB => GpuData.LocalAABB;
        public AABB WorldAABB => LocalAABB.ToWorld(Position, Scale);

        public StaticObject(GltfModelGpuData gpu, Vector3 pos, float yawDeg, float scale = 1f)
        {
            GpuData = gpu;
            Position = pos;
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yawDeg * MathF.PI / 180f);
            Scale = scale;
        }

        public void Draw(int modelLoc, int baseColorFactorLoc, int useAlbedoLoc, int albedoMapLoc)
        {
            if (!IsVisible) return;

            Matrix4x4 model =
                Matrix4x4.CreateScale(Scale)
              * Matrix4x4.CreateFromQuaternion(Rotation)
              * Matrix4x4.CreateTranslation(Position);

            GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&model);

            foreach (var mesh in GpuData.Meshes)
            {
                var mat = mesh.Material;

                // BaseColorFactor
                GL.Uniform4f(baseColorFactorLoc,
                    mat.BaseColorFactor.X,
                    mat.BaseColorFactor.Y,
                    mat.BaseColorFactor.Z,
                    mat.BaseColorFactor.W);

                // Texture binding
                if (mat.HasTexture)
                {
                    GL.Uniform1i(useAlbedoLoc, 1);
                    GL.ActiveTexture(Const.GL_TEXTURE0);
                    GL.BindTexture(Const.GL_TEXTURE_2D, mat.TextureID);
                    GL.Uniform1i(albedoMapLoc, 0);
                }
                else
                {
                    GL.Uniform1i(useAlbedoLoc, 0);
                }

                // Double-sided
                if (mat.DoubleSided)
                    GL.Disable(Const.GL_CULL_FACE);
                else
                    GL.Enable(Const.GL_CULL_FACE);

                GL.BindVertexArray(mesh.VAO);
                GL.DrawElements(Const.GL_TRIANGLES, mesh.IndexCount, Const.GL_UNSIGNED_INT, (void*)0);
            }
        }

    }
}
