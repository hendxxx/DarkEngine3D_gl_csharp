#version 400 core
out vec4 FragColor;

in vec3 TexCoords;

uniform vec3 fogColor;
uniform vec3 sunDir;
uniform vec3 time; 
uniform sampler2D moonTex; // Uniform Tekstur Bulan Nyata Anda

// --- PERBAIKAN: MENURUNKAN THRESHOLD MENDUNG AGAR AWAN MENUTUP 99% ---
const vec2 configCerah          = vec2(0.76, 0.88); // Tetap 10% serpihan tipis sesuai modifikasi sebelumnya
const vec2 configSedikitMendung = vec2(0.42, 0.68); // Tetap bolong-bolong natural di tengah langit
const vec2 configMendungSekali  = vec2(0.02, 0.25); // Angka diturunkan drastis agar gumpalan menutup rapat 99% langit


uniform float weatherMode; 

float hash(float n) { return fract(sin(n) * 43758.5453123); }
float noise(vec3 x) {
    vec3 p = floor(x);
    vec3 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    float n = p.x + p.y * 57.0 + 113.0 * p.z;
    return mix(mix(mix(hash(n + 0.0), hash(n + 1.0), f.x),
                   mix(hash(n + 57.0), hash(n + 58.0), f.x), f.y),
               mix(mix(hash(n + 113.0), hash(n + 114.0), f.x),
                   mix(hash(n + 170.0), hash(n + 171.0), f.x), f.y), f.z);
}

float Remap(float value, float inputMin, float inputMax, float outputMin, float outputMax) {
    return outputMin + (clamp(value, inputMin, inputMax) - inputMin) * (outputMax - outputMin) / max(inputMax - inputMin, 0.001);
}

void main()
{
    vec3 viewDir = normalize(TexCoords);
    vec3 lightDir = normalize(sunDir); 
    float sunY = lightDir.y;
    
    // =========================================================================
    // 1. MASTER TIMING GRADIENT (ANTI LOMPAT / SUPER HALUS)
    // =========================================================================
    // Menggunakan rentang yang sangat lebar (0.0 sampai 0.65) dengan fungsi clamp murni
    // Ini memastikan pencampuran warna siang ke senja dicicil sangat lambat dan konstan
    float tSunset = clamp((sunY - 0.0) / (0.65 - 0.0), 0.0, 1.0);
    
    // Transisi dari senja ke malam hari (saat matahari tenggelam di bawah ufuk)
    float tNight = smoothstep(-0.25, 0.05, sunY);
    float tMalam = 1.0 - smoothstep(-0.3, 0.1, sunY);

    // Kumpulan palet warna langit dan awan sinematik
    vec3 noonSky      = vec3(0.4, 0.6, 0.85);     // Biru Siang Cerah
    vec3 noonCloud    = vec3(1.0, 1.0, 1.0);      // Putih Siang
    vec3 sunsetColor  = vec3(1.0, 0.48, 0.25);    // Oranye Senja Hangat
    vec3 nightColor   = vec3(0.06, 0.06, 0.10);   // Biru Gelap Malam

    // --- GRADASI WARNA DASAR LANGIT (SKYBASE) ---
    // Sekarang warna latar belakang langit ikut berubah mulus mengikuti posisi matahari
    vec3 currentSkyColor = mix(sunsetColor * 0.6, noonSky, tSunset);
    currentSkyColor      = mix(nightColor * 0.4, currentSkyColor, tNight);
    
    float up = max(viewDir.y, 0.0);
    vec3 skyBase = mix(fogColor * 0.5, currentSkyColor, up);
    
    // --- GRADASI WARNA DASAR AWAN (CLOUDBASECOLOR) ---
    vec3 dayToSunsetColor = mix(sunsetColor, noonCloud, tSunset);
    vec3 cloudBaseColor   = mix(nightColor, dayToSunsetColor, tNight);

    // =========================================================================
    // 2. EVALUASI PARAMETER CUACA DINAMIS (ASLI BAWAAN ANDA)
    // =========================================================================
    float cutMin, cutMax;
    if (weatherMode <= 0.5) {
        float t = weatherMode / 0.5;
        cutMin = mix(configCerah.x, configSedikitMendung.x, t);
        cutMax = mix(configCerah.y, configSedikitMendung.y, t);
    } else {
        float t = (weatherMode - 0.5) / 0.5;
        cutMin = mix(configSedikitMendung.x, configMendungSekali.x, t);
        cutMax = mix(configSedikitMendung.y, configMendungSekali.y, t);
    }
    
    // 3. PROCEDURAL CLOUDS (NUBIS METHOD JACK TOLLENAAR)
    float cloudAlpha = 0.0;
    if (viewDir.y > 0.0) { 
        float cloudPlaneHeight = 2.5;
        float distToPlane = cloudPlaneHeight / max(viewDir.y, 0.25); 
        vec3 cloudPos = viewDir * distToPlane;
        
        cloudPos.xz *= 0.45; 
        cloudPos.x += time.x * 0.04; 
        cloudPos.z += time.x * 0.01; 
        
        float baseProfile = 0.85; 
        
        float baseNoise = noise(cloudPos * 1.5);
        float detailNoise = noise(cloudPos * 3.0) * 0.3;
        float combinedNoise = baseNoise * 0.7 + detailNoise;
        
        float erodedCloud = Remap(baseProfile, combinedNoise, 1.0, 0.0, 1.0);
        float c = smoothstep(cutMin, cutMax, erodedCloud);
        
        float horizonFade = smoothstep(0.0, 0.22, viewDir.y);
        float nightCloudReduce = mix(0.45, 1.0, smoothstep(-0.2, 0.1, sunY));
        
        cloudAlpha = c * horizonFade * nightCloudReduce;
    }

    // --- PENDARAN CAHAYA MATAHARI DINAMIS ---
    float sunInClouds = pow(max(dot(viewDir, lightDir), 0.0), 10.0);
    float moonInClouds = pow(max(dot(viewDir, normalize(-lightDir)), 0.0), 10.0);
    
    // Gradasi pendaran matahari di awan (Putih di zenit, Kuning Emas lambat di horizon)
    float sunColorFactor = clamp((sunY - 0.0) / (0.5 - 0.0), 0.0, 1.0);
    vec3 dynamicGlowColor = mix(vec3(1.0, 0.78, 0.40), vec3(1.0, 1.0, 1.0), sunColorFactor);
    
    vec3 finalCloudColor = mix(cloudBaseColor, dynamicGlowColor, sunInClouds * 0.7);
    finalCloudColor = mix(finalCloudColor, vec3(0.5, 0.7, 1.0), moonInClouds * 0.5 * tMalam);
    
    float sunsetIntensity = 1.0 - clamp(smoothstep(0.0, 0.35, sunY), 0.0, 1.0);
    finalCloudColor += vec3(1.5, 0.45, 0.15) * sunInClouds * sunsetIntensity * 1.5;

    // --- PROSES SINKRONISASI BLOCKER DAN BULAN SEPERTI SEBELUMNYA ---
    // (Pertahankan sisa kode penempatan matahari, awan, dan cinematic moon Anda di bawah sini hingga akhir...)


    // --- PERBAIKAN BLOCKER MATAHARI (ANTI-HILANG) ---
    vec3 skyWithCelestial = skyBase;
    float sunDot = dot(viewDir, lightDir);
    float sunVisible = smoothstep(-0.1, 0.1, sunY);

    // FIX 1: Melonggarkan blocker agar matahari tetap bisa menembus awan tebal sebagai pendaran cahaya terang (Silver Lining)
    float sunBlocker = mix(1.0, 0.15, cloudAlpha);

    float sunHeightFactor = clamp(smoothstep(0.0, 0.35, sunY), 0.0, 1.0);
    vec3 dynamicSunCore = mix(vec3(1.5, 0.45, 0.15), vec3(1.5, 1.4, 1.2), sunHeightFactor);
    vec3 dynamicSunGlow = mix(vec3(1.0, 0.35, 0.1),  vec3(1.0, 0.7, 0.3), sunHeightFactor);
    vec3 dynamicBloomOuter = mix(vec3(0.8, 0.2, 0.05), vec3(0.8, 0.5, 0.2), sunHeightFactor);

    // --- PERSPECTIVE-CORRECTED SUN SHAPE (ANTI-OVAL) ---
    // dot(viewDir, lightDir) defines a circular cone in 3D, but it maps to an ellipse on
    // screen when the sun is off-center under perspective projection. Switch to tangent-
    // plane coordinates around the sun direction so the disc and glow stay circular at
    // any screen position, the same trick used for the moon below.
    float sunCoreMask = 0.0;
    float sunGlowMask = 0.0;
    float sunBloomMask = 0.0;
    if (sunDot > 0.0)
    {
        vec3 sunUpVec = vec3(0.0, 1.0, 0.0);
        // Fallback when lightDir is nearly straight up to avoid degenerate cross product.
        if (abs(lightDir.y) > 0.999) sunUpVec = vec3(0.0, 0.0, 1.0);
        vec3 sunRight = normalize(cross(sunUpVec, lightDir));
        vec3 sunUpAxis = cross(lightDir, sunRight);

        vec2 sunUV = vec2(dot(viewDir, sunRight), dot(viewDir, sunUpAxis)) / sunDot;
        float d = length(sunUV);

        // Hard sun disc (~3° radius). smoothstep gives a soft anti-aliased edge.
        const float sunAngularRadius = 0.052;
        sunCoreMask = smoothstep(sunAngularRadius, sunAngularRadius * 0.75, d);

        // Wider halo + sharper inner bloom, both circular in the tangent plane.
        // Coefficients chosen to roughly match the previous pow(cos, 45) / pow(cos, 300) feel.
        sunGlowMask  = exp(-d * d * 22.5);
        sunBloomMask = exp(-d * d * 150.0);
    }

    skyWithCelestial += dynamicSunCore * sunCoreMask * 4.0 * sunVisible * sunBlocker;

    // Glow/Bloom matahari menembus awan (Kombinasi Blocker Dinamis)
    float bloomMask = mix(1.0, 0.4, cloudAlpha);
    skyWithCelestial += dynamicSunGlow * sunGlowMask * 0.6 * sunVisible * bloomMask;
    skyWithCelestial += dynamicBloomOuter * sunBloomMask * 1.2 * sunVisible * bloomMask;

    // 4. CINEMATIC MOON
    // All shape/glow calculations done in the moon-tangent plane so the moon and its halo
    // stay perfectly circular at any screen position (no more elliptical "oval" moon at edges).
    vec3 moonDir = normalize(-lightDir);
    float moonDot = dot(viewDir, moonDir);

    vec3 moonGlow = vec3(0.0);
    if (moonDot > 0.0 && tMalam > 0.01)
    {
        vec3 moonUpVec = vec3(0.0, 1.0, 0.0);
        // Fallback when moon is nearly straight overhead/below to avoid degenerate cross product.
        if (abs(moonDir.y) > 0.999) moonUpVec = vec3(0.0, 0.0, 1.0);
        vec3 moonRight = normalize(cross(moonUpVec, moonDir));
        vec3 moonUpAxis = cross(moonDir, moonRight);

        // Tangent-plane offset from the moon direction. Circular in 3D angle, so it stays
        // circular on screen regardless of where the moon sits in the viewport.
        vec2 moonUV = vec2(dot(viewDir, moonRight), dot(viewDir, moonUpAxis)) / moonDot;
        float d = length(moonUV);

        // Circular halo around the moon (replaces pow(moonDisc, 120.0) which produced an ellipse).
        float moonGlowMask = exp(-d * d * 60.0);
        moonGlow = vec3(0.2, 0.4, 0.8) * moonGlowMask * 0.6 * tMalam * bloomMask * sunBlocker;
        skyWithCelestial += moonGlow;

        // Moon disc — angular radius in the tangent plane. Same visual size as the previous
        // sqrt(1 - moonSizeThreshold) ≈ 0.0283 sizing, kept stylistic (larger than real moon).
        const float moonAngularRadius = 0.0283;
        float localR = d / moonAngularRadius;

        if (localR <= 1.02)
        {
            vec2 finalMoonUV = (moonUV / moonAngularRadius) * 0.5 + vec2(0.5);
            vec3 textureMoonColor = texture(moonTex, finalMoonUV).rgb;

            float brightness = dot(textureMoonColor, vec3(0.2126, 0.7152, 0.0722));
            float customAlpha = smoothstep(0.02, 0.1, brightness);

            // Soft anti-aliased outer edge of the disc.
            float softEdgeMask = smoothstep(1.0, 0.92, localR);

            vec3 litMoonColor = textureMoonColor * vec3(1.0, 0.95, 0.85) * 1.6 * tMalam;
            litMoonColor += moonGlow * 0.35;

            float finalMoonAlpha = customAlpha * pow(sunBlocker, 2.0) * softEdgeMask;
            skyWithCelestial = mix(skyWithCelestial, litMoonColor, finalMoonAlpha);
        }
    }

    // SATUKAN AWAN DAN LANGIT
    vec3 result = mix(skyWithCelestial, finalCloudColor, cloudAlpha * 0.85);

    FragColor = vec4(result, 1.0);
}
