#version 330 core

uniform sampler2D impostorAtlas;
uniform vec2 atlasTiles;     // e.g. (4, 2) for 4×2 grid
uniform vec3 viewPos;
uniform vec3 center;
uniform vec3 sunDir;
uniform vec3 lightColor;
uniform vec3 fogColor;
uniform int useFog;
uniform vec3 realSunDir;

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

    // 4×2 grid layout: columns first, then rows
    vec2 tileSize = 1.0 / atlasTiles;

    // Tile A
    int txA = tileA % 4;
    int tyA = 1 - tileA / 4;  // row 0 at bottom
    vec2 uvA = vUV * tileSize + vec2(txA, tyA) * tileSize;

    // Tile B
    int txB = tileB % 4;
    int tyB = 1 - tileB / 4;
    vec2 uvB = vUV * tileSize + vec2(txB, tyB) * tileSize;

    // Sample and blend
    vec4 colorA = texture(impostorAtlas, uvA);
    vec4 colorB = texture(impostorAtlas, uvB);
    vec4 color = mix(colorA, colorB, blend);

    // Discard fully transparent pixels
    if (color.a < 0.05) discard;

    // Apply simple lighting from pre-rendered albedo
    // (The impostor atlas stores pre-lit final color)
    // Just apply fog
    if (useFog == 1)
    {
        float dist = length(viewPos - center);
        float fogDensity = 0.01;
        float fogFactor = exp(-pow(dist * fogDensity, 2.0));
        fogFactor = clamp(fogFactor, 0.0, 1.0);
        color.rgb = mix(fogColor, color.rgb, fogFactor);
    }

    FragColor = vec4(color.rgb, color.a);
}
