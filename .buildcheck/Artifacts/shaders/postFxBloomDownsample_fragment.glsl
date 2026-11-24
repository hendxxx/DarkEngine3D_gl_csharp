#version 330 core
in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D sceneTex;
uniform vec2 texelSize;      // 1 / SOURCE texture size

// 13-tap box blur (3x3 with corners) — smooths while halving resolution.
// Used both for the mip downscale and to soften each bloom level.
void main()
{
    vec3 result = texture(sceneTex, TexCoord).rgb * 4.0;

    result += texture(sceneTex, TexCoord + vec2(-texelSize.x, -texelSize.y)).rgb;
    result += texture(sceneTex, TexCoord + vec2( 0.0f,        -texelSize.y)).rgb;
    result += texture(sceneTex, TexCoord + vec2( texelSize.x, -texelSize.y)).rgb;
    result += texture(sceneTex, TexCoord + vec2(-texelSize.x,  0.0f       )).rgb;
    result += texture(sceneTex, TexCoord + vec2( texelSize.x,  0.0f       )).rgb;
    result += texture(sceneTex, TexCoord + vec2(-texelSize.x,  texelSize.y)).rgb;
    result += texture(sceneTex, TexCoord + vec2( 0.0f,         texelSize.y)).rgb;
    result += texture(sceneTex, TexCoord + vec2( texelSize.x,  texelSize.y)).rgb;

    FragColor = vec4(result / 12.0, 1.0);
}
