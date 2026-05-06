#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec3 aColor;

out vec3 FragPos;   // Kirim posisi pixel ke Fragment
out vec3 Normal;    // Kirim normal ke Fragment
out vec3 ObjColor;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

void main() {
    // Hitung posisi di dunia nyata agar cahaya tahu di mana pixel ini berada
    FragPos = vec3(model * vec4(aPos, 1.0));
    
    // Kirim normal (pastikan tetap tegak lurus meski objek berputar)
    Normal = mat3(transpose(inverse(model))) * aNormal;
    
    ObjColor = aColor;
    gl_Position = projection * view * vec4(FragPos, 1.0);
}
