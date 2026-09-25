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

    // MODE BOX
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
