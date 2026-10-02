using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using StbTrueTypeSharp;
using System.Numerics;
using ImGuiNET;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>Per-button style override. When non-null, overrides the global palette for this button.</summary>
    public struct ButtonStyle
    {
        public Vector3 SelectedBg, UnselectedBg;
        public Vector3 SelectedBorder, UnselectedBorder;
        public Vector3 SelectedText, UnselectedText;
        public Vector3 SelectedSidebar;
        public Vector3 GlowColor;

        public ButtonStyle(
            Vector3 selectedBg, Vector3 unselectedBg,
            Vector3 selectedBorder, Vector3 unselectedBorder,
            Vector3 selectedText, Vector3 unselectedText,
            Vector3 selectedSidebar, Vector3 glowColor)
        {
            SelectedBg = selectedBg;
            UnselectedBg = unselectedBg;
            SelectedBorder = selectedBorder;
            UnselectedBorder = unselectedBorder;
            SelectedText = selectedText;
            UnselectedText = unselectedText;
            SelectedSidebar = selectedSidebar;
            GlowColor = glowColor;
        }
    }

    /// <summary>A baked font atlas — holds character data and OpenGL texture for one (fontPath, fontSize) pair.</summary>
    public struct FontSlot
    {
        public StbTrueType.stbtt_bakedchar[] BakedChars; // 96 chars (ASCII 32..126)
        public uint TextureID;
        public string FontPath;
        public float FontSize;
    }

    /// <summary>Describes a single interactive button — position, label, callbacks, and font slot.</summary>
    public struct ButtonDef
    {
        public string Label;
        public float X, Y, W, H;
        /// <summary>Called when the button is clicked (mouse press while hovering).</summary>
        public Action? OnClick;
        /// <summary>Called when the mouse enters the button area.</summary>
        public Action? OnHoverEnter;
        /// <summary>Called when the mouse leaves the button area.</summary>
        public Action? OnHoverExit;
        /// <summary>Whether the mouse is currently hovering over the button (set by UpdateButtons).</summary>
        public bool IsHovered;
        /// <summary>Optional per-button style override. When set, overrides the global palette for DrawButtons.</summary>
        public ButtonStyle? Style;
        /// <summary>Optional tag for custom data.</summary>
        public object? Tag;
        /// <summary>Index into HUD._fontSlots for this button's font face + size.</summary>
        public int FontSlotIndex;
        /// <summary>Cached text extents — computed once in AddButton() to avoid GetTextExtents() per frame per button.</summary>
        public HUD.TextExtents CachedExtents;
        /// <summary>When true, label text wraps to next line if it exceeds button width.</summary>
        public bool WordWrap;
        /// <summary>Text alignment: Left, Center, or Right.</summary>
        public TextAlignment Alignment;
    }

    public unsafe class HUD
    {
        // ════════════════════════════════════════════════════════════════════
        //  FALLBACK TEXT OUT (ImGui draw-list mirror) — see HUD.TextOut below.
        //  Lives on the static HUDEx class because DialogueSystem must draw the
        //  mirrored strings AFTER its own overlay via ImGui (namespace-neutral,
        //  no Visual→IDE dependency from the HUD itself).
        // ════════════════════════════════════════════════════════════════════

        /// <summary>One mirrored HUD text string, drawn by the ImGui overlay when
        /// the stb-atlas path produces no glyphs in the editor preview.</summary>
        public struct TextOutItem
        {
            public float X, Y;                 // scene px (top-left baseline top)
            public string Text;
            public Vector4 Color;              // rgba 0..1
            public bool CenteredAtX;           // true: X is the CENTER of the string
            public float FontSizePx;           // 0 = overlay default
            public int Frame;                  // Glfw.FrameId at queue time (stale-drop)
        }

        /// <summary>Current frame's mirrored HUD text strings (scene-px coords).
        /// Emptied at the start of every Flush(); the ViewportPanel's ImGui overlay
        /// draws them AFTER the dialogue overlay so they sit on the panels.</summary>
        public static readonly List<TextOutItem> FrameTextOut = [];

        /// <summary>Glfw.FrameId of the most recent TextOut() add — used to drop
        /// strings from frames whose overlay pass never ran (no leak, no stale draw).</summary>
        private static int _textOutFrame = -1;

        /// <summary>Optional overlay-supplied font resolver (set by ViewportPanel:
        /// (path, sizePx) → ImGui ImFont*). Used by DialogueSystem to draw the
        /// mirrored HUD text with the SAME proven font path as its own overlay.
        /// Signature keeps the Visual→IDE dependency inverted.</summary>
        public static Func<string, float, nint>? ImGuiFontResolver;

        /// <summary>ImGui draw list the overlay published for THIS frame (the viewport's
        /// scene-image window list). Null = no overlay this frame → mirroring no-ops.</summary>
        public static ImDrawListPtr? OverlayDrawList;

        /// <summary>Font FILE the overlay resolved for mirrored strings (Worldstar) —
        /// passed to the font resolver together with each item's size.</summary>
        public static string OverlayFontPath = "";

        /// <summary>Draw every mirrored HUD text string through the ImGui overlay
        /// (called by ViewportPanel AFTER the dialogue overlay). Coordinates are
        /// scene-px; sceneToScreen converts them into window space. Guarded so any
        /// missing piece (list, draw list, font) silently skips — never throws.</summary>
        public static void DrawTextOutOverlay(Func<Vector2, Vector2> sceneToScreen)
        {
            // Only THIS frame's strings — a late pass must never redraw stale text
            // from a frame whose overlay didn't run.
            if (FrameTextOut.Count > 0 && FrameTextOut[0].Frame != Glfw.FrameId)
                FrameTextOut.Clear();
            if (FrameTextOut.Count == 0) return;
            var dl = OverlayDrawList;
            if (dl == null || (nint)dl.Value.NativePtr == 0) return;

            foreach (var item in FrameTextOut)
            {
                float size = item.FontSizePx > 0f ? item.FontSizePx : 16f;
                nint fontRaw = ImGuiFontResolver?.Invoke(OverlayFontPath, size) ?? 0;
                if (fontRaw == 0)
                {
                    // Resolver returned nothing (font queued for NEXT frame) → fall back
                    // to ImGui's current default font so the string still shows.
                    fontRaw = (nint)ImGuiNET.ImGui.GetFont().NativePtr;
                    if (fontRaw == 0) continue;
                }
                var font = new ImFontPtr((ImFont*)fontRaw);
                float fs = size;

                var scr = sceneToScreen(new Vector2(item.X, item.Y));
                var col = new Vector4(item.Color.X, item.Color.Y, item.Color.Z, item.Color.W);
                uint packed = ImGui.GetColorU32(col);

                Vector2 pos = new(scr.X, scr.Y);
                if (item.CenteredAtX)
                {
                    float w = font.CalcTextSizeA(fs, float.MaxValue, 0f, item.Text).X;
                    pos.X -= w * 0.5f;
                }
                dl.Value.AddText(font, fs, pos, packed, item.Text);
            }
            FrameTextOut.Clear(); // drawn — don't let a second overlay pass double them
        }

        /// <summary>Mirror a HUD text draw to the ImGui fallback list (no-op unless
        /// the overlay opted in this frame). Call after the primary DrawText.
        /// centerAtX: X is the string's horizontal center (badges/labels).</summary>
        public static void TextOut(float x, float y, string text, Vector3 rgb, float alpha = 1f,
            bool centerAtX = false, float fontSizePx = 0f)
        {
            if (string.IsNullOrEmpty(text)) return;
            // Frame-scoped mirror: drop strings from earlier frames on the first add
            // of a new frame (paths without an overlay — GameScene docked/editor —
            // would otherwise accumulate forever; DrawTextOutOverlay also filters).
            if (Glfw.FrameId != _textOutFrame)
            {
                FrameTextOut.Clear();
                _textOutFrame = Glfw.FrameId;
            }
            FrameTextOut.Add(new TextOutItem
            {
                X = x, Y = y, Text = text, Frame = Glfw.FrameId,
                Color = new Vector4(rgb.X, rgb.Y, rgb.Z, alpha),
                CenteredAtX = centerAtX, FontSizePx = fontSizePx,
            });
        }

        private readonly uint vao;
        private readonly uint vbo;
        private readonly uint shaderProgram;
        private readonly int posLoc, sizeLoc, colorLoc, uvOffsetLoc, uvScaleLoc, texLoc, rotLoc;
        private nuint _vboAllocatedSize = 0;

        /// <summary>Cache of baked font atlases, each for a unique (fontPath, fontSize) pair. Slot 0 is the default.</summary>
        private readonly List<FontSlot> _fontSlots = [];
        /// <summary>O(1) lookup: (fontPath, fontSize) → slot index. Updated when slots are created or cleared.</summary>
        private readonly Dictionary<(string path, float size), int> _fontSlotLookup = [];

        private const int AtlasSize = 2048;

        // ════════════════════════════════════════════
        //  BATCH RENDER QUEUES
        // ════════════════════════════════════════════

        /// <summary>Queued box draw commands. Flushed by Flush().</summary>
        private readonly List<(float x, float y, float w, float h, Vector3 color)> _boxQueue = [];

        /// <summary>UNIFIED box submission queue — DrawBox (blended 0.6-alpha look)
        /// and DrawSolidBox (opaque, blend off) append here IN CALL ORDER and Flush
        /// renders consecutive same-kind+colour runs in sequence (painter's order).
        /// The previous two-queue design rendered ALL solids before ALL boxes, which
        /// let blended slot backgrounds overdraw the solid number chips and banners
        /// queued before them ("layer UI" regression: chips/banner invisible).
        /// _boxQueue is kept only because FlushWithBackdrop-era callers probe it.</summary>
        private readonly List<(bool solid, float x, float y, float w, float h, Vector3 color)> _drawQueue = [];

        /// <summary>Queued text draw commands. Flushed by Flush().</summary>
        private readonly List<(int fontSlot, float x, float y, string text, Vector3 color)> _textQueue = [];

        /// <summary>Queued image draw commands. Flushed by Flush().</summary>
        private readonly List<(float x, float y, float w, float h, uint texId, float rotation)> _imageQueue = [];

        /// <summary>Queued SUB-RECT image commands (icon crops). Flushed by Flush().</summary>
        private readonly List<(float x, float y, float w, float h, uint texId, float u0, float v0, float u1, float v1)> _imageUvQueue = [];

        /// <summary>Count of OPAQUE quads currently in _drawQueue (DrawSolidBox).
        /// QueuedItemCount must include these so the preview flush guard fires when
        /// only panels were queued.</summary>
        private int _solidCount;

        // ── Panel occlusion registry (for world-anchored overlays) ──
        // Every opaque quad queued this frame is remembered so overlays that composite
        // LATER, OUTSIDE the HUD batch (the ImGui draw-list overlay that draws NPC
        // badges, names and speech bubbles over the finished scene texture) can skip
        // markers that sit BEHIND an open panel. Filtered by frame id — only rects        // claimed during the CURRENT frame occlude.
        private static readonly List<(int frame, float x, float y, float w, float h)> _occluders = [];

        // ════════════════════════════════════════════
        //  CONSTRUCTOR
        // ════════════════════════════════════════════

        public unsafe HUD(string fontPath, float fontSize)
        {
            shaderProgram = Shader.GetHudShaderProgram();
            posLoc = GL.GetUniformLocation(shaderProgram, "position");
            sizeLoc = GL.GetUniformLocation(shaderProgram, "size");
            colorLoc = GL.GetUniformLocation(shaderProgram, "textColor");
            uvOffsetLoc = GL.GetUniformLocation(shaderProgram, "uvOffset");
            uvScaleLoc = GL.GetUniformLocation(shaderProgram, "uvScale");
            texLoc = GL.GetUniformLocation(shaderProgram, "hudTexture");
            rotLoc = GL.GetUniformLocation(shaderProgram, "rotation");

            // 1. Vertex setup
            float[] vertices = [
                0f, 1f,     0f, 0f,
                0f, 0f,     0f, 1f,
                1f, 1f,     1f, 0f,
                0f, 0f,     0f, 1f,
                1f, 0f,     1f, 1f,
                1f, 1f,     1f, 0f
            ];

            fixed (uint* pVao = &vao) GL.GenVertexArrays(1, pVao);
            fixed (uint* pVbo = &vbo) GL.GenBuffers(1, pVbo);

            _vboAllocatedSize = 1000 * 6 * 4 * sizeof(float);
            int stride = 4 * sizeof(float);

            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

            fixed (float* p = vertices)
                GL.BufferData(Const.GL_ARRAY_BUFFER, _vboAllocatedSize, (void*)0, Const.GL_DYNAMIC_DRAW);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, stride, (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, stride, (void*)(2 * sizeof(float)));

            // 2. Create initial font slot (slot 0 = default)
            GetOrCreateFontSlot(fontPath, fontSize);
            PrimaryFontPath = PathHelpers.Resolve(fontPath);
            PrimaryFontSize = fontSize;
        }

        // ════════════════════════════════════════════
        //  FONT SLOT SYSTEM
        // ════════════════════════════════════════════

        /// <summary>Find or create a font slot for the given (fontPath, fontSize). Returns the slot index.</summary>
        public int GetOrCreateFontSlot(string fontPath, float fontSize)
        {
            // Resolve relative paths against the exe folder (also normalizes the old
            // double-backslash "Artifacts\\fonts\\..." forms) so fonts load anywhere.
            fontPath = PathHelpers.Resolve(fontPath);

            // O(1) lookup via dictionary cache
            var key = (fontPath, fontSize);
            if (_fontSlotLookup.TryGetValue(key, out int cachedIdx))
                return cachedIdx;

            // Create new slot
            uint texID;
            GL.GenTextures(1, &texID);

            var slot = new FontSlot
            {
                BakedChars = new StbTrueType.stbtt_bakedchar[96],
                TextureID = texID,
                FontPath = fontPath,
                FontSize = fontSize,
            };

            BakeFontIntoSlot(ref slot);
            _fontSlots.Add(slot);
            _fontSlotLookup[key] = _fontSlots.Count - 1;

            Console.WriteLine($"[HUD] Created font slot {_fontSlots.Count - 1}: {fontPath} @ {fontSize}px");
            return _fontSlots.Count - 1;
        }

        /// <summary>Bake TTF data into a FontSlot's bakedChars array and upload to its GPU texture.</summary>
        private static void BakeFontIntoSlot(ref FontSlot slot)
        {
            byte[] ttfData = File.ReadAllBytes(slot.FontPath);
            byte[] tempBitmap = new byte[AtlasSize * AtlasSize];

            int baked;
            fixed (byte* pTtf = ttfData)
            fixed (byte* pTemp = tempBitmap)
            fixed (StbTrueType.stbtt_bakedchar* pChars = slot.BakedChars)
            {
                baked = StbTrueType.stbtt_BakeFontBitmap(pTtf, 0, slot.FontSize, pTemp, AtlasSize, AtlasSize, 32, 96, pChars);
            }

            // stbtt returns <=0 on failure and leaves the bitmap zeroed — the slot then
            // produces zero-size extents (shrunken windows) and text that the shader
            // discards entirely (alpha < 0.1). Log loudly: this previously failed
            // SILENTLY and looked like "boxes draw but no text ever appears".
            int nonZero = 0;
            for (int i = 0; i < tempBitmap.Length; i++)
                if (tempBitmap[i] != 0) nonZero++;
            if (baked <= 0 || nonZero == 0)
            {
                Console.WriteLine($"[HUD] FONT BAKE FAILED for '{slot.FontPath}' @ {slot.FontSize}px " +
                    $"(stbtt result={baked}, nonZeroBytes={nonZero}, ttfBytes={ttfData.Length}) — text will be invisible!");
            }

            byte[] rgbaBitmap = new byte[AtlasSize * AtlasSize * 4];
            for (int i = 0; i < tempBitmap.Length; i++)
            {
                rgbaBitmap[i * 4 + 0] = 255;
                rgbaBitmap[i * 4 + 1] = 255;
                rgbaBitmap[i * 4 + 2] = 255;
                rgbaBitmap[i * 4 + 3] = tempBitmap[i];
            }

            GL.BindTexture(Const.GL_TEXTURE_2D, slot.TextureID);
            fixed (byte* pB = rgbaBitmap)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, AtlasSize, AtlasSize, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, pB);
            }
            GL.GenerateMipmap(Const.GL_TEXTURE_2D);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR_MIPMAP_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);

            // ── GPU READBACK VERIFICATION + AUTO RE-BAKE ──
            // CPU bitmap non-empty + GPU upload logged no error, yet glyphs stayed            // invisible in the editor preview: the driver silently dropped/zeroed the            // upload (context-state dependent). Read the atlas BACK from VRAM and            // verify; if empty, bake+upload once more immediately. The Prewarm-time            // retry runs with clean state, which is exactly the condition that has            // always produced a WORKING atlas on the other paths.
            if (VerifyAtlasUploaded("initial bake", slot.FontPath, slot.FontSize, rgbaBitmap))
                return;

            Console.WriteLine($"[HUD] Atlas GPU-empty after first upload — re-baking '{slot.FontPath}' @ {slot.FontSize}px");
            fixed (byte* pTtf2 = ttfData)
            fixed (byte* pTemp2 = tempBitmap)
            fixed (StbTrueType.stbtt_bakedchar* pChars2 = slot.BakedChars)
            {
                StbTrueType.stbtt_BakeFontBitmap(pTtf2, 0, slot.FontSize, pTemp2, AtlasSize, AtlasSize, 32, 96, pChars2);
            }
            for (int i = 0; i < tempBitmap.Length; i++)
            {
                rgbaBitmap[i * 4 + 0] = 255;
                rgbaBitmap[i * 4 + 1] = 255;
                rgbaBitmap[i * 4 + 2] = 255;
                rgbaBitmap[i * 4 + 3] = tempBitmap[i];
            }
            fixed (byte* pB2 = rgbaBitmap)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, AtlasSize, AtlasSize, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, pB2);
            }
            GL.GenerateMipmap(Const.GL_TEXTURE_2D);
            if (!VerifyAtlasUploaded("re-bake", slot.FontPath, slot.FontSize, rgbaBitmap))
            {
                Console.WriteLine($"[HUD] Atlas STILL empty after re-bake — '{slot.FontPath}' @ {slot.FontSize}px");
            }
        }

        /// <summary>Read the mipmap level 0 of the currently-bound atlas texture back
        /// from VRAM and confirm its alpha channel actually contains the baked glyphs.
        /// Logs and returns true when the GPU copy matches the CPU bitmap (or when        /// readback itself is unavailable — never fail loudly here).</summary>
        private static unsafe bool VerifyAtlasUploaded(string stage, string fontPath, float fontSize, byte[] expectedRgba)
        {
            try
            {
                var probe = new byte[AtlasSize * AtlasSize * 4];
                fixed (byte* pProbe = probe)
                {
                    GL.PixelStore(Const.GL_PACK_ALIGNMENT, 1);
                    GL.GetTexImage(Const.GL_TEXTURE_2D, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, pProbe);
                    GL.PixelStore(Const.GL_PACK_ALIGNMENT, 4);
                }

                int nonZeroAlpha = 0;
                for (int i = 3; i < probe.Length; i += 4)
                    if (probe[i] != 0) nonZeroAlpha++;

                bool ok = nonZeroAlpha > 1000; // glyphs ≈ tens of thousands of lit pixels
                if (!ok)
                {
                    Console.WriteLine($"[HUD] ATLAS GPU READBACK EMPTY ({stage}) '{fontPath}' @ {fontSize}px " +
                        $"(alpha-lit bytes={nonZeroAlpha}, glError=0x{GL.GetError():X})");
                }
                return ok;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD] Atlas readback unavailable ({stage}): {ex.Message} — skipping verification");
                return true; // no verification possible → assume the upload stuck
            }
        }

        // ════════════════════════════════════════════
        //  QUEUE-BASED DRAWING (deferred until Flush)
        // ════════════════════════════════════════════

        /// <summary>Queue a solid-colour rectangle. Rendered during Flush().</summary>
        public void DrawBox(float x, float y, float w, float h, Vector3 color)
        {
            _boxQueue.Add((x, y, w, h, color));
            _drawQueue.Add((false, x, y, w, h, color));
        }

        /// <summary>Queue an OPAQUE solid rectangle: drawn IN QUEUE ORDER (like every
        /// other category) with blending DISABLED — the quad REPLACES the framebuffer,
        /// so a panel stamped this way is fully solid no matter how bright the scene        /// behind it is, and its shader mode writes alpha 1.0 (MODE SOLID) so the RGBA8
        /// scene texture stays blittable by ImGui without fading. Also registers an        /// occluder rect for the world-anchored overlay (see PointInPanelOccluder).</summary>
        public void DrawSolidBox(float x, float y, float w, float h, Vector3 color)
        {
            _drawQueue.Add((true, x, y, w, h, color));
            _solidCount++;
            _occluders.Add((Glfw.FrameId, x, y, w, h));
        }

        /// <summary>Is this scene-px point covered by any OPAQUE panel queued this        /// frame (DrawSolidBox)? The ImGui world-anchored overlay (NPC badges, names,        /// speech bubbles) composites AFTER the HUD batch with no depth — without this        /// check its markers draw ON TOP of open inventory/shop panels ("layer UI").        /// Cheap: a handful of rects, one point test each.</summary>
        public static bool PointInPanelOccluder(float x, float y)
            => PanelCoversRect(x - 0.5f, y - 0.5f, 1f, 1f);

        /// <summary>Does an open OPAQUE panel (the DrawSolidBox rects queued this frame
        /// by the inventory/shop/quest panels) overlap ANY part of the given scene-px
        /// rect? World-anchored markers must test their FULL on-screen extent with this,
        /// not just their anchor point: the name badge + bubble tower ~40–80px ABOVE the
        /// anchor, so a point-only test let the marker's upper half punch through the
        /// panel parchment. Same-frame occluder rects, one rect test each.</summary>
        public static bool PanelCoversRect(float x, float y, float w, float h)
        {
            int frame = Glfw.FrameId;
            foreach (var (f, ox, oy, ow, oh) in _occluders)
            {
                if (f != frame) continue;
                if (x < ox + ow && x + w > ox && y < oy + oh && y + h > oy) return true;
            }
            return false;
        }

        /// <summary>
        /// Queue a pair of horizontal border bars (top + bottom) as a single batch concept.
        /// Same colour, same width, positioned at top and bottom of the given rect.
        /// Flush() groups them by colour automatically, so this is purely for cleaner code.
        /// </summary>
        public void DrawBoxHorizontalBorders(float x, float y, float w, float h, float thickness, Vector3 color)
        {
            _boxQueue.Add((x, y, w, thickness, color));                // top bar
            _boxQueue.Add((x, y + h - thickness, w, thickness, color)); // bottom bar
        }

        /// <summary>Queue text using a specific font slot. Rendered during Flush().</summary>
        private void DrawTextBatched(string text, float startX, float startY, Vector3 textColor, int fontSlotIndex = 0)
        {
            if (fontSlotIndex < 0 || fontSlotIndex >= _fontSlots.Count) return;
            if (string.IsNullOrEmpty(text)) return;
            _textQueue.Add((fontSlotIndex, startX, startY, text, textColor));
        }

        /// <summary>Queue text using the default font slot (slot 0), with optional outline.</summary>
        public void DrawText(string text, float startX, float startY, Vector3 color, Vector3? outlineColor = null, float outlineSize = 0.0f)
        {
            DrawText(text, startX, startY, color, outlineColor, outlineSize, fontSlotIndex: 0);
        }

        /// <summary>Queue text using a specific font slot, with optional outline.</summary>
        public void DrawText(string text, float startX, float startY, Vector3 color, Vector3? outlineColor, float outlineSize, int fontSlotIndex)
        {
            if (fontSlotIndex < 0 || fontSlotIndex >= _fontSlots.Count)
                fontSlotIndex = 0;

            // FALLBACK TEXT MIRROR: hand the string to the ImGui overlay too. The stb
            // atlas path can render nothing in the editor preview (GPU-empty atlas); the
            // overlay draws every mirrored string with the PROVEN ImGui font path, so
            // HUD labels survive in both modes. Full alpha when outlined (the outline
            // darkens the backdrop the old halo provided), slightly softer otherwise.
            TextOut(startX, startY, text, color, outlineColor != null ? 1f : 0.95f);

            if (outlineColor != null)
            {
                DrawTextBatched(text, startX - outlineSize, startY, outlineColor.GetValueOrDefault(), fontSlotIndex);
                DrawTextBatched(text, startX + outlineSize, startY, outlineColor.GetValueOrDefault(), fontSlotIndex);
                DrawTextBatched(text, startX, startY - outlineSize, outlineColor.GetValueOrDefault(), fontSlotIndex);
                DrawTextBatched(text, startX, startY + outlineSize, outlineColor.GetValueOrDefault(), fontSlotIndex);
            }

            DrawTextBatched(text, startX, startY, color, fontSlotIndex);
        }

        /// <summary>Draw text with word wrap: split into lines so no line exceeds maxWidth.</summary>
        public void DrawTextWrapped(string text, float startX, float startY, float maxWidth, Vector3 color, int fontSlotIndex = 0)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f) return;
            var lines = WordWrapText(text, maxWidth, fontSlotIndex);
            float lineHeight = MeasureTextHeight("A|g");
            for (int i = 0; i < lines.Count; i++)
            {
                DrawText(lines[i], startX, startY + i * lineHeight, color, null, 0f, fontSlotIndex);
            }
        }

        /// <summary>Split text into lines that fit within maxWidth using the given font slot.</summary>
        public List<string> WordWrapText(string text, float maxWidth, int fontSlotIndex = 0)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) { result.Add(""); return result; }
            var words = text.Split(' ');
            string currentLine = "";
            foreach (var word in words)
            {
                string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                if (MeasureText(testLine) > maxWidth && !string.IsNullOrEmpty(currentLine))
                {
                    result.Add(currentLine);
                    currentLine = word;
                }
                else
                {
                    currentLine = testLine;
                }
            }
            if (!string.IsNullOrEmpty(currentLine))
                result.Add(currentLine);
            return result;
        }

        // ════════════════════════════════════════════
        //  SHAPES & IMAGES
        // ════════════════════════════════════════════

        private readonly Dictionary<ulong, uint> _scratchTextureCache = [];

        public uint GetOrCreateScratchTexture(int width, int height, Vector3 color)
        {
            byte r = (byte)(color.X * 255);
            byte g = (byte)(color.Y * 255);
            byte b = (byte)(color.Z * 255);
            ulong key = ((ulong)(uint)r << 48) | ((ulong)(uint)g << 40) | ((ulong)(uint)b << 32)
                      | ((ulong)(uint)width << 16) | (ulong)(uint)height;

            if (_scratchTextureCache.TryGetValue(key, out uint existing))
                return existing;

            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                pixels[i * 4 + 0] = r;
                pixels[i * 4 + 1] = g;
                pixels[i * 4 + 2] = b;
                pixels[i * 4 + 3] = 255;
            }

            uint tex;
            GL.GenTextures(1, &tex);
            GL.BindTexture(Const.GL_TEXTURE_2D, tex);

            fixed (byte* p = pixels)
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, width, height, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, p);

            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);

            _scratchTextureCache[key] = tex;
            return tex;
        }

        public void ClearScratchTextureCache()
        {
            foreach (var kvp in _scratchTextureCache)
            {
                uint tex = kvp.Value;
                GL.DeleteTextures(1, &tex);
            }
            _scratchTextureCache.Clear();
        }

        /// <summary>Queue an image draw. Rendered during Flush().</summary>
        public void DrawImage(float x, float y, float w, float h, uint textureId = 0, float rotation = 0f, Vector3? scratchColor = null)
        {
            uint finalTex = textureId;
            if (textureId == 0 && scratchColor != null)
                finalTex = GetOrCreateScratchTexture((int)w, (int)h, scratchColor.Value);
            _imageQueue.Add((x, y, w, h, finalTex, rotation));
        }

        /// <summary>Queue an image draw with a SUB-RECT of the texture. UVs in
        /// HUD texture space: v grows DOWNWARD (v=0 = TOP row of the image — HUD
        /// textures upload top-row-first), so uvV0 = top edge, uvV1 = bottom edge of
        /// the crop. This is how inventory icons crop a cell out of a sheet.</summary>
        public void DrawImageUV(float x, float y, float w, float h, uint textureId,
            float uvU0, float uvV0, float uvU1, float uvV1)
        {
            if (textureId == 0) return;
            _imageUvQueue.Add((x, y, w, h, textureId, uvU0, uvV0, uvU1, uvV1));
        }

        // ════════════════════════════════════════════
        //  FLUSH — render all queued batches
        // ════════════════════════════════════════════

        /// <summary>
        /// Render all queued DrawBox, DrawText, and DrawImage commands with minimal GL state changes.
        /// Boxes are grouped by colour. Text is batched sequentially (consecutive same fontSlot+colour).
        /// Images are grouped by texture ID. Call this at the end of every frame's HUD rendering.
        /// </summary>
        public void Flush()
        {
            if (_drawQueue.Count == 0 && _textQueue.Count == 0 && _imageQueue.Count == 0 && _imageUvQueue.Count == 0)
                return;

            GL.UseProgram(shaderProgram);
            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

            GL.Disable(Const.GL_DEPTH_TEST);
            GL.Enable(Const.GL_BLEND);
            // RGB: standard alpha blending. ALPHA: dst = max(src·srcA, dst·(1−srcA))
            // — box quads composite at the shader's fixed 0.6 alpha, so plain
            // BlendFunc would DRIVE THE TARGET'S ALPHA DOWN (dst → 0.6). Everything
            // that later mixes using dst-alpha (MSAA resolve, DoF composite, bloom/
            // exposure in-place passes) then treats the HUD as partially transparent
            // and the panels ghost back toward the scene. Keeping dst at ~1 fixes the
            // "HUD layer tembus cahaya" without touching the shader's 0.6 look.
            GL.BlendFuncSeparate(
                Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA,
                Const.GL_ONE, Const.GL_ONE_MINUS_SRC_ALPHA);
            OpenGL.EnableFaceCulling(false);

            int stride = 4 * sizeof(float);
            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;

            // ── 0. BOX RUNS: solids + blended boxes IN QUEUE ORDER. Consecutive
            // same kind+colour quads batch into one draw call (painter's order kept).
            // The two-queue version drew ALL solids before ALL boxes, so blended slot
            // backgrounds overdraw the solid number chips and banners queued before
            // them ("layer UI" regression: chips/banner invisible). Solids toggle
            // blending OFF and use shader MODE SOLID (alpha 1.0 — the RGBA8 scene
            // texture must stay opaque for the Viewport panel's ImGui blit). ──
            if (_drawQueue.Count > 0)
            {
                int di = 0;
                while (di < _drawQueue.Count)
                {
                    bool runSolid = _drawQueue[di].solid;
                    var runColor = _drawQueue[di].color;
                    int start = di;
                    while (di < _drawQueue.Count && _drawQueue[di].solid == runSolid &&
                           ColorsEqual(_drawQueue[di].color, runColor))
                        di++;

                    var verts = new List<float>((di - start) * 24); // 6 verts × 4 floats
                    for (int k = start; k < di; k++)
                    {
                        var (_, bx, by, bw, bh, _) = _drawQueue[k];
                        float x0 = (bx / w) * 2.0f - 1.0f;
                        float y0 = 1.0f - (by / h) * 2.0f;
                        float x1 = ((bx + bw) / w) * 2.0f - 1.0f;
                        float y1 = 1.0f - ((by + bh) / h) * 2.0f;

                        verts.Add(x0); verts.Add(y0); verts.Add(0f); verts.Add(0f);
                        verts.Add(x1); verts.Add(y1); verts.Add(0f); verts.Add(0f);
                        verts.Add(x0); verts.Add(y1); verts.Add(0f); verts.Add(0f);

                        verts.Add(x0); verts.Add(y0); verts.Add(0f); verts.Add(0f);
                        verts.Add(x1); verts.Add(y0); verts.Add(0f); verts.Add(0f);
                        verts.Add(x1); verts.Add(y1); verts.Add(0f); verts.Add(0f);
                    }

                    if (runSolid)
                    {
                        GL.Disable(Const.GL_BLEND); // opaque replace
                        UploadAndDraw(verts, runColor, new Vector3(3, 0, 0), 0, stride, 0f); // MODE SOLID (alpha 1)
                        GL.Enable(Const.GL_BLEND);  // back to the blended state the rest of Flush expects
                    }
                    else
                    {
                        UploadAndDraw(verts, runColor, new Vector3(0, 0, 0), 0, stride, 0f); // MODE BOX (0.6 look)
                    }
                }
            }

            // ── 2. TEXT: sequential batching (consecutive same fontSlot+colour) ──
            if (_textQueue.Count > 0)
            {
                int ti = 0;
                while (ti < _textQueue.Count)
                {
                    int start = ti;
                    var (fsIdx, _, _, _, color) = _textQueue[ti];
                    while (ti < _textQueue.Count &&
                           _textQueue[ti].fontSlot == fsIdx &&
                           ColorsEqual(_textQueue[ti].color, color))
                        ti++;

                    // Build vertices for batch [start..ti)
                    if (fsIdx < 0 || fsIdx >= _fontSlots.Count) continue;
                    var slot = _fontSlots[fsIdx];
                    var verts = new List<float>((ti - start) * 144); // ~24 chars × 6 verts × 4 floats = 576 per item
                    const int atlasSize = AtlasSize;

                    for (int j = start; j < ti; j++)
                    {
                        var (_, sx, sy, text, _) = _textQueue[j];
                        float x = sx;
                        foreach (char c in text)
                        {
                            if (c < 32 || c > 126) continue;
                            var bc = slot.BakedChars[c - 32];

                            float pxX = x + bc.xoff;
                            float pxY = sy + bc.yoff;
                            float pxW = bc.x1 - bc.x0;
                            float pxH = bc.y1 - bc.y0;

                            float x0 = (pxX / w) * 2.0f - 1.0f;
                            float y0 = 1.0f - (pxY / h) * 2.0f;
                            float x1 = ((pxX + pxW) / w) * 2.0f - 1.0f;
                            float y1 = 1.0f - ((pxY + pxH) / h) * 2.0f;

                            float u0 = bc.x0 / atlasSize;
                            float v0 = bc.y0 / atlasSize;
                            float u1 = bc.x1 / atlasSize;
                            float v1 = bc.y1 / atlasSize;

                            verts.Add(x0); verts.Add(y0); verts.Add(u0); verts.Add(v0);
                            verts.Add(x0); verts.Add(y1); verts.Add(u0); verts.Add(v1);
                            verts.Add(x1); verts.Add(y1); verts.Add(u1); verts.Add(v1);

                            verts.Add(x0); verts.Add(y0); verts.Add(u0); verts.Add(v0);
                            verts.Add(x1); verts.Add(y1); verts.Add(u1); verts.Add(v1);
                            verts.Add(x1); verts.Add(y0); verts.Add(u1); verts.Add(v0);

                            x += bc.xadvance;
                        }
                    }

                    if (verts.Count > 0)
                    {
                        UploadAndDraw(verts, color, new Vector3(1, 1, 1), slot.TextureID, stride, 0f);
                    }
                }
            }

            // ── 3. IMAGES: group by (textureId, rotation), render in first-occurrence order ──
            if (_imageQueue.Count > 0)
            {
                var imgGroups = new List<(uint texId, float rot, List<(float x, float y, float w, float h)> items, int order)>();
                var imgMap = new Dictionary<(uint texId, float rot), int>();

                foreach (var img in _imageQueue)
                {
                    var key = (img.texId, img.rotation);
                    if (!imgMap.TryGetValue(key, out int idx))
                    {
                        idx = imgGroups.Count;
                        imgMap[key] = idx;
                        imgGroups.Add((img.texId, img.rotation, [], imgGroups.Count));
                    }
                    imgGroups[idx].items.Add((img.x, img.y, img.w, img.h));
                }

                var whiteColor = new Vector3(1, 1, 1);
                foreach (var (texId, rot, items, _) in imgGroups)
                {
                    var verts = new List<float>(items.Count * 24);
                    foreach (var (ix, iy, iw, ih) in items)
                    {
                        float x0 = (ix / w) * 2.0f - 1.0f;
                        float y0 = 1.0f - (iy / h) * 2.0f;
                        float x1 = ((ix + iw) / w) * 2.0f - 1.0f;
                        float y1 = 1.0f - ((iy + ih) / h) * 2.0f;

                        verts.Add(x0); verts.Add(y0); verts.Add(0f); verts.Add(0f);
                        verts.Add(x1); verts.Add(y1); verts.Add(1f); verts.Add(1f);
                        verts.Add(x0); verts.Add(y1); verts.Add(0f); verts.Add(1f);

                        verts.Add(x0); verts.Add(y0); verts.Add(0f); verts.Add(0f);
                        verts.Add(x1); verts.Add(y0); verts.Add(1f); verts.Add(0f);
                        verts.Add(x1); verts.Add(y1); verts.Add(1f); verts.Add(1f);
                    }

                    if (texId != 0)
                        UploadAndDraw(verts, whiteColor, new Vector3(2, 0, 0), texId, stride, rot);
                    else
                        UploadAndDraw(verts, whiteColor, new Vector3(0, 0, 0), 0, stride, 0f);
                }
            }

            // ── 4. SUB-RECT IMAGES: one draw per icon (uvOffset shifts the quad's UVs
            // into the sheet cell — grouping by uv would defeat the point, but icon
            // counts per frame are small, tens at most). ──
            if (_imageUvQueue.Count > 0)
            {
                var whiteColor = new Vector3(1, 1, 1);
                foreach (var (ix, iy, iw, ih, texId, u0, v0, u1, v1) in _imageUvQueue)
                {
                    float x0 = (ix / w) * 2.0f - 1.0f;
                    float y0 = 1.0f - (iy / h) * 2.0f;
                    float x1 = ((ix + iw) / w) * 2.0f - 1.0f;
                    float y1 = 1.0f - ((iy + ih) / h) * 2.0f;

                    // IMAGE-mode corner UVs (0/1) OFFSET into the sub-rect.
                    var verts = new List<float>(24)
                    {
                        x0, y0, u0, v0,
                        x1, y1, u1, v1,
                        x0, y1, u0, v1,
                        x0, y0, u0, v0,
                        x1, y0, u1, v0,
                        x1, y1, u1, v1,
                    };
                    UploadAndDraw(verts, whiteColor, new Vector3(2, 0, 0), texId, stride, 0f);
                }
            }

            // Restore standard 3D rendering state: cull back faces, CCW winding order.
            // Note: using explicit GL calls ensures a clean restore regardless of any
            // prior state corruption (e.g., from other subsystems).
            GL.Enable(Const.GL_CULL_FACE);
            GL.CullFace(Const.GL_BACK);
            GL.FrontFace(Const.GL_CCW);
            GL.Disable(Const.GL_BLEND);
            GL.Enable(Const.GL_DEPTH_TEST);

            // Clear all queues
            _drawQueue.Clear();
            _solidCount = 0;
            _boxQueue.Clear();
            _textQueue.Clear();
            _imageQueue.Clear();
            _imageUvQueue.Clear();
            // Text mirror: DO NOT clear FrameTextOut here — Flush runs mid-frame and
            // the ImGui overlay reads this list AFTER the flush returns (same frame);
            // clearing at flush end wiped every mirrored string before the overlay
            // could draw it ("semua teks HUD hilang" — panel quads visible, glyphs
            // nowhere). Strings survive until DrawTextOutOverlay consumes them;
            // stale frames drop in TextOut() / DrawTextOutOverlay (frame-stamp).
            // Keep only THIS frame's occluder rects — the world-anchored overlay
            // queries them after Flush; stale frames must not occlude.
            _occluders.RemoveAll(o => o.frame != Glfw.FrameId);
        }

        /// <summary>Upload vertex data and issue a single draw call.</summary>
        private void UploadAndDraw(List<float> verts, Vector3 color, Vector3 uvScale, uint textureId, int stride, float rotation)
        {
            if (verts.Count == 0) return;

            float[] data = [.. verts];
            nuint neededSize = (nuint)(data.Length * sizeof(float));
            EnsureVBOSize(neededSize);

            fixed (float* p = data)
                GL.BufferSubData(Const.GL_ARRAY_BUFFER, 0, neededSize, p);

            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, stride, (void*)0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, stride, (void*)(2 * sizeof(float)));
            GL.EnableVertexAttribArray(1);

            GL.Uniform3f(colorLoc, color.X, color.Y, color.Z);
            GL.Uniform3f(uvScaleLoc, uvScale.X, uvScale.Y, uvScale.Z);

            if (textureId != 0)
            {
                GL.ActiveTexture(Const.GL_TEXTURE0);
                GL.BindTexture(Const.GL_TEXTURE_2D, textureId);
                GL.Uniform1i(texLoc, 0);
            }
            else
            {
                GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            }

            if (uvScale.X >= 1.9f && uvScale.X <= 2.1f) // IMAGE mode
            {
                GL.Uniform1f(rotLoc, rotation);
            }

            GL.DrawArrays(Const.GL_TRIANGLES, 0, data.Length / 4);
        }

        /// <summary>Compare two Vector3 colours for exact float equality (used for batch grouping).</summary>
        private static bool ColorsEqual(Vector3 a, Vector3 b)
        {
            return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        }

        /// <summary>Docked GameScene path: stamp the finished scene frame into the
        /// CURRENT framebuffer as an OPAQUE backdrop, THEN flush the queued HUD on top.
        /// The backdrop must NOT go through DrawImage/Flush — Flush draws boxes and
        /// text FIRST and images LAST, so a queued backdrop image overdraws every box
        /// and glyph queued before it (the in-game inventory/shop rendered washed-out
        /// with no text, while the editor preview path — which never queues a backdrop
        /// — looked correct). Opaque: blend disabled, the texture REPLACES the target.
        /// Caller must already be bound to the target framebuffer (shared FBO).</summary>
        public void FlushWithBackdrop(uint backdropTexture)
        {
            if (backdropTexture != 0)
            {
                GL.UseProgram(shaderProgram);
                GL.BindVertexArray(vao);
                GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
                GL.Disable(Const.GL_DEPTH_TEST);
                GL.Disable(Const.GL_BLEND); // opaque replace — no queue-order blending
                OpenGL.EnableFaceCulling(false);

                // Full-screen quad, IMAGE uv mode (uvScale ~2 flags the shader).
                var verts = new List<float>(24)
                {
                    -1f,  1f, 0f, 0f,
                     1f, -1f, 1f, 1f,
                    -1f, -1f, 0f, 1f,

                    -1f,  1f, 0f, 0f,
                     1f,  1f, 1f, 0f,
                     1f, -1f, 1f, 1f,
                };
                UploadAndDraw(verts, new Vector3(1, 1, 1), new Vector3(2, 0, 0), backdropTexture, 4 * sizeof(float), 0f);
            }
            Flush(); // queues draw with blending on top of the opaque backdrop
        }

        // ════════════════════════════════════════════
        //  TEXT EXTENTS (multi-font aware)
        // ════════════════════════════════════════════

        public readonly struct TextExtents
        {
            public float Width { get; }
            public float Height { get; }
            public float MinY { get; }

            public TextExtents(float width, float height, float minY)
            {
                Width = width;
                Height = height;
                MinY = minY;
            }

            public float GetCenteredBaselineY(float boxY, float boxH) => boxY + (boxH - Height) * 0.5f - MinY;
        }

        /// <summary>Compute text extents using a specific font slot. Defaults to slot 0.</summary>
        public TextExtents GetTextExtents(string text, int fontSlotIndex = 0)
        {
            if (fontSlotIndex < 0 || fontSlotIndex >= _fontSlots.Count)
                fontSlotIndex = 0;

            var chars = _fontSlots[fontSlotIndex].BakedChars;

            if (string.IsNullOrEmpty(text))
                return new TextExtents(0f, 0f, 0f);

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            bool hasGlyph = false;
            float x = 0f;

            foreach (char c in text)
            {
                if (c < 32 || c > 126) continue;
                var bc = chars[c - 32];

                float left = x + bc.xoff;
                float right = x + bc.xoff + (bc.x1 - bc.x0);
                float top = bc.yoff;
                float bottom = bc.yoff + (bc.y1 - bc.y0);

                if (!hasGlyph) { minX = left; maxX = right; minY = top; maxY = bottom; hasGlyph = true; }
                else
                {
                    if (left < minX) minX = left;
                    if (right > maxX) maxX = right;
                    if (top < minY) minY = top;
                    if (bottom > maxY) maxY = bottom;
                }

                x += bc.xadvance;
            }

            return hasGlyph
                ? new TextExtents(maxX - minX, maxY - minY, minY)
                : new TextExtents(0f, 0f, 0f);
        }

        public float MeasureText(string text) => GetTextExtents(text, 0).Width;
        public float MeasureTextHeight(string text) => GetTextExtents(text, 0).Height;

        public float GetCenteredBaselineY(string text, float boxY, float boxH, int fontSlotIndex = 0)
            => GetTextExtents(text, fontSlotIndex).GetCenteredBaselineY(boxY, boxH);

        public void DrawCenteredText(string text, float containerWidth, float y, Vector3 color, int fontSlotIndex = 0)
        {
            float x = (containerWidth - GetTextExtents(text, fontSlotIndex).Width) * 0.5f;
            DrawText(text, x, y, color, null, 0f, fontSlotIndex);
        }

        private float spinnerAngle = 0f;
        public void DrawSpinner(float x, float y, float size, uint tex, float deltaTime)
        {
            spinnerAngle += deltaTime * 4.0f;
            if (spinnerAngle > MathF.Tau) spinnerAngle -= MathF.Tau;
            DrawImage(x, y, size, size, tex, spinnerAngle, null);
        }

        // ════════════════════════════════════════════
        //  BUTTON SYSTEM
        // ════════════════════════════════════════════

        private readonly List<ButtonDef> _buttons = [];
        private bool _btnMouseWasDown = false;

        /// <summary>Register a button with a specific font slot index. Returns the button index.</summary>
        public int AddButton(string label, float x, float y, float w, float h, Action? onClick = null, ButtonStyle? style = null, int fontSlotIndex = 0)
        {
            // Compute text extents once at add-time and cache in the struct
            var ext = GetTextExtents(label, fontSlotIndex);
            var btn = new ButtonDef
            {
                Label = label, X = x, Y = y, W = w, H = h,
                OnClick = onClick, Style = style,
                FontSlotIndex = fontSlotIndex,
                CachedExtents = ext,
            };
            _buttons.Add(btn);
            return _buttons.Count - 1;
        }

        public void UpdateButtons()
        {
            Mouse.GetCursorPosition(out double mx, out double my);
            bool mouseDown = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);

            ButtonDef[] snapshot = [.. _buttons];
            int liveCount = _buttons.Count;

            for (int i = 0; i < snapshot.Length; i++)
            {
                var btn = snapshot[i];
                bool hovered = mx >= btn.X && mx <= btn.X + btn.W &&
                               my >= btn.Y && my <= btn.Y + btn.H;

                if (i < liveCount)
                {
                    var liveBtn = _buttons[i];
                    if (hovered && !liveBtn.IsHovered)
                        btn.OnHoverEnter?.Invoke();
                    else if (!hovered && liveBtn.IsHovered)
                        btn.OnHoverExit?.Invoke();
                }

                btn.IsHovered = hovered;

                if (hovered && mouseDown && !_btnMouseWasDown)
                    btn.OnClick?.Invoke();

                if (i < _buttons.Count)
                {
                    var live = _buttons[i];
                    live.IsHovered = hovered;
                    _buttons[i] = live;
                }
            }

            _btnMouseWasDown = mouseDown;
        }

        /// <summary>
        /// Draw all buttons, each using its own font slot for text rendering.
        /// Internally queues geometry and flushes at the end.
        /// </summary>
        public void DrawButtons(float time, int keyboardSelected = -1,
            Vector3? selectedBg = null, Vector3? unselectedBg = null,
            Vector3? selectedBorder = null, Vector3? unselectedBorder = null,
            Vector3? selectedText = null, Vector3? unselectedText = null,
            Vector3? selectedSidebar = null, Vector3? glowColor = null)
        {
            var selBg = selectedBg ?? new Vector3(0.22f, 0.28f, 0.45f);
            var unsBg = unselectedBg ?? new Vector3(0.10f, 0.12f, 0.18f);
            var selBr = selectedBorder ?? new Vector3(0.5f, 0.6f, 1.0f);
            var unsBr = unselectedBorder ?? new Vector3(0.15f, 0.18f, 0.25f);
            var selTx = selectedText ?? new Vector3(0.95f, 0.95f, 1.0f);
            var unsTx = unselectedText ?? new Vector3(0.6f, 0.6f, 0.7f);
            var selSb = selectedSidebar ?? new Vector3(0.4f, 0.5f, 0.9f);
            var glow  = glowColor ?? new Vector3(0.3f, 0.4f, 0.9f);

            for (int i = 0; i < _buttons.Count; i++)
            {
                var btn = _buttons[i];
                bool isSelected = keyboardSelected >= 0
                    ? (i == keyboardSelected)
                    : btn.IsHovered;

                var s = btn.Style;
                var bg = isSelected ? (s?.SelectedBg ?? selBg) : (s?.UnselectedBg ?? unsBg);
                var br = isSelected ? (s?.SelectedBorder ?? selBr) : (s?.UnselectedBorder ?? unsBr);
                var tx = isSelected ? (s?.SelectedText ?? selTx) : (s?.UnselectedText ?? unsTx);
                var sb = s?.SelectedSidebar ?? selSb;
                var gl = s?.GlowColor ?? glow;

                float bx = btn.X, by = btn.Y, bw = btn.W, bh = btn.H;

                if (isSelected)
                {
                    float glowPulse = 0.5f + 0.5f * MathF.Sin(time * 3f);
                    float glowAlpha = 0.07f + glowPulse * 0.05f;
                    DrawBox(bx - 6f, by - 5f, bw + 12f, bh + 10f, gl * glowAlpha);
                }

                DrawBox(bx, by, bw, bh, bg);
                DrawBoxHorizontalBorders(bx, by, bw, bh, 1f, br);

                if (isSelected)
                {
                    float barPulse = 0.7f + 0.3f * MathF.Sin(time * 3f);
                    DrawBox(bx - 3f, by + 4f, 3f, bh - 8f, sb * barPulse);
                }

                // Use cached text extents (computed once in AddButton) instead of GetTextExtents per frame
                var ext = btn.CachedExtents;
                float textY = ext.GetCenteredBaselineY(by, bh);
                float textPad = 8f;
                float textX;
                switch (btn.Alignment)
                {
                    case TextAlignment.Left:
                        textX = bx + textPad;
                        break;
                    case TextAlignment.Right:
                        textX = bx + bw - textPad - ext.Width;
                        break;
                    default: // Center
                        textX = bx + (bw - ext.Width) * 0.5f;
                        break;
                }
                if (btn.WordWrap)
                {
                    DrawTextWrapped(btn.Label, bx + textPad, textY, bw - textPad * 2f, tx, btn.FontSlotIndex);
                }
                else
                {
                    DrawText(btn.Label, textX, textY, tx, null, 0f, btn.FontSlotIndex);
                }
            }

            // Flush all queued button geometry immediately so buttons are rendered
            // in the correct Z-order (boxes behind text)
            Flush();
        }

        public void ClearButtons()
        {
            _buttons.Clear();
        }

        private void EnsureVBOSize(nuint neededSize)
        {
            if (neededSize <= _vboAllocatedSize)
                return;

            nuint newSize = _vboAllocatedSize;
            while (newSize < neededSize)
                newSize = (newSize == 0) ? (nuint)(64 * 1024) : newSize * 2;

            Console.WriteLine($"[HUD] Resizing VBO from {_vboAllocatedSize} to {newSize} bytes");
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
            GL.BufferData(Const.GL_ARRAY_BUFFER, newSize, (void*)0, Const.GL_DYNAMIC_DRAW);
            _vboAllocatedSize = newSize;
        }

        public IReadOnlyList<ButtonDef> Buttons => _buttons;
        public int ButtonCount => _buttons.Count;
        public ButtonDef GetButton(int index) => index >= 0 && index < _buttons.Count ? _buttons[index] : default;
        /// <summary>Number of baked font slots (external systems validate cached slot indexes against this).</summary>
        public int FontSlotCount => _fontSlots.Count;

        /// <summary>Total queued items (boxes+text+images) — diagnostics.</summary>
        public int QueuedItemCount => _drawQueue.Count + _textQueue.Count + _imageQueue.Count + _imageUvQueue.Count;

        /// <summary>The (font,size) pair this HUD was CONSTRUCTED with → always slot 0.
        /// Slot 0 is baked by the constructor before the render loop (clean GL state) and
        /// is the only bake path proven to produce a working atlas — mid-frame bakes can
        /// land with corrupt GL state and yield a GPU-empty atlas (invisible text).</summary>
        public string PrimaryFontPath { get; private set; } = "";
        public float PrimaryFontSize { get; private set; }

        /// <summary>True when (fontPath,fontSize) maps to this HUD's constructor slot 0.</summary>
        public bool IsPrimarySlot(string fontPath, float fontSize)
        {
            return MathF.Abs(fontSize - PrimaryFontSize) < 0.01f &&
                   string.Equals(Helpers.PathHelpers.Normalize(fontPath),
                                 Helpers.PathHelpers.Normalize(PrimaryFontPath),
                                 StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>One-shot bake log de-dup (same font+size can be baked by several HUDs).</summary>

        /// <summary>Delete all font slot GPU textures and clear the cache. Keeps scratch textures intact.</summary>
        public void ClearFontSlots()
        {
            foreach (var slot in _fontSlots)
            {
                uint tex = slot.TextureID;
                if (tex != 0)
                    GL.DeleteTextures(1, &tex);
            }
            _fontSlots.Clear();
            _fontSlotLookup.Clear();
        }

        /// <summary>Clean up GPU resources: all font slot textures + scratch textures.</summary>
        public void Cleanup()
        {
            ClearScratchTextureCache();
            ClearFontSlots();
        }
    }
}
