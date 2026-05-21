#version 330 core

layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in vec4 aBoneWeights;
layout(location = 4) in SkinnedVertex.BoneIndex4 aBoneIds;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

// joint matrices: nodeGlobal * inverseBind
// sesuaikan MAX_JOINTS dengan skin kamu
const int MAX_JOINTS = 128;
uniform mat4 uJoints[MAX_JOINTS];

out vec3 FragPos;
out vec3 Normal;
out vec2 TexCoord;

void main()
{
    // skinning matrix
    mat4 skinMat =
        aBoneWeights.x * uJoints[int(aBoneIds.x)] +
        aBoneWeights.y * uJoints[int(aBoneIds.y)] +
        aBoneWeights.z * uJoints[int(aBoneIds.z)] +
        aBoneWeights.w * uJoints[int(aBoneIds.w)];

    // apply skinning ke posisi & normal
    if (aBoneWeights.x + aBoneWeights.y + aBoneWeights.z + aBoneWeights.w < 0.0001)
    {
        skinnedPos = vec4(aPos, 1.0);
        skinnedNormal = aNormal;
    }
    else
    {
        skinnedPos = skinMat * vec4(aPos, 1.0);
        skinnedNormal = mat3(skinMat) * aNormal;
    }


    // world space
    vec4 worldPos = model * skinnedPos;
    FragPos  = worldPos.xyz;

    // normal pakai normal matrix dari model
    mat3 normalMat = mat3(transpose(inverse(model)));
    Normal = normalize(normalMat * skinnedNormal);

    TexCoord = aTexCoord;

    gl_Position = projection * view * worldPos;
}
