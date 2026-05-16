#version 400 core
out vec4 FragColor;
uniform vec3 lineColor; // Ditambahkan ini
void main() {
    FragColor = vec4(lineColor, 1.0);
}