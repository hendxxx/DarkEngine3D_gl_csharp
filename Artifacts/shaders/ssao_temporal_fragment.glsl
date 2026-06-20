#version 330 core

in vec2 texCoords;
out float FragColor;

uniform sampler2D currentTex;
uniform sampler2D prevTex;
uniform float blendFactor; // 0.0 = use all previous, 1.0 = use all current

void main()
{
    vec2 flippedUV = vec2(texCoords.x, 1.0 - texCoords.y);
    float current = texture(currentTex, flippedUV).r;
    float previous = texture(prevTex, flippedUV).r;

    // Blend: 85% previous frame, 15% current frame
    // This smooths out temporal noise significantly
    float result = mix(previous, current, blendFactor);

    FragColor = result;
}
