using System;
using DarkEngine3D_gl_csharp.Engine.Libs;

namespace DarkEngine3D_gl_csharp.Engine.Helpers
{
    public static unsafe class GltfModelGpuDataExtensions
    {
        public static void FreeGpu(this ObjectHelpers.GltfModelGpuData gpu)
        {
            if (gpu == null) return;

            try
            {
                // Free per-mesh VAO/VBO/EBO
                if (gpu.Meshes != null)
                {
                    for (int i = 0; i < gpu.Meshes.Length; i++)
                    {
                        ref var m = ref gpu.Meshes[i];
                        if (m.VAO != 0)
                        {
                            uint id = m.VAO;
                            GL.DeleteVertexArrays(1, &id);
                            m.VAO = 0;
                        }

                        if (m.VBO != 0)
                        {
                            uint id = m.VBO;
                            GL.DeleteBuffers(1, &id);
                            m.VBO = 0;
                        }

                        if (m.EBO != 0)
                        {
                            uint id = m.EBO;
                            GL.DeleteBuffers(1, &id);
                            m.EBO = 0;
                        }
                    }
                }

                // Free shared textures
                if (gpu.TextureIDs != null)
                {
                    for (int t = 0; t < gpu.TextureIDs.Length; t++)
                    {
                        if (gpu.TextureIDs[t] != 0)
                        {
                            uint id = gpu.TextureIDs[t];
                            GL.DeleteTextures(1, &id);
                            gpu.TextureIDs[t] = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Don't throw during cleanup - log and continue
                Console.WriteLine($"[GltfModelGpuData.FreeGpu] Exception: {ex.Message}");
            }
        }
    }
}
