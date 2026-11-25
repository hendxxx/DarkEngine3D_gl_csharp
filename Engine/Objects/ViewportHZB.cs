using DarkEngine3D_gl_csharp.Engine.Libs;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;
using System;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Objects
{
    /// <summary>
    /// Optional "OC: HZB" occlusion source — the max-depth pyramid (hi-Z) of the PREVIOUS
    /// viewport frame, tested on the CPU against object AABBs.
    /// <para/>
    /// Why: the analytic hull test (<see cref="ViewportOcclusion"/>) can only hide something
    /// behind the few boxes flagged as Occluder; a depth pyramid is ground truth, so terrain,
    /// GLB meshes and every other drawn pixel occlude for free (the same idea as the
    /// nvpro-samples gl_occlusion_culling sample, whose "HiZ (last frame)" mode is what this
    /// implements; the hull test stays the default).
    /// <para/>
    /// Pipeline, once per frame after the viewport draw:
    /// <list type="number">
    /// <item>copy the viewport depth (single sample) into our own depth texture with
    /// glBlitFramebuffer — a depth blit is invalid between different sample counts, which is
    /// why OC: HZB makes the viewport FBO single-sample;</item>
    /// <item>downsample with MAX (4x4 then 2x2 passes) into a level chain — MAX keeps the
    /// test conservative: a tile can only claim "farther than this" if everything inside it
    /// is farther;</item>
    /// <item>read the small pyramid back (base = window/4) and test AABBs on the CPU: the
    /// box's NEAREST depth vs the MAX depth of the tiles its screen rect touches. Occluded
    /// only when the nearest point is still behind every tile it covers.</item>
    /// </list>
    /// One frame of latency is deliberate (the sample's "Last Frame" mode): no readback
    /// stall inside the cull loop, and the depth is exactly what the viewport rendered.
    /// </summary>
    public static unsafe class ViewportHZB
    {
        /// <summary>Pyramid base = window / 4 (an editor-sized 1920x1080 frame gives 480x270,
        /// ~690 KB of readback for the whole chain).</summary>
        private const int BaseDivisor = 4;
        private const int MaxLevels = 16;

        private static uint _depthTex, _depthFbo;
        private static int _w, _h;                       // size of the depth copy (viewport)
        private static readonly uint[] _levelTex = new uint[MaxLevels];
        private static readonly int[] _levelW = new int[MaxLevels];
        private static readonly int[] _levelH = new int[MaxLevels];
        private static readonly float[][] _cpu = new float[MaxLevels][];
        private static int _levelCount;
        private static uint _scratchFbo;                 // one FBO, re-attached per level
        private static uint _quadVao, _quadVbo;
        private static Shader? _down4, _max2;
        private static bool _shadersTried, _blitWarned, _completeWarned, _loggedActive;
        private static int _captureFrame = -1;

        /// <summary>True when a pyramid of a recent frame is available for testing. A stale
        /// capture (more than two frames old) is refused so nothing is culled from ancient
        /// depth data after the viewport stops rendering.</summary>
        public static bool Valid =>
            _levelCount > 0 && _captureFrame >= 0 && Glfw.FrameId - _captureFrame <= 2;

        /// <summary>Glfw.FrameId of the captured frame (-1 = never captured).</summary>
        public static int CaptureFrame => _captureFrame;

        /// <summary>Number of pyramid levels (0 = not built yet).</summary>
        public static int LevelCount => _levelCount;

        /// <summary>Base level size in pixels (0,0 = not built yet).</summary>
        public static (int Width, int Height) BaseSize => (_levelW.Length > 0 ? _levelW[0] : 0, _levelH[0]);

        /// <summary>Copy the viewport depth of THIS frame into the pyramid used next frame.
        /// <paramref name="sourceFbo"/> must be the single-sample framebuffer the viewport
        /// just rendered into (it must have a depth attachment). Returns false when the copy
        /// or the shaders are unavailable (callers then keep using the hull test).</summary>
        public static bool Capture(uint sourceFbo, int width, int height)
        {
            if (sourceFbo == 0 || width <= 0 || height <= 0) return false;
            if (!Ensure(width, height)) return false;

            // ── 1. depth copy: viewport depth → our DEPTH24_STENCIL8 texture ──
            // Identical internal format on both sides (no conversion is allowed for depth
            // blits) and identical sample counts (single-sample enforced by SceneManager).
            GL.BindFramebuffer(Const.GL_READ_FRAMEBUFFER, sourceFbo);
            GL.BindFramebuffer(Const.GL_DRAW_FRAMEBUFFER, _depthFbo);
            bool copied = GL.BlitFramebuffer(0, 0, _w, _h, 0, 0, _w, _h,
                Const.GL_DEPTH_BUFFER_BIT, Const.GL_NEAREST);
            GL.BindFramebuffer(Const.GL_READ_FRAMEBUFFER, 0);
            GL.BindFramebuffer(Const.GL_DRAW_FRAMEBUFFER, 0);
            if (!copied)
            {
                if (!_blitWarned)
                {
                    Console.WriteLine("[OC/HZB] glBlitFramebuffer is unavailable — OC: HZB stays inactive (hull test used).");
                    _blitWarned = true;
                }
                _levelCount = 0;
                return false;
            }

            // ── 2. downsample with MAX into the level chain ──
            bool depthWasOn = GL.IsEnabled(Const.GL_DEPTH_TEST);
            bool cullWasOn = GL.IsEnabled(Const.GL_CULL_FACE);
            bool blendWasOn = GL.IsEnabled(Const.GL_BLEND);
            GL.Disable(Const.GL_DEPTH_TEST);
            GL.Disable(Const.GL_CULL_FACE);
            GL.Disable(Const.GL_BLEND);
            GL.BindVertexArray(_quadVao);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _scratchFbo);

            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                Const.GL_TEXTURE_2D, _levelTex[0], 0);
            GL.Viewport(0, 0, _levelW[0], _levelH[0]);
            GL.UseProgram(_down4!.ProgramId);
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, _depthTex);
            GL.Uniform1i(GL.GetUniformLocation(_down4.ProgramId, "srcTex"), 0);
            GL.Uniform2f(GL.GetUniformLocation(_down4.ProgramId, "srcSize"), _w, _h);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);

            for (int i = 1; i < _levelCount; i++)
            {
                GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                    Const.GL_TEXTURE_2D, _levelTex[i], 0);
                GL.Viewport(0, 0, _levelW[i], _levelH[i]);
                GL.UseProgram(_max2!.ProgramId);
                GL.BindTexture(Const.GL_TEXTURE_2D, _levelTex[i - 1]);
                GL.Uniform1i(GL.GetUniformLocation(_max2.ProgramId, "srcTex"), 0);
                GL.Uniform2f(GL.GetUniformLocation(_max2.ProgramId, "srcSize"),
                    _levelW[i - 1], _levelH[i - 1]);
                GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            }

            GL.BindVertexArray(0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            if (depthWasOn) GL.Enable(Const.GL_DEPTH_TEST);
            if (cullWasOn) GL.Enable(Const.GL_CULL_FACE);
            if (blendWasOn) GL.Enable(Const.GL_BLEND);

            // ── 3. read the pyramid back (small: base is window/4) ──
            for (int i = 0; i < _levelCount; i++)
            {
                GL.BindTexture(Const.GL_TEXTURE_2D, _levelTex[i]);
                fixed (float* p = _cpu[i])
                    GL.GetTexImage(Const.GL_TEXTURE_2D, 0, Const.GL_RED, Const.GL_FLOAT, p);
            }
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            _captureFrame = Glfw.FrameId;
            if (!_loggedActive)
            {
                _loggedActive = true;
                Console.WriteLine($"[OC/HZB] active — {_levelCount} level(s), base {_levelW[0]}x{_levelH[0]} " +
                    $"from a {_w}x{_h} viewport depth copy (previous-frame pyramid, tested on the CPU).");
            }
            return true;
        }

        /// <summary>Conservative hi-Z test: is <paramref name="bounds"/> completely behind the
        /// captured depth? <paramref name="viewProj"/> must be the view-projection of the
        /// camera that produced the depth (the pass's own camera), otherwise the answer is
        /// meaningless — callers check that before calling.
        /// <para/>
        /// The box's nearest corner depth is compared with the MAX depth of every pyramid
        /// texel its screen rect touches, at the mip level whose tile is at least as large as
        /// the rect. If even the nearest point is farther than the farthest occluder in those
        /// tiles, no pixel of the box can pass the depth test.</summary>
        public static bool IsOccluded(in AABB bounds, in Matrix4x4 viewProj)
        {
            if (!Valid) return false;

            Vector3 min = bounds.Min;
            Vector3 max = bounds.Max;
            float nearest = float.MaxValue;
            float uMin = float.MaxValue, uMax = float.MinValue;
            float vMin = float.MaxValue, vMax = float.MinValue;

            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3(
                    (i & 1) == 0 ? min.X : max.X,
                    (i & 2) == 0 ? min.Y : max.Y,
                    (i & 4) == 0 ? min.Z : max.Z);
                var clip = Vector4.Transform(new Vector4(p, 1f), viewProj);
                //  w <= 0: at/behind the eye plane — never claim such a box is hidden.
                if (clip.W <= 1e-5f) return false;

                float ndcZ = clip.Z / clip.W;
                float depth = ndcZ * 0.5f + 0.5f;             // GL window depth [0,1]
                if (depth < nearest) nearest = depth;

                float sx = (clip.X / clip.W * 0.5f + 0.5f) * _levelW[0];
                float sy = (clip.Y / clip.W * 0.5f + 0.5f) * _levelH[0];
                if (sx < uMin) uMin = sx;
                if (sx > uMax) uMax = sx;
                if (sy < vMin) vMin = sy;
                if (sy > vMax) vMax = sy;
            }

            //  Straddles / crosses the near plane, or behind the far plane: let the frustum
            //  test own that decision.
            if (nearest <= 0f || nearest >= 1f) return false;

            return TestScreenRect(uMin, uMax, vMin, vMax, nearest);
        }

        /// <summary>Conservative hi-Z test for a SPHERE occludee. Same idea as the AABB test,
        /// but the covered region is the sphere's projected DISK instead of the 8 AABB
        /// corners: a sphere hidden behind a hill was never culled from up close because the
        /// corners of its bounding box projected past the ridge into the sky. Distance is not
        /// part of the decision — only whether the sphere's deepest-covered tile still shows
        /// something nearer.
        public static bool IsOccludedSphere(Vector3 center, float radius, in Matrix4x4 viewProj)
        {
            if (!Valid || radius <= 0f) return false;

            var s = ViewportOcclusion.ProjectSphere(viewProj, center, radius);
            if (!s.Valid) return false;

            float nearest = s.Nearest * 0.5f + 0.5f;      // NDC → GL window depth [0,1]
            if (nearest <= 0f || nearest >= 1f) return false;

            // NDC centre/half-extents → base-level pixels (base = window/4).
            float cx = (s.Cx * 0.5f + 0.5f) * _levelW[0];
            float cy = (s.Cy * 0.5f + 0.5f) * _levelH[0];
            float hx = s.Rx * 0.5f * _levelW[0];
            float hy = s.Ry * 0.5f * _levelH[0];

            return TestScreenRect(cx - hx, cx + hx, cy - hy, cy + hy, nearest);
        }

        private static bool TestScreenRect(float uMin, float uMax, float vMin, float vMax, float nearest)
        {
            float span = MathF.Max(uMax - uMin, vMax - vMin);
            int level = 0;
            while (level + 1 < _levelCount && (1 << level) < span) level++;

            int lw = _levelW[level], lh = _levelH[level];
            if (lw <= 0 || lh <= 0) return false;

            //  Level-local coordinates (base → level scale, not 1/2^level: the base size is
            //  window/4 and need not be a power of two).
            float scaleX = (float)lw / _levelW[0];
            float scaleY = (float)lh / _levelH[0];
            int x0 = Math.Clamp((int)MathF.Floor(uMin * scaleX), 0, lw - 1);
            int x1 = Math.Clamp((int)MathF.Floor(uMax * scaleX), 0, lw - 1);
            int y0 = Math.Clamp((int)MathF.Floor(vMin * scaleY), 0, lh - 1);
            int y1 = Math.Clamp((int)MathF.Floor(vMax * scaleY), 0, lh - 1);

            float[] tile = _cpu[level];
            float tileMax = 0f;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float t = tile[y * lw + x];
                    if (t > tileMax) tileMax = t;
                }

            return nearest > tileMax + 1e-4f;
        }

        // ───────────────────────────── plumbing ─────────────────────────────

        private static bool Ensure(int width, int height)
        {
            if (!EnsureShaders()) return false;
            if (_depthTex != 0 && _w == width && _h == height && _levelCount > 0) return true;

            Destroy();

            int w = width, h = height;
            uint depthTex = 0;
            GL.GenTextures(1, &depthTex);
            GL.BindTexture(Const.GL_TEXTURE_2D, depthTex);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_DEPTH24_STENCIL8, w, h, 0,
                Const.GL_DEPTH_STENCIL, Const.GL_UNSIGNED_INT_24_8, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_NEAREST);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);

            uint depthFbo = 0;
            GL.GenFramebuffers(1, &depthFbo);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, depthFbo);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_DEPTH_STENCIL_ATTACHMENT,
                Const.GL_TEXTURE_2D, depthTex, 0);
            GL.DrawBuffer(Const.GL_NONE);
            uint status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            if (status != Const.GL_FRAMEBUFFER_COMPLETE)
            {
                if (!_completeWarned)
                {
                    Console.WriteLine($"[OC/HZB] depth FBO incomplete: 0x{status:X} — OC: HZB stays inactive.");
                    _completeWarned = true;
                }
                GL.DeleteFramebuffers(1, &depthFbo);
                GL.DeleteTextures(1, &depthTex);
                return false;
            }

            uint scratchFbo = 0;
            GL.GenFramebuffers(1, &scratchFbo);
            if (_quadVao == 0) CreateQuad();

            int bw = Math.Max(1, (w + BaseDivisor - 1) / BaseDivisor);
            int bh = Math.Max(1, (h + BaseDivisor - 1) / BaseDivisor);
            int levels = 0;
            int lw = bw, lh = bh;
            while (levels < MaxLevels)
            {
                uint tex = 0;
                GL.GenTextures(1, &tex);
                GL.BindTexture(Const.GL_TEXTURE_2D, tex);
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_R32F, lw, lh, 0,
                    Const.GL_RED, Const.GL_FLOAT, (void*)0);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_NEAREST);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_NEAREST);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
                _levelTex[levels] = tex;
                _levelW[levels] = lw;
                _levelH[levels] = lh;
                _cpu[levels] = new float[lw * lh];
                levels++;
                if (lw == 1 && lh == 1) break;
                lw = Math.Max(1, (lw + 1) / 2);
                lh = Math.Max(1, (lh + 1) / 2);
            }
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            _depthTex = depthTex;
            _depthFbo = depthFbo;
            _scratchFbo = scratchFbo;
            _w = w;
            _h = h;
            _levelCount = levels;
            return true;
        }

        private static bool EnsureShaders()
        {
            if (_shadersTried) return _down4 != null && _max2 != null;
            _shadersTried = true;

            const string vs = """
                #version 330 core
                layout (location = 0) in vec2 aPos;
                layout (location = 1) in vec2 aUV;
                void main()
                {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                }
                """;

            //  Base level: MAX of the 4x4 block of viewport depth this texel covers.
            const string fsDown4 = """
                #version 330 core
                out vec4 FragColor;
                uniform sampler2D srcTex;
                uniform vec2 srcSize;
                void main()
                {
                    ivec2 smax = ivec2(srcSize) - ivec2(1);
                    ivec2 base = ivec2(gl_FragCoord.xy) * 4;
                    float m = 0.0;
                    for (int y = 0; y < 4; ++y)
                        for (int x = 0; x < 4; ++x)
                            m = max(m, texelFetch(srcTex, min(base + ivec2(x, y), smax), 0).r);
                    FragColor = vec4(m, 0.0, 0.0, 1.0);
                }
                """;

            //  Coarser levels: MAX of the 2x2 block below.
            const string fsMax2 = """
                #version 330 core
                out vec4 FragColor;
                uniform sampler2D srcTex;
                uniform vec2 srcSize;
                void main()
                {
                    ivec2 smax = ivec2(srcSize) - ivec2(1);
                    ivec2 base = ivec2(gl_FragCoord.xy) * 2;
                    float m = 0.0;
                    for (int y = 0; y < 2; ++y)
                        for (int x = 0; x < 2; ++x)
                            m = max(m, texelFetch(srcTex, min(base + ivec2(x, y), smax), 0).r);
                    FragColor = vec4(m, 0.0, 0.0, 1.0);
                }
                """;

            _down4 = new Shader(vs, fsDown4);
            _max2 = new Shader(vs, fsMax2);
            if (_down4.ProgramId == 0 || _max2.ProgramId == 0)
            {
                Console.WriteLine("[OC/HZB] downsample shader failed to compile — OC: HZB stays inactive.");
                _down4 = null;
                _max2 = null;
                return false;
            }
            return true;
        }

        /// <summary>Full-screen quad (pos2 + uv2, CCW) matching every other post quad.</summary>
        private static void CreateQuad()
        {
            float[] quad =
            [
                -1f, -1f,  0f, 0f,
                 1f, -1f,  1f, 0f,
                 1f,  1f,  1f, 1f,

                -1f, -1f,  0f, 0f,
                 1f,  1f,  1f, 1f,
                -1f,  1f,  0f, 1f,
            ];

            uint vao = 0, vbo = 0;
            GL.GenVertexArrays(1, &vao);
            GL.GenBuffers(1, &vbo);
            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
            fixed (float* q = quad)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(quad.Length * sizeof(float)), q, Const.GL_STATIC_DRAW);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
            GL.BindVertexArray(0);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, 0);

            _quadVao = vao;
            _quadVbo = vbo;
        }

        private static void Destroy()
        {
            for (int i = 0; i < MaxLevels; i++)
            {
                if (_levelTex[i] != 0)
                {
                    fixed (uint* p = &_levelTex[i]) GL.DeleteTextures(1, p);
                    _levelTex[i] = 0;
                }
                _levelW[i] = _levelH[i] = 0;
                _cpu[i] = null!;
            }
            if (_depthFbo != 0) { fixed (uint* p = &_depthFbo) GL.DeleteFramebuffers(1, p); _depthFbo = 0; }
            if (_depthTex != 0) { fixed (uint* p = &_depthTex) GL.DeleteTextures(1, p); _depthTex = 0; }
            if (_scratchFbo != 0) { fixed (uint* p = &_scratchFbo) GL.DeleteFramebuffers(1, p); _scratchFbo = 0; }
            _levelCount = 0;
            _w = _h = 0;
            _captureFrame = -1;
        }
    }
}
