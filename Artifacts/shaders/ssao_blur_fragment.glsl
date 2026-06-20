#version 330 core

in vec2 texCoords;
out float FragColor;

uniform sampler2D ssaoTex;

void main()
{
    vec2 texelSize = 1.0 / vec2(textureSize(ssaoTex, 0));
    float result = 0.0;

    // Flip Y: ssaoTex is from FBO (bottom-left origin), quad UV is top-left origin
    vec2 flippedUV = vec2(texCoords.x, 1.0 - texCoords.y);

    // 5x5 gaussian blur
    float weights[5] = float[5](0.153388, 0.171694, 0.183662, 0.171694, 0.153388);
    // Normalize weights
    float sum = weights[0] + weights[1] + weights[2] + weights[3] + weights[4];
    for (int i = 0; i < 5; i++) weights[i] /= sum;

    for (int x = -2; x <= 2; x++)
    {
        for (int y = -2; y <= 2; y++)
        {
            vec2 offset = vec2(float(x), float(y)) * texelSize;
            float ao = texture(ssaoTex, flippedUV + offset).r;
            result += ao * weights[x + 2] * weights[y + 2];
        }
    }

    FragColor = result;
}
