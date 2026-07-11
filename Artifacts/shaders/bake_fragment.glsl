#version 330 core

out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec2 TexCoord;

// ── Base Lighting ──────────────────────────────────────────────────────────
uniform vec3 sunDir;
uniform vec3 lightColor;
uniform vec3 viewPos;

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

const float PI = 3.14159265359;

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

vec3 FresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

float DistributionGGX(vec3 N, vec3 H, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    float nom   = a2;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;
    return nom / denom;
}

float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;
    float nom   = NdotV;
    float denom = NdotV * (1.0 - k) + k;
    return nom / denom;
}

float GeometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float ggx2  = GeometrySchlickGGX(NdotV, roughness);
    float ggx1  = GeometrySchlickGGX(NdotL, roughness);
    return ggx1 * ggx2;
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
        // glTF 2.0: G channel = Roughness, B channel = Metallic
        metallic = mrSample.g * metallicFactor;
        roughness = mrSample.r * roughnessFactor;
    }
    roughness = max(roughness, 0.04);

    // ── Normal Mapping ────────────────────────────────────────────────────
    vec3 N = getNormalFromMap();
    vec3 V = normalize(viewPos - FragPos);

    // ── Fixed bake lighting (no night/day, no shadows) ────────────────────
    vec3 L = normalize(sunDir);
    vec3 H = normalize(L + V);

    float NdotV = max(dot(N, V), 0.001);
    float NdotL = max(dot(N, L), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float LdotH = max(dot(L, H), 0.0);

    // ── Cook-Torrance Specular ────────────────────────────────────────────
    vec3 F0 = mix(vec3(0.04), albedo.rgb, metallic);
    vec3 F = FresnelSchlick(LdotH, F0);

    float D = DistributionGGX(N, H, roughness);
    float G = GeometrySmith(N, V, L, roughness);

    vec3 kd = (vec3(1.0) - F) * (1.0 - metallic);
    vec3 kSpecular = F * D * G / max(4.0 * NdotV * NdotL, 0.001);
    vec3 Lo = (kd * albedo.rgb / PI + kSpecular) * lightColor * NdotL;

    // ── Ambient + Occlusion ────────────────────────────────────────────────
    float ambientStrength = 0.25;
    vec3 ambient = ambientStrength * albedo.rgb * lightColor;

    float occlusion = 1.0;
    if (hasOcclusionTexture == 1)
    {
        occlusion = texture(occlusionMap, TexCoord).r;
        occlusion = mix(1.0, occlusion, occlusionStrength);
    }

    // ── Emissive ───────────────────────────────────────────────────────────
    vec3 emissive = vec3(0.0);
    if (hasEmissiveTexture == 1)
        emissive = texture(emissiveMap, TexCoord).rgb * emissiveFactor;
    else
        emissive = emissiveFactor;

    // ── Occlusion-based ambient darkening (matches in-game AO + shadowDarken) ──
    // In-game: ambient *= occlusion (AO) then ambient *= mix(0.65,1.0,shadow) (shadow).
    // Lo *= shadow (CSM shadow only — AO does NOT affect Lo in in-game).
    // We approximate: ambient gets AO + uniform shadowDarken; Lo is unoccluded.
    ambient *= occlusion;  // same as in-game: AO on ambient
    float bakeShadow = mix(0.65, 1.0, occlusion);
    ambient *= bakeShadow; // proxy for in-game ambientShadow = mix(shadowDarken,1.0,shadow)

    // ── Combine ────────────────────────────────────────────────────────────
    vec3 result = ambient + Lo + emissive;

    // ── Tone mapping + gamma ──────────────────────────────────────────────
    result = result / (result + vec3(1.0));
    result = pow(result, vec3(1.0 / 2.2));

    FragColor = vec4(result, albedo.a);
}
