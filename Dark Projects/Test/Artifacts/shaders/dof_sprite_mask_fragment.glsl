#version 330 core
in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D spriteTex;
// Silhouette expansion in MASK pixels (mask runs at half scene resolution):
// 0 = exact alpha, 1-3 = slightly grown shape so the sharp region comfortably
// covers the sprite after the blur eats into its edge.
uniform float u_ExpandPx;
// Alpha bias: raises coverage so semi-transparent pixels count as inside.
uniform float u_AlphaBias;

void main()
{
    float a = texture(spriteTex, TexCoord).a;

    // Cheap dilation: take the max alpha of 4 neighbors one texel out, weighted
    // by the expansion factor. At half-res mask scale, one texel ≈ 2 scene px.
    if (u_ExpandPx > 0.001)
    {
        vec2 ts = vec2(textureSize(spriteTex, 0));
        vec2 o = u_ExpandPx / max(ts, vec2(1.0));
        float n = max(texture(spriteTex, TexCoord + vec2(o.x, 0.0)).a,
                      texture(spriteTex, TexCoord - vec2(o.x, 0.0)).a);
        n = max(n, texture(spriteTex, TexCoord + vec2(0.0, o.y)).a);
        n = max(n, texture(spriteTex, TexCoord - vec2(0.0, o.y)).a);
        a = max(a, n * min(u_ExpandPx, 1.0));
    }

    a = clamp(a + u_AlphaBias, 0.0, 1.0);
    FragColor = vec4(a, a, a, 1.0);
}
