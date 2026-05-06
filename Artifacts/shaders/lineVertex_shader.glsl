#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aColor; // Tambahkan ini
out vec3 vertexColor;
void main() {
    vertexColor = aColor;
    gl_Position = projection * view * vec4(aPos, 1.0);
}