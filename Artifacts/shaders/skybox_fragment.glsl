#version 400 core
out vec4 FragColor;

in vec3 TexCoords;

// 6 cubemap face textures (+X, -X, +Y, -Y, +Z, -Z)
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

vec2 cubemapUV(vec3 dir, int face)
{
    vec2 uv;
    vec3 absDir = abs(dir);

    if (face == 0) { // +X (Right)
        uv = vec2(-dir.x / absDir.x, -dir.y / absDir.x);
        uv = uv * 0.5 + 0.5;
    } else if (face == 1) { // -X (Left)
        uv = vec2(dir.x / absDir.x, -dir.y / absDir.x);
        uv = uv * 0.5 + 0.5;
    } else if (face == 2) { // +Y (Top)
        uv = vec2(dir.x / absDir.y, dir.z / absDir.y);
        uv = uv * 0.5 + 0.5;
        uv.y = 1.0 - uv.y;
    } else if (face == 3) { // -Y (Bottom)
        uv = vec2(dir.x / absDir.y, -dir.z / absDir.y);
        uv = uv * 0.5 + 0.5;
        uv.y = 1.0 - uv.y;
    } else if (face == 4) { // +Z (Front)
        uv = vec2(dir.x / absDir.z, -dir.y / absDir.z);
        uv = uv * 0.5 + 0.5;
    } else { // -Z (Back)
        uv = vec2(-dir.x / absDir.z, -dir.y / absDir.z);
        uv = uv * 0.5 + 0.5;
    }
    return uv;
}

vec3 sampleFace(vec3 dir)
{
    vec3 absDir = abs(dir);
    float maxComp = max(absDir.x, max(absDir.y, absDir.z));
    vec3 absDirN = dir / maxComp;

    vec2 uv;
    vec3 color = fallbackColor;

    if (absDir.x >= absDir.y && absDir.x >= absDir.z)
    {
        if (dir.x > 0.0 && hasFaceRight > 0.5)
        {
            uv = vec2(-absDirN.z, -absDirN.y) * 0.5 + 0.5;
            color = texture(skyboxRight, uv).rgb;
        }
        else if (dir.x < 0.0 && hasFaceLeft > 0.5)
        {
            uv = vec2(absDirN.z, -absDirN.y) * 0.5 + 0.5;
            color = texture(skyboxLeft, uv).rgb;
        }
    }
    else if (absDir.y >= absDir.x && absDir.y >= absDir.z)
    {
        if (dir.y > 0.0 && hasFaceTop > 0.5)
        {
            uv = vec2(absDirN.x, absDirN.z) * 0.5 + 0.5;
            uv.y = 1.0 - uv.y;
            color = texture(skyboxTop, uv).rgb;
        }
        else if (dir.y < 0.0 && hasFaceBottom > 0.5)
        {
            uv = vec2(absDirN.x, -absDirN.z) * 0.5 + 0.5;
            uv.y = 1.0 - uv.y;
            color = texture(skyboxBottom, uv).rgb;
        }
    }
    else
    {
        if (dir.z > 0.0 && hasFaceFront > 0.5)
        {
            uv = vec2(absDirN.x, -absDirN.y) * 0.5 + 0.5;
            color = texture(skyboxFront, uv).rgb;
        }
        else if (dir.z < 0.0 && hasFaceBack > 0.5)
        {
            uv = vec2(-absDirN.x, -absDirN.y) * 0.5 + 0.5;
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
