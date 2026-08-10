using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using DarkEngine3D_gl_csharp.Engine.Libs;
using StbTrueTypeSharp;
using System.Numerics;

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
    }

    public unsafe class HUD
    {
        private readonly uint vao;
        private readonly uint vbo;
        private readonly uint shaderProgram;
        private readonly int posLoc, sizeLoc, colorLoc, uvOffsetLoc, uvScaleLoc, texLoc, rotLoc;
        private nuint _vboAllocatedSize = 0;

        /// <summary>Cache of baked font atlases, each for a unique (fontPath, fontSize) pair. Slot 0 is the default.</summary>
        private readonly List<FontSlot> _fontSlots = [];
        /// <summary>O(1) lookup: (fontPath, fontSize) → slot index. Updated when slots are created or cleared.</summary>
        private readonly Dictionary<(string path, float size), int> _fontSlotLookup = [];

        private const int AtlasSize = 1024;

        // ════════════════════════════════════════════
        //  BATCH RENDER QUEUES
        // ════════════════════════════════════════════

        /// <summary>Queued box draw commands. Flushed by Flush().</summary>
        private readonly List<(float x, float y, float w, float h, Vector3 color)> _boxQueue = [];

        /// <summary>Queued text draw commands. Flushed by Flush().</summary>
        private readonly List<(int fontSlot, float x, float y, string text, Vector3 color)> _textQueue = [];

        /// <summary>Queued image draw commands. Flushed by Flush().</summary>
        private readonly List<(float x, float y, float w, float h, uint texId, float rotation)> _imageQueue = [];

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

            fixed (byte* pTtf = ttfData)
            fixed (byte* pTemp = tempBitmap)
            fixed (StbTrueType.stbtt_bakedchar* pChars = slot.BakedChars)
            {
                StbTrueType.stbtt_BakeFontBitmap(pTtf, 0, slot.FontSize, pTemp, AtlasSize, AtlasSize, 32, 96, pChars);
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
        }

        // ════════════════════════════════════════════
        //  QUEUE-BASED DRAWING (deferred until Flush)
        // ════════════════════════════════════════════

        /// <summary>Queue a solid-colour rectangle. Rendered during Flush().</summary>
        public void DrawBox(float x, float y, float w, float h, Vector3 color)
        {
            _boxQueue.Add((x, y, w, h, color));
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

            if (outlineColor != null)
            {
                DrawTextBatched(text, startX - outlineSize, startY, outlineColor.GetValueOrDefault(), fontSlotIndex);
                DrawTextBatched(text, startX + outlineSize, startY, outlineColor.GetValueOrDefault(), fontSlotIndex);
                DrawTextBatched(text, startX, startY - outlineSize, outlineColor.GetValueOrDefault(), fontSlotIndex);
                DrawTextBatched(text, startX, startY + outlineSize, outlineColor.GetValueOrDefault(), fontSlotIndex);
            }

            DrawTextBatched(text, startX, startY, color, fontSlotIndex);
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
            if (_boxQueue.Count == 0 && _textQueue.Count == 0 && _imageQueue.Count == 0)
                return;

            GL.UseProgram(shaderProgram);
            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

            GL.Disable(Const.GL_DEPTH_TEST);
            GL.Enable(Const.GL_BLEND);
            GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);
            OpenGL.EnableFaceCulling(false);

            int stride = 4 * sizeof(float);
            int w = Glfw.WindowWidth;
            int h = Glfw.WindowHeight;

            // ── 1. BOXES: group by colour, render in first-occurrence order ──
            if (_boxQueue.Count > 0)
            {
                // Group by colour preserving first-occurrence order
                var boxGroups = new List<(Vector3 color, List<(float x, float y, float w, float h)> items, int order)>();
                var colorMap = new Dictionary<(float r, float g, float b), int>();

                foreach (var box in _boxQueue)
                {
                    var key = (box.color.X, box.color.Y, box.color.Z);
                    if (!colorMap.TryGetValue(key, out int idx))
                    {
                        idx = boxGroups.Count;
                        colorMap[key] = idx;
                        boxGroups.Add((box.color, [], boxGroups.Count));
                    }
                    boxGroups[idx].items.Add((box.x, box.y, box.w, box.h));
                }

                foreach (var group in boxGroups)
                {
                    var verts = new List<float>(group.items.Count * 24); // 6 verts × 4 floats
                    foreach (var (bx, by, bw, bh) in group.items)
                    {
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

                    UploadAndDraw(verts, group.color, new Vector3(0, 0, 0), 0, stride, 0f);
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

            // Restore standard 3D rendering state: cull back faces, CCW winding order.
            // Note: using explicit GL calls ensures a clean restore regardless of any
            // prior state corruption (e.g., from other subsystems).
            GL.Enable(Const.GL_CULL_FACE);
            GL.CullFace(Const.GL_BACK);
            GL.FrontFace(Const.GL_CCW);
            GL.Disable(Const.GL_BLEND);
            GL.Enable(Const.GL_DEPTH_TEST);

            // Clear all queues
            _boxQueue.Clear();
            _textQueue.Clear();
            _imageQueue.Clear();
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
                float textX = bx + (bw - ext.Width) * 0.5f;
                float textY = ext.GetCenteredBaselineY(by, bh);
                DrawText(btn.Label, textX, textY, tx, null, 0f, btn.FontSlotIndex);
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
