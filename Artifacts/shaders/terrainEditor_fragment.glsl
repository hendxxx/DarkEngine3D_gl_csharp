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

    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);
    float angle = randomAngle(gl_FragCoord.xy) * 6.2831853;
    float shadow = 0.0;
    for (int i = 0; i < 16; ++i)
    {
        float r = sqrt(poissonDisk16[i].x) * radius;
        float a = poissonDisk16[i].y + angle;
        vec2 offset = vec2(r * cos(a), r * sin(a));
        float d = texture(shadowMap, projCoords.xy + offset * texelSize).r;
        shadow += (projCoords.z - bias > d) ? 0.0 : 1.0;
    }
    return shadow / 16.0;
}

float hardShadow(vec4 fragPosLightSpace, sampler2D shadowMap, float bias)
{
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;
    if (projCoords.z > 1.0 || projCoords.z < 0.0) return 1.0;
    float d = texture(shadowMap, projCoords.xy).r;
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
    if (usePaintMask == 1 && layerCount <= 4) {
        vec4 w = texture(tex4, TexCoord + 0.5).rgba;
        float wsum = w.x + w.y + w.z + w.w;
        if (wsum > 0.02) {
            vec4 w2 = w * w;
            float t = w2.x + w2.y + w2.z + w2.w;
            if (t > 0.001) {
                // Sample the first 4 layers for splat paint
                vec3 painted = vec3(0.0);
                if (layerCount > 0) painted += sampleDynLayer(0, FragPos.xz, norm, layerTiling[0], layerStochastic[0]) * w2.x;
                if (layerCount > 1) painted += sampleDynLayer(1, FragPos.xz, norm, layerTiling[1], layerStochastic[1]) * w2.y;
                if (layerCount > 2) painted += sampleDynLayer(2, FragPos.xz, norm, layerTiling[2], layerStochastic[2]) * w2.z;
                if (layerCount > 3) painted += sampleDynLayer(3, FragPos.xz, norm, layerTiling[3], layerStochastic[3]) * w2.w;
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
    vec3 lightDir = normalize(sunDir);
    float ndl = dot(norm, lightDir);
    float sunShade = smoothstep(-0.18, 0.55, ndl);            // hard terminator = strong relief
    float slopeShade = 1.0 - clamp(slope * 0.55, 0.0, 0.5);   // steeper faces get darker
    vec3 ambient = 0.14 * lightColor * slopeShade;
    vec3 diffuse = lightColor * (0.30 + 1.05 * sunShade);

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
    float baseBias = max(u_ConstantBias + u_SlopeBias * slopeFactor, u_MinBias);
    // Far cascades need proportionally more bias (same 1.5×/3× scaling as fragment_shader)
    // AND their ortho depth range is far larger, so normalize the NDC bias by the per-cascade
    // depth range (uploaded from CSM) — otherwise the same NDC bias pushes shadows tens of
    // world units away at distance and they vanish.
    float bias0 = baseBias;
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
    vec4 worldPos4 = vec4(FragPos, 1.0);
    float depth = viewDepth;
    float blendRange0 = cascadeEnds[0] * u_BlendRange;
    float blendRange1 = cascadeEnds[1] * u_BlendRange;

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

    // Terrain only receives shadows when the sun side faces up (no shadow on slopes
    // pointing away from the light) — same smoothstep mask as the main shader.
    float shadowMask = smoothstep(0.0, 0.20, dot(norm, shadowLightDir));
    vec3 result = (ambient + diffuse * shadowMask * shadow) * texColor;

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
