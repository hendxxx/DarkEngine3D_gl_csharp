#version 330 core

layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 3) in vec4 aBoneWeights;
layout(location = 4) in ivec4 aBoneIds;

uniform mat4 model;
uniform mat4 lightSpaceMatrix;

const int MAX_JOINTS = 128;
uniform mat4 u_Joints[MAX_JOINTS];

// Normal-bias extrusion amount (anti-acne), uploaded live from the Shadow Settings panel.
uniform float u_NormalBias = 0.0;

// Inverse-transpose of the model matrix. The skinned normal is in MODEL space after
// skinning, so it must be rotated by this before extrusion to stay correct under
// non-uniform scale / rotation (otherwise surfaces push INTO the shadow map and dark
// stripes follow the geometry).
uniform mat3 u_NormalMatrix = mat3(1.0);

void main()
{
    float wsum = aBoneWeights.x + aBoneWeights.y + aBoneWeights.z + aBoneWeights.w;
    vec4 skinnedPos;
    vec3 skinnedNormal;

    if (wsum < 1e-6)
    {
        skinnedPos = vec4(aPos, 1.0);
        skinnedNormal = aNormal;
    }
    else
    {
        vec4 w = aBoneWeights / wsum;
        ivec4 ids = clamp(aBoneIds, 0, MAX_JOINTS - 1);

        mat4 skinMat =
            w.x * u_Joints[ids.x] +
            w.y * u_Joints[ids.y] +
            w.z * u_Joints[ids.z] +
            w.w * u_Joints[ids.w];

        skinnedPos = skinMat * vec4(aPos, 1.0);
        skinnedNormal = mat3(skinMat) * aNormal;
    }

    // Anti-acne: push the vertex slightly along its WORLD-space normal AFTER the model
    // transform. Extruding in model space (aPos + n*bias, then × model) makes the world
    // offset anisotropic under non-uniform scale — a terrain scaled (500,1,500) gets a
    // 500× larger XZ extrusion than Y, so no single bias value fixes both acne (needs
    // more Y) and peter-panning (XZ detaches the shadow). World-space extrusion is
    // uniform in every direction, so u_NormalBias is directly in world units.
    float normalBias = u_NormalBias;
    vec3 n = normalize(u_NormalMatrix * skinnedNormal);

    vec4 worldPos = model * skinnedPos;
    worldPos.xyz += n * normalBias;
    gl_Position   = lightSpaceMatrix * worldPos;
}
