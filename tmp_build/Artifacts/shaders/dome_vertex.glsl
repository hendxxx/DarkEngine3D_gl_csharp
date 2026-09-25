#version 400 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;

out vec3 TexCoords;
out vec3 WorldPos;

uniform mat4 view;
uniform mat4 projection;
uniform float domeRadius;
uniform float domeRotationY;
uniform float domeRotationX;
uniform float time;

void main()
{
    // Mesh stays static — only texture coordinates rotate.
    // This makes the texture visibly pan across the dome surface.
    vec3 meshPos = aPos;
    WorldPos = meshPos * domeRadius;

    // Rotate texture coordinates (UV panning)
    vec3 texDir = meshPos;

    // Apply Y rotation (horizontal pan)
    float cosR = cos(domeRotationY);
    float sinR = sin(domeRotationY);
    texDir = vec3(
        texDir.x * cosR + texDir.z * sinR,
        texDir.y,
        -texDir.x * sinR + texDir.z * cosR
    );

    // Apply X rotation (vertical tilt) if non-zero
    if (abs(domeRotationX) > 0.0001) {
        float cosX = cos(domeRotationX);
        float sinX = sin(domeRotationX);
        texDir = vec3(
            texDir.x,
            texDir.y * cosX - texDir.z * sinX,
            texDir.y * sinX + texDir.z * cosX
        );
    }

    TexCoords = normalize(texDir);

    vec4 pos = projection * view * vec4(WorldPos, 1.0);
    gl_Position = pos.xyww;
}
