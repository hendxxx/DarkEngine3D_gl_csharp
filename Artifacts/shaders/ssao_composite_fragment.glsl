#version 330 core

in vec2 texCoords;
out vec4 FragColor;

uniform sampler2D sceneTex;
uniform sampler2D ssaoTex;
uniform int useSSAO;
uniform float ssaoStrength;

void main()
{
    // Flip Y because FBO textures have origin at bottom-left,
    // but our fullscreen quad UV has origin at top-left
    vec2 flippedUV = vec2(texCoords.x, 1.0 - texCoords.y);

    vec4 color = texture(sceneTex, flippedUV);

    if (useSSAO == 1)
    {
        // ssaoTex is also from FBO (bottom-left origin), needs same Y-flip
        float ao = texture(ssaoTex, flippedUV).r;
        // Blend AO: lerp between fully occluded and no occlusion
        float aoFactor = mix(1.0 - ssaoStrength, 1.0, ao);
        color.rgb *= aoFactor;
    }

    FragColor = color;
}
