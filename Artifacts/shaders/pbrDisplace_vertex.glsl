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
uniform vec3 u_heightTuning = vec3(1.0, 0.0, 0.0);       // strength, invert, blur
uniform vec4 u_heightAdvance = vec4(1.0, 0.5, 0.0, 0.5); // contrast, ctr, offset, scale center
uniform vec2 u_uvScale[7];
uniform vec2 u_uvOffset[7];

// Same calibration pipeline as the fragment's sampleHeight() so displaced
// geometry and shading agree on where the surface sits.
float dispHeight(vec2 uv) {
    float h = texture(heightMap, uv).r;
    h = (h - 0.5) * u_heightTuning.x + 0.5;
    if (u_heightTuning.y > 0.5) h = 1.0 - h;
    h = (h - u_heightAdvance.y) * u_heightAdvance.x + u_heightAdvance.y;
    h += u_heightAdvance.z;
    return clamp(h - u_heightAdvance.w, 0.0, 1.0);
}

void main() {
    vec4 worldPos = model * vec4(aPos, 1.0);
    vec3 nw = normalize(mat3(transpose(inverse(model))) * aNormal);
    vec2 uvH = aTexCoord * u_uvScale[5] + u_uvOffset[5];

    if (u_vertexDisplace > 0.5 && u_dispScale > 0.0) {
        // Displace along the model-space normal (plane normal = +Y up).
        float h = dispHeight(uvH);
        worldPos.xyz += nw * h * u_dispScale;

        // Re-derive the normal from the height-field gradient (central
        // differences). World tangents: u runs along +X, v along +Z (plane).
        vec2 texel = 1.5 / vec2(textureSize(heightMap, 0));
        float dhdu = (dispHeight(uvH + vec2(texel.x, 0.0)) - dispHeight(uvH - vec2(texel.x, 0.0))) / (2.0 * texel.x);
        float dhdv = (dispHeight(uvH + vec2(0.0, texel.y)) - dispHeight(uvH - vec2(0.0, texel.y))) / (2.0 * texel.y);
        vec3 Tx = mat3(model) * vec3(1.0, 0.0, 0.0);
        vec3 Bz = mat3(model) * vec3(0.0, 0.0, 1.0);
        // World distance per UV unit: plane extent / tiling (guards the divide).
        float Lu = max(length(Tx) / max(u_uvScale[5].x, 1e-3), 1e-4);
        float Lv = max(length(Bz) / max(u_uvScale[5].y, 1e-3), 1e-4);
        nw = normalize(nw - normalize(Tx) * (dhdu * u_dispScale / Lu)
                           - normalize(Bz) * (dhdv * u_dispScale / Lv));
    }

    FragPos = worldPos.xyz;
    Normal = nw;
    ObjColor = aColor;
    TexCoord = aTexCoord;
    vec4 viewPos = view * worldPos;
    viewDepth = -viewPos.z;
    gl_Position = projection * viewPos;
}
