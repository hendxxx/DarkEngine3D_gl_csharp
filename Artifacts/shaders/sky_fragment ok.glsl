#version 400 core
out vec4 FragColor;

in vec3 TexCoords;

uniform vec3 fogColor;
uniform vec3 sunDir;
uniform vec3 time;
uniform sampler2D moonTex;

// Threshold FBM (range 0.15–0.65)
const vec2 configCerah          = vec2(0.18, 0.38);
const vec2 configSedikitMendung = vec2(0.14, 0.32);
const vec2 configMendungSekali  = vec2(0.08, 0.25);

uniform float weatherMode; // 0.0 = cerah, 0.5 = sedang, 1.0 = mendung

float hash(float n) { return fract(sin(n) * 43758.5453123); }

float noise(vec3 x) {
    vec3 p = floor(x);
    vec3 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    float n = p.x + p.y * 57.0 + 113.0 * p.z;
    return mix(
        mix(mix(hash(n + 0.0), hash(n + 1.0), f.x),
            mix(hash(n + 57.0), hash(n + 58.0), f.x), f.y),
        mix(mix(hash(n + 113.0), hash(n + 114.0), f.x),
            mix(hash(n + 170.0), hash(n + 171.0), f.x), f.y), f.z
    );
}

// FBM 5 OCTAVE (AWAN FLUFFY)
float fbm(vec3 p)
{
    float f = 0.0;
    float w = 0.5;
    for (int i = 0; i < 5; i++)
    {
        f += w * noise(p);
        p *= 2.0;
        w *= 0.5;
    }
    return f;
}

void main()
{
    vec3 viewDir = normalize(TexCoords);
    vec3 lightDir = normalize(sunDir);
    float sunY = lightDir.y;

    // SKY GRADIENT
    float tSunset = clamp((sunY - 0.0) / (0.65 - 0.0), 0.0, 1.0);
    float tNight  = smoothstep(-0.25, 0.05, sunY);
    float tMalam  = 1.0 - smoothstep(-0.3, 0.1, sunY);

    vec3 noonSky      = vec3(0.4, 0.6, 0.85);
    vec3 noonCloud    = vec3(1.0);
    vec3 sunsetColor  = vec3(1.0, 0.48, 0.25);
    vec3 nightColor   = vec3(0.06, 0.06, 0.10);

    vec3 currentSkyColor = mix(sunsetColor * 0.6, noonSky, tSunset);
    currentSkyColor      = mix(nightColor * 0.4, currentSkyColor, tNight);

    float up = max(viewDir.y, 0.0);
    vec3 skyBase = mix(fogColor * 0.5, currentSkyColor, up);

    vec3 dayToSunsetColor = mix(sunsetColor, noonCloud, tSunset);
    vec3 cloudBaseColor   = mix(nightColor, dayToSunsetColor, tNight);

    // WEATHER THRESHOLD
    float cutMin = mix(configCerah.x, configMendungSekali.x, weatherMode);
    float cutMax = mix(configCerah.y, configMendungSekali.y, weatherMode);

    // CLOUDS
    float cloudAlpha = 0.0;
    vec3 finalCloudColor = vec3(0.0);

    if (viewDir.y > 0.0)
    {
        float cloudPlaneHeight = 2.5;
        float distToPlane = cloudPlaneHeight / max(viewDir.y, 0.25);
        vec3 cloudPos = viewDir * distToPlane;

        vec3 p1 = cloudPos;
        p1.xz *= 0.45;
        p1.x += time.x * 0.04;
        p1.z += time.x * 0.01;

        float n1 = fbm(p1 * 1.2);
        float c1 = smoothstep(cutMin * 0.25, cutMax * 1.35, n1);

        float horizonFade = smoothstep(0.0, 0.22, viewDir.y);

        float shadow = fbm(p1 * 0.5);
        shadow = smoothstep(0.2, 0.8, shadow);

        float scatter = max(dot(lightDir, viewDir), 0.0);
        scatter = pow(scatter, 6.0);

        vec3 cloudColor1 = cloudBaseColor;
        cloudColor1 *= mix(0.6, 1.0, shadow);
        cloudColor1 += vec3(1.0, 0.8, 0.5) * scatter * 0.4;

        // Cirrus
        vec3 p2 = cloudPos * 0.25;
        p2.x += time.x * 0.02;
        p2.z += time.x * 0.015;

        float n2 = fbm(p2 * 2.0);
        float cirrusAlpha = smoothstep(0.6, 0.8, n2);

        vec3 cloudColor2 = mix(vec3(1.0), sunsetColor, 0.3);

        cloudAlpha = c1 * horizonFade;
        finalCloudColor = cloudColor1;
        finalCloudColor = mix(finalCloudColor, cloudColor2, cirrusAlpha * 0.25);

        float depth = fbm(p1 * 0.8);
        finalCloudColor *= 0.8 + depth * 0.2;

        cloudAlpha = max(cloudAlpha, 0.0001);
    }

    // CLOUD THICKNESS
    cloudAlpha *= mix(0.15, 1.8, weatherMode);

    // SUN PREP
    vec3 skyWithCelestial = skyBase;
    float sunDot = dot(viewDir, lightDir);
    float sunVisible = smoothstep(-0.1, 0.1, sunY);

    // safer sunBlocker and stronger glow retention
    float sunBlocker = mix(0.05, 0.25, weatherMode);
    float sunGlowBoost = mix(2.0, 0.95, weatherMode);

    // adjusted sun colors (less yellow)
    float sunHeightFactor = clamp(smoothstep(0.0, 0.35, sunY), 0.0, 1.0);
    vec3 sunColorLow  = vec3(1.12, 0.95, 0.85);
    vec3 sunColorHigh = vec3(1.03, 1.00, 0.98);
    vec3 dynamicSunCore = mix(sunColorLow, sunColorHigh, sunHeightFactor);

    vec3 glowColorLow  = vec3(1.0, 0.88, 0.78);
    vec3 glowColorHigh = vec3(1.0, 0.96, 0.94);
    vec3 dynamicSunGlow = mix(glowColorLow, glowColorHigh, sunHeightFactor);

    vec3 dynamicBloomOuter = mix(vec3(0.9,0.6,0.35), vec3(0.9,0.75,0.6), sunHeightFactor);

    float sunCoreMask = 0.0;
    float sunGlowMask = 0.0;
    float sunBloomMask = 0.0;

    // prepare sunUV and distance for sun-hole calculations
    vec2 sunUV = vec2(0.0);
    float d = 1000.0;

    if (sunDot > 0.0)
    {
        vec3 sunUpVec = abs(lightDir.y) > 0.999 ? vec3(0,0,1) : vec3(0,1,0);
        vec3 sunRight = normalize(cross(sunUpVec, lightDir));
        vec3 sunUpAxis = cross(lightDir, sunRight);

        sunUV = vec2(dot(viewDir, sunRight), dot(viewDir, sunUpAxis)) / sunDot;
        d = length(sunUV);

        const float sunAngularRadius = 0.025;
        sunCoreMask = smoothstep(sunAngularRadius, sunAngularRadius * 0.75, d);

        sunGlowMask  = exp(-d * d * 250.0);
        sunBloomMask = exp(-d * d * 500.0);
    }

    // SUN HOLE (soft, partial) — avoid hard ring
    float sunHoleMax = mix(0.0, 0.60, weatherMode); // max partial reduction
    float sunHoleRadius = 0.032;
    float sunHoleSoft = 0.014;
    float sunHoleMask = smoothstep(sunHoleRadius + sunHoleSoft, sunHoleRadius - sunHoleSoft, d);

    // apply partial reduction to cloudAlpha but keep a floor
    cloudAlpha = max(cloudAlpha * (1.0 - sunHoleMask * sunHoleMax), 0.03);

    // add sun core & glow into skyWithCelestial (core colored)
    skyWithCelestial += dynamicSunCore * sunCoreMask * 4.0 * sunVisible * sunBlocker;
    skyWithCelestial += dynamicSunGlow * sunGlowMask * 0.6 * sunVisible * sunGlowBoost;
    skyWithCelestial += dynamicBloomOuter * sunBloomMask * 1.2 * sunVisible * sunGlowBoost;

    // additive sun pass AFTER cloudAlpha reduction to cover soft edges
    // stronger additive at day, slightly reduced at heavy clouds
    float sunAddFactor = mix(1.0, 0.6, weatherMode);
    vec3 sunAdd = dynamicSunGlow * sunGlowMask * 0.6 * sunVisible * sunAddFactor;
    skyWithCelestial += sunAdd;

    // MOON (improved visibility)
    vec3 moonDir = normalize(-lightDir);
    float moonDot = dot(viewDir, moonDir);

    if (moonDot > 0.0 && tMalam > 0.01)
    {
        vec3 moonUpVec = abs(moonDir.y) > 0.999 ? vec3(0,0,1) : vec3(0,1,0);
        vec3 moonRight = normalize(cross(moonUpVec, moonDir));
        vec3 moonUpAxis = cross(moonDir, moonRight);

        vec2 moonUV = vec2(dot(viewDir, moonRight), dot(viewDir, moonUpAxis)) / moonDot;
        float md = length(moonUV);

        // moon glow independent of sunBlocker and cloudAlpha (so visible behind clouds)
        float moonGlowMask = exp(-md * md * 60.0);
        vec3 moonGlow = vec3(0.36, 0.46, 0.95) * moonGlowMask * 1.0 * tMalam;
        // additive glow first
        skyWithCelestial += moonGlow;

        const float moonAngularRadius = 0.0283;
        float localR = md / moonAngularRadius;

        if (localR <= 1.02)
        {
            vec2 finalMoonUV = (moonUV / moonAngularRadius) * 0.5 + vec2(0.5);
            vec3 textureMoonColor = texture(moonTex, finalMoonUV).rgb;

            float brightness = dot(textureMoonColor, vec3(0.2126, 0.7152, 0.0722));
            float customAlpha = smoothstep(0.02, 0.1, brightness);

            float softEdgeMask = smoothstep(1.0, 0.92, localR);

            vec3 litMoonColor = textureMoonColor * vec3(1.0, 0.98, 0.95) * 1.8 * tMalam;
            litMoonColor += moonGlow * 0.45;

            // moon visibility factor: keep high even in heavy clouds
            float moonVisibilityFactor = mix(0.95, 0.7, weatherMode);
            float finalMoonAlpha = customAlpha * moonVisibilityFactor * softEdgeMask;

            // blend moon into skyWithCelestial (mix so it overlays naturally)
            skyWithCelestial = mix(skyWithCelestial, litMoonColor, finalMoonAlpha);
        }
    }

    // FINAL MIX
    vec3 result = mix(skyWithCelestial, finalCloudColor, cloudAlpha);

    // small gamma/tonemap tweak to avoid oversaturation
    result = pow(result, vec3(1.0 / 1.1));

    FragColor = vec4(result, 1.0);
}
