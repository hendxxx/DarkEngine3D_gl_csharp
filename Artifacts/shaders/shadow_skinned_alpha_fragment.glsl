#version 330 core

in vec2 TexCoord;

uniform sampler2D albedoMap;
uniform int useAlbedo;
uniform float alphaThreshold;
uniform int useAlphaTest;

uniform float evsmWarp;

out vec4 FragColor;

void main()
{
    // Perform alpha testing if texture is used AND alpha test is enabled
    if (useAlbedo == 1 && useAlphaTest == 1) {
        vec4 texColor = texture(albedoMap, TexCoord);
        // Discard fragments below threshold to support transparency
        if (texColor.a < alphaThreshold) {
            discard;
        }
    }
    
    // Write EVSM values (same as shadow_fragment.glsl)
    float depth = gl_FragCoord.z;
    float c = evsmWarp;
    float pos = exp(c * depth);
    float pos2 = pos * pos;
    float neg = exp(-c * depth);
    float neg2 = neg * neg;
    FragColor = vec4(pos, pos2, neg, neg2);
}
