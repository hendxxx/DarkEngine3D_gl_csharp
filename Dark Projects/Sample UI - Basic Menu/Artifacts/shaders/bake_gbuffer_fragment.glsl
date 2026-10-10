#version 330 core

// ── Two output modes ──────────────────────────────────────────────────────
// gbufferMode == 0: FragColor = (albedo.rgb, roughness)
// gbufferMode == 1: FragColor = (normal.xy * 0.5 + 0.5, metallic, occlusion)
uniform int gbufferMode;

out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec2 TexCoord;

// ── Base Color ─────────────────────────────────────────────────────────────
uniform sampler2D albedoMap;
uniform int useAlbedo;
uniform vec4 baseColorFactor;

// ── PBR Textures ──────────────────────────────────────────────────────────
uniform sampler2D normalMap;
uniform sampler2D metallicRoughnessMap;
uniform sampler2D occlusionMap;
uniform sampler2D emissiveMap;

// ── PBR Factors ───────────────────────────────────────────────────────────
uniform float metallicFactor;
uniform float roughnessFactor;
uniform vec3 emissiveFactor;
uniform float normalScale;
uniform float occlusionStrength;

// ── Texture Flags ─────────────────────────────────────────────────────────
uniform int hasNormalTexture;
uniform int hasMetallicRoughnessTexture;
uniform int hasOcclusionTexture;
uniform int hasEmissiveTexture;

// ── Normal mapping (tangent-space → world-space via screen-space dFdX/dFdY) ──
vec3 getNormalFromMap()
{
    if (hasNormalTexture == 0)
        return normalize(Normal);

    vec3 normal = texture(normalMap, TexCoord).rgb;
    normal = normal * 2.0 - 1.0;
    normal.xy *= normalScale;

    vec3 N = normalize(Normal);
    vec3 dPos1 = dFdx(FragPos);
    vec3 dPos2 = dFdy(FragPos);
    vec2 dUV1 = dFdx(TexCoord);
    vec2 dUV2 = dFdy(TexCoord);

    vec3 T = normalize(dPos1 * dUV2.y - dPos2 * dUV1.y);
    vec3 B = -normalize(cross(N, T));
    mat3 TBN = mat3(T, B, N);

    return normalize(TBN * normal);
}

void main()
{
    // ── Base Color ─────────────────────────────────────────────────────────
    vec4 albedo = vec4(0.72, 0.68, 0.62, 1.0);
    if (useAlbedo == 1) albedo = texture(albedoMap, TexCoord);
    albedo *= baseColorFactor;
    if (albedo.a < 0.1) discard;

    // ── PBR Parameters ────────────────────────────────────────────────────
    float metallic = metallicFactor;
    float roughness = roughnessFactor;
    if (hasMetallicRoughnessTexture == 1) {
        vec2 mrSample = texture(metallicRoughnessMap, TexCoord).gb;
        metallic = mrSample.g * metallicFactor;
        roughness = mrSample.r * roughnessFactor;
    }
    roughness = max(roughness, 0.04);

    // ── Output ─────────────────────────────────────────────────────────────
    if (gbufferMode == 0)
    {
        // Mode 0: Albedo (RGB) + Roughness (A)
        // Roughness packed into alpha (0..1)
        FragColor = vec4(albedo.rgb, roughness);
    }
    else
    {
        // Mode 1: World Normal XY (RG, encoded [0,1]) + Metallic (B) + Occlusion (A)
        // Normal Z is reconstructed in the impostor shader: Z = sqrt(1 - x² - y²)
        vec3 N = getNormalFromMap();
        float occlusion = 1.0;
        if (hasOcclusionTexture == 1)
        {
            occlusion = texture(occlusionMap, TexCoord).r;
            occlusion = mix(1.0, occlusion, occlusionStrength);
        }
        FragColor = vec4(N.xy * 0.5 + 0.5, metallic, occlusion);
    }
}
