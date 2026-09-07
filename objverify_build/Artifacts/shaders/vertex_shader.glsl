#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec3 aColor;
layout (location = 3) in vec2 aTexCoord;   

out vec3 FragPos;
out vec3 Normal;
out vec3 ObjColor;
out vec2 TexCoord;  
out float viewDepth;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

void main() {
    vec4 worldPos = model * vec4(aPos, 1.0);
    FragPos = worldPos.xyz;

    // normal world-space
    Normal = normalize(mat3(transpose(inverse(model))) * aNormal);

    ObjColor = aColor;
    TexCoord = aTexCoord;  

    // hitung posisi di view space
    vec4 viewPos = view * worldPos;
    // pakai -z (kamera lihat ke -Z)
    viewDepth = -viewPos.z;

    gl_Position = projection * viewPos;
}
