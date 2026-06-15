#version 400 core

in vec2 vUV;
out vec4 FragColor;

uniform sampler2D uSceneTex;
uniform float uTime;
uniform float uRainAmount;

// ------------------------------------------------------------
// Hash & Noise
// ------------------------------------------------------------
float hash(vec2 p) {
    return fract(sin(dot(p, vec2(27.1, 91.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);

    float a = hash(i);
    float b = hash(i + vec2(1,0));
    float c = hash(i + vec2(0,1));
    float d = hash(i + vec2(1,1));

    vec2 u = f*f*(3.0 - 2.0*f);

    return mix(mix(a,b,u.x), mix(c,d,u.x), u.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for(int i=0; i<5; i++) {
        v += a * noise(p);
        p *= 2.0;
        a *= 0.5;
    }
    return v;
}

// ------------------------------------------------------------
// Circle mask
// ------------------------------------------------------------
float circleMask(vec2 uv, float scale)
{
    vec2 c = fract(uv * scale) - 0.5;
    float d = length(c);
    return smoothstep(0.35, 0.0, d);
}

// ------------------------------------------------------------
// Droplet Heightfield
// ------------------------------------------------------------
float dropletField(vec2 uv)
{
    float n1 = fbm(uv * 8.0 + vec2(0, uTime * 0.1));
    float d1 = smoothstep(0.55, 0.80, n1);

    float n2 = fbm(uv * 14.0 + vec2(0, uTime * 0.15));
    float d2 = smoothstep(0.50, 0.75, n2);

    float c1 = circleMask(uv, 8.0);
    float c2 = circleMask(uv, 14.0);

    float droplets = d1 * 0.65 + d2 * 0.45;
    float circles  = c1 * 0.55 + c2 * 0.35;

    return clamp(droplets * 0.6 + circles * 0.7, 0.0, 1.0);
}

// ------------------------------------------------------------
// Flow Map
// ------------------------------------------------------------
vec2 flow(vec2 uv) {
    float f = fbm(uv * 3.0 + vec2(0, uTime * 0.4));
    return vec2(0.0, -f * 0.05);
}

// ------------------------------------------------------------
// Normal Map
// ------------------------------------------------------------
vec3 getNormal(vec2 uv) {
    float h = dropletField(uv);
    float hx = dropletField(uv + vec2(0.002, 0.0)) - h;
    float hy = dropletField(uv + vec2(0.0, 0.002)) - h;
    return normalize(vec3(-hx, -hy, 1.0));
}

// ------------------------------------------------------------
// Main
// ------------------------------------------------------------
void main()
{
    vec3 scene = texture(uSceneTex, vUV).rgb;

    // ============================================================
    // EARLY EXIT — kalau uRainAmount == 0 → langsung return scene
    // ============================================================
    if (uRainAmount <= 0.0001)
    {
        FragColor = vec4(scene, 1.0);
        return;
    }

    // apply flow
    vec2 uvFlow = vUV + flow(vUV) * uRainAmount;

    // heightfield mask
    float mask = dropletField(uvFlow);

    // normal map
    vec3 N = getNormal(uvFlow);

    // refraction
    vec2 offset = N.xy * 0.2 * uRainAmount;
    vec3 refracted = texture(uSceneTex, vUV + offset).rgb;

    // blend
    float intensity = pow(mask, 0.7) * uRainAmount;

    vec3 finalColor = mix(scene, refracted, intensity);
    
    // highlight
    finalColor += vec3(0.1) * mask;

    FragColor = vec4(finalColor, 1.0);
}
