#version 400 core
out vec4 FragColor;

in vec2 vUV;

uniform vec4  uColor;       // rgb + opacity
uniform float uDiscScale;   // disc radius relative to the quad half-size
uniform float uGlow;        // 1 = soft halo outside the disc (sun), 0 = flat disc
uniform float uUseTexture;  // 1 = sample uTex across the disc (moon)
uniform sampler2D uTex;

void main()
{
    vec2 p = vUV * 2.0 - 1.0;
    float d = length(p) / max(uDiscScale, 1e-4);

    float disc = 1.0 - smoothstep(0.94, 1.0, d);
    vec3 color = uColor.rgb;
    float alpha = disc;

    if (uUseTexture > 0.5)
    {
        vec2 uv = p / max(uDiscScale, 1e-4) * 0.5 + 0.5;
        vec4 texel = texture(uTex, uv);
        float lum = dot(texel.rgb, vec3(0.2126, 0.7152, 0.0722));
        alpha *= smoothstep(0.03, 0.14, lum);
        color *= texel.rgb * 1.35;
    }
    else if (uGlow > 0.5 && d > 1.0)
    {
        // Halo outside the sun core — fades out to the quad edge.
        alpha = exp(-(d - 1.0) * 2.2) * 0.35;
    }

    FragColor = vec4(color, alpha * uColor.a);
}
