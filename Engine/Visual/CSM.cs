using DarkEngine3D_gl_csharp.Engine.Libs;
using System;
using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Terrains;
namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    public class CSM : IDisposable
    {
        public const int NumCascades = 3;
        public int ShadowSize { get; private set; }
        public uint[] FBOs = new uint[NumCascades];
        public uint[] ShadowTextures = new uint[NumCascades];
        public Matrix4x4[] LightSpaceMatrices = new Matrix4x4[NumCascades];
        
        // Cascade split distances (near to far)
        public float[] CascadeEnds = [20.0f, 80.0f, 300.0f];
        public CSM(int shadowSize = 2048)
        {
            ShadowSize = shadowSize;
            CreateShadowMaps();
        }
        private unsafe void CreateShadowMaps()
        {
            fixed (uint* pFbos = FBOs)
            {
                GL.GenFramebuffers(NumCascades, pFbos);
            }
            fixed (uint* pTexs = ShadowTextures)
            {
                GL.GenTextures(NumCascades, pTexs);
            }
            for (int i = 0; i < NumCascades; i++)
            {
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, FBOs[i]);
                GL.BindTexture(Const.GL_TEXTURE_2D, ShadowTextures[i]);
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_DEPTH_COMPONENT24,
                              ShadowSize, ShadowSize, 0,
                              Const.GL_DEPTH_COMPONENT, Const.GL_FLOAT, (void*)0);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_BORDER);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_BORDER);
                // Set border color to white so areas outside shadow map are not shadowed
                float[] borderColor = [1.0f, 1.0f, 1.0f, 1.0f];
                fixed (float* pBorder = borderColor)
                {
                    GL.TexParameterfv(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_BORDER_COLOR, pBorder);
                }
                GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_ATTACHMENT,
                                        Const.GL_TEXTURE_2D, ShadowTextures[i], 0);
                // No color buffer needed for shadow map FBO
                uint none = 0; // GL_NONE = 0
                GL.DrawBuffers(0, &none);
                uint status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
                if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                {
                    Console.WriteLine($"[CSM] FBO {i} incomplete: 0x{status:X}");
                }
                GL.DrawBuffer(Const.GL_NONE);
                GL.ReadBuffer(Const.GL_NONE);
            }
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);

        }
        private static Matrix4x4 CreateOrthographicOffCenterOpenGL(float left, float right, float bottom, float top, float zNear, float zFar)
        {
            Matrix4x4 m = Matrix4x4.Identity;
            m.M11 = 2.0f / (right - left);
            m.M22 = 2.0f / (top - bottom);
            m.M33 = -2.0f / (zFar - zNear);
            m.M41 = -(right + left) / (right - left);
            m.M42 = -(top + bottom) / (top - bottom);
            m.M43 = -(zFar + zNear) / (zFar - zNear);
            return m;
        }
        public void UpdateMatrices(Camera camera, Vector3 lightDir)
        {
            float prevSplit = camera.NearDist;

            for (int i = 0; i < NumCascades; i++)
            {
                float nextSplit = CascadeEnds[i];

                Matrix4x4 splitProj = Matrix4x4.CreatePerspectiveFieldOfView(
                    camera.FoV, camera.GetAspect(), prevSplit, nextSplit);

                Vector3[] corners = TerrainChunk.GetFrustumCorners(
                    camera.GetViewMatrix(), splitProj);

                // 1. center
                Vector3 center = Vector3.Zero;
                foreach (var c in corners)
                    center += c;

                center /= 8.0f;
                // === radius ===
                float radius = 0f;
                for (int j = 0; j < 8; j++)
                {
                    float dist = (corners[j] - center).Length();
                    radius = MathF.Max(radius, dist);
                }

                radius = MathF.Ceiling(radius * 16.0f) / 16.0f;

                // 🔥 scale per cascade
                float radiusScale = 1.25f;
                if (i == 1)
                    radiusScale = 1.7f;

                radius *= radiusScale;


                // ✅ WAJIB (ini tadi missing)
                float minX = -radius;
                float maxX = radius;
                float minY = -radius;
                float maxY = radius;


                // === light ===
                Vector3 lightPos = center + lightDir * radius;
                Matrix4x4 lightView = Matrix4x4.CreateLookAt(lightPos, center, up);


                // === Z range ===
                float minZ = float.MaxValue;
                float maxZ = float.MinValue;

                for (int j = 0; j < 8; j++)
                {
                    Vector3 lp = Vector3.Transform(corners[j], lightView);
                    minZ = MathF.Min(minZ, lp.Z);
                    maxZ = MathF.Max(maxZ, lp.Z);
                }

                // cascade tuning
                float forwardFactor = 3.5f;
                float backwardFactor = 0.7f;

                if (i == 1)
                {
                    forwardFactor = 4.5f;
                    backwardFactor = 1.2f;
                }

                // depth
                float zNear = minZ - (radius * backwardFactor + 80.0f);
                float zFar = maxZ + (radius * forwardFactor + 150.0f);

                // extra safety umum
                zFar += radius * 0.7f;
                zNear -= radius * 0.5f;

                // 🔥 tambahan khusus cascade 1
                if (i == 1)
                {
                    zFar += radius * 1.0f;
                    zNear -= radius * 0.7f;
                }


                Matrix4x4 lightProj = CreateOrthographicOffCenterOpenGL(
                    minX, maxX, minY, maxY, zNear, zFar);

                LightSpaceMatrices[i] = lightView * lightProj ;

                prevSplit = nextSplit;
            }
        }


        public void BindFramebuffer(int index)
        {
            if (index < 0 || index >= NumCascades) return;
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, FBOs[index]);
            GL.Viewport(0, 0, ShadowSize, ShadowSize);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthMask(true);
            GL.Clear(Const.GL_DEPTH_BUFFER_BIT);
        }
        public unsafe void Dispose()
        {
            fixed (uint* pFbos = FBOs)
            {
                GL.DeleteFramebuffers(NumCascades, pFbos);
            }
            fixed (uint* pTexs = ShadowTextures)
            {
                GL.DeleteTextures(NumCascades, pTexs);
            }
        }
    }
}