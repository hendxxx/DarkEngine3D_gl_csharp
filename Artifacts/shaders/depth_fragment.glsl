#version 330 core
out float fragDepth;

uniform float u_Near;
uniform float u_Far;

void main()
{
    // Linearize depth buffer value to world-space distance
    float z_ndc = 2.0 * gl_FragCoord.z - 1.0;
    float linearDepth = (2.0 * u_Near * u_Far) / (u_Far + u_Near - z_ndc * (u_Far - u_Near));
    fragDepth = linearDepth;
}
