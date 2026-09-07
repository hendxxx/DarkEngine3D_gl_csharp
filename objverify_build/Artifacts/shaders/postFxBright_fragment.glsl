#version 330 core
in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D sceneTex;
uniform float u_Threshold;   // luminance above which pixels bloom
uniform float u_SoftKnee;    // width of the soft transition (in luminance units)

void main()
{
    vec3 color = texture(sceneTex, TexCoord).rgb;

    // Luminance (Rec. 709 weights) drives the amount of bloom contribution.
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));

    // Soft knee: pixels below threshold get nothing, above threshold + knee are full,
    // in between ramp smoothly — avoids a hard cutoff edge around bright objects.
    // When both threshold and knee are 0 (used by auto-exposure downscale), bypass
    // smoothstep to avoid undefined behavior (smoothstep(a,a,x) is undefined in GLSL).
    float contribution = 1.0;
    if (u_Threshold > 0.0 || u_SoftKnee > 0.0)
        contribution = smoothstep(u_Threshold, u_Threshold + max(u_SoftKnee, 0.001), luma);

    FragColor = vec4(color * contribution, 1.0);
}
