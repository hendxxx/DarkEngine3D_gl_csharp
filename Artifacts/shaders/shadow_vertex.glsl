#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;

uniform mat4 model;
uniform mat4 lightSpaceMatrix;

// Normal-bias extrusion amount (anti-acne), uploaded live from the Shadow Settings panel.
uniform float u_NormalBias = 0.000001;

// Inverse-transpose of the model matrix — transforms the model-space normal into the
// same space as aPos BEFORE the model matrix is applied. Without this, extruding along
// the raw aNormal breaks on non-uniform scale / rotation: the extrusion direction is
// wrong, surfaces get pushed INTO the shadow map, and dark stripes follow the geometry.
uniform mat3 u_NormalMatrix = mat3(1.0);

void main()
{
    // Fix Projection artefacts — push vertex slightly along the (model-space) normal to
    // prevent self-shadowing. Using u_NormalMatrix keeps the direction correct even when
    // the object is non-uniformly scaled or rotated.
    float normalBias = u_NormalBias;
    vec3 n = normalize(u_NormalMatrix * aNormal);
    vec3 extrudedPos = aPos + n * normalBias;
    gl_Position = lightSpaceMatrix * model * vec4(extrudedPos, 1.0);
}
