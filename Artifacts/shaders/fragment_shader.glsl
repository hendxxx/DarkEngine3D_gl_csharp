#version 330 core
out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec3 ObjColor;
in vec2 TexCoord;

uniform vec3 sunDir;
uniform vec3 lightColor;
uniform vec3 viewPos;

uniform sampler2D diffuseTex;
uniform bool useTexture;       // when true, sample diffuseTex; otherwise use ObjColor
uniform float alphaCutoff;     // pixels below this alpha get discarded (alpha-cutout)

void main() {
    // Pick base color: textured or vertex-colored.
    vec4 baseRGBA;
    if (useTexture) {
        baseRGBA = texture(diffuseTex, TexCoord);
        if (baseRGBA.a < alphaCutoff) discard; // hair-card / eye-card transparency
    } else {
        baseRGBA = vec4(ObjColor, 1.0);
    }

    // 1. AMBIENT
    float ambientStrength = 0.15;
    vec3 ambient = ambientStrength * lightColor;

    // 2. DIFFUSE
    vec3 norm = normalize(Normal);
    vec3 lightDir = normalize(-sunDir);
    float diff = max(dot(norm, lightDir), 0.0);
    vec3 diffuse = diff * lightColor;

    // 3. SPECULAR
    float specularStrength = 0.05;
    vec3 viewDir = normalize(viewPos - FragPos);
    vec3 reflectDir = reflect(-lightDir, norm);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32);
    vec3 specular = specularStrength * spec * lightColor;

    vec3 lit = (ambient + diffuse + specular) * baseRGBA.rgb;
    FragColor = vec4(lit, baseRGBA.a);
}
