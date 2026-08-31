#version 400 core
out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec3 ObjColor;
in vec2 TexCoord;
in float viewDepth;

// ── CONFIG (set per terrain plane) ──
uniform vec3 heightScale;      // .x = world height range used to normalize FragPos.y
uniform vec3 sunDir, lightColor, viewPos, fogColor;
uniform int useFog;

// ── DYNAMIC LAYER SYSTEM ──
#define MAX_LAYERS 8
uniform int layerCount;        // actual number of active layers (1..8)
uniform vec2 layerTiling[MAX_LAYERS];    // per-layer (tilingX, tilingY)
uniform vec2 layerHeightRange[MAX_LAYERS]; // per-layer (heightMin, heightMax)
uniform float layerBlendSharpness[MAX_LAYERS]; // per-layer blend sharpness

// ── LAYER ALBEDO SAMPLERS ──
uniform sampler2D dynLayer0, dynLayer1, dynLayer2, dynLayer3;
uniform sampler2D dynLayer4, dynLayer5, dynLayer6, dynLayer7;

// ── SLOPE LAYER (optional) ──
uniform int slopeEnabled;      // 0 = no slope, 1 = slope layer active
uniform float slopeThreshold;  // steepness (1 - n.y) above which slope layer takes over
uniform float slopeTilingVal;  // slope layer tiling
uniform int slopeStochastic;    // slope layer random tile
uniform sampler2D dynSlopeTex;

// ── TERRAIN PBR (adjustable from Inspector) ──
uniform float terrainMetallic = 0.0;
uniform float terrainRoughness = 0.5;

// ── Height detail strength (0 = off, 0.5 = subtle, 1.0 = strong) ──
uniform float parallaxScale = 0.0;

// Legacy compat — map old uniforms
uniform vec4 layerLevels;      // kept for old scenes, new system uses layerHeightRange

// ── FOG SETTINGS (Config.FogSettings — uploaded from the Inspector "Fog" section) ──
uniform int u_fogMode = 3;            // 1 = Linear, 2 = Exponential, 3 = Exp2 + height blend
uniform float u_fogDensity = 0.0035;
uniform float u_fogStart = 50.0;
uniform float u_fogEnd = 300.0;
uniform float u_fogHeight = 10.0;
uniform float u_fogHeightRange = 45.0;

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
    float heightWeight = mix(heightFactor, 1.0, 1.0 - d);
    return clamp(mix(1.0, d, heightWeight), 0.0, 1.0);
}

uniform sampler2D tex4;      // splat/control map: RGBA weights for up to 4 layers (manual paint)
uniform int usePaintMask;    // 1 = the terrain has manual layer paint to apply
uniform int showHeatmap;     // 1 = height heatmap + contour overlay (editor clarity)
uniform int showContours;    // 1 = dark height contour lines only (no heatmap colors)

// ── INDEPENDENT PAINT LAYER TEXTURES (separate from terrain auto-layers) ──
uniform sampler2D paintTex0, paintTex1, paintTex2, paintTex3;
uniform vec2 paintTiling[4];   // per-paint-layer tiling (X, Y)
uniform int paintTexCount;     // how many paint layers have textures assigned (0..4)
uniform int paintLayerCount;   // how many paint layers are active (1..4)
uniform int paintStochastic[4]; // per-paint-layer random tile flag

// ── CSM SHADOWS (editor viewport — same uniforms as fragment_shader.glsl) ──
uniform int shadowFilterMode;
uniform sampler2D shadowMap0;
uniform sampler2D shadowMap1;
uniform sampler2D shadowMap2;
uniform mat4 lightSpaceMatrices[3];
uniform float cascadeEnds[3];
uniform vec3 shadowDir;

// ── LIVE SHADOW TUNING (uploaded from the IDE Shadow Settings panel; the defaults
// match the values that were previously hardcoded here) ──
uniform float u_ConstantBias = 0.00005;   // always-added bias (all surfaces)
uniform float u_SlopeBias = 0.00005;  // slope-scaled bias coefficient (Tutorial 16: bias ∝ tan(acos(N·L)))
uniform float u_MinBias = 0.00001;    // minimum bias (~2.5 texels flat, light-facing surfaces)
uniform float u_BlendRange = 1.0;   // cascade blend width, fraction of the split distance
uniform vec3 u_DepthRange = vec3(1.0); // world ortho depth range per cascade (zFar - zNear)
// World size of one shadow-map texel per cascade. CPU MUST upload these two uniforms
// (ShadowUniforms.UploadCascadeScales) — the vec3(1.0) defaults are only a fallback.
uniform vec3 u_TexelWorld = vec3(1.0);
uniform vec3 u_MaxWorldBias = vec3(50.5, 150.5, 350.5); // per-cascade cap on the bias' WORLD offset (m)

// Debug overlay (L key): tint each cascade with a transparent color code.
uniform int showCSMCascadeColor;

// Cascade overlay strength (L key debug tint), 0..1 — adjustable from the Shadow panel.
uniform float u_CascadeOverlayAlpha = 0.15;

// ── LOCAL LIGHTS (Point / Spot from editor Light markers) ──
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
uniform sampler2D u_localShadowSpot[4];
uniform samplerCube u_localShadowPoint[3];
uniform mat4 u_localLightSpace[MAX_LOCAL_LIGHTS];
uniform int u_localShadowSpotIdx[MAX_LOCAL_LIGHTS];
uniform int u_localShadowPointIdx[MAX_LOCAL_LIGHTS];
uniform float u_localShadowFar[MAX_LOCAL_LIGHTS];
uniform float u_localShadowBias = 0.004;
uniform float u_localShadowPointBias = 0.02;

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
// NOISE & STOCHASTIC SAMPLING
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

// Per-layer stochastic sampling: 1 = randomized per-tile, 0 = plain sampling.
uniform int layerStochastic[MAX_LAYERS];

vec3 sampleLayer(sampler2D tex, vec2 uv, int stochastic) {
    if (stochastic == 0) {
        return texture(tex, uv).rgb;
    }
    // Fast 2x2 stochastic: 4 samples instead of 9 — 3× cheaper
    vec2 p = floor(uv);
    vec2 f = fract(uv);
    vec3 res = vec3(0.0);
    float w00 = (1.0 - f.x) * (1.0 - f.y);
    float w10 = f.x * (1.0 - f.y);
    float w01 = (1.0 - f.x) * f.y;
    float w11 = f.x * f.y;
    res += texture(tex, uv + hash2(p) - 0.5).rgb * w00;
    res += texture(tex, uv + hash2(p + vec2(1, 0)) - 0.5).rgb * w10;
    res += texture(tex, uv + hash2(p + vec2(0, 1)) - 0.5).rgb * w01;
    res += texture(tex, uv + hash2(p + vec2(1, 1)) - 0.5).rgb * w11;
    return res;
}

// ── Height-based normal detail (from albedo luminance proxy) ──
// Adds fine surface relief without extra textures. strength = 0..1.
vec3 heightNormalDetail(sampler2D tex, vec2 uv, float tiling, vec3 geoNormal, float strength) {
    if (strength <= 0.0) return geoNormal;
    float texelSize = 1.0 / (textureSize(tex, 0).x * tiling);
    float s = texelSize * 2.0;
    vec3 c = texture(tex, uv).rgb;
    float h  = dot(c, vec3(0.299, 0.587, 0.114));
    float hx = dot(texture(tex, uv + vec2(s, 0.0)).rgb, vec3(0.299, 0.587, 0.114));
    float hz = dot(texture(tex, uv + vec2(0.0, s)).rgb, vec3(0.299, 0.587, 0.114));
    vec3 tangentDetail = vec3((hx - h) * strength * 4.0, 0.0, (hz - h) * strength * 4.0);
    return normalize(geoNormal + tangentDetail);
}

// Dark contour-line factor at normalized height t: 1 = exactly on a line (every 10%
// height), 0 = between lines. Shared by the heatmap and the contour-only overlays so
// the interval/width never drift apart.
float contourFactor(float t) {
    float band = fract(clamp(t, 0.0, 1.0) * 10.0);
    return 1.0 - smoothstep(0.0, 0.06, min(band, 1.0 - band));
}

vec3 triplanarLayer(sampler2D tex, vec3 worldPos, vec3 normal, float tiling, int stochastic) {
    vec3 blending = abs(normalize(normal));
    blending = pow(blending, vec3(10.0));
    blending /= (blending.x + blending.y + blending.z);

    vec3 xTex = sampleLayer(tex, worldPos.zy * tiling, stochastic);
    vec3 yTex = sampleLayer(tex, worldPos.xz * tiling, stochastic);
    vec3 zTex = sampleLayer(tex, worldPos.xy * tiling, stochastic);

    return xTex * blending.x + yTex * blending.y + zTex * blending.z;
}

// ======================================================
// SHADOW — Poisson PCF (same math as fragment_shader.glsl)
// ======================================================
float randomAngle(vec2 uv) {
    return fract(sin(dot(uv, vec2(12.9898, 78.233))) * 43758.5453);
}

const vec2 poissonDisk16[16] = vec2[](
    vec2(0.0152, 2.9841), vec2(0.0541, 0.5123), vec2(0.1128, 4.2219), vec2(0.1894, 1.3347),
    vec2(0.2817, 5.6128), vec2(0.3869, 3.0471), vec2(0.5018, 0.1029), vec2(0.6234, 3.8762),
    vec2(0.7481, 1.9024), vec2(0.8723, 5.1487), vec2(0.9512, 2.4129), vec2(0.9941, 0.3451),
    vec2(0.8203, 4.7892), vec2(0.6904, 0.9821), vec2(0.5609, 2.7418), vec2(0.4302, 5.4983)
);

float poisson16(vec4 fragPosLightSpace, sampler2D shadowMap, float bias, float radius)
{
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;
    if (projCoords.z > 1.0 || projCoords.z < 0.0) return 1.0;
    vec2 uv = clamp(projCoords.xy, 0.001, 0.999);

    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);
    float angle = randomAngle(gl_FragCoord.xy) * 6.2831853;
    float shadow = 0.0;
    for (int i = 0; i < 16; ++i)
    {
        float r = sqrt(poissonDisk16[i].x) * radius;
        float a = poissonDisk16[i].y + angle;
        vec2 offset = vec2(r * cos(a), r * sin(a));
        float d = texture(shadowMap, uv + offset * texelSize).r;
        shadow += (projCoords.z - bias > d) ? 0.0 : 1.0;
    }
    return shadow / 16.0;
}

float hardShadow(vec4 fragPosLightSpace, sampler2D shadowMap, float bias)
{
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;
    if (projCoords.z > 1.0 || projCoords.z < 0.0) return 1.0;
    vec2 uv = clamp(projCoords.xy, 0.001, 0.999);
    float d = texture(shadowMap, uv).r;
    return (projCoords.z - bias > d) ? 0.0 : 1.0;
}

float CalculateShadow(vec4 fragPosLightSpace, sampler2D shadowMap, float bias, float radiusScale)
{
    if (shadowFilterMode == 1) return hardShadow(fragPosLightSpace, shadowMap, bias);
    if (shadowFilterMode == 3) return poisson16(fragPosLightSpace, shadowMap, bias, 10.0 * radiusScale);
    return poisson16(fragPosLightSpace, shadowMap, bias, 5.0 * radiusScale); // mode 0/2 default PCF16
}

// ======================================================
// MAIN
// ======================================================
vec3 sampleDynLayer(int idx, vec2 worldXZ, vec3 norm, vec2 tiling, int stochastic) {
    vec3 blending = abs(normalize(norm));
    blending = pow(blending, vec3(10.0));
    blending /= (blending.x + blending.y + blending.z);

    vec3 col = vec3(0.0);
    if (idx == 0) {
        col = sampleLayer(dynLayer0, FragPos.zy * tiling, stochastic) * blending.x
            + sampleLayer(dynLayer0, FragPos.xz * tiling, stochastic) * blending.y
            + sampleLayer(dynLayer0, FragPos.xy * tiling, stochastic) * blending.z;
    } else if (idx == 1) {
        col = sampleLayer(dynLayer1, FragPos.zy * tiling, stochastic) * blending.x
            + sampleLayer(dynLayer1, FragPos.xz * tiling, stochastic) * blending.y
            + sampleLayer(dynLayer1, FragPos.xy * tiling, stochastic) * blending.z;
    } else if (idx == 2) {
        col = sampleLayer(dynLayer2, FragPos.zy * tiling, stochastic) * blending.x
            + sampleLayer(dynLayer2, FragPos.xz * tiling, stochastic) * blending.y
            + sampleLayer(dynLayer2, FragPos.xy * tiling, stochastic) * blending.z;
    } else if (idx == 3) {
        col = sampleLayer(dynLayer3, FragPos.zy * tiling, stochastic) * blending.x
            + sampleLayer(dynLayer3, FragPos.xz * tiling, stochastic) * blending.y
            + sampleLayer(dynLayer3, FragPos.xy * tiling, stochastic) * blending.z;
    } else if (idx == 4) {
        col = sampleLayer(dynLayer4, FragPos.zy * tiling, stochastic) * blending.x
            + sampleLayer(dynLayer4, FragPos.xz * tiling, stochastic) * blending.y
            + sampleLayer(dynLayer4, FragPos.xy * tiling, stochastic) * blending.z;
    } else if (idx == 5) {
        col = sampleLayer(dynLayer5, FragPos.zy * tiling, stochastic) * blending.x
            + sampleLayer(dynLayer5, FragPos.xz * tiling, stochastic) * blending.y
            + sampleLayer(dynLayer5, FragPos.xy * tiling, stochastic) * blending.z;
    } else if (idx == 6) {
        col = sampleLayer(dynLayer6, FragPos.zy * tiling, stochastic) * blending.x
            + sampleLayer(dynLayer6, FragPos.xz * tiling, stochastic) * blending.y
            + sampleLayer(dynLayer6, FragPos.xy * tiling, stochastic) * blending.z;
    } else {
        col = sampleLayer(dynLayer7, FragPos.zy * tiling, stochastic) * blending.x
            + sampleLayer(dynLayer7, FragPos.xz * tiling, stochastic) * blending.y
            + sampleLayer(dynLayer7, FragPos.xy * tiling, stochastic) * blending.z;
    }
    return col;
}

// Triplanar sample for independent paint textures (paintTex0..3)
vec3 samplePaintLayer(int idx, vec3 worldPos, vec3 norm, vec2 tiling, int stochastic) {
    vec3 blending = abs(normalize(norm));
    blending = pow(blending, vec3(10.0));
    blending /= (blending.x + blending.y + blending.z);
    vec3 col = vec3(0.0);
    if (idx == 0) {
        col = sampleLayer(paintTex0, worldPos.zy * tiling, stochastic) * blending.x
            + sampleLayer(paintTex0, worldPos.xz * tiling, stochastic) * blending.y
            + sampleLayer(paintTex0, worldPos.xy * tiling, stochastic) * blending.z;
    } else if (idx == 1) {
        col = sampleLayer(paintTex1, worldPos.zy * tiling, stochastic) * blending.x
            + sampleLayer(paintTex1, worldPos.xz * tiling, stochastic) * blending.y
            + sampleLayer(paintTex1, worldPos.xy * tiling, stochastic) * blending.z;
    } else if (idx == 2) {
        col = sampleLayer(paintTex2, worldPos.zy * tiling, stochastic) * blending.x
            + sampleLayer(paintTex2, worldPos.xz * tiling, stochastic) * blending.y
            + sampleLayer(paintTex2, worldPos.xy * tiling, stochastic) * blending.z;
    } else {
        col = sampleLayer(paintTex3, worldPos.zy * tiling, stochastic) * blending.x
            + sampleLayer(paintTex3, worldPos.xz * tiling, stochastic) * blending.y
            + sampleLayer(paintTex3, worldPos.xy * tiling, stochastic) * blending.z;
    }
    return col;
}

// ── PBR BRDF (Cook-Torrance, hardcoded defaults — no new uniforms needed) ──
const float PI = 3.14159265359;
vec3 pbrFresnel(float cosTheta, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}
float pbrDGGX(vec3 N, vec3 H, float r) {
    float a = r * r; float a2 = a * a;
    float d = dot(N, H); float d2 = d * d;
    return a2 / max(PI * (d2 * (a2 - 1.0) + 1.0) * (d2 * (a2 - 1.0) + 1.0), 0.0001);
}
float pbrGSmith(vec3 N, vec3 V, vec3 L, float r) {
    float k = (r + 1.0) * (r + 1.0) / 8.0;
    float nv = max(dot(N, V), 0.0); float nl = max(dot(N, L), 0.0);
    return (nv / (nv * (1.0 - k) + k)) * (nl / (nl * (1.0 - k) + k));
}

void main() {
    vec3 norm = normalize(Normal);
    float slope = 1.0 - norm.y;

    float h = FragPos.y / max(heightScale.x, 1.0);
    float noise = smoothNoise(FragPos.xz * 0.2) * 0.08;
    float hn = clamp(h + noise, 0.0, 1.0);

    // ── DYNAMIC LAYER HEIGHT BLENDING ──
    // Build layer weights: each layer contributes where hn is within its [HeightMin, HeightMax].
    float weights[MAX_LAYERS];
    vec3 layerColors[MAX_LAYERS];
    float totalWeight = 0.0;

    for (int i = 0; i < MAX_LAYERS; i++) {
        weights[i] = 0.0;
        layerColors[i] = vec3(0.0);
    }

    for (int i = 0; i < layerCount; i++) {
        vec2 hr = layerHeightRange[i];
        float sharp = max(layerBlendSharpness[i], 0.5);
        float rangeWidth = max(hr.y - hr.x, 0.001);
        float blendWidth = rangeWidth * 0.5 / sharp;

        // Smooth bell-curve weight: ramp up at HeightMin, full in center, ramp down at HeightMax
        float rampUp = smoothstep(hr.x - blendWidth, hr.x + blendWidth, hn);
        float rampDn = smoothstep(hr.y + blendWidth, hr.y - blendWidth, hn);
        float w = rampUp * rampDn;

        weights[i] = max(w, 0.0);
        totalWeight += weights[i];
    }

    // Normalize weights
    if (totalWeight > 0.001) {
        for (int i = 0; i < layerCount; i++) {
            weights[i] /= totalWeight;
        }
    }

    // Sample all layers and blend
    vec3 texColor = vec3(0.0);
    for (int i = 0; i < layerCount; i++) {
        if (weights[i] < 0.001) continue;
        vec2 tiling = layerTiling[i];
        vec3 lc = sampleDynLayer(i, FragPos.xz, norm, tiling, layerStochastic[i]);
        texColor += lc * weights[i];
    }
    // Fallback: if no layers rendered, use a default gray
    if (layerCount == 0) texColor = vec3(0.5);

    // ── SLOPE OVERRIDE ──
    if (slopeEnabled == 1) {
        float cliffMask = smoothstep(slopeThreshold - 0.1, slopeThreshold + 0.12, slope + noise * 0.1);
        vec3 slopeColor = triplanarLayer(dynSlopeTex, FragPos, norm, slopeTilingVal, slopeStochastic);
        texColor = mix(texColor, slopeColor, cliffMask * 0.85);
    }

    // ── MANUAL LAYER PAINT (splat override, painted with the brush) ──
    // Uses independent paint textures (paintTex0..3) — NOT the terrain auto-layers.
    if (usePaintMask == 1) {
        vec4 w = texture(tex4, TexCoord + 0.5).rgba;
        float wsum = w.x + w.y + w.z + w.w;
        if (wsum > 0.02) {
            vec4 w2 = w * w;
            float t = w2.x + w2.y + w2.z + w2.w;
            if (t > 0.001) {
                // Sample independent paint textures with per-layer tiling + stochastic
                vec3 painted = vec3(0.0);
                int pCount = min(paintLayerCount, paintTexCount);
                if (pCount > 0) painted += samplePaintLayer(0, FragPos, norm, paintTiling[0], paintStochastic[0]) * w2.x;
                if (pCount > 1) painted += samplePaintLayer(1, FragPos, norm, paintTiling[1], paintStochastic[1]) * w2.y;
                if (pCount > 2) painted += samplePaintLayer(2, FragPos, norm, paintTiling[2], paintStochastic[2]) * w2.z;
                if (pCount > 3) painted += samplePaintLayer(3, FragPos, norm, paintTiling[3], paintStochastic[3]) * w2.w;
                painted /= t;
                texColor = mix(texColor, painted, clamp(wsum * 1.5, 0.0, 1.0));
            }
        }
    }

    // ── HEIGHT SHADING OVERLAY (editor clarity) ──
    // Optional heatmap: low = blue … high = red with contour lines every 10% height,
    // blended over the texture. It is lit by the sun below, so it still reads as relief.
    if (showHeatmap == 1) {
        // Use the pure (un-noised) height so the ramp and contour lines track the
        // actual terrain level instead of wobbling with the texture noise.
        float t = clamp(h, 0.0, 1.0);
        vec3 c0 = vec3(0.05, 0.10, 0.55);
        vec3 c1 = vec3(0.05, 0.60, 0.85);
        vec3 c2 = vec3(0.10, 0.80, 0.35);
        vec3 c3 = vec3(0.95, 0.85, 0.15);
        vec3 c4 = vec3(0.85, 0.15, 0.10);
        vec3 hm;
        if (t < 0.25) hm = mix(c0, c1, t / 0.25);
        else if (t < 0.50) hm = mix(c1, c2, (t - 0.25) / 0.25);
        else if (t < 0.75) hm = mix(c2, c3, (t - 0.50) / 0.25);
        else hm = mix(c3, c4, (t - 0.75) / 0.25);
        texColor = mix(texColor, hm, 0.82);

        // Contour lines every 10% of the normalized height range (darker on top).
        texColor *= (1.0 - contourFactor(t) * 0.55);
    }

    // ── HEIGHT CONTOURS ONLY (editor clarity, no heatmap colors) ──
    // Dark topographic lines every 10% of the normalized height range — the texture
    // stays fully visible while the relief reads clearly (skipped when the heatmap is
    // already drawing its own contours, to avoid double-darkening).
    if (showContours == 1 && showHeatmap == 0) {
        texColor *= (1.0 - contourFactor(h) * 0.55);
    }

    // ── SUN HILLSHADE (directional shading by sun position) ──
    // A crisp terminator + slope darkening makes the terrain relief read instantly:
    // faces toward the sun are bright, faces away fall into shade, and steep cliffs
    // darken further — so the high/low structure is visible from any camera angle.
    // ── HEIGHT NORMAL DETAIL ──
    // Perturb the geometry normal using the primary layer's albedo luminance
    // gradients — creates fine surface relief without extra textures.
    if (parallaxScale > 0.0 && layerCount > 0) {
        vec2 primaryTiling = layerTiling[0];
        norm = heightNormalDetail(dynLayer0, FragPos.xz * primaryTiling.x, 1.0, norm, parallaxScale);
    }

    // PBR sun lighting (terrainMetallic, terrainRoughness from Inspector)
    vec3 lightDir = normalize(sunDir);
    vec3 V = normalize(viewPos - FragPos);
    vec3 H = normalize(V + lightDir);
    float tMet = clamp(terrainMetallic, 0.0, 1.0);
    float tRou = clamp(terrainRoughness, 0.04, 1.0);
    vec3 F0 = mix(vec3(0.04), texColor, tMet);
    float NdotL = max(dot(norm, lightDir), 0.0);
    float NdotV = max(dot(norm, V), 0.001);
    float D = pbrDGGX(norm, H, tRou);
    float G = pbrGSmith(norm, V, lightDir, tRou);
    vec3 F = pbrFresnel(max(dot(H, V), 0.0), F0);
    vec3 spec = D * G * F / max(4.0 * NdotV * NdotL, 0.0001);
    vec3 kD = (vec3(1.0) - F) * (1.0 - tMet);
    vec3 sunPbr = (kD * texColor / PI + spec) * lightColor * 4.0 * NdotL;
    float slopeShade = 1.0 - clamp(slope * 0.55, 0.0, 0.5);
    vec3 ambient = texColor * lightColor * 0.14 * slopeShade;

    // ── CSM SHADOWS (editor viewport) ──
    // Slope-scaled bias (same as fragment_shader.glsl) so the terrain doesn't show
    // acne on gently-sloped faces while keeping shadows tight on flat ground.
    vec3 shadowLightDir = normalize(shadowDir);
    float ndotl = max(dot(norm, shadowLightDir), 0.0);
    // Slope-scaled bias (OpenGL Tutorial 16, same as fragment_shader.glsl): bias ∝
    // tan(acos(N·L)) — grows far faster than the old linear (1−N·L) on slopes turning
    // away from the light, which is what kills self-shadow acne on heightmapped terrain.
    // tan(acos(x)) = sqrt(1−x²)/x, denominator clamped so N·L = 0 can't divide by zero.
    float ndotlSafe = max(ndotl, 0.05);
    float slopeFactor = sqrt(max(1.0 - ndotlSafe * ndotlSafe, 0.0)) / ndotlSafe;
    float bias0 = max(u_ConstantBias + u_SlopeBias * slopeFactor, u_MinBias);
    float bias1 = bias0;
    float bias2 = bias0;
    vec4 worldPos4 = vec4(FragPos, 1.0);
    float depth = viewDepth;
    // Same blend width for ALL transitions — identical look between every cascade pair.
    float blendW = cascadeEnds[0] * 0.5;
    float blendRange0 = blendW;
    float blendRange1 = blendW;
    float rScale1 = 1.0;
    float rScale2 = 1.0;

    int cascadeIndex = 0;
    float cascadeBlendT = 0.0;
    float shadow;
    if (depth < cascadeEnds[0] - blendRange0) {
        shadow = CalculateShadow(lightSpaceMatrices[0] * worldPos4, shadowMap0, bias0, 1.0);
        cascadeIndex = 0;
    }
    else if (depth < cascadeEnds[0]) {
        float t = smoothstep(cascadeEnds[0] - blendRange0, cascadeEnds[0], depth);
        float s0 = CalculateShadow(lightSpaceMatrices[0] * worldPos4, shadowMap0, bias0, 1.0);
        float s1 = CalculateShadow(lightSpaceMatrices[1] * worldPos4, shadowMap1, bias1, 1.0);
        shadow = mix(s0, s1, t);
        cascadeIndex = 1;
        cascadeBlendT = t;
    }
    else if (depth < cascadeEnds[1] - blendRange1) {
        shadow = CalculateShadow(lightSpaceMatrices[1] * worldPos4, shadowMap1, bias1, 1.0);
        cascadeIndex = 1;
    }
    else if (depth < cascadeEnds[1]) {
        // Blend: keep cascade 1 shadow, fade to fully lit at the boundary.
        float t = smoothstep(cascadeEnds[1] - blendRange1, cascadeEnds[1], depth);
        float s1 = CalculateShadow(lightSpaceMatrices[1] * worldPos4, shadowMap1, bias1, 1.0);
        shadow = mix(s1, 1.0, t);
        cascadeIndex = 2;
        cascadeBlendT = t;
    }
    else {
        shadow = 1.0;
        cascadeIndex = 2;
    }

    // Terrain only receives shadows when the sun side faces up (no shadow on slopes
    // pointing away from the light) — same smoothstep mask as the main shader.
    float shadowMask = smoothstep(0.0, 0.20, dot(norm, shadowLightDir));
    vec3 result = ambient + sunPbr * shadowMask * shadow;

    // ── LOCAL LIGHTS (Point / Spot) — added on top of the sun lighting ──
    result += calcLocalLights(norm, normalize(viewPos - FragPos)) * texColor;

    // ── FOG ──
    if (useFog == 1) {
        float dist = length(viewPos - FragPos);
        float fogFactor = calcFogFactor(dist, FragPos);
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

    // ── TONEMAP + GAMMA ──
    vec3 mapped = result / (result + vec3(1.0));
    mapped = pow(mapped, vec3(1.0 / 2.2));

    FragColor = vec4(mapped, 1.0);
}
