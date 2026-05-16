#version 400 core
out vec4 FragColor;

in vec3 TexCoords;

uniform vec3 fogColor;
uniform vec3 sunDir;
uniform vec3 time; 
uniform sampler2D moonTex;

// Uniform Mode Cuaca (0.0 = Cerah 10%, 0.5 = Sedikit Mendung 50%, 1.0 = Mendung 90%)
uniform float weatherMode; 
 
// =========================================================================
// VARIABEL BARU: KONFIGURASI PERSENTASE AMBANG BATAS CUACA (BISA DIUBAH-UBAH)
// =========================================================================
// Format vec2: vec2(Batas_Bawah_Smoothstep, Batas_Atas_Smoothstep)
// Semakin BESAR angkanya = awan semakin sedikit. Semakin KECIL angkanya = awan semakin padat.
    uniform vec2 configCerah          = vec2(0.48, 0.82); // Mengunci 25% awan cerah estetik
    uniform vec2 configSedikitMendung = vec2(0.38, 0.75); // 50% awan proporsional
    uniform vec2 configMendungSekali  = vec2(0.05, 0.55); // 90% awan tebal menutupi langit
    uniform vec2 configGlobalAlpha    = vec2(0.18, 0.75); 
// =========================================================================


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

void main()
{
    vec3 viewDir = normalize(TexCoords);
    vec3 lightDir = normalize(sunDir); 
    float sunY = lightDir.y;
    
    // 1. DYNAMIC SKY GRADIENT (Ikut meredup jika mendung)
    float up = max(viewDir.y, 0.0);
    vec3 skyBase = mix(fogColor * 0.5, fogColor, up);
    vec3 skyOvercastTint = mix(vec3(1.0), vec3(0.4, 0.42, 0.45), weatherMode);
    skyBase *= skyOvercastTint; 
    
     // =========================================================================
    // 2. VOLUMETRIC CLOUDS (NUBIS METHOD: BASE PROFILE + EDGE EROSION)
    // =========================================================================
    float cloudAlpha = 0.0;
    vec3 cloudLighting = vec3(0.0);
    
    if (viewDir.y > 0.03) { 
        float minHeight = 2.2;
        float maxHeight = 4.2; 
        
        float startDist = minHeight / viewDir.y;
        float endDist = maxHeight / viewDir.y;
        
        startDist = min(startDist, 40.0);
        endDist = min(endDist, 85.0);
        
        // 20 Langkah linier murni (Bebas anyaman kotak & pasir)
        int steps = 20; 
        float stepSize = (endDist - startDist) / float(steps);
        float rayT = startDist; 
        float transmittance = 1.0;
        
        for (int i = 0; i < steps; i++) {
            vec3 p = viewDir * rayT;
            
            // --- ADAPTASI NUBIS: HITUNG BASE PROFILE & ERODE ---
            vec3 movement = vec3(time.x * 0.05, 0.0, time.x * 0.02);
            vec3 coord = p * 0.4 + movement;
            
            // 1. Ambil gumpalan dasar rendah (Mulus murni, anti-ripple)
            float baseProfile = noise(coord * 0.8); 
            
            // 2. Ambil detail noise frekuensi tinggi untuk mengikis tepi
            float detailNoise = noise(coord * 3.5) * 0.6 + noise(coord * 7.0) * 0.4;
            
            // 3. Rumus Remap/Erode Nubis: Kikis ujung-ujung gradasi yang tipis, 
            // tetapi biarkan bagian tengah awan tetap padat menyatu murni
            float density = smoothstep(0.35, 0.75, baseProfile);
            density = clamp((density - detailNoise * 0.25) / (1.0 - detailNoise * 0.25), 0.0, 1.0);
            
            // Potong tinggi volume ruang awan 3D
            float heightFade = smoothstep(1.5, 2.3, p.y) * (1.0 - smoothstep(2.3, 4.0, p.y));
            density *= heightFade;
            
            if (density > 0.01) {
                // LIGHTING APPROXIMATION (Beer's Law)
                float lightSteps = 3.0;
                float lightStepSize = 0.4;
                float lightDensity = 0.0;
                
                for (int l = 0; l < 3; l++) {
                    vec3 lp = p + lightDir * (float(l) * lightStepSize);
                    float lpBase = noise(lp * 0.4 * 0.8);
                    lightDensity += smoothstep(0.35, 0.75, lpBase) * heightFade;
                }
                
                // MULTIPLE IN-SCATTERING APPROXIMATION (Sesuai paper Nubis)
                float beerLaw = exp(-lightDensity * 1.4);
                
                float alphaSample = density * stepSize * 0.32;
                cloudAlpha += alphaSample * transmittance;
                
                // Gabungkan akumulasi densitas dengan transmisi cahaya matahari
                cloudLighting += vec3(beerLaw) * alphaSample * transmittance;
                
                transmittance *= (1.0 - alphaSample);
                if (transmittance < 0.01) break;
            }
            rayT += stepSize;
        }
        
        // Bersihkan sisa bintik landai di ujung horizon pegunungan Anda
        float horizonCleanMask = smoothstep(0.03, 0.15, viewDir.y);
        cloudAlpha = clamp(cloudAlpha, 0.0, 1.0) * horizonCleanMask;
    }

    // =========================================================================
    // 3. CLOUD COLORING BASED ON TIME (FIX TOTAL: LENYAPKAN KUNING SAAT MENDUNG)
    // =========================================================================
    vec3 noonColor = vec3(1.0, 1.0, 1.0);
    vec3 sunsetColor = vec3(1.0, 0.5, 0.3);
    vec3 nightColor = vec3(0.1, 0.1, 0.15);
    float tMalam = 1.0 - smoothstep(-0.3, 0.1, sunY);

    vec3 cloudBaseColor;
    if (sunY > 0.2) cloudBaseColor = noonColor;
    else if (sunY > -0.2) cloudBaseColor = mix(sunsetColor, noonColor, (sunY + 0.2) / 0.4);
    else cloudBaseColor = mix(nightColor, sunsetColor, clamp((sunY + 0.5) / 0.3, 0.0, 1.0));
    
    // Jinakkan warna gelap mendung agar hanya aktif penuh saat weatherMode mendekati 1.0
    vec3 darkOvercastTint = mix(vec3(1.0), vec3(0.45, 0.45, 0.5), weatherMode);
    cloudBaseColor *= darkOvercastTint;

    float sunInClouds = pow(max(dot(viewDir, lightDir), 0.0), 10.0);
    float moonInClouds = pow(max(dot(viewDir, normalize(-lightDir)), 0.0), 10.0);
    
    // Kecerahan dasar awan melorot saat mendung pekat
    float baseBrightnessFactor = mix(1.0, 0.1, weatherMode);
    
    // PERBAIKAN UTAMA: Buat warna pendaran matahari di dalam awan menjadi dinamis.
    // Jika Cerah (weatherMode = 0.0) -> Menyala Kuning Emas (1.0, 0.8, 0.4)
    // Jika Mendung (weatherMode = 1.0) -> Berubah menjadi Abu-abu Putih Redup (0.6, 0.6, 0.6)
    vec3 dynamicSunCloudColor = mix(vec3(1.0, 0.8, 0.4), vec3(0.6, 0.6, 0.6), weatherMode);
    
    // Kurangi kekuatan sorotan sunInClouds secara drastis saat mendung agar tidak bocor terang
    float dynamicSunInCloudsFactor = sunInClouds * mix(0.7, 0.1, weatherMode);
    
    vec3 finalCloudColor = mix(cloudBaseColor * baseBrightnessFactor, dynamicSunCloudColor, dynamicSunInCloudsFactor);
    finalCloudColor = mix(finalCloudColor, vec3(0.5, 0.7, 1.0), moonInClouds * 0.5 * tMalam);

    // =========================================================================
    // SINKRONISASI BLOCKER MATAHARI (FIX: KUNCI PUTIH DI ATAS, GELAP DI MENDUNG)
    // =========================================================================
    vec3 skyWithCelestial = skyBase;
    float sunDisc = max(dot(viewDir, lightDir), 0.0);
    float sunVisible = smoothstep(-0.1, 0.1, sunY); 
    float sunBlocker = clamp(1.0 - (cloudAlpha * 1.5), 0.0, 1.0); 
    
    // 1. Kunci kurva transisi ketinggian matahari agar sangat sensitif tepat di horizon
    float sunHeightFactor = clamp(smoothstep(0.0, 0.12, sunY), 0.0, 1.0);
    
    // PERBAIKAN 1: Pastikan saat di atas (sunHeightFactor = 1.0), semua komponen warna 
    // matahari beralih total menjadi PUTIH MURNI DAN BERSIH (tanpa campuran abu-abu kuning)
    vec3 baseSunCore = mix(vec3(1.5, 0.45, 0.15), vec3(1.5, 1.5, 1.5), sunHeightFactor);
    vec3 baseSunGlow = mix(vec3(1.0, 0.35, 0.1),  vec3(1.0, 1.0, 1.0), sunHeightFactor);
    vec3 baseBloomOuter = mix(vec3(0.8, 0.2, 0.05), vec3(1.0, 1.0, 1.0), sunHeightFactor); // Putih bersih!
    
    // PERBAIKAN 2: Kalibrasi peredaman mutlak saat mendung.
    // Jika weatherMode = 1.0 (Mendung), pendaran matahari dipotong habis hingga tersisa 2%
    // Ini melenyapkan bintik kuning raksasa saat awan badai berkumpul
    float lightIntensityFactor = mix(1.0, 0.02, weatherMode);
    vec3 dynamicSunCore = baseSunCore * lightIntensityFactor;
    vec3 dynamicSunGlow = baseSunGlow * lightIntensityFactor;
    vec3 dynamicBloomOuter = baseBloomOuter * lightIntensityFactor;

    // Gambar Inti Cakram Matahari
    skyWithCelestial += dynamicSunCore * smoothstep(0.9995, 1.0, sunDisc) * 5.0 * sunVisible * sunBlocker;
    
    // Saring silau pendaran agar menyatu di dalam gumpalan mendung pekat
    float bloomMask = mix(1.0, 0.02, cloudAlpha); 
    skyWithCelestial += dynamicSunGlow * pow(sunDisc, 60.0) * 0.5 * sunVisible * bloomMask;
    skyWithCelestial += dynamicBloomOuter * pow(sunDisc, 500.0) * 1.0 * sunVisible * bloomMask;
     
    // 4. CINEMATIC MOON
    vec3 moonDir = normalize(-lightDir);
    float moonDisc = max(dot(viewDir, moonDir), 0.0);
    vec3 moonGlow = vec3(0.2, 0.4, 0.8) * pow(moonDisc, 120.0) * 0.6 * tMalam * bloomMask * sunBlocker;
    skyWithCelestial += moonGlow;

    float moonSizeThreshold = 0.9992; 
    if (moonDisc > moonSizeThreshold && tMalam > 0.01) {
        vec3 upVec = vec3(0.0, 1.0, 0.0);
        vec3 moonRight = normalize(cross(upVec, moonDir));
        vec3 moonUp = cross(moonDir, moonRight);

        vec2 moonUV;
        moonUV.x = dot(viewDir, moonRight);
        moonUV.y = dot(viewDir, moonUp);

        float moonRadius = sqrt(1.0 - moonSizeThreshold); 
        vec2 localUV = moonUV / moonRadius;
        float distFromCenter = length(localUV);

        if (distFromCenter <= 1.0) {
            vec2 finalMoonUV = localUV * 0.5 + vec2(0.5);
            vec4 textureMoon = texture(moonTex, finalMoonUV);
            vec3 textureMoonColor = textureMoon.rgb;

            float customAlpha = smoothstep(0.1, 0.3, textureMoon.a);

            vec3 litMoonColor = textureMoonColor * vec3(1.0, 0.95, 0.85) * 1.6 * tMalam;
            litMoonColor += (moonGlow * 0.35) * sunBlocker;

            float edgeAlpha = smoothstep(0.98, 0.93, distFromCenter);
            float finalMoonAlpha = customAlpha * pow(sunBlocker, 2.0) * edgeAlpha;

            skyWithCelestial = mix(skyWithCelestial, litMoonColor, finalMoonAlpha);
        }
    }

    // Mengunci bobot transparansi global awan agar mode cerah terlihat sangat tipis & minimalis
    float globalAlphaWeight = mix(0.18, 0.75, weatherMode);
    vec3 result = mix(skyWithCelestial, finalCloudColor, cloudAlpha * globalAlphaWeight);
    FragColor = vec4(result, 1.0);
}
 
#version 400 core
out vec4 FragColor;

in vec3 TexCoords;

uniform vec3 fogColor;
uniform vec3 sunDir;
uniform vec3 time; 
uniform sampler2D moonTex; // Uniform Tekstur Bulan PNG Transparan 1:1 Anda
uniform float weatherMode; // 0.0 = Cerah (25%), 0.5 = Sedikit Mendung (50%), 1.0 = Mendung Sekali (90%)

// Variabel konfigurasi persentase kerapatan awan dinamis
uniform vec2 configCerah          = vec2(0.48, 0.82); // Mengunci 25% awan cerah estetik
uniform vec2 configSedikitMendung = vec2(0.38, 0.75); // 50% awan proporsional
uniform vec2 configMendungSekali  = vec2(0.05, 0.55); // 90% awan tebal menutupi langit
uniform vec2 configGlobalAlpha    = vec2(0.18, 0.75); 

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

// Fungsi pembentuk kerapatan awan menggunakan struktur fisik pengikisan Nubis (Jack Tollenaar)
float getCloudDensity(vec3 p, float cutMin, float cutMax) {
    vec3 movement = vec3(time.x * 0.05, 0.0, time.x * 0.02);
    vec3 coord = p * 0.4 + movement; // Mengembalikan skala kerapatan asli Jack Tollenaar
    
    // 1. Ambil gumpalan profil dasar beresolusi rendah (Anti-ripple)
    float baseProfile = noise(coord * 0.8); 
    
    // 2. Ambil detail noise frekuensi tinggi untuk mengikis tepi luar gumpalan
    float detailNoise = noise(coord * 3.5) * 0.6 + noise(coord * 7.0) * 0.4;
    
    // 3. Remap rumus pengikisan tepi awan kumulus asli Jack Tollenaar
    float d = smoothstep(cutMin, cutMax, baseProfile);
    d = clamp((d - detailNoise * 0.25) / (1.0 - detailNoise * 0.25), 0.0, 1.0);
    
    // Mengembalikan tinggi volume ruang awan 3D yang stabil
    float heightFade = smoothstep(1.5, 2.3, p.y) * (1.0 - smoothstep(2.3, 4.0, p.y));
    return d * heightFade;
}

void main()
{
    vec3 viewDir = normalize(TexCoords);
    vec3 lightDir = normalize(sunDir); 
    float sunY = lightDir.y;
    
    // 1. DYNAMIC SKY GRADIENT 
    float up = max(viewDir.y, 0.0);
    vec3 skyWithCelestial = mix(fogColor * 0.5, fogColor, up);
    vec3 skyOvercastTint = mix(vec3(1.0), vec3(0.4, 0.42, 0.45), weatherMode);
    skyWithCelestial *= skyOvercastTint; 
    
    float tMalam = 1.0 - smoothstep(-0.3, 0.1, sunY);
    
    // 2. VOLUMETRIC CLOUDS (NUBIS METHOD JACK TOLLENAAR)
    float cloudAlpha = 0.0;
    vec3 cloudLighting = vec3(0.0);
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
    
    if (viewDir.y > 0.03) { 
        float minHeight = 2.2;
        float maxHeight = 4.5; 
        
        // Jarak penembakan sinar orisinil Jack Tollenaar
        float startDist = minHeight / viewDir.y;
        float endDist = maxHeight / viewDir.y;
        startDist = min(startDist, 40.0);
        endDist = min(endDist, 85.0);
        
        int steps = 20; 
        float stepSize = (endDist - startDist) / float(steps);
        float rayT = startDist; 
        float transmittance = 1.0;
        
        for (int i = 0; i < steps; i++) {
            vec3 p = viewDir * rayT;
            float density = getCloudDensity(p, cutMin, cutMax);
            
            if (density > 0.01) {
                float lightSteps = 3.0;
                float lightStepSize = 0.4;
                float lightDensity = 0.0;
                
                for (int l = 0; l < 3; l++) {
                    vec3 lp = p + lightDir * (float(l) * lightStepSize);
                    vec3 lpCoord = lp * 0.4 + vec3(time.x * 0.05, 0.0, time.x * 0.02);
                    float lpBase = noise(lpCoord * 0.8);
                    float heightFade = smoothstep(1.5, 2.3, lp.y) * (1.0 - smoothstep(2.3, 4.0, lp.y));
                    lightDensity += smoothstep(cutMin, cutMax, lpBase) * heightFade;
                }
                
                float beerLaw = exp(-lightDensity * 1.4);
                float alphaSample = density * stepSize * 0.32;
                
                cloudAlpha += alphaSample * transmittance;
                cloudLighting += vec3(beerLaw) * alphaSample * transmittance;
                
                transmittance *= (1.0 - alphaSample);
                if (transmittance < 0.01) break;
            }
            rayT += stepSize;
        }
        float horizonCleanMask = smoothstep(0.03, 0.15, viewDir.y);
        cloudAlpha = clamp(cloudAlpha, 0.0, 1.0) * horizonCleanMask;
    }

    // 3. CLOUD COLORING BASED ON TIME
    vec3 noonColor = vec3(1.0, 1.0, 1.0);
    vec3 sunsetColor = vec3(1.0, 0.5, 0.3);
    vec3 nightColor = vec3(0.08, 0.08, 0.12);

    vec3 cloudBaseColor;
    if (sunY > 0.2) cloudBaseColor = noonColor;
    else if (sunY > -0.2) cloudBaseColor = mix(sunsetColor, noonColor, (sunY + 0.2) / 0.4);
    else cloudBaseColor = mix(nightColor, sunsetColor, clamp((sunY + 0.5) / 0.3, 0.0, 1.0));
    
    vec3 darkOvercastTint = mix(vec3(1.0), vec3(0.45, 0.45, 0.5), weatherMode);
    cloudBaseColor *= darkOvercastTint;

    vec3 finalCloudColor = cloudBaseColor * (0.3 + cloudLighting * 1.8);
    float sunInClouds = pow(max(dot(viewDir, lightDir), 0.0), 8.0);
    float sunsetIntensity = 1.0 - clamp(smoothstep(0.0, 0.35, sunY), 0.0, 1.0);
    
    vec3 dynamicSunCloudColor = mix(vec3(1.0, 0.7, 0.3), vec3(0.6, 0.6, 0.6), weatherMode);
    float dynamicSunInCloudsFactor = sunInClouds * mix(0.7, 0.1, weatherMode);
    finalCloudColor += mix(dynamicSunCloudColor, vec3(1.8, 0.4, 0.1), sunsetIntensity) * dynamicSunInCloudsFactor * cloudAlpha * 1.5;

    // SINKRONISASI BLOCKER MATAHARI
    float sunDisc = max(dot(viewDir, lightDir), 0.0);
    float sunVisible = smoothstep(-0.1, 0.1, sunY); 
    float sunBlocker = clamp(1.0 - (cloudAlpha * 1.5), 0.0, 1.0); 
    
    float sunHeightFactor = clamp(smoothstep(0.0, 0.12, sunY), 0.0, 1.0);
    vec3 baseSunCore = mix(vec3(1.5, 0.45, 0.15), vec3(1.5, 1.5, 1.5), sunHeightFactor);
    vec3 baseSunGlow = mix(vec3(1.0, 0.35, 0.1),  vec3(1.0, 1.0, 1.0), sunHeightFactor);
    vec3 baseBloomOuter = mix(vec3(0.8, 0.2, 0.05), vec3(1.0, 1.0, 1.0), sunHeightFactor);
    
    float lightIntensityFactor = mix(1.0, 0.02, weatherMode);
    vec3 dynamicSunCore = baseSunCore * lightIntensityFactor;
    vec3 dynamicSunGlow = baseSunGlow * lightIntensityFactor;
    vec3 dynamicBloomOuter = baseBloomOuter * lightIntensityFactor;

    skyWithCelestial += dynamicSunCore * smoothstep(0.99985, 1.0, sunDisc) * 5.0 * sunVisible * sunBlocker;
    float bloomMask = mix(1.0, 0.05, cloudAlpha); 
    skyWithCelestial += dynamicSunGlow * pow(sunDisc, 180.0) * 0.5 * sunVisible * bloomMask;
    skyWithCelestial += dynamicBloomOuter * pow(sunDisc, 1200.0) * 1.0 * sunVisible * bloomMask;

    // 4. CINEMATIC MOON
    vec3 moonDir = normalize(-lightDir);
    float moonDisc = max(dot(viewDir, moonDir), 0.0);
    vec3 moonGlow = vec3(0.2, 0.4, 0.8) * pow(moonDisc, 120.0) * 0.6 * tMalam * bloomMask * sunBlocker;
    skyWithCelestial += moonGlow;

    float moonSizeThreshold = 0.9992; 
    if (moonDisc > moonSizeThreshold && tMalam > 0.01) {
        vec3 upVec = vec3(0.0, 1.0, 0.0);
        vec3 moonRight = normalize(cross(upVec, moonDir));
        vec3 moonUp = cross(moonDir, moonRight);
        vec2 moonUV;
        moonUV.x = dot(viewDir, moonRight);
        moonUV.y = dot(viewDir, moonUp);
        float moonRadius = sqrt(1.0 - moonSizeThreshold); 
        vec2 localUV = moonUV / moonRadius;
        float distFromCenter = length(localUV);

        if (distFromCenter <= 1.0) {
            vec2 finalMoonUV = localUV * 0.5 + vec2(0.5);
            vec4 textureMoon = texture(moonTex, finalMoonUV);
            vec3 textureMoonColor = textureMoon.rgb;
            float customAlpha = smoothstep(0.1, 0.3, textureMoon.a);
            vec3 litMoonColor = textureMoonColor * vec3(1.0, 0.95, 0.85) * 1.6 * tMalam;
            litMoonColor += (moonGlow * 0.35) * sunBlocker;
            float edgeAlpha = smoothstep(0.98, 0.93, distFromCenter);
            float finalMoonAlpha = customAlpha * pow(sunBlocker, 2.0) * edgeAlpha;
            skyWithCelestial = mix(skyWithCelestial, litMoonColor, finalMoonAlpha);
        }
    }

    float globalAlphaWeight = mix(configGlobalAlpha.x, configGlobalAlpha.y, weatherMode);
    vec3 result = mix(skyWithCelestial, finalCloudColor, cloudAlpha * globalAlphaWeight);
    FragColor = vec4(result, 1.0);
}