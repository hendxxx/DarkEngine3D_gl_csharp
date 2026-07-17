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

    /// <summary>Describes a single interactive button — position, label, and callbacks.</summary>
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
    }

    public unsafe class HUD
    {
        private readonly uint vao;
        private readonly uint vbo;
        private readonly uint shaderProgram;
        private readonly int posLoc, sizeLoc, colorLoc, uvOffsetLoc, uvScaleLoc, texLoc;

        private readonly StbTrueType.stbtt_bakedchar[] bakedChars = new StbTrueType.stbtt_bakedchar[96]; // ASCII 32..126
        readonly uint fontTexture = 0;

        private const int AtlasSize = 1024;

        public unsafe HUD(string fontPath, float fontSize)
        {
            shaderProgram = Shader.GetHudShaderProgram();
            // ... ambil Uniform Location seperti kode lama Anda ...
            posLoc = GL.GetUniformLocation(shaderProgram, "position");
            sizeLoc = GL.GetUniformLocation(shaderProgram, "size");
            colorLoc = GL.GetUniformLocation(shaderProgram, "textColor");
            uvOffsetLoc = GL.GetUniformLocation(shaderProgram, "uvOffset");
            uvScaleLoc = GL.GetUniformLocation(shaderProgram, "uvScale");
            texLoc = GL.GetUniformLocation(shaderProgram, "hudTexture");

            // 1. Persiapan Vertices (Unit Quad)
            // DI KONSTRUKTOR HUD (Pastikan urutan V ini):
            float[] vertices = [
                // x, y      u, v
                0f, 1f,     0f, 0f,   // top-left
                0f, 0f,     0f, 1f,   // bottom-left
                1f, 1f,     1f, 0f,   // top-right

                0f, 0f,     0f, 1f,   // bottom-left
                1f, 0f,     1f, 1f,   // bottom-right
                1f, 1f,     1f, 0f    // top-right
            ];


            fixed (uint* pVao = &vao) GL.GenVertexArrays(1, pVao);
            fixed (uint* pVbo = &vbo) GL.GenBuffers(1, pVbo);

            nuint maxSize = 1000 * 6 * 4 * sizeof(float);
            int stride = 4 * sizeof(float); // Karena satu baris data kita adalah: X, Y, U, V

            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

            fixed (float* p = vertices)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, maxSize, (void*)0, Const.GL_DYNAMIC_DRAW);
            }
            // Atribut 0: Posisi (X, Y) -> Ambil 2 float, mulai dari index 0
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, stride, (void*)0);

            // Atribut 1: TexCoords (U, V) -> Ambil 2 float, mulai SETELAH 2 float posisi (offset 8 byte)
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, stride, (void*)(2 * sizeof(float)));

            // 2. Load & Bake TTF Font
            byte[] ttfData = File.ReadAllBytes(fontPath);
            byte[] tempBitmap = new byte[AtlasSize * AtlasSize]; // 1-channel untuk baking

            fixed (byte* pTtf = ttfData)
            fixed (byte* pTemp = tempBitmap)
            fixed (StbTrueType.stbtt_bakedchar* pChars = bakedChars)
            {
                StbTrueType.stbtt_BakeFontBitmap(pTtf, 0, fontSize, pTemp, AtlasSize, AtlasSize, 32, 96, pChars);
            }

            // 3. KONVERSI KE 4-CHANNEL (RGBA) - Agar tidak miring
            byte[] rgbaBitmap = new byte[AtlasSize * AtlasSize * 4];
            for (int i = 0; i < tempBitmap.Length; i++)
            {
                rgbaBitmap[i * 4 + 0] = 255; // R
                rgbaBitmap[i * 4 + 1] = 255; // G
                rgbaBitmap[i * 4 + 2] = 255; // B
                rgbaBitmap[i * 4 + 3] = tempBitmap[i]; // Alpha (Data font)
            }

            // 4. Upload ke GPU sebagai RGBA
            uint texID;
            GL.GenTextures(1, &texID);
            fontTexture = texID;
            GL.BindTexture(Const.GL_TEXTURE_2D, fontTexture); 

            fixed (byte* pB = rgbaBitmap)
            {
                // Gunakan GL_RGBA (0x1908) agar pas dengan alignment 4-byte default
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, AtlasSize, AtlasSize, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, pB);
            }
            GL.GenerateMipmap(Const.GL_TEXTURE_2D);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR_MIPMAP_LINEAR); // GL_LINEAR_MIPMAP_LINEAR

            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR); // MAG_FILTER


        }

        private void DrawTextBatched(string text, float startX, float startY, Vector3 textColor)
        {
            GL.UseProgram(shaderProgram);
            GL.BindVertexArray(0);
            OpenGL.EnableFaceCulling(true);

            GL.Disable(Const.GL_DEPTH_TEST);
            GL.Enable(Const.GL_BLEND);
            GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);
             
            GL.BindVertexArray(vao);
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, fontTexture);
            GL.Uniform1i(texLoc, 0);
            GL.Uniform3f(colorLoc, textColor.X, textColor.Y, textColor.Z);

            float x = startX; // Posisi kursor awal (dalam pixel)
            List<float> allVertices = [];

            foreach (char c in text)
            {
                // Pastikan hanya karakter yang ada di Atlas (ASCII 32-126)
                if (c < 32 || c > 126) continue;
                var bc = bakedChars[c - 32];

                // 1. Hitung posisi pixel murni (Gunakan koordinat layar)
                float pxX = x + bc.xoff;
                float pxY = startY + bc.yoff;
                float pxW = bc.x1 - bc.x0;
                float pxH = bc.y1 - bc.y0;

                // 2. Konversi ke NDC (-1.0 sampai 1.0)
                // Rumus NDC yang lebih stabil
                float x0 = (pxX / (float)Glfw.WindowWidth) * 2.0f - 1.0f;
                float y0 = 1.0f - (pxY / (float)Glfw.WindowHeight) * 2.0f;
                float x1 = ((pxX + pxW) / (float)Glfw.WindowWidth) * 2.0f - 1.0f;
                float y1 = 1.0f - ((pxY + pxH) / (float)Glfw.WindowHeight) * 2.0f;


                float u0 = bc.x0 / (float)AtlasSize;
                float v0 = bc.y0 / (float)AtlasSize;
                float u1 = bc.x1 / (float)AtlasSize;
                float v1 = bc.y1 / (float)AtlasSize;

                // 3. Masukkan 6 vertex per huruf ke dalam Batch
                allVertices.AddRange([
                    x0, y0, u0, v0, // Top Left
                    x0, y1, u0, v1, // Bottom Left
                    x1, y1, u1, v1, // Bottom Right

                    x0, y0, u0, v0, // Top Left
                    x1, y1, u1, v1, // Bottom Right
                    x1, y0, u1, v0  // Top Right
                ]);

                // 4. GESER X agar huruf berikutnya tidak menumpuk
                x += bc.xadvance;
            }

            if (allVertices.Count > 0)
            {
                float[] data = [.. allVertices];
                GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
                fixed (float* p = data)
                {
                    GL.BufferSubData(Const.GL_ARRAY_BUFFER, 0, (nuint)(data.Length * sizeof(float)), p);
                }
                GL.Uniform3f(uvScaleLoc, 1.0f, 1.0f, 1.0f); // MODE TEKS
                GL.DrawArrays(Const.GL_TRIANGLES, 0, allVertices.Count / 4);
            }

            OpenGL.EnableFaceCulling(false);
        } 
        public void DrawText(string text, float startX, float startY, Vector3 color, Vector3? outlineColor = null, float outlineSize = 0.0f)
        {

            if (outlineColor != null)
            {
                DrawTextBatched(text, startX - outlineSize, startY, outlineColor.GetValueOrDefault());
                DrawTextBatched(text, startX + outlineSize, startY, outlineColor.GetValueOrDefault());
                DrawTextBatched(text, startX, startY - outlineSize, outlineColor.GetValueOrDefault());
                DrawTextBatched(text, startX, startY + outlineSize, outlineColor.GetValueOrDefault());

            }

            DrawTextBatched(text, startX, startY, color);
        }
        public void DrawBox(float x, float y, float w, float h, Vector3 color)
        { 
            GL.UseProgram(shaderProgram);
            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo); // WAJIB: Ikat kembali buffer

            GL.Disable(Const.GL_DEPTH_TEST);
            OpenGL.EnableFaceCulling(false);
            GL.Enable(Const.GL_BLEND);

            // 1. Konversi Koordinat
            float x0 = (x / (float)Glfw.WindowWidth) * 2.0f - 1.0f;
            float y0 = 1.0f - (y / (float)Glfw.WindowHeight) * 2.0f;
            float x1 = ((x + w) / (float)Glfw.WindowWidth) * 2.0f - 1.0f;
            float y1 = 1.0f - ((y + h) / (float)Glfw.WindowHeight) * 2.0f;

            // 2. Data 6 titik (X, Y, U, V)
            float[] boxVertices = {
                // Triangle 1 (CCW)
                x0, y0, 0, 0,   x1, y1, 0, 0,   x0, y1, 0, 0,
                // Triangle 2 (CCW)
                x0, y0, 0, 0,   x1, y0, 0, 0,   x1, y1, 0, 0
            };


            // 3. Kirim data ke VBO
            fixed (float* p = boxVertices)
            {
                GL.BufferSubData(Const.GL_ARRAY_BUFFER, 0, (nuint)(boxVertices.Length * sizeof(float)), p);
            }

            // 4. RESET POINTER (PENTING: Pastikan shader tahu cara baca X,Y dan U,V)
            int stride = 4 * sizeof(float);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, stride, (void*)0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, stride, (void*)(2 * sizeof(float)));
            GL.EnableVertexAttribArray(1);

            // 5. Set Uniform & Draw
            GL.Uniform3f(colorLoc, color.X, color.Y, color.Z);
            GL.Uniform3f(uvScaleLoc, 0, 0, 0); // Masuk ke mode IF di shader

            GL.BindTexture(Const.GL_TEXTURE_2D, 0); // Pastikan tidak ada tekstur
            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);

            OpenGL.EnableFaceCulling(true);

            GL.Enable(Const.GL_DEPTH_TEST);
            OpenGL.EnableFaceCulling(false);
            GL.Disable(Const.GL_BLEND);
        }
        public uint CreateScratchTexture(int width, int height, Vector3 color)
        {
            byte r = (byte)(color.X * 255);
            byte g = (byte)(color.Y * 255);
            byte b = (byte)(color.Z * 255);

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
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, width, height, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, p);
            }


            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
             

            return tex;
        }

        public unsafe void DrawImage( float x, float y, float w, float h, uint textureId = 0, float rotation = 0f, Vector3? scratchColor = null)
        {
            GL.UseProgram(shaderProgram);
            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

            GL.Disable(Const.GL_DEPTH_TEST);
            OpenGL.EnableFaceCulling(false);
            GL.Enable(Const.GL_BLEND);

            // 1. Convert screen → NDC
            float x0 = (x / (float)Glfw.WindowWidth) * 2.0f - 1.0f;
            float y0 = 1.0f - (y / (float)Glfw.WindowHeight) * 2.0f;
            float x1 = ((x + w) / (float)Glfw.WindowWidth) * 2.0f - 1.0f;
            float y1 = 1.0f - ((y + h) / (float)Glfw.WindowHeight) * 2.0f;

            // 2. Vertex (pos + uv)
            float[] verts = {
                x0, y0, 0, 0,
                x1, y1, 1, 1,
                x0, y1, 0, 1,

                x0, y0, 0, 0,
                x1, y0, 1, 0,
                x1, y1, 1, 1
            };

            fixed (float* p = verts)
                GL.BufferSubData(Const.GL_ARRAY_BUFFER, 0, (nuint)(verts.Length * sizeof(float)), p);

            int stride = 4 * sizeof(float);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, stride, (void*)0);
            GL.EnableVertexAttribArray(0);

            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, stride, (void*)(2 * sizeof(float)));
            GL.EnableVertexAttribArray(1);

            uint finalTex = textureId;

            // 3. If scratchColor requested → generate scratch texture
            if (textureId == 0 && scratchColor != null)
            {
                finalTex = CreateScratchTexture((int)w, (int)h, scratchColor.Value);
            }

            // 4. If still no texture → solid color mode
            if (finalTex == 0)
            {
                GL.Uniform3f(colorLoc, 1, 1, 1);
                GL.Uniform3f(uvScaleLoc, 0, 0, 0);
                GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            }
            else
            {
                GL.Uniform3f(uvScaleLoc, 2, 0, 0);   // MODE IMAGE
                GL.Uniform3f(colorLoc, 1, 1, 1);     // tidak mempengaruhi gambar

                GL.ActiveTexture(Const.GL_TEXTURE0);
                GL.BindTexture(Const.GL_TEXTURE_2D, finalTex);
                GL.Uniform1i(GL.GetUniformLocation(shaderProgram, "hudTexture"), 0);

            }
            int rotLoc = GL.GetUniformLocation(shaderProgram, "rotation");
            GL.Uniform1f(rotLoc, rotation);


            GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);

            OpenGL.EnableFaceCulling(true);
        }

        /// <summary>Bundles the visual bounding box of a string: Width (pixel width from xoff/xadvance),
        /// Height (pixel height from yoff/glyph height), and MinY (top yoff — usually negative).
        /// Obtained via a single call to GetTextExtents, avoiding redundant iterations.</summary>
        public readonly struct TextExtents
        {
            /// <summary>Visual pixel width of the text (maxX - minX).</summary>
            public float Width { get; }
            /// <summary>Visual pixel height of the text (maxY - minY).</summary>
            public float Height { get; }
            /// <summary>Top yoff offset from baseline (usually negative, above baseline).</summary>
            public float MinY { get; }

            public TextExtents(float width, float height, float minY)
            {
                Width = width;
                Height = height;
                MinY = minY;
            }

            /// <summary>Return the baseline Y that centers this text vertically in a box.</summary>
            public float GetCenteredBaselineY(float boxY, float boxH) => boxY + (boxH - Height) * 0.5f - MinY;
        }

        /// <summary>Compute all text extents (width, height, top offset) in a single pass through the string.
        /// Combines the horizontal bounding box (minX/maxX from xoff + xadvance) and the vertical bounding
        /// box (minY/maxY from yoff + glyph height) so callers get everything with one iteration.</summary>
        public TextExtents GetTextExtents(string text)
        {
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
                var bc = bakedChars[c - 32];

                // Horizontal
                float left = x + bc.xoff;
                float right = x + bc.xoff + (bc.x1 - bc.x0);

                // Vertical
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

        /// <summary>Measure the exact pixel width of a string using the baked font glyph bounding box.
        /// Unlike a simple xadvance sum, this accounts for negative xoff (glyph overhang) and
        /// the actual rightmost extent of the last glyph — matching what DrawTextBatched renders.</summary>
        public float MeasureText(string text) => GetTextExtents(text).Width;

        /// <summary>Measure the exact visual pixel height of a string using the baked font glyph
        /// bounding box. Accounts for ascent (negative yoff) and descent (positive yoff + height).</summary>
        public float MeasureTextHeight(string text) => GetTextExtents(text).Height;

        /// <summary>Return the baseline Y position that centers the visual glyph bounding box
        /// vertically within a box at (boxY, boxH). Uses yoff + glyph height from baked chars
        /// so it works correctly even when font ascent/descent don't match fontSize exactly.</summary>
        public float GetCenteredBaselineY(string text, float boxY, float boxH)
            => GetTextExtents(text).GetCenteredBaselineY(boxY, boxH);

        /// <summary>Draw text centered horizontally within a container of the given width.
        /// The container is assumed to start at x=0. For buttons/panels at an offset (bx),
        /// use: DrawCenteredText(text, bx + btnW * 0.5f, y, color) where the second parameter
        /// is the center X of the container.</summary>
        public void DrawCenteredText(string text, float containerWidth, float y, Vector3 color)
        {
            float x = (containerWidth - GetTextExtents(text).Width) * 0.5f;
            DrawText(text, x, y, color);
        }

        private float spinnerAngle = 0f;
        public void DrawSpinner(float x, float y, float size, uint tex, float deltaTime)
        {
            spinnerAngle += deltaTime * 4.0f;
            if (spinnerAngle > MathF.Tau) spinnerAngle -= MathF.Tau;

            DrawImage(x, y, size, size, tex, spinnerAngle, null);
        }

        // ──────────────────────────────────────────────
        //  BUTTON SYSTEM — centralized hover/click/render
        // ──────────────────────────────────────────────

        private readonly List<ButtonDef> _buttons = [];
        private bool _btnMouseWasDown = false;

        /// <summary>Register a button. Returns its index for keyboard navigation.</summary>
        public int AddButton(string label, float x, float y, float w, float h, Action? onClick = null, ButtonStyle? style = null)
        {
            var btn = new ButtonDef { Label = label, X = x, Y = y, W = w, H = h, OnClick = onClick, Style = style };
            _buttons.Add(btn);
            return _buttons.Count - 1;
        }

        /// <summary>Call once per frame BEFORE DrawButtons(). Detects hover + click for all buttons.
        /// Updates _buttons[i].IsHovered and fires OnClick on mouse-press.</summary>
        public void UpdateButtons()
        {
            Mouse.GetCursorPosition(out double mx, out double my);
            bool mouseDown = Mouse.IsButtonPressed(Const.GLFW_MOUSE_BUTTON_LEFT);

            for (int i = 0; i < _buttons.Count; i++)
            {
                var btn = _buttons[i];
                bool hovered = mx >= btn.X && mx <= btn.X + btn.W &&
                               my >= btn.Y && my <= btn.Y + btn.H;

                // Hover enter/exit callbacks
                if (hovered && !btn.IsHovered)
                    btn.OnHoverEnter?.Invoke();
                else if (!hovered && btn.IsHovered)
                    btn.OnHoverExit?.Invoke();

                btn.IsHovered = hovered;

                // Click detection (edge-triggered)
                if (hovered && mouseDown && !_btnMouseWasDown)
                    btn.OnClick?.Invoke();

                if (_buttons.Count>0)
                    _buttons[i] = btn;
            }

            _btnMouseWasDown = mouseDown;
        }

        /// <summary>
        /// Draw all registered buttons with a standard style.
        /// Style palette (can be overridden via params):
        ///   - selectedBg / unselectedBg
        ///   - selectedBorder / unselectedBorder
        ///   - selectedText / unselectedText
        ///   - selectedSidebar
        ///   - glowColor
        /// Pass keyboardSelected = -1 to use IsHovered as selection indicator.
        /// </summary>
        public void DrawButtons(float time, int keyboardSelected = -1,
            Vector3? selectedBg = null, Vector3? unselectedBg = null,
            Vector3? selectedBorder = null, Vector3? unselectedBorder = null,
            Vector3? selectedText = null, Vector3? unselectedText = null,
            Vector3? selectedSidebar = null, Vector3? glowColor = null)
        {
            // Default palette
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

                // Resolve colors: per-button style overrides global palette
                var s = btn.Style;
                var bg = isSelected ? (s?.SelectedBg ?? selBg) : (s?.UnselectedBg ?? unsBg);
                var br = isSelected ? (s?.SelectedBorder ?? selBr) : (s?.UnselectedBorder ?? unsBr);
                var tx = isSelected ? (s?.SelectedText ?? selTx) : (s?.UnselectedText ?? unsTx);
                var sb = s?.SelectedSidebar ?? selSb;
                var gl = s?.GlowColor ?? glow;

                float bx = btn.X, by = btn.Y, bw = btn.W, bh = btn.H;

                // Glow behind selected
                if (isSelected)
                {
                    float glowPulse = 0.5f + 0.5f * MathF.Sin(time * 3f);
                    float glowAlpha = 0.07f + glowPulse * 0.05f;
                    DrawBox(bx - 6f, by - 5f, bw + 12f, bh + 10f, gl * glowAlpha);
                }

                // Background
                DrawBox(bx, by, bw, bh, bg);

                // Top/bottom borders
                DrawBox(bx, by, bw, 1f, br);
                DrawBox(bx, by + bh - 1f, bw, 1f, br);

                // Selected: left sidebar
                if (isSelected)
                {
                    float barPulse = 0.7f + 0.3f * MathF.Sin(time * 3f);
                    DrawBox(bx - 3f, by + 4f, 3f, bh - 8f, sb * barPulse);
                }

                // Text centered
                var ext = GetTextExtents(btn.Label);
                float textX = bx + (bw - ext.Width) * 0.5f;
                float textY = ext.GetCenteredBaselineY(by, bh);
                DrawText(btn.Label, textX, textY, tx);
            }
        }

        /// <summary>Remove all registered buttons.</summary>
        public void ClearButtons()
        {
            _buttons.Clear();
        }

        /// <summary>Access registered buttons for custom rendering or keyboard navigation.</summary>
        public IReadOnlyList<ButtonDef> Buttons => _buttons;

        /// <summary>Number of registered buttons.</summary>
        public int ButtonCount => _buttons.Count;

        /// <summary>Get the bounding rect of a registered button (for keyboard nav indicators).</summary>
        public ButtonDef GetButton(int index) => index >= 0 && index < _buttons.Count ? _buttons[index] : default;
    }
}
