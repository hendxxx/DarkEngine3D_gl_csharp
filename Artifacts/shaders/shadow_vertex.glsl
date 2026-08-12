#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;

uniform mat4 model;
uniform mat4 lightSpaceMatrix;

// Normal-bias extrusion amount (anti-acne), uploaded live from the Shadow Settings panel.
uniform float u_NormalBias = 0.0010;

// Inverse-transpose of the model matrix — transforms the model-space normal into the
// same space as aPos BEFORE the model matrix is applied. Without this, extruding along
// the raw aNormal breaks on non-uniform scale / rotation: the extrusion direction is
// wrong, surfaces get pushed INTO the shadow map, and dark stripes follow the geometry.
uniform mat3 u_NormalMatrix = mat3(1.0);

void main()
{
    // Anti-acne: push the vertex slightly along its WORLD-space normal AFTER the model
    // transform. Extruding in model space (aPos + n*bias, then × model) makes the world
    // offset anisotropic under non-uniform scale — a terrain scaled (500,1,500) gets a
    // 500× larger XZ extrusion than Y, so no single bias value fixes both acne (needs
    // more Y) and peter-panning (XZ detaches the shadow). World-space extrusion is
    // uniform in every direction, so u_NormalBias is directly in world units.
    float normalBias = u_NormalBias;
    vec3 n = normalize(u_NormalMatrix * aNormal);
    vec4 worldPos = model * vec4(aPos, 1.0);
    worldPos.xyz += n * normalBias;
    gl_Position = lightSpaceMatrix * worldPos;
}
