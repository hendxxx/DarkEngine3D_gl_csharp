#version 330 core
in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D sceneTex;
uniform sampler2D bloomTex;
uniform float u_BloomIntensity;
uniform float u_Exposure;
uniform float u_Gamma;

// ACES Filmic Tonemapping (Stephen Hill fit)
vec3 ACESFilm(vec3 x) {
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

// Vignette
vec3 applyVignette(vec3 color, vec2 uv, float radius, float softness) {
    float d = distance(uv, vec2(0.5));
    float vig = smoothstep(radius, radius - softness, d);
    return color * vig;
}

void main()
{
    vec3 color = texture(sceneTex, TexCoord).rgb;

    // Additive bloom (before tonemapping so bloom also gets filmic rolloff)
    vec3 bloom = texture(bloomTex, TexCoord).rgb;
    color += bloom * u_BloomIntensity;

    // Exposure
    color *= u_Exposure;

    // ACES filmic tonemap
    color = ACESFilm(color);

    // Gamma correction
    color = pow(color, vec3(1.0 / u_Gamma));

    // Vignette: darkens edges for cinematic feel
    color = applyVignette(color, TexCoord, 0.85, 0.45);

    FragColor = vec4(color, 1.0);
}
