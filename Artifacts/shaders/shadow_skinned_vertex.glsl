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
uniform float u_NormalBias = 0.03;

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

    // Push the vertex slightly along its (skinned) normal so surfaces never self-shadow
    // against their own shadow-map texels — prevents acne on skinned/GLB geometry
    // (same anti-acne trick the static shadow shaders use).
    float normalBias = u_NormalBias;
    vec3 n = length(skinnedNormal) > 1e-6 ? normalize(skinnedNormal) : vec3(0.0, 1.0, 0.0);
    vec3 extrudedPos = skinnedPos.xyz + n * normalBias;

    vec4 worldPos = model * vec4(extrudedPos, 1.0);
    gl_Position   = lightSpaceMatrix * worldPos;
}
