#version 400 core
out vec4 FragColor;

in vec3 TexCoords;

uniform vec3 fogColor;
uniform vec3 sunDir;
uniform vec3 time;
uniform sampler2D moonTex;
uniform float exposureState;
uniform float sunBlocked;
uniform float weatherMode;

// ===============================
// HASH + NOISE + FBM
// ===============================
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

// ===============================
// STAR FIELD
// ===============================
float hash2D(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float starNoise(vec2 uv)
{
    vec2 gv = fract(uv) - 0.5;
    vec2 id = floor(uv);

    float n = hash2D(id);

    if (n > 0.994)
    {
        float d = length(gv);
        float star = smoothstep(0.035, 0.0, d);

        float twFreq  = 0.03 + n * 0.4;
        float twPhase = n * 200.0;

        float tw = sin(time.x * twFreq + twPhase) * 0.3 + 0.2;

        return star * tw * 1.1;
    }
    return 0.0;
}

// ===============================
// ATMOSPHERIC SCATTERING
// ===============================
vec3 totalAtmosphereColor(vec3 viewDir, vec3 sunDirection, float sunIntensity)
{
    const float PI = 3.14159265;
    const float HR = 8.0;
    const float HM = 1.2;
    const vec3 betaR = vec3(5.8e-6, 13.5e-6, 33.1e-6);
    const float betaM = 21e-6;
    const float g = 0.76;

    float cosViewUp = max(dot(normalize(viewDir), vec3(0.0, 1.0, 0.0)), 0.001);
    float rayleighLength = exp(-1.0 / HR) / cosViewUp;
    float mieLength = exp(-1.0 / HM) / cosViewUp;

    float mu = clamp(dot(normalize(viewDir), normalize(sunDirection)), -1.0, 1.0);
    float phaseR = (3.0 / (16.0 * PI)) * (1.0 + mu * mu);
    float phaseM = (3.0 / (8.0 * PI)) * ((1.0 - g * g) / pow(1.0 + g * g - 2.0 * g * mu, 1.5));

    vec3 tau = betaR * rayleighLength + vec3(betaM * mieLength);

    vec3 trans = exp(-tau);

    vec3 Lr = betaR * phaseR * sunIntensity * trans;
    vec3 Lm = vec3(betaM) * phaseM * sunIntensity * trans;

    vec3 sky = Lr + Lm;

    sky *= 1.6;
    sky = sky / (sky + vec3(1.0));
    return pow(sky, vec3(1.0 / 1.1));
}

// ===============================
// SKY COLOR AT SUN DIRECTION
// ===============================
vec3 GetSkyColorAtDirection(vec3 dir)
{
    float sunY = dir.y;

    float tSunset = clamp((sunY - 0.0) / (0.65 - 0.0), 0.0, 1.0);
    float tNight  = smoothstep(-0.25, 0.05, sunY);

    vec3 noonSky      = vec3(0.4, 0.6, 0.85);
    vec3 sunsetColor  = vec3(1.0, 0.48, 0.25);
    vec3 nightColor   = vec3(0.06, 0.06, 0.10);

    float sunIntensity = clamp(sunY * 1.5 + 0.5, 0.0, 2.0);

    vec3 atmosphereSky = totalAtmosphereColor(dir, sunDir, sunIntensity);

    vec3 sunsetBlend = mix(sunsetColor * 0.6, noonSky, tSunset);
    vec3 horizonTint = mix(nightColor * 0.4, sunsetBlend, tNight);

        float up = max(dir.y, 0.0);
    vec3 skyBase = mix(fogColor * 0.5, mix(atmosphereSky, horizonTint, 0.35), up);

    return skyBase;
}

// ===============================
// MAIN
// ===============================
void main()
{
    vec3 viewDir = normalize(TexCoords);
    vec3 lightDir = normalize(sunDir);
    float sunY = lightDir.y;

    float tSunset = clamp((sunY - 0.0) / (0.65 - 0.0), 0.0, 1.0);
    float tNight  = smoothstep(-0.25, 0.05, sunY);
    float tMalam  = 1.0 - smoothstep(-0.3, 0.1, sunY);

    // ===============================
    // SUN FACTOR — MATIKAN MATAHARI SAAT MALAM
    // ===============================
    float sunFactor = 1.0 - tMalam;   // 1 = siang, 0 = malam

    vec3 noonSky      = vec3(0.4, 0.6, 0.85);
    vec3 sunsetColor  = vec3(1.0, 0.48, 0.25);
    vec3 nightColor   = vec3(0.06, 0.06, 0.10);

    float sunIntensity = clamp(sunY * 1.5 + 0.5, 0.0, 2.0);

    vec3 atmosphereSky = totalAtmosphereColor(viewDir, lightDir, sunIntensity);

    vec3 sunsetBlend = mix(sunsetColor * 0.6, noonSky, tSunset);
    vec3 horizonTint = mix(nightColor * 0.4, sunsetBlend, tNight);

    float up = max(viewDir.y, 0.0);
    vec3 skyBase = mix(fogColor * 0.5, mix(atmosphereSky, horizonTint, 0.35), up);

    // ===============================
    // WEATHER PRESETS
    // ===============================
    float weather = weatherMode;

    float densityBoost = mix(0.55, 1.65, weather);
    float erosionBoost = mix(0.65, 0.15, weather);

    float cutMin = mix(0.18, 0.05, weather);
    float cutMax = mix(0.32, 0.20, weather);

    float shadowStrength = mix(0.25, 1.25, weather);
    float cirrusStrength = mix(1.0, 0.10, weather);

    vec3 cloudTint = mix(
        vec3(0.88, 0.92, 1.00),
        vec3(0.95, 0.95, 0.95),
        weather
    );
    cloudTint = mix(cloudTint, vec3(0.55, 0.58, 0.60), weather * 0.85);

    // ===============================
    // STRONG DARKEN FOR NIGHT + OVERCAST
    // ===============================
    float nightDark = tMalam * weather;

    skyBase = mix(skyBase, skyBase * 0.18, nightDark);

    float scatterReduce = mix(1.0, 0.05, nightDark);

    float sunGlowBoost = mix(2.0, 0.95, weather);
    sunGlowBoost = mix(sunGlowBoost, sunGlowBoost * 0.10, nightDark);

    cloudTint = mix(cloudTint, cloudTint * 0.35, nightDark);

    // ===============================
    // CLOUDS (WARPED FBM + EROSION + MULTI-LAYER)
    // ===============================
    float cloudAlpha = 0.0;
    vec3 finalCloudColor = vec3(0.0);

    if (viewDir.y > 0.0)
    {
        float cloudPlaneHeight = 2.5;
        float distToPlane = cloudPlaneHeight / max(viewDir.y, 0.25);
        vec3 cloudPos = viewDir * distToPlane;

        vec3 p = cloudPos;
        p.xz *= 0.42;
        p.x += time.x * 0.035;
        p.z += time.x * 0.012;

        vec3 warp = vec3(
            fbm(p * 0.9),
            fbm(p * 1.3),
            fbm(p * 0.7)
        );
        p += warp * 0.35;

        float base = fbm(p * 1.25);
        float detail = fbm(p * 3.5);
        float density = mix(base, detail, 0.35) * densityBoost;

        float erosion = fbm(p * 2.0) * erosionBoost;
        density = smoothstep(cutMin * 0.55, cutMax * 1.45, density - erosion * 0.25);

        float horizonFade = smoothstep(0.0, 0.22, viewDir.y);

        float shadow = fbm(p * 0.55);
        shadow = smoothstep(0.25, 0.85, shadow);
        shadow = mix(1.0, shadow, shadowStrength);

        float scatter = max(dot(lightDir, viewDir), 0.0);
        scatter = pow(scatter, 6.0) * scatterReduce * sunFactor;

        vec3 lightTint = mix(sunsetColor, vec3(1.0), tSunset);

        vec3 cloudLit = cloudTint;
        cloudLit *= shadow;
        cloudLit += lightTint * scatter * mix(0.25, 0.05, weather);

        // CIRRUS — tetap ada, tapi highlight mati saat malam
        vec3 pCirrus = cloudPos * 0.22;
        pCirrus.x += time.x * 0.018;
        pCirrus.z += time.x * 0.014;

        float cir = fbm(pCirrus * 2.2);
        float cirAlpha = smoothstep(0.62, 0.82, cir) * cirrusStrength;

        vec3 cirColor = mix(vec3(1.0), sunsetColor, 0.25);
        cirColor *= sunFactor;   // cirrus tidak dapat cahaya matahari saat malam

        finalCloudColor = mix(cloudLit, cirColor, cirAlpha * 0.22 * sunFactor);

        float depth = fbm(p * 0.8);
        finalCloudColor *= 0.82 + depth * 0.18;

        cloudAlpha = density * horizonFade;
        cloudAlpha = max(cloudAlpha, 0.0001);
    }

    cloudAlpha *= mix(0.22, 1.75, weatherMode);

    // ===============================
    // SUN + MOON + STARS
    // ===============================
    vec3 skyWithCelestial = skyBase;

    float sunDot = dot(viewDir, lightDir);
    float sunVisible = smoothstep(-0.1, 0.1, sunY);

    float sunBlocker = mix(0.05, 0.25, weatherMode);

    float sunHeightFactor = clamp(smoothstep(0.0, 0.35, sunY), 0.0, 1.0);
    vec3 sunColorLow  = vec3(1.12, 0.95, 0.85);
    vec3 sunColorHigh = vec3(1.03, 1.00, 0.98);
    vec3 dynamicSunCore = mix(sunColorLow, sunColorHigh, sunHeightFactor);

    vec3 glowColorLow  = vec3(1.0, 0.88, 0.78);
    vec3 glowColorHigh = vec3(1.0, 0.96, 0.94);
    vec3 dynamicSunGlow = mix(glowColorLow, glowColorHigh, sunHeightFactor);

    float sunCoreMask = 0.0;
    float sunGlowMask = 0.0;
    float sunBloomMask = 0.0;

    vec2 sunUV = vec2(0.0);
    float d = 1000.0;
    const float sunAngularRadius = 0.025;

    if (sunDot > 0.0)
    {
        vec3 sunUpVec = abs(lightDir.y) > 0.999 ? vec3(0,0,1) : vec3(0,1,0);
        vec3 sunRight = normalize(cross(sunUpVec, lightDir));
        vec3 sunUpAxis = cross(lightDir, sunRight);

        sunUV = vec2(dot(viewDir, sunRight), dot(viewDir, sunUpAxis)) / sunDot;
        d = length(sunUV);

        sunCoreMask = smoothstep(sunAngularRadius, sunAngularRadius * 0.75, d);
        sunGlowMask  = exp(-d * d * 250.0);
        sunBloomMask = exp(-d * d * 500.0);
    }

    vec3 sunSkyColor = GetSkyColorAtDirection(lightDir);
    vec3 overColor = sunSkyColor * sunFactor;

    skyWithCelestial += dynamicSunCore * sunCoreMask * 4.0 * sunVisible * sunBlocker * sunFactor;
    skyWithCelestial += overColor * sunGlowMask * 1.4 * sunVisible * sunGlowBoost * sunFactor;
    skyWithCelestial += overColor * sunBloomMask * 2.8 * sunVisible * sunGlowBoost * sunFactor;

    // MOON
    vec3 moonDir = normalize(-lightDir);
    float moonDot = dot(viewDir, moonDir);

    if (moonDot > 0.0 && tMalam > 0.01)
    {
        vec3 moonUpVec = abs(moonDir.y) > 0.999 ? vec3(0,0,1) : vec3(0,1,0);
        vec3 moonRight = normalize(cross(moonUpVec, moonDir));
        vec3 moonUpAxis = cross(moonDir, moonRight);

        vec2 moonUV = vec2(dot(viewDir, moonRight), dot(viewDir, moonUpAxis)) / moonDot;
        float md = length(moonUV);

        float moonGlowMask = exp(-md * md * 60.0);
        vec3 moonGlow = vec3(0.36, 0.46, 0.95) * moonGlowMask * 1.0 * tMalam;

        // moon glow tidak menerangi cirrus terlalu kuat
        moonGlow *= mix(1.0, 0.35, weatherMode);

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

            float moonVisibilityFactor = mix(0.95, 0.7, weatherMode);
            float finalMoonAlpha = customAlpha * moonVisibilityFactor * softEdgeMask;

            skyWithCelestial = mix(skyWithCelestial, litMoonColor, finalMoonAlpha);
        }
    }

    // STARS
    float nightFactor = tMalam;
    float starMask = smoothstep(0.0, 0.25, nightFactor);

    if (viewDir.y > 0.0)
    {
        vec2 starUV = viewDir.xz * 150.0;
        float stars = starNoise(starUV);
        vec3 starColor = vec3(1.0, 0.96, 0.88) * stars * starMask;
        skyWithCelestial += starColor;
    }

    // FINAL MIX
    vec3 result = mix(skyWithCelestial, finalCloudColor, cloudAlpha);

    FragColor = vec4(result, 1.0);
}
