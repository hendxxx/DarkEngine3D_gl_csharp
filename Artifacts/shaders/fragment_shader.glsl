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

// ── LIVE SHADOW TUNING (uploaded from the IDE Shadow Settings panel; the defaults
// match the values that were previously hardcoded here) ──
uniform float u_ConstantBias = 0.00005;   // always-added bias (all surfaces)
uniform float u_SlopeBias = 0.00008;  // slope-scaled bias coefficient (~4 texels steep)
uniform float u_MinBias = 0.00002;    // minimum bias (~2.5 texels flat, light-facing surfaces)
uniform float u_BlendRange = 0.5;   // cascade blend width, fraction of the split distance
uniform vec3 u_DepthRange = vec3(1.0); // world ortho depth range per cascade (zFar - zNear)
uniform vec3 u_TexelWorld = vec3(1.0); // world size of one shadow-map texel per cascade
uniform vec3 u_MaxWorldBias = vec3(0.29, 0.43, 1.5); // per-cascade cap on the bias' WORLD offset (m)

// DEBUG
uniform int showLODColor;

// Cascade overlay strength (L key debug tint), 0..1 — adjustable from the Shadow panel.
uniform float u_CascadeOverlayAlpha = 0.15;
uniform int lodLevel;
uniform int showCSMCascadeColor;
uniform int useFog;

// ── FOG SETTINGS (Config.FogSettings — uploaded from the Inspector "Fog" section) ──
uniform int u_fogMode = 3;            // 1 = Linear, 2 = Exponential, 3 = Exp2 + height blend
uniform float u_fogDensity = 0.0035;  // density (Exp / Exp2)
uniform float u_fogStart = 50.0;      // linear start distance
uniform float u_fogEnd = 300.0;       // linear end distance
uniform float u_fogHeight = 10.0;     // height fog floor (world Y)
uniform float u_fogHeightRange = 45.0;// height fog falloff range

// Shared fog factor (0 = full fog, 1 = no fog).
float calcFogFactor(float dist, vec3 worldPos) {
    if (u_fogMode == 1) { // Linear
        return clamp((u_fogEnd - dist) / max(u_fogEnd - u_fogStart, 0.001), 0.0, 1.0);
    } else if (u_fogMode == 2) { // Exponential
        return exp(-dist * u_fogDensity);
    }
    // Exp2 + height blend (default — matches the original terrain fog)
    float d = exp(-pow(dist * u_fogDensity, 2.0));
    float heightFactor = clamp(1.0 - (worldPos.y - u_fogHeight) / max(u_fogHeightRange, 0.001), 0.0, 1.0);
    heightFactor = pow(heightFactor, 2.0);
    // Height only clears fog at close range; at the horizon fog always applies.
    float heightWeight = mix(heightFactor, 1.0, 1.0 - d);
    return clamp(mix(1.0, d, heightWeight), 0.0, 1.0);
}

uniform vec3 realSunDir;   // arah matahari asli dari CPU
uniform vec3 shadowDir;    // arah shadow (sun/moon blend)

// ── LOCAL LIGHTS (Point / Spot from editor Light markers) ──
// The sun (Direct) stays the global directional light; every Point/Spot marker
// adds its own colored light with distance falloff + spot cone.
#define MAX_LOCAL_LIGHTS 8
uniform int u_lightCount;
uniform int u_lightType[MAX_LOCAL_LIGHTS];     // 1 = Point, 2 = Spotlight
uniform vec3 u_lightPos[MAX_LOCAL_LIGHTS];
uniform vec3 u_lightDir[MAX_LOCAL_LIGHTS];     // spot: direction the light points toward
uniform vec3 u_lightColor[MAX_LOCAL_LIGHTS];
uniform float u_lightIntensity[MAX_LOCAL_LIGHTS];
uniform float u_lightRange[MAX_LOCAL_LIGHTS];
uniform vec2 u_lightCone[MAX_LOCAL_LIGHTS];    // x = cos(outer), y = cos(inner)

// ── LOCAL LIGHT SHADOWS (per-light shadow maps for Point/Spot) ──
// Spot maps are 2D depth textures on units 10..13, point maps are cube depth maps on
// units 14..15 (unit 9 is skipped: the PBR object shader binds its CSM cascade 2 there).
// u_localShadowSpotIdx/PointIdx[i] = shadow slot for local light i
// (-1 = this light casts no shadow). u_localLightSpace[i] is the spot light's
// view-projection; point shadows compare depth along the fragment→light direction.
uniform sampler2D u_localShadowSpot[4];
uniform samplerCube u_localShadowPoint[3];
uniform mat4 u_localLightSpace[MAX_LOCAL_LIGHTS];
uniform int u_localShadowSpotIdx[MAX_LOCAL_LIGHTS];
uniform int u_localShadowPointIdx[MAX_LOCAL_LIGHTS];
uniform float u_localShadowFar[MAX_LOCAL_LIGHTS];
uniform float u_localShadowBias = 0.004;
uniform float u_localShadowPointBias = 0.02;

// Accumulate diffuse + blinn-phong specular from all local lights.
vec3 calcLocalLights(vec3 N, vec3 V) {
    vec3 acc = vec3(0.0);
    for (int i = 0; i < MAX_LOCAL_LIGHTS; i++) {
        if (i >= u_lightCount) break;
        vec3 L;
        float attenuation = 1.0;
        if (u_lightType[i] == 1) { // Point
            vec3 toLight = u_lightPos[i] - FragPos;
            float dist = length(toLight);
            L = toLight / max(dist, 0.0001);
            float range = max(u_lightRange[i], 0.1);
            float d = dist / range;
            attenuation = clamp(1.0 - d * d, 0.0, 1.0);
            attenuation *= attenuation;
        } else if (u_lightType[i] == 2) { // Spotlight
            vec3 toLight = u_lightPos[i] - FragPos;
            float dist = length(toLight);
            L = toLight / max(dist, 0.0001);
            float range = max(u_lightRange[i], 0.1);
            float d = dist / range;
            attenuation = clamp(1.0 - d * d, 0.0, 1.0);
            attenuation *= attenuation;
            // Cone: -L points from the light toward the fragment; compare with the
            // light's aim direction (u_lightDir) via the cosine of the half-angle.
            float theta = dot(-L, normalize(u_lightDir[i]));
            float cosOuter = u_lightCone[i].x;
            float cosInner = u_lightCone[i].y;
            float epsilon = max(cosInner - cosOuter, 0.001);
            attenuation *= clamp((theta - cosOuter) / epsilon, 0.0, 1.0);
        } else {
            continue; // Direct lights drive the sun, not the local set
        }
        if (attenuation <= 0.001) continue;
        float diff = max(dot(N, L), 0.0);
        if (diff <= 0.0) continue;
        float shadowFactor = 1.0;
        if (u_lightType[i] == 1) { // Point light shadow (cube map, linear depth compare)
            int ci = u_localShadowPointIdx[i];
            if (ci >= 0) {
                vec3 toLight = FragPos - u_lightPos[i];
                float dist = max(length(toLight), 1e-5);
                vec3 dir = toLight / dist;
                float far = max(u_localShadowFar[i], 0.1);
                // The cube stores the perspective depth (near 0.05, far = range, remapped
                // by the GL depth range); invert it back to a linear axis depth so the
                // bias below is in constant world units.
                float s = texture(u_localShadowPoint[ci], dir).r;
                float zD3D = clamp(s * 2.0 - 1.0, 0.0, 1.0);
                float zOcc = 0.05 / max(1.0 - zD3D * ((far - 0.05) / far), 1e-5);
                float ad = max(abs(dir.x), max(abs(dir.y), abs(dir.z))) * dist;
                float bias = max(u_localShadowPointBias * (1.0 - dot(N, L)), u_localShadowPointBias * 0.1);
                if (zOcc < ad - bias) shadowFactor = 0.0;
            }
        } else if (u_lightType[i] == 2) { // Spotlight shadow (projective map, linear depth compare)
            int si = u_localShadowSpotIdx[i];
            if (si >= 0) {
                vec4 clip = u_localLightSpace[i] * vec4(FragPos, 1.0);
                vec3 ndc = clip.xyz / clip.w;
                if (ndc.z > 0.0 && ndc.z < 1.0) {
                    vec2 uv = ndc.xy * 0.5 + 0.5;
                    float far = max(u_localShadowFar[i], 0.1);
                    // Linear view depth of the fragment (clip.w = -view z for the RH
                    // projection) vs. the occluder (inverted from the stored depth).
                    float zFrag = clip.w;
                    float s = texture(u_localShadowSpot[si], uv).r;
                    float zD3D = clamp(s * 2.0 - 1.0, 0.0, 1.0);
                    float zOcc = 0.05 / max(1.0 - zD3D * ((far - 0.05) / far), 1e-5);
                    float bias = max(u_localShadowBias * (1.0 - dot(N, L)), u_localShadowBias * 0.1);
                    if (zOcc < zFrag - bias) shadowFactor = 0.0;
                }
            }
        }
        vec3 H = normalize(L + V);
        float spec = pow(max(dot(N, H), 0.0), 24.0) * 0.6;
        acc += (diff + spec) * u_lightColor[i] * u_lightIntensity[i] * attenuation * shadowFactor;
    }
    return acc;
}

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

    float blendRange0 = cascadeEnds[0] * u_BlendRange;
    float blendRange1 = cascadeEnds[1] * u_BlendRange;

    // FIX: gunakan arah bulan saat malam
    //vec3 shadowLightDir = (nightBlendFactor > 0.5) ? moonDir : sunDir; 

    // FIX: bias harus pakai shadowLightDir 
    float ndotl = max(dot(norm, shadowLightDir), 0.0);
    // Slope-scaled bias: steep surfaces (ndotl → 0) get a much larger bias so they don't
    // show acne; flat light-facing surfaces stay tight. Raised from 0.0005 to kill the
    // speckling that appeared on sloped terrain and detailed geometry.
    float baseBias = max(u_ConstantBias + u_SlopeBias * (1.0 - ndotl), u_MinBias);
    float bias0 = baseBias;
    // Far cascades cover much more world space per shadow-map texel, so they need a
    // proportionally larger bias to stay acne-free (same 1.5×/3× scaling the gltf shader
    // already applies for its cascade 1/2). The depth-range term keeps the WORLD offset
    // consistent across cascades: worldOffset = bias_ndc × depthRange, and a far cascade's
    // ortho Z range is 10×+ larger than the near one's — a fixed NDC bias there would push
    // shadows tens of world units away and they would vanish at distance.
    // World bias offset proportional to the LOCAL texel size (texel_i / texel_0): a constant
    // texel count at every distance, so far cascades get proportionally more bias instead of
    // the old fixed 1.5×/3× (which under-shot far cascades → sub-texel acne). The depth-range
    // term converts that world offset into NDC; the ratio is clamped so a pathological small
    // cascade-0 range can never explode the far-cascade bias.
    float bias1 = baseBias * max(u_TexelWorld.y / max(u_TexelWorld.x, 1e-5), 1.0)
                * clamp(u_DepthRange.x / u_DepthRange.y, 0.02, 4.0);
    float bias2 = baseBias * max(u_TexelWorld.z / max(u_TexelWorld.x, 1e-5), 1.0)
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

    float shadow;
    int cascadeIndex = 0;
    float cascadeBlendT = 0.0;

    vec4 worldPos4 = vec4(FragPos, 1.0);

    if (depth < cascadeEnds[0] - blendRange0) {
        shadow = CalculateShadow(lightSpaceMatrices[0] * worldPos4, shadowMap0, bias0, 1.0);
        cascadeIndex = 0;
    }
    else if (depth < cascadeEnds[0]) {
        float t = (depth - (cascadeEnds[0] - blendRange0)) / blendRange0;
        float s0 = CalculateShadow(lightSpaceMatrices[0] * worldPos4, shadowMap0, bias0, 1.0);
        float s1 = CalculateShadow(lightSpaceMatrices[1] * worldPos4, shadowMap1, bias1, rScale1);
        shadow = mix(s0, s1, t);
        cascadeIndex = 1;
        cascadeBlendT = t;
    }
    else if (depth < cascadeEnds[1] - blendRange1) {
        shadow = CalculateShadow(lightSpaceMatrices[1] * worldPos4, shadowMap1, bias1, rScale1);
        cascadeIndex = 1;
    }
    else if (depth < cascadeEnds[1]) {
        float t = (depth - (cascadeEnds[1] - blendRange1)) / blendRange1;
        float s1 = CalculateShadow(lightSpaceMatrices[1] * worldPos4, shadowMap1, bias1, rScale1);
        float s2 = CalculateShadow(lightSpaceMatrices[2] * worldPos4, shadowMap2, bias2, rScale2);
        shadow = mix(s1, s2, t);
        cascadeIndex = 2;
        cascadeBlendT = t;
    }
    else {
        shadow = CalculateShadow(lightSpaceMatrices[2] * worldPos4, shadowMap2, bias2, rScale2);
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
        // Vertex-colored: flat shading with shadows. The surface's facing toward the
        // light (NdotL) still gates the lit term, so faces that point away from the
        // light keep only the ambient floor even where the shadow map can't reach
        // them — e.g. the thin bright rim the CSM bias leaves at an object's base
        // when the sun is at grazing / below-horizon angles (the shadow map's bias
        // keeps the self-shadow just short of the silhouette, and without the facing
        // term that strip would glow with the full light color).
        float ndl = max(dot(norm, activeLightDir), 0.0);
        float lit = ambientStrength + shadow * max(ndl - ambientStrength, 0.0);
        result = lit * activeLightColor * texColor;
    }

    // ── LOCAL LIGHTS (Point / Spot) — added on top of the sun lighting, tinted by
    //    the object's own albedo so every object keeps its color. ──
    vec3 localLighting = calcLocalLights(norm, normalize(viewPos - FragPos));
    result += localLighting * texColor;

    // DEBUG CSM COLOR
    if (showCSMCascadeColor == 1) {
        // High-contrast palette (cyan / yellow / magenta) — distinct from the scene's
        // green/brown terrain and sky, so the cascade bands pop even at 15% overlay.
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

        // Transparent overlay — strength from u_CascadeOverlayAlpha (panel-adjustable)
        // so the scene stays readable while the cascade bands are still distinguishable.
        result = mix(result, cColor, u_CascadeOverlayAlpha);
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
        float fogFactor = calcFogFactor(dist, FragPos);
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
