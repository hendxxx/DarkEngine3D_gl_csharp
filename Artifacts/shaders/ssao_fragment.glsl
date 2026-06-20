#version 330 core

in vec2 texCoords;
out float FragColor;

uniform sampler2D depthTex;

// Projection matrix parameters for reconstructing view-space position
uniform mat4 projection;
uniform mat4 invProjection;
uniform float radius;      // SSAO sampling radius
uniform float bias;        // depth bias
uniform float power;       // AO power/contrast

// Hemisphere sample kernel (32 samples, passed as uniform array)
const int KERNEL_SIZE = 32;
uniform vec3 samples[32];

// Reconstruct view-space position from depth buffer
vec3 ReconstructViewPos(vec2 uv, float depth)
{
    float z = depth * 2.0 - 1.0;
    vec4 clipPos = vec4(uv * 2.0 - 1.0, z, 1.0);
    vec4 viewPos = invProjection * clipPos;
    return viewPos.xyz / viewPos.w;
}

// Compute view-space normal from depth buffer using explicit UV-offset sampling
vec3 ReconstructNormal(vec2 uv, vec3 viewPos)
{
    vec2 texelSize = 1.0 / vec2(textureSize(depthTex, 0));

    float dL = texture(depthTex, uv + vec2(-texelSize.x, 0.0)).r;
    float dR = texture(depthTex, uv + vec2( texelSize.x, 0.0)).r;
    float dD = texture(depthTex, uv + vec2(0.0,  texelSize.y)).r;
    float dU = texture(depthTex, uv + vec2(0.0, -texelSize.y)).r;

    vec3 pL = ReconstructViewPos(uv + vec2(-texelSize.x, 0.0), dL);
    vec3 pR = ReconstructViewPos(uv + vec2( texelSize.x, 0.0), dR);
    vec3 pD = ReconstructViewPos(uv + vec2(0.0,  texelSize.y), dD);
    vec3 pU = ReconstructViewPos(uv + vec2(0.0, -texelSize.y), dU);

    vec3 dx = pR - pL;
    vec3 dy = pU - pD;
    vec3 normal = cross(dx, dy);
    float len = length(normal);

    if (len < 0.001)
        normal = normalize(-viewPos);
    else
        normal /= len;

    // Face-viewer flip: ensure normal points toward camera.
    // The hemisphere samples (z>0) then point toward camera + tangent plane,
    // which is the correct direction for SSAO — we sample the space IN FRONT
    // of the surface (between camera and surface) to detect occlusion from
    // nearby geometry. Samples that go behind camera are caught by offset.w check.
    if (dot(normal, -viewPos) < 0.0)
        normal = -normal;
    return normal;
}

void main()
{
    vec2 flippedUV = vec2(texCoords.x, 1.0 - texCoords.y);
    float depth = texture(depthTex, flippedUV).r;

    if (depth >= 1.0)
    {
        FragColor = 1.0;
        return;
    }

    vec3 viewPos = ReconstructViewPos(flippedUV, depth);
    vec3 hemiAxis = ReconstructNormal(flippedUV, viewPos);

    // Build tangent space around hemiAxis
    vec3 tangent = normalize(cross(hemiAxis, vec3(0.0, 1.0, 0.0)));
    if (length(cross(hemiAxis, vec3(0.0, 1.0, 0.0))) < 0.001)
        tangent = normalize(cross(hemiAxis, vec3(1.0, 0.0, 0.0)));
    vec3 bitangent = cross(hemiAxis, tangent);

    float occlusion = 0.0;
    int validSamples = 0;

    for (int i = 0; i < KERNEL_SIZE; i++)
    {
        // Transform sample from tangent space to view space
        vec3 sampleDir = samples[i].x * tangent + samples[i].y * bitangent + samples[i].z * hemiAxis;
        vec3 samplePos = viewPos + sampleDir * radius;

        vec4 offset = projection * vec4(samplePos, 1.0);
        offset.xy /= offset.w;
        offset.xy = offset.xy * 0.5 + 0.5;

        // Skip behind-camera samples (when w <= 0, division flips XY sign
        // and UV can land inside [0,1], sampling wrong depth values)
        if (offset.w <= 0.0 || offset.x < 0.0 || offset.x > 1.0 || offset.y < 0.0 || offset.y > 1.0)
            continue;
        validSamples++;

        float sampleDepth = texture(depthTex, offset.xy).r;

        // Reconstruct the actual geometry's view-space Z at the sample's UV
        float sampleZ = sampleDepth * 2.0 - 1.0;
        vec4 clipSample = vec4(offset.xy * 2.0 - 1.0, sampleZ, 1.0);
        vec4 viewSample = invProjection * clipSample;
        float geometryZ = viewSample.z / viewSample.w;

        // Occlusion: geometry closer to camera than sample?
        // In view space, closer = GREATER Z (less negative, e.g. -3 > -5).
        bool isOccluded = geometryZ >= samplePos.z + bias;

        // Range check: ignore samples too far in depth from center
        float rangeCheck = smoothstep(0.0, 2.0, radius / (abs(viewPos.z - geometryZ) + 0.001));
        occlusion += (isOccluded ? 1.0 : 0.0) * rangeCheck;
    }

    if (validSamples > 0)
        occlusion = 1.0 - (occlusion / float(validSamples));
    else
        occlusion = 1.0;

    FragColor = pow(clamp(occlusion, 0.0, 1.0), power);
}
