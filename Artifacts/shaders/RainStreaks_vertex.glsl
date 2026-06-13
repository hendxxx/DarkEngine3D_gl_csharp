#version 400 core

layout (location = 0) in vec3 aPos;          // quad local pos (-0.5..0.5)
layout (location = 1) in vec2 aUV;           // UV quad
layout (location = 2) in vec3 aInstancePos;  // world-space rain position

out vec3 vWorldPos;
out vec2 vUV;

uniform mat4 uView;
uniform mat4 uProj;
uniform float uTime;

// simple hash
float hash(vec2 p)
{
    return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void main()
{
    // ambil right & up dari view matrix (billboard)
    vec3 right = vec3(uView[0][0], uView[1][0], uView[2][0]);
    vec3 up    = vec3(uView[0][1], uView[1][1], uView[2][1]);

    // variasi ukuran tiap streak
    float sizeRand = hash(aInstancePos.xz * 0.123);
    float height = mix(0.25, 0.45, sizeRand);
    float width  = mix(0.03, 0.07, sizeRand);

    // sway angin halus (opsional, sangat kecil)
    float sway = (hash(aInstancePos.xz * 0.91) - 0.5) * 0.05;
    sway *= sin(uTime * 2.0 + aInstancePos.x * 0.1);

    // posisi quad di world space
    vec3 worldPos =
        aInstancePos +
        right * (aPos.x * width + sway) +
        up    * (aPos.y * height);

    vWorldPos = worldPos;
    vUV = aUV;

    gl_Position = uProj * uView * vec4(worldPos, 1.0);
}
