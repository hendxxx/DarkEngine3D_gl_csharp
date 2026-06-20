#version 400 core
out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec3 ObjColor;
in vec2 TexCoord;
in float viewDepth;

// CONFIG
uniform vec3 sunDir, lightColor, viewPos, fogColor, heightScale;
uniform sampler2D tex0, tex1, tex2, tex3, tex4;
uniform int useTexture;

// CSM
uniform sampler2D shadowMap0;
uniform sampler2D shadowMap1;
uniform sampler2D shadowMap2;
uniform mat4 lightSpaceMatrices[3];
uniform float cascadeEnds[3];

// DEBUG
uniform int showLODColor;

uniform int lodLevel;
uniform int showCSMCascadeColor;
uniform int useFog;

uniform vec3 realSunDir;   // arah matahari asli dari CPU
uniform vec3 shadowDir;    // arah shadow (sun/moon blend)

// ======================================================
// NOISE & STOCHASTIC
// ======================================================
vec2 hash2(vec2 p) {
    return fract(sin(vec2(dot(p, vec2(127.1, 311.7)),
                          dot(p, vec2(269.5, 183.3)))) * 43758.5453);
}

float smoothNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = fract(sin(dot(i, vec2(12.9898, 78.233))) * 43758.5453);
    float b = fract(sin(dot(i + vec2(1.0, 0.0), vec2(12.9898, 78.233))) * 43758.5453);
    float c = fract(sin(dot(i + vec2(0.0, 1.0), vec2(12.9898, 78.233))) * 43758.5453);
    float d = fract(sin(dot(i + vec2(1.0, 1.0), vec2(12.9898, 78.233))) * 43758.5453);
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

vec3 stochasticSample(sampler2D tex, vec2 uv) {
    vec2 p = floor(uv);
    vec2 f = fract(uv);
    vec3 res = vec3(0.0);
    float weightSum = 0.0;
    for(int j=-1; j<=1; j++) {
        for(int i=-1; i<=1; i++) {
            vec2 b = vec2(i, j);
            vec2 r = hash2(p + b);
            vec3 sampleColor = texture(tex, uv + r).rgb;
            float dist = length(b - f + r);
            float w = exp(-2.0 * dist * dist);
            res += sampleColor * w;
            weightSum += w;
        }
    }
    return res / weightSum;
}

vec3 stochasticTriplanarCliff(sampler2D tex, vec3 worldPos, vec3 normal, float tiling) {
    vec3 blending = abs(normalize(normal));
    blending = pow(blending, vec3(10.0));
    blending /= (blending.x + blending.y + blending.z);

    vec3 xTex = stochasticSample(tex, worldPos.zy * tiling);
    vec3 yTex = stochasticSample(tex, worldPos.xz * tiling);
    vec3 zTex = stochasticSample(tex, worldPos.xy * tiling);

    return xTex * blending.x + yTex * blending.y + zTex * blending.z;
}

// ======================================================
// HYBRID PCSS + EVSM
// ======================================================
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
    // Handles near occluders where positive EVSM can over-darken
    float mu_neg  = evsm.b;
    float mu2_neg = evsm.a;
    float md_neg  = exp(-EVSM_C * zReceiver);
    float p_neg   = Chebyshev_Positive(mu_neg, mu2_neg, md_neg);

    return clamp(min(p_pos, p_neg), 0.0, 1.0);
}

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

float PCSS_EVSM(sampler2D evsmMap, vec4 fragPosLightSpace, float bias)
{
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0 || projCoords.z < 0.0)
        return 1.0;

    vec2 uv = clamp(projCoords.xy, 0.001, 0.999);
    float zReceiver = projCoords.z - bias;

    // PCSS Step 1: Blocker search via EVSM depth reconstruction
    float avgBlocker = SearchBlocker_EVSM(evsmMap, uv, zReceiver, 12.0);
    if (avgBlocker < 0.0)
        return 1.0;

    // PCSS Step 2: Penumbra estimation
    // Small penumbra = contact region (hard shadows)
    // Large penumbra = soft region (lighter transition)
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

// ======================================================
// MAIN
// ======================================================
void main() {
    vec3 norm = normalize(Normal);
    float slope = 1.0 - norm.y;

    // TEXTURING
    vec3 texColor;
    if (useTexture == 1) {
        float tilingDatar = 0.5;
        float tilingTebing = 0.055;

        vec3 t3 = stochasticTriplanarCliff(tex3, FragPos, norm, tilingTebing);

        vec2 uvDatar = FragPos.xz * tilingDatar;
        vec3 t0 = stochasticSample(tex0, uvDatar);
        vec3 t1 = stochasticSample(tex1, uvDatar);
        vec3 t2 = stochasticSample(tex2, uvDatar);

        float h = FragPos.y / max(heightScale.x, 1.0);
        float noise = smoothNoise(FragPos.xz * 0.2) * 0.1;
        float hn = h + noise;

        vec3 base;
        if(hn < 0.25) base = mix(t0, t1, smoothstep(0.1, 0.25, hn));
        else if(hn < 0.90) base = t1;
        else base = mix(t1, t2, smoothstep(0.90, 0.99, hn));

        float sn = slope + (noise * 0.1);
        float cliffMask = smoothstep(0.35, 0.45, sn);

        texColor = mix(base, t3 * 0.8, cliffMask);
    }
    else texColor = ObjColor;

     // LIGHTING
    float nightBlendFactor = smoothstep(0.15, 0.0, realSunDir.y);

    // arah bulan asli
    vec3 moonDir = normalize(-realSunDir);

    // arah cahaya untuk shading (sun → moon)
    vec3 activeLightDir = normalize(mix(realSunDir, moonDir, nightBlendFactor));

    // arah cahaya untuk SHADOW (sudah diputuskan di CPU)
    vec3 shadowLightDir = normalize(shadowDir);
    
    vec3 moonColor = vec3(0.08, 0.12, 0.25);
    vec3 targetNightColor = (length(lightColor) < 0.1) ? moonColor : lightColor;

    vec3 activeLightColor = mix(lightColor, targetNightColor, nightBlendFactor);
    float ambientStrength = mix(0.15, 0.04, nightBlendFactor);

    vec3 ambient = ambientStrength * activeLightColor;

    float diff = max(dot(norm, activeLightDir), 0.0);
    float slopeShadow = pow(1.0 - slope, 0.5);
    float finalDiff = diff * mix(0.7, 1.0, slopeShadow);

    // CSM + PCSS
    float depth = viewDepth;

    float blendRange0 = cascadeEnds[0] * 0.1;
    float blendRange1 = cascadeEnds[1] * 0.1;

    // FIX: gunakan arah bulan saat malam
    //vec3 shadowLightDir = (nightBlendFactor > 0.5) ? moonDir : sunDir; 

    // FIX: bias harus pakai shadowLightDir 
    float ndotl = max(dot(norm, shadowLightDir), 0.0);
    float baseBias = max(0.0005 * (1.0 - ndotl), 0.0005);
    float bias0 = baseBias;


        float shadow;
    int cascadeIndex = 0;
    float cascadeBlendT = 0.0;

    vec4 worldPos4 = vec4(FragPos, 1.0);

    if (depth < cascadeEnds[0] - blendRange0) {
        shadow = PCSS_EVSM(shadowMap0, lightSpaceMatrices[0] * worldPos4, bias0);
        cascadeIndex = 0;
    }
    else if (depth < cascadeEnds[0]) {
        float t = (depth - (cascadeEnds[0] - blendRange0)) / blendRange0;
        float s0 = PCSS_EVSM(shadowMap0, lightSpaceMatrices[0] * worldPos4, bias0);
        float s1 = PCSS_EVSM(shadowMap1, lightSpaceMatrices[1] * worldPos4, bias0);
        shadow = mix(s0, s1, t);
        cascadeIndex = 1;
        cascadeBlendT = t;
    }
    else if (depth < cascadeEnds[1] - blendRange1) {
        shadow = PCSS_EVSM(shadowMap1, lightSpaceMatrices[1] * worldPos4, bias0);
        cascadeIndex = 1;
    }
    else if (depth < cascadeEnds[1]) {
        float t = (depth - (cascadeEnds[1] - blendRange1)) / blendRange1;
        float s1 = PCSS_EVSM(shadowMap1, lightSpaceMatrices[1] * worldPos4, bias0);
        float s2 = PCSS_EVSM(shadowMap2, lightSpaceMatrices[2] * worldPos4, bias0);
        shadow = mix(s1, s2, t);
        cascadeIndex = 2;
        cascadeBlendT = t;
    }
    else {
        shadow = PCSS_EVSM(shadowMap2, lightSpaceMatrices[2] * worldPos4, bias0);
        cascadeIndex = 2;
    }

    // FIX: shadowMask harus pakai shadowLightDir
    float shadowMask = smoothstep(0.0, 0.20, dot(norm, shadowLightDir));

    vec3 diffuse = finalDiff * activeLightColor * shadowMask * shadow;

    vec3 result = (ambient + diffuse) * texColor;

    // DEBUG CSM COLOR
    if (showCSMCascadeColor == 1) {
        vec3 cascadeColors[3] = vec3[](
            vec3(1.0, 0.0, 0.0),
            vec3(0.0, 1.0, 0.0),
            vec3(0.0, 0.0, 1.0)
        );

        vec3 cColor = cascadeColors[cascadeIndex];

        if (cascadeBlendT > 0.0 && cascadeIndex > 0) {
            vec3 prevColor = cascadeColors[cascadeIndex - 1];
            cColor = mix(prevColor, cColor, cascadeBlendT);
        }

        result = mix(result, cColor, 0.35);
    }

    // DEBUG LOD COLOR
    if (showLODColor == 1) {
        vec3 lodColors[4] = vec3[](
            vec3(1.0, 0.0, 0.0),
            vec3(0.0, 1.0, 0.0),
            vec3(0.0, 0.0, 1.0),
            vec3(1.0, 1.0, 0.0)
        );
        result = mix(result, lodColors[clamp(lodLevel, 0, 3)], 0.1);
    }

    // ======================================================
    // FOG
    // ======================================================
    vec3 terrainWithFog;

    if (useFog == 1) {
        float dist = length(viewPos - FragPos);
        float fogDensity = 0.0035;
        float distanceFactor = exp(-pow(dist * fogDensity, 2.0));

        float fogFloor = 10.0;
        float fogHeightRange = 45.0;
        float heightFactor = clamp(1.0 - (FragPos.y - fogFloor) / fogHeightRange, 0.0, 1.0);
        heightFactor = pow(heightFactor, 2.0);

        float fogFactor = mix(1.0, distanceFactor, heightFactor);
        fogFactor = clamp(fogFactor, 0.0, 1.0);

        terrainWithFog = mix(fogColor, result, fogFactor);
    } 
    else {
        terrainWithFog = result;
    }

    // ======================================================
    // TONEMAP + GAMMA
    // ======================================================
    vec3 mapped = terrainWithFog / (terrainWithFog + vec3(1.0));
    mapped = pow(mapped, vec3(1.0 / 2.2));

    FragColor = vec4(mapped, 1.0);
}
