#version 330 core

in vec2 TexCoord;

uniform sampler2D albedoMap;
uniform int useAlbedo;
uniform float alphaThreshold;

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
    // If no texture, render normally (opaque depth)
}
