using DarkEngine3D_gl_csharp.Engine.Libs;
using StbTrueTypeSharp;
using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual
{
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
                0f, 1f,      0f, 0f, // Atas Kiri
                0f, 0f,      0f, 1f, // Bawah Kiri
                1f, 1f,      1f, 0f, // Atas Kanan
                1f, 0f,      1f, 1f  // Bawah Kanan
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
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.MIN_FILTER, (int)Const.GL_LINEAR_MIPMAP_LINEAR); // GL_LINEAR_MIPMAP_LINEAR

            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.MIN_FILTER, (int)Const.GL_LINEAR); // MIN_FILTER
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.MAG_FILTER, (int)Const.GL_LINEAR); // MAG_FILTER


        }

        private void DrawTextBatched(string text, float startX, float startY, Vector3 textColor)
        {
            GL.UseProgram(shaderProgram);
            GL.BindVertexArray(0);
            OpenGL.EnableFaceCulling(false);

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
                x0, y0, 0, 0,  x0, y1, 0, 0,  x1, y1, 0, 0,
                x0, y0, 0, 0,  x1, y1, 0, 0,  x1, y0, 0, 0
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
        }
         
    }
}
