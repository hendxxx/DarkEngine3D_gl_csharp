#version 400 core

in vec3 vWorldPos;
in vec2 vUV;

out vec4 FragColor;

uniform vec3 uCameraPos;
uniform vec3 uWindDir;
uniform float uRainAmount;
uniform float uTime;

// simple hash
float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// world-space streak
float rainStreakWS(vec3 pos)
{
    // Jarak ke kamera
    float d = length(pos - uCameraPos);

    // Fade by distance (lebih natural)
    float fade = clamp(1.0 - d * 0.0035, 0.0, 1.0);

    // Variasi kecepatan tiap streak
    float speedRand = hash(pos.xz * 0.77);
    float speed = mix(0.8, 1.4, speedRand);

    // World-locked pattern (tidak ikut kamera)
    float n = hash(pos.xz * 0.25 + uTime * speed);

    // Variasi intensitas
    float streak = smoothstep(0.92, 1.0, n);

    return streak * fade;
}

void main()
{
    // Arah jatuh
    vec3 fallDir = normalize(vec3(uWindDir.x * 0.5, -1.0, uWindDir.z * 0.5));

    float streak = rainStreakWS(vWorldPos);

    // Parallax: hujan dekat lebih cepat & lebih terang
    float depthFactor = clamp(1.0 - length(vWorldPos - uCameraPos) * 0.02, 0.1, 1.0);

    // Motion factor
    float motion = clamp(dot(fallDir, vec3(0.0, -1.0, 0.0)), 0.2, 1.0);
    motion *= depthFactor;

    // Warna hujan adaptif
    vec3 rainColor = mix(vec3(0.35, 0.45, 0.65),
                         vec3(0.55, 0.65, 0.85),
                         depthFactor);

    float alpha = streak * uRainAmount * motion * 2.0;
    alpha = clamp(alpha, 0.0, 1.0);

    if (alpha < 0.003)
        discard;

    FragColor = vec4(rainColor, alpha);
}
