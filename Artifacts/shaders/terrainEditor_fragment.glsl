#version 400 core
out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec3 ObjColor;
in vec2 TexCoord;
in float viewDepth;

// ── CONFIG (set per terrain plane) ──
uniform vec3 heightScale;      // .x = world height range used to normalize FragPos.y
uniform vec4 layerLevels;      // .x=air top, .y=dirt top, .z=grass top, .w=snow top (normalized heights)
uniform float slopeThreshold;  // steepness (1 - n.y) above which dirt/rock takes over
uniform float texTiling;       // world-space texture tiling
uniform vec3 sunDir, lightColor, viewPos, fogColor;
uniform int useFog;
uniform sampler2D tex0, tex1, tex2, tex3; // air, dirt, grass, snow
uniform sampler2D tex4;      // splat/control map: RGBA weights for air/dirt/grass/snow (manual paint)
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
uniform float u_ConstantBias = 0.000001;   // always-added bias (all surfaces)
uniform float u_SlopeBias = 0.0003;  // slope-scaled bias coefficient (~4 texels steep)
uniform float u_MinBias = 0.0002;    // minimum bias (~2.5 texels flat, light-facing surfaces)
uniform float u_BlendRange = 0.10;   // cascade blend width, fraction of the split distance
uniform vec3 u_DepthRange = vec3(1.0); // world ortho depth range per cascade (zFar - zNear)
// World size of one shadow-map texel per cascade. CPU MUST upload these two uniforms
// (ShadowUniforms.UploadCascadeScales) — the vec3(1.0) defaults are only a fallback.
uniform vec3 u_TexelWorld = vec3(1.0);
uniform vec3 u_MaxWorldBias = vec3(0.15, 0.25, 1.0); // per-cascade cap on the bias' WORLD offset (m)

// Debug overlay (L key): tint each cascade with a transparent color code.
uniform int showCSMCascadeColor;

// Cascade overlay strength (L key debug tint), 0..1 — adjustable from the Shadow panel.
uniform float u_CascadeOverlayAlpha = 0.15;
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

vec3 sampleLayer(sampler2D tex, vec2 uv) {
    vec2 p = floor(uv);
    vec2 f = fract(uv);
    vec3 res = vec3(0.0);
    float weightSum = 0.0;
    for (int j = -1; j <= 1; j++) {
        for (int i = -1; i <= 1; i++) {
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

// Dark contour-line factor at normalized height t: 1 = exactly on a line (every 10%
// height), 0 = between lines. Shared by the heatmap and the contour-only overlays so
// the interval/width never drift apart.
float contourFactor(float t) {
    float band = fract(clamp(t, 0.0, 1.0) * 10.0);
    return 1.0 - smoothstep(0.0, 0.06, min(band, 1.0 - band));
}

vec3 triplanarLayer(sampler2D tex, vec3 worldPos, vec3 normal, float tiling) {
    vec3 blending = abs(normalize(normal));
    blending = pow(blending, vec3(10.0));
    blending /= (blending.x + blending.y + blending.z);

    vec3 xTex = sampleLayer(tex, worldPos.zy * tiling);
    vec3 yTex = sampleLayer(tex, worldPos.xz * tiling);
    vec3 zTex = sampleLayer(tex, worldPos.xy * tiling);

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
void main() {
    vec3 norm = normalize(Normal);
    float slope = 1.0 - norm.y;

    // ── 4-LAYER HEIGHT TEXTURING ──
    float h = FragPos.y / max(heightScale.x, 1.0);
    float noise = smoothNoise(FragPos.xz * 0.2) * 0.08;
    float hn = clamp(h + noise, 0.0, 1.0);

    vec3 t0 = triplanarLayer(tex0, FragPos, norm, texTiling); // air
    vec3 t1 = triplanarLayer(tex1, FragPos, norm, texTiling); // tanah
    vec3 t2 = triplanarLayer(tex2, FragPos, norm, texTiling); // rumput
    vec3 t3 = triplanarLayer(tex3, FragPos, norm, texTiling); // salju

    vec3 base;
    if (hn < layerLevels.x) base = t0;
    else if (hn < layerLevels.y) base = mix(t0, t1, smoothstep(layerLevels.x, layerLevels.y, hn));
    else if (hn < layerLevels.z) base = mix(t1, t2, smoothstep(layerLevels.y, layerLevels.z, hn));
    else base = mix(t2, t3, smoothstep(layerLevels.z, layerLevels.w, hn));

    // ── SLOPE → tanah/batu (cliff) ──
    float sn = slope + noise * 0.1;
    float cliffMask = smoothstep(slopeThreshold, slopeThreshold + 0.12, sn);
    vec3 texColor = mix(base, t1, cliffMask * 0.85);

    // ── MANUAL LAYER PAINT (splat override, painted with the 🎨 brush) ──
    // The splat map holds per-layer weights in [0,1]. Where the total painted weight
    // is significant, the painted layer takes over the auto height/slope choice.
    if (usePaintMask == 1) {
        vec4 w = texture(tex4, TexCoord + 0.5).rgba;
        float wsum = w.x + w.y + w.z + w.w;
        if (wsum > 0.02) {
            vec4 w2 = w * w;              // sharpen the dominant channel
            float t = w2.x + w2.y + w2.z + w2.w;
            if (t > 0.001) {
                vec3 painted = (t0 * w2.x + t1 * w2.y + t2 * w2.z + t3 * w2.w) / t;
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
    // Slope-scaled bias (same tuning as fragment_shader.glsl): steeper faces get a larger
    // bias so the heightmapped terrain doesn't show self-shadow acne on its slopes.
    float baseBias = max(u_ConstantBias + u_SlopeBias * (1.0 - ndotl), u_MinBias);
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

    // ── FOG ──
    if (useFog == 1) {
        float dist = length(viewPos - FragPos);
        float fogFactor = 1.0 - exp(-pow(dist * 0.0035, 2.0));
        result = mix(result, fogColor, clamp(fogFactor, 0.0, 1.0));
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
