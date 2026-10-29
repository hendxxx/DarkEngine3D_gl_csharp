// TEMPORARY verification script — approximates the 2D fog/mist pass at the same
// coordinates/textures as Effect2DSystem so the layouts can be eyeballed as a PNG.
// Delete after use (not part of the engine).
#:package System.Drawing.Common@9.0.0

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

const int W = 960, H = 540;
const float halfH = 5f;
float halfW = halfH * W / (float)H;
float cx = 0f, cy = 0f, t = 3.0f, windX = 2f;

float Hash01(int i)
{
    uint h = (uint)i * 2654435761u;
    h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
    return (h & 0x00FFFFFF) / 16777215f;
}

// ── textures (same builders as the engine, 32×32 alpha) ──
float[] BuildCloud()
{
    var lobes = new (float cx, float cy, float r)[] {
        (0.00f,-0.05f,0.60f),(-0.42f,0.05f,0.44f),(0.40f,0.08f,0.40f),
        (-0.10f,0.32f,0.34f),(0.24f,0.28f,0.30f) };
    var px = new float[32 * 32];
    for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
        {
            float dx = (x + 0.5f) / 32 * 2f - 1f, dy = (y + 0.5f) / 32 * 2f - 1f;
            float a = 0f;
            foreach (var (lx, ly, r) in lobes)
            {
                float n = 1f - MathF.Sqrt((dx - lx) * (dx - lx) + (dy - ly) * (dy - ly)) / r;
                if (n > 0f) a = MathF.Max(a, n * n * 1.7f);
            }
            a *= 0.80f + 0.20f * Hash01(x * 131 + y * 977);
            px[y * 32 + x] = Math.Clamp(a, 0f, 1f);
        }
    return px;
}

float[] BuildWisp()
{
    var px = new float[32 * 32];
    for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
        {
            float u = (x + 0.5f) / 32 * 2f - 1f, v = (y + 0.5f) / 32 * 2f - 1f;
            float wob = MathF.Sin(u * 5.2f + 0.7f) * 0.09f;
            float across = MathF.Max(0f, 1f - MathF.Abs(v + wob) * 6.5f);
            float ends = MathF.Max(0f, 1f - MathF.Abs(u) * MathF.Abs(u) * MathF.Abs(u));
            px[y * 32 + x] = MathF.Max(0f, across * across * ends);
        }
    return px;
}

var cloud = BuildCloud();
var wisp = BuildWisp();

// ── float canvas: rgb + premultiplied-ish composite ──
var buf = new float[W * H * 3];
for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
    {
        // stand-in "scene": dark sky + ground band so overlay visibility is clear
        float sky = 0.16f + 0.10f * (1f - y / (float)H);
        float g = y > H * 0.72f ? 0.22f : sky;
        int i = (y * W + x) * 3;
        buf[i] = g * 0.8f; buf[i + 1] = g * 0.95f; buf[i + 2] = g * 1.1f;
    }

float Sample(float[] tex, float u, float v)
{
    if (u < 0f || u > 1f || v < 0f || v > 1f) return 0f;
    float fx = u * 31f, fy = v * 31f;
    int x0 = (int)fx, y0 = (int)fy;
    int x1 = Math.Min(31, x0 + 1), y1 = Math.Min(31, y0 + 1);
    float tx = fx - x0, ty = fy - y0;
    float a = tex[y0 * 32 + x0] * (1 - tx) * (1 - ty) + tex[y0 * 32 + x1] * tx * (1 - ty)
            + tex[y1 * 32 + x0] * (1 - tx) * ty + tex[y1 * 32 + x1] * tx * ty;
    return a;
}

// world → screen
void Quad(float ax, float ay, float bx, float by, float[] tex, float r, float gcol, float bcol, float alpha)
{
    int x0 = (int)MathF.Floor((ax - cx + halfW) / (2 * halfW) * W);
    int x1 = (int)MathF.Ceiling((bx - cx + halfW) / (2 * halfW) * W);
    int y0 = (int)MathF.Floor((cy + halfH - by) / (2 * halfH) * H);
    int y1 = (int)MathF.Ceiling((cy + halfH - ay) / (2 * halfH) * H);
    x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
    x1 = Math.Min(W - 1, x1); y1 = Math.Min(H - 1, y1);
    for (int py = y0; py <= y1; py++)
        for (int px = x0; px <= x1; px++)
        {
            float wx = (px + 0.5f) / W * 2 * halfW + cx - halfW;
            float wy = cy + halfH - (py + 0.5f) / H * 2 * halfH;
            float u = (wx - ax) / (bx - ax), v = (wy - ay) / (by - ay);
            float a = Sample(tex, u, v) * alpha;
            if (a <= 0.002f) continue;
            int i = (py * W + px) * 3;
            buf[i] = buf[i] * (1 - a) + r * a;
            buf[i + 1] = buf[i + 1] * (1 - a) + gcol * a;
            buf[i + 2] = buf[i + 2] * (1 - a) + bcol * a;
        }
}

// ── FOG pass (defaults: coverage .75, 12 clusters, size 1.30, opacity .30, haze .35) ──
void EmitCloudMass(float x, float y, float size, float alpha, float r, float gcol, float bcol)
{
    Quad(x - size, y - size * 0.58f, x + size, y + size * 0.58f, cloud, r, gcol, bcol, alpha);
    Quad(x - size * 1.45f, y - size * 0.34f, x - size * 0.10f, y + size * 0.40f, cloud, r, gcol, bcol, alpha * 0.82f);
    Quad(x + size * 0.15f, y - size * 0.26f, x + size * 1.35f, y + size * 0.48f, cloud, r, gcol, bcol, alpha * 0.74f);
    Quad(x - size * 0.60f, y + size * 0.06f, x + size * 0.58f, y + size * 1.02f, cloud, r, gcol, bcol, alpha * 0.55f);
}

void Fog(bool enabled)
{
    if (!enabled) return;
    float a = 0.30f, sizeMul = 1.30f, coverage = 0.75f, speed = 1f;
    float windK = MathF.Abs(windX), dir = windX < -0.01f ? -1f : 1f;
    float yb = cy - halfH, zone = 2f * halfH * coverage;
    float x0w = cx - halfW * 1.2f, x1w = cx + halfW * 1.2f;
    float baseA = a * 0.35f * 0.6f;
    // haze body
    Quad(x0w, yb, x1w, yb + zone * 0.72f, [.. Enumerable.Repeat(1f, 32 * 32)], 0.72f, 0.74f, 0.78f, baseA);
    float span = halfW * 3.2f;
    for (int i = 0; i < 12; i++)
    {
        float h1 = Hash01(i * 71 + 13), h2 = Hash01(i * 37 + 5), h3 = Hash01(i * 53 + 29);
        float size = halfH * (0.52f + 0.78f * h1) * sizeMul;
        float drift = (0.35f + windK * 0.22f) * dir * (0.6f + 0.8f * h2) * speed;
        float travel = h3 * span + t * drift * halfH;
        float x = cx - halfW * 1.6f + ((travel % span) + span) % span;
        float y = yb + zone * (0.10f + 0.78f * h2)
                + MathF.Sin(t * (0.12f + 0.10f * h3) * speed + i * 1.7f) * halfH * 0.045f;
        float alpha = Math.Clamp(a * (0.60f + 0.40f * h1)
            * (0.85f + 0.15f * MathF.Sin(t * 0.25f * speed + i * 2.3f)), 0f, 1f);
        EmitCloudMass(x, y, size, alpha, 0.72f, 0.74f, 0.78f);
    }
}

// ── MIST pass (defaults: 40 wisps, height .20, length .16, thickness .018, opacity .20) ──
void Mist(bool enabled)
{
    if (!enabled) return;
    int wisps = 40;
    float zone = 2f * halfH * 0.20f;
    float len = halfW * 0.16f;
    float thick = halfH * 0.018f;
    float a = 0.20f, speed = 1f;
    float dir = windX < -0.01f ? -1f : 1f;
    float windK = 1f + MathF.Abs(windX) * 0.50f;
    float yb = cy - halfH;
    float span = halfW * 2.2f;
    for (int i = 0; i < wisps; i++)
    {
        float h1 = Hash01(i * 43 + 7), h2 = Hash01(i * 67 + 19), h3 = Hash01(i * 29 + 3);
        float w = len * (0.55f + 0.85f * h1);
        float drift = (0.6f + windK * 0.8f) * dir * (0.7f + 0.6f * h2) * speed;
        float travel = h3 * span + t * drift * halfW * 0.35f;
        float x = cx - halfW * 1.10f + ((travel % span) + span) % span;
        float stray = h3 > 0.82f ? 2.6f : 1f;
        float y = yb + zone * (0.06f + 0.86f * h2) * stray
                + MathF.Sin(t * 0.5f * speed + i * 1.9f) * zone * 0.10f;
        float alpha = Math.Clamp(a * (0.45f + 0.55f * h3)
            * (0.70f + 0.30f * MathF.Sin(t * 0.30f * speed + i)), 0f, 1f);
        float th = thick * (0.7f + 0.8f * h1);
        Quad(x - w, y - th, x + w, y + th, wisp, 0.85f, 0.88f, 0.92f, alpha);
    }
}

string mode = args.Length > 0 ? args[0] : "both";
Fog(mode is "both" or "fog");
Mist(mode is "both" or "mist");

// ── write PNG (both panels side by side) ──
var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
var bytes = new byte[W * H * 3];
for (int i = 0; i < W * H; i++)
{
    // note: bmp rows are bottom-up
    int y = H - 1 - i / W, x = i % W;
    int s = (y * W + x) * 3;
    bytes[i * 3 + 2] = (byte)Math.Clamp(buf[s] * 255f, 0f, 255f);
    bytes[i * 3 + 1] = (byte)Math.Clamp(buf[s + 1] * 255f, 0f, 255f);
    bytes[i * 3 + 0] = (byte)Math.Clamp(buf[s + 2] * 255f, 0f, 255f);
}
Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
bmp.UnlockBits(data);
string outPath = Path.Combine(AppContext.BaseDirectory, $"fog-check-{mode}.png");
bmp.Save(outPath, ImageFormat.Png);
Console.WriteLine(outPath);
