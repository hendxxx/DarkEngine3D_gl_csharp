using System;
using System.Numerics;
using System.IO;
using StbImageSharp;

namespace DarkEngine3D_gl_csharp.Engine
{
    public class MapLoader
    {
        private readonly byte[] _pixels;
        private readonly int _channels;
        public int Width { get; }
        public int Height { get; }

        public MapLoader(string path)
        {
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            Width = image.Width;
            Height = image.Height;
            _channels = (int)image.Comp; // typically 3 or 4
            _pixels = image.Data;
        }

        // Ambil Ketinggian (Y) berdasarkan kecerahan pixel
        // Coordinat dunia dipusatkan ke tengah heightmap seperti sebelumnya
        public float GetHeight(int x, int z)
        {
            int px = x + (Width / 2);
            int pz = z + (Height / 2);

            if (px < 0) px = 0;
            if (px >= Width) px = Width - 1;
            if (pz < 0) pz = 0;
            if (pz >= Height) pz = Height - 1;

            int idx = (pz * Width + px) * _channels;
            byte r = _pixels[idx];
            // If image has alpha channel, channels >= 4; we still use R for height
            return (r / 255f) * 25.0f;
        }

        // Ambil Warna (RGB)
        public Vector3 GetColor(int x, int z)
        {
            int px = x + (Width / 2);
            int pz = z + (Height / 2);

            if (px < 0) px = 0;
            if (px >= Width) px = Width - 1;
            if (pz < 0) pz = 0;
            if (pz >= Height) pz = Height - 1;

            int idx = (pz * Width + px) * _channels;
            float r = _pixels[idx] / 255f;
            float g = _pixels[idx + 1] / 255f;
            float b = _pixels[idx + 2] / 255f;
            return new Vector3(r, g, b);
        }
    }
}
