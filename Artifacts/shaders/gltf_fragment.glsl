#version 330 core

out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec2 TexCoord;

// ── Base Lighting ──────────────────────────────────────────────────────────
uniform vec3 sunDir;
uniform vec3 lightColor;
uniform vec3 viewPos;
uniform vec3 fogColor;

// ── CSM Shadow Maps ────────────────────────────────────────────────────────
uniform sampler2D shadowMap0;
uniform sampler2D shadowMap1;
uniform sampler2D shadowMap2;
uniform mat4 lightSpaceMatrices[3];
uniform float cascadeEnds[3];

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

// ── Fog ────────────────────────────────────────────────────────────────────
uniform int useFog;

const float PI = 3.14159265359;

// ── HYBRID PCSS + EVSM ──────────────────────────────────────
const float EVSM_C = 30.0;

// Reconstruct raw depth from EVSM R channel: z = ln(R) / c
float ReadEVSMDepth(sampler2D evsmMap, vec2 uv)
{
    float r = texture(evsmMap, uv).r;
    return log(max(r, 0.000001)) / EVSM_C;
}

// Standard EVSM Chebyshev inequality with the max(p, p_max) pattern.
// When receiver <= mean (shallower): fully lit (p=1).
// When receiver > mean (deeper): Chebyshev upper-bound shadow probability.
float Chebyshev_Positive(float mu, float mu2, float md)
{
    float p = (md <= mu) ? 1.0 : 0.0;
    float variance = max(mu2 - mu * mu, 0.00001);
    float d = md - mu;
    float p_max = variance / (variance + d * d);
    return max(p, p_max);
}

// Dual EVSM Chebyshev test using both positive and negative warps.
// Takes min of both tests to reduce light bleeding.
float EVSM_Chebyshev(sampler2D evsmMap, vec2 uv, float zReceiver)
{
    vec4 evsm = texture(evsmMap, uv);

    // Positive EVSM: moments = (exp(c*z), exp(2c*z))
    float mu_pos  = evsm.r;
    float mu2_pos = evsm.g;
    float md_pos  = exp(EVSM_C * zReceiver);
    float p_pos   = Chebyshev_Positive(mu_pos, mu2_pos, md_pos);

    // Negative EVSM: moments = (exp(-c*z), exp(-2c*z))
    float mu_neg  = evsm.b;
    float mu2_neg = evsm.a;
    float md_neg  = exp(-EVSM_C * zReceiver);
    float p_neg   = Chebyshev_Positive(mu_neg, mu2_neg, md_neg);

    return clamp(min(p_pos, p_neg), 0.0, 1.0);
}

// PCSS blocker search using EVSM depth reconstruction
float SearchBlocker_EVSM(sampler2D evsmMap, vec2 uv, float zReceiver, float searchRadius)
{
    float blockers = 0.0;
    float count = 0.0;
    vec2 texel = 1.0 / vec2(textureSize(evsmMap, 0));
    for (int x = -2; x <= 2; x++)
    for (int y = -2; y <= 2; y++)
    {
        vec2 offset = vec2(x, y) * texel * searchRadius;
        float sampleZ = ReadEVSMDepth(evsmMap, uv + offset);
        if (sampleZ < zReceiver - 0.0002) {
            blockers += sampleZ;
            count += 1.0;
        }
    }
    if (count < 1.0) return -1.0;
    return blockers / count;
}

// PCSS blocker search → penumbra → dual EVSM Chebyshev test → contact-hardening
float PCSS_EVSM(sampler2D evsmMap, vec4 fragPosLightSpace, float bias)
{
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;
    if(projCoords.z > 1.0) return 1.0;
    
    vec2 uv = clamp(projCoords.xy, 0.001, 0.999);
    float zReceiver = projCoords.z - bias;
    
    // PCSS Step 1: Blocker search via EVSM depth reconstruction
    float avgBlocker = SearchBlocker_EVSM(evsmMap, uv, zReceiver, 12.0);
    if (avgBlocker < 0.0) return 1.0;
    
    // PCSS Step 2: Penumbra estimation
    float penumbra = (zReceiver - avgBlocker) / max(avgBlocker, 0.0001);
    penumbra = clamp(penumbra * 4.0, 0.0, 6.0);
    
    // PCSS Step 3: Dual EVSM Chebyshev test (positive + negative)
    float shadow = EVSM_Chebyshev(evsmMap, uv, zReceiver);
    
    // PCSS Step 4: Contact-hardening modulation
    // Small penumbra → fully dark shadow (hard contact edge)
    // Large penumbra → slight brightening (soft, diffuse transition)
    float contactFactor = 1.0 - penumbra * 0.06;
    shadow = 1.0 - (1.0 - shadow) * contactFactor;
    
    return clamp(shadow, 0.0, 1.0);
}

// ── PBR FUNCTIONS ──────────────────────────────────────────────────────────

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

float CalculateShadow(vec4 fragPosLightSpace, sampler2D evsmMap, float bias) {
    return PCSS_EVSM(evsmMap, fragPosLightSpace, bias);
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
        metallic = mrSample.r * metallicFactor;
        roughness = mrSample.g * roughnessFactor;
    }
    roughness = max(roughness, 0.04);
    
    // ── Normal Mapping ────────────────────────────────────────────────────
    vec3 N = getNormalFromMap();
    vec3 V = normalize(viewPos - FragPos);
    
    // ── Night/Day Transition ──────────────────────────────────────────────
    float nightBlend = smoothstep(0.15, 0.0, sunDir.y);
    vec3 moonDir = normalize(vec3(-sunDir.x, 0.7, -sunDir.z));
    vec3 moonColor = vec3(0.25, 0.30, 0.45);
    vec3 L = normalize(mix(normalize(sunDir), moonDir, nightBlend));
    vec3 activeColor = mix(lightColor, moonColor, nightBlend);
    float ambientStrength = mix(0.25, 0.18, nightBlend);
    // Shadow darkening
    float shadowDarken = mix(0.65, 0.78, nightBlend);

    // ── Shadow Calculation ────────────────────────────────────────────────
    float depth = length(viewPos - FragPos);
    float blendRange0 = cascadeEnds[0] * 0.1;
    float blendRange1 = cascadeEnds[1] * 0.1;
    float bias0 = max(0.002 * (1.0 - dot(N, L)), 0.0001);
    float bias1 = bias0 * 1.5;
    float bias2 = bias0 * 3.0;
    
    float shadow;
    if (depth < cascadeEnds[0] - blendRange0) {
        shadow = CalculateShadow(lightSpaceMatrices[0] * vec4(FragPos, 1.0), shadowMap0, bias0);
    } else if (depth < cascadeEnds[0]) {
        float t = (depth - (cascadeEnds[0] - blendRange0)) / blendRange0;
        float s0 = CalculateShadow(lightSpaceMatrices[0] * vec4(FragPos, 1.0), shadowMap0, bias0);
        float s1 = CalculateShadow(lightSpaceMatrices[1] * vec4(FragPos, 1.0), shadowMap1, bias1);
        shadow = mix(s0, s1, t);
    } else if (depth < cascadeEnds[1] - blendRange1) {
        shadow = CalculateShadow(lightSpaceMatrices[1] * vec4(FragPos, 1.0), shadowMap1, bias1);
    } else if (depth < cascadeEnds[1]) {
        float t = (depth - (cascadeEnds[1] - blendRange1)) / blendRange1;
        float s1 = CalculateShadow(lightSpaceMatrices[1] * vec4(FragPos, 1.0), shadowMap1, bias1);
        float s2 = CalculateShadow(lightSpaceMatrices[2] * vec4(FragPos, 1.0), shadowMap2, bias2);
        shadow = mix(s1, s2, t);
    } else {
        shadow = CalculateShadow(lightSpaceMatrices[2] * vec4(FragPos, 1.0), shadowMap2, bias2);
    }
    
    // ── Cook-Torrance PBR Lighting ───────────────────────────────────────────
    vec3 H = normalize(L + V);
    float NdotV = max(dot(N, V), 0.001);
    float NdotL = max(dot(N, L), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float LdotH = max(dot(L, H), 0.0);
    
    // Calculate Fresnel component
    vec3 F0 = mix(vec3(0.04), albedo.rgb, metallic);
    vec3 F = FresnelSchlick(LdotH, F0);
    
    // Calculate distribution and geometry
    float D = DistributionGGX(N, H, roughness);
    float G = GeometrySmith(N, V, L, roughness);
    
    // Calculate Cook-Torrance BRDF
    vec3 kd = (vec3(1.0) - F) * (1.0 - metallic);
    vec3 kSpecular = F * D * G / max(4.0 * NdotV * NdotL, 0.001);
    vec3 Lo = (kd * albedo.rgb / PI + kSpecular) * activeColor * NdotL * shadow;
    
    // Add ambient lighting
    vec3 ambient = ambientStrength * albedo.rgb;
    
    // Apply occlusion if available
    float occlusion = 1.0;
    if (hasOcclusionTexture == 1)
    {
        occlusion = texture(occlusionMap, TexCoord).r;
        occlusion = mix(1.0, occlusion, occlusionStrength);
        ambient *= occlusion;
    }
    
    // Add emissive contribution
    vec3 emissive = vec3(0.0);
    if (hasEmissiveTexture == 1)
    {
        emissive = texture(emissiveMap, TexCoord).rgb * emissiveFactor;
    }
    else
    {
        emissive = emissiveFactor;
    }
    
    // Combine all contributions
    vec3 result = ambient + Lo + emissive* shadow;
    
    // ── Fog Application ───────────────────────────────────────────────────────
    if (useFog == 1)
    {
        float dist = length(viewPos - FragPos);
        float fogDensity = 0.01;
        float fogFactor = exp(-pow(dist * fogDensity, 2.0));
        fogFactor = clamp(fogFactor, 0.0, 1.0);
        result = mix(fogColor, result, fogFactor);
    }

    // ── Tone mapping + gamma ──────────────────────────────────────────────────
    result = result / (result + vec3(1.0));
    result = pow(result, vec3(1.0 / 2.2));
    result = mix(result, result * shadowDarken, 1.0 - shadow);

    FragColor = vec4(result, 1.0);
}
