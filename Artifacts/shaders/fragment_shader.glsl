#version 400 core
out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec3 ObjColor;
in vec2 TexCoord;  

uniform vec3 sunDir, lightColor, viewPos, fogColor, heightScale;
uniform sampler2D tex0, tex1, tex2, tex3, tex4; // 0:Dirt, 1:Rock, 2:Snow, 3:Cliff, 4:Moon
uniform int useTexture;  

// --- CSM UNIFORMS ---
uniform sampler2D shadowMap0;
uniform sampler2D shadowMap1;
uniform sampler2D shadowMap2;
uniform mat4 lightSpaceMatrices[3];
uniform float cascadeEnds[3];

// --- LOD COLOR TOGGLE ---
uniform int showLODColor;
uniform int lodLevel;
uniform int showCSMCascadeColor;

// --- SAKELAR TOGGLE UNTUK MENGAKTIFKAN/MEMATIKAN KABUT ---
uniform int useFog; 

// --- CSM SHADOW CALCULATION ---
float CalculateShadow(vec4 fragPosLightSpace, sampler2D shadowMap, float bias) {
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
   projCoords = projCoords * 0.5 + 0.5;

    if (projCoords.z > 1.0)
        return 1.0;

    projCoords.xy = clamp(projCoords.xy, 0.001, 0.999);

    float shadow = 0.0;
    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);

    for (int x = -1; x <= 1; ++x) {
        for (int y = -1; y <= 1; ++y) {
            vec2 uv = projCoords.xy + vec2(x, y) * texelSize;
            uv = clamp(uv, 0.001, 0.999);

            float pcfDepth = texture(shadowMap, uv).r;
            shadow += (projCoords.z - bias > pcfDepth) ? 0.0 : 1.0;
        }
    }
    shadow /= 9.0;
    return shadow;
}

// --- FUNGSI NOISE & STOCHASTIC (TETAP STANDAR) ---
vec2 hash2(vec2 p) {
    return fract(sin(vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)))) * 43758.5453);
}

float smoothNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = fract(sin(dot(i, vec2(12.9898, 78.233))) * 43758.5453);
    float b = fract(sin(dot(i + vec2(1.0, 0.0), vec2(12.9898, 78.233))) * 43758.5453);
    float c = fract(sin(dot(i + vec2(0.0, 1.0), vec2(12.9898, 78.233))) * 43758.5453);
    float d = fract(sin(dot(i + vec2(1.0, 1.0), vec2(12.9898, 78.233))) * 43758.5453);
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

vec3 stochasticSample(sampler2D tex, vec2 uv) {
    vec2 p = floor(uv);
    vec2 f = fract(uv);
    vec3 res = vec3(0.0);
    float weightSum = 0.0;
    for(int j=-1; j<=1; j++) {
        for(int i=-1; i<=1; i++) {
            vec2 b = vec2(i, j);
            vec2 r = hash2(p + b);
            vec3 sampleColor = texture(tex, uv + r).rgb;
            float dist = length(b - f + r);
            float w = exp(-2.0 * dist * dist);
            res += sampleColor * w;
            weightSum += w;
        }
    }
    return res / weightSum;
}

// --- FUNGSI BARU: STOCHASTIC + TRIPLANAR KHUSUS TEBING (TEX3) ---
vec3 stochasticTriplanarCliff(sampler2D tex, vec3 worldPos, vec3 normal, float tiling) {
    vec3 blending = abs(normalize(normal));
    blending = pow(blending, vec3(10.0)); 
    blending /= (blending.x + blending.y + blending.z);

    vec3 xTex = stochasticSample(tex, worldPos.zy * tiling);
    vec3 yTex = stochasticSample(tex, worldPos.xz * tiling);
    vec3 zTex = stochasticSample(tex, worldPos.xy * tiling);

    return xTex * blending.x + yTex * blending.y + zTex * blending.z;
}

void main() {
    vec3 norm = normalize(Normal);
    float slope = 1.0 - norm.y; 
    
    vec3 texColor;
    if (useTexture == 1) {
        float tilingDatar = 0.5; 
        float tilingTebing = 0.055; 
        
        vec3 t3 = stochasticTriplanarCliff(tex3, FragPos, norm, tilingTebing);

        vec2 uvDatar = FragPos.xz * tilingDatar;
        vec3 t0 = stochasticSample(tex0, uvDatar); 
        vec3 t1 = stochasticSample(tex1, uvDatar); 
        vec3 t2 = stochasticSample(tex2, uvDatar); 

        float h = FragPos.y / max(heightScale.x, 1.0);
        float noise = smoothNoise(FragPos.xz * 0.2) * 0.1;
        float hn = h + noise;

        vec3 base;
        if(hn < 0.25) {
            base = mix(t0, t1, smoothstep(0.1, 0.25, hn));
        } else if(hn < 0.90) {
            base = t1;
        } else {
            base = mix(t1, t2, smoothstep(0.90, 0.99, hn));
        }

        float sn = slope + (noise * 0.1); 
        float cliffMask = smoothstep(0.35, 0.45, sn); 

        vec3 boldCliff = t3 * 0.8; 

        texColor = mix(base, boldCliff, cliffMask);

    } else {
        texColor = ObjColor;
    }

    // =========================================================================
    // LIGHTING INTERPOLASI HALUS (TRANSISI PERLAHAN SIANG -> MALAM)
    // =========================================================================
    
    // 1. Hitung faktor transisi berdasarkan tinggi matahari (sunDir.y)
    // Ketika matahari turun dari ketinggian 0.15 menuju 0.0, nilai blendFactor bergeser dari 0.0 ke 1.0
    float nightBlendFactor = smoothstep(0.15, 0.0, sunDir.y);

    // 2. Siapkan properti cahaya Bulan sebagai target saat malam tiba
    vec3 moonDir = normalize(vec3(-sunDir.x, 0.7, -sunDir.z)); 
    vec3 moonColor = vec3(0.08, 0.12, 0.25); // Warna biru bulan safir redup

    // Jika C++ mengirim warna cahaya yang valid, gunakan itu. Jika hitam total, gunakan moonColor bawaan shader
    vec3 targetNightColor = (length(lightColor) < 0.1) ? moonColor : lightColor;

    // 3. Gabungkan arah dan warna cahaya secara perlahan/gradual (Interpolasi Linear)
    vec3 activeLightDir = normalize(mix(normalize(sunDir), moonDir, nightBlendFactor));
    vec3 activeLightColor = mix(lightColor, targetNightColor, nightBlendFactor);
    float ambientStrength = mix(0.15, 0.04, nightBlendFactor);

    // 4. Hitung Ambient dinamis
    vec3 ambient = ambientStrength * activeLightColor;
    
    // Selaraskan bayangan terdalam medan dengan warna kabut/langit secara perlahan
    vec3 shadowAmbientColor = mix(ambient, fogColor * 0.2, nightBlendFactor * pow(1.0 - slope, 0.3));
    ambient = mix(ambient, shadowAmbientColor, nightBlendFactor);

    // 5. Hitung Diffuse & Shadow Mask dari perpaduan posisi matahari/bulan aktif
    float diff = max(dot(norm, activeLightDir), 0.0);
    float slopeShadow = pow(1.0 - slope, 0.5); 
    float finalDiff = diff * mix(0.7, 1.0, slopeShadow);
    
    // --- CALCULATE SHADOW MULTIPLIER (CSM) ---
    float depth = length(viewPos - FragPos);

    // Blend width between cascades (in world units)
    float blendRange0 = cascadeEnds[0] * 0.1;  // 10% of cascade 0 end
    float blendRange1 = cascadeEnds[1] * 0.1;  // 10% of cascade 1 end

    float bias0 = max(0.002 * (1.0 - dot(norm, activeLightDir)), 0.0001);
    float bias1 = bias0 * 3.0;
    float bias2 = bias0 * 6.0;

    float shadow;
    int cascadeIndex = 0;
    float cascadeBlendT = 0.0;

    if (depth < cascadeEnds[0] - blendRange0) {
        // Fully inside cascade 0
        shadow = CalculateShadow(lightSpaceMatrices[0] * vec4(FragPos, 1.0), shadowMap0, bias0);
        cascadeIndex = 0;
        cascadeBlendT = 0.0;
    } else if (depth < cascadeEnds[0]) {
        // Blend zone between cascade 0 and 1
        float t = (depth - (cascadeEnds[0] - blendRange0)) / blendRange0;
        float s0 = CalculateShadow(lightSpaceMatrices[0] * vec4(FragPos, 1.0), shadowMap0, bias0);
        float s1 = CalculateShadow(lightSpaceMatrices[1] * vec4(FragPos, 1.0), shadowMap1, bias1);
        shadow = mix(s0, s1, t);
        cascadeIndex = 1;      // anggap “utama” ke 1
        cascadeBlendT = t;     // 0..1 antara 0 dan 1
    } else if (depth < cascadeEnds[1] - blendRange1) {
        // Fully inside cascade 1
        shadow = CalculateShadow(lightSpaceMatrices[1] * vec4(FragPos, 1.0), shadowMap1, bias1);
        cascadeIndex = 1;
        cascadeBlendT = 0.0;
    } else if (depth < cascadeEnds[1]) {
        // Blend zone between cascade 1 and 2
        float t = (depth - (cascadeEnds[1] - blendRange1)) / blendRange1;
        float s1 = CalculateShadow(lightSpaceMatrices[1] * vec4(FragPos, 1.0), shadowMap1, bias1);
        float s2 = CalculateShadow(lightSpaceMatrices[2] * vec4(FragPos, 1.0), shadowMap2, bias2);
        shadow = mix(s1, s2, t);
        cascadeIndex = 2;
        cascadeBlendT = t;
    } else {
        // Fully inside cascade 2
        shadow = CalculateShadow(lightSpaceMatrices[2] * vec4(FragPos, 1.0), shadowMap2, bias2);
        cascadeIndex = 2;
        cascadeBlendT = 0.0;
    }

    float shadowMask = smoothstep(0.0, 0.20, dot(norm, activeLightDir));
    vec3 diffuse = finalDiff * activeLightColor * shadowMask * shadow;
    
    vec3 result = (ambient + diffuse) * texColor;
    
    // --- DEBUG: WARNA CSM LOD ---
    if (showCSMCascadeColor == 1) {
        vec3 cascadeColors[3] = vec3[](
            vec3(1.0, 0.0, 0.0), // Cascade 0: Merah
            vec3(0.0, 1.0, 0.0), // Cascade 1: Hijau
            vec3(0.0, 0.0, 1.0)  // Cascade 2: Biru
        );

        vec3 cColor = cascadeColors[cascadeIndex];

        // Kalau di zona blend, bikin sedikit gradasi ke cascade sebelumnya
        if (cascadeBlendT > 0.0 && cascadeIndex > 0) {
            vec3 prevColor = cascadeColors[cascadeIndex - 1];
            cColor = mix(prevColor, cColor, cascadeBlendT);
        }

        // Overlay tipis supaya masih kelihatan lighting
        result = mix(result, cColor, 0.35);
    }

    // --- APPLY LOD COLOR IF ENABLED ---
    if (showLODColor == 1) {
        vec3 lodColors[4] = vec3[](
            vec3(1.0, 0.0, 0.0), // LOD 0: Red
            vec3(0.0, 1.0, 0.0), // LOD 1: Green
            vec3(0.0, 0.0, 1.0), // LOD 2: Blue
            vec3(1.0, 1.0, 0.0)  // LOD 3: Yellow
        );
        // Mix factor diturunkan ke 0.2 agar lebih transparan
        result = mix(result, lodColors[clamp(lodLevel, 0, 3)], 0.1);
    }

    // =========================================================================
    // IMPLEMENTASI PERCABANGAN INTERAKTIF TOGGLE KABUT
    // =========================================================================
    vec3 terrainWithFog;

    if (useFog == 1) {
        float dist = length(viewPos - FragPos);
        float fogDensity = 0.0035; 
        float distanceFactor = exp(-pow(dist * fogDensity, 2.0));
        
        float fogFloor = 10.0;
        float fogHeightRange = 45.0;
        float heightFactor = clamp(1.0 - (FragPos.y - fogFloor) / fogHeightRange, 0.0, 1.0);
        heightFactor = pow(heightFactor, 2.0); 

        float fogFactor = mix(1.0, distanceFactor, heightFactor);
        fogFactor = clamp(fogFactor, 0.0, 1.0);
        
        terrainWithFog = mix(fogColor, result, fogFactor);
    } else {
        terrainWithFog = result;
    }
    
    // 4. Cinematic Reinhard Tone Mapping
    vec3 mapped = terrainWithFog / (terrainWithFog + vec3(1.0));
    
    // 5. Koreksi Gamma layar standar 2.2
    mapped = pow(mapped, vec3(1.0 / 2.2));
      
    FragColor = vec4(mapped, 1.0);
}
