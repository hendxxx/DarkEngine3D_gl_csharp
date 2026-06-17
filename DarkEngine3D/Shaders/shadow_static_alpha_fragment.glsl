#version 330 core

in vec2 TexCoord;

uniform sampler2D albedoMap;
uniform float alphaThreshold;

void main()
{
    if(texture(albedoMap, TexCoord).a < alphaThreshold)
        discard;
}
