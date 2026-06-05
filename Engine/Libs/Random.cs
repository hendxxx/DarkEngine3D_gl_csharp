
namespace DarkEngine3D_gl_csharp.Engine.Utils
{
    public static class RandomExtensions
    {
        public static float NextFloat(this Random r, float min, float max)
        {
            return min + (float)r.NextDouble() * (max - min);
        }
    }
}
