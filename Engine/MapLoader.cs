using System.Numerics;
using System.Drawing;

namespace DarkEngine3D_gl_csharp.Engine
{
    public class MapLoader
    {
        //private readonly Bitmap _map;
        //public int Width => _map.Width;
        //public int Height => _map.Height;

        //public MapLoader(string path)
        //{
        //    _map = new Bitmap(path);
        //}

        //// Ambil Ketinggian (Y) berdasarkan kecerahan pixel
        //public float GetHeight(int x, int z)
        //{
        //    if (x < 0 || x >= Width || z < 0 || z >= Height) return 0f;
        //    Color pixel = _map.GetPixel(x, z);
        //    // Semakin putih semakin tinggi, dikali skala (misal 50.0f)
        //    return (pixel.R / 255f) * 50.0f;
        //}

        //// Ambil Warna (RGB)
        //public Vector3 GetColor(int x, int z)
        //{
        //    if (x < 0 || x >= Width || z < 0 || z >= Height) return new Vector3(0, 1, 0); // Default Hijau
        //    Color pixel = _map.GetPixel(x, z);
        //    return new Vector3(pixel.R / 255f, pixel.G / 255f, pixel.B / 255f);
        //}
    }
}
