#version 400 core

in vec3 vWorldPos;
in vec2 vUV;

out vec4 FragColor;

uniform vec3 uCameraPos;
uniform vec3 uWindDir;
uniform float uRainAmount;
uniform float uTime;
uniform float uLightning;   // 0..1 dari lightningFlash()

// simple hash
float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// world-space streak
float rainStreakWS(vec3 pos)
{
    float d = length(pos - uCameraPos);

    // fade by distance
    float fade = clamp(1.0 - d * 0.0035, 0.0, 1.0);

    // random speed
    float speedRand = hash(pos.xz * 0.77);
    float speed = mix(0.8, 1.4, speedRand);

    // world-locked pattern
    float n = hash(pos.xz * 0.25 + uTime * speed);

    // more streaks visible
    float streak = smoothstep(0.88, 1.0, n);

    return streak * fade;
}

void main()
{
    // fall direction
    vec3 fallDir = normalize(vec3(uWindDir.x * 0.5, -1.0, uWindDir.z * 0.5));

    float streak = rainStreakWS(vWorldPos);

    // depth factor (parallax)
    float depthFactor = clamp(1.0 - length(vWorldPos - uCameraPos) * 0.02, 0.1, 1.0);

    // motion factor
    float motion = clamp(dot(fallDir, vec3(0.0, -1.0, 0.0)), 0.2, 1.0);
    motion *= depthFactor;

    // --- Unreal Upgrade: Motion Blur ---
    float blur = mix(1.0, 1.8, depthFactor);
    streak *= blur;

    // --- Unreal Upgrade: Specular Highlight ---
    float center = exp(-pow((vUV.x - 0.5) * 6.0, 2.0));
    streak = streak * 0.7 + center * 0.3;

    // --- Unreal Upgrade: Depth Stretch ---
    float stretch = mix(1.0, 1.6, depthFactor);
    streak *= stretch;

    // color (brighter)
    vec3 rainColor = mix(vec3(0.45, 0.55, 0.85),
                         vec3(0.70, 0.80, 1.0),
                         depthFactor);

    // --- Unreal Upgrade: Lightning Reflection ---
    rainColor += uLightning * 0.5;
    streak += uLightning * 0.3;

    // alpha stronger
    float alpha = streak * uRainAmount * motion * 3.0;
    alpha = clamp(alpha, 0.0, 1.0);

    if (alpha < 0.003)
        discard;

    FragColor = vec4(rainColor, alpha);
}
