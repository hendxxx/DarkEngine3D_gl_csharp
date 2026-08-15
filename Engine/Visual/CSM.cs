using DarkEngine3D_gl_csharp.Engine.Libs;
using System;
using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Terrains;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    public class CSM : IDisposable
    {
        public const int NumCascades = 3;

        /// <summary>Cascade resolutions — taken from ShadowSettings at construction and
        /// refreshed automatically when the Shadow panel changes the quality preset.</summary>
        private int[] CascadeSizes = Config.ShadowSettings.CascadeSizes;

        public int ShadowSize { get; private set; }

        public uint[] FBOs = new uint[NumCascades];
        public uint[] ShadowTextures = new uint[NumCascades];
        public Matrix4x4[] LightSpaceMatrices = new Matrix4x4[NumCascades];
        public Vector3[][] OrthoCorners = new Vector3[NumCascades][];

        // Copy the cascade split distances into a private array so every CSM instance
        // (GameScene / MainMenuScene / SceneManager editor) has its own copy instead of
        // mutating the shared static ShadowSettings.CascadeLayer.
        public float[] CascadeEnds = (float[])Config.ShadowSettings.CascadeLayer.Clone();

        /// <summary>World depth range (zFar − zNear) of the LAST-computed ortho projection
        /// per cascade, updated by every UpdateMatrices call. The fragment shaders divide
        /// their NDC bias by this so the WORLD-space bias offset stays consistent across
        /// cascades — a far cascade's ortho depth range is 10×+ larger than the near one's,
        /// so a fixed NDC bias would push shadows tens of world units away and they would
        /// vanish at distance. Shared across all scenes (set by whichever CSM renders last).</summary>
        public static float[] LastDepthRanges = [1f, 1f, 1f];

        /// <summary>World-space size of ONE shadow-map texel per cascade, from the LAST
        /// UpdateMatrices call (texel = XY ortho extent ÷ resolution, max of X/Y so the
        /// bias stays conservative). The fragment shaders scale their bias by
        /// texel_i / texel_0 so the WORLD bias offset is proportional to the local texel
        /// size — i.e. a constant number of texels at every distance — instead of the old
        /// fixed 1.5×/3× multipliers, which under-shot the far cascades (sub-texel bias →
        /// acne) and over-shot them at close range. Shared across scenes like
        /// <see cref="LastDepthRanges"/>.</summary>
        public static float[] LastTexelWorld = [1f, 1f, 1f];

        /// <summary>Precomputed texel ratio per cascade (LastTexelWorld[i] / LastTexelWorld[0],
        /// floored at 1, clamped at 128). Read by ShadowUniforms.UploadNormalBias so the
        /// vertex-extrusion bias scales with the active cascade's texel size without dividing
        /// on every upload.</summary>
        public static float[] LastTexelScale = [1f, 1f, 1f];

        /// <summary>Index of the cascade whose FBO is currently bound (set by
        /// <see cref="BindFramebuffer"/>). ShadowUniforms.UploadNormalBias reads it to scale
        /// the vertex-extrusion bias by that cascade's texel ratio — so the WORLD extrusion
        /// stays a constant texel count at every distance, matching the texel-proportional
        /// fragment bias. Default 0 = no scaling (backward compatible).</summary>
        public static int ActiveCascadeIndex = 0;

        // ShadowSettings versions this instance was built with — used to detect when the
        // Shadow panel changed cascade sizes/splits (full rebuild) or just the texture
        // filter (cheap TexParameteri) so changes apply without scene wiring.
        private int _sizeVersion = -1;
        private int _filterVersion = -1;

        public CSM(int shadowSize = 1024)
        {
            ShadowSize = shadowSize;
            // Clamp the requested cascade sizes to what this GPU can actually allocate
            // (GL_MAX_TEXTURE_SIZE) — otherwise the Ultra preset's 8192² maps silently
            // fail on smaller GPUs and every shadow disappears with no error.
            CascadeSizes = ClampToGpu(Config.ShadowSettings.CascadeSizes);
            _sizeVersion = Config.ShadowSettings.Version;
            _filterVersion = Config.ShadowSettings.FilterVersion;
            CreateShadowMaps();
        }

        /// <summary>GPU max texture size (queried once). At least 1024 so tiny/virtual
        /// drivers still get usable maps.</summary>
        private static int? _maxTexSize;
        private static int MaxTexSize => _maxTexSize ??= QueryMaxTexSize();

        private static unsafe int QueryMaxTexSize()
        {
            int v = 4096;
            GL.GetIntegerv(Const.GL_MAX_TEXTURE_SIZE, &v);
            return Math.Max(1024, v);
        }

        private static int[] ClampToGpu(int[] sizes)
        {
            if (sizes == null) return [1024, 1024, 1024];
            var clamped = new int[sizes.Length];
            for (int i = 0; i < sizes.Length; i++)
                clamped[i] = Math.Min(sizes[i], MaxTexSize);
            return clamped;
        }

        /// <summary>Resource-free constructor — allocates no FBOs/textures. Used by
        /// <see cref="LocalLightShadow"/> as a read-only data shim: the caster renderers
        /// only read LightSpaceMatrices / OrthoCorners / CascadeEnds, never bind this
        /// instance's FBOs, so no GPU allocations are needed.</summary>
        protected CSM()
        {
            ShadowSize = 16;
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
                    CascadeSizes[i],
                    CascadeSizes[i],
                    0,
                    Const.GL_DEPTH_COMPONENT,
                    Const.GL_FLOAT,
                    (void*)0);

                // Filtering depth map. LINEAR cocok kalau shadow di-sample manual (PCF/PCSS di shader);
                // the toggle lives in the Shadow Settings panel (ShadowSettings.LinearShadowMap).
                int depthFilter = Config.ShadowSettings.LinearShadowMap
                    ? (int)Const.GL_LINEAR
                    : (int)Const.GL_NEAREST;
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, depthFilter);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, depthFilter);

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

                uint status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
                if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                {
                    Console.WriteLine($"[CSM] Shadow FBO cascade {i} incomplete ({CascadeSizes[i]}²): 0x{status:X} — shadows will be broken; lower the quality preset.");
                }
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

        /// <summary>Rebuild the depth maps (or re-apply the filter) when the Shadow panel
        /// changes cascade sizes / splits / filtering. Called at the top of UpdateMatrices
        /// so every scene's shadow pass picks up changes on the next frame automatically.</summary>
        private unsafe void EnsureCurrent()
        {
            if (_sizeVersion != Config.ShadowSettings.Version)
            {
                CascadeSizes = ClampToGpu(Config.ShadowSettings.CascadeSizes);
                CascadeEnds = (float[])Config.ShadowSettings.CascadeLayer.Clone();
                Dispose();
                CreateShadowMaps();
                _sizeVersion = Config.ShadowSettings.Version;
                _filterVersion = Config.ShadowSettings.FilterVersion;
            }
            else if (_filterVersion != Config.ShadowSettings.FilterVersion)
            {
                ApplyFilter();
                _filterVersion = Config.ShadowSettings.FilterVersion;
            }
        }

        /// <summary>Apply the depth-map texture filter (NEAREST/LINEAR) without a rebuild.</summary>
        private unsafe void ApplyFilter()
        {
            int filter = Config.ShadowSettings.LinearShadowMap
                ? (int)Const.GL_LINEAR
                : (int)Const.GL_NEAREST;

            for (int i = 0; i < NumCascades; i++)
            {
                GL.BindTexture(Const.GL_TEXTURE_2D, ShadowTextures[i]);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, filter);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, filter);
            }
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        }

        public void UpdateMatrices(Camera camera, Vector3 lightDir)
        {
            EnsureCurrent();
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

                // Sertakan posisi kamera dalam XY extent agar objek di belakang
                // kamera (yang shadow-nya terlihat di depan) tidak di-cull.
                Vector3 camPosLS = Vector3.Transform(camera.Position, lightView);
                float camMargin = radius * 0.5f;
                minX = MathF.Min(minX, camPosLS.X - camMargin);
                maxX = MathF.Max(maxX, camPosLS.X + camMargin);
                minY = MathF.Min(minY, camPosLS.Y - camMargin);
                maxY = MathF.Max(maxY, camPosLS.Y + camMargin);

                // Padding XY agar receiver / caster di tepi tidak kepotong.
                float padXY = MathF.Max(radius * 0.75f, 15.0f);
                minX -= padXY;
                maxX += padXY;
                minY -= padXY;
                maxY += padXY;

                // Padding Z agar caster di luar split masih bisa nge-cast shadow ke area terlihat.
                // padZBack lebih besar: tangkap caster di balik bukit / jauh dari frustum
                float padZFront = radius * 2.0f;
                float padZBack  = radius * 8.0f + (i * 60.0f);

                float zNear = minZ - padZBack;
                float zFar  = maxZ + padZFront;

                // Jangan clamp maxRange terlalu ketat agar caster tidak terpotong.
                // Biarkan range Z penuh sesuai frustum + padding.
                // (Tidak ada clamping disini – precision tetap OK karena GL_DEPTH_COMPONENT32F)
                // Pastikan zNear tidak terlalu positif (bisa balik sign):
                if (zNear > zFar - 1.0f) zNear = zFar - 1.0f;

                // Expose the world depth range + texel size for the fragment bias scaling
                // (see LastDepthRanges / LastTexelWorld docs). Guarded so degenerate values
                // can never produce a division-by-zero in the shaders.
                LastDepthRanges[i] = MathF.Max(zFar - zNear, 1f);
                float texelX = (maxX - minX) / CascadeSizes[i];
                float texelY = (maxY - minY) / CascadeSizes[i];
                LastTexelWorld[i] = MathF.Max(MathF.Max(texelX, texelY), 1e-4f);
                // Precompute the texel ratio for the normal-bias scaling (floored at 1 so a
                // cascade never gets less extrusion than cascade 0; clamped at 8 so the world
                // extrusion stays ≤ ~0.25 m — higher would detach far shadows from their
                // casters (the bright peter-panning outline around objects).
                LastTexelScale[i] = MathF.Min(
                    MathF.Max(LastTexelWorld[i] / MathF.Max(LastTexelWorld[0], 1e-5f), 1f), 8f);

                // Stable CSM: snap CENTER ortho ke texel grid, lebih stabil dari snap min/max terpisah.
                float extentX = 0.5f * (maxX - minX);
                float extentY = 0.5f * (maxY - minY);
                float centerX = 0.5f * (minX + maxX);
                float centerY = 0.5f * (minY + maxY);

                // BUG FIX: gunakan CascadeSizes[i] bukan ShadowSize (default 1024)
                // supaya texel snap akurat → shadow tidak flicker saat kamera bergerak
                float texelSizeX = (extentX * 2.0f) / CascadeSizes[i];
                float texelSizeY = (extentY * 2.0f) / CascadeSizes[i];

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
            GL.Viewport(0, 0, CascadeSizes[index], CascadeSizes[index]);

            // Remember which cascade we're rendering so normal-bias uploads can scale to
            // its texel size (see ActiveCascadeIndex docs).
            ActiveCascadeIndex = index;

            GL.Enable(Const.GL_DEPTH_TEST);
            GL.DepthMask(true);

            // Shadow pass renders BOTH faces (culling disabled). The old CullFace(GL_FRONT)
            // (render back faces) is the classic anti-acne trick for CLOSED meshes, but it
            // culled single-sided CCW casters — editor plane/terrain, game terrain, skinned
            // GLB, and mirrored (negative-scale) objects — out of the depth map entirely, so
            // hills never cast shadows on objects behind them. With culling off, every caster
            // contributes; the depth test's LESS keeps the light-facing surface winning for
            // closed meshes (identical depth map to back-face culling), and anti-acne stays
            // under control via the normal-bias extrusion every shadow vertex shader applies.
            GL.Disable(Const.GL_CULL_FACE);

            GL.Clear(Const.GL_DEPTH_BUFFER_BIT);
        }

        /// <summary>Clear every cascade depth map to the maximum depth (1.0) so all shadow
        /// samples read as fully lit. Used when the viewport shadows toggle is OFF — the
        /// main shader still samples the maps, so they must be cleared instead of left stale
        /// (a stale map would keep showing last frame's shadows). NOTE: relies on the GL
        /// depth clear value being 1.0 (the default) — do not set glClearDepth elsewhere.
        /// Callers must re-bind their framebuffer + viewport after this (as the shadow
        /// pass path already does).</summary>
        public void ClearShadowMaps()
        {
            for (int i = 0; i < NumCascades; i++)
            {
                GL.BindFramebuffer(Const.GL_FRAMEBUFFER, FBOs[i]);
                GL.Viewport(0, 0, CascadeSizes[i], CascadeSizes[i]);
                GL.DepthMask(true);
                GL.Clear(Const.GL_DEPTH_BUFFER_BIT);
            }
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
        }

        public void UnbindFramebuffer(int viewportWidth, int viewportHeight)
        {
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.Viewport(0, 0, viewportWidth, viewportHeight);

            // Kembalikan state default umum.
            GL.CullFace(Const.GL_BACK);
        }

        public virtual unsafe void Dispose()
        {
            fixed (uint* pFbos = FBOs)
                GL.DeleteFramebuffers(NumCascades, pFbos);

            fixed (uint* pTexs = ShadowTextures)
                GL.DeleteTextures(NumCascades, pTexs);
        }
    }
}
