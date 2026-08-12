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
uniform float u_NormalBias = 0.0010;

out vec2 TexCoord;

void main()
{
    mat4 model = mat4(aModelRow0, aModelRow1, aModelRow2, aModelRow3);
    TexCoord = aTexCoord;

    // Anti-acne: push the vertex slightly along its WORLD-space normal AFTER the model
    // transform. Extruding in model space (aPos + n*bias, then × model) makes the world
    // offset anisotropic under non-uniform scale — a terrain scaled (500,1,500) gets a
    // 500× larger XZ extrusion than Y, so no single bias value fixes both acne (needs
    // more Y) and peter-panning (XZ detaches the shadow). World-space extrusion is
    // uniform in every direction, so u_NormalBias is directly in world units.
    float normalBias = u_NormalBias;
    mat3 normalMatrix = transpose(inverse(mat3(model)));
    vec3 n = normalize(normalMatrix * aNormal);
    vec4 worldPos = model * vec4(aPos, 1.0);
    worldPos.xyz += n * normalBias;
    gl_Position = lightSpaceMatrix * worldPos;
}
