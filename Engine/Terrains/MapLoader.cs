using StbImageSharp;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Terrains
{
    public class MapLoader
    {
        // PERBAIKAN: Hapus kata 'readonly' agar array piksel bisa diproses oleh fungsi SmoothHeightmap
        private ushort[] _pixels;
        public ushort[] Pixels => _pixels;

        private readonly int channels;
        public int Width { get; }
        public int Height { get; }
        public float _terrainScale;
        public float _heightScale;
        public static float TerrainScale { get; set; }

        public static float HeightScale { get; set; }

        public MapLoader(string path, float terrainScale = 1.0f, float heightScale = 75.0f)
        {
            TerrainScale = terrainScale;
            HeightScale = heightScale;
            channels = 4;

            // Resolve relative heightmap paths against the exe folder.
            path = DarkEngine3D_gl_csharp.Engine.Helpers.PathHelpers.Resolve(path);

            if (path.EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
            {
                // === PROSES RAW 8-BIT YANG BENAR ===
                byte[] rawDataBytes = File.ReadAllBytes(path);

                // Karena 8-bit, 1 byte mewakili 1 piksel utuh
                int totalPixels = rawDataBytes.Length;
                int width = (int)Math.Sqrt(totalPixels);
                int height = width;

                Width = width;
                Height = height;

                _pixels = new ushort[width * height * 4];

                int maxSafePixels = width * height;
                for (int i = 0; i < maxSafePixels; i++)
                {
                    // Ambil 1 byte mentah (0 - 255)
                    byte grayValue = rawDataBytes[i];

                    // Lakukan Upsampling aman: Geser bit ke kiri (<< 8) agar nilainya 
                    // terpetakan secara proporsional ke rentang ushort (0 - 65535)
                    ushort upsampledValue = (ushort)(grayValue << 8);

                    int idx = i * 4;
                    _pixels[idx] = upsampledValue; // R (Tinggi Utama)
                    _pixels[idx + 1] = upsampledValue; // G
                    _pixels[idx + 2] = upsampledValue; // B
                    _pixels[idx + 3] = 65535;          // A
                }

                Console.WriteLine($"[MapLoader] Loaded RAW {path}: {Width}×{Height} (fileSize={rawDataBytes.Length}B, pixels={totalPixels})");
            }
            else
            {
                // === PROSES GAMBAR NON-RAW ===
                byte[] buffer = File.ReadAllBytes(path);
                var image16 = ImageResult.FromMemory(buffer, ColorComponents.RedGreenBlueAlpha);

                Width = image16.Width;
                Height = image16.Height;

                // Check if image decoded successfully
                // image16.Data could be null (decode failed) or empty (decode partially failed)
                // Also check if decoded dimensions are unreasonably small compared to file size
                // (raw heightmap files are 1 byte/pixel, so Width*Height should be close to buffer.Length)
                bool decodeFailed = Width <= 0 || Height <= 0 ||
                    image16.Data == null || image16.Data.Length == 0 ||
                    (long)Width * Height < buffer.Length / 4; // expected pixels << file size → decode failure

                if (decodeFailed)
                {
                    Console.WriteLine($"[MapLoader] ERROR: Failed to decode image '{path}' (Width={Width}, Height={Height}, fileSize={buffer.Length}B). Falling back to RAW interpretation...");

                    // Fallback: treat as RAW 8-bit
                    int totalPixels = buffer.Length;
                    Width = (int)Math.Sqrt(totalPixels);
                    Height = Width;

                    _pixels = new ushort[Width * Height * 4];
                    int maxSafePixels = Width * Height;
                    for (int i = 0; i < maxSafePixels; i++)
                    {
                        byte grayValue = i < buffer.Length ? buffer[i] : (byte)0;
                        ushort upsampledValue = (ushort)(grayValue << 8);
                        int idx = i * 4;
                        _pixels[idx] = upsampledValue;
                        _pixels[idx + 1] = upsampledValue;
                        _pixels[idx + 2] = upsampledValue;
                        _pixels[idx + 3] = 65535;
                    }

                    Console.WriteLine($"[MapLoader] Loaded RAW (fallback) '{path}': {Width}×{Height}");
                }
                else
                {
                    // Decode was successful — read pixels from image16.Data
                    _pixels = new ushort[Width * Height * 4];
                    for (int i = 0; i < Width * Height * 4; i++)
                    {
                        _pixels[i] = i < image16.Data.Length
                            ? (ushort)(image16.Data[i] << 8)
                            : (ushort)0;
                    }

                    Console.WriteLine($"[MapLoader] Loaded image '{path}': {Width}×{Height} (fileSize={buffer.Length}B)");
                }
            }

            // =========================================================================
            // KUNCI UTAMA: Jalankan filter pelembut di bawah ini untuk menghilangkan
            // riak dan undakan gerigi tajam pada bukit 8-bit Anda!
            // =========================================================================
            SmoothHeightmap16();
        }

        private void SmoothHeightmap16()
        {
            if (_pixels == null || Width <= 2 || Height <= 2) return;

            ushort[] smoothedPixels = new ushort[_pixels.Length];
            Array.Copy(_pixels, smoothedPixels, _pixels.Length);

            for (int z = 1; z < Height - 1; z++)
            {
                for (int x = 1; x < Width - 1; x++)
                {
                    float totalR = 0.0f;
                    float totalG = 0.0f;
                    float totalB = 0.0f;

                    // Box Blur 3x3 Filter untuk meratakan elevasi
                    for (int sz = -1; sz <= 1; sz++)
                    {
                        for (int sx = -1; sx <= 1; sx++)
                        {
                            int neighborIdx = ((x + sx) + (z + sz) * Width) * 4;
                            totalR += _pixels[neighborIdx];
                            totalG += _pixels[neighborIdx + 1];
                            totalB += _pixels[neighborIdx + 2];
                        }
                    }

                    int targetIdx = (x + z * Width) * 4;
                    smoothedPixels[targetIdx] = (ushort)MathF.Round(totalR / 9.0f);
                    smoothedPixels[targetIdx + 1] = (ushort)MathF.Round(totalG / 9.0f);
                    smoothedPixels[targetIdx + 2] = (ushort)MathF.Round(totalB / 9.0f);
                }
            }

            _pixels = smoothedPixels;
        }


        // Helper method - ambil height dari pixel index langsung (tanpa interpolasi)
        public float GetHeightAtPixel(int x, int z)
        {
            // Proteksi batas koordinat map
            x = Math.Clamp(x, 0, Width - 1);
            z = Math.Clamp(z, 0, Height - 1);

            // Ambil index komponen warna Red (R) dari susunan RGBA ushort
            int index = (x + z * Width) * 4;
            ushort heightValue16 = _pixels[index];

            // Konversi nilai 0-65535 menjadi float linear (0.0f sampai 1.0f)
            float normalizedHeight = heightValue16 / 65535.0f;

            // Kalikan dengan skala tinggi dunia nyata Anda
            return normalizedHeight * HeightScale;
        }


        public float GetHeight(float x, float z)
        {
            // Convert world coordinates to pixel coordinates
            float px = (x / TerrainScale) + (Width / 2f);
            float pz = (z / TerrainScale) + (Height / 2f);

            // 1. Kunci pembulatan ke bawah sekali saja
            int x0 = (int)MathF.Floor(px);
            int z0 = (int)MathF.Floor(pz);

            // 2. === PERBAIKAN UTAMA ===
            // Hitung bobot pecahan langsung dari integer yang sudah dikunci (Aman dari Bug Floating-Point)
            float fracX = px - x0;
            float fracZ = pz - z0;

            // Batasi (Clamp) agar indeks dasar tidak meluncur keluar batas array
            x0 = Math.Max(0, Math.Min(x0, Width - 1));
            z0 = Math.Max(0, Math.Min(z0, Height - 1));

            // Dapatkan indeks tetangga kanan dan bawah dengan proteksi batas maksimum
            int x1 = Math.Min(x0 + 1, Width - 1);
            int z1 = Math.Min(z0 + 1, Height - 1);

            // Ambil 4 sample dari pixel corners
            float h00 = GetHeightAtPixel(x0, z0);
            float h10 = GetHeightAtPixel(x1, z0);
            float h01 = GetHeightAtPixel(x0, z1);
            float h11 = GetHeightAtPixel(x1, z1);

            // Jalankan Bilinear interpolation
            float lowSide = h00 + fracX * (h10 - h00);
            float highSide = h01 + fracX * (h11 - h01);

            return (lowSide + fracZ * (highSide - lowSide));
        }




        // Method lama - untuk backward compatibility jika diperlukan
        public float GetHeightInterpolated(float x, float z)
        {
            return GetHeight(x, z); // Sudah interpolated di GetHeight()
        }


        // Ambil Warna (RGB) dengan Bilinear Interpolation (smooth)
        public Vector3 GetColor(float x, float z)
        {
            // Convert world coordinates to pixel coordinates
            float px = (x / TerrainScale) + (Width / 2f);
            float pz = (z / TerrainScale) + (Height / 2f);

            // Ambil 4 pixel tetangga
            int x0 = (int)MathF.Floor(px);
            int z0 = (int)MathF.Floor(pz);

            // Hitung interpolation weights (0.0 - 1.0)
            float fracX = px - x0;
            float fracZ = pz - z0;

            // Ambil 4 sample dari pixel corners
            Vector3 c00 = GetColorAtPixel(x0, z0);
            Vector3 c10 = GetColorAtPixel(x0 + 1, z0);
            Vector3 c01 = GetColorAtPixel(x0, z0 + 1);
            Vector3 c11 = GetColorAtPixel(x0 + 1, z0 + 1);

            // Bilinear interpolation untuk setiap channel
            Vector3 lowSide = c00 + fracX * (c10 - c00);
            Vector3 highSide = c01 + fracX * (c11 - c01);

            return lowSide + fracZ * (highSide - lowSide);
        }

        // Helper method - ambil color dari pixel index langsung (tanpa interpolasi)
        private Vector3 GetColorAtPixel(int px, int pz)
        {
            if (px < 0) px = 0;
            if (px >= Width) px = Width - 1;
            if (pz < 0) pz = 0;
            if (pz >= Height) pz = Height - 1;

            int idx = (pz * Width + px) * channels;
            float r = _pixels[idx] / 255f;
            float g = _pixels[idx + 1] / 255f;
            float b = _pixels[idx + 2] / 255f;


            // // bagi jadi 4 level warna berdasarkan nilai r (grayscale) untuk memberikan variasi warna yang lebih menarik
            // // paling tinggi (putih) akan lebih cerah, sedang (abu-abu) akan lebih netral, dan rendah (hijau) akan lebih gelap
            // (r, g, b) = r switch
            // {
            //     < 0.2f => (r * 0.0f, g * 0.2f, b * 0.6f),// Biru Tua    
            //     < 0.3f => (r * 0.4f, g * 0.7f, b * 0.2f), // Hijau mudah 
            //     < 0.4f => (r * 0.1f, g * 0.35f, b * 0.31f), // Hijau tua 
            //     < 0.85f => (r * 0.4f, g * 0.3f, b * 0.2f), // Coklat Tanah 
            //     _ => (r * 0.9f, g * 0.9f, b * 1.0f) // putih kebiruan untuk puncak gunung
            // };


            return new Vector3(r, g, b);
        }

        // Method untuk meningkatkan kontras heightmap
        public void EnhanceContrast(float contrastStrength = 2.0f)
        {
            // contrastStrength: 1.0 = normal, 2.0 = 2x lebih kontras, 3.0 = 3x lebih kontras
            for (int i = 0; i < _pixels.Length; i += channels)
            {
                // Ambil nilai grayscale dari R channel
                float gray = _pixels[i] / 255f;

                // Pindahkan nilai ke 0.5 sebagai pivot, kemudian perkuat
                float centered = gray - 0.5f;
                float enhanced = centered * contrastStrength;
                float result = enhanced + 0.5f;

                // Clamp ke 0-1 dan konversi ke byte
                byte newValue = (byte)(Math.Clamp(result, 0f, 1f) * 255f);

                // Update semua channel (R, G, B)
                _pixels[i] = newValue;
                _pixels[i + 1] = newValue;
                _pixels[i + 2] = newValue;
            }
        }

        // Method untuk equalize histogram (lebih agresif)
        public void EqualizeHistogram()
        {
            // Hitung histogram
            int[] histogram = new int[256];
            for (int i = 0; i < _pixels.Length; i += channels)
            {
                histogram[_pixels[i]]++;
            }

            // Hitung CDF (Cumulative Distribution Function)
            int[] cdf = new int[256];
            cdf[0] = histogram[0];
            for (int i = 1; i < 256; i++)
            {
                cdf[i] = cdf[i - 1] + histogram[i];
            }

            // Normalisasi CDF
            float cdfMin = cdf[0];
            int pixelCount = Width * Height;

            byte[] lookupTable = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                lookupTable[i] = (byte)((cdf[i] - cdfMin) * 255f / (pixelCount - 1));
            }

            // Apply lookup table
            for (int i = 0; i < _pixels.Length; i += channels)
            {
                byte originalValue = (byte)_pixels[i];
                byte newValue = lookupTable[originalValue];
                _pixels[i] = (ushort)(newValue * 257); // Scale back to 16-bit
                _pixels[i + 1] = (ushort)(newValue * 257);
                _pixels[i + 2] = (ushort)(newValue * 257);
            }
        }

        public static void GeneratePhotorealHeightmap(string path, int size)
        {
            Console.WriteLine("\r\nGenerating High-Alpine Terrain (No Edge Mountains)...");
            byte[] data = new byte[size * size];
            Random rand = new();
            float seedX = (float)rand.NextDouble() * 50000f;
            float seedY = (float)rand.NextDouble() * 50000f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (float)x / size;
                    float ny = (float)y / size;

                    // ---------------------------------------------------------
                    // 1. DOMAIN WARPING (High-Alpine Style)
                    // ---------------------------------------------------------
                    float warp = MathF.Sin(nx * 5.0f + seedX) * MathF.Cos(ny * 5.0f + seedY) * 0.2f;
                    float wx = nx + warp;
                    float wy = ny + warp;

                    float h = 0;
                    float amp = 1.0f;
                    float freq = 2.5f;

                    for (int i = 0; i < 9; i++)
                    {
                        float vx = wx * freq + (i * 1.41f) + seedX;
                        float vy = wy * freq + (i * 1.73f) + seedY;

                        float noise = 1.0f - MathF.Abs(MathF.Sin(vx) * MathF.Cos(vy));
                        h += MathF.Pow(noise, 2.8f) * amp;

                        freq *= 2.1f;
                        amp *= 0.46f;
                    }

                    // ---------------------------------------------------------
                    // 2. EDGE FALLOFF (Gunung tidak boleh muncul di pinggir)
                    // ---------------------------------------------------------
                    float cx = nx - 0.5f;
                    float cy = ny - 0.5f;

                    // 0 di tengah, 1 di pinggir (lingkaran)
                    float dist = MathF.Sqrt(cx * cx + cy * cy) * 2.0f;
                    dist = Math.Clamp(dist, 0f, 1f);

                    // Falloff halus
                    float falloff = 1.0f - MathF.Pow(dist, 2.0f);

                    // Terapkan falloff
                    h *= falloff;

                    // ---------------------------------------------------------
                    // 3. Kurangi dataran rendah (biar alpine)
                    // ---------------------------------------------------------
                    h = MathF.Max(0, h - 0.15f);

                    // ---------------------------------------------------------
                    // 4. SHARPENING (puncak lebih tajam)
                    // ---------------------------------------------------------
                    h = MathF.Pow(h, 2.0f);

                    // ---------------------------------------------------------
                    // 5. Konversi ke byte
                    // ---------------------------------------------------------
                    data[y * size + x] = (byte)(Math.Clamp(h * 150f, 0, 255));
                }
            }

            File.WriteAllBytes(path, data);
        }



    }
}
