#version 400 core
out vec4 FragColor;

in vec3 TexCoords;
in vec3 WorldPos;

uniform sampler2D domeTexture;
uniform vec3 tintColor;
uniform float hasTexture;

// Equirectangular to UV mapping (corrected orientation)
vec2 equirectUV(vec3 dir)
{
    float phi = atan(dir.z, dir.x); // -PI..PI
    float theta = asin(clamp(dir.y, -1.0, 1.0)); // -PI/2..PI/2

    float u = phi / (2.0 * 3.14159265) + 0.5;
    // Flip v: stb_image loads top-down but OpenGL v=0 is bottom
    float v = -(theta / 3.14159265) + 0.5;

    return vec2(u, v);
}

void main()
{
    vec3 dir = normalize(TexCoords);

    vec3 color;
    if (hasTexture > 0.5)
    {
        vec2 uv = equirectUV(dir);
        color = texture(domeTexture, uv).rgb;
    }
    else
    {
        // Procedural gradient fallback: horizon to zenith
        float h = dir.y * 0.5 + 0.5;
        vec3 horizon = vec3(0.5, 0.7, 1.0);
        vec3 zenith = vec3(0.1, 0.2, 0.5);
        color = mix(horizon, zenith, h);
    }

    color *= tintColor;
    FragColor = vec4(color, 1.0);
}
