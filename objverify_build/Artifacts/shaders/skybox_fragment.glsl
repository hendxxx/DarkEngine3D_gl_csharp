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

void main()
{
    vec3 dir = normalize(TexCoords);
    vec3 absDir = abs(dir);
    float maxComp = max(absDir.x, max(absDir.y, absDir.z));
    if (maxComp < 1e-6) {
        FragColor = vec4(fallbackColor, 1.0);
        return;
    }
    // Project onto the dominant face plane: dominant axis becomes ±1
    vec3 d = dir / maxComp;

    // Compute blend weights: how far each axis dominates over the other two
    float wX = max(0.0, absDir.x - max(absDir.y, absDir.z));
    float wY = max(0.0, absDir.y - max(absDir.x, absDir.z));
    float wZ = max(0.0, absDir.z - max(absDir.x, absDir.y));
    float totalW = wX + wY + wZ;
    if (totalW < 1e-6) {
        FragColor = vec4(fallbackColor, 1.0);
        return;
    }
    wX /= totalW;
    wY /= totalW;
    wZ /= totalW;

    // ── Sample X face ──
    vec3 colX;
    if (dir.x > 0.0 && hasFaceRight > 0.5)
        colX = texture(skyboxRight, vec2(-d.z, -d.y) * 0.5 + 0.5).rgb;
    else if (dir.x < 0.0 && hasFaceLeft > 0.5)
        colX = texture(skyboxLeft, vec2(d.z, -d.y) * 0.5 + 0.5).rgb;
    else
        colX = fallbackColor;

    // ── Sample Y face ──
    vec3 colY;
    if (dir.y > 0.0 && hasFaceTop > 0.5)
        colY = texture(skyboxTop, vec2(d.x, d.z) * 0.5 + 0.5).rgb;
    else if (dir.y < 0.0 && hasFaceBottom > 0.5)
        colY = texture(skyboxBottom, vec2(d.x, -d.z) * 0.5 + 0.5).rgb;
    else
        colY = fallbackColor;

    // ── Sample Z face ──
    vec3 colZ;
    if (dir.z > 0.0 && hasFaceFront > 0.5)
        colZ = texture(skyboxFront, vec2(d.x, -d.y) * 0.5 + 0.5).rgb;
    else if (dir.z < 0.0 && hasFaceBack > 0.5)
        colZ = texture(skyboxBack, vec2(-d.x, -d.y) * 0.5 + 0.5).rgb;
    else
        colZ = fallbackColor;

    // ── Weighted blend — seamless at all face boundaries ──
    vec3 color = colX * wX + colY * wY + colZ * wZ;

    FragColor = vec4(color, 1.0);
}
