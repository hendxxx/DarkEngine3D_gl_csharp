#version 330 core
in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D sceneTex;
uniform vec2 texelSize;      // 1 / texture size
uniform vec2 u_Direction;    // (1,0) = horizontal, (0,1) = vertical

// Classic 9-tap Gaussian with linear filtering: samples at 0, ±1.3846, ±3.2308
// texels — only 5 texture fetches per direction.
const float w0 = 0.2270270270;
const float w1 = 0.3162162162;
const float w2 = 0.0702702703;

void main()
{
    vec3 result = texture(sceneTex, TexCoord).rgb * w0;

    result += texture(sceneTex, TexCoord + u_Direction * texelSize * 1.3846153846).rgb * w1;
    result += texture(sceneTex, TexCoord - u_Direction * texelSize * 1.3846153846).rgb * w1;

    result += texture(sceneTex, TexCoord + u_Direction * texelSize * 3.2307692308).rgb * w2;
    result += texture(sceneTex, TexCoord - u_Direction * texelSize * 3.2307692308).rgb * w2;

    FragColor = vec4(result, 1.0);
}
