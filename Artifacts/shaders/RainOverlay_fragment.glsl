#version 400 core

in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uSceneTex;
uniform float uTime;
uniform float uRainAmount;   // sama seperti di streak, 0..1

// hash
float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

// sliding droplet
float droplet(vec2 uv)
{
    uv.y += uTime * 0.25;
    float n = hash(uv * 25.0);
    return smoothstep(0.93, 1.0, n);
}

// smear streak
float smear(vec2 uv)
{
    uv.y += uTime * 1.4;
    float n = hash(uv * 40.0);
    return smoothstep(0.96, 1.0, n);
}

void main()
{
    vec3 scene = texture(uSceneTex, vUV).rgb;

    float d = droplet(vUV) * 1.5;

    float s = smear(vUV)*1.2;

    float rainFX = (d * 0.6 + s * 0.4) * uRainAmount;

    // kecilkan sedikit brightness saat hujan
    scene *= mix(1.0, 0.9, uRainAmount * 0.4);

    // distort ke bawah (seperti air lari di kaca)
    vec2 offset = vec2(0.0, rainFX * 0.05); // dari 0.03 → 0.05
    vec3 distorted = texture(uSceneTex, vUV + offset).rgb;

    vec3 finalColor = mix(scene, distorted, rainFX * 0.6);
    FragColor = vec4(finalColor, 1.0);
}
