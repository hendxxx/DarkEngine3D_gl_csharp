using DarkEngine3D_gl_csharp.Engine.Helpers;
using StbImageSharp;

namespace DarkEngine3D_gl_csharp.Engine.Libs
{
    public class Texture
    {
        public uint ID { get; private set; }

        public Texture(string texturePath)
        {
            ID = LoadTexture(texturePath);
        }

        public void Bind()
        {
            GL.ActiveTexture(Const.GL_TEXTURE0);
            GL.BindTexture(Const.GL_TEXTURE_2D, ID);
        }

        private unsafe uint LoadTexture(string path)
        {
            // Resolve relative paths against the exe folder so textures load anywhere.
            path = PathHelpers.Resolve(path);

            uint textureID;
            GL.GenTextures(1, &textureID);
            GL.BindTexture(Const.GL_TEXTURE_2D, textureID);

            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            fixed (byte* ptr = image.Data)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                              image.Width, image.Height, 0,
                              Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, ptr);
            }

            GL.GenerateMipmap(Const.GL_TEXTURE_2D);
            GL.TexParameterf(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAX_ANISOTROPY, 16.0f);

            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_REPEAT);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_REPEAT);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR_MIPMAP_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);

            return textureID;
        }
    }
}