#version 400 core
// 2D celestial quad (sun / moon). Positions arrive in NDC, so the quad is drawn
// straight into clip space and never depends on the scene's projection.
layout (location = 0) in vec2 aPos;   // unit quad: -1..1

out vec2 vUV;

uniform vec2 uCenter;     // quad centre in NDC
uniform vec2 uHalfSize;   // quad half-size in NDC (x pre-divided by aspect)

void main()
{
    vUV = aPos * 0.5 + 0.5;
    gl_Position = vec4(uCenter + aPos * uHalfSize, 0.0, 1.0);
}
