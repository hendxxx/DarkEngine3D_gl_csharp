namespace DarkEngine3D_gl_csharp.Engine
{
    public class Helpers
    {
        public static class Math
        {
            public static float ToRadians(float degrees) => degrees * (MathF.PI / 180.0f);
        }

        public static class Noise
        {
            public static float GetHeight(float x, float z, float scale = 0.1f, float amplitude = 5.0f)
            {
                // .NET tidak punya Perlin bawaan, untuk tes awal Anda bisa gunakan ini:
                // Ini mensimulasikan bukit-bukit kecil
                float noise = MathF.Sin(x * scale) + MathF.Sin(z * scale);
                return noise * amplitude;
            }
        }
    }
}
