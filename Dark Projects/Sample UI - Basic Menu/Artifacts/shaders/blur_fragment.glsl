#version 330 core
in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D sceneTex;
uniform vec2 texelSize;
uniform float blurRadius;
uniform float blurStrength;

// Gaussian weight helper
float gaussian(float x, float sigma)
{
    return exp(-(x * x) / (2.0 * sigma * sigma));
}

void main()
{
    vec2 uv = TexCoord;

    // When blurRadius <= 0, just pass through without blur (used for non-paused frames)
    if (blurRadius <= 0.0)
    {
        FragColor = texture(sceneTex, uv);
        return;
    }

    int radius = int(max(1.0, blurRadius));
    float sigma = float(radius) * 0.5 * blurStrength;

    vec3 color = vec3(0.0);
    float totalWeight = 0.0;

    for (int x = -radius; x <= radius; x++)
    {
        for (int y = -radius; y <= radius; y++)
        {
            vec2 offset = vec2(float(x), float(y)) * texelSize;
            float w = gaussian(float(x), sigma) * gaussian(float(y), sigma);
            color += texture(sceneTex, uv + offset).rgb * w;
            totalWeight += w;
        }
    }

    color /= totalWeight;

    // Slight darken for the overlay feel
    FragColor = vec4(color * 0.65, 1.0);
}
