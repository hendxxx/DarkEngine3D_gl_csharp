#version 330 core

// ── Selection outline — vertex stage ─────────────────────────────────────────
// Technique: vertex extrusion performed in CLIP space ("screen-space inverted
// hull"), see "5 ways to draw an outline" — ameye.dev/notes/rendering-outlines.
//
// The duplicated hull is pushed outward along its projected normal by a width
// expressed in FRAMEBUFFER PIXELS:
//
//   clipPos.xy += normalize(normalClip.xy) / viewportSize * widthPx * 2 * clipPos.w
//
// * dividing by the viewport size converts pixels to NDC (1 px == 2/size);
// * multiplying by clipPos.w cancels the perspective divide that follows, so
//   the offset survives unchanged in screen space.
//
// That is what makes the outline equally thick at every distance, for every
// object scale and on every aspect ratio — the old approach scaled the model by
// a fixed factor, so the width depended on the object's size and its distance
// from the camera.
//
// Pass 1 (mask) runs with u_OutlineWidthPx == 0: the mesh is drawn exactly on
// top of the scene geometry and only writes stencil. Pass 2 runs with a non-zero
// width and is masked by that stencil, so only the band outside the silhouette
// is coloured.
//
// Skinned meshes are posed here as well (same math as gltf_vertex.glsl), so the
// mask and the hull follow the animated silhouette instead of the bind pose.

layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 3) in vec4 aBoneWeights;
layout(location = 4) in ivec4 aBoneIds;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

// Outline thickness in framebuffer pixels. 0 disables the extrusion (mask pass).
uniform float u_OutlineWidthPx = 0.0;
// Framebuffer size in pixels, used to convert the width from pixels to NDC.
uniform vec2 u_ViewportSize = vec2(0.0);

const int MAX_JOINTS = 128;
uniform mat4 u_Joints[MAX_JOINTS];
// Editor primitives have no skeleton, so this stays 0 and they are drawn as-is.
uniform int u_UseSkinning = 0;

void main()
{
    vec4 localPos = vec4(aPos, 1.0);
    vec3 localNormal = aNormal;

    float wsum = aBoneWeights.x + aBoneWeights.y + aBoneWeights.z + aBoneWeights.w;
    if (u_UseSkinning != 0 && wsum > 1e-6)
    {
        vec4 w = aBoneWeights / wsum;
        ivec4 ids = clamp(aBoneIds, 0, MAX_JOINTS - 1);

        mat4 skinMat =
            w.x * u_Joints[ids.x] +
            w.y * u_Joints[ids.y] +
            w.z * u_Joints[ids.z] +
            w.w * u_Joints[ids.w];

        localPos = skinMat * localPos;
        localNormal = mat3(skinMat) * localNormal;
    }

    vec4 clipPos = projection * view * model * localPos;

    if (u_OutlineWidthPx > 0.0 && u_ViewportSize.x > 0.0 && u_ViewportSize.y > 0.0)
    {
        // Project the normal with the same matrices as the position so the
        // direction is correct in NDC (this also folds in the aspect ratio).
        vec3 normalClip = (projection * view * model * vec4(localNormal, 0.0)).xyz;
        vec2 dir = normalClip.xy;
        float len = length(dir);

        // A missing/zero normal leaves the vertex untouched instead of shooting
        // it off in an arbitrary direction.
        if (len > 1e-5)
        {
            vec2 offsetNdc = (dir / len) * (2.0 * u_OutlineWidthPx / u_ViewportSize);
            clipPos.xy += offsetNdc * clipPos.w;
        }
    }

    gl_Position = clipPos;
}
