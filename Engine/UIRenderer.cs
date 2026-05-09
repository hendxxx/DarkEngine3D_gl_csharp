using System;
using System.Globalization;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine
{
    public unsafe class UIRenderer : IDisposable
    {
        private uint vao, vbo;
        private uint shader;
        private int resLoc, colorLoc;

        // Display state — game logic can write to these later.
        public static int CurrentHP = 100;
        public static int MaxHP = 100;
        public static float HPAccumulator = 100f; // Track HP as float for smooth regen
        public static float CurrentStamina = Const.STAMINA_MAX;
        public static float MaxStamina = Const.STAMINA_MAX;

        // Set by MovingObjects when the player gets hit; ticked down inside Render().
        public static float HitFlashTimer = 0f;
        private const float HitFlashDuration = 0.4f;

        // Reused CPU-side scratch buffer (avoids per-frame allocation).
        // Each vertex is 2 floats (x, y in pixels). Plenty for any single primitive.
        private readonly float[] _scratch = new float[256 * 2];

        // 7-segment bit flags
        private const int SA = 1, SB = 2, SC = 4, SD = 8, SE = 16, SF = 32, SG = 64;

        // Dedicated UI vertex shader: takes pixel coordinates + screen size,
        // outputs NDC directly. No view/projection matrices, no transpose ambiguity.
        private const string VS = @"#version 330 core
layout(location = 0) in vec2 aPos;
uniform vec3 uResolution; // xy = pixel size; z unused (only Uniform3f is bound in this engine)
void main() {
    vec2 ndc = vec2(
        aPos.x / uResolution.x * 2.0 - 1.0,
        1.0 - aPos.y / uResolution.y * 2.0
    );
    gl_Position = vec4(ndc, 0.0, 1.0);
}";

        private const string FS = @"#version 330 core
out vec4 FragColor;
uniform vec3 uColor;
void main() {
    FragColor = vec4(uColor, 1.0);
}";

        public UIRenderer()
        {
            // Compile dedicated shaders for the UI overlay.
            uint vs = GL.CreateShader(Const.GL_VERTEX_SHADER);
            GL.ShaderSource(vs, VS);
            GL.CompileShader(vs);

            uint fs = GL.CreateShader(Const.GL_FRAGMENT_SHADER);
            GL.ShaderSource(fs, FS);
            GL.CompileShader(fs);

            shader = GL.CreateProgram();
            GL.AttachShader(shader, vs);
            GL.AttachShader(shader, fs);
            GL.LinkProgram(shader);

            // Shader objects can be safely deleted once linked.
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);

            resLoc = GL.GetUniformLocation(shader, "uResolution");
            colorLoc = GL.GetUniformLocation(shader, "uColor");

            fixed (uint* p = &vao) GL.GenVertexArrays(1, p);
            fixed (uint* p = &vbo) GL.GenBuffers(1, p);

            GL.BindVertexArray(vao);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, Const.GL_FLOAT, false, 2 * sizeof(float), (void*)0);
            GL.BindVertexArray(0);
        }

        public void Render(int screenW, int screenH, float fps, Vector3 camPos, int selectedSlot, float deltaTime)
        {
            // Tick down the bump-flash overlay
            if (HitFlashTimer > 0f) HitFlashTimer = MathF.Max(0f, HitFlashTimer - deltaTime);

            GL.UseProgram(shader);
            GL.Disable(Const.GL_DEPTH_TEST);
            GL.Disable(Const.GL_CULL_FACE);

            // Tell the shader the screen dimensions so it can map pixels to NDC.
            GL.Uniform3f(resLoc, screenW, screenH, 0f);

            GL.BindVertexArray(vao);

            DrawTopLeftStats(fps, camPos);
            DrawStatusBars(screenW, screenH);
            DrawQuickSlots(screenW, screenH, selectedSlot);

            // Red border flash when the player just took damage.
            if (HitFlashTimer > 0f)
            {
                float intensity = MathF.Min(1f, HitFlashTimer / HitFlashDuration);
                DrawHitFlashFrame(screenW, screenH, intensity);
            }

            // Persistent "DEAD" overlay while HP is zero.
            if (CurrentHP <= 0)
            {
                DrawDeadOverlay(screenW, screenH);
            }

            GL.BindVertexArray(0);
            GL.Enable(Const.GL_DEPTH_TEST);
            GL.Enable(Const.GL_CULL_FACE);
        }

        // 4 thin red rectangles around the screen edges — gets thicker the fresher the hit.
        private void DrawHitFlashFrame(int sw, int sh, float intensity)
        {
            float thick = 28f * intensity;
            Vector3 red = new(0.9f * intensity, 0.05f, 0.05f);
            DrawFilledQuad(0,            0,            sw, thick, red);
            DrawFilledQuad(0,            sh - thick,   sw, thick, red);
            DrawFilledQuad(0,            0,            thick, sh, red);
            DrawFilledQuad(sw - thick,   0,            thick, sh, red);
        }

        // Dark-red border + big "DEAD" text in the middle.
        private void DrawDeadOverlay(int sw, int sh)
        {
            // Border
            float thick = 50f;
            Vector3 dark = new(0.45f, 0.0f, 0.0f);
            DrawFilledQuad(0,            0,            sw, thick, dark);
            DrawFilledQuad(0,            sh - thick,   sw, thick, dark);
            DrawFilledQuad(0,            0,            thick, sh, dark);
            DrawFilledQuad(sw - thick,   0,            thick, sh, dark);

            // "DEAD" centered
            string txt = "DEAD";
            float cw = 56f, ch = 90f, gap = 18f;
            float textW = txt.Length * cw + (txt.Length - 1) * gap;
            float tx = (sw - textW) * 0.5f;
            float ty = (sh - ch) * 0.5f;
            DrawString(tx, ty, cw, ch, gap, txt, new Vector3(1f, 0.15f, 0.15f));
        }

        // ---------- Sections ----------

        private void DrawTopLeftStats(float fps, Vector3 pos)
        {
            float x0 = 20f, y0 = 20f;
            float cw = 14f, ch = 22f, gap = 6f;
            float lineH = ch + 10f;
            Vector3 white = new(1, 1, 1);
            Vector3 cyan = new(0.6f, 0.95f, 1f);

            DrawString(x0, y0, cw, ch, gap, "FPS", cyan);
            DrawNumber(x0 + 3 * (cw + gap) + 6, y0, cw, ch, gap, fps, 0, white);

            DrawChar(x0,                       y0 + lineH * 1, cw, ch, 'X', cyan);
            DrawNumber(x0 + (cw + gap) + 6,    y0 + lineH * 1, cw, ch, gap, pos.X, 2, white);

            DrawChar(x0,                       y0 + lineH * 2, cw, ch, 'Y', cyan);
            DrawNumber(x0 + (cw + gap) + 6,    y0 + lineH * 2, cw, ch, gap, pos.Y, 2, white);

            DrawChar(x0,                       y0 + lineH * 3, cw, ch, 'Z', cyan);
            DrawNumber(x0 + (cw + gap) + 6,    y0 + lineH * 3, cw, ch, gap, pos.Z, 2, white);
        }

        private void DrawStatusBars(int sw, int sh)
        {
            float w = 280f, h = 28f;
            float x = 24f;
            float gapBetweenBars = 6f;

            // HP bar — bottom
            float yHP = sh - h - 24f;
            float hpPct = MathF.Max(0f, MathF.Min(1f, (float)CurrentHP / MaxHP));
            Vector3 hpFill = hpPct > 0.5f
                ? Vector3.Lerp(new Vector3(1f, 0.95f, 0.2f), new Vector3(0.2f, 0.85f, 0.25f), (hpPct - 0.5f) * 2f)
                : Vector3.Lerp(new Vector3(0.85f, 0.15f, 0.15f), new Vector3(1f, 0.95f, 0.2f), hpPct * 2f);
            DrawStatusBar(x, yHP, w, h, hpPct, hpFill);

            // Stamina bar — sits directly above the HP bar.
            float yST = yHP - h - gapBetweenBars;
            float stPct = MathF.Max(0f, MathF.Min(1f, (MaxStamina > 0f) ? CurrentStamina / MaxStamina : 0f));
            // Red when below the slow-down threshold (30%), cyan when full, yellow in between.
            Vector3 stFill = stPct < 0.3f
                ? Vector3.Lerp(new Vector3(0.9f, 0.2f, 0.2f), new Vector3(1.0f, 0.8f, 0.1f), stPct / 0.3f)
                : Vector3.Lerp(new Vector3(1.0f, 0.8f, 0.1f), new Vector3(0.3f, 0.7f, 1.0f), (stPct - 0.3f) / 0.7f);
            DrawStatusBar(x, yST, w, h, stPct, stFill);
        }

        // Renders one bar: dark background, fill, white outline, and a centered "<pct>%" label.
        private void DrawStatusBar(float x, float y, float w, float h, float pct, Vector3 fillColor)
        {
            DrawFilledQuad(x, y, w, h, new Vector3(0.12f, 0.12f, 0.12f));
            DrawFilledQuad(x + 2f, y + 2f, (w - 4f) * pct, h - 4f, fillColor);
            DrawQuadOutline(x, y, w, h, new Vector3(0.95f, 0.95f, 0.95f));

            // Centered "NN%" — kept short so it fits inside any bar fill state.
            int pctInt = (int)MathF.Round(pct * 100f);
            string txt = pctInt + "%";
            float cw = 11f, ch = 16f, cgap = 4f;
            float textW = txt.Length * cw + (txt.Length - 1) * cgap;
            float tx = x + (w - textW) * 0.5f;
            float ty = y + (h - ch) * 0.5f;
            DrawString(tx, ty, cw, ch, cgap, txt, new Vector3(1f, 1f, 1f));
        }

        private void DrawQuickSlots(int sw, int sh, int selected)
        {
            const int count = 6;
            float box = 64f, slotGap = 8f;
            float total = box * count + slotGap * (count - 1);
            float x0 = (sw - total) * 0.5f;
            float y = sh - box - 20f;

            for (int i = 0; i < count; i++)
            {
                float x = x0 + i * (box + slotGap);
                bool sel = (selected == i + 1);

                Vector3 bg = sel ? new Vector3(0.30f, 0.32f, 0.55f) : new Vector3(0.10f, 0.10f, 0.12f);
                DrawFilledQuad(x, y, box, box, bg);

                Vector3 outline = sel ? new Vector3(1f, 0.82f, 0.2f) : new Vector3(0.65f, 0.65f, 0.7f);
                DrawQuadOutline(x, y, box, box, outline);

                DrawChar(x + 8f, y + 8f, 12f, 18f, (char)('0' + i + 1), outline);
            }
        }

        // ---------- Primitive draws ----------

        private void DrawFilledQuad(float x, float y, float w, float h, Vector3 color)
        {
            GL.Uniform3f(colorLoc, color.X, color.Y, color.Z);
            int j = 0;
            void V(float a, float b) { _scratch[j++] = a; _scratch[j++] = b; }
            V(x, y);     V(x + w, y);     V(x + w, y + h);
            V(x, y);     V(x + w, y + h); V(x, y + h);
            UploadAndDraw(j, Const.GL_TRIANGLES);
        }

        private void DrawQuadOutline(float x, float y, float w, float h, Vector3 color)
        {
            GL.Uniform3f(colorLoc, color.X, color.Y, color.Z);
            int j = 0;
            void V(float a, float b) { _scratch[j++] = a; _scratch[j++] = b; }
            V(x, y);         V(x + w, y);
            V(x + w, y);     V(x + w, y + h);
            V(x + w, y + h); V(x, y + h);
            V(x, y + h);     V(x, y);
            UploadAndDraw(j, Const.GL_LINES);
        }

        private void UploadAndDraw(int floatCount, uint mode)
        {
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
            fixed (void* p = _scratch)
            {
                GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(floatCount * sizeof(float)), p, Const.GL_DYNAMIC_DRAW);
            }
            GL.DrawArrays(mode, 0, floatCount / 2);
        }

        // ---------- Vector font ----------

        private void DrawString(float x, float y, float cw, float ch, float gap, string s, Vector3 color)
        {
            float cx = x;
            for (int i = 0; i < s.Length; i++)
            {
                DrawChar(cx, y, cw, ch, s[i], color);
                cx += cw + gap;
            }
        }

        private void DrawNumber(float x, float y, float cw, float ch, float gap, float value, int decimals, Vector3 color)
        {
            string s = decimals == 0
                ? ((int)MathF.Round(value)).ToString(CultureInfo.InvariantCulture)
                : value.ToString("F" + decimals, CultureInfo.InvariantCulture);
            DrawString(x, y, cw, ch, gap, s, color);
        }

        private void DrawChar(float x, float y, float w, float h, char ch, Vector3 color)
        {
            GL.Uniform3f(colorLoc, color.X, color.Y, color.Z);
            float mh = h * 0.5f;
            int j = 0;

            void Seg(float ax, float ay, float bx, float by)
            {
                _scratch[j++] = x + ax; _scratch[j++] = y + ay;
                _scratch[j++] = x + bx; _scratch[j++] = y + by;
            }

            int s = 0;
            char c = char.ToUpperInvariant(ch);
            switch (c)
            {
                case '0': s = SA | SB | SC | SD | SE | SF; break;
                case '1': s = SB | SC; break;
                case '2': s = SA | SB | SG | SE | SD; break;
                case '3': s = SA | SB | SG | SC | SD; break;
                case '4': s = SF | SG | SB | SC; break;
                case '5': s = SA | SF | SG | SC | SD; break;
                case '6': s = SA | SF | SG | SE | SC | SD; break;
                case '7': s = SA | SB | SC; break;
                case '8': s = SA | SB | SC | SD | SE | SF | SG; break;
                case '9': s = SA | SB | SC | SD | SF | SG; break;
                case 'F': s = SA | SF | SG | SE; break;
                case 'P': s = SA | SB | SF | SG | SE; break;
                case 'S': s = SA | SF | SG | SC | SD; break;
                case 'H': s = SB | SC | SE | SF | SG; break;
                case 'A': s = SA | SB | SC | SE | SF | SG; break;
                case 'D': s = SB | SC | SD | SE | SG; break; // lowercase-d shape (distinguishes from 0)
                case 'E': s = SA | SD | SE | SF | SG; break;
                case 'X':
                    Seg(0, 0, w, h);
                    Seg(w, 0, 0, h);
                    if (j > 0) UploadAndDraw(j, Const.GL_LINES);
                    return;
                case 'Y':
                    Seg(0, 0, w * 0.5f, mh);
                    Seg(w, 0, w * 0.5f, mh);
                    Seg(w * 0.5f, mh, w * 0.5f, h);
                    if (j > 0) UploadAndDraw(j, Const.GL_LINES);
                    return;
                case 'Z':
                    Seg(0, 0, w, 0);
                    Seg(w, 0, 0, h);
                    Seg(0, h, w, h);
                    if (j > 0) UploadAndDraw(j, Const.GL_LINES);
                    return;
                case '-': s = SG; break;
                case '.':
                    Seg(w * 0.5f, h - 3f, w * 0.5f, h);
                    if (j > 0) UploadAndDraw(j, Const.GL_LINES);
                    return;
                case '%':
                    {
                        // Top-left tiny square + bottom-right tiny square + diagonal between them.
                        float bw = w * 0.35f, bh = h * 0.35f;
                        // top-left box
                        Seg(0,  0,  bw, 0);
                        Seg(bw, 0,  bw, bh);
                        Seg(bw, bh, 0,  bh);
                        Seg(0,  bh, 0,  0);
                        // bottom-right box
                        float bx = w - bw, by = h - bh;
                        Seg(bx, by, w,  by);
                        Seg(w,  by, w,  h);
                        Seg(w,  h,  bx, h);
                        Seg(bx, h,  bx, by);
                        // diagonal slash
                        Seg(w, 0, 0, h);
                        if (j > 0) UploadAndDraw(j, Const.GL_LINES);
                        return;
                    }
                case ' ': return;
                default: return;
            }

            if ((s & SA) != 0) Seg(0, 0, w, 0);
            if ((s & SB) != 0) Seg(w, 0, w, mh);
            if ((s & SC) != 0) Seg(w, mh, w, h);
            if ((s & SD) != 0) Seg(0, h, w, h);
            if ((s & SE) != 0) Seg(0, mh, 0, h);
            if ((s & SF) != 0) Seg(0, 0, 0, mh);
            if ((s & SG) != 0) Seg(0, mh, w, mh);

            if (j > 0) UploadAndDraw(j, Const.GL_LINES);
        }

        public void Dispose()
        {
            if (vao != 0) { fixed (uint* p = &vao) GL.DeleteVertexArrays(1, p); vao = 0; }
            if (vbo != 0) { fixed (uint* p = &vbo) GL.DeleteBuffers(1, p); vbo = 0; }
            GC.SuppressFinalize(this);
        }
    }
}
