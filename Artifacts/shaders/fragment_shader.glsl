#version 400 core
out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec3 ObjColor;
in vec2 TexCoord;
in float viewDepth;

// CONFIG
uniform int shadowFilterMode;
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
// SHADOW — Poisson 32 PCF (sama seperti gltf_fragment)
// ======================================================

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

// 32 uniformly-distributed points on a unit disk (radius², angle)
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


// Poisson 32 PCF with configurable radius (texels)
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

// Hard shadow (single sample)
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
float pcss(sampler2D shadowMap, vec4 fragPosLightSpace, float bias, int sampleCount, float maxRadius)
{
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;
    if (projCoords.z > 1.0 || projCoords.z < 0.0) return 1.0;

    vec2 uv = clamp(projCoords.xy, 0.001, 0.999);
    float zReceiver = projCoords.z - bias;

    float avgBlocker = SearchBlocker(shadowMap, uv, zReceiver, 8.0);
    if (avgBlocker < 0.0) return 1.0;

    float penumbra = (zReceiver - avgBlocker) / max(avgBlocker, 0.0001);
    penumbra = clamp(penumbra * 4.0, 0.0, 8.0);
    float filterRadius = clamp(penumbra * 0.006 * 350.0, 1.5, maxRadius);

    vec2 texel = 1.0 / textureSize(shadowMap, 0);
    float angle = randomAngle(gl_FragCoord.xy) * 6.2831853;
    int n = (sampleCount == 16) ? 16 : 32;

    float shadow = 0.0;
    for (int i = 0; i < n; i++)
    {
        int idx = (i * 3) % 32;
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
float CalculateShadow(vec4 fragPosLightSpace, sampler2D shadowMap, float bias)
{
    if (shadowFilterMode == 1) return hardShadow(fragPosLightSpace, shadowMap, bias);
    if (shadowFilterMode == 2) return poisson16(fragPosLightSpace, shadowMap, bias, 5.0);
    if (shadowFilterMode == 3) return poisson16(fragPosLightSpace, shadowMap, bias, 10.0);
    if (shadowFilterMode == 4) return poisson32(fragPosLightSpace, shadowMap, bias, 5.0);
    if (shadowFilterMode == 5) return poisson32(fragPosLightSpace, shadowMap, bias, 10.0);
    if (shadowFilterMode == 6) return pcss(shadowMap, fragPosLightSpace, bias, 16, 6.0);
    if (shadowFilterMode == 7) return pcss(shadowMap, fragPosLightSpace, bias, 16, 12.0);
    if (shadowFilterMode == 8) return pcss(shadowMap, fragPosLightSpace, bias, 32, 6.0);
    if (shadowFilterMode == 9) return pcss(shadowMap, fragPosLightSpace, bias, 32, 12.0);
    return poisson16(fragPosLightSpace, shadowMap, bias, 5.0); // default mode 0  
}

// ── Horizon fog color (dynamic, matches sky shader) ──
vec3 GetHorizonFogColor(vec3 sunDir)
{
    vec3 sd = normalize(sunDir);
    float sunY = sd.y;
    float sunIntensity = clamp(sunY * 1.5 + 0.5, 0.0, 2.0);

    // Simplified Mie scattering at horizon
    float mu = clamp(dot(vec3(0.0, 0.0, 1.0), sd), -1.0, 1.0);
    float g = 0.76;
    float phaseMie = (1.0 - g*g) / pow(1.0 + g*g - 2.0*g*mu, 1.5);

    vec3 scattering = vec3(1.0, 0.85, 0.65) * phaseMie * sunIntensity * 0.008;

    // Day / dusk / night blend
    float tDay = smoothstep(0.05, 0.30, sunY);
    float tNight = 1.0 - smoothstep(-0.20, 0.05, sunY);
    float tDusk = max(0.0, 1.0 - tDay - tNight);

    vec3 dayColor   = vec3(0.55, 0.72, 0.90);
    vec3 duskColor  = vec3(0.85, 0.40, 0.22);
    vec3 nightColor = vec3(0.02, 0.03, 0.06);

    vec3 color = dayColor * tDay + duskColor * tDusk + nightColor * tNight;
    color += scattering * 0.5;

    return color;
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

    // CSM + CalculateShadow
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
        shadow = CalculateShadow(lightSpaceMatrices[0] * worldPos4, shadowMap0, bias0);
        cascadeIndex = 0;
    }
    else if (depth < cascadeEnds[0]) {
        float t = (depth - (cascadeEnds[0] - blendRange0)) / blendRange0;
        float s0 = CalculateShadow(lightSpaceMatrices[0] * worldPos4, shadowMap0, bias0);
        float s1 = CalculateShadow(lightSpaceMatrices[1] * worldPos4, shadowMap1, bias0);
        shadow = mix(s0, s1, t);
        cascadeIndex = 1;
        cascadeBlendT = t;
    }
    else if (depth < cascadeEnds[1] - blendRange1) {
        shadow = CalculateShadow(lightSpaceMatrices[1] * worldPos4, shadowMap1, bias0);
        cascadeIndex = 1;
    }
    else if (depth < cascadeEnds[1]) {
        float t = (depth - (cascadeEnds[1] - blendRange1)) / blendRange1;
        float s1 = CalculateShadow(lightSpaceMatrices[1] * worldPos4, shadowMap1, bias0);
        float s2 = CalculateShadow(lightSpaceMatrices[2] * worldPos4, shadowMap2, bias0);
        shadow = mix(s1, s2, t);
        cascadeIndex = 2;
        cascadeBlendT = t;
    }
    else {
        shadow = CalculateShadow(lightSpaceMatrices[2] * worldPos4, shadowMap2, bias0);
        cascadeIndex = 2;
    }

    // FIX: shadowMask harus pakai shadowLightDir
    float shadowMask = smoothstep(0.0, 0.20, dot(norm, shadowLightDir));

    vec3 diffuse = finalDiff * activeLightColor * shadowMask * shadow;

    vec3 result;
    if (useTexture == 1) {
        // Textured terrain: per-pixel normal-dependent lighting
        result = (ambient + diffuse) * texColor;
    } else {
        // Vertex-colored: uniform lighting (no normal-dependent shading) with shadows
        float lit = ambientStrength * (1.0 - shadow) + shadow;
        result = lit * activeLightColor * texColor;
    }

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

        vec3 horizonFog = GetHorizonFogColor(realSunDir);
        terrainWithFog = mix(horizonFog, result, fogFactor);
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
