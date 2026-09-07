#version 330 core

in vec2 TexCoord;

uniform sampler2D albedoMap;
uniform int useAlbedo;
uniform float alphaThreshold;

out vec4 FragColor;

void main()
{
    // Perform alpha testing if texture is used
    if (useAlbedo == 1) {
        vec4 texColor = texture(albedoMap, TexCoord);
        // Discard fragments below threshold to support transparency
        if (texColor.a < alphaThreshold) {
            discard;
        }
    }
    // Always write raw depth for PCSS sampling
    float depth = gl_FragCoord.z;
    FragColor = vec4(depth, 0.0, 0.0, 0.0);
}
