#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

// Instanced model matrix (4 rows)
layout(location = 5) in vec4 aModelRow0;
layout(location = 6) in vec4 aModelRow1;
layout(location = 7) in vec4 aModelRow2;
layout(location = 8) in vec4 aModelRow3;

uniform mat4 lightSpaceMatrix;

// Normal-bias extrusion amount (anti-acne), uploaded live from the Shadow Settings panel.
uniform float u_NormalBias = 0.000001;

out vec2 TexCoord;

void main()
{
    mat4 model = mat4(aModelRow0, aModelRow1, aModelRow2, aModelRow3);
    TexCoord = aTexCoord;

    // Fix Projection artefacts — push vertex slightly along the normal to prevent
    // self-shadowing. The extrusion must follow the inverse-transpose of the model's
    // upper 3x3 (normal matrix): on non-uniform scale / rotation the raw model-space
    // normal points the wrong way and pushes surfaces INTO the shadow map (dark
    // stripes following the geometry). Computed per-instance here — a 3x3 inverse
    // per vertex is negligible.
    float normalBias = u_NormalBias;
    mat3 normalMatrix = transpose(inverse(mat3(model)));
    vec3 n = normalize(normalMatrix * aNormal);
    vec3 extrudedPos = aPos + n * normalBias;
    gl_Position = lightSpaceMatrix * model * vec4(extrudedPos, 1.0);
}
