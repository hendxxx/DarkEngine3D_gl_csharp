#version 330 core

out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec2 TexCoord;

// ── Lighting uniforms (sama dengan terrain shader) ────────────────────────────
uniform vec3 sunDir;
uniform vec3 lightColor;
uniform vec3 viewPos;
uniform vec3 fogColor;

// ── CSM UNIFORMS ──
uniform sampler2D shadowMap0;
uniform sampler2D shadowMap1;
uniform sampler2D shadowMap2;
uniform mat4 lightSpaceMatrices[3];
uniform float cascadeEnds[3];

// ── Texture ───────────────────────────────────────────────────────────────────
uniform sampler2D albedoMap;
uniform int       useAlbedo;        // 1 = gunakan texture, 0 = warna default
uniform vec4      baseColorFactor;  // warna dasar tambahan sesuai glTF 2.0

// ── Fog toggle ────────────────────────────────────────────────────────────────
uniform int useFog;

// ── CSM SHADOW CALCULATION ──
float CalculateShadow(vec4 fragPosLightSpace, sampler2D shadowMap, float bias) {
    vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
    projCoords = projCoords * 0.5 + 0.5;
    
    if(projCoords.z > 1.0)
        return 1.0;
        
    float shadow = 0.0;
    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);
    for(int x = -1; x <= 1; ++x) {
        for(int y = -1; y <= 1; ++y) {
            float pcfDepth = texture(shadowMap, projCoords.xy + vec2(x, y) * texelSize).r; 
            shadow += projCoords.z - bias > pcfDepth ? 0.0 : 1.0;        
        }    
    }
    shadow /= 9.0;
    return shadow;
}

void main()
{
    vec3 norm = normalize(Normal);

    // ── Base color ────────────────────────────────────────────────────────────
    vec4 albedo = vec4(0.72, 0.68, 0.62, 1.0); // warna skin netral default
    if (useAlbedo == 1)
        albedo = texture(albedoMap, TexCoord);

    // Kalikan dengan baseColorFactor (glTF 2.0)
    albedo *= baseColorFactor;

    // Alpha masking untuk bagian transparan (misal pakaian/rambut/aksesoris)
    if (albedo.a < 0.1)
        discard;

    vec3 baseColor = albedo.rgb;

    // ── Phong lighting (sederhana, cepat) ─────────────────────────────────────
    float nightBlend = smoothstep(0.15, 0.0, sunDir.y);

    vec3 moonDir   = normalize(vec3(-sunDir.x, 0.7, -sunDir.z));
    vec3 moonColor = vec3(0.08, 0.12, 0.25);

    vec3 lightDir   = normalize(mix(normalize(sunDir), moonDir, nightBlend));
    vec3 activeColor = mix(lightColor, moonColor, nightBlend);
    float ambient   = mix(0.25, 0.05, nightBlend);

    float diff      = max(dot(norm, lightDir), 0.0);
    
    // ── CALCULATE SHADOW MULTIPLIER (CSM) ──
    float depth = length(viewPos - FragPos);
    int cascadeIndex = 2;
    if (depth < cascadeEnds[0]) {
        cascadeIndex = 0;
    } else if (depth < cascadeEnds[1]) {
        cascadeIndex = 1;
    }

    float bias = max(0.003 * (1.0 - dot(norm, lightDir)), 0.0003);
    if (cascadeIndex == 0) bias *= 0.1;
    else if (cascadeIndex == 1) bias *= 0.5;

    float shadow = 1.0;
    if (cascadeIndex == 0) {
        shadow = CalculateShadow(lightSpaceMatrices[0] * vec4(FragPos, 1.0), shadowMap0, bias);
    } else if (cascadeIndex == 1) {
        shadow = CalculateShadow(lightSpaceMatrices[1] * vec4(FragPos, 1.0), shadowMap1, bias);
    } else {
        shadow = CalculateShadow(lightSpaceMatrices[2] * vec4(FragPos, 1.0), shadowMap2, bias);
    }

    vec3  ambComp   = ambient * activeColor;
    vec3  diffComp  = diff   * activeColor * 0.8 * shadow;

    // ── Specular (rim-light style) ────────────────────────────────────────────
    vec3  viewDir   = normalize(viewPos - FragPos);
    vec3  halfDir   = normalize(lightDir + viewDir);
    float spec      = pow(max(dot(norm, halfDir), 0.0), 32.0) * 0.3;
    vec3  specComp  = spec * activeColor * shadow;

    vec3 result = (ambComp + diffComp + specComp) * baseColor;

    // ── Fog ───────────────────────────────────────────────────────────────────
    if (useFog == 1)
    {
        float dist        = length(viewPos - FragPos);
        float fogDensity  = 0.003;
        float fogFactor   = exp(-pow(dist * fogDensity, 2.0));
        fogFactor         = clamp(fogFactor, 0.0, 1.0);
        result            = mix(fogColor, result, fogFactor);
    }

    // ── Tone mapping + gamma ──────────────────────────────────────────────────
    result = result / (result + vec3(1.0));
    result = pow(result, vec3(1.0 / 2.2));

    FragColor = vec4(result, 1.0);
}
