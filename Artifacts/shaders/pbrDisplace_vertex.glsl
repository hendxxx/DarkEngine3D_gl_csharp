#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 3) in vec2 aTexCoord;

// ── GEOMETRIC DISPLACEMENT vertex stage (PBR planes with a height map) ──
// True Marmoset-style Height displacement: each vertex of the tessellated
// plane grid is pushed along its normal by the height map (white = peaks,
// gray = flat, black = valleys) and the normal is re-derived from the height
// gradient, so lighting follows the REAL geometry — silhouette, parallax and
// self-occlusion become physical instead of faked.
out vec3 FragPos;
out vec3 Normal;
out vec3 ObjColor;
out vec2 TexCoord;
out float viewDepth;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

uniform sampler2D heightMap;                 // unit 5 (bound by DrawPbrPrimitive)
uniform sampler2D terrainHeightMap;          // unit 15 — terrain ELEVATION (base shape),
                                             // separate from the POM detail height map
uniform sampler2D u_sculptDeltaMap;          // unit 16 — ADDITIVE sculpt delta layer
                                             // (0.5 = neutral, brush edits live here so
                                             // the authored base heightmap is NEVER touched)
uniform float u_sculptAmp = 0.0;             // delta amplitude in world units:
                                             // (delta − 0.5)·2·amp added to elevation
// NOTE: the sculpt delta is sampled with RAW aTexCoord (NO tiling) — it is a
// per-plane edit layer and must map 1:1 onto the mesh, or brush strokes land
// offset/wrapped whenever the base heightmap's tiling ≠ 1. The BASE keeps uvT.
uniform float u_terrainDisplace = 0.0;       // 1 = displace from terrainHeightMap (RAW 0..1)
uniform vec2 u_terrainUvScale = vec2(1.0);   // terrain elevation OWN tiling — independent
                                             // from the PBR per-map tiling / global Map Tiling
uniform float u_vertexDisplace = 0.0;        // 1 = displace this draw
uniform float u_dispScale = 0.15;            // peak height in world units
uniform float u_dispOffset = 0.0;            // base offset in world units — shifts the WHOLE
                                             // height field up/down (negative = sink the base)
uniform float u_dispStrength = 1.0;          // relief intensity: reshapes the RAW elevation
                                             // around mid-gray (1 = unchanged, <1 = flatter,
                                             // >1 = steeper peaks/deeper valleys, 0 = flat)
uniform float u_terrainBaseHeight = 0.15;    // BASE heightmap amplitude — the terrain shape
                                             // from the image stands this tall (raw h scaled)
uniform float u_pbrHeightDetail = 0.0;       // 1 = a REAL PBR height map exists (unit 5) —
                                             // its calibrated value drives the DETAIL
                                             // vertex displacement (u_dispScale). The
                                             // elevation fallback bind does NOT count
uniform float u_dispGrid = 256.0;            // tessellation segments per side
uniform vec3 u_heightTuning = vec3(1.0, 0.0, 0.0);       // strength, invert, blur
uniform vec4 u_heightAdvance = vec4(1.0, 0.5, 0.0, 0.5); // contrast, ctr, offset, scale center
uniform vec2 u_uvScale[7];
uniform vec2 u_uvOffset[7];

// Same calibration pipeline as the fragment's sampleHeight() so displaced
// geometry and shading agree on where the surface sits. A small fixed blur
// (Marmoset: smooth microscopic surface noise to prevent displacement
// tearing across polygon vertices) keeps vertices from spiking on per-pixel
// 4K noise the 256² grid can't represent anyway.
// Height sampling for DISPLACEMENT — the POM height map (unit 5) with full
// Marmoset calibration.
float dispHeightRaw(sampler2D tex, vec2 uv) {
    vec2 o = 0.75 / vec2(textureSize(tex, 0));
    return (texture(tex, uv).r * 2.0
          + texture(tex, uv + vec2(o.x, 0.0)).r
          + texture(tex, uv - vec2(o.x, 0.0)).r
          + texture(tex, uv + vec2(0.0, o.y)).r
          + texture(tex, uv - vec2(0.0, o.y)).r) * 0.2;
}

float dispHeight(vec2 uv) {
    float h = dispHeightRaw(heightMap, uv);
    h = (h - 0.5) * u_heightTuning.x + 0.5;
    if (u_heightTuning.y > 0.5) h = 1.0 - h;
    h = (h - u_heightAdvance.y) * u_heightAdvance.x + u_heightAdvance.y;
    h += u_heightAdvance.z;
    return clamp(h - u_heightAdvance.w, 0.0, 1.0);
}

// Active displacement source. TERRAIN ELEVATION (base shape) comes from the
// dedicated unit-15 map RAW 0..1 — NO Marmoset calibration (calibration fakes
// slopes and desyncs CPU picking); the PBR height map (unit 5) stays a POM
// DETAIL source with full calibration, never geometry.
// WORLD-UNIT elevation of the displaced surface. TWO SEPARATE SOURCES:
//   base   = raw terrain heightmap (unit 15) × u_terrainBaseHeight → the SHAPE
//   detail = PBR height map (unit 5, calibrated — POM tuning applies) × u_dispScale
//            → extra VERTEX DISPLACEMENT detail from the PBR map (own tiling)
// The slope/normal derivation reads this SAME function, so lighting stays
// physically consistent with the displaced geometry.
float displaceWorld(vec2 uvT, vec2 uvD, vec2 uvP) {
    if (u_terrainDisplace > 0.5) {
        float base = texture(terrainHeightMap, uvT).r * u_terrainBaseHeight;
        // ADDITIVE sculpt layer: brush edits live on a SEPARATE delta map
        // (0.5 = neutral) so the authored base heightmap is never overwritten;
        // the combined surface = base + (delta − 0.5)·2·amp (bidirectional).
        // Delta uses RAW mesh UV (uvD) — see the note above the uniform.
        float sculpt = (u_sculptAmp > 0.0)
            ? (texture(u_sculptDeltaMap, uvD).r - 0.5) * 2.0 * u_sculptAmp
            : 0.0;
        float detail = (u_pbrHeightDetail > 0.5 && u_dispScale > 0.0)
            ? dispHeight(uvP) * u_dispScale   // baseline-relative 0..1 (calibrated)
            : 0.0;
        return base + sculpt + detail;
    }
    return dispHeight(uvP) * u_dispScale;
}

void main() {
    vec4 worldPos = model * vec4(aPos, 1.0);
    vec3 nw = normalize(mat3(transpose(inverse(model))) * aNormal);
    // TWO SEPARATE UVs: the terrain elevation samples through its OWN tiling;
    // the PBR height DETAIL samples through the POM slot's tiling/offset.
    vec2 uvT = aTexCoord * u_terrainUvScale;
    vec2 uvP = aTexCoord * u_uvScale[5] + u_uvOffset[5];

    // Displace when flagged. For terrain-driven planes the heights live in the
    // decomposed uniforms (base + detail), so u_dispScale is NOT part of the gate.
    vec2 uvD = aTexCoord;   // sculpt delta: RAW mesh UV (no tiling — 1:1 edit layer)
    if (u_vertexDisplace > 0.5 && (u_terrainDisplace > 0.5 || u_dispScale > 0.0)) {
        // Displace along the model-space normal (plane normal = +Y up).
        // u_dispOffset lifts the WHOLE displaced surface — a terrain "height
        // offset": raise islands above water level or sink the base below the grid.
        float elev = displaceWorld(uvT, uvD, uvP);
        worldPos.xyz += nw * (elev + u_dispOffset);

        // Re-derive the normal from the height-field gradient (central
        // differences). Footprint = ONE GRID CELL measured in mesh-UV space,
        // not texels: a texel-wide window on a 4K map measures pure per-pixel
        // noise (near-vertical fake slopes, speckled shading). One-cell
        // gradients match what the mesh can actually render — smooth,
        // believable slopes; the normal map carries the sub-cell detail.
        // Slope = Δh over the cell / cell world size. Tiling cancels because
        // the sample window scales with u_uvScale (cell in height-UV space) and
        // the world span of a mesh-UV cell is planeLength / gridSegments.
        // One GRID CELL per SOURCE: the base (terrain tiling) and the detail
        // (POM tiling) each step through their own UV space — gradients of the
        // two sources add up just like the elevations do.
        vec2 cellT = max(u_terrainUvScale, vec2(1e-3)) / u_dispGrid;
        vec2 cellD = vec2(1.0) / u_dispGrid;   // delta = raw UV, one mesh-UV cell
        vec2 cellP = max(u_uvScale[5], vec2(1e-3)) / u_dispGrid;
        vec3 Tx = mat3(model) * vec3(1.0, 0.0, 0.0);
        vec3 Bz = mat3(model) * vec3(0.0, 0.0, 1.0);
        float cellWorldU = max(length(Tx) / u_dispGrid, 1e-5);
        float cellWorldV = max(length(Bz) / u_dispGrid, 1e-5);
        // displaceWorld already returns WORLD UNITS — slope = Δelev / Δworld.
        float slopeU = (displaceWorld(uvT + vec2(cellT.x, 0.0), uvD + vec2(cellD.x, 0.0), uvP + vec2(cellP.x, 0.0))
                      - displaceWorld(uvT - vec2(cellT.x, 0.0), uvD - vec2(cellD.x, 0.0), uvP - vec2(cellP.x, 0.0))) / (2.0 * cellWorldU);
        float slopeV = (displaceWorld(uvT + vec2(0.0, cellT.y), uvD + vec2(0.0, cellD.y), uvP + vec2(0.0, cellP.y))
                      - displaceWorld(uvT - vec2(0.0, cellT.y), uvD - vec2(0.0, cellD.y), uvP - vec2(0.0, cellP.y))) / (2.0 * cellWorldV);
        nw = normalize(nw - normalize(Tx) * slopeU
                           - normalize(Bz) * slopeV);
    }

    FragPos = worldPos.xyz;
    Normal = nw;
    ObjColor = vec3(1.0);
    TexCoord = aTexCoord;
    vec4 viewPos = view * worldPos;
    viewDepth = -viewPos.z;
    gl_Position = projection * viewPos;
}
