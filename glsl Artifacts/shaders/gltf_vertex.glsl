#version 330 core

layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in vec4 aBoneWeights;
layout(location = 4) in ivec4 aBoneIds;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

// joint matrices: nodeGlobal * inverseBind
// adjust MAX_JOINTS if needed (must match engine max)
const int MAX_JOINTS = 128;
uniform mat4 u_Joints[MAX_JOINTS];

out vec3 FragPos;
out vec3 Normal;
out vec2 TexCoord;

void main()
{
    vec4 skinnedPos;
    vec3 skinnedNormal;

    float wsum = aBoneWeights.x + aBoneWeights.y + aBoneWeights.z + aBoneWeights.w;
    if (wsum < 1e-6)
    {
        skinnedPos = vec4(aPos, 1.0);
        skinnedNormal = aNormal;
    }
    else
    {
        // normalize weights if they don't sum to 1
        vec4 w = aBoneWeights / wsum;

        mat4 skinMat =
            w.x * u_Joints[aBoneIds.x] +
            w.y * u_Joints[aBoneIds.y] +
            w.z * u_Joints[aBoneIds.z] +
            w.w * u_Joints[aBoneIds.w];

        skinnedPos = skinMat * vec4(aPos, 1.0);
        skinnedNormal = mat3(skinMat) * aNormal;
    }

    // world space
    vec4 worldPos = model * skinnedPos;
    FragPos  = worldPos.xyz;

    // normal uses model's normal matrix
    mat3 normalMat = mat3(transpose(inverse(model)));
    Normal = normalize(normalMat * skinnedNormal);

    TexCoord = aTexCoord;

    gl_Position = projection * view * worldPos;
}