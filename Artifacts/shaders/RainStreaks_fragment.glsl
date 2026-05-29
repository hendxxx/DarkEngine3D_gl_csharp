#version 400 core

in vec3 vWorldPos;   // from vertex shader
in vec2 vUV;

out vec4 FragColor;

uniform vec3 uCameraPos;
uniform vec3 uWindDir;      // normalized, ex: vec3(0.3, 0.0, 0.2)
uniform float uRainAmount;  // 0..1, biasanya dari smoothstep(0.7,1.0, weatherMode)
uniform float uTime;

// simple hash
float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// world-space streak
float rainStreakWS(vec3 pos)
{
    // posisi relatif kamera (camera-stable)
    vec3 localPos = pos - uCameraPos;

    float d = length(localPos);

    // fade radius besar
    float fade = clamp(1.0 - d * 0.005, 0.0, 1.0);

    // pattern stabil
    float n = hash(localPos.xz * 0.35 + uTime * 0.7);
    float streak = smoothstep(0.94, 1.0, n);

    return streak * fade;
}

void main()
{
    vec3 fallDir = normalize(vec3(uWindDir.x * 0.5, -1.0, uWindDir.z * 0.5));

    float streak = rainStreakWS(vWorldPos);

    float motion = clamp(dot(fallDir, vec3(0.0, -1.0, 0.0)), 0.2, 1.0);

    vec3 rainColor = vec3(0.45, 0.55, 0.75);

    float alpha = streak * uRainAmount * motion * 2.0;
    alpha = clamp(alpha, 0.0, 1.0);

    if (alpha < 0.003)
        discard;

    FragColor = vec4(rainColor, alpha);
}

