#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;

uniform mat4 model;
uniform mat4 lightSpaceMatrix;

void main()
{
    // Fix Projection artefacts — push vertex slightly along normal to prevent self-shadowing
    float normalBias = 0.02;
    vec3 extrudedPos = aPos + aNormal * normalBias;
    gl_Position = lightSpaceMatrix * model * vec4(extrudedPos, 1.0);
}
