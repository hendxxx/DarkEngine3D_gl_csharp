using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Visual;
using System;

namespace DarkEngine3D_gl_csharp.Engine.Visual.PostProcessing
{
    /// <summary>
    /// Depth-of-field compositor shared by BOTH render paths:
    /// <list type="bullet">
    /// <item>GameScene in-game/preview — PostProcessStack.RunStack composites into
    /// SceneColorTex before it is displayed or consumed by chained passes.</item>
    /// <item>IDE edit mode — SceneManager renders the viewport into the SharedFBO
    /// (resolve target _sharedColorTex); compositing there makes the sliders live in
    /// the editor viewport too, not only while playing.</item>
    /// </list>
    /// Everything outside a screen-space focus circle (sliders in the Post FX panel,
    /// persisted via <see cref="Config.PostFxSettings"/>) blurs with a golden-angle
    /// disk, ramping over a feather band up to the max blur radius.
    /// Renders through a private scratch FBO, then copies back INTO the scene texture's
    /// FBO with a passthrough quad draw — an in-place composite, so every consumer of
    /// the scene texture sees it. Skipped silently when disabled or when the shader
    /// failed to compile.
    /// </summary>
    public static unsafe class DepthOfFieldComposite
    {
        private static uint _fbo = 0, _tex = 0;
        private static int _w = 0, _h = 0;
        private static uint _quadVAO = 0, _quadVBO = 0;
        private static bool _ranOnceLogged = false;
        private static bool _failLogged = false;
        // Copy-back program (scratch → scene texture). Deliberately NOT glBlitFramebuffer:
        // the wrapper no-ops silently when the wglGetProcAddress pointer failed to load,
        // which left the blurred result stranded in the scratch target (sharp viewport bug).
        private static Shader? _copyProgram;
        private static bool _copyInitTried = false;

        // ── Sprite-shape focus mask: renders the alpha silhouette of every Sprite2D on
        // the configured render layer into a half-res mask; the DoF shader keeps those
        // pixels sharp (sprite-shaped focus instead of a circle). ──
        private static uint _maskFbo = 0, _maskTex = 0;
        private static int _maskW = 0, _maskH = 0;
        private static uint _maskVAO = 0, _maskVBO = 0;
        private static bool _maskFailLogged = false;
        // One-shot log PER TARGET PATH — shows exactly which render path composited
        // (editor shared texture vs GameScene scene texture) and with what values.
        private static readonly System.Collections.Generic.HashSet<string> _loggedTargets = new();

        /// <summary>True once the composite actually executed at least once this
        /// session — the Post FX panel shows this so "no effect" is instantly
        /// distinguishable from "effect running but too subtle".</summary>
        public static bool HasEverRun => _ranOnceLogged;

        /// <summary>How many times the composite actually executed since startup
        /// (all render paths combined) — a live counter, so a stalled value means
        /// the compositing loop has stopped running.</summary>
        public static long RunCount { get; private set; }

        /// <summary>GPU handle of the DoF scratch texture (0 = not allocated yet).
        /// The FrameBuffer Debug panel renders this as a thumbnail.</summary>
        public static uint ScratchTex => _tex;

        /// <summary>Size of the DoF scratch target in pixels (0,0 = not allocated).</summary>
        public static (int Width, int Height) ScratchSize => (_w, _h);

        /// <summary>GPU handle of the sprite-shape focus mask (0 = not allocated / unused).
        /// Half scene resolution; white = sharp silhouette. Debug panel shows it.</summary>
        public static uint MaskTex => _maskTex;

        /// <summary>Size of the sprite-shape mask in pixels (0,0 = not allocated).</summary>
        public static (int Width, int Height) MaskSize => (_maskW, _maskH);

        /// <summary>True when the composite executed on the last frame it was called
        /// (reset by the next DISABLED/SKIP early-return). Cheap "is it alive" signal.</summary>
        public static bool RanLastCall { get; private set; }

        /// <summary>Composite DoF over <paramref name="sceneColorTex"/> in-place.
        /// <paramref name="sceneColorFbo"/> must be the framebuffer the texture is
        /// attached to (the blit destination). <paramref name="targetName"/> labels the
        /// render path in the one-shot console log. Returns true when the effect ran.</summary>
        public static bool Apply(uint sceneColorTex, uint sceneColorFbo, int width, int height,
            string targetName = "scene")
        {
            if (!PostFxSettings.DofEnabled) { RanLastCall = false; return false; }
            if (sceneColorTex == 0 || sceneColorFbo == 0 || width <= 0 || height <= 0)
            {
                RanLastCall = false;
                if (!_failLogged)
                {
                    Console.WriteLine($"[DepthOfField] SKIP: enabled but target invalid (tex={sceneColorTex}, fbo={sceneColorFbo}, {width}x{height}).");
                    _failLogged = true;
                }
                return false;
            }

            uint shader = Shader.GetPostFxDofShaderProgram();
            if (shader == 0)
            {
                RanLastCall = false;
                if (!_failLogged)
                {
                    Console.WriteLine("[DepthOfField] DISABLED — shader failed to compile (see [SHADER COMPILE ERROR] above). Slider changes will do nothing.");
                    _failLogged = true;
                }
                return false;
            }

            EnsureTargets(width, height);
            if (_fbo == 0) { RanLastCall = false; return false; }
            if (_quadVAO == 0) CreateQuad();
            if (!EnsureCopyProgram()) { RanLastCall = false; return false; }

            // Focus tracking (Follow Player / Hovered Tile / Hovered Object / Selection):
            // resolve → smooth → project → write PostFxSettings.DofFocusX/Y. Runs here
            // because Apply executes EVERY FRAME in both render paths — so tracking is
            // live in edit mode AND play/preview with no extra wiring.
            DepthOfFieldFocusTracker.Update();

            // Optional sprite-silhouette focus shape (Post FX panel): render the alpha
            // of every Sprite2D on the configured render layer into a half-res mask.
            // When it fails (no camera / no sprites / shader compile error) the DoF
            // pass silently falls back to the plain focus circle.
            bool maskOn = false;
            if (PostFxSettings.DofSpriteShapeEnable)
                maskOn = RenderSpriteMask(width, height);

            // ── 1. DoF composite: scene texture → scratch FBO ──
            // Draw the quad with a KNOWN state — the live state here belongs to whatever
            // the scene last rendered (editor render props can leave culling ON and
            // blending ON). A culled quad = black frame; blend left on = ghost composite
            // over the scratch target's previous frame. Save + restore everything.
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _fbo);
            GL.Viewport(0, 0, _w, _h);
            bool depthWasOn = GL.IsEnabled(Const.GL_DEPTH_TEST);
            bool cullWasOn = GL.IsEnabled(Const.GL_CULL_FACE);
            bool blendWasOn = GL.IsEnabled(Const.GL_BLEND);
            GL.Disable(Const.GL_DEPTH_TEST);
            GL.Disable(Const.GL_CULL_FACE);   // quad is wound CCW (engine front face) — disable culling so scene props can never reject it
            GL.Disable(Const.GL_BLEND);       // opaque composite, never mix with the scratch target's old content

            GL.UseProgram(shader);
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, sceneColorTex);
            GL.Uniform1i(GL.GetUniformLocation(shader, "sceneTex"), 0);
            GL.Uniform2f(GL.GetUniformLocation(shader, "texelSize"), 1f / _w, 1f / _h);
            GL.Uniform2f(GL.GetUniformLocation(shader, "u_FocusPoint"),
                PostFxSettings.DofFocusX, PostFxSettings.DofFocusY);
            GL.Uniform1f(GL.GetUniformLocation(shader, "u_Radius"), PostFxSettings.DofRadius);
            GL.Uniform1f(GL.GetUniformLocation(shader, "u_Feather"), PostFxSettings.DofFeather);
            GL.Uniform1f(GL.GetUniformLocation(shader, "u_MaxBlur"), PostFxSettings.DofMaxBlur);
            GL.Uniform1i(GL.GetUniformLocation(shader, "u_MaskEnabled"), maskOn ? 1 : 0);
            GL.Uniform1i(GL.GetUniformLocation(shader, "u_MaskOnly"), PostFxSettings.DofSpriteMaskOnly ? 1 : 0);
            GL.Uniform1i(GL.GetUniformLocation(shader, "u_MaskTex"), 1);
            if (maskOn)
            {
                GL.ActiveTexture(Const.GL_TEXTURE1);
                GL.BindTexture(Const.GL_TEXTURE_2D, _maskTex);
            }

            GL.BindVertexArray(_quadVAO);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            GL.BindVertexArray(0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.ActiveTexture(Const.GL_TEXTURE0);
            // State restore happens after the copy-back draw (step 2) — it needs the
            // same safe state (no cull, no blend, no depth) as the composite draw.

            // ── 2. In-place copy-back: scratch → scene texture, via the SAME quad path
            // as step 1 (proven to work). glBlitFramebuffer is intentionally NOT used —
            // its wrapper silently no-ops when the GL pointer failed to load, which left
            // the blur stranded in the scratch target while the viewport stayed sharp.
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, sceneColorFbo);
            GL.Viewport(0, 0, _w, _h);
            GL.UseProgram(_copyProgram!.ProgramId);
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, _tex);
            GL.Uniform1i(GL.GetUniformLocation(_copyProgram.ProgramId, "sceneTex"), 0);

            GL.BindVertexArray(_quadVAO);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
            GL.BindVertexArray(0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);

            // Restore the render state both draws changed (callers re-set viewport/FB).
            if (depthWasOn) GL.Enable(Const.GL_DEPTH_TEST);
            if (cullWasOn) GL.Enable(Const.GL_CULL_FACE);
            if (blendWasOn) GL.Enable(Const.GL_BLEND);

            // One-shot console proof PER RENDER PATH — if your path's line never
            // appears, that path is not compositing (viewport shows another texture).
            if (_loggedTargets.Add(targetName))
            {
                _ranOnceLogged = true; // set BEFORE the log so HasEverRun is already true for the panel this frame
                Console.WriteLine($"[DepthOfField] ACTIVE on '{targetName}' ({_w}x{_h}) — " +
                    $"focus=({PostFxSettings.DofFocusX:F2},{PostFxSettings.DofFocusY:F2}) " +
                    $"radius={PostFxSettings.DofRadius:F2} feather={PostFxSettings.DofFeather:F2} blur={PostFxSettings.DofMaxBlur:F1}px");
            }
            RunCount++;
            RanLastCall = true;
            return true;
        }

        /// <summary>Render the alpha silhouette of every Sprite2D on the configured
        /// render layer into the half-res mask. Same quad math as DrawSprite2D (via
        /// TryGetSprite2DDrawData) so the mask matches the drawn sprite pixel-for-pixel.</summary>
        private static bool RenderSpriteMask(int sceneW, int sceneH)
        {
            var bridge = DepthOfFieldFocusTracker.Bridge;
            var cam = bridge?.Camera;
            var mgr = bridge?.EditorObjectManager;
            if (cam == null || mgr == null) return false;

            uint maskShader = Shader.GetDofSpriteMaskShaderProgram();
            if (maskShader == 0)
            {
                if (!_maskFailLogged)
                {
                    Console.WriteLine("[DepthOfField] sprite mask shader failed to compile — plain circular focus used.");
                    _maskFailLogged = true;
                }
                return false;
            }

            EnsureMaskTargets(sceneW, sceneH);
            if (_maskFbo == 0) return false;
            if (_maskVAO == 0) CreateMaskQuad();

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, _maskFbo);
            GL.Viewport(0, 0, _maskW, _maskH);
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(Const.GL_COLOR_BUFFER_BIT);

            bool depthWasOn = GL.IsEnabled(Const.GL_DEPTH_TEST);
            bool cullWasOn = GL.IsEnabled(Const.GL_CULL_FACE);
            bool blendWasOn = GL.IsEnabled(Const.GL_BLEND);
            GL.Disable(Const.GL_DEPTH_TEST);
            GL.Disable(Const.GL_CULL_FACE);   // projected quad winding can flip — never cull
            GL.Enable(Const.GL_BLEND);
            GL.BlendFunc(Const.GL_ONE, Const.GL_ONE_MINUS_SRC_ALPHA); // white coverage

            GL.UseProgram(maskShader);
            GL.Uniform1i(GL.GetUniformLocation(maskShader, "spriteTex"), 0);
            GL.Uniform1f(GL.GetUniformLocation(maskShader, "u_ExpandPx"), PostFxSettings.DofSpriteExpandPx);
            GL.Uniform1f(GL.GetUniformLocation(maskShader, "u_AlphaBias"), PostFxSettings.DofSpriteAlphaBias);

            GL.BindVertexArray(_maskVAO);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _maskVBO);
            Span<float> v = stackalloc float[24];
            int drawn = 0;

            foreach (var o in mgr.Objects)
            {
                if (o == null || o.PrimitiveType != EditorPrimitiveType.Sprite2D) continue;
                if (o.Sprite2DRenderLayer != PostFxSettings.DofSpriteShapeLayer) continue;
                if (!o.TryGetSprite2DDrawData(out uint texId, out var uvMin, out var uvMax,
                    out var bl, out var br, out var tr, out var tl)) continue;

                var pBL = TransformGizmo.ProjectToScreen(cam, bl, _maskW, _maskH);
                var pBR = TransformGizmo.ProjectToScreen(cam, br, _maskW, _maskH);
                var pTR = TransformGizmo.ProjectToScreen(cam, tr, _maskW, _maskH);
                var pTL = TransformGizmo.ProjectToScreen(cam, tl, _maskW, _maskH);
                if (float.IsNaN(pBL.X) || float.IsInfinity(pBL.X)) continue; // behind camera

                // UVs match DrawSprite2D exactly (draw space, y-flipped + mirrored).
                WriteMaskVert(v, 0, pBL, uvMin.X, uvMax.Y);   // bottom-left
                WriteMaskVert(v, 4, pBR, uvMax.X, uvMax.Y);   // bottom-right
                WriteMaskVert(v, 8, pTR, uvMax.X, uvMin.Y);   // top-right
                WriteMaskVert(v, 12, pBL, uvMin.X, uvMax.Y);
                WriteMaskVert(v, 16, pTR, uvMax.X, uvMin.Y);
                WriteMaskVert(v, 20, pTL, uvMin.X, uvMin.Y);  // top-left

                GL.ActiveTexture(Const.GL_TEXTURE0);
                GL.BindTexture(Const.GL_TEXTURE_2D, texId);
                fixed (float* pv = v)
                    GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(24 * sizeof(float)), pv, Const.GL_DYNAMIC_DRAW);
                GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
                drawn++;
            }

            GL.BindVertexArray(0);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, 0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            GL.Disable(Const.GL_BLEND);
            if (depthWasOn) GL.Enable(Const.GL_DEPTH_TEST);
            if (cullWasOn) GL.Enable(Const.GL_CULL_FACE);
            if (blendWasOn) GL.Enable(Const.GL_BLEND);
            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);

            return drawn > 0; // nothing on that layer → fall back to the circle
        }

        /// <summary>Write one quad vertex: ProjectToScreen pixels → NDC for the
        /// post_vertex shader (mask viewport is _maskW × _maskH).</summary>
        private static void WriteMaskVert(Span<float> v, int i,
            System.Numerics.Vector2 p, float u, float vv)
        {
            v[i] = p.X / _maskW * 2f - 1f;
            v[i + 1] = p.Y / _maskH * 2f - 1f;
            v[i + 2] = u;
            v[i + 3] = vv;
        }

        /// <summary>Half-resolution RGBA8 mask target.</summary>
        private static void EnsureMaskTargets(int sceneW, int sceneH)
        {
            int w = Math.Max(1, sceneW / 2);
            int h = Math.Max(1, sceneH / 2);
            if (_maskFbo != 0 && _maskW == w && _maskH == h) return;
            DestroyMaskTargets();

            uint fbo = 0, tex = 0;
            GL.GenFramebuffers(1, &fbo);
            GL.GenTextures(1, &tex);
            GL.BindTexture(Const.GL_TEXTURE_2D, tex);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA8,
                          w, h, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, fbo);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, tex, 0);
            uint status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                Console.WriteLine($"[DepthOfField] mask FBO incomplete: 0x{status:X}");

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            _maskFbo = fbo; _maskTex = tex; _maskW = w; _maskH = h;
        }

        /// <summary>Mask quad: pos2 + uv2 (post_vertex layout), CCW.</summary>
        private static unsafe void CreateMaskQuad()
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

            _maskVAO = vao; _maskVBO = vbo;
        }

        private static void DestroyMaskTargets()
        {
            if (_maskFbo != 0) { fixed (uint* p = &_maskFbo) GL.DeleteFramebuffers(1, p); _maskFbo = 0; }
            if (_maskTex != 0) { fixed (uint* p = &_maskTex) GL.DeleteTextures(1, p); _maskTex = 0; }
            if (_maskVBO != 0) { fixed (uint* p = &_maskVBO) GL.DeleteBuffers(1, p); _maskVBO = 0; }
            if (_maskVAO != 0) { fixed (uint* p = &_maskVAO) GL.DeleteVertexArrays(1, p); _maskVAO = 0; }
            _maskW = _maskH = 0;
        }

        /// <summary>Compile the passthrough copy program once (vs = post_vertex layout:
        /// loc0 pos2 + loc1 uv2 — matches the composite quad exactly).</summary>
        private static bool EnsureCopyProgram()
        {
            if (_copyInitTried) return _copyProgram != null;
            _copyInitTried = true;

            const string vs = """
                #version 330 core
                layout (location = 0) in vec2 aPos;
                layout (location = 1) in vec2 aUV;
                out vec2 TexCoord;
                void main()
                {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                    TexCoord = aUV;
                }
                """;
            const string fs = """
                #version 330 core
                in vec2 TexCoord;
                out vec4 FragColor;
                uniform sampler2D sceneTex;
                void main() { FragColor = texture(sceneTex, TexCoord); }
                """;

            _copyProgram = new Shader(vs, fs);
            if (_copyProgram.ProgramId == 0)
            {
                Console.WriteLine("[DepthOfField] DISABLED — copy-back shader failed to compile.");
                _copyProgram = null;
                return false;
            }
            return true;
        }

        /// <summary>(Re)create the scratch target at the scene texture's size.</summary>
        private static void EnsureTargets(int width, int height)
        {
            if (_fbo != 0 && _w == width && _h == height) return;
            DestroyTargets();

            uint fbo = 0, tex = 0;
            GL.GenFramebuffers(1, &fbo);
            GL.GenTextures(1, &tex);
            GL.BindTexture(Const.GL_TEXTURE_2D, tex);
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA8,
                          width, height, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, (void*)0);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, fbo);
            GL.FramebufferTexture2D(Const.GL_FRAMEBUFFER, Const.GL_COLOR_ATTACHMENT0,
                                    Const.GL_TEXTURE_2D, tex, 0);
            uint status = (uint)GL.CheckFramebufferStatus(Const.GL_FRAMEBUFFER);
            if (status != Const.GL_FRAMEBUFFER_COMPLETE)
                Console.WriteLine($"[DepthOfField] FBO incomplete: 0x{status:X}");

            GL.BindFramebuffer(Const.GL_FRAMEBUFFER, 0);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);

            _fbo = fbo;
            _tex = tex;
            _w = width;
            _h = height;
        }

        /// <summary>Full-screen quad for the composite draw (pos2 + uv2). WOUND CCW —
        /// the engine's front face is GL_CCW, matching the other post-process quads —
        /// with UV flipped so the texture's bottom-left origin matches.</summary>
        private static unsafe void CreateQuad()
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
            fixed (float* v = quad)
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(quad.Length * sizeof(float)), v, Const.GL_STATIC_DRAW);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
            GL.BindVertexArray(0);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, 0);

            _quadVAO = vao;
            _quadVBO = vbo;
        }

        private static void DestroyTargets()
        {
            if (_fbo != 0) { fixed (uint* p = &_fbo) GL.DeleteFramebuffers(1, p); _fbo = 0; }
            if (_tex != 0) { fixed (uint* p = &_tex) GL.DeleteTextures(1, p); _tex = 0; }
            _w = _h = 0;
            DestroyMaskTargets();
        }
    }
}
