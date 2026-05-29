#version 400 core

layout (location = 0) in vec3 aPos;      // posisi quad (biasanya -0.5..0.5)
layout (location = 1) in vec2 aUV;       // UV quad
layout (location = 2) in vec3 aInstancePos; // posisi world-space tiap hujan (instancing)

out vec3 vWorldPos;
out vec2 vUV;

uniform mat4 uView;
uniform mat4 uProj;
uniform vec3 uCameraPos;

// billboard: quad selalu menghadap kamera
void main()
{
    // ambil right & up dari view matrix
    vec3 right = vec3(uView[0][0], uView[1][0], uView[2][0]);
    vec3 up    = vec3(uView[0][1], uView[1][1], uView[2][1]);

    // scale hujan (tinggi & lebar)
    float width  = 0.05;
    float height = 0.35;

    // posisi quad di world space
    vec3 worldPos =
        aInstancePos +
        right * aPos.x * width +
        up    * aPos.y * height;

    vWorldPos = worldPos;
    vUV = aUV;

    gl_Position = uProj * uView * vec4(worldPos, 1.0);
}
