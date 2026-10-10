#version 330 core
in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D sceneTex;
uniform sampler2D u_MaskTex;     // sprite-shape focus mask (white = sharp silhouette)
uniform int   u_MaskEnabled;     // 1 = mask valid this frame
uniform int   u_MaskOnly;        // 1 = debug: sharp ONLY inside the mask
uniform int   u_FocusShape;      // 0 = Circle (Geometric), 1 = Silhouette Only (non-geometric), 2 = Hybrid
uniform int   u_InvertMask;      // 0 = Subject sharp / bg blurred, 1 = Subject blurred / bg sharp
uniform vec2 texelSize;      // 1 / texture size
// Focus circle in normalized screen coords (0..1). Y is measured from the
// BOTTOM-left to match the FBO/OpenGL convention used by the quad UVs.
uniform vec2 u_FocusPoint;   // focus center, normalized
uniform float u_Radius;      // sharp radius around the focus point
uniform float u_Feather;     // blur ramp width outside the radius
uniform float u_MaxBlur;     // max blur disk radius in pixels

// Variable-blur depth of field:
//   - Geometric Circle: Inside the focus circle → sharp, outside → disk blur.
//   - Sprite Silhouette: Sharp region strictly matches the Player/Sprite silhouette (no circle).
//   - Inverted mode: Blur is applied to the sprite silhouette itself.
// The disk uses the golden-angle spiral (uniform, noise-free sample spread).
void main()
{
    vec2 uv = TexCoord;

    // Geometric circle calculation:
    // Distance from the focus point, in units of screen HEIGHT (aspect-correct).
    float aspect = texelSize.y / texelSize.x;   // (1/h) / (1/w) = w/h
    vec2 d = uv - u_FocusPoint;
    d.x *= aspect;
    float dist = length(d);

    // Blur amount 0..1: 0 inside the radius, rising to 1 at radius + feather.
    float t = clamp((dist - u_Radius) / max(u_Feather, 0.0001), 0.0, 1.0);
    float circleBlur = t * t * (3.0 - 2.0 * t);

    float blurAmt = circleBlur;

    // Sprite-shape focus: exact silhouette of Player sprites or Sprite2D objects.
    if (u_MaskEnabled == 1)
    {
        float m = texture(u_MaskTex, uv).r;
        if (u_FocusShape == 1 || u_MaskOnly == 1)
        {
            // Pure silhouette focus (NON-GEOMETRIC, no circle):
            blurAmt = (u_InvertMask == 1) ? m : (1.0 - m);
        }
        else if (u_FocusShape == 2)
        {
            // Hybrid: both circle and silhouette stay sharp (or inverted)
            if (u_InvertMask == 1)
                blurAmt = max(circleBlur, m);
            else
                blurAmt = circleBlur * (1.0 - m);
        }
        else
        {
            // Circle mode with silhouette subtraction if mask active
            blurAmt = circleBlur * (1.0 - m);
        }
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
