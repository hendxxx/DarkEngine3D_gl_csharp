#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec3 aColor;
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
uniform float u_vertexDisplace = 0.0;        // 1 = displace this draw
uniform float u_dispScale = 0.15;            // peak height in world units
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

// Active displacement source.
float displaceSource(vec2 uv) {
    return dispHeight(uv);
}

void main() {
    vec4 worldPos = model * vec4(aPos, 1.0);
    vec3 nw = normalize(mat3(transpose(inverse(model))) * aNormal);
    vec2 uvH = aTexCoord * u_uvScale[5] + u_uvOffset[5];

    if (u_vertexDisplace > 0.5 && u_dispScale > 0.0) {
        // Displace along the model-space normal (plane normal = +Y up).
        float h = displaceSource(uvH);
        worldPos.xyz += nw * h * u_dispScale;

        // Re-derive the normal from the height-field gradient (central
        // differences). Footprint = ONE GRID CELL measured in mesh-UV space,
        // not texels: a texel-wide window on a 4K map measures pure per-pixel
        // noise (near-vertical fake slopes, speckled shading). One-cell
        // gradients match what the mesh can actually render — smooth,
        // believable slopes; the normal map carries the sub-cell detail.
        // Slope = Δh over the cell / cell world size. Tiling cancels because
        // the sample window scales with u_uvScale (cell in height-UV space) and
        // the world span of a mesh-UV cell is planeLength / gridSegments.
        vec2 cellUv = max(u_uvScale[5], vec2(1e-3)) / u_dispGrid;   // one cell, height-map UV space
        vec3 Tx = mat3(model) * vec3(1.0, 0.0, 0.0);
        vec3 Bz = mat3(model) * vec3(0.0, 0.0, 1.0);
        float cellWorldU = max(length(Tx) / u_dispGrid, 1e-5);
        float cellWorldV = max(length(Bz) / u_dispGrid, 1e-5);
        float slopeU = (displaceSource(uvH + vec2(cellUv.x, 0.0)) - displaceSource(uvH - vec2(cellUv.x, 0.0))) * u_dispScale / (2.0 * cellWorldU);
        float slopeV = (displaceSource(uvH + vec2(0.0, cellUv.y)) - displaceSource(uvH - vec2(0.0, cellUv.y))) * u_dispScale / (2.0 * cellWorldV);
        nw = normalize(nw - normalize(Tx) * slopeU
                           - normalize(Bz) * slopeV);
    }

    FragPos = worldPos.xyz;
    Normal = nw;
    ObjColor = aColor;
    TexCoord = aTexCoord;
    vec4 viewPos = view * worldPos;
    viewDepth = -viewPos.z;
    gl_Position = projection * viewPos;
}
