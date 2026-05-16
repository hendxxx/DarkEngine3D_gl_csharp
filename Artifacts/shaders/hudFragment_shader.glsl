#version 400 core
out vec4 FragColor;
in vec2 TexCoords;

uniform sampler2D hudTexture;
uniform vec3 textColor;
uniform vec3 uvScale; // Sebagai flag: x=0 (Box), x=1 (Text)

void main() {
    // MODE BOX
    if (uvScale.x == 0.0) {
        // Gambar kotak dengan warna dari textColor dan alpha 0.5
        FragColor = vec4(textColor, 0.6); 
        return; // BERHENTI DI SINI agar tidak kena discard di bawah
    }

    // MODE TEKS
    float alpha = texture(hudTexture, TexCoords).a; // Gunakan .a karena kamu pakai RGBA
    
    // Gunakan discard hanya untuk teks agar background kotak tidak hilang
    if (alpha < 0.1) discard;

    FragColor = vec4(textColor, alpha);
}
