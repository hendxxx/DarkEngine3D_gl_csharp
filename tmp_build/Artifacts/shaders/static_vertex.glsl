#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

// Instanced model matrix (4 rows)
layout(location = 5) in vec4 aModelRow0;
layout(location = 6) in vec4 aModelRow1;
layout(location = 7) in vec4 aModelRow2;
layout(location = 8) in vec4 aModelRow3;

uniform mat4 view;
uniform mat4 projection;

out vec3 FragPos;
out vec3 Normal;
out vec2 TexCoord;

void main()
{
    mat4 model = mat4(aModelRow0, aModelRow1, aModelRow2, aModelRow3);
    vec4 worldPos = model * vec4(aPos, 1.0);
    FragPos = worldPos.xyz;
    Normal = mat3(transpose(inverse(model))) * aNormal;
    TexCoord = aTexCoord;
    gl_Position = projection * view * worldPos;
}
