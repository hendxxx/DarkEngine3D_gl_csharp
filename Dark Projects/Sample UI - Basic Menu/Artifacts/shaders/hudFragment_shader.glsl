#version 400 core
out vec4 FragColor;
in vec2 TexCoords;

uniform sampler2D hudTexture;
uniform vec3 textColor;
uniform vec3 uvScale; 
// uvScale.x = 0 → BOX
// uvScale.x = 1 → TEXT
// uvScale.x = 2 → IMAGE
uniform float rotation; // radian

void main() {

    // MODE SOLID (uvScale.x == 3): alpha 1.0 — panel HUD OPAQUE sungguhan.
    // Dipakai HUD.DrawSolidBox (blend OFF). Dulu solid quad memakai MODE BOX
    // (alpha 0.6) sehingga RGBA8 target menerima A=0.6: ImGui mem-blit scene
    // texture dengan blending (SrcAlpha, OneMinusSrcAlpha) → panel tampak
    // ter-fade pucat + teks HUD "hilang", dan dst-alpha 0.6 juga membuat
    // resolve/DoF/PostFx menganggap HUD semi-transparan.
    if (uvScale.x == 3.0) {
        FragColor = vec4(textColor, 1.0);
        return;
    }

    // MODE BOX
    // Alpha tetap 0.6 (dim overlay & vignette mengandalkan ini), TAPI HUD.Flush
    // memakai BlendFuncSeparate agar alpha DESTINATION tetap 1 — resolve/DoF/Bloom/
    // PostFx yang mencampur memakai dst-alpha tidak lagi menyapu panel menuju
    // transparan ("layer HUD tembus cahaya"). Panel yang harus OPAQUE dipakai lewat
    // HUD.DrawSolidBox (quad solid, blend off), bukan lewat alpha box.
    if (uvScale.x == 0.0) {
        FragColor = vec4(textColor, 0.6);
        return;
    }

    // MODE TEXT (alpha mask)
    if (uvScale.x == 1.0) {
        float alpha = texture(hudTexture, TexCoords).a;

        if (alpha < 0.1)
            discard;

        FragColor = vec4(textColor, alpha);
        return;
    }

    // MODE IMAGE (RGBA penuh + rotasi)
    if (uvScale.x == 2.0) {

        vec2 center = vec2(0.5, 0.5);
        vec2 uv = TexCoords - center;

        // scale down biar tidak kena pinggir
        if (rotation > 0)
        uv *= 0.80;

        float c = cos(rotation);
        float s = sin(rotation);

        vec2 rotatedUV = vec2(
            uv.x * c - uv.y * s,
            uv.x * s + uv.y * c
        );

        rotatedUV += center;

        FragColor = texture(hudTexture, rotatedUV);
        return;
    }
}
