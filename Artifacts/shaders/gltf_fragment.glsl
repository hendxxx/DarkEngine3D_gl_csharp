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

// ── Texture ───────────────────────────────────────────────────────────────────
uniform sampler2D albedoMap;
uniform int       useAlbedo;        // 1 = gunakan texture, 0 = warna default
uniform vec4      baseColorFactor;  // warna dasar tambahan sesuai glTF 2.0

// ── Fog toggle ────────────────────────────────────────────────────────────────
uniform int useFog;

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
    vec3  ambComp   = ambient * activeColor;
    vec3  diffComp  = diff   * activeColor * 0.8;

    // ── Specular (rim-light style) ────────────────────────────────────────────
    vec3  viewDir   = normalize(viewPos - FragPos);
    vec3  halfDir   = normalize(lightDir + viewDir);
    float spec      = pow(max(dot(norm, halfDir), 0.0), 32.0) * 0.3;
    vec3  specComp  = spec * activeColor;

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
