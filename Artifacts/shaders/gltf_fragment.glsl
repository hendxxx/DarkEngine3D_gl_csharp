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

// ── LIVE SHADOW TUNING (uploaded from the IDE Shadow Settings panel; the defaults
// match the values that were previously hardcoded here) ──
uniform float u_ConstantBias = 0.0;   // always-added bias (all surfaces)
uniform float u_SlopeBias = 0.0004;  // slope-scaled bias coefficient (~5 texels steep)
uniform float u_MinBias = 0.0002;    // minimum bias (~2.5 texels flat, light-facing surfaces)
uniform float u_BlendRange = 0.10;   // cascade blend width, fraction of the split distance
uniform vec3 u_DepthRange = vec3(1.0); // world ortho depth range per cascade (zFar - zNear)
uniform vec3 u_TexelWorld = vec3(1.0); // world size of one shadow-map texel per cascade
uniform vec3 u_MaxWorldBias = vec3(0.15, 0.25, 0.5); // per-cascade cap on the bias' WORLD offset (m)

// Debug overlay (L key): tint each cascade with a transparent color code.
uniform int showCSMCascadeColor;

// Cascade overlay strength (L key debug tint), 0..1 — adjustable from the Shadow panel.
uniform float u_CascadeOverlayAlpha = 0.15;
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

// ── Shadow Filter Mode (0-9: PCF 16, Hard, PCF 16, PCF 16 Soft, PCF 32, PCF 32 Soft, PCSS 16, PCSS 16 Soft, PCSS 32, PCSS 32 Soft) ──
uniform int shadowFilterMode;

// ── Fog ────────────────────────────────────────────────────────────────────
uniform int useFog;

// ── HLOD Alpha Fade (set per-frame by HLOD renderer, default 1.0) ──
uniform float hlodAlpha = 1.0;

const float PI = 3.14159265359;

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

// ── Poisson-disk helpers ──────────────────────────────────────────────────

// Pseudo-random rotation angle from fragment screen position
float randomAngle(vec2 uv)
{
    return fract(sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453);
}

// 16 uniformly-distributed points on a unit disk (radius², angle)
const vec2 poissonDisk16[16] = vec2[](
    vec2(0.0152, 2.9841), vec2(0.0541, 0.5123), vec2(0.1128, 4.2219), vec2(0.1894, 1.3347),
    vec2(0.2817, 5.6128), vec2(0.3869, 3.0471), vec2(0.5018, 0.1029), vec2(0.6234, 3.8762),
    vec2(0.7481, 1.9024), vec2(0.8723, 5.1487), vec2(0.9512, 2.4129), vec2(0.9941, 0.3451),
    vec2(0.8203, 4.7892), vec2(0.6904, 0.9821), vec2(0.5609, 2.7418), vec2(0.4302, 5.4983)
);

// 32 uniformly-distributed points on a unit disk (pre-computed Poisson disk)
// Values are pairs of (radius², angle) packed as (x, y) — radius² for
// uniform density, angle in radians.
const vec2 poissonDisk32[32] = vec2[](
    vec2(0.0034, 2.9785), vec2(0.0162, 5.8570), vec2(0.0386, 1.4657), vec2(0.0696, 4.4191),
    vec2(0.1087, 0.0767), vec2(0.1548, 2.6142), vec2(0.2079, 5.2858), vec2(0.2679, 1.0664),
    vec2(0.3329, 4.4218), vec2(0.4021, 0.6931), vec2(0.4732, 3.6884), vec2(0.5456, 0.2867),
    vec2(0.6186, 2.9115), vec2(0.6909, 5.6626), vec2(0.7611, 1.8262), vec2(0.8279, 4.6792),
    vec2(0.8893, 1.2053), vec2(0.9443, 4.0302), vec2(0.9912, 0.4060), vec2(0.9920, 3.3644),
    vec2(0.9447, 6.1380), vec2(0.8905, 2.3424), vec2(0.8294, 5.2761), vec2(0.7622, 0.9003),
    vec2(0.6903, 3.8174), vec2(0.6161, 0.0229), vec2(0.5434, 2.7208), vec2(0.4725, 5.7597),
    vec2(0.4034, 1.5024), vec2(0.3355, 4.6110), vec2(0.2685, 0.5141), vec2(0.2025, 3.6672)
);

// Shared projCoords setup — returns projected UV or early-out values
// stored in retVal for the caller to return immediately.
// We use out-parameters since GLSL doesn't have multiple return values.
bool setupShadow(vec4 fragPosLightSpace, out vec2 uv, out float receiverZ)
{
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;
    uv = projCoords.xy;
    receiverZ = projCoords.z;
    return projCoords.z > 1.0 || projCoords.z < 0.0;
}

// Poisson 16 PCF with configurable radius (texels)
float poisson16(vec4 fragPosLightSpace, sampler2D shadowMap, float bias, float radius)
{
    vec2 uv; float receiverZ;
    if (setupShadow(fragPosLightSpace, uv, receiverZ)) return receiverZ > 1.0 ? 1.0 : 0.0;

    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);
    float angle = randomAngle(gl_FragCoord.xy) * 6.2831853;

    float shadow = 0.0;
    for (int i = 0; i < 16; ++i)
    {
        float r = sqrt(poissonDisk16[i].x) * radius;
        float a = poissonDisk16[i].y + angle;
        vec2 offset = vec2(r * cos(a), r * sin(a));
        float d = texture(shadowMap, uv + offset * texelSize).r;
        shadow += (receiverZ - bias > d) ? 0.0 : 1.0;
    }
    return shadow / 16.0;
}



// ── Poisson 32-disk PCF with configurable radius (texels) ──
// 32 irregular samples with per-fragment rotation give smooth,
// natural shadow edges without grid or blocky artifacts.
float poisson32(vec4 fragPosLightSpace, sampler2D shadowMap, float bias, float radius)
{
    vec2 uv; float receiverZ;
    if (setupShadow(fragPosLightSpace, uv, receiverZ)) return receiverZ > 1.0 ? 1.0 : 0.0;

    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);
    float angle = randomAngle(gl_FragCoord.xy) * 6.2831853;

    float shadow = 0.0;
    for (int i = 0; i < 32; ++i)
    {
        float r = sqrt(poissonDisk32[i].x) * radius;
        float a = poissonDisk32[i].y + angle;
        vec2 offset = vec2(r * cos(a), r * sin(a));
        float d = texture(shadowMap, uv + offset * texelSize).r;
        shadow += (receiverZ - bias > d) ? 0.0 : 1.0;
    }
    return shadow / 32.0;
}

// ── Mode 1: Hard shadow (single sample, no filtering) ──
float hardShadow(vec4 fragPosLightSpace, sampler2D shadowMap, float bias)
{
    vec2 uv; float receiverZ;
    if (setupShadow(fragPosLightSpace, uv, receiverZ)) return receiverZ > 1.0 ? 1.0 : 0.0;
    float d = texture(shadowMap, uv).r;
    return (receiverZ - bias > d) ? 0.0 : 1.0;
}

// ── PCSS Helpers ─────────────────────────────────────────────────────────

// Find average blocker depth in search region
float SearchBlocker(sampler2D shadowMap, vec2 uv, float zReceiver, float searchRadius)
{
    float blockers = 0.0;
    float count = 0.0;
    vec2 texel = 1.0 / textureSize(shadowMap, 0);
    for (int x = -2; x <= 2; x++)
    for (int y = -2; y <= 2; y++)
    {
        vec2 offset = vec2(x, y) * texel * searchRadius;
        float d = texture(shadowMap, uv + offset).r;
        if (d < zReceiver - 0.0002) { blockers += d; count += 1.0; }
    }
    if (count < 1.0) return -1.0;
    return blockers / count;
}

// PCSS with Poisson sampling
float pcss(sampler2D shadowMap, vec4 fragPosLightSpace, float bias, int sampleCount, float maxRadius, float radiusScale)
{
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;
    if (projCoords.z > 1.0 || projCoords.z < 0.0) return 1.0;

    vec2 uv = clamp(projCoords.xy, 0.001, 0.999);
    float zReceiver = projCoords.z - bias;

    float avgBlocker = SearchBlocker(shadowMap, uv, zReceiver, 8.0 * radiusScale);
    if (avgBlocker < 0.0) return 1.0;

    float penumbra = (zReceiver - avgBlocker) / max(avgBlocker, 0.0001);
    penumbra = clamp(penumbra * 4.0, 0.0, 8.0);
    float filterRadius = clamp(penumbra * 0.006 * 350.0 * radiusScale, 1.5 * radiusScale, maxRadius * radiusScale);

    vec2 texel = 1.0 / textureSize(shadowMap, 0);
    float angle = randomAngle(gl_FragCoord.xy) * 6.2831853;
    int n = (sampleCount == 16) ? 16 : 32;

    float shadow = 0.0;
    for (int i = 0; i < n; i++)
    {
        int idx = (i * 3) % 32; // spread indices for variety
        float r = sqrt(poissonDisk32[idx].x) * filterRadius;
        float a = poissonDisk32[idx].y + angle;
        vec2 offset = vec2(r * cos(a), r * sin(a));
        float d = texture(shadowMap, uv + offset * texel).r;
        shadow += (zReceiver > d) ? 0.0 : 1.0;
    }
    return shadow / float(n);
}

// Dispatch — 10 modes consistent across all shaders
// 0=PCF 16, 1=Hard, 2=PCF 16, 3=PCF 16 Soft, 4=PCF 32, 5=PCF 32 Soft, 6=PCSS 16, 7=PCSS 16 Soft, 8=PCSS 32, 9=PCSS 32 Soft
float CalculateShadow(vec4 fragPosLightSpace, sampler2D shadowMap, float bias, float radiusScale)
{
    if (shadowFilterMode == 1) return hardShadow(fragPosLightSpace, shadowMap, bias);
    if (shadowFilterMode == 2) return poisson16(fragPosLightSpace, shadowMap, bias, 5.0 * radiusScale);
    if (shadowFilterMode == 3) return poisson16(fragPosLightSpace, shadowMap, bias, 10.0 * radiusScale);
    if (shadowFilterMode == 4) return poisson32(fragPosLightSpace, shadowMap, bias, 5.0 * radiusScale);
    if (shadowFilterMode == 5) return poisson32(fragPosLightSpace, shadowMap, bias, 10.0 * radiusScale);
    if (shadowFilterMode == 6) return pcss(shadowMap, fragPosLightSpace, bias, 16, 6.0, radiusScale);
    if (shadowFilterMode == 7) return pcss(shadowMap, fragPosLightSpace, bias, 16, 12.0, radiusScale);
    if (shadowFilterMode == 8) return pcss(shadowMap, fragPosLightSpace, bias, 32, 6.0, radiusScale);
    if (shadowFilterMode == 9) return pcss(shadowMap, fragPosLightSpace, bias, 32, 12.0, radiusScale);
    return poisson16(fragPosLightSpace, shadowMap, bias, 5.0 * radiusScale); // default mode 0  
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
    float blendRange0 = cascadeEnds[0] * u_BlendRange;
    float blendRange1 = cascadeEnds[1] * u_BlendRange;
    // Slope-scaled bias — the base term is raised to kill self-shadow acne on detailed
    // geometry (the minimum clamps the bias so flat, light-facing surfaces stay acne-free).
    // Uses the GEOMETRIC normal (not the normal-mapped N) so bump detail never spikes the
    // bias into patchy peter-panning.
    float bias0 = max(u_ConstantBias + u_SlopeBias * (1.0 - max(dot(normalize(Normal), L), 0.0)), u_MinBias);
    // Normalize the NDC bias by each cascade's ortho depth range (uploaded from CSM):
    // worldOffset = bias_ndc × depthRange, and far cascades have 10×+ larger Z ranges — a
    // fixed NDC bias would push shadows tens of world units away and they vanish at distance.
    // World bias offset proportional to the LOCAL texel size (texel_i / texel_0): a constant
    // texel count at every distance, so far cascades get proportionally more bias instead of
    // the old fixed 1.5×/3× (which under-shot far cascades → sub-texel acne). The depth-range
    // term converts that world offset into NDC; the ratio is clamped so a pathological small
    // cascade-0 range can never explode the far-cascade bias.
    float bias1 = bias0 * max(u_TexelWorld.y / max(u_TexelWorld.x, 1e-5), 1.0)
                * clamp(u_DepthRange.x / u_DepthRange.y, 0.02, 4.0);
    float bias2 = bias0 * max(u_TexelWorld.z / max(u_TexelWorld.x, 1e-5), 1.0)
                * clamp(u_DepthRange.x / u_DepthRange.z, 0.02, 4.0);
    // Peter-panning guard: the texel-proportional scaling keeps a constant TEXEL count,
    // but in the far cascades a texel is ~0.5-1 m, so the bias' WORLD offset (bias × range)
    // would reach meters and the shadow detaches from its caster (bright outline). Cap
    // the world offset directly, per cascade — cascade 0 tight (kills the outline at
    // object bases), cascade 2 loose (keeps anti-acne at distance).
    bias0 = min(bias0, u_MaxWorldBias.x / max(u_DepthRange.x, 1e-4));
    bias1 = min(bias1, u_MaxWorldBias.y / max(u_DepthRange.y, 1e-4));
    bias2 = min(bias2, u_MaxWorldBias.z / max(u_DepthRange.z, 1e-4));

    // PCF/PCSS radii are in TEXELS — scale per cascade by the texel-size ratio so the
    // WORLD penumbra stays constant at every distance (a fixed 5-texel disk covers 0.15 m
    // in cascade 0 but ~6 m in cascade 2). worldWidth = radius × texelWorld stays the same
    // when radius ∝ texel0/texel_i; clamped so a pathological tiny near texel can't blow
    // the far-cascade radius up (ratio > 1 is capped at 1.0).
    float rScale1 = clamp(u_TexelWorld.x / max(u_TexelWorld.y, 1e-5), 0.25, 1.0);
    float rScale2 = clamp(u_TexelWorld.x / max(u_TexelWorld.z, 1e-5), 0.25, 1.0);
    
    int cascadeIndex = 0;
    float cascadeBlendT = 0.0;
    float shadow;
    if (depth < cascadeEnds[0] - blendRange0) {
        shadow = CalculateShadow(lightSpaceMatrices[0] * vec4(FragPos, 1.0), shadowMap0, bias0, 1.0);
        cascadeIndex = 0;
    } else if (depth < cascadeEnds[0]) {
        float t = (depth - (cascadeEnds[0] - blendRange0)) / blendRange0;
        float s0 = CalculateShadow(lightSpaceMatrices[0] * vec4(FragPos, 1.0), shadowMap0, bias0, 1.0);
        float s1 = CalculateShadow(lightSpaceMatrices[1] * vec4(FragPos, 1.0), shadowMap1, bias1, rScale1);
        shadow = mix(s0, s1, t);
        cascadeIndex = 1;
        cascadeBlendT = t;
    } else if (depth < cascadeEnds[1] - blendRange1) {
        shadow = CalculateShadow(lightSpaceMatrices[1] * vec4(FragPos, 1.0), shadowMap1, bias1, rScale1);
        cascadeIndex = 1;
    } else if (depth < cascadeEnds[1]) {
        float t = (depth - (cascadeEnds[1] - blendRange1)) / blendRange1;
        float s1 = CalculateShadow(lightSpaceMatrices[1] * vec4(FragPos, 1.0), shadowMap1, bias1, rScale1);
        float s2 = CalculateShadow(lightSpaceMatrices[2] * vec4(FragPos, 1.0), shadowMap2, bias2, rScale2);
        shadow = mix(s1, s2, t);
        cascadeIndex = 2;
        cascadeBlendT = t;
    } else {
        shadow = CalculateShadow(lightSpaceMatrices[2] * vec4(FragPos, 1.0), shadowMap2, bias2, rScale2);
        cascadeIndex = 2;
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
    
    // Add ambient lighting (weather-dimmed via activeColor)
    vec3 ambient = ambientStrength * albedo.rgb * activeColor;
    
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
    // Darken ambient in shadow (but keep Lo already shadowed, emissive unshadowed)
    float ambientShadow = mix(shadowDarken, 1.0, shadow);
    vec3 result = ambient * ambientShadow + Lo + emissive;
    
    // ── Fog Application ───────────────────────────────────────────────────────
    if (useFog == 1)
    {
        float dist = length(viewPos - FragPos);
        float fogDensity = 0.01;
        float fogFactor = exp(-pow(dist * fogDensity, 2.0));
        fogFactor = clamp(fogFactor, 0.0, 1.0);
        result = mix(fogColor, result, fogFactor);
    }

    // ── DEBUG CSM COLOR (transparent cascade overlay, same palette as the main shader) ──
    if (showCSMCascadeColor == 1) {
        vec3 cascadeColors[3] = vec3[](
            vec3(0.0, 1.0, 1.0),   // cyan   — cascade 0
            vec3(1.0, 1.0, 0.0),   // yellow — cascade 1
            vec3(1.0, 0.0, 1.0)    // magenta— cascade 2
        );

        vec3 cColor = cascadeColors[cascadeIndex];
        if (cascadeBlendT > 0.0 && cascadeIndex > 0) {
            vec3 prevColor = cascadeColors[cascadeIndex - 1];
            cColor = mix(prevColor, cColor, cascadeBlendT);
        }
        result = mix(result, cColor, u_CascadeOverlayAlpha);
    }

    // ── Tone mapping + gamma ──────────────────────────────────────────────────
    result = result / (result + vec3(1.0));
    result = pow(result, vec3(1.0 / 2.2));

    FragColor = vec4(result, hlodAlpha);
}
