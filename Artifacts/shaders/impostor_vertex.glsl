#version 330 core
layout(location = 0) in vec2 aPos; // unit quad (-1..1)

uniform vec3 center;       // world center of the region
uniform float radius;      // billboard half-size
uniform vec3 cameraRight;  // camera right direction (world space)
uniform vec3 cameraUp;     // camera up direction (world space)
uniform mat4 view;
uniform mat4 projection;

out vec2 vUV;

void main()
{
    // Expand unit quad into a camera-facing billboard
    vec3 worldPos = center + aPos.x * radius * cameraRight + aPos.y * radius * cameraUp;
    vUV = aPos * 0.5 + 0.5; // map [-1,1] to [0,1]
    gl_Position = projection * view * vec4(worldPos, 1.0);
}
