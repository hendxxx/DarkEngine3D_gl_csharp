#version 330 core

in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D evsmTex;
uniform vec2 texelSize;

// 7-tap Gaussian blur (horizontal pass)
void main()
{
    vec2 uv = clamp(TexCoord, 0.0, 1.0);
    vec4 result = texture(evsmTex, uv) * 0.383;

    for (int i = 1; i <= 3; i++)
    {
        float weight = (i == 1) ? 0.242 : ((i == 2) ? 0.061 : 0.006);
        vec2 offset = vec2(float(i) * texelSize.x, 0.0);
        result += texture(evsmTex, uv + offset) * weight;
        result += texture(evsmTex, uv - offset) * weight;
    }

    FragColor = result;
}
