#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 2) in vec2 aTexCoord;

// Instanced model matrix (4 rows)
layout(location = 5) in vec4 aModelRow0;
layout(location = 6) in vec4 aModelRow1;
layout(location = 7) in vec4 aModelRow2;
layout(location = 8) in vec4 aModelRow3;

uniform mat4 lightSpaceMatrix;

out vec2 TexCoord;

void main()
{
    mat4 model = mat4(aModelRow0, aModelRow1, aModelRow2, aModelRow3);
    TexCoord = aTexCoord;
    gl_Position = lightSpaceMatrix * model * vec4(aPos, 1.0);
}
