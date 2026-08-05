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
    vec3 result = (ambient + diffuse) * texColor;

    // ── FOG ──
    if (useFog == 1) {
        float dist = length(viewPos - FragPos);
        float fogFactor = 1.0 - exp(-pow(dist * 0.0035, 2.0));
        result = mix(result, fogColor, clamp(fogFactor, 0.0, 1.0));
    }

    // ── TONEMAP + GAMMA ──
    vec3 mapped = result / (result + vec3(1.0));
    mapped = pow(mapped, vec3(1.0 / 2.2));

    FragColor = vec4(mapped, 1.0);
}
