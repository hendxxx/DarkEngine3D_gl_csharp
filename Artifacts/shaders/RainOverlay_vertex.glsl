#version 400 core

layout (location = 0) in vec2 aPos;   // fullscreen quad positions (-1..1)
layout (location = 1) in vec2 aUV;    // UV (0..1)

out vec2 vUV;

void main()
{
    vUV = aUV;
    gl_Position = vec4(aPos, 0.0, 1.0);
}
