using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Libs;
using StbImageSharp;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>
/// 2D LIGHTING for the sidescroller scene: every quad drawn by the sprite / tilemap /
/// particle passes is shaded by the scene's Light markers.
///
/// The SAME Light editor objects the 3D viewport uses drive it, so there is nothing new
/// to author or serialize:
///   • Direct  → the global sun (Lights.SunDir / LightColor), at 10 mixed in in the XY plane.
///   • Point   → a lamp at the marker position with distance falloff (LightPointRadius).
///   • Spot    → the same lamp, narrowed by the marker's cone (LightConeAngle) in XY.
///
/// Sprites additionally sample their baked normal atlas (`&lt;sheet&gt;Normal.png` produced by
/// SpriteAtlasBaker) so Point/Spot lights reveal the relief of the art instead of a flat
/// wash. Tilemaps, parallax layers and particles have no normal atlas — they shade with
/// a flat (0,0,1) normal.
///
/// Uniform state is uploaded ONCE per frame per program (see <see cref="Apply"/>); the
/// per-draw callers only flip the lit flag / normal texture, which keeps hundreds of
/// sprites at a handful of uniform calls per frame.
/// </summary>
public static class SpriteLighting2D
{
    /// <summary>Master switch for 2D lighting (the 2D passes still draw, just unlit).</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Enable/disable the baked NORMAL TEXTURES (&lt;sheet&gt;Normal.png) for the 2D
    /// passes. Off = every 2D surface shades with a flat (0,0,1) normal — one less texture
    /// fetch per fragment and no per-sheet normal atlas load. Lighting itself keeps
    /// working. Persisted per project (settings.json → SpriteNormalMaps2D).</summary>
    public static bool NormalMapsEnabled { get; set; } = true;

    /// <summary>Maximum number of Point/Spot lights uploaded (mirrors the 3D cap).</summary>
    public const int MaxLights = Lights.MaxLocalLights;

    // How much of the sun's 3D direction becomes 2D spread; the nominal Z keeps flat
    // normals (0,0,1) lit instead of black when the light sits in the sprite plane.
    private const float LightElevation = 0.85f;
    private const float AmbientLight = 0.30f;
    private const float AmbientFloor = 0.06f;
    private const float SunStrength = 0.85f;

    // ── Per-frame captured light state (see Capture) ──
    private static Vector3 _ambient = new(0.32f);
    private static Vector3 _sunDir = Vector3.Normalize(new Vector3(0f, 1f, LightElevation));
    private static Vector3 _sunColor = new(0.85f);
    private static int _lightCount;
    private static readonly Lights.LocalLightData[] _lights = new Lights.LocalLightData[MaxLights];

    private static int _frame;
    private static uint _program;
    private static bool _lit;
    private static int _uploadedFrame = -1;

    // ── GPU resources ──
    private static readonly Dictionary<string, uint> _normalTextures = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Locs
    {
        public int Lit = -1, Ambient = -1, SunDir = -1, SunColor = -1;
        public int Count = -1, NormalTex = -1, HasNormal = -1;
        public readonly int[] Type = new int[MaxLights];
        public readonly int[] Pos = new int[MaxLights];
        public readonly int[] Dir = new int[MaxLights];
        public readonly int[] Color = new int[MaxLights];
        public readonly int[] Intensity = new int[MaxLights];
        public readonly int[] Range = new int[MaxLights];
        public readonly int[] Cone = new int[MaxLights];

        public static Locs For(uint program)
        {
            if (_locs.TryGetValue(program, out var cached)) return cached;
            var l = new Locs
            {
                Lit = GL.GetUniformLocation(program, "u_lit2D"),
                Ambient = GL.GetUniformLocation(program, "u_ambient2D"),
                SunDir = GL.GetUniformLocation(program, "u_sunDir2D"),
                SunColor = GL.GetUniformLocation(program, "u_sunColor2D"),
                Count = GL.GetUniformLocation(program, "u_lightCount"),
                NormalTex = GL.GetUniformLocation(program, "u_normalTex"),
                HasNormal = GL.GetUniformLocation(program, "u_hasNormal2D"),
            };
            for (int i = 0; i < MaxLights; i++)
            {
                l.Type[i] = GL.GetUniformLocation(program, $"u_lightType[{i}]");
                l.Pos[i] = GL.GetUniformLocation(program, $"u_lightPos[{i}]");
                l.Dir[i] = GL.GetUniformLocation(program, $"u_lightDir[{i}]");
                l.Color[i] = GL.GetUniformLocation(program, $"u_lightColor[{i}]");
                l.Intensity[i] = GL.GetUniformLocation(program, $"u_lightIntensity[{i}]");
                l.Range[i] = GL.GetUniformLocation(program, $"u_lightRange[{i}]");
                l.Cone[i] = GL.GetUniformLocation(program, $"u_lightCone[{i}]");
            }
            _locs[program] = l;
            return l;
        }
    }

    private static readonly Dictionary<uint, Locs> _locs = new();

    /// <summary>
    /// Snapshot the frame's lighting from the shared <see cref="Lights"/> instance: the
    /// sun (Direct / procedural day-night) plus every collected Point/Spot marker.
    /// Call once per rendered frame, before the 2D passes.
    /// </summary>
    public static void Capture(Lights? light)
    {
        _frame++;
        _lightCount = 0;

        if (light == null || !Enabled)
        {
            _ambient = new Vector3(0.35f);
            _sunDir = Vector3.Normalize(new Vector3(0f, 1f, LightElevation));
            _sunColor = light == null ? Vector3.Zero : light.LightColor;
            return;
        }

        Vector3 c = light.LightColor;
        _ambient = c * AmbientLight + new Vector3(AmbientFloor);
        _sunColor = c * SunStrength;

        // Only the XY part of the sun direction matters on the sprite plane; a sun that
        // points straight at the camera falls back to "from above".
        var xy = new Vector2(light.SunDir.X, light.SunDir.Y);
        if (xy.LengthSquared() < 1e-6f) xy = new Vector2(0f, 1f);
        xy = Vector2.Normalize(xy);
        _sunDir = Vector3.Normalize(new Vector3(xy.X, xy.Y, LightElevation));

        var src = light.LocalLights;
        int n = Math.Min(src.Count, MaxLights);
        for (int i = 0; i < n; i++) _lights[i] = src[i];
        _lightCount = n;
    }

    /// <summary>Upload the captured lights to <paramref name="program"/> and turn lighting on.</summary>
    public static void Apply(uint program) => Upload(program, lit: true);

    /// <summary>Turn lighting off for this program (editor aids: collision/trigger overlays, UI quads).</summary>
    public static void ApplyUnlit(uint program) => Upload(program, lit: false);

    private static void Upload(uint program, bool lit)
    {
        if (program == 0) return;
        if (_program == program && _lit == lit && _uploadedFrame == _frame) return;
        _program = program;
        _lit = lit;
        _uploadedFrame = _frame;

        var l = Locs.For(program);
        if (l.Lit >= 0) GL.Uniform1i(l.Lit, lit ? 1 : 0);
        if (!lit) return;

        if (l.Ambient >= 0) GL.Uniform3f(l.Ambient, _ambient.X, _ambient.Y, _ambient.Z);
        if (l.SunDir >= 0) GL.Uniform3f(l.SunDir, _sunDir.X, _sunDir.Y, _sunDir.Z);
        if (l.SunColor >= 0) GL.Uniform3f(l.SunColor, _sunColor.X, _sunColor.Y, _sunColor.Z);
        if (l.Count >= 0) GL.Uniform1i(l.Count, _lightCount);
        if (l.NormalTex >= 0) GL.Uniform1i(l.NormalTex, 1); // normal atlas lives on unit 1

        for (int i = 0; i < _lightCount; i++)
        {
            var s = _lights[i];
            if (l.Type[i] >= 0) GL.Uniform1i(l.Type[i], s.Type);
            if (l.Pos[i] >= 0) GL.Uniform3f(l.Pos[i], s.Position.X, s.Position.Y, s.Position.Z);
            if (l.Dir[i] >= 0) GL.Uniform3f(l.Dir[i], s.Direction.X, s.Direction.Y, s.Direction.Z);
            if (l.Color[i] >= 0) GL.Uniform3f(l.Color[i], s.Color.X, s.Color.Y, s.Color.Z);
            if (l.Intensity[i] >= 0) GL.Uniform1f(l.Intensity[i], s.Intensity);
            if (l.Range[i] >= 0) GL.Uniform1f(l.Range[i], s.Range);
            if (l.Cone[i] >= 0)
            {
                float half = s.ConeAngleDeg * MathF.PI / 180f;
                GL.Uniform2f(l.Cone[i], MathF.Cos(half), MathF.Cos(MathF.Min(half, half * 0.85f)));
            }
        }
    }

    /// <summary>Bind a sheet's normal atlas on texture unit 1 and flag the shader to sample it.
    /// Pass 0 to fall back to the flat normal for this draw.</summary>
    public static void BindNormal(uint program, uint normalTexId)
    {
        if (program == 0) return;
        if (!NormalMapsEnabled) normalTexId = 0;
        var l = Locs.For(program);
        if (l.HasNormal >= 0) GL.Uniform1i(l.HasNormal, normalTexId != 0 ? 1 : 0);
        if (normalTexId == 0) return;
        if (l.NormalTex >= 0) GL.Uniform1i(l.NormalTex, 1);
        GL.ActiveTexture(Const.GL_TEXTURE1);
        GL.BindTexture(Const.GL_TEXTURE_2D, normalTexId);
        GL.ActiveTexture(Const.GL_TEXTURE0);
    }

    /// <summary>The baked normal atlas that belongs to a sprite sheet:
    /// `&lt;sheet name&gt;Normal.png` (or `&lt;image name&gt;Normal.png`) next to the color atlas.
    /// False = no atlas on disk; the shader then uses a flat normal for that sheet.
    /// Loaded lazily, once per sheet, and re-read after <see cref="InvalidateNormalMaps"/>.</summary>
    public static bool TryGetNormalTexture(SpriteSheet? sheet, out uint texId)
    {
        texId = 0;
        if (!Enabled || !NormalMapsEnabled || sheet == null || string.IsNullOrEmpty(sheet.ImagePath)) return false;

        string resolved = PathHelpers.ResolveProjectAsset(sheet.ImagePath);
        if (!_normalTextures.TryGetValue(resolved, out texId))
        {
            texId = LoadNormalTexture(sheet, resolved);
            _normalTextures[resolved] = texId;
        }
        return texId != 0;
    }

    /// <summary>Drop every cached normal atlas (project switch, sprite re-bake, editor reload).
    /// Textures themselves are deleted on the GL thread by the next load.</summary>
    public static void InvalidateNormalMaps()
    {
        foreach (uint tex in _normalTextures.Values)
        {
            if (tex == 0) continue;
            uint id = tex;
            unsafe { GL.DeleteTextures(1, &id); }
        }
        _normalTextures.Clear();
        _uploadedFrame = -1; // uniforms are still valid, but the next frame re-captures anyway
    }

    private static uint LoadNormalTexture(SpriteSheet sheet, string resolvedImage)
    {
        try
        {
            string folder = Path.GetDirectoryName(resolvedImage) ?? "";
            if (folder.Length == 0) return 0;

            string? path = null;
            foreach (string name in new[] { sheet.Name + "Normal.png",
                                            Path.GetFileNameWithoutExtension(resolvedImage) + "Normal.png" })
            {
                string candidate = Path.Combine(folder, name);
                if (File.Exists(candidate)) { path = candidate; break; }
            }
            if (path == null) return 0;

            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            uint texId = 0;
            unsafe
            {
                GL.GenTextures(1, &texId);
                GL.BindTexture(Const.GL_TEXTURE_2D, texId);
                fixed (byte* ptr = image.Data)
                {
                    GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                        image.Width, image.Height, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, ptr);
                }
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
                GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            }
            Console.WriteLine($"[SpriteLighting2D] Normal atlas for '{sheet.Name}': {Path.GetFileName(path)} ({image.Width}x{image.Height})");
            return texId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpriteLighting2D] Could not load the normal atlas for '{sheet.Name}': {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// GLSL shared by every 2D shader that wants lighting (sprite/tilemap/particle passes):
    /// the uniforms, the light loop and the ambient/sun term. The host shader appends its
    /// own <c>main()</c> after it and calls <c>Apply2DLighting(color, uv, worldPos)</c>.
    /// </summary>
    public const string GlslLighting = @"
uniform sampler2D u_normalTex;
uniform int u_hasNormal2D;
uniform int u_lit2D;
uniform vec3 u_ambient2D;      // ambient (already scaled by the scene's light color)
uniform vec3 u_sunDir2D;       // normalized direction TO the sun (z > 0)
uniform vec3 u_sunColor2D;     // sun color * intensity
#define SPRITE_MAX_LIGHTS 8
uniform int u_lightCount;
uniform int u_lightType[SPRITE_MAX_LIGHTS];     // 1 = Point, 2 = Spotlight
uniform vec3 u_lightPos[SPRITE_MAX_LIGHTS];
uniform vec3 u_lightDir[SPRITE_MAX_LIGHTS];     // spotlight: beam direction
uniform vec3 u_lightColor[SPRITE_MAX_LIGHTS];
uniform float u_lightIntensity[SPRITE_MAX_LIGHTS];
uniform float u_lightRange[SPRITE_MAX_LIGHTS];
uniform vec2 u_lightCone[SPRITE_MAX_LIGHTS];    // x = cos(outer), y = cos(inner)

vec3 Apply2DLighting(vec3 albedo, vec2 uv, vec3 worldPos) {
    if (u_lit2D == 0) return albedo;

    // Tangent-space normal from the baked atlas, converted to world space. The atlas is
    // baked in image space (+y = down), so flip Y; flat pixels keep nz = 1.
    vec3 n = vec3(0.0, 0.0, 1.0);
    if (u_hasNormal2D != 0) {
        vec3 s = texture(u_normalTex, uv).xyz * 2.0 - 1.0;
        n = normalize(vec3(s.x, -s.y, max(s.z, 0.05)));
    }

    vec3 lit = u_ambient2D + u_sunColor2D * max(dot(n, u_sunDir2D), 0.0);

    for (int i = 0; i < SPRITE_MAX_LIGHTS; i++) {
        if (i >= u_lightCount) break;
        vec2 toLight = u_lightPos[i].xy - worldPos.xy;
        float dist = length(toLight);
        vec2 dirToLight = toLight / max(dist, 0.0001);

        float range = max(u_lightRange[i], 0.1);
        float d = clamp(dist / range, 0.0, 1.0);
        float attenuation = 1.0 - d * d;
        attenuation *= attenuation;

        if (u_lightType[i] == 2) { // Spotlight: 2D cone around the marker's beam.
            vec2 beam = normalize(u_lightDir[i].xy + vec2(0.00001, 0.0));
            float theta = dot(-dirToLight, beam);
            float epsilon = max(u_lightCone[i].y - u_lightCone[i].x, 0.001);
            attenuation *= clamp((theta - u_lightCone[i].x) / epsilon, 0.0, 1.0);
        }
        if (attenuation <= 0.001) continue;

        // A little nominal depth keeps flat (camera-facing) quads lit by a lamp that
        // sits in the same plane, while normal-mapped relief still varies.
        vec3 L = normalize(vec3(dirToLight, 0.85));
        lit += u_lightColor[i] * u_lightIntensity[i] * attenuation * max(dot(n, L), 0.0);
    }

    return albedo * min(lit, vec3(4.0));
}
";
}
