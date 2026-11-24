#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
// aTexCoord MUST be location 3: the shared primitive VAO (Object3D.SetupGPUResources)
// binds loc 2 = COLOR (vec3) and loc 3 = UV (vec2). Declaring the UV at loc 2 made
// every objectPbr-drawn Box/Sphere sample its maps through color.xy as UV (constant →
// textures never showed), while the plain path's useTexture=0 hid the same bug.
// pbrDisplace_vertex.glsl already used location 3 — planes were unaffected.
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

    ObjColor = vec3(1.0);
    TexCoord = aTexCoord;

    // hitung posisi di view space
    vec4 viewPos = view * worldPos;
    // pakai -z (kamera lihat ke -Z)
    viewDepth = -viewPos.z;

    gl_Position = projection * viewPos;
}
