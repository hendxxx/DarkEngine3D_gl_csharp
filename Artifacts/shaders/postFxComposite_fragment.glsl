#version 330 core
in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D sceneTex;
uniform sampler2D bloomTex;
uniform float u_BloomIntensity;
uniform float u_Exposure;
uniform float u_Gamma;

// ACES filmic tonemap (Narkowicz approximation) — the classic AAA "filmic" curve:
// lifts midtones slightly, rolls off highlights smoothly, keeps shadows deep.
vec3 acesTonemap(vec3 x)
{
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}

void main()
{
    vec3 color = texture(sceneTex, TexCoord).rgb;

    // Bloom additive composite (before tonemapping, so bloom also gets the filmic rolloff).
    vec3 bloom = texture(bloomTex, TexCoord).rgb;
    color += bloom * u_BloomIntensity;

    // Exposure → tonemap → gamma correction.
    color *= u_Exposure;
    color = acesTonemap(color);
    color = pow(color, vec3(1.0 / u_Gamma));

    FragColor = vec4(color, 1.0);
}
