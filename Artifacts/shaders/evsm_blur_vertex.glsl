#version 330 core

out vec2 TexCoord;

void main()
{
    // Fullscreen triangle covering all NDC
    // Vertex 0: (-1,-1) → UV (0,0)
    // Vertex 1: ( 3,-1) → UV (2,0)
    // Vertex 2: (-1, 3) → UV (0,2)
    float x = -1.0 + float((gl_VertexID & 1) << 2);
    float y = -1.0 + float((gl_VertexID & 2) << 1);
    float u = float(gl_VertexID & 1) * 2.0;
    float v = float(gl_VertexID & 2);

    TexCoord = vec2(u, v);
    gl_Position = vec4(x, y, 0.0, 1.0);
}
