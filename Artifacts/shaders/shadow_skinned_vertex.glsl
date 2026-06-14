#version 330 core

layout(location = 0) in vec3 aPos;
layout(location = 3) in vec4 aBoneWeights;
layout(location = 4) in ivec4 aBoneIds;

uniform mat4 model;
uniform mat4 lightSpaceMatrix;

const int MAX_JOINTS = 128;
uniform mat4 u_Joints[MAX_JOINTS];

void main()
{
    float wsum = aBoneWeights.x + aBoneWeights.y + aBoneWeights.z + aBoneWeights.w;
    vec4 skinnedPos;

    if (wsum < 1e-6)
    {
        skinnedPos = vec4(aPos, 1.0);
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
    }

    vec4 worldPos = model * skinnedPos;
    gl_Position   = lightSpaceMatrix * worldPos;
}
