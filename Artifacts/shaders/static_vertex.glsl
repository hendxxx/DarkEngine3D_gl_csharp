#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

layout(location = 3) in mat4 instanceMatrix;

uniform mat4 view;
uniform mat4 projection;

out vec3 FragPos;
out vec3 Normal;
out vec2 TexCoord;

void main()
{
    vec4 worldPos = instanceMatrix * vec4(aPos, 1.0);
    FragPos = worldPos.xyz;
    // Normal = mat3(transpose(inverse(instanceMatrix))) * aNormal;
    // optimization since we only use uniform scaling:
    Normal = normalize(mat3(instanceMatrix) * aNormal);
    TexCoord = aTexCoord;
    gl_Position = projection * view * worldPos;
}
