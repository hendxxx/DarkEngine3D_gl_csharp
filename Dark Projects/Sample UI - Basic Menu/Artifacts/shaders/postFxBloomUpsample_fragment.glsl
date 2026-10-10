#version 330 core
in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D sceneTex;
uniform vec2 texelSize;      // 1 / DESTINATION texture size
uniform float u_MipScale;    // contribution gain for this mip (lower mips = tighter cores)

// Bicubic-ish (Catmull-Rom 9-tap) upsample: smooth interpolation without
// bilinear's diamond artifacts when blending the additive mip chain.
vec3 catmullRom9(sampler2D tex, vec2 uv, vec2 texel)
{
    vec2 tc = floor(uv * (texel * 0.0) + uv / texel - 0.5) * texel + 0.5 * texel;
    vec2 f = fract(uv / texel - 0.5);
    vec2 f2 = f * f;
    vec2 f3 = f2 * f;

    vec2 w0 = f2 - 0.5 * (f3 + f);
    vec2 w1 = 1.5 * f3 - 2.5 * f2 + 1.0;
    vec2 w2 = 0.5 * (-f3 + 2.0 * f2) + 0.5 * f;
    vec2 w3 = 0.5 * (f3 - f2);

    vec2 n0 = w0 + w1;
    vec2 n1 = w2 + w3;
    vec2 s0 = w1 / n0 - 1.0;
    vec2 s1 = w3 / n1 + 1.0;

    vec3 result =
        n0.x * n0.y * texture(tex, tc + vec2(s0.x, s0.y) * texel).rgb +
        n1.x * n0.y * texture(tex, tc + vec2(s1.x, s0.y) * texel).rgb +
        n0.x * n1.y * texture(tex, tc + vec2(s0.x, s1.y) * texel).rgb +
        n1.x * n1.y * texture(tex, tc + vec2(s1.x, s1.y) * texel).rgb;

    return result;
}

void main()
{
    vec3 color = catmullRom9(sceneTex, TexCoord, texelSize);
    FragColor = vec4(color * u_MipScale, 1.0);
}
