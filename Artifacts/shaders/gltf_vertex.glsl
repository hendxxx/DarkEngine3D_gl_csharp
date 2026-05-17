#version 330 core

// ── Vertex attributes ──────────────────────────────────────────────────────────
layout(location = 0) in vec3  aPos;
layout(location = 1) in vec3  aNormal;
layout(location = 2) in vec2  aTexCoord;
layout(location = 3) in vec4  aBoneWeights;
layout(location = 4) in vec4  aBoneIds;    // stored as float, cast to int below


// ── Uniforms ───────────────────────────────────────────────────────────────────
uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

// Maksimum 100 joint — cukup untuk karakter humanoid standar (Xbot punya ~65 joint)
#define MAX_JOINTS 100
uniform mat4  jointMatrices[MAX_JOINTS];
uniform int   numJoints;
uniform int   hasSkin;      // 1 = skinned mesh, 0 = static mesh

// ── Output ke fragment shader ──────────────────────────────────────────────────
out vec3 FragPos;
out vec3 Normal;
out vec2 TexCoord;

void main()
{
    vec4 skinnedPos    = vec4(0.0);
    vec3 skinnedNormal = vec3(0.0);

    if (hasSkin == 1 && numJoints > 0)
    {
        // ── Skinning (Linear Blend Skinning) ────────────────────────────────
        for (int i = 0; i < 4; i++)
        {
            float w = aBoneWeights[i];
            if (w <= 0.0) continue;

            int jIdx = int(aBoneIds[i]);
            if (jIdx >= numJoints) continue;

            mat4 jMat = jointMatrices[jIdx];
            skinnedPos    += w * (jMat * vec4(aPos, 1.0));
            skinnedNormal += w * (mat3(jMat) * aNormal);
        }
    }
    else
    {
        // Static mesh — tidak ada skinning
        skinnedPos    = vec4(aPos, 1.0);
        skinnedNormal = aNormal;
    }

    vec4 worldPos = model * skinnedPos;
    FragPos  = worldPos.xyz;
    Normal   = normalize(mat3(transpose(inverse(model))) * skinnedNormal);
    TexCoord = aTexCoord;

    gl_Position = projection * view * worldPos;
}
