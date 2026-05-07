using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine
{
    public class Helpers
    {
        // Ini untuk Noice
        public static Vector3 CalculateNormalForNoice(float x, float z)
        {
            float off = 0.1f;
            float hL = Noise.GetHeight((x - off), z);
            float hR = Noise.GetHeight((x + off), z);
            float hD = Noise.GetHeight(x, (z - off));
            float hU = Noise.GetHeight(x,  (z + off));

            // Semakin curam tanah, semakin kuat bayangannya
            Vector3 normal = new Vector3(hL - hR, 2.0f * off, hD - hU);
            return Vector3.Normalize(normal);
        }
        public static class OGLMath
        {
            public static float ToRadians(float degrees) => degrees * (MathF.PI / 180.0f);
        }
         
    }
}
