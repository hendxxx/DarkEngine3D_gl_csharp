#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

// Instanced model matrix (4 rows)
layout(location = 5) in vec4 aModelRow0;
layout(location = 6) in vec4 aModelRow1;
layout(location = 7) in vec4 aModelRow2;
layout(location = 8) in vec4 aModelRow3;

uniform mat4 lightSpaceMatrix;

// Normal-bias extrusion amount (anti-acne), uploaded live from the Shadow Settings panel.
uniform float u_NormalBias = 0.03;

out vec2 TexCoord;

void main()
{
    mat4 model = mat4(aModelRow0, aModelRow1, aModelRow2, aModelRow3);
    TexCoord = aTexCoord;

    // Fix Projection artefacts — push vertex slightly along normal to prevent self-shadowing
    float normalBias = u_NormalBias;
    vec3 extrudedPos = aPos + aNormal * normalBias;
    gl_Position = lightSpaceMatrix * model * vec4(extrudedPos, 1.0);
}
