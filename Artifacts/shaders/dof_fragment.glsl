#version 330 core
in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D sceneTex;
uniform sampler2D u_MaskTex;     // sprite-shape focus mask (white = sharp silhouette)
uniform int   u_MaskEnabled;     // 1 = mask valid this frame
uniform int   u_MaskOnly;        // 1 = debug: sharp ONLY inside the mask
uniform vec2 texelSize;      // 1 / texture size
// Focus circle in normalized screen coords (0..1). Y is measured from the
// BOTTOM-left to match the FBO/OpenGL convention used by the quad UVs.
uniform vec2 u_FocusPoint;   // focus center, normalized
uniform float u_Radius;      // sharp radius around the focus point
uniform float u_Feather;     // blur ramp width outside the radius
uniform float u_MaxBlur;     // max blur disk radius in pixels

// Variable-blur depth of field, slider-driven:
//   - Inside the focus circle           → sharp (1 sample, passthrough).
//   - Outside, ramping over u_Feather   → increasingly wide disk blur.
// The disk uses the golden-angle spiral (uniform, noise-free sample spread)
// with per-sample radius jitter, so a 32-sample disk looks far larger than 32 taps.
void main()
{
    vec2 uv = TexCoord;

    // Distance from the focus point, in units of screen HEIGHT (aspect-correct:
    // the focus region is a circle on screen, not stretched by the window size).
    float aspect = texelSize.y / texelSize.x;   // (1/h) / (1/w) = w/h
    vec2 d = uv - u_FocusPoint;
    d.x *= aspect;
    float dist = length(d);

    // Blur amount 0..1: 0 inside the radius, rising to 1 at radius + feather.
    float t = clamp((dist - u_Radius) / max(u_Feather, 0.0001), 0.0, 1.0);
    // Smoothstep the ramp edge so the transition band has no visible seam.
    float blurAmt = t * t * (3.0 - 2.0 * t);

    // Sprite-shape focus: pixels covered by the mask (the sprite silhouette on the
    // chosen render layer) are pulled toward sharp. The 0.03 guard keeps a fully
    // white mask from leaking a faint blur over the sprite's soft alpha edge.
    if (u_MaskEnabled == 1)
    {
        float m = texture(u_MaskTex, uv).r;
        if (u_MaskOnly == 1)
            blurAmt = 1.0 - m;                    // debug: sharp ONLY the silhouette
        else
            blurAmt *= (1.0 - m);                 // circle blur ∧ NOT silhouette
    }

    // Fully sharp fast path — most of a focused scene exits here.
    if (blurAmt <= 0.001)
    {
        FragColor = texture(sceneTex, uv);
        return;
    }

    float radius = u_MaxBlur * blurAmt;         // pixels of blur for this pixel

    // Golden-angle spiral disk: consecutive samples are 137.5° apart, giving an
    // even, low-pattern distribution over the disk without a lookup table.
    const float GOLDEN = 2.39996323;            // radians
    vec3 sum = texture(sceneTex, uv).rgb;
    float wsum = 1.0;
    // Tweak the per-sample angle/radius offsets with the screen-space pixel pos —
    // breaks up any residual banding without visible noise.
    float seed = fract(dot(gl_FragCoord.xy, vec2(0.7548776662, 0.5698402961)));
    float ang0 = seed * 6.2831853;

    for (int i = 0; i < 32; i++)
    {
        float fi = float(i) + 0.5;
        float ang = ang0 + fi * GOLDEN;
        float r = sqrt(fi / 32.0);              // uniform disk density
        // Pixel-space disk: component-wise texelSize multiply turns a pixel
        // displacement into a UV displacement, so the disk stays circular on screen.
        vec2 offs = vec2(cos(ang), sin(ang)) * (r * radius) * texelSize;
        sum += texture(sceneTex, uv + offs).rgb;
        wsum += 1.0;
    }

    FragColor = vec4(sum / wsum, 1.0);
}
