#version 400 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;

out vec3 TexCoords;
out vec3 WorldPos;

uniform mat4 view;
uniform mat4 projection;
uniform float domeRadius;
uniform float domeRotationY;

void main()
{
    // Apply Y rotation to get panoramic UV mapping
    float cosR = cos(domeRotationY);
    float sinR = sin(domeRotationY);
    vec3 rotated = vec3(
        aPos.x * cosR + aPos.z * sinR,
        aPos.y,
        -aPos.x * sinR + aPos.z * cosR
    );

    TexCoords = normalize(rotated);
    WorldPos = rotated * domeRadius;

    vec4 pos = projection * view * vec4(WorldPos, 1.0);
    gl_Position = pos.xyww; // Force depth to far plane
}
