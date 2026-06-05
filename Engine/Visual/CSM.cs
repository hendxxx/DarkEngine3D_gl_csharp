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

        // NEW: Ortho corners per cascade (world space)
        public Vector3[][] OrthoCorners = new Vector3[NumCascades][];

        public float[] CascadeEnds = { 20.0f, 80.0f, 300.0f };

        public CSM(int shadowSize = 2048)
        {
            ShadowSize = shadowSize;
            CreateShadowMaps();
        }

        private unsafe void CreateShadowMaps()
        {
            fixed (uint* pFbos = FBOs)
                GL.GenFramebuffers(NumCascades, pFbos);

            fixed (uint* pTexs = ShadowTextures)
                GL.GenTextures(NumCascades, pTexs);

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

                float[] borderColor = { 1, 1, 1, 1 };
                fixed (float* pBorder = borderColor)
                    GL.TexParameterfv(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_BORDER_COLOR, pBorder);

                GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_ATTACHMENT,
                    Const.GL_TEXTURE_2D, ShadowTextures[i], 0);

                uint none = 0;
                GL.DrawBuffers(0, &none);
                GL.ReadBuffer(Const.GL_NONE);
            }

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
        }

        private static Matrix4x4 CreateOrthographicOffCenterOpenGL(
            float left, float right, float bottom, float top, float zNear, float zFar)
        {
            Matrix4x4 m = Matrix4x4.Identity;
            m.M11 = 2f / (right - left);
            m.M22 = 2f / (top - bottom);
            m.M33 = -2f / (zFar - zNear);
            m.M41 = -(right + left) / (right - left);
            m.M42 = -(top + bottom) / (top - bottom);
            m.M43 = -(zFar + zNear) / (zFar - zNear);
            return m;
        }

        public void UpdateMatrices(Camera camera, Vector3 lightDir)
        {
            lightDir = Vector3.Normalize(lightDir);

            float prevSplit = camera.NearDist;

            for (int i = 0; i < NumCascades; i++)
            {
                float nextSplit = CascadeEnds[i];

                Matrix4x4 splitProj = Matrix4x4.CreatePerspectiveFieldOfView(
                    camera.FoV, camera.GetAspect(), prevSplit, nextSplit);

                Vector3[] corners = TerrainChunk.GetFrustumCorners(
                    camera.GetViewMatrix(), splitProj);

                // === CENTER ===
                Vector3 center = Vector3.Zero;
                for (int j = 0; j < 8; j++)
                    center += corners[j];
                center /= 8f;

                // === RADIUS ===
                float radius = 0f;
                for (int j = 0; j < 8; j++)
                    radius = MathF.Max(radius, (corners[j] - center).Length());

                radius = MathF.Ceiling(radius * 16f) / 16f;

                // ✅ extra scale supaya tidak kepotong
                float radiusScale = 1.25f;
                if (i == 1) radiusScale = 1.4f;
                if (i == 2) radiusScale = 1.6f; // 🔥 cascade jauh lebih besar

                radius *= radiusScale;

                // === LIGHT VIEW ===
                Vector3 up = MathF.Abs(Vector3.Dot(lightDir, Vector3.UnitY)) > 0.99f
                    ? Vector3.UnitZ : Vector3.UnitY;

                // ✅ lebih jauh supaya coverage cukup
                Vector3 lightPos = center + lightDir * radius * 4.0f;

                Matrix4x4 lightView = Matrix4x4.CreateLookAt(lightPos, center, up);

                // === XY BOUNDS (LIGHT SPACE AABB) ===
                float minX = float.MaxValue;
                float maxX = float.MinValue;
                float minY = float.MaxValue;
                float maxY = float.MinValue;

                for (int j = 0; j < 8; j++)
                {
                    Vector3 lp = Vector3.Transform(corners[j], lightView);
                    minX = MathF.Min(minX, lp.X);
                    maxX = MathF.Max(maxX, lp.X);
                    minY = MathF.Min(minY, lp.Y);
                    maxY = MathF.Max(maxY, lp.Y);
                }

                // ✅ padding XY (hilangkan ring / circle artifact)
                float padding = radius * 0.25f;
                minX -= padding;
                maxX += padding;
                minY -= padding;
                maxY += padding;

                // === Z RANGE (REAL SCENE BASED) ===
                float minZ = float.MaxValue;
                float maxZ = float.MinValue;

                for (int j = 0; j < 8; j++)
                {
                    Vector3 lp = Vector3.Transform(corners[j], lightView);
                    minZ = MathF.Min(minZ, lp.Z);
                    maxZ = MathF.Max(maxZ, lp.Z);
                }

                // ✅ asymmetric depth (shadow jatuh ke depan)
                float forwardFactor = 3.0f;
                float backwardFactor = 1.0f;

                if (i == 1)
                {
                    forwardFactor = 4.0f;
                    backwardFactor = 1.3f;
                }

                if (i == 2) // 🔥 cascade jauh
                {
                    forwardFactor = 6.0f;
                    backwardFactor = 2.5f;
                }

                float zNear = minZ - (radius * backwardFactor + 0.0f);
                float zFar = maxZ + (radius * forwardFactor + 2500.0f);

                // ✅ extra safety supaya tidak kepotong
                zFar += radius * 1.0f;
                zNear -= radius * 0.7f;

                // === BUILD ORTHO CORNERS (WORLD SPACE for debug & culling) ===
                Vector3[] ls =
                {
                    new(minX, minY, zNear),
                    new(maxX, minY, zNear),
                    new(maxX, maxY, zNear),
                    new(minX, maxY, zNear),

                    new(minX, minY, zFar),
                    new(maxX, minY, zFar),
                    new(maxX, maxY, zFar),
                    new(minX, maxY, zFar),
                };

                Matrix4x4 invView = Matrix4x4.Invert(lightView, out var inv)
                    ? inv : Matrix4x4.Identity;

                for (int k = 0; k < 8; k++)
                    ls[k] = Vector3.Transform(ls[k], invView);

                OrthoCorners[i] = ls;

                // === FINAL PROJECTION ===
                Matrix4x4 lightProj = CreateOrthographicOffCenterOpenGL(
                    minX, maxX, minY, maxY, zNear, zFar);

                // ✅ sesuai engine kamu (ROW MAJOR)
                LightSpaceMatrices[i] = lightView * lightProj;

                prevSplit = nextSplit;
            }
        }


        public void BindFramebuffer(int index)
        {
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, FBOs[index]);
            GL.Viewport(0, 0, ShadowSize, ShadowSize);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthMask(true);
            GL.Clear(Const.GL_DEPTH_BUFFER_BIT);
        }

        public unsafe void Dispose()
        {
            fixed (uint* pFbos = FBOs)
                GL.DeleteFramebuffers(NumCascades, pFbos);

            fixed (uint* pTexs = ShadowTextures)
                GL.DeleteTextures(NumCascades, pTexs);
        }
    }
}
