#version 400 core
out vec4 FragColor;

in vec3 TexCoords;

// ── Camera / Time ──
uniform vec3 fogColor;
uniform vec3 sunDir;
uniform vec3 time;
uniform float weatherMode;
uniform float u_aspectRatio;

// ── Sun Settings ──
uniform float sunSize;
uniform float sunSoftness;
uniform vec3  sunColor;
uniform float sunGlowIntensity;

// ── Atmospheric Scattering ──
uniform float scatteringIntensity;
uniform float rayleighStrength;
uniform vec3  rayleighColor;
uniform float rayleighHeight;
uniform float mieStrength;
uniform vec3  mieColor;
uniform float mieFocus;
uniform float mieHeight;

// ── Volumetric Clouds ──
uniform float cloudDensity;
uniform float cloudAltitude;
uniform float cloudSpeed;
uniform float cloudDetail;
uniform float cloudErosion;
uniform float cloudShadowStrength;
uniform float cloudScatter;
uniform vec3  cloudTintColor;
uniform float cirrusStrength;
uniform float cloudsEnabled;

// ── Moon ──
uniform sampler2D moonTex;
uniform float moonBrightness;
uniform float moonSize;
uniform float moonGlowRadius;
uniform vec3  moonTintColor;
uniform float moonPhaseOffset;
uniform float moonRotationSpeed;

// ── Stars ──
uniform float starBrightness;
uniform float starDensity;
uniform float starTwinkleSpeed;
uniform vec3  starColor;
uniform float starsEnabled;

// ── Eclipses ──
uniform float solarEclipse;
uniform float lunarEclipse;
uniform vec3  eclipseGlowColor;

// ── Sun Rays ──
uniform float sunRayIntensity;
uniform float sunRayCount;
uniform float sunRayLength;
uniform vec3  sunRayColor;
uniform float sunRaysEnabled;

// ═══════════════════════════════════════════════
// HASH + NOISE + FBM
// ═══════════════════════════════════════════════
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

// ═══════════════════════════════════════════════
// ATMOSPHERIC SCATTERING (parameterized)
// ═══════════════════════════════════════════════
vec3 atmosphericScattering(vec3 viewDir, vec3 sDir, float sunInt)
{
    const float PI = 3.14159265;

    float HR = rayleighHeight;
    float HM = mieHeight;
    vec3 betaR = rayleighColor * 33.1e-6 * rayleighStrength;
    float betaM = 21e-6 * mieStrength;
    float g = mieFocus;

    float cosViewUp = max(dot(normalize(viewDir), vec3(0.0, 1.0, 0.0)), 0.001);
    float rayleighLength = exp(-1.0 / HR) / cosViewUp;
    float mieLength = exp(-1.0 / HM) / cosViewUp;

    float mu = clamp(dot(normalize(viewDir), normalize(sDir)), -1.0, 1.0);
    float phaseR = (3.0 / (16.0 * PI)) * (1.0 + mu * mu);
    float phaseM = (3.0 / (8.0 * PI)) * ((1.0 - g * g) / pow(1.0 + g * g - 2.0 * g * mu, 1.5));

    vec3 tau = betaR * rayleighLength + vec3(betaM * mieLength);
    vec3 trans = exp(-tau);

    vec3 Lr = betaR * phaseR * sunInt * trans;
    vec3 Lm = mieColor * betaM * phaseM * sunInt * trans;

    vec3 sky = Lr + Lm;
    // Scale from physical radiance (~1e-5) to visible range before Reinhard
    sky *= scatteringIntensity * 40000.0;
    sky = sky / (sky + vec3(1.0));
    return pow(sky, vec3(1.0 / 1.1));
}

// ═══════════════════════════════════════════════
// STAR FIELD
// ═══════════════════════════════════════════════
float hash2D(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float starNoise(vec2 uv)
{
    vec2 gv = fract(uv) - 0.5;
    vec2 id = floor(uv);

    float n = hash2D(id);

    if (n > (1.0 - starDensity * 0.006))
    {
        float d = length(gv);
        float star = smoothstep(0.035, 0.0, d);

        float tw = sin(time.x * starTwinkleSpeed * (0.5 + n * 2.0)) * 0.3 + 0.7;

        return star * tw;
    }
    return 0.0;
}

// ═══════════════════════════════════════════════
// SUN RAYS (god rays from sun position)
// ═══════════════════════════════════════════════
float sunRays(vec3 viewDir, vec3 sDir)
{
    if (sunRaysEnabled < 0.5) return 0.0;

    float sunDot = dot(viewDir, normalize(sDir));
    if (sunDot <= 0.0) return 0.0;

    vec3 sunUpVec = abs(sDir.y) > 0.999 ? vec3(0,0,1) : vec3(0,1,0);
    vec3 sunRight = normalize(cross(sunUpVec, sDir));
    vec3 sunUpAxis = cross(sDir, sunRight);

    vec2 sunUV = vec2(dot(viewDir, sunRight), dot(viewDir, sunUpAxis)) / max(sunDot, 0.001);
    float dist = length(sunUV);

    float rays = 0.0;
    float angle = atan(sunUV.y, sunUV.x);

    // Create spoke pattern
    float spoke = 0.0;
    float count = max(sunRayCount, 3.0);
    for (float i = 0.0; i < 16.0; i += 1.0)
    {
        if (i >= count) break;
        float a = (i / count) * 6.2831853;
        float rayAngle = abs(mod(angle - a + 3.14159265, 6.2831853) - 3.14159265);
        float rayProfile = exp(-rayAngle * 3.0) * exp(-dist * (2.0 / max(sunRayLength, 0.1)));
        spoke += rayProfile;
    }

    rays = spoke * sunRayIntensity;
    return max(rays, 0.0);
}

// ═══════════════════════════════════════════════
// LIGHTNING BOLT
// ═══════════════════════════════════════════════
float sdSegment(vec2 p, vec2 a, vec2 b)
{
    vec2 pa = p - a;
    vec2 ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

float lightningBoltShape(vec2 uv, float seed)
{
    float d = 1e6;
    float r1 = fract(sin(seed * 12.345) * 54321.123);
    float r2 = fract(sin(seed * 45.678) * 98765.321);

    vec2 p0 = vec2(0.0,  0.8);
    vec2 p1 = vec2(0.1 * r1,  0.5);
    vec2 p2 = vec2(-0.1 * r2, 0.2);
    vec2 p3 = vec2(0.15 * r1, -0.1);
    vec2 p4 = vec2(0.0, -0.4);

    d = min(d, sdSegment(uv, p0, p1));
    d = min(d, sdSegment(uv, p1, p2));
    d = min(d, sdSegment(uv, p2, p3));
    d = min(d, sdSegment(uv, p3, p4));

    float bolt = exp(-d * 35.0);
    float b1 = sdSegment(uv, p1, p1 + vec2(0.15, 0.25));
    float b2 = sdSegment(uv, p2, p2 + vec2(-0.12, 0.18));
    bolt += exp(-b1 * 60.0) * 0.5;
    bolt += exp(-b2 * 60.0) * 0.4;
    return bolt;
}

float lightningFlash(float t, float weather)
{
    float slowT = t * 0.000005;
    float storm = smoothstep(0.5, 0.7, weather);
    if (storm <= 0.0) return 0.0;

    float r = fract(sin(slowT * 5.123) * 98765.4321);
    if (r > 0.998)
    {
        float strikeCount = 2.0 + floor(fract(sin(slowT * 3.77) * 24680.135) * 2.0);
        float flashTotal = 0.0;
        for (int i = 0; i < 3; i++)
        {
            if (i >= int(strikeCount)) break;
            float delay = fract(sin((slowT + float(i)) * 1.91) * 13579.864) * 0.12;
            float f = fract((t - delay) * 2.0);
            float pre = smoothstep(0.0, 0.06, 0.06 - f);
            float main = smoothstep(0.0, 0.18, 0.18 - f);
            float flicker = smoothstep(0.12, 0.16, 0.16 - f);
            float after = smoothstep(0.20, 0.32, 0.32 - f);
            flashTotal += pre * 0.25 + main * 1.0 + flicker * 0.45 + after * 0.30;
        }
        return flashTotal * storm * 1.25;
    }
    return 0.0;
}

// ═══════════════════════════════════════════════
// SKY COLOR AT SUN DIRECTION
// ═══════════════════════════════════════════════
vec3 GetSkyColorAtDirection(vec3 dir)
{
    float sunY = dir.y;
    float tSunset = clamp((sunY - 0.0) / (0.65 - 0.0), 0.0, 1.0);
    float tNight  = smoothstep(-0.25, 0.05, sunY);

    vec3 noonSky      = vec3(0.4, 0.6, 0.85);
    vec3 sunsetColor  = vec3(1.0, 0.48, 0.25);
    vec3 nightColor   = vec3(0.06, 0.06, 0.10);

    float sunIntensity = clamp(sunY * 1.5 + 0.5, 0.0, 2.0);
    vec3 atmosphereSky = atmosphericScattering(dir, sunDir, sunIntensity);
    vec3 sunsetBlend = mix(sunsetColor * 0.6, noonSky, tSunset);
    vec3 horizonTint = mix(nightColor * 0.4, sunsetBlend, tNight);
    float up = max(dir.y, 0.0);
    return mix(fogColor * 0.5, mix(atmosphereSky, horizonTint, 0.12), up * 0.85 + 0.15);
}

// ═══════════════════════════════════════════════
// SOLAR ECLIPSE
// ═══════════════════════════════════════════════
vec3 applySolarEclipse(vec3 skyWithSun, vec3 viewDir, vec3 sDir, float tMalam)
{
    if (solarEclipse < 0.01) return skyWithSun;

    vec3 moonDir = normalize(-sDir);
    float moonDot = dot(viewDir, moonDir);
    if (moonDot <= 0.0) return skyWithSun;

    vec3 moonUpVec = abs(moonDir.y) > 0.999 ? vec3(0,0,1) : vec3(0,1,0);
    vec3 moonRight = normalize(cross(moonUpVec, moonDir));
    vec3 moonUpAxis = cross(moonDir, moonRight);
    vec2 moonUV = vec2(dot(viewDir, moonRight), dot(viewDir, moonUpAxis)) / moonDot;
    float md = length(moonUV);

    float moonR = 0.0288 * moonSize;
    float offset = moonR * (1.0 - solarEclipse);
    float dist = abs(md - offset);

    if (dist < moonR * 1.5)
    {
        float corona = exp(-dist * 20.0 / moonR);
        float ring = smoothstep(moonR * 0.5, moonR * 0.8, dist) * smoothstep(moonR * 1.5, moonR * 1.0, dist);
        float eclipseGlow = max(corona * solarEclipse, ring * solarEclipse * 0.5);
        skyWithSun += eclipseGlowColor * eclipseGlow * 2.0 * tMalam;
    }

    return skyWithSun;
}

// ═══════════════════════════════════════════════
// LUNAR ECLIPSE
// ═══════════════════════════════════════════════
vec3 applyLunarEclipse(vec3 moonColor, float md, float moonAngularRadius)
{
    if (lunarEclipse < 0.01) return moonColor;

    float normalizedDist = md / moonAngularRadius;
    float bloodFactor = smoothstep(1.0, 0.3, normalizedDist) * lunarEclipse;

    vec3 bloodMoon = moonColor * vec3(1.0, 0.3, 0.1);
    return mix(moonColor, bloodMoon, bloodFactor);
}

// ═══════════════════════════════════════════════
// FACE BLENDING — smooth transition at cube face edges
// ═══════════════════════════════════════════════
vec3 blendFaceDir(vec3 dir) {
    vec3 a = abs(dir);
    float bw = 0.05;
    // Detect proximity to each face boundary pair
    float dXY = abs(a.x - a.y);
    float dXZ = abs(a.x - a.z);
    float dYZ = abs(a.y - a.z);
    float minDist = min(dXY, min(dXZ, dYZ));
    // Blend factor: 1.0 far from edge, 0.0 on edge
    float w = smoothstep(0.0, bw, minDist);
    if (w > 0.99) return dir;
    // Compute a blended direction: average with the dominant-axis-flipped version
    vec3 blended = normalize(dir + vec3(0.002));
    return mix(blended, dir, w);
}

// ═══════════════════════════════════════════════
// MAIN
// ═══════════════════════════════════════════════
void main()
{
    vec3 viewDir = normalize(TexCoords);
    viewDir = blendFaceDir(viewDir);
    vec3 lightDir = normalize(sunDir);
    float sunY = lightDir.y;

    // Init petir
    float lightning = lightningFlash(time.x, weatherMode);

    float tSunset = clamp((sunY - 0.0) / (0.65 - 0.0), 0.0, 1.0);
    float tNight  = smoothstep(-0.25, 0.05, sunY);
    float tMalam  = 1.0 - smoothstep(-0.3, 0.1, sunY);
    float nightOverride = smoothstep(0.1, 0.3, tMalam);
    float sunFactor = 1.0 - tMalam;

    vec3 noonSky      = vec3(0.4, 0.6, 0.85);
    vec3 sunsetColor  = vec3(1.0, 0.48, 0.25);
    vec3 nightColor   = vec3(0.06, 0.06, 0.10);

    float sunIntensity = clamp(sunY * 1.5 + 0.5, 0.0, 2.0);
    vec3 atmosphereSky = atmosphericScattering(viewDir, lightDir, sunIntensity);
    vec3 sunsetBlend = mix(sunsetColor * 0.6, noonSky, tSunset);
    vec3 horizonTint = mix(nightColor * 0.4, sunsetBlend, tNight);

    float up = max(viewDir.y, 0.0);

    // Lightning bolt
    vec2 screenUV = TexCoords.xy / TexCoords.z;
    screenUV = screenUV * 0.5 + 0.5;
    vec2 boltUV = (screenUV - 0.5) * 2.0;
    float smoothRnd = fract(sin(time.x * 0.25) * 54321.123);
    float smoothRnd2 = smoothRnd * 2.0 - 1.0;
    float boltOffsetX = smoothRnd2 * 0.9;
    float boltOffsetY = smoothRnd2 * 0.3;
    boltUV.x += boltOffsetX;
    boltUV.y += 0.4 * boltOffsetY;
    float boltShape = lightningBoltShape(boltUV, 0);
    float bolt = boltShape * lightning;
    float boltFlash = bolt * 1.0;

    // Atmosphere dominates the sky like the reference — reduced fog/horizon tint dilution
    vec3 skyBase = mix(fogColor * 0.5, mix(atmosphereSky, horizonTint, 0.12), up * 0.85 + 0.15);
    skyBase += vec3(1.0, 1.0, 1.2) * lightning * 3.0;

    // Weather
    float weather = weatherMode;
    float densityBoost = mix(0.55, 1.65, weather);
    float erosionBoost = mix(0.65, 0.15, weather);
    float cutMin = mix(0.18, 0.05, weather);
    float cutMax = mix(0.32, 0.20, weather);
    float shadowStr = mix(0.35, 0.65, weather);
    float scatterReduce = mix(1.0, 0.05, tMalam * weather);
    float sunGlowBoost = mix(2.0, 0.95, weather);
    sunGlowBoost = mix(sunGlowBoost, sunGlowBoost * 0.10, tMalam * weather);

    vec3 cloudTint = mix(
        cloudTintColor * vec3(1.0),
        cloudTintColor * vec3(0.55, 0.58, 0.60),
        weather
    );
    cloudTint = mix(cloudTint, cloudTint * 0.1, tMalam * weather);
    skyBase = mix(skyBase, skyBase * 0.45, tMalam * weather);

    // ──── CLOUDS ────
    float cloudAlpha = 0.0;
    vec3 finalCloudColor = vec3(0.0);

    if (cloudsEnabled > 0.5 && viewDir.y > 0.0)
    {
        float distToPlane = cloudAltitude / max(viewDir.y, 0.25);
        vec3 cloudPos = viewDir * distToPlane;

        vec3 p = cloudPos;
        p.xz *= 0.42;
        p.x += time.x * cloudSpeed;
        p.z += time.x * cloudSpeed * 0.34;

        vec3 warp = vec3(
            fbm(p * 0.9),
            fbm(p * 1.3),
            fbm(p * 0.7)
        );
        p += warp * 0.35;

        float base = fbm(p * 1.25);
        float detailN = fbm(p * 3.5);
        float density = mix(base, detailN, cloudDetail) * densityBoost;

        float erosion = fbm(p * 2.0) * cloudErosion;
        density = smoothstep(cutMin * 0.55, cutMax * 1.45, density - erosion * 0.25);

        float horizonFade = smoothstep(0.0, 0.22, viewDir.y);

        float shadow = fbm(p * 0.55);
        shadow = smoothstep(0.25, 0.85, shadow);
        shadow = mix(1.0, shadow, cloudShadowStrength);
        shadow = max(shadow, 0.35);

        float scatter = max(dot(lightDir, viewDir), 0.0);
        scatter = pow(scatter, 6.0) * cloudScatter * sunFactor;

        vec3 lightTint = mix(sunsetColor, vec3(1.0), tSunset);
        lightTint = mix(lightTint, vec3(0.6, 0.7, 1.0), nightOverride);

        vec3 cloudLit = cloudTint;
        cloudLit *= shadow;
        cloudLit += lightTint * scatter * mix(0.25, 0.05, weather);

        // Cirrus
        vec3 pCirrus = cloudPos * 0.22;
        pCirrus.x += time.x * 0.018;
        pCirrus.z += time.x * 0.014;
        float cir = fbm(pCirrus * 2.2);
        float cirAlpha = smoothstep(0.62, 0.82, cir) * cirrusStrength;
        vec3 cirColor = vec3(0.75, 0.80, 1.0) * tMalam;

        finalCloudColor = mix(cloudLit, cirColor, cirAlpha * 0.22 * sunFactor);
        float depth = fbm(p * 0.8);
        finalCloudColor *= 0.82 + depth * 0.18;
        finalCloudColor += vec3(0.85, 0.92, 1.25) * lightning * 1.8;

        cloudAlpha = density * horizonFade;
        cloudAlpha = max(cloudAlpha, 0.0001);
    }

    cloudAlpha *= mix(0.22, 1.75, weatherMode);

    // ──── SUN + MOON + STARS ────
    vec3 skyWithCelestial = skyBase;

    float sunDot = dot(viewDir, lightDir);
    float sunVisible = smoothstep(-0.1, 0.1, sunY);
    float sunBlocker = mix(0.05, 0.25, weatherMode);
    float sunHeightFactor = clamp(smoothstep(0.0, 0.35, sunY), 0.0, 1.0);

    // Dynamic sun color from settings
    vec3 dynamicSunCore = sunColor * mix(1.05, 1.0, sunHeightFactor);
    vec3 dynamicSunGlow = sunColor * mix(0.95, 1.0, sunHeightFactor);

    float sunCoreMask = 0.0;
    float sunGlowMask = 0.0;
    float sunBloomMask = 0.0;
    float sunDiscMask = 0.0;

    vec2 sunUV = vec2(0.0);
    float d = 1000.0;
    float sunAngularRadius = 0.025 * sunSize;

    if (sunDot > 0.0)
    {
        vec3 sunUpVec = abs(lightDir.y) > 0.999 ? vec3(0,0,1) : vec3(0,1,0);
        vec3 sunRight = normalize(cross(sunUpVec, lightDir));
        vec3 sunUpAxis = cross(lightDir, sunRight);

        sunUV = vec2(dot(viewDir, sunRight), dot(viewDir, sunUpAxis)) / sunDot;
        d = length(sunUV);

        float sunVisibility = 1.0 - smoothstep(0.45, 0.75, weatherMode);

        // Sun disc with softness control (0=soft, 1=hard edge)
        float innerEdge = sunAngularRadius * max(1.0 - sunSoftness, 0.0);
        sunDiscMask = 1.0 - smoothstep(innerEdge, sunAngularRadius, d);

        // Sun glow/bloom scale with sunSize so size changes are visually apparent
        float sunGlowFalloff = 250.0 / max(sunSize * sunSize, 0.01);
        float sunBloomFalloff = 500.0 / max(sunSize * sunSize, 0.01);
        sunGlowMask  = exp(-d * d * sunGlowFalloff);
        sunBloomMask = exp(-d * d * sunBloomFalloff);

        sunDiscMask *= sunVisibility;
        sunGlowMask *= sunVisibility;
        sunBloomMask *= sunVisibility;
        skyWithCelestial += vec3(1.0, 0.9, 0.8) * sunGlowMask * 0.15 * sunVisibility;
    }

    vec3 sunSkyColor = GetSkyColorAtDirection(lightDir);
    vec3 overColor = sunSkyColor * sunFactor;

    // Sun disc
    skyWithCelestial += dynamicSunCore * sunDiscMask * 4.0 * sunVisible * sunBlocker * sunFactor;
    // Sun glow
    skyWithCelestial += overColor * sunGlowMask * 1.4 * sunVisible * sunGlowBoost * sunFactor * sunGlowIntensity;
    // Sun bloom
    skyWithCelestial += overColor * sunBloomMask * 2.8 * sunVisible * sunGlowBoost * sunFactor * sunGlowIntensity;

    // ──── SUN RAYS ────
    float rays = sunRays(viewDir, lightDir);
    skyWithCelestial += sunRayColor * rays * sunFactor;

    // ──── SOLAR ECLIPSE ────
    skyWithCelestial = applySolarEclipse(skyWithCelestial, viewDir, lightDir, 1.0 - tMalam);

    // ──── MOON ────
    vec3 moonDir = normalize(-lightDir);
    float moonDot = dot(viewDir, moonDir);

    if (moonDot > 0.0 && tMalam > 0.01)
    {
        vec3 moonUpVec = abs(moonDir.y) > 0.999 ? vec3(0,0,1) : vec3(0,1,0);
        vec3 moonRight = normalize(cross(moonUpVec, moonDir));
        vec3 moonUpAxis = cross(moonDir, moonRight);

        vec2 moonUV = vec2(dot(viewDir, moonRight), dot(viewDir, moonUpAxis)) / moonDot;
        float md = length(moonUV);

        // Moon glow with radius control
        float moonGlowMask = exp(-md * md * (60.0 / max(moonGlowRadius, 0.1)));
        vec3 moonGlow = moonTintColor * moonGlowMask * tMalam;
        moonGlow *= mix(1.0, 0.65, weatherMode);
        moonGlow *= mix(1.0, 0.75, weatherMode);

        float moonOcclusion = exp(-cloudAlpha * 1.5);
        skyWithCelestial += moonGlow;

        // Moon disc with size control
        float moonAngularRadius = 0.0288 * moonSize;
        float localR = md / moonAngularRadius;

        if (localR <= 1.02)
        {
            vec2 finalMoonUV = (moonUV / moonAngularRadius) * 0.5 + vec2(0.5);
            // Apply moon rotation (rotate UV around center)
            if (abs(moonRotationSpeed) > 0.001) {
                float rotAngle = time.x * moonRotationSpeed;
                vec2 rotCenter = vec2(0.5, 0.5);
                vec2 rotUV = finalMoonUV - rotCenter;
                float cosR = cos(rotAngle);
                float sinR = sin(rotAngle);
                rotUV = vec2(rotUV.x * cosR - rotUV.y * sinR, rotUV.x * sinR + rotUV.y * cosR);
                finalMoonUV = rotUV + rotCenter;
            }
            vec3 textureMoonColor = texture(moonTex, finalMoonUV).rgb;

            float brightness = dot(textureMoonColor, vec3(0.2126, 0.7152, 0.0722));
            float customAlpha = smoothstep(0.02, 0.1, brightness);
            float softEdgeMask = smoothstep(1.0, 0.92, localR);

            // Moon phase: darken portion of moon based on sun-moon angle + phase offset
            float phaseAngle = dot(normalize(lightDir), normalize(moonDir));
            float phase = phaseAngle * 0.5 + 0.5 + moonPhaseOffset - 0.5;
            phase = clamp(phase, 0.0, 1.0);
            float phaseMask = smoothstep(0.0, 0.35, phase);

            vec3 litMoonColor = textureMoonColor * moonTintColor * moonBrightness * tMalam * phaseMask;
            litMoonColor += moonGlow * 0.25 * phaseMask;

            // Apply lunar eclipse
            litMoonColor = applyLunarEclipse(litMoonColor, md, moonAngularRadius);

            float moonVisibilityFactor = mix(0.95, 0.7, weatherMode);
            float finalMoonAlpha = customAlpha * moonVisibilityFactor * softEdgeMask;

            skyWithCelestial = mix(skyWithCelestial, litMoonColor, finalMoonAlpha * moonOcclusion);
        }
    }

    // ──── STARS ────
    if (starsEnabled > 0.5)
    {
        float nightFactor = tMalam;
        float starMask = smoothstep(0.0, 0.25, nightFactor);

        if (viewDir.y > 0.0)
        {
            vec2 starUV = viewDir.xz * 150.0;
            float stars = starNoise(starUV);
            vec3 sColor = starColor * stars * starMask * starBrightness;
            skyWithCelestial += sColor;
        }
    }

    // ──── FINAL MIX ────
    vec3 result = mix(skyWithCelestial, finalCloudColor, cloudAlpha);
    result += vec3(0.9, 0.95, 1.3) * bolt * 2.5;
    result += vec3(0.9, 0.95, 1.3) * bolt * 4.0 * lightning;

    FragColor = vec4(result, 1.0);
}
