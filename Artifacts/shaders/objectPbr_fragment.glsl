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
    // Exp2 + height blend (default)
    float d = exp(-pow(dist * u_fogDensity, 2.0));
    float heightFactor = clamp(1.0 - (worldPos.y - u_fogHeight) / max(u_fogHeightRange, 0.001), 0.0, 1.0);
    heightFactor = pow(heightFactor, 2.0);
    float heightWeight = mix(heightFactor, 1.0, 1.0 - d);
    return clamp(mix(1.0, d, heightWeight), 0.0, 1.0);
}

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

uniform vec2 u_uvScale[7];   // per-map UV tiling multiplier (x = U, y = V) — albedo..emission
uniform vec2 u_uvOffset[7];  // per-map UV offset (x = U, y = V)
uniform float parallaxScale = 0.15;   // base height-map displacement strength (0 = off, 0.15 = default strong)
uniform int u_pomMinSteps = 16;       // POM ray-march steps head-on → min (cheap)
uniform int u_pomMaxSteps = 96;       // POM ray-march steps grazing → max (detail, no banding)
uniform float u_pomShadowStrength = 0.6; // relief self-shadowing strength (0 = off, 1 = hard)
uniform float u_vertexDisplace = 0.0; // 1 = geometry ALREADY displaced by the vertex stage → skip view-ray POM
uniform float u_splatDetail = 0.0;    // splat per-layer POM detail amplitude (0 = off)

// ── PBR MAP TUNING (uploaded from the PBR panel; applies to the selected object) ──
uniform vec3 u_albedoTuning = vec3(1.0, 1.0, 1.0);    // brightness, saturation, contrast
uniform vec2 u_normalTuning = vec2(1.0, 0.0);         // strength, blur (texels)
uniform vec3 u_metallicTuning = vec3(0.5, 0.1, 1.0);  // threshold, softness, strength
uniform vec2 u_roughnessTuning = vec2(1.0, 0.0);      // strength, invert(0/1)
uniform vec2 u_aoTuning = vec2(1.0, 0.0);             // strength, brightness offset
uniform vec3 u_heightTuning = vec3(1.0, 0.0, 0.0);    // strength, invert(0/1), blur (texels)
// ── HEIGHT ADVANCE (Marmoset-style displacement calibration) ──
// x = contrast (1 = off), y = contrast center, z = offset (−0.5..0.5), w = scale center
uniform vec4 u_heightAdvance = vec4(1.0, 0.5, 0.0, 0.5);
uniform float u_emissionIntensity = 1.0;

// ── CSM SHADOWS (editor viewport — shadow maps bound to units 7/8/9) ──
uniform int shadowFilterMode;
uniform sampler2D shadowMap0;
uniform sampler2D shadowMap1;
uniform sampler2D shadowMap2;
uniform mat4 lightSpaceMatrices[3];
uniform float cascadeEnds[3];
uniform vec3 shadowDir;

// ── LIVE SHADOW TUNING (Shadow Settings panel — same uniform names as the gltf /
//    main shaders so ShadowUniforms.UploadMain fills them; defaults are the fallback) ──
uniform float u_ConstantBias = 0.00005;
uniform float u_SlopeBias = 0.00005;
uniform float u_MinBias = 0.00001;
uniform float u_BlendRange = 1.0;
uniform vec3 u_DepthRange = vec3(1.0);
uniform vec3 u_TexelWorld = vec3(1.0);
uniform vec3 u_MaxWorldBias = vec3(50.5, 150.5, 350.5);

// Debug overlay (L key): tint each cascade with a transparent color code.
uniform int showCSMCascadeColor;
uniform float u_CascadeOverlayAlpha = 0.15;

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
uniform sampler2D u_localShadowSpot[4];
uniform samplerCube u_localShadowPoint[3];

// ── TERRAIN SPLAT (4 texture layers blended on the displaced plane) ──
// Weight map: RGBA8 (unit 10) — one channel per layer, weights sum to 1 per
// texel. Layer textures live on units 11-14 (albedo), the whole set samples
// through the BASE albedo map's tiling/offset (uniform look across the map).
// Weights come from TWO sources combined with max: manual viewport painting and
// AUTO height bands computed CPU-side from the sculpted elevation.
uniform sampler2D u_splatWeights;        // unit 10 (RGBA weights)
uniform sampler2D u_splatAlbedo[4];      // units 11-14 (layer albedo)
uniform sampler2D u_splatNormal[4];      // per-layer tangent-space normals
uniform sampler2D u_splatMetal[4];       // per-layer metallic (R)
uniform sampler2D u_splatRough[4];       // per-layer roughness (R)
uniform sampler2D u_splatAo[4];          // per-layer AO (R)
uniform sampler2D u_splatHeight[4];      // per-layer POM detail height (R)
uniform int u_splatActive;               // 1 = blend the layers (else plain PBR path)
uniform vec4 u_splatHasAlbedo;           // per-layer presence (empty slot w → weight 0)
uniform vec4 u_splatHasNormal;
uniform vec4 u_splatHasMetal;
uniform vec4 u_splatHasRough;
uniform vec4 u_splatHasAo;
uniform vec4 u_splatHasHeight;
uniform vec3 u_splatTint[4];             // layer fallback tint (empty slot OR no albedo tex)
uniform float u_splatNormalStr = 1.0;    // normal-map strength for layer normals

// Blend two already-tuned PBR parameter sets by weight w (0..1).
// NOTE: emission is intentionally NOT blended — layers carry no emission maps,
// so faking it from layer albedo would make every terrain glow.
void splatMerge(inout vec3 albA, inout vec3 nrmA, inout float metA,
                inout float rghA, inout float aoA,
                vec3 albB, vec3 nrmB, float metB, float rghB, float aoB,
                float w) {
    albA = mix(albA, albB, w);
    nrmA = normalize(mix(nrmA, nrmB, w));
    metA = mix(metA, metB, w);
    rghA = mix(rghA, rghB, w);
    aoA  = mix(aoA, aoB, w);
}
uniform mat4 u_localLightSpace[MAX_LOCAL_LIGHTS];
uniform int u_localShadowSpotIdx[MAX_LOCAL_LIGHTS];
uniform int u_localShadowPointIdx[MAX_LOCAL_LIGHTS];
uniform float u_localShadowFar[MAX_LOCAL_LIGHTS];
uniform float u_localShadowBias = 0.004;
uniform float u_localShadowPointBias = 0.02;

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
// HEIGHT SAMPLING (Marmoset-style calibration — raw 0.5 = flat)
// Pipeline: blur → invert → strength → contrast around center → offset →
// scale-center rebase. Contrast exaggerates the separation between low and
// high areas (Marmoset "Contrast"); center picks which gray level is treated
// as mid; offset shifts the whole field to stop crumpled edges / lift valleys;
// scale center sets the baseline the parallax depth measures from.
// ======================================================
float sampleHeight(sampler2D tex, vec2 uv, float blurTexels) {
    float h = sampleHeightBlurred(tex, uv, blurTexels);
    h = (h - 0.5) * u_heightTuning.x + 0.5;
    if (u_heightTuning.y > 0.5) h = 1.0 - h;
    // Contrast around the adjustable center (Marmoset "Contrast" + "Contrast Center")
    h = (h - u_heightAdvance.y) * u_heightAdvance.x + u_heightAdvance.y;
    // Global field shift (Marmoset "Offset") — raise valleys / tame spikes
    h += u_heightAdvance.z;
    // Rebase so the scale center maps to 0 depth (Marmoset "Scale Center")
    return clamp(h - u_heightAdvance.w, 0.0, 1.0);
}

// ======================================================
// PBR BRDF (Cook-Torrance — same math as the gltf shaders)
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

// Accumulate Cook-Torrance PBR lighting from all local (point/spot) lights.
vec3 calcLocalLights(vec3 N, vec3 V, vec3 albedo, float roughness, float metallic)
{
    vec3 Lo = vec3(0.0);
    for (int i = 0; i < MAX_LOCAL_LIGHTS; i++)
    {
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

        float NdotL = max(dot(N, L), 0.0);
        if (NdotL <= 0.0) continue;
        float NdotV = max(dot(N, V), 0.001);
        vec3 H = normalize(V + L);
        float LdotH = max(dot(L, H), 0.0);
        vec3 F0 = mix(vec3(0.04), albedo, metallic);
        vec3 F = FresnelSchlick(LdotH, F0);
        float D = DistributionGGX(N, H, roughness);
        float G = GeometrySmith(N, V, L, roughness);
        vec3 specular = D * G * F / max(4.0 * NdotV * NdotL, 0.001);
        vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);
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
        Lo += (kD * albedo / PI + specular) * u_lightColor[i] * u_lightIntensity[i] * NdotL * attenuation * shadowFactor;
    }
    return Lo;
}

// ======================================================
// SHADOW — Poisson PCF (shared line-for-line with the gltf shader)
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
    // ── TWO-SIDED SHADING: flat planes are a single CCW quad drawn with culling
    //    disabled (see EditorObject.DrawPbrPrimitive), so their back side IS
    //    rendered when the camera looks from below. The attribute normal still
    //    points up there — lighting + the derivative tangent basis built from it
    //    would shade the underside inverted (dark, relief flipped). Flipping the
    //    geometry normal here flips the whole T/B basis with it (T/B derive from
    //    cross products against norm), so both sides shade correctly. Closed
    //    meshes (Box/Sphere) never see backfaces — no change for them.
    if (!gl_FrontFacing) norm = -norm;
    float slope = 1.0 - norm.y;
    vec3 viewDir = normalize(viewPos - FragPos);
    // Per-map UV transforms (each map has its own tiling/offset in TextureSettings).
    // The albedo map defines the base UV space used by parallax below.

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

    // ── PER-MAP UVs (each texture slot has its own tiling/offset) ──
    vec2 uvAlbedo   = TexCoord * u_uvScale[0] + u_uvOffset[0];
    vec2 uvNormal   = TexCoord * u_uvScale[1] + u_uvOffset[1];
    vec2 uvMetallic = TexCoord * u_uvScale[2] + u_uvOffset[2];
    vec2 uvRough    = TexCoord * u_uvScale[3] + u_uvOffset[3];
    vec2 uvAo       = TexCoord * u_uvScale[4] + u_uvOffset[4];
    vec2 uvHeight   = TexCoord * u_uvScale[5] + u_uvOffset[5];
    vec2 uvEmission = TexCoord * u_uvScale[6] + u_uvOffset[6];

    // ── Sun key-light direction (also used by the POM relief self-shadow below
    //    and the direct-lighting term). Night → flips to the MOON direction
    //    (opposite the sun) so primitives keep a soft directional moon key
    //    instead of losing all direct light (NdotL of a down-pointing sun is
    //    ≤ 0). Radiance dimming itself is owned by Lights.cs — lightColor
    //    arrives already faded to the moon tint.
    float nightBlendPbr = smoothstep(0.15, 0.0, sunDir.y);
    vec3 L = normalize(mix(normalize(sunDir), normalize(-sunDir), nightBlendPbr));

    // ── STEEP PARALLAX OCCLUSION MAPPING (POM) — revamped ──
    // Height map displaces the UV along the tangent-space view ray. Revamp:
    //   • ray in the CORRECT basis (transpose(TBN)·V — TBN·V was an inverse
    //     rotation, so grazing relief depth was wrong on every primitive),
    //   • canonical height mapping (sampleHeight): full 0..1 depth span,
    //   • adaptive steps 8..48 by view obliqueness (far/grazing = cheap, no
    //     banding close up), step count scales with parallaxScale,
    //   • occlusion offset is per-map-scaled downstream so per-map tiling
    //     stays locked (identical displacement for all 7 maps).
    int pomSteps = u_pomMinSteps;
    vec2 pomOff = vec2(0.0);
    float pomShadow = 1.0;
    // Splat per-layer POM detail amplitude (0 = off): uses the base POM slider so
    // the layer relief is always proportional to the base relief depth.
    float effSplatDetail = (u_splatDetail > 0.0) ? parallaxScale : 0.0;
    if (useHeight == 1 && parallaxScale > 0.0) {
        // ── SMEAR GUARDS (relief stays FULL at every angle) — the classic POM
        //    failure mode is the huge OFFSET at grazing angles, not the depth
        //    itself; the ±0.16 UV hard cap below handles that. Physical tangent
        //    kept up to ~87°, no oblique fading; far-field fade for stability.
        float pomDistFade = 1.0 - smoothstep(80.0, 160.0, viewDepth);
        // Layered detail: over vertex-displaced geometry the large shapes are
        // REAL, so the ray march contributes only ~30% depth — the sub-cell
        // micro occlusion (workflow: displacement for large forms, POM for
        // micro detail). Flat surfaces keep the full-depth march.
        float micro = (u_vertexDisplace >= 0.5) ? 0.3 : 1.0;
        float effScale = parallaxScale * pomDistFade * micro;
        if (effScale > 0.004) {
        vec3 Vts = normalize(transpose(TBN) * viewDir);   // tangent-space view ray
        float facing = clamp(abs(Vts.z), 0.0, 1.0);       // 1 = head-on, 0 = grazing
        pomSteps = int(mix(float(u_pomMaxSteps), float(u_pomMinSteps), facing) * clamp(effScale / 0.15, 0.2, 1.5));
        pomSteps = clamp(pomSteps, 4, 96);
        float layerDepth = 1.0 / float(pomSteps);
        vec2 texelPerLayer = Vts.xy / max(abs(Vts.z), 0.05) * effScale / float(pomSteps);
        vec2 currentUV = uvHeight;
        float currentLayerDepth = 0.0;
        float currentTexelHeight = sampleHeight(heightMap, currentUV, max(u_heightTuning.z, 0.0));
        // Linear ray march (tangent-space ray tracing): stop where the layer
        // plane passes under the height surface.
        for (int i = 0; i < 96; i++) {
            if (i >= pomSteps) break;
            if (currentLayerDepth >= currentTexelHeight) break;
            currentUV -= texelPerLayer;
            currentLayerDepth += layerDepth;
            currentTexelHeight = sampleHeight(heightMap, currentUV, max(u_heightTuning.z, 0.0));
        }
        // Occlusion interpolation between the straddling samples (parallax
        // occlusion, not plain steep parallax — kills the layer stair-stepping).
        vec2 prevUV = currentUV + texelPerLayer;
        float afterDepth = currentTexelHeight - currentLayerDepth;
        float beforeDepth = sampleHeight(heightMap, prevUV, max(u_heightTuning.z, 0.0)) - (currentLayerDepth - layerDepth);
        float weight = clamp(afterDepth / max(afterDepth - beforeDepth, 0.001), 0.0, 1.0);
        pomOff = uvHeight - mix(currentUV, prevUV, weight);
        // Hard-cap the shift well below one tile: anything beyond that smears
        // unrelated texture regions across the surface (the comb-tooth look).
        // 0.16 ≈ full relief depth before grazing fade — deep but tile-safe.
        pomOff = clamp(pomOff, vec2(-0.16), vec2(0.16));
        }

        // ── RELIEF SELF-SHADOWING — march the height field toward the SUN in
        //    tangent space (like the “soft shadow” pass in the classic POM
        //    paper). This is what makes stones look like they sit ON the
        //    ground: slopes facing away from the key light fall into shadow,
        //    peaks catch light. u_pomShadowStrength 0 = off, 1 = hard.
        if (u_pomShadowStrength > 0.001 && effScale > 0.01) {
            vec3 Lts = normalize(transpose(TBN) * L);
            if (Lts.z > 0.0) { // sun above the surface — relief can cast shadow
                float shadowScale = effScale * 0.5; // short soft penumbra
                float numSamplesSh = 8.0;
                float layerSh = 1.0 / numSamplesSh;
                vec2 offsetSh = Lts.xy / max(Lts.z, 0.3) * shadowScale / numSamplesSh;
                float occ = 0.0;
                float reach = 0.0;
                float hAt = sampleHeight(heightMap, uvHeight - pomOff, max(u_heightTuning.z, 0.0));
                for (int s = 0; s < 8; s++) {
                    reach += layerSh;
                    float h = sampleHeight(heightMap, uvHeight - pomOff + offsetSh * reach, max(u_heightTuning.z, 0.0));
                    occ += max(0.0, (hAt + reach * 1.0 - h));
                }
                // Attenuate by slope toward the light + distance falloff (twin:
                // fine steps everywhere is too expensive; far away shadows fade
                // into the CSM/NdotL term anyway).
                float slopeDarken = clamp(1.0 - (1.0 - max(dot(norm, L), 0.0)) * 1.5, 0.2, 1.0);
                float penumbra = 1.0 - clamp(occ / numSamplesSh * u_pomShadowStrength * 1.6, 0.0, 0.95);
                float distFade = 1.0 - smoothstep(60.0, 140.0, viewDepth);
                pomShadow = mix(1.0, penumbra * slopeDarken, distFade);
            }
        }
    }

    // ── Occlusion offset per map: the POM march runs in the HEIGHT map's UV
    //    space; convert its shift into each map's own UV space
    //    (off_i = off_h · tiling_i / tiling_h) so per-map tiling stays
    //    pixel-locked under displacement. Shared by the plain path and the
    //    splat layer path below. ──
    vec2 pomOffPerMap[7];
    {
        vec2 sH = max(abs(u_uvScale[5]), vec2(1e-3));
        for (int m = 0; m < 7; m++)
            pomOffPerMap[m] = clamp(pomOff * (u_uvScale[m] / sH), vec2(-0.25), vec2(0.25));
    }

    // ── TERRAIN SPLAT — blend up to 4 texture layers by the weight map ──
    // Runs BEFORE albedo tuning so the combined surface flows through the same
    // brightness/saturation/contrast pipeline as a single-texture material.
    vec3 albedo = useAlbedo == 1 ? texture(albedoMap, uvAlbedo - pomOffPerMap[0]).rgb : ObjColor;
    vec3 tsNormalSplat = useNormal == 1
        ? sampleNormalBlurred(normalMap, uvNormal - pomOffPerMap[1], max(u_normalTuning.y, 0.0))
        : vec3(0.0, 0.0, 1.0);
    float metallicSplat  = useMetallic  == 1 ? texture(metallicMap,  uvMetallic  - pomOffPerMap[2]).r : 0.0;
    float roughnessSplat = useRoughness == 1 ? texture(roughnessMap, uvRough     - pomOffPerMap[3]).r : 0.6;
    float aoSplat        = useAo        == 1 ? texture(aoMap,        uvAo        - pomOffPerMap[4]).r : 1.0;
    vec3  emission       = useEmission  == 1 ? texture(emissionMap,  uvEmission  - pomOffPerMap[6]).rgb : vec3(0.0);

    if (u_splatActive == 1) {
        // Weights + layer texture footprint: the WHOLE set samples through the
        // BASE albedo map's tiling/offset (uniform across the terrain, no
        // per-layer tiling panels — one consistent texture scale).
        vec4 splatW = texture(u_splatWeights, uvAlbedo - pomOffPerMap[0]);
        // NOTE: do NOT zero weights of albedo-empty layers here — their fallback
        // is the TINT below (u_splatTint). Zeroing made paint/bands on texture-less
        // layers invisible: the renormalize poured every weight back into layer 0
        // and painting appeared to do nothing at all.
        float splatSum = splatW.x + splatW.y + splatW.z + splatW.w;
        if (splatSum > 0.001) splatW /= splatSum; else splatW = vec4(1.0, 0.0, 0.0, 0.0);
        // Layer 0 full → the plain PBR path below is already exactly right.
        if (dot(splatW, vec4(0.0, 1.0, 1.0, 1.0)) > 0.001) {
            vec3 tsN = tsNormalSplat;
            for (int l = 0; l < 4; l++) {
                float w = splatW[l];
                if (w <= 0.001) continue;
                vec3 albL = u_splatHasAlbedo[l] > 0.5
                    ? texture(u_splatAlbedo[l], uvAlbedo - pomOffPerMap[0]).rgb
                    : u_splatTint[l];
                vec3 nrmL = u_splatHasNormal[l] > 0.5
                    ? sampleNormalBlurred(u_splatNormal[l], uvNormal - pomOffPerMap[1], max(u_normalTuning.y, 0.0))
                    : vec3(0.0, 0.0, 1.0);
                float metL = u_splatHasMetal[l] > 0.5
                    ? texture(u_splatMetal[l], uvMetallic - pomOffPerMap[2]).r
                    : 0.0;
                float rghL = u_splatHasRough[l] > 0.5
                    ? texture(u_splatRough[l], uvRough - pomOffPerMap[3]).r
                    : 0.6;
                float aoL = u_splatHasAo[l] > 0.5
                    ? texture(u_splatAo[l], uvAo - pomOffPerMap[4]).r
                    : 1.0;
                // Per-layer POM detail: layer heights share the base height map's
                // UV footprint so the existing pomOff march stays valid.
                if (u_splatHasHeight[l] > 0.5 && useHeight == 1 && parallaxScale > 0.0) {
                    float hL = sampleHeight(u_splatHeight[l], uvHeight - pomOff, max(u_heightTuning.z, 0.0));
                    // Subtract the BASE map's own depth at the same footprint: the
                    // march below already accounts for the base relief, only the
                    // layer's DELTA detail may bend the occlusion further.
                    float hB = sampleHeight(heightMap, uvHeight - pomOff, max(u_heightTuning.z, 0.0));
                    pomOff += clamp(vec2((hB - hL) * effSplatDetail), vec2(-0.16), vec2(0.16));
                }
                // Weighted layer color set — layer 0 folds in with weight (1 − Σ others)
                // so manual paint over the default layer stays exact.
                splatMerge(albedo, tsN, metallicSplat, roughnessSplat, aoSplat,
                    albL, nrmL * u_splatNormalStr + vec3(0.0, 0.0, 1.0 - u_splatNormalStr),
                    metL, rghL, aoL, w);
            }
            tsNormalSplat = tsN;
        }
    }

    // ── NORMAL: tangent-space map → world via TBN (flat geometry normal when absent). ──
    vec3 tsNormal = tsNormalSplat;
    vec3 mapNormal = normalize(T * tsNormal.x + B * tsNormal.y + norm * tsNormal.z);

    float metallic  = metallicSplat;
    float roughness = roughnessSplat;
    float ao        = aoSplat;

    // ── CREVICE AO from the height field: darken occluded valleys (cheap 4-tap
    //    height diff) — sells contact between relief and the surface, boosting
    //    whatever AO map exists.
    if (useHeight == 1 && parallaxScale > 0.0) {
        vec2 texelH = 1.5 / vec2(textureSize(heightMap, 0));
        float h0 = sampleHeight(heightMap, uvHeight - pomOff, 0.0);
        float hAvg = sampleHeight(heightMap, uvHeight - pomOff + vec2( texelH.x, 0.0), 0.0)
                   + sampleHeight(heightMap, uvHeight - pomOff - vec2( texelH.x, 0.0), 0.0)
                   + sampleHeight(heightMap, uvHeight - pomOff + vec2(0.0,  texelH.y), 0.0)
                   + sampleHeight(heightMap, uvHeight - pomOff - vec2(0.0,  texelH.y), 0.0);
        hAvg *= 0.25;
        float crevice = clamp(1.0 - max(hAvg - h0, 0.0) * 4.0 * clamp(parallaxScale / 0.15, 0.0, 1.0), 0.0, 1.0);
        float aoMixW = (useAo == 1) ? 0.5 : 1.0;
        ao = clamp(mix(ao, min(ao, crevice), aoMixW), 0.0, 1.0);
    }

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
    // (Key-light direction L computed above the POM block — the relief
    // self-shadow marches toward it.)
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
    vec3 Lo = (kD * albedo / PI + specular) * lightColor * 4.0 * NdotL * pomShadow;

    // Ambient fill (hemisphere): sky light from above, ground bounce from below, scaled
    // by AO and a gentle slope darkening — strong enough that shadowed faces and sides
    // away from the sun stay clearly readable instead of falling to black.
    float skyMix = 0.5 + 0.5 * N.y;
    float slopeShade = 1.0 - clamp(slope * 0.55, 0.0, 0.5);
    vec3 ambient = albedo * ao * lightColor * (0.14 + 0.30 * skyMix) * slopeShade;

    // ── CSM SHADOWS (editor viewport) ──
    vec3 shadowLightDir = normalize(shadowDir);
    float ndotl = max(dot(norm, shadowLightDir), 0.0);
    // Slope-scaled bias (OpenGL Tutorial 16): bias ∝ tan(acos(N·L)) — grows far faster
    // than the old linear (1−N·L) on slopes turning away from the light, killing
    // self-shadow acne on detailed geometry. tan(acos(x)) = sqrt(1−x²)/x, denominator
    // clamped so N·L = 0 can't divide by zero.
    float ndotlSafe = max(ndotl, 0.05);
    float slopeFactor = sqrt(max(1.0 - ndotlSafe * ndotlSafe, 0.0)) / ndotlSafe;
    float bias0 = max(u_ConstantBias + u_SlopeBias * slopeFactor, u_MinBias);
    float bias1 = bias0;
    float bias2 = bias0;
    vec4 worldPos4 = vec4(FragPos, 1.0);
    float depth = viewDepth;
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

    float shadowMask = smoothstep(0.0, 0.20, dot(norm, shadowLightDir));
    vec3 result = ambient + Lo * shadowMask * shadow;

    // ── LOCAL LIGHTS (Point / Spot) — added on top of the sun lighting ──
    result += calcLocalLights(N, V, albedo, roughness, metallic);

    // ── EMISSION (from emission maps × intensity; 0 by default) ──
    result += emission * u_emissionIntensity;

    // ── FOG ──
    if (useFog == 1) {
        float dist = length(viewPos - FragPos);
        float fogFactor = calcFogFactor(dist, FragPos);
        result = mix(fogColor, result, fogFactor);
    }

    // ── DEBUG CSM COLOR (transparent cascade overlay) ──
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
