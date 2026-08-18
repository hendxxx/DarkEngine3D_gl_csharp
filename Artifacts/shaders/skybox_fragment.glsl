#version 400 core
out vec4 FragColor;

in vec3 TexCoords;

uniform sampler2D skyboxRight;    // +X
uniform sampler2D skyboxLeft;     // -X
uniform sampler2D skyboxTop;      // +Y
uniform sampler2D skyboxBottom;   // -Y
uniform sampler2D skyboxFront;    // +Z
uniform sampler2D skyboxBack;     // -Z
uniform float hasFaceRight;
uniform float hasFaceLeft;
uniform float hasFaceTop;
uniform float hasFaceBottom;
uniform float hasFaceFront;
uniform float hasFaceBack;
uniform vec3 fallbackColor;

// Clamp UV inward so we never sample the very edge texel
// (prevents seams between adjacent cubemap faces)
vec2 safeUV(vec2 uv) {
    const float MARGIN = 0.001;
    return clamp(uv, MARGIN, 1.0 - MARGIN);
}

vec3 sampleFace(vec3 dir)
{
    vec3 absDir = abs(dir);
    float maxComp = max(absDir.x, max(absDir.y, absDir.z));
    if (maxComp < 1e-6) return fallbackColor;
    vec3 absDirN = dir / maxComp;

    vec2 uv;
    vec3 color = fallbackColor;

    // UV mapping for stb_image loaded textures (top-down pixel order)
    if (absDir.x >= absDir.y && absDir.x >= absDir.z)
    {
        if (dir.x > 0.0 && hasFaceRight > 0.5)
        {
            // +X Right
            uv = safeUV(vec2(-absDirN.z, -absDirN.y) * 0.5 + 0.5);
            color = texture(skyboxRight, uv).rgb;
        }
        else if (dir.x < 0.0 && hasFaceLeft > 0.5)
        {
            // -X Left
            uv = safeUV(vec2(absDirN.z, -absDirN.y) * 0.5 + 0.5);
            color = texture(skyboxLeft, uv).rgb;
        }
    }
    else if (absDir.y >= absDir.x && absDir.y >= absDir.z)
    {
        if (dir.y > 0.0 && hasFaceTop > 0.5)
        {
            // +Y Top
            uv = safeUV(vec2(absDirN.x, absDirN.z) * 0.5 + 0.5);
            color = texture(skyboxTop, uv).rgb;
        }
        else if (dir.y < 0.0 && hasFaceBottom > 0.5)
        {
            // -Y Bottom
            uv = safeUV(vec2(absDirN.x, -absDirN.z) * 0.5 + 0.5); 
            color = texture(skyboxBottom, uv).rgb;
        }
    }
    else
    {
        if (dir.z > 0.0 && hasFaceFront > 0.5)
        {
            // +Z Front
            uv = safeUV(vec2(absDirN.x, -absDirN.y) * 0.5 + 0.5);
            color = texture(skyboxFront, uv).rgb;
        }
        else if (dir.z < 0.0 && hasFaceBack > 0.5)
        {
            // -Z Back
            uv = safeUV(vec2(-absDirN.x, -absDirN.y) * 0.5 + 0.5);
            color = texture(skyboxBack, uv).rgb;
        }
    }

    return color;
}

void main()
{
    vec3 dir = normalize(TexCoords);
    vec3 color = sampleFace(dir);
    FragColor = vec4(color, 1.0);
}
