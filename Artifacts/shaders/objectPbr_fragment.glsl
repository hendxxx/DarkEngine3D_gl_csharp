#version 400 core
out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec3 ObjColor;
in vec2 TexCoord;
in float viewDepth;

// ── LIGHTS / ENVIRONMENT ──
uniform vec3 sunDir, lightColor, viewPos, fogColor;
uniform int useFog;

// ── PBR MAPS — units 0..6, each optional (use* flags; missing maps keep neutral
//    defaults so the material degrades gracefully: vertex color albedo, flat normal,
//    0 metallic, 0.6 roughness, 1 AO, no parallax, no emission). ──
uniform sampler2D albedoMap;    // unit 0 — base color (fallback: ObjColor)
uniform sampler2D normalMap;    // unit 1 — tangent-space normal
uniform sampler2D metallicMap;  // unit 2 — metalness (R)
uniform sampler2D roughnessMap; // unit 3 — roughness (R)
uniform sampler2D aoMap;        // unit 4 — ambient occlusion (R)
uniform sampler2D heightMap;    // unit 5 — height / displacement (R, 0.5 = flat)
uniform sampler2D emissionMap;  // unit 6 — emissive color
uniform int useAlbedo, useNormal, useMetallic, useRoughness, useAo, useHeight, useEmission;

uniform float u_texTiling = 1.0;      // UV tiling multiplier
uniform float parallaxScale = 0.02;   // base height-map displacement strength (0 = off)

// ── PBR MAP TUNING (uploaded from the PBR panel; applies to the selected object) ──
uniform vec3 u_albedoTuning = vec3(1.0, 1.0, 1.0);    // brightness, saturation, contrast
uniform vec2 u_normalTuning = vec2(1.0, 0.0);         // strength, blur (texels)
uniform vec3 u_metallicTuning = vec3(0.5, 0.1, 1.0);  // threshold, softness, strength
uniform vec2 u_roughnessTuning = vec2(1.0, 0.0);      // strength, invert(0/1)
uniform vec2 u_aoTuning = vec2(1.0, 0.0);             // strength, brightness offset
uniform vec3 u_heightTuning = vec3(1.0, 0.0, 0.0);    // strength, invert(0/1), blur (texels)
uniform float u_emissionIntensity = 1.0;

// ── CSM SHADOWS (editor viewport — shadow maps bound to units 7/8/9) ──
uniform int shadowFilterMode;
uniform sampler2D shadowMap0;
uniform sampler2D shadowMap1;
uniform sampler2D shadowMap2;
uniform mat4 lightSpaceMatrices[3];
uniform float cascadeEnds[3];
uniform vec3 shadowDir;

// ── LIVE SHADOW TUNING (Shadow Settings panel — same uniform names as the terrain /
//    main shaders so ShadowUniforms.UploadMain fills them; defaults are the fallback) ──
uniform float u_ConstantBias = 0.000001;
uniform float u_SlopeBias = 0.0003;
uniform float u_MinBias = 0.0002;
uniform float u_BlendRange = 0.10;
uniform vec3 u_DepthRange = vec3(1.0);
uniform vec3 u_TexelWorld = vec3(1.0);
uniform vec3 u_MaxWorldBias = vec3(0.15, 0.25, 1.0);

// Debug overlay (L key): tint each cascade with a transparent color code.
uniform int showCSMCascadeColor;
uniform float u_CascadeOverlayAlpha = 0.15;

// ======================================================
// MAP BLUR HELPERS (5-tap cross blur in texel space)
// ======================================================
vec3 sampleNormalBlurred(sampler2D tex, vec2 uv, float blurTexels) {
    vec3 c = texture(tex, uv).xyz * 2.0 - 1.0;
    if (blurTexels <= 0.001) return c;
    vec2 o = blurTexels / vec2(textureSize(tex, 0));
    vec3 s = (texture(tex, uv + vec2(o.x, 0.0)).xyz * 2.0 - 1.0)
           + (texture(tex, uv - vec2(o.x, 0.0)).xyz * 2.0 - 1.0)
           + (texture(tex, uv + vec2(0.0, o.y)).xyz * 2.0 - 1.0)
           + (texture(tex, uv - vec2(0.0, o.y)).xyz * 2.0 - 1.0);
    return normalize(c + s);
}

float sampleHeightBlurred(sampler2D tex, vec2 uv, float blurTexels) {
    float c = texture(tex, uv).r;
    if (blurTexels <= 0.001) return c;
    vec2 o = blurTexels / vec2(textureSize(tex, 0));
    return (c
      + texture(tex, uv + vec2(o.x, 0.0)).r
      + texture(tex, uv - vec2(o.x, 0.0)).r
      + texture(tex, uv + vec2(0.0, o.y)).r
      + texture(tex, uv - vec2(0.0, o.y)).r) / 5.0;
}

// ======================================================
// PBR BRDF (Cook-Torrance — same math as the terrain / gltf shaders)
// ======================================================
const float PI = 3.14159265359;

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

// ======================================================
// SHADOW — Poisson PCF (same math as the terrain shader)
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
    vec3 viewDir = normalize(viewPos - FragPos);
    float tiling = max(u_texTiling, 0.001);
    vec2 uvBase = TexCoord * tiling;

    // ── TANGENT BASIS from screen-space derivatives (primitives have no vertex
    //    tangents). Flat faces get a constant basis; UV seams on spheres are the
    //    only visible artifact — acceptable for editor primitives. ──
    vec2 duv1 = dFdx(TexCoord);
    vec2 duv2 = dFdy(TexCoord);
    vec3 dp2perp = cross(dFdy(FragPos), norm);
    vec3 dp1perp = cross(norm, dFdx(FragPos));
    vec3 T = dp2perp * duv1.x + dp1perp * duv2.x;
    vec3 B = dp2perp * duv1.y + dp1perp * duv2.y;
    // Degenerate UVs (e.g. sphere poles where the ring collapses to a point) yield a
    // zero-length basis → +inf → NaN tangents. Fall back to an axis-aligned basis.
    float invmax = inversesqrt(max(dot(T, T), dot(B, B)));
    if (invmax > 1e4) { T = vec3(1.0, 0.0, 0.0); B = vec3(0.0, 1.0, 0.0); }
    else { T *= invmax; B *= invmax; }
    mat3 TBN = mat3(T, B, norm);

    // ── PARALLAX (height map displaces the sample UV along the tangent-space view
    //    ray). Clamped so grazing angles can't swim the texture by many tiles. ──
    vec2 uv = uvBase;
    if (useHeight == 1) {
        float hRaw = sampleHeightBlurred(heightMap, uvBase, max(u_heightTuning.z, 0.0));
        float hh = (hRaw - 0.5) * u_heightTuning.x * (u_heightTuning.y > 0.5 ? -1.0 : 1.0);
        vec3 Vts = normalize(TBN * viewDir);
        vec2 off = Vts.xy / max(abs(Vts.z), 0.02) * hh * parallaxScale;
        uv = uvBase - clamp(off, vec2(-0.05), vec2(0.05));
    }

    // ── ALBEDO: texture if present, else the object's vertex color. ──
    vec3 albedo = useAlbedo == 1 ? texture(albedoMap, uv).rgb : ObjColor;

    // ── NORMAL: tangent-space map → world via TBN (flat geometry normal when absent). ──
    vec3 tsNormal = useNormal == 1
        ? sampleNormalBlurred(normalMap, uv, max(u_normalTuning.y, 0.0))
        : vec3(0.0, 0.0, 1.0);
    vec3 mapNormal = normalize(T * tsNormal.x + B * tsNormal.y + norm * tsNormal.z);

    float metallic  = useMetallic  == 1 ? texture(metallicMap,  uv).r : 0.0;
    float roughness = useRoughness == 1 ? texture(roughnessMap, uv).r : 0.6;
    float ao        = useAo        == 1 ? texture(aoMap,        uv).r : 1.0;
    vec3  emission  = useEmission  == 1 ? texture(emissionMap,  uv).rgb : vec3(0.0);

    // ── ALBEDO TUNING (brightness / saturation / contrast) ──
    albedo *= u_albedoTuning.x;
    float albLuma = dot(albedo, vec3(0.299, 0.587, 0.114));
    albedo = mix(vec3(albLuma), albedo, u_albedoTuning.y);
    albedo = clamp((albedo - 0.5) * u_albedoTuning.z + 0.5, 0.0, 1.0);

    // ── NORMAL STRENGTH (0 = geometry normal, 1 = full map, 2 = overdriven) ──
    vec3 N = normalize(mix(norm, mapNormal, clamp(u_normalTuning.x, 0.0, 2.0)));

    // ── METALLIC / ROUGHNESS / AO TUNING ──
    metallic = smoothstep(u_metallicTuning.x - u_metallicTuning.y,
                          u_metallicTuning.x + u_metallicTuning.y, metallic)
             * u_metallicTuning.z;
    if (u_roughnessTuning.y > 0.5) roughness = 1.0 - roughness; // smoothness map → roughness
    roughness = clamp(roughness * u_roughnessTuning.x, 0.0, 1.0);
    ao = clamp(ao * u_aoTuning.x + u_aoTuning.y, 0.0, 1.0);

    // ── PBR DIRECT LIGHTING (Cook-Torrance, sun as the directional light) ──
    vec3 L = normalize(sunDir);
    vec3 V = viewDir;
    vec3 H = normalize(V + L);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 0.001);

    float D = DistributionGGX(N, H, roughness);
    float G = GeometrySmith(N, V, L, roughness);
    vec3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);
    vec3 specular = D * G * F / (4.0 * NdotV * NdotL + 0.0001);
    vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);

    // Sun radiance scaled (×4 ≈ 1.27 after the /PI) so full-sun brightness matches the
    // hillshade brightness of the other editor shaders; NdotL drives the PBR falloff.
    vec3 Lo = (kD * albedo / PI + specular) * lightColor * 4.0 * NdotL;

    // Ambient fill (hemisphere): sky light from above, ground bounce from below, scaled
    // by AO and a gentle slope darkening — strong enough that shadowed faces and sides
    // away from the sun stay clearly readable instead of falling to black.
    float skyMix = 0.5 + 0.5 * N.y;
    float slopeShade = 1.0 - clamp(slope * 0.55, 0.0, 0.5);
    vec3 ambient = albedo * ao * lightColor * (0.14 + 0.30 * skyMix) * slopeShade;

    // ── CSM SHADOWS (editor viewport) ──
    vec3 shadowLightDir = normalize(shadowDir);
    float ndotl = max(dot(norm, shadowLightDir), 0.0);
    float baseBias = max(u_ConstantBias + u_SlopeBias * (1.0 - ndotl), u_MinBias);
    float bias0 = baseBias;
    float bias1 = baseBias * max(u_TexelWorld.y / max(u_TexelWorld.x, 1e-5), 1.0)
                * clamp(u_DepthRange.x / u_DepthRange.y, 0.02, 4.0);
    float bias2 = baseBias * max(u_TexelWorld.z / max(u_TexelWorld.x, 1e-5), 1.0)
                * clamp(u_DepthRange.x / u_DepthRange.z, 0.02, 4.0);
    bias0 = min(bias0, u_MaxWorldBias.x / max(u_DepthRange.x, 1e-4));
    bias1 = min(bias1, u_MaxWorldBias.y / max(u_DepthRange.y, 1e-4));
    bias2 = min(bias2, u_MaxWorldBias.z / max(u_DepthRange.z, 1e-4));
    vec4 worldPos4 = vec4(FragPos, 1.0);
    float depth = viewDepth;
    float blendRange0 = cascadeEnds[0] * u_BlendRange;
    float blendRange1 = cascadeEnds[1] * u_BlendRange;

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

    float shadowMask = smoothstep(0.0, 0.20, dot(norm, shadowLightDir));
    vec3 result = ambient + Lo * shadowMask * shadow;

    // ── EMISSION (from emission maps × intensity; 0 by default) ──
    result += emission * u_emissionIntensity;

    // ── FOG ──
    if (useFog == 1) {
        float dist = length(viewPos - FragPos);
        float fogFactor = 1.0 - exp(-pow(dist * 0.0035, 2.0));
        result = mix(result, fogColor, clamp(fogFactor, 0.0, 1.0));
    }

    // ── DEBUG CSM COLOR (transparent cascade overlay, same palette as the terrain shader) ──
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
