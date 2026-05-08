using System;
using System.IO;
using StbImageSharp;

namespace DarkEngine3D_gl_csharp.Engine
{
    // Wraps a single GL texture object loaded from an image file.
    // Always uploaded as RGBA8 so the alpha channel is available for cutout shaders.
    public unsafe class Texture : IDisposable
    {
        public uint Id { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public Texture(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Texture file not found: {path}");

            using var stream = File.OpenRead(path);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            Width = image.Width;
            Height = image.Height;

            uint id;
            GL.GenTextures(1, &id);
            Id = id;

            GL.BindTexture(Const.GL_TEXTURE_2D, Id);

            // Default filtering: bilinear, no mipmaps (we don't have a mipmap-generation function bound).
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
            // Wrap: REPEAT works for tiled textures; CLAMP_TO_EDGE would be safer for atlases.
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_REPEAT);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_REPEAT);

            fixed (byte* p = image.Data)
            {
                GL.TexImage2D(
                    Const.GL_TEXTURE_2D,
                    0,
                    (int)Const.GL_RGBA,
                    Width, Height, 0,
                    Const.GL_RGBA, Const.GL_UNSIGNED_BYTE,
                    p);
            }

            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        }

        // Bind to texture unit `unit` (0 by default).
        public void Bind(int unit = 0)
        {
            GL.ActiveTexture((uint)((int)Const.GL_TEXTURE0 + unit));
            GL.BindTexture(Const.GL_TEXTURE_2D, Id);
        }

        public void Dispose()
        {
            if (Id != 0)
            {
                uint id = Id;
                GL.DeleteTextures(1, &id);
                Id = 0;
            }
            GC.SuppressFinalize(this);
        }
    }
}
