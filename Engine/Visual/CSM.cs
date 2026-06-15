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

        public Vector3[][] OrthoCorners = new Vector3[NumCascades][];

        public float[] CascadeEnds = { 30.0f, 120.0f, 600.0f };

        public CSM(int shadowSize = 2046)
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

                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_DEPTH_COMPONENT32F,
                    ShadowSize, ShadowSize, 0,
                    Const.GL_DEPTH_COMPONENT, Const.GL_FLOAT, (void*)0);

                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);

                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_COMPARE_MODE, (int)Const.GL_NONE);

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

            // Cascade terakhir harus sama dengan far plane kamera
            CascadeEnds[NumCascades - 1] = camera.FarDist;

            float prevSplit = camera.NearDist;

            for (int i = 0; i < NumCascades; i++)
            {
                float nextSplit = CascadeEnds[i];
                float fovRad = Helpers.OGLMath.ToRadians(camera.FoV);

                Matrix4x4 splitProj = Matrix4x4.CreatePerspectiveFieldOfView(
                    fovRad, camera.GetAspect(), prevSplit, nextSplit);

                Vector3[] corners = TerrainChunk.GetFrustumCorners(camera.GetViewMatrix(), splitProj);

                Vector3 center = Vector3.Zero;
                for (int j = 0; j < 8; j++)
                    center += corners[j];
                center /= 8f;

                float radius = 0f;
                for (int j = 0; j < 8; j++)
                    radius = MathF.Max(radius, (corners[j] - center).Length());

                radius = MathF.Ceiling(radius * 32f) / 32f;

                Vector3 up = MathF.Abs(Vector3.Dot(lightDir, Vector3.UnitY)) > 0.99f
                    ? Vector3.UnitZ : Vector3.UnitY;

                Vector3 lightPos = center + lightDir * radius;

                Matrix4x4 lightView = Matrix4x4.CreateLookAt(lightPos, center, up);

                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;

                for (int j = 0; j < 8; j++)
                {
                    Vector3 lp = Vector3.Transform(corners[j], lightView);
                    minX = MathF.Min(minX, lp.X);
                    maxX = MathF.Max(maxX, lp.X);
                    minY = MathF.Min(minY, lp.Y);
                    maxY = MathF.Max(maxY, lp.Y);
                    minZ = MathF.Min(minZ, lp.Z);
                    maxZ = MathF.Max(maxZ, lp.Z);
                }
                // FIX: padding XY besar supaya tubuh tidak terpotong
                float padXY = radius * 0.75f;
                if (padXY < 15.0f) padXY = 15.0f;

                // FIX: padding Z belakang lebih besar untuk objek tinggi
                float padZFront = radius * 3.0f;
                float padZBack = radius * 6.0f + (i * 40.0f);


                float zNear = minZ - padZBack;
                float zFar = maxZ + padZFront;

                // FIX: clamp Z-range supaya precision cascade jauh tidak hancur
                float maxRange = radius * 10.0f;
                float centerZ = 0.5f * (zNear + zFar);
                float half = 0.5f * maxRange;

                zNear = centerZ - half;
                zFar = centerZ + half;

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

                Matrix4x4.Invert(lightView, out Matrix4x4 invView);
                for (int k = 0; k < 8; k++)
                    ls[k] = Vector3.Transform(ls[k], invView);

                OrthoCorners[i] = ls;

                Matrix4x4 lightProj = CreateOrthographicOffCenterOpenGL( minX, maxX, minY, maxY, zNear, zFar);

                // Pipeline kamu: row-major → view * proj
                LightSpaceMatrices[i] = lightView * lightProj;

                prevSplit = nextSplit;
            }
        }

        public static Plane[]? BuildPlanesFromCorners(Vector3[] c)
        {
            if (c == null || c.Length < 8) return null;

            var planes = new Plane[6];

            static Plane MakePlane(Vector3 a, Vector3 b, Vector3 d, Vector3 insidePoint)
            {
                var n = Vector3.Normalize(Vector3.Cross(b - a, d - a));
                float D = -Vector3.Dot(n, a);

                Plane p = new Plane(n, D);

                float dist = Vector3.Dot(p.Normal, insidePoint) + p.D;
                if (dist < 0)
                {
                    p.Normal = -p.Normal;
                    p.D = -p.D;
                }

                return Plane.Normalize(p);
            }

            Vector3 center = (c[0] + c[6]) * 0.5f;

            planes[0] = MakePlane(c[1], c[2], c[6], center);
            planes[1] = MakePlane(c[3], c[0], c[4], center);
            planes[2] = MakePlane(c[0], c[1], c[5], center);
            planes[3] = MakePlane(c[2], c[3], c[7], center);
            planes[4] = MakePlane(c[0], c[3], c[2], center);
            planes[5] = MakePlane(c[5], c[6], c[7], center);

            return planes;
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
