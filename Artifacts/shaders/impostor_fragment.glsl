#version 330 core

uniform sampler2D impostorAtlas;
uniform vec2 atlasTiles;     // (8, 2) — row 0 = albedo+roughness, row 1 = normal+metallic
uniform vec3 viewPos;
uniform vec3 center;
uniform vec3 sunDir;
uniform vec3 lightColor;
uniform vec3 fogColor;
uniform int useFog;
uniform float debugOpacity;   // 1.0 = opaque (normal), <1.0 = semi-transparent (debug)

// ── CSM Shadow uniforms ──────────────────────────────────────────────────
uniform sampler2D shadowMap0;
uniform sampler2D shadowMap1;
uniform sampler2D shadowMap2;
uniform mat4 lightSpaceMatrices[3];
uniform float cascadeEnds[3];
uniform int shadowFilterMode;
uniform float shadowOffsetY;  // upward offset from ground for shadow sampling

in vec2 vUV;
in vec3 vWorldPos;           // world-space position for PBR view direction

out vec4 FragColor;

// ── PBR constants ─────────────────────────────────────────────────────────
const float PI = 3.14159265359;

// ── Poisson helpers (for PCF shadow filtering) ────────────────────────────
float randomAngle(vec2 uv)
{
    return fract(sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453);
}

const vec2 poissonDisk16[16] = vec2[](
    vec2(0.0152, 2.9841), vec2(0.0541, 0.5123), vec2(0.1128, 4.2219), vec2(0.1894, 1.3347),
    vec2(0.2817, 5.6128), vec2(0.3869, 3.0471), vec2(0.5018, 0.1029), vec2(0.6234, 3.8762),
    vec2(0.7481, 1.9024), vec2(0.8723, 5.1487), vec2(0.9512, 2.4129), vec2(0.9941, 0.3451),
    vec2(0.8203, 4.7892), vec2(0.6904, 0.9821), vec2(0.5609, 2.7418), vec2(0.4302, 5.4983)
);

bool setupShadow(vec4 fragPosLightSpace, out vec2 uv, out float receiverZ)
{
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;
    uv = projCoords.xy;
    receiverZ = projCoords.z;
    return projCoords.z > 1.0 || projCoords.z < 0.0;
}

float hardShadow(vec4 fragPosLightSpace, sampler2D shadowMap, float bias)
{
    vec2 uv; float receiverZ;
    if (setupShadow(fragPosLightSpace, uv, receiverZ)) return receiverZ > 1.0 ? 1.0 : 0.0;
    float d = texture(shadowMap, uv).r;
    return (receiverZ - bias > d) ? 0.0 : 1.0;
}

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

float CalculateShadow(vec4 fragPosLightSpace, sampler2D shadowMap, float bias)
{
    if (shadowFilterMode == 1) return hardShadow(fragPosLightSpace, shadowMap, bias);
    return poisson16(fragPosLightSpace, shadowMap, bias, 5.0);
}

// ── PBR functions (matches gltf_fragment.glsl) ────────────────────────────
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
    // ── Compute horizontal angle from camera to center ──────────────────────
    vec3 toCenter = normalize(center - viewPos);
    float angle = atan(toCenter.z, toCenter.x);

    // Map angle [-PI, PI] → [0, 8) to select from 8 views
    float tileF = (angle / 3.14159265) * 0.5 + 0.5;
    tileF = tileF * 8.0;

    // Blend between the two nearest tiles
    int tileA = int(mod(floor(tileF), 8.0));
    int tileB = int(mod(ceil(tileF), 8.0));
    float blend = fract(tileF);

    vec2 tileSize = 1.0 / atlasTiles; // (1/8, 1/2)

    // ── Sample G-buffer: Row 0 = Albedo (RGB) + Roughness (A) ──────────────
    // Row 1 = Normal XY (RG encoded [0,1]) + Metallic (B) + Occlusion (A)
    //   Normal Z reconstructed: Z = sqrt(1 - x² - y²)

    // Tile A — row 0 (albedo+roughness), row 1 (normalXY+metallic+occlusion)
    vec2 uvA_base = vUV * tileSize + vec2(tileA, 0) * tileSize;
    vec2 uvA_gbuf = vUV * tileSize + vec2(tileA, 1) * tileSize;

    // Tile B — row 0 (albedo+roughness), row 1 (normalXY+metallic+occlusion)
    vec2 uvB_base = vUV * tileSize + vec2(tileB, 0) * tileSize;
    vec2 uvB_gbuf = vUV * tileSize + vec2(tileB, 1) * tileSize;

    // Sample and blend between the two nearest tiles
    vec4 albedoRoughA = texture(impostorAtlas, uvA_base);
    vec4 albedoRoughB = texture(impostorAtlas, uvB_base);
    vec4 albedoRough = mix(albedoRoughA, albedoRoughB, blend);

    vec4 gbufA = texture(impostorAtlas, uvA_gbuf);
    vec4 gbufB = texture(impostorAtlas, uvB_gbuf);
    vec4 gbuf = mix(gbufA, gbufB, blend);

    // Discard background pixels: row 0 alpha = roughness (NOT transparency).
    // Background has roughness=0 (clear color). Tree has roughness >= 0.04
    // (clamped in bake shader). We use a 0.02 threshold to cleanly separate.
    // GL_LINEAR filtering at tree edges blends with background (0,0,0,0),
    // causing darkened RGB + low roughness. The 0.02 threshold correctly
    // discards these blended edge pixels.
    if (albedoRough.a < 0.02) discard;

    vec3 albedo = albedoRough.rgb;
    float roughness = albedoRough.a;
    float metallic = gbuf.b;
    float occlusion = gbuf.a;

    // Decode normal: XY from RG channels, Z reconstructed
    vec3 N;
    N.xy = gbuf.rg * 2.0 - 1.0;
    N.z = sqrt(max(0.0, 1.0 - dot(N.xy, N.xy))); // max(0) guards GL_LINEAR filter precision
    N = normalize(N);

    // ── PBR lighting with scene sun direction ───────────────────────────────
    vec3 V = normalize(viewPos - vWorldPos);
    vec3 L = normalize(sunDir);
    vec3 H = normalize(L + V);

    // Night/day transition (matches gltf_fragment.glsl)
    float nightBlend = smoothstep(0.15, 0.0, sunDir.y);
    vec3 moonColor = vec3(0.25, 0.30, 0.45);
    vec3 effectiveLightColor = mix(lightColor, moonColor, nightBlend);
    float ambientStrength = mix(0.25, 0.18, nightBlend);

    float NdotV = max(dot(N, V), 0.001);
    float NdotL = max(dot(N, L), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float LdotH = max(dot(L, H), 0.0);

    // Cook-Torrance specular
    vec3 F0 = mix(vec3(0.04), albedo, metallic);
    vec3 F = FresnelSchlick(LdotH, F0);
    float D = DistributionGGX(N, H, roughness);
    float G = GeometrySmith(N, V, L, roughness);

    vec3 kd = (vec3(1.0) - F) * (1.0 - metallic);
    vec3 kSpecular = F * D * G / max(4.0 * NdotV * NdotL, 0.001);

    // Diffuse + specular lighting
    vec3 Lo = (kd * albedo / PI + kSpecular) * effectiveLightColor * NdotL;

    // Ambient (with occlusion, matching in-game: ambient *= occlusion)
    vec3 ambient = ambientStrength * albedo * effectiveLightColor * occlusion;

    // ── CSM Shadow ──────────────────────────────────────────────────────────
    vec3 shadowPos = center + vec3(0.0, shadowOffsetY, 0.0);
    vec4 lightSpacePos = vec4(shadowPos, 1.0);
    float centerDepth = length(viewPos - center);
    float blendRange0 = cascadeEnds[0] * 0.1;
    float blendRange1 = cascadeEnds[1] * 0.1;
    float bias = 0.0005;

    bool insideFrustum = false;
    for (int ci = 0; ci < 3; ci++) {
        vec4 clip = lightSpaceMatrices[ci] * lightSpacePos;
        vec3 ndc = clip.xyz / clip.w;
        if (abs(ndc.x) <= 1.0 && abs(ndc.y) <= 1.0 && abs(ndc.z) <= 1.0) {
            insideFrustum = true;
            break;
        }
    }

    float shadow;
    if (!insideFrustum) {
        shadow = 0.35;
    } else {
        shadow = 1.0;
        if (centerDepth < cascadeEnds[0] - blendRange0) {
            shadow = CalculateShadow(lightSpaceMatrices[0] * lightSpacePos, shadowMap0, bias);
        } else if (centerDepth < cascadeEnds[0]) {
            float t = (centerDepth - (cascadeEnds[0] - blendRange0)) / blendRange0;
            float s0 = CalculateShadow(lightSpaceMatrices[0] * lightSpacePos, shadowMap0, bias);
            float s1 = CalculateShadow(lightSpaceMatrices[1] * lightSpacePos, shadowMap1, bias * 1.5);
            shadow = mix(s0, s1, t);
        } else if (centerDepth < cascadeEnds[1] - blendRange1) {
            shadow = CalculateShadow(lightSpaceMatrices[1] * lightSpacePos, shadowMap1, bias * 1.5);
        } else if (centerDepth < cascadeEnds[1]) {
            float t = (centerDepth - (cascadeEnds[1] - blendRange1)) / blendRange1;
            float s1 = CalculateShadow(lightSpaceMatrices[1] * lightSpacePos, shadowMap1, bias * 1.5);
            float s2 = CalculateShadow(lightSpaceMatrices[2] * lightSpacePos, shadowMap2, bias * 3.0);
            shadow = mix(s1, s2, t);
        } else {
            shadow = CalculateShadow(lightSpaceMatrices[2] * lightSpacePos, shadowMap2, bias * 3.0);
        }
    }

    // Apply shadow (same as in-game gltf_fragment)
    float shadowDarken = mix(0.65, 0.78, nightBlend);
    ambient *= mix(shadowDarken, 1.0, shadow);
    Lo *= shadow;

    // ── Combine ────────────────────────────────────────────────────────────
    vec3 result = ambient + Lo;

    // ── Fog (pre-tonemap, matching gltf_fragment & terrain order) ──────────
    if (useFog == 1)
    {
        float dist = length(viewPos - center);
        float fogDensity = 0.01;
        float fogFactor = exp(-pow(dist * fogDensity, 2.0));
        fogFactor = clamp(fogFactor, 0.0, 1.0);
        result = mix(fogColor, result, fogFactor);
    }

    // ── Tone mapping + gamma ──────────────────────────────────────────────
    result = result / (result + vec3(1.0));
    result = pow(result, vec3(1.0 / 2.2));

    FragColor = vec4(result, debugOpacity);
}
