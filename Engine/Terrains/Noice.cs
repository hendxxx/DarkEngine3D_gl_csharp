namespace DarkEngine3D_gl_csharp.Engine.Terrains
{
    public static class Noise
    {
        // Seed untuk membuat variasi setiap kali app dijalankan
        private static readonly int _seed = 12345;

        public static float GetHeight(float x, float z)
        {
            // Parameter untuk mengatur bentuk tanah
            float scale = 0.05f;     // Semakin kecil, semakin luas bukitnya
            float amplitude = 15.0f; // Tinggi maksimal gunung

            // Layer 1: Bentuk utama (Bukit besar)
            float h = RawNoise(x * scale, z * scale) * amplitude;

            // Layer 2: Detail (Batu-batuan/kerikil)
            h += RawNoise(x * scale * 4, z * scale * 4) * (amplitude * 0.2f);

            return h;
        }

        // Fungsi Noise dasar (Sederhana tapi efektif)
        private static float RawNoise(float x, float z)
        {
            int nx = (int)MathF.Floor(x);
            int nz = (int)MathF.Floor(z);
            float fx = x - nx;
            float fz = z - nz;

            // Smoothstep agar transisi antar titik halus
            float sx = fx * fx * (3 - 2 * fx);
            float sz = fz * fz * (3 - 2 * fz);

            float n00 = Hash(nx, nz);
            float n10 = Hash(nx + 1, nz);
            float n01 = Hash(nx, nz + 1);
            float n11 = Hash(nx + 1, nz + 1);

            // Interpolasi Bilinear
            float ix0 = n00 + sx * (n10 - n00);
            float ix1 = n01 + sx * (n11 - n01);
            return ix0 + sz * (ix1 - ix0);
        }

        private static float Hash(int x, int z)
        {
            // Algoritma hashing sederhana untuk hasilkan angka acak dari koordinat
            long n = x + z * 57 + _seed;
            n = (n << 13) ^ n;
            return (float)(1.0 - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0);
        }
    }

}
