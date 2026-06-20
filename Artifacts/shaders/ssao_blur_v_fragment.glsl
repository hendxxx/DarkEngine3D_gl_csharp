#version 330 core

in vec2 texCoords;
out float FragColor;

uniform sampler2D ssaoTex;

void main()
{
    vec2 texelSize = 1.0 / vec2(textureSize(ssaoTex, 0));
    vec2 flippedUV = vec2(texCoords.x, 1.0 - texCoords.y);

    // 1D Gaussian weights (7 taps)
    float w[7] = float[7](0.006, 0.061, 0.242, 0.383, 0.242, 0.061, 0.006);

    float result = 0.0;
    for (int i = -3; i <= 3; i++)
    {
        vec2 uv = flippedUV + vec2(0.0, float(i) * texelSize.y);
        result += texture(ssaoTex, uv).r * w[i + 3];
    }

    FragColor = result;
}
