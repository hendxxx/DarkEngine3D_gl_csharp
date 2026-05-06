#version 330 core
out vec4 FragColor;

in vec3 FragPos;
in vec3 Normal;
in vec3 ObjColor;

uniform vec3 sunDir;    // Arah Matahari (Directional Light)
uniform vec3 lightColor;
uniform vec3 viewPos;   // Posisi Kamera (untuk efek kilau/specular)

void main() {
    // 1. AMBIENT (Cahaya lingkungan agar bayangan tidak hitam pekat)
    float ambientStrength = 0.15;
    vec3 ambient = ambientStrength * lightColor;
    
    // 2. DIFFUSE (Bayangan utama berdasarkan lekukan bukit)
    vec3 norm = normalize(Normal);
    vec3 lightDir = normalize(-sunDir); // Cahaya datang dari arah berlawanan matahari
    float diff = max(dot(norm, lightDir), 0.0);
    vec3 diffuse = diff * lightColor;

    // 3. SPECULAR (Kilauan cahaya pada sudut tertentu - opsional)
    float specularStrength = 0.05;
    vec3 viewDir = normalize(viewPos - FragPos);
    vec3 reflectDir = reflect(-lightDir, norm);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32);
    vec3 specular = specularStrength * spec * lightColor;
            
    // Gabungkan semua komponen
    vec3 result = (ambient + diffuse + specular) * ObjColor;
    FragColor = vec4(result, 1.0);

}
