#version 330 core

uniform sampler2D impostorAtlas;
uniform vec2 atlasTiles;     // e.g. (8, 1) for 8×1 horizontal strip (matches baking)
uniform vec3 viewPos;
uniform vec3 center;
uniform vec3 sunDir;
uniform vec3 lightColor;
uniform vec3 fogColor;
uniform int useFog;
uniform vec3 realSunDir;
uniform float debugOpacity;   // 1.0 = opaque (normal), <1.0 = semi-transparent (debug)

in vec2 vUV;

out vec4 FragColor;

void main()
{
    // Compute horizontal angle from camera to center
    vec3 toCenter = normalize(center - viewPos);
    float angle = atan(toCenter.z, toCenter.x);   // -PI to PI

    // Map angle [-PI, PI] → [0, 8) to select from 8 views
    float tileF = (angle / 3.14159265) * 0.5 + 0.5;  // 0..1
    tileF = tileF * 8.0;                              // 0..8

    // Blend between the two nearest tiles
    int tileA = int(mod(floor(tileF), 8.0));
    int tileB = int(mod(ceil(tileF), 8.0));
    float blend = fract(tileF);

    // Horizontal strip layout: 8 columns × 1 row
    // Matches how BuildImpostors() renders views side-by-side
    vec2 tileSize = 1.0 / atlasTiles;

    // Tile A — horizontal strip (row 0, column = tile index)
    int txA = tileA;
    int tyA = 0;
    vec2 uvA = vUV * tileSize + vec2(txA, tyA) * tileSize;

    // Tile B — horizontal strip (row 0, column = tile index)
    int txB = tileB;
    int tyB = 0;
    vec2 uvB = vUV * tileSize + vec2(txB, tyB) * tileSize;

    // Sample and blend between the two nearest tiles
    vec4 colorA = texture(impostorAtlas, uvA);
    vec4 colorB = texture(impostorAtlas, uvB);
    vec4 color = mix(colorA, colorB, blend);

    // Discard fully transparent pixels
    if (color.a < 0.05) discard;

    // ── Alpha un-premultiply: fix dark halos ──
    // The atlas background is black (0,0,0,0). Bilinear texture filtering blends
    // tree-edge pixels with the black background, producing a dark translucent edge.
    // We reconstruct the original color by dividing RGB by alpha.
    // This eliminates the "dark halo" artifact around tree crowns and leaves.
    if (color.a < 0.95)
        color.rgb = color.rgb / max(color.a, 0.001);
    color.a = 1.0;  // Fully opaque after reconstruction

    // ── Use baked atlas directly (no dynamic brightness/tint) ──
    // The atlas was baked with a 45° sun and warm light color (0.95, 0.93, 0.88).
    // Rendering it as-is preserves the original rich baked shading exactly.
    // Dynamic brightness/tint adjustments made the impostor look darker than
    // the original trees, which is the most noticeable artifact at distance.

    // ── Fog ──
    if (useFog == 1)
    {
        float dist = length(viewPos - center);
        float fogDensity = 0.01;
        float fogFactor = exp(-pow(dist * fogDensity, 2.0));
        fogFactor = clamp(fogFactor, 0.0, 1.0);
        color.rgb = mix(fogColor, color.rgb, fogFactor);
    }

    FragColor = vec4(color.rgb, debugOpacity);
}
