using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// Pixel analysis for 9-slice borders. A bordered panel is an outer band of border art
/// around a flat stretchable fill, so the border ends where the colour stops changing and
/// a uniform region begins. This class finds those cut lines from the image's own pixels
/// so the Inspector can fill the Border (px) fields for you.
/// </summary>
public static class NineSliceDetector
{
    /// <summary>Largest per-channel difference between two colours (0..1).</summary>
    public static float ColorDist(Vector4 a, Vector4 b)
    {
        return Math.Max(
            Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)),
            Math.Max(Math.Abs(a.Z - b.Z), Math.Abs(a.W - b.W)));
    }

    public static Vector4 PixelAt(byte[] px, int imgW, int x, int y)
    {
        int i = (y * imgW + x) * 4;
        return new Vector4(px[i] / 255f, px[i + 1] / 255f, px[i + 2] / 255f, px[i + 3] / 255f);
    }

    /// <summary>Average colour of every column and every row of the source window, sampled
    /// from the middle band only so corner decoration cannot skew the opposite axis.</summary>
    public static (Vector4[] cols, Vector4[] rows) BuildProfiles(
        byte[] px, int imgW, int x0, int y0, int w, int h)
    {
        var cols = new Vector4[w];
        var rows = new Vector4[h];

        int yA = y0 + h / 4;
        int yB = y0 + h - h / 4;         // exclusive
        if (yB <= yA) yB = Math.Min(y0 + h, yA + 1);
        int xA = x0 + w / 4;
        int xB = x0 + w - w / 4;         // exclusive
        if (xB <= xA) xB = Math.Min(x0 + w, xA + 1);

        int yStep = Math.Max(1, (yB - yA) / 64);
        int xStep = Math.Max(1, (xB - xA) / 64);

        for (int x = 0; x < w; x++)
        {
            Vector4 sum = Vector4.Zero;
            int n = 0;
            for (int y = yA; y < yB; y += yStep) { sum += PixelAt(px, imgW, x0 + x, y); n++; }
            cols[x] = n > 0 ? sum / n : Vector4.Zero;
        }
        for (int y = 0; y < h; y++)
        {
            Vector4 sum = Vector4.Zero;
            int n = 0;
            for (int x = xA; x < xB; x += xStep) { sum += PixelAt(px, imgW, x, y0 + y); n++; }
            rows[y] = n > 0 ? sum / n : Vector4.Zero;
        }
        return (cols, rows);
    }

    /// <summary>Border width on one side of a colour profile. Picks the innermost visible
    /// colour step inside the outer half whose inner side settles into flat fill — that step
    /// is where the border art ends and the stretchable region begins. Falls back to the
    /// strongest step when the interior is textured (no flat run).</summary>
    public static int DetectInset(Vector4[] profile, bool fromEnd, float threshold)
    {
        int n = profile.Length;
        int limit = n / 2;
        if (limit < 2)
            return 0;

        int start = fromEnd ? n - 1 : 0;
        int step = fromEnd ? -1 : 1;
        float flatTol = threshold * 0.5f;

        int best = 0, maxCut = 0;
        float maxStep = 0f;
        for (int cut = 1; cut < limit; cut++)
        {
            int inner = start + step * cut;
            int outer = inner - step;
            float d = ColorDist(profile[outer], profile[inner]);
            if (d > maxStep) { maxStep = d; maxCut = cut; }
            if (d < threshold)
                continue;

            bool flat = true;
            for (int k = 1; k <= 3 && cut + k <= n - 1; k++)
            {
                int a = start + step * (cut + k - 1);
                int b = start + step * (cut + k);
                if (ColorDist(profile[a], profile[b]) > flatTol)
                {
                    flat = false;
                    break;
                }
            }
            if (flat)
                best = cut;
        }

        if (best == 0 && maxStep >= threshold * 1.5f)
            return maxCut;
        return best;
    }

    /// <summary>Border insets (left, right, top, bottom) for a source window, clamped so a
    /// 1px stretchable strip always survives between each opposite pair.</summary>
    public static (int left, int right, int top, int bottom) Detect(
        byte[] px, int imgW, int x0, int y0, int w, int h, float threshold = 0.06f)
    {
        var (cols, rows) = BuildProfiles(px, imgW, x0, y0, w, h);
        int left = DetectInset(cols, false, threshold);
        int right = DetectInset(cols, true, threshold);
        int top = DetectInset(rows, false, threshold);
        int bottom = DetectInset(rows, true, threshold);

        if (left + right > w - 1)
        {
            float s = (w - 1f) / Math.Max(1f, left + right);
            left = (int)(left * s);
            right = (int)(right * s);
        }
        if (top + bottom > h - 1)
        {
            float s = (h - 1f) / Math.Max(1f, top + bottom);
            top = (int)(top * s);
            bottom = (int)(bottom * s);
        }
        return (left, right, top, bottom);
    }
}
