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

        // Pastikan Config.ShadowConfig.CascadeLayer minimal punya NumCascades elemen.
        public float[] CascadeEnds = Config.ShadowConfig.CascadeLayer;

        public CSM(int shadowSize = 1024)
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

                GL.TexImage2D(
                    Const.GL_TEXTURE_2D,
                    0,
                    (int)Const.GL_DEPTH_COMPONENT32F,
                    ShadowSize,
                    ShadowSize,
                    0,
                    Const.GL_DEPTH_COMPONENT,
                    Const.GL_FLOAT,
                    (void*)0);

                // Filtering depth map. LINEAR cocok kalau shadow di-sample manual (PCF/PCSS di shader).
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);

                // Depth map sebaiknya border = 1.0 (fully lit) saat sample keluar area.
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_BORDER);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_BORDER);

                float[] borderColor = { 1f, 1f, 1f, 1f };
                fixed (float* pBorder = borderColor)
                {
                    GL.TexParameterfv(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_BORDER_COLOR, pBorder);
                }

                // Manual compare di shader.
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_COMPARE_MODE, (int)Const.GL_NONE);

                GL.FramebufferTexture2D(
                    Const.GL_FRAMEBUFFER,
                    Const.GL_DEPTH_ATTACHMENT,
                    Const.GL_TEXTURE_2D,
                    ShadowTextures[i],
                    0);

                // FBO depth-only.
                uint none = Const.GL_NONE;
                GL.DrawBuffers(0, &none);
                GL.ReadBuffer(Const.GL_NONE);

                //uint status = GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
                //if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                //{
                //    Console.WriteLine($"[CSM] Shadow FBO {i} incomplete: 0x{status:X}");
                //}
            }

            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
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

            // Cascade terakhir menutup sampai far plane kamera.
            CascadeEnds[NumCascades - 1] = camera.FarDist;

            float prevSplit = camera.NearDist;
            Matrix4x4 cameraView = camera.GetViewMatrix();
            float fovRad = Helpers.OGLMath.ToRadians(camera.FoV);

            for (int i = 0; i < NumCascades; i++)
            {
                float nextSplit = CascadeEnds[i];

                Matrix4x4 splitProj = Matrix4x4.CreatePerspectiveFieldOfView(
                    fovRad,
                    camera.GetAspect(),
                    prevSplit,
                    nextSplit);

                Vector3[] corners = TerrainChunk.GetFrustumCorners(cameraView, splitProj);

                // Center frustum split.
                Vector3 center = Vector3.Zero;
                for (int j = 0; j < 8; j++)
                    center += corners[j];
                center /= 8f;

                // Sphere bound kasar agar orientasi - stabil saat kamera rotasi.
                float radius = 0f;
                for (int j = 0; j < 8; j++)
                    radius = MathF.Max(radius, Vector3.Distance(corners[j], center));

                // Sedikit quantize radius agar makin stabil.
                radius = MathF.Ceiling(radius * 32f) / 32f;

                Vector3 up = MathF.Abs(Vector3.Dot(lightDir, Vector3.UnitY)) > 0.99f
                    ? Vector3.UnitZ
                    : Vector3.UnitY;

                // Jika arah shadow terasa kebalik, ganti '+' menjadi '-'.
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

                // Padding XY agar receiver / caster di tepi tidak kepotong.
                float padXY = MathF.Max(radius * 0.75f, 15.0f);
                minX -= padXY;
                maxX += padXY;
                minY -= padXY;
                maxY += padXY;

                // Padding Z agar caster di luar split masih bisa nge-cast shadow ke area terlihat.
                float padZFront = radius * 3.0f;
                float padZBack = radius * 6.0f + (i * 40.0f);

                float zNear = minZ - padZBack;
                float zFar = maxZ + padZFront;

                // Clamp range Z supaya precision cascade jauh tidak hancur.
                float maxRange = radius * 10.0f; // tweakable bila caster jauh masih terpotong
                float centerZ = 0.5f * (zNear + zFar);
                float halfZ = 0.5f * maxRange;
                zNear = centerZ - halfZ;
                zFar = centerZ + halfZ;

                // Stable CSM: snap CENTER ortho ke texel grid, lebih stabil dari snap min/max terpisah.
                float extentX = 0.5f * (maxX - minX);
                float extentY = 0.5f * (maxY - minY);
                float centerX = 0.5f * (minX + maxX);
                float centerY = 0.5f * (minY + maxY);

                float texelSizeX = (extentX * 2.0f) / ShadowSize;
                float texelSizeY = (extentY * 2.0f) / ShadowSize;

                if (texelSizeX > 0.0f)
                    centerX = MathF.Floor(centerX / texelSizeX) * texelSizeX;
                if (texelSizeY > 0.0f)
                    centerY = MathF.Floor(centerY / texelSizeY) * texelSizeY;

                minX = centerX - extentX;
                maxX = centerX + extentX;
                minY = centerY - extentY;
                maxY = centerY + extentY;

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

                Matrix4x4 lightProj = CreateOrthographicOffCenterOpenGL(
                    minX, maxX, minY, maxY, zNear, zFar);

                // Pipeline ini pakai row-major dan di shader dikalikan terhadap worldPos.
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

            // Best practice umum untuk shadow pass directional light:
            // render front faces agar mengurangi shadow acne.
            GL.Enable(Const.GL_CULL_FACE);
            GL.CullFace(Const.GL_FRONT);
            GL.FrontFace(Const.GL_CCW);

            GL.Clear(Const.GL_DEPTH_BUFFER_BIT);
        }

        public void UnbindFramebuffer(int viewportWidth, int viewportHeight)
        {
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, viewportWidth, viewportHeight);

            // Kembalikan state default umum.
            GL.CullFace(Const.GL_BACK);
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
