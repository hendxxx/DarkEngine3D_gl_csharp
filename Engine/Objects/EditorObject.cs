using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Serialization;
using DarkEngine3D_gl_csharp.Engine.Visual;
using DarkEngine3D_gl_csharp.Engine.Libs;
using ImGuiNET;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Inputs;
using static DarkEngine3D_gl_csharp.Engine.Helpers.ObjectHelpers;

namespace DarkEngine3D_gl_csharp.Engine.Objects;

/// <summary>
/// A user-defined animation action for Player2D (Idle/Walk/Run/... + custom).
/// Binds an action name to a sprite-sheet clip, a keyboard key, and a priority —
/// higher-priority actions interrupt lower ones; Dead (100) cancels everything.
/// </summary>
public class Player2DAction
{
    public string Name { get; set; } = "";
    /// <summary>Sprite sheet name from the Sprite Editor registry ("" = player's sheet).</summary>
    public string SpriteSheet { get; set; } = "";
    /// <summary>Animation clip name within the sheet.</summary>
    public string Clip { get; set; } = "";
    public bool Loop { get; set; } = true;
    /// <summary>Priority — a playing action can only be replaced by equal/higher priority.
    /// Dead = 100 cancels everything; Walk = 5 is interrupted by Attack/Skills.</summary>
    public int Priority { get; set; } = 5;
    /// <summary>Keyboard binding (ImGuiKey name, e.g. "J", "None" = not bound).</summary>
    public string KeyBinding { get; set; } = "None";
}

/// <summary>
/// Type of editor-placed 3D primitive.
/// </summary>
public enum EditorPrimitiveType
{
    Plane,
    Box,
    Sphere,
    GlbReference,
    Camera,
    Light,
    Sky,
    Map2D,
    /// <summary>2D player character: capsule collider + animated sprite from a
    /// sprite-sheet clip. Spawned at the Start2D object's position in-game.</summary>
    Player2D,
    /// <summary>Player spawn marker. In preview/in-game the Player2D is placed here.</summary>
    Start2D,
    /// <summary>Camera start marker for 2D levels: preview/in-game cameras begin here
    /// (position = camera center, Scale.Y>0 marker field CameraStartZoom = ortho zoom).</summary>
    CameraStart2D
}

/// <summary>
/// Type of light source for directional, point, or spotlight effects.
/// </summary>
public enum LightType
{
    Direct = 0,      // Directional light (sun-like, parallel rays)
    Point = 1,       // Point light (omnidirectional from a location)
    Spotlight = 2    // Directional spotlight with cone angle
}

/// <summary>
/// Represents a user-placed 3D object in the editor scene.
/// Can be a Plane, Box, Sphere, or a reference to a .glb file.
/// Contains all properties needed for rendering, shadow casting, and gizmo interaction.
/// </summary>
/// <summary>
/// A single dynamic terrain layer. Contains albedo texture, PBR maps, tiling,
/// height blending range, and texture sampling settings.
/// Layers stack from bottom (index 0) to top. HeightMin/HeightMax define where
/// this layer blends in (smooth transition at edges).
/// </summary>
public class TerrainLayer
{
    public const int MaxPbrMaps = 6; // normal, metallic, roughness, ao, height, emission

    // ── Identity ──
    public string Name { get; set; } = "Base";
    public bool Visible { get; set; } = true;

    // ── Albedo ──
    public string AlbedoPath { get; set; } = "Artifacts/Textures/default.jpg";

    // ── PBR maps (index 0=normal, 1=metallic, 2=roughness, 3=ao, 4=height, 5=emission) ──
    public string?[] PbrPaths { get; set; } = new string?[MaxPbrMaps];

    // ── Tiling ──
    public float TilingX { get; set; } = 0.5f;
    public float TilingY { get; set; } = 0.5f;

    // ── Height blending (normalized 0..1) ──
    public float HeightMin { get; set; } = 0.0f;
    public float HeightMax { get; set; } = 1.0f;
    public float BlendSharpness { get; set; } = 2.0f; // 1=smooth, higher=sharper

    // ── Texture sampling ──
    public Libs.TextureSettings? TextureSettings { get; set; } = null;
    public bool StochasticSampling { get; set; } = false;

    // ── PBR tuning (per layer) ──
    public float NormalStrength { get; set; } = 1.0f;
    public float NormalBlur { get; set; } = 0.0f;
    public float MetallicThreshold { get; set; } = 0.5f;
    public float MetallicSoftness { get; set; } = 0.1f;
    public float MetallicStrength { get; set; } = 1.0f;
    public float RoughnessStrength { get; set; } = 1.0f;
    public bool RoughnessInvert { get; set; } = false;
    public float AoStrength { get; set; } = 1.0f;
    public float AoBrightness { get; set; } = 0.0f;
    public float HeightStrength { get; set; } = 1.0f;
    public bool HeightInvert { get; set; } = false;
    public float HeightBlur { get; set; } = 0.0f;
    public float EmissionIntensity { get; set; } = 1.0f;
    public float AlbedoBrightness { get; set; } = 1.0f;
    public float AlbedoSaturation { get; set; } = 1.0f;
    public float AlbedoContrast { get; set; } = 1.0f;

    // ── Slope-specific ──
    public float SlopeThreshold { get; set; } = 0.35f; // only used for slope layer

    // ── Factory ──
    public static TerrainLayer CreateDefault() => new() { Name = "Layer 1", HeightMin = 0f, HeightMax = 1f };
    public static TerrainLayer CreateSlope() => new() { Name = "Slope", HeightMin = 0f, HeightMax = 1f, SlopeThreshold = 0.35f };

    public TerrainLayer Clone()
    {
        var c = (TerrainLayer)MemberwiseClone();
        c.PbrPaths = (string?[])PbrPaths.Clone();
        c.TextureSettings = TextureSettings?.Clone();
        return c;
    }

    public string? GetPbrPath(int mapType) => mapType >= 0 && mapType < MaxPbrMaps ? PbrPaths[mapType] : null;
    public void SetPbrPath(int mapType, string? path) { if (mapType >= 0 && mapType < MaxPbrMaps) PbrPaths[mapType] = path; }

    public TerrainLayer WithRelativePaths()
    {
        var c = Clone();
        c.AlbedoPath = PathHelpers.MakeRelative(c.AlbedoPath);
        for (int i = 0; i < MaxPbrMaps; i++)
            if (c.PbrPaths[i] != null) c.PbrPaths[i] = PathHelpers.MakeRelative(c.PbrPaths[i]!);
        return c;
    }

    public TerrainLayer WithResolvedPaths()
    {
        var c = Clone();
        c.AlbedoPath = PathHelpers.Resolve(c.AlbedoPath);
        for (int i = 0; i < MaxPbrMaps; i++)
            if (c.PbrPaths[i] != null) c.PbrPaths[i] = PathHelpers.Resolve(c.PbrPaths[i]!);
        return c;
    }
}

/// <summary>
/// Per-layer PBR configuration for advanced terrain. PBR is per texture: each of the
/// 5 layers (air, dirt, grass, snow, slope) owns its own 6 companion maps (normal /
/// metallic / roughness / AO / height / emission) and its own unique tuning values —
/// there is no shared "global" PBR look anymore. A layer's albedo lives in the matching
/// TerrainTexture*Path property.
/// <para>Map paths are nullable: <c>null</c> = not set yet (auto-discovered next to the
/// albedo when the terrain builds), <c>""</c> = explicitly cleared (neutral default, no
/// map loaded), any other value = explicit path.</para>
/// </summary>
public class TerrainPbrLayerData
{
    public string? NormalPath { get; set; }
    public string? MetallicPath { get; set; }
    public string? RoughnessPath { get; set; }
    public string? AoPath { get; set; }
    public string? HeightPath { get; set; }
    public string? EmissionPath { get; set; }

    public float AlbedoBrightness { get; set; } = 1f;
    public float AlbedoSaturation { get; set; } = 1f;
    public float AlbedoContrast { get; set; } = 1f;
    public float NormalStrength { get; set; } = 1f;
    public float NormalBlur { get; set; } = 0f;
    public float MetallicThreshold { get; set; } = 0.5f;
    public float MetallicSoftness { get; set; } = 0.1f;
    public float MetallicStrength { get; set; } = 1f;
    public float RoughnessStrength { get; set; } = 1f;
    public bool RoughnessInvert { get; set; } = false;
    public float AoStrength { get; set; } = 1f;
    public float AoBrightness { get; set; } = 0f;
    public float HeightStrength { get; set; } = 1f;
    public bool HeightInvert { get; set; } = false;
    public float HeightBlur { get; set; } = 0f;
    public float EmissionIntensity { get; set; } = 1f;

    public TerrainPbrLayerData Clone() => (TerrainPbrLayerData)MemberwiseClone();

    /// <summary>Map type index → path (1=normal … 6=emission). 0 = albedo — lives in the
    /// matching TerrainTexture*Path property, so it returns null here.</summary>
    public string? GetPath(int t) => t switch
    {
        0 => null,
        1 => NormalPath, 2 => MetallicPath, 3 => RoughnessPath,
        4 => AoPath, 5 => HeightPath, _ => EmissionPath,
    };

    public void SetPath(int t, string? path)
    {
        switch (t)
        {
            case 0: return; // albedo lives in the matching TerrainTexture*Path property
            case 1: NormalPath = path; break;
            case 2: MetallicPath = path; break;
            case 3: RoughnessPath = path; break;
            case 4: AoPath = path; break;
            case 5: HeightPath = path; break;
            default: EmissionPath = path; break;
        }
    }

    /// <summary>Clone with all map paths stored relative to the exe (for saving).</summary>
    public TerrainPbrLayerData WithRelativePaths()
    {
        var c = Clone();
        c.NormalPath = NormalPath == null ? null : PathHelpers.MakeRelative(NormalPath);
        c.MetallicPath = MetallicPath == null ? null : PathHelpers.MakeRelative(MetallicPath);
        c.RoughnessPath = RoughnessPath == null ? null : PathHelpers.MakeRelative(RoughnessPath);
        c.AoPath = AoPath == null ? null : PathHelpers.MakeRelative(AoPath);
        c.HeightPath = HeightPath == null ? null : PathHelpers.MakeRelative(HeightPath);
        c.EmissionPath = EmissionPath == null ? null : PathHelpers.MakeRelative(EmissionPath);
        return c;
    }

    /// <summary>Clone with all map paths resolved to absolute (for loading).</summary>
    public TerrainPbrLayerData WithResolvedPaths()
    {
        var c = Clone();
        c.NormalPath = NormalPath == null ? null : PathHelpers.Resolve(NormalPath);
        c.MetallicPath = MetallicPath == null ? null : PathHelpers.Resolve(MetallicPath);
        c.RoughnessPath = RoughnessPath == null ? null : PathHelpers.Resolve(RoughnessPath);
        c.AoPath = AoPath == null ? null : PathHelpers.Resolve(AoPath);
        c.HeightPath = HeightPath == null ? null : PathHelpers.Resolve(HeightPath);
        c.EmissionPath = EmissionPath == null ? null : PathHelpers.Resolve(EmissionPath);
        return c;
    }
}/// <summary>Runtime render data for one Map2D parallax layer. The Map Editor panel
/// pushes these onto <see cref="EditorObject.Map2dParallaxLayers"/> each time a layer is
/// added/edited; <see cref="DrawMap2D"/> draws each as an upright textured quad at a
/// world Z offset derived from the layer's ZPosition (positive = in front of the grid,
/// negative = behind it), matching the sidescroller depth convention.</summary>
public class MapParallaxRenderLayer
{
    public string Name = "";
    public string ImagePath = "";
    public bool IsVisible = true;
    /// <summary>Depth factor: world-Z offset in tile cells. &gt; 0 = in front of the
    /// grid, &lt; 0 = behind it.</summary>
    public float ZPosition;
    public float Alpha = 1f;
    public bool TileHorizontal = true;
    /// <summary>Scroll speed multiplier for the parallax preview: while the editor
    /// camera pans, each layer slides horizontally by -camX × ScrollFactor — the same
    /// rule the runtime background uses — so the depth illusion is visible while editing.
    /// 0 = static, 1 = locked to the camera, 0.5 = half speed.</summary>
    public float ScrollFactor = 0.5f;
    /// <summary>GPU texture ID (owned/loaded by the Map Editor panel).</summary>
    public uint TextureId;
    /// <summary>Image size in pixels (natural texture size, used for auto sizing).</summary>
    public int ImageWidth;
    public int ImageHeight;
    /// <summary>Quad width in pixels. 0 = auto: follow the map grid width.</summary>
    public float WidthPx;
    /// <summary>Quad height in pixels. 0 = auto: follow the map grid height.</summary>
    public float HeightPx;
    /// <summary>Horizontal texture repeats across the quad. 0 = auto (image shown once
    /// per natural size; TileHorizontal still extends tiling across the grid).</summary>
    public int RepeatX;
    /// <summary>Vertical texture repeats across the quad. 0 = auto.</summary>
    public int RepeatY;
    /// <summary>Left offset in pixels from the grid's left edge (negative = extend left).</summary>
    public float LeftPx;
    /// <summary>Top offset in pixels pushing the quad's top edge DOWN from the grid's top
    /// edge (0 = flush with grid top; negative = extend above the grid).</summary>
    public float TopPx;
}

public unsafe class EditorObject
{
    // ── Identity ──
    public string Name { get; set; } = "EditorObject";
    public EditorPrimitiveType PrimitiveType { get; set; } = EditorPrimitiveType.Box;

    // ── Transform ──
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Vector3 RotationEuler { get; set; } = Vector3.Zero; // degrees
    public Vector3 Scale { get; set; } = Vector3.One;

    // ── Visual ──
    public Vector3 Color { get; set; } = new(0.8f, 0.8f, 0.9f);
    public string? TexturePath { get; set; } = null;
    public bool CastShadow { get; set; } = true;
    public bool IsVisible { get; set; } = true;

    // ── Player2D: sprite animation + capsule collider ──
    /// <summary>Sprite sheet name (from Sprite Editor) driving the player sprite.</summary>
    public string Player2DSpriteSheet { get; set; } = "";
    /// <summary>Animation clip name (from Sprite Editor) played on the player sprite.
    /// Clip carries its own FPS/loop/reverse/speed settings. Doubles as the IDLE clip —
    /// played whenever no action overrides it. ALL other states (Walk/Run/Jump/Attack/
    /// custom) are configured through the Animation Actions system (Player2DActions).</summary>
    public string Player2DAnimationClip { get; set; } = "";
    /// <summary>Sprite height in world units. Width derives from the sheet's frame aspect.
    /// Used as the BASE height reference. Each animation frame is scaled proportionally
    /// to this height based on its native pixel size, so all animations render at the same
    /// visual size regardless of their original frame dimensions (idle 64px, run 80px, etc.).</summary>
    public float Player2DHeight { get; set; } = 2f;
    // Per-sheet master-height normalization + render offsets live on SpriteSheet
    // (Sprite Editor → Sheet Settings → Render Normalization) — shared by all objects
    // using that sheet, serialized in sprites.sheets.json.
    /// <summary>Capsule collider radius in world units.</summary>
    public float Player2DCapsuleRadius { get; set; } = 0.35f;
    /// <summary>Capsule collider total height in world units.</summary>
    public float Player2DCapsuleHeight { get; set; } = 1.8f;
    /// <summary>Capsule collider horizontal offset from the object position (world units).
    /// With the left-bottom sprite anchor, the physics body usually needs shifting right
    /// into the character's body — nudge X (and Y for tall/short art) until the capsule
    /// hugs the visible character. Applied in BOTH the gizmo draw and the physics resolve.</summary>
    public float Player2DCapsuleOffsetX { get; set; } = 0f;
    /// <summary>Capsule collider vertical offset from the object position (world units).
    /// Positive lifts the capsule base off the ground line (e.g. art feet drawn above
    /// the anchor). Applied in BOTH the gizmo draw and the physics resolve.</summary>
    public float Player2DCapsuleOffsetY { get; set; } = 0f;
    /// <summary>Render the capsule outline (edit mode only — hidden in-game).</summary>
    public bool Player2DShowCapsule { get; set; } = true;
    /// <summary>Gravity acceleration (world units/s²) applied in preview/in-game.</summary>
    public float Player2DGravity { get; set; } = 25f;
    /// <summary>Runtime animation clock (seconds since play started). Editor ticks it too
    /// so the idle animation previews live in the viewport. Resets on state switches so
    /// each clip plays from its first frame.</summary>
    public float Player2DAnimTime { get; set; }
    /// <summary>Runtime state: true while the player has horizontal input (WALK clip),
    /// false when standing still (IDLE clip). Set by Player2DSystem, read by DrawPlayer2D.</summary>
    public bool Player2DMoving { get; set; }
    /// <summary>Runtime facing: true = sprite drawn normally (assumes right-facing art),
    /// false = mirrored horizontally so the character faces LEFT. Set by Player2DSystem.</summary>
    public bool Player2DFacingRight { get; set; } = true;
    /// <summary>Runtime physics velocity (Y only — sidescroller).</summary>
    public float Player2DVelocityY { get; set; }
    /// <summary>Current horizontal velocity (world units/s) — drives facing + walk/run anims.</summary>
    public float Player2DVelocityX { get; set; }
    /// <summary>Facing: 1 = right, -1 = left. Flipped automatically from Player2DVelocityX.</summary>
    public float Player2DFacing { get; set; } = 1f;
    /// <summary>True when standing on a collision tile this frame (grounded).
    /// Defaults TRUE so edit mode (which never runs physics) treats the player as
    /// standing — the locomotion resolver then picks Idle, not Jump.</summary>
    public bool Player2DGrounded { get; set; } = true;

    // ── Movement tuning (all live in the Inspector, used by Player2DSystem) ──
    /// <summary>Horizontal walk speed (world units/s). Sensible platformer default ≈ 1 tile/s.
    /// Raise Run Speed for a faster sprint (LeftShift).</summary>
    public float Player2DMoveSpeed { get; set; } = 8f;
    /// <summary>Horizontal run speed (world units/s) — used while LeftShift is held.</summary>
    public float Player2DRunSpeed { get; set; } = 14f;
    /// <summary>Initial upward velocity on jump (world units/s). Keep modest so airtime is snappy.
    /// With the default gravity (25) this gives ≈ 0.9s to apex then fall — raise Gravity if
    /// you want even shorter airtime.</summary>
    public float Player2DJumpForce { get; set; } = 9f;
    /// <summary>Multiplier on Player2DGravity.</summary>
    public float Player2DGravityScale { get; set; } = 1.6f;
    /// <summary>Ground horizontal acceleration (world units/s²). High values give immediate,
    /// non-slippery response (no ice-skating).</summary>
    public float Player2DAcceleration { get; set; } = 200f;
    /// <summary>Ground horizontal deceleration when no input (world units/s²). Keep ≥
    /// Acceleration so stopping is as snappy as starting.</summary>
    public float Player2DDeceleration { get; set; } = 220f;
    /// <summary>0..1 — how much of ground acceleration applies while airborne.
    /// Low values = no mid-air steering (classic platformer feel).</summary>
    public float Player2DAirControl { get; set; } = 0.25f;

    // ── Camera-follow tuning (used by the Player2DSystem camera follow) ──
    /// <summary>How fast the camera catches the target (higher = snappier).</summary>
    public float CameraFollowSpeed { get; set; } = 6f;
    /// <summary>Dead zone width in px — camera doesn't move while the player is inside it.</summary>
    public float CameraDeadZoneWidth { get; set; } = 96f;
    /// <summary>Dead zone height in px.</summary>
    public float CameraDeadZoneHeight { get; set; } = 64f;
    /// <summary>Camera rises only when the player climbs above this many px from the anchor.</summary>
    public float CameraVerticalThreshold { get; set; } = 64f;
    /// <summary>How fast the camera returns down to the player after a high climb.</summary>
    public float CameraReturnSpeed { get; set; } = 3f;
    /// <summary>Look-ahead distance in px toward the movement direction.</summary>
    public float CameraLookAhead { get; set; } = 150f;
    /// <summary>Optional per-camera 2D view offset (added after the follow/frame math).
    /// Default (0,0,0) = no offset. Lets the user nudge the ortho viewport position.</summary>
    public Vector3 CameraViewOffset { get; set; } = new(0, 0, 0);

    // ── Animation action system ──
    /// <summary>User-defined animation actions (Idle/Walk/... + custom). Persisted with the object.</summary>
    public List<Player2DAction> Actions { get; set; } = new();
    /// <summary>Currently playing action name ("" = base locomotion).</summary>
    public string Player2DCurrentAction { get; set; } = "";
    /// <summary>Time inside the current action (reset on action change).</summary>
    public float Player2DActionTime { get; set; }
    /// <summary>Static: request all Player2D objects to respawn at their Start2D marker
    /// on the NEXT update (set when in-game mode begins — objects may be re-created
    /// asynchronously by the .ing reload that follows, so spawning must be deferred).</summary>
    public static bool Player2DSpawnPending { get; set; }

    /// <summary>Static camera-follow state: true once the follow camera snapped to its
    /// start point for this play session (prevents a lerp swoop from the editor view).</summary>
    public static bool CameraFollowInitialized { get; set; }

    /// <summary>Ensure the default locomotion actions exist (Idle/Walk/Run/Jump/
    /// Jump Start/Jump End/Fall). Jump covers rising+falling in one clip; Jump Start
    /// /Jump End are the split variant: rise plays Jump Start (holds its last frame
    /// while airborne), the descent switches to Jump End.</summary>
    public void EnsureDefaultActions()
    {
        if (Actions.Count > 0) return;
        string[] defaults = ["Idle", "Walk", "Run", "Jump", "Jump Start", "Jump End", "Fall"];
        foreach (var n in defaults)
            Actions.Add(new Player2DAction { Name = n, SpriteSheet = Player2DSpriteSheet, Clip = Player2DAnimationClip });
    }

    // ── PBR material (Box/Sphere/flat-plane) — dedicated PBR shader with 7 optional
    //    maps + tuning. Every map is optional: missing maps keep neutral defaults
    //    (vertex color albedo, flat normal, 0 metallic, 0.6 roughness, 1 AO, no
    //    parallax, no emission), so the material degrades gracefully. ──
    /// <summary>Base-color / albedo map (optional). When empty, the vertex color is used.</summary>
    public string PbrAlbedoPath { get; set; } = "";
    /// <summary>Tangent-space normal map (optional; flat when absent).</summary>
    public string PbrNormalPath { get; set; } = "";
    /// <summary>Metallic mask (R channel) (optional; 0 when absent).</summary>
    public string PbrMetallicPath { get; set; } = "";
    /// <summary>Roughness map (R channel) (optional; 0.6 default when absent).</summary>
    public string PbrRoughnessPath { get; set; } = "";
    /// <summary>Ambient-occlusion map (R channel) (optional; 1 when absent).</summary>
    public string PbrAoPath { get; set; } = "";
    /// <summary>Height / displacement map (R channel, 0.5 = flat) (optional; drives parallax).</summary>
    public string PbrHeightPath { get; set; } = "";
    /// <summary>Emissive color map (optional; 0 when absent).</summary>
    public string PbrEmissionPath { get; set; } = "";
    /// <summary>UV tiling multiplier for all PBR maps on this object (legacy — new scenes
    /// store per-map tiling in <see cref="PbrTexSettings"/>; kept for old files).</summary>
    public float PbrTexTiling { get; set; } = 1f;
    /// <summary>PBR parallax depth (0 = off, 0.15 = default strong). Steep POM height-map displacement.</summary>
    public float PbrParallaxScale { get; set; } = 0.15f;
    /// <summary>Sampling settings for the SIMPLE texture (<see cref="TexturePath"/>):
    /// min/mag filter, mipmapping & anisotropy, wrapping, UV tiling/offset.</summary>
    public Libs.TextureSettings TexSettings { get; set; } = new();

    private Libs.TextureSettings[]? _pbrTexSettings;
    /// <summary>Per-PBR-map sampling settings (index 0..6 = albedo, normal, metallic,
    /// roughness, ao, height, emission). Lazily cloned from <see cref="TexSettings"/> so
    /// objects created before this feature keep one shared default per map.</summary>
    public Libs.TextureSettings[] PbrTexSettings
    {
        get => _pbrTexSettings ??= InitSlotSettings(7);
        set => _pbrTexSettings = value;
    }

    private Libs.TextureSettings[]? _terrainLayerSettings;
    /// <summary>Per-terrain-layer sampling settings (index 0..3 = air, dirt, grass, snow).
    /// Lazily cloned from <see cref="TexSettings"/> so legacy objects keep defaults.</summary>
    public Libs.TextureSettings[] TerrainLayerSettings
    {
        get => _terrainLayerSettings ??= InitSlotSettings(4);
        set => _terrainLayerSettings = value;
    }

    private Libs.TextureSettings[] InitSlotSettings(int count)
    {
        var arr = new Libs.TextureSettings[count];
        for (int i = 0; i < count; i++)
            arr[i] = TexSettings.Clone();
        return arr;
    }
    /// <summary>True when any PBR map is set — switches the object to the PBR shader.</summary>
    public bool HasPbrMaterial =>
        !string.IsNullOrEmpty(PbrAlbedoPath) || !string.IsNullOrEmpty(PbrNormalPath) ||
        !string.IsNullOrEmpty(PbrMetallicPath) || !string.IsNullOrEmpty(PbrRoughnessPath) ||
        !string.IsNullOrEmpty(PbrAoPath) || !string.IsNullOrEmpty(PbrHeightPath) ||
        !string.IsNullOrEmpty(PbrEmissionPath);

    // ── glb reference (only used when PrimitiveType == GlbReference) ──
    public string? GlbFilePath { get; set; } = null;

    // ── glb reference GPU object (loaded lazily from GlbFilePath) ──
    private GltfObject? _glbObject;
    private string? _glbLoadedPath;
    /// <summary>Shared GPU data cache (Flyweight) so multiple GLB references to the same
    /// file reuse one set of GPU buffers. Keyed by the resolved absolute path.</summary>
    private static readonly Dictionary<string, GltfModelGpuData> GlbGpuCache = new();

    // Cached uniforms of the SKINNED shadow shader (shadow_skinned_vertex.glsl). GLB
    // references must use this shader — not the static shadow_vertex.glsl — because it
    // applies bone skinning, so a skinned model casts a shadow matching its animated
    // pose instead of the raw bind pose, and it has no normal-bias extrusion that would
    // be scaled incorrectly by large model transforms.
    private static uint _glbShadowShader;
    private static int _glbShadowModelLoc = -1, _glbShadowLightSpaceLoc = -1, _glbShadowJointsLoc = -1;

    private static void EnsureGlbShadowLocations()
    {
        if (_glbShadowShader != 0) return;
        _glbShadowShader = Shader.GetShadowSkinnedShaderProgram();
        _glbShadowModelLoc = GL.GetUniformLocation(_glbShadowShader, "model");
        _glbShadowLightSpaceLoc = GL.GetUniformLocation(_glbShadowShader, "lightSpaceMatrix");
        _glbShadowJointsLoc = GL.GetUniformLocation(_glbShadowShader, "u_Joints");
    }

    /// <summary>Render the GLB reference into the given cascade's shadow map using the
    /// SKINNED shadow shader (the same one the game uses for GltfObjects) so skinned
    /// models cast a silhouette matching their animated pose instead of the raw bind pose,
    /// and static meshes match their node transforms without a scaled normal-bias offset.</summary>
    private void DrawGlbShadow(CSM csm, int cascadeIndex)
    {
        EnsureGlb();
        if (_glbObject == null) return;
        SyncGlbTransform();

        EnsureGlbShadowLocations();
        GL.UseProgram(_glbShadowShader);

        // Live normal-bias tuning (Shadow Settings panel) — skinned shadow shader.
        Visual.ShadowUniforms.UploadNormalBias(_glbShadowShader);

        var glbLightSpace = csm.LightSpaceMatrices[cascadeIndex];
        GL.UniformMatrix4fv(_glbShadowLightSpaceLoc, 1, false, (float*)&glbLightSpace);
        _glbObject.DrawShadow(_glbShadowModelLoc, _glbShadowJointsLoc);
    }

    // ── Camera (only used when PrimitiveType == Camera) ──
    /// <summary>Vertical FOV in degrees for the placed camera.</summary>
    public float CameraFov { get; set; } = 60f;
    /// <summary>Near clip distance for the placed camera.</summary>
    public float CameraNear { get; set; } = 0.1f;
    /// <summary>Far clip distance for the placed camera.</summary>
    public float CameraFar { get; set; } = 500f;
    /// <summary>Whether the view-frustum wireframe gizmo is drawn in the viewport.</summary>
    public bool ShowFrustum { get; set; } = true;

    // ── Light (only used when PrimitiveType == Light) ──
    /// <summary>Direction the light points toward (world space, not normalized).</summary>
    public Vector3 LightDirection { get; set; } = new(-0.5f, 0.8f, -0.3f);

    /// <summary>The direction the light ACTUALLY points toward in the world:
    /// <see cref="LightDirection"/> rotated by the marker's world rotation — the exact
    /// same convention the light gizmo uses to draw its beam/cone. This is what the
    /// lighting system reads (local light collection + the Direct-sun override), so
    /// rotating a light marker with the gizmo really re-aims the light instead of only
    /// rotating the gizmo.</summary>
    public Vector3 WorldLightDirection
    {
        get
        {
            var rot = Matrix4x4.CreateFromYawPitchRoll(
                RotationEuler.Y * MathF.PI / 180f,
                RotationEuler.X * MathF.PI / 180f,
                RotationEuler.Z * MathF.PI / 180f);
            var dir = Vector3.Transform(LightDirection, rot);
            float len = dir.Length();
            return len > 1e-4f ? dir / len : new Vector3(0f, 1f, 0f);
        }
    }
    /// <summary>Brightness multiplier for the light color.</summary>
    public float LightIntensity { get; set; } = 1f;
    /// <summary>Spotlight cone half-angle in degrees — used by the viewport light gizmo
    /// (and by the editor light sampling) to visualize the light's spread.</summary>
    public float LightConeAngle { get; set; } = 30f;
    /// <summary>Whether the direction-ray + spotlight-cone gizmo is drawn in the viewport.</summary>
    public bool ShowLightGizmo { get; set; } = true;
    /// <summary>Type of light source: Direct (sun-like), Point (omnidirectional), or Spotlight (cone-based).</summary>
    public LightType LightTypeEnum { get; set; } = LightType.Direct;
    /// <summary>For Point lights: falloff distance in world units (0 = no falloff, uses intensity only).</summary>
    public float LightPointRadius { get; set; } = 50f;

    // ── Terrain removed — planes render as flat PBR primitives ──
    private bool _terrainEnabled = false;
    public bool TerrainEnabled
    {
        get => false; // terrain rendering disabled — flat plane only
        set => _terrainEnabled = false; // always false
    }
    /// <summary>Heightmap file (.raw 8-bit or any image). Empty = flat plane (height 0 everywhere).</summary>
    public string TerrainHeightmapPath { get; set; } = "";
    /// <summary>Grid resolution per side (4..256). Higher = more detail, more triangles.</summary>
    public int TerrainChunkSize { get; set; } = 32;
    /// <summary>How many chunk sub-meshes per side (1..128). The terrain is split into
    /// ChunksPerSide × ChunksPerSide chunks, each one a grid of TerrainChunkSize quads —
    /// more chunks = more sub-meshes and more total triangles = more detail.</summary>
    public int TerrainChunksPerSide { get; set; } = 1;
    /// <summary>Vertical exaggeration of the heightmap (world units for full white).</summary>
    public float TerrainHeightScale { get; set; } = 30f;
    /// <summary>Slope steepness (1 - normal.y) above which the dirt/rock layer takes over.</summary>
    public float TerrainSlopeThreshold { get; set; } = 0.35f;
    /// <summary>World-space tiling frequency of the layer textures.</summary>
    public float TerrainTexTiling { get; set; } = 0.5f;
    /// <summary>Texture tiling for steep slope/cliff surfaces (triplanar).</summary>
    public float TerrainSlopeTexTiling { get; set; } = 0.3f;
    /// <summary>Parallax occlusion mapping strength (0 = off, 0.02 = subtle, 0.06 = strong).</summary>
    public float TerrainParallaxScale { get; set; } = 0.0f;
    /// <summary>Number of POM ray-march steps (8-32, higher = more accurate but slower).</summary>
    public int TerrainPomSteps { get; set; } = 16;
    /// <summary>Stochastic (random per-tile) sampling — OFF by default so the default plane
    /// tiles deterministically. ON breaks up the repeating pattern.</summary>
    public bool TerrainUseStochasticSampling { get; set; } = false;
    /// <summary>Normalized height where the air layer ends (water level).</summary>
    public float TerrainLayerAirTop { get; set; } = 0.18f;
    /// <summary>Normalized height where the dirt layer ends.</summary>
    public float TerrainLayerDirtTop { get; set; } = 0.45f;
    /// <summary>Normalized height where the grass layer ends (snow starts after).</summary>
    public float TerrainLayerGrassTop { get; set; } = 0.75f;
    /// <summary>Normalized height where the snow layer is fully dominant.</summary>
    public float TerrainLayerSnowTop { get; set; } = 1.0f;
    /// <summary>Texture for layer 1 — air / water. Defaults to default.jpg for new planes.</summary>
    public string TerrainTextureAirPath { get; set; } = "Artifacts/Textures/default.jpg";
    /// <summary>Texture for layer 2 — tanah / dirt.</summary>
    public string TerrainTextureDirtPath { get; set; } = "";
    /// <summary>Texture for layer 3 — rumput / grass.</summary>
    public string TerrainTextureGrassPath { get; set; } = "";
    /// <summary>Texture for layer 4 — salju / snow.</summary>
    public string TerrainTextureSnowPath { get; set; } = "";
    /// <summary>Texture for layer 5 — lereng / slope (steep cliffs). Replaces the dirt
    /// layer on steep faces so cliffs get their own rock texture.</summary>
    public string TerrainTextureSlopePath { get; set; } = "";

    // ── PBR map tuning (global per map type — applies to ALL layers; uniforms only,
    //    so editing these never triggers an expensive terrain rebuild) ──
    /// <summary>Albedo brightness multiplier (0..2).</summary>
    public float TerrainPbrAlbedoBrightness { get; set; } = 1f;
    /// <summary>Albedo saturation (0 = grayscale, 1 = original, 2 = oversaturated).</summary>
    public float TerrainPbrAlbedoSaturation { get; set; } = 1f;
    /// <summary>Albedo contrast (1 = original, 0 = flat gray, 2 = high contrast).</summary>
    public float TerrainPbrAlbedoContrast { get; set; } = 1f;
    /// <summary>Normal map strength (0 = off, 1 = full, 2 = overdriven).</summary>
    public float TerrainPbrNormalStrength { get; set; } = 1f;
    /// <summary>Normal map blur in texels (0 = sharp, up to 8 = soft).</summary>
    public float TerrainPbrNormalBlur { get; set; } = 0f;
    /// <summary>Metallic mask threshold — values above become metal.</summary>
    public float TerrainPbrMetallicThreshold { get; set; } = 0.5f;
    /// <summary>Metallic threshold transition softness (0 = hard cut, 0.3 = wide blend).</summary>
    public float TerrainPbrMetallicSoftness { get; set; } = 0.1f;
    /// <summary>Metallic final strength (0 = never metal, 1 = as masked).</summary>
    public float TerrainPbrMetallicStrength { get; set; } = 1f;
    /// <summary>Roughness multiplier (0 = glossy, 1 = as mapped, 2 = very rough).</summary>
    public float TerrainPbrRoughnessStrength { get; set; } = 1f;
    /// <summary>Invert roughness (for smoothness maps that store gloss instead).</summary>
    public bool TerrainPbrRoughnessInvert { get; set; } = false;
    /// <summary>Ambient occlusion strength (0 = no AO, 1 = as mapped).</summary>
    public float TerrainPbrAoStrength { get; set; } = 1f;
    /// <summary>Ambient occlusion brightness offset (0 = as mapped, 1 = fully bright).</summary>
    public float TerrainPbrAoBrightness { get; set; } = 0f;
    /// <summary>Height / parallax strength (0 = flat, 1 = full displacement offset).</summary>
    public float TerrainPbrHeightStrength { get; set; } = 1f;
    /// <summary>Invert the height map (swap valleys/peaks).</summary>
    public bool TerrainPbrHeightInvert { get; set; } = false;
    /// <summary>Height map blur in texels (0 = sharp, up to 8 = soft).</summary>
    public float TerrainPbrHeightBlur { get; set; } = 0f;
    /// <summary>Emission intensity multiplier (0 = off, 1 = as mapped).</summary>
    public float TerrainPbrEmissionIntensity { get; set; } = 1f;
    /// <summary>Per-layer PBR data for the legacy 5 terrain layers (kept for backward compat).
    /// New code should use <see cref="TerrainLayerList"/> instead.</summary>
    public TerrainPbrLayerData[] TerrainLayers { get; set; } = [new(), new(), new(), new(), new()];

    // ══════════════════════════════════════════════════════════════════════
    //  NEW DYNAMIC TERRAIN LAYER SYSTEM
    // ══════════════════════════════════════════════════════════════════════
    /// <summary>Dynamic terrain layers. Default: 1 layer (Base). User can add more.</summary>
    public List<TerrainLayer> TerrainLayerList { get; set; } = [TerrainLayer.CreateDefault()];
    /// <summary>Slope layer: applies on steep faces. Toggle on/off.</summary>
    public TerrainLayer? TerrainSlopeLayer { get; set; } = null;
    /// <summary>Slope layer data serialized separately (so slope can be null = disabled).</summary>
    public bool TerrainSlopeEnabled { get; set; } = false;

    /// <summary>Max layers supported (GPU texture unit limit).</summary>
    public const int MaxTerrainLayers = 8;
    /// <summary>Migrate old fixed-layer properties to the new TerrainLayerList.
    /// Called after deserialization for scenes saved with the old format.
    /// If TerrainLayerList already has real layers (count > 1 or non-default), skip.</summary>
    public void MigrateTerrainLayers()
    {
        if (TerrainLayerList.Count > 1) return; // already migrated or user added layers
        var only = TerrainLayerList[0];
        bool isDefault = only.AlbedoPath == "Artifacts/Textures/default.jpg"
            && only.HeightMin == 0f && only.HeightMax == 1f && only.TilingX == 0.5f;
        if (!isDefault) return; // user customized the single layer

        // Check if old properties have meaningful data
        bool hasOldData = !string.IsNullOrEmpty(TerrainTextureDirtPath)
            || !string.IsNullOrEmpty(TerrainTextureGrassPath)
            || !string.IsNullOrEmpty(TerrainTextureSnowPath)
            || TerrainLayerAirTop != 0.18f || TerrainLayerDirtTop != 0.45f;
        if (!hasOldData) return;

        // Migrate old 4 layers → new dynamic layers
        TerrainLayerList.Clear();
        var l1 = TerrainLayer.CreateDefault();
        l1.Name = "Layer 1"; l1.AlbedoPath = TerrainTextureAirPath;
        l1.HeightMin = 0f; l1.HeightMax = TerrainLayerAirTop;
        l1.TilingX = TerrainTexTiling; l1.TilingY = TerrainTexTiling;
        TerrainLayerList.Add(l1);

        if (!string.IsNullOrEmpty(TerrainTextureDirtPath))
        {
            var l2 = TerrainLayer.CreateDefault();
            l2.Name = "Layer 2"; l2.AlbedoPath = TerrainTextureDirtPath;
            l2.HeightMin = TerrainLayerAirTop; l2.HeightMax = TerrainLayerDirtTop;
            l2.TilingX = TerrainTexTiling; l2.TilingY = TerrainTexTiling;
            TerrainLayerList.Add(l2);
        }
        if (!string.IsNullOrEmpty(TerrainTextureGrassPath))
        {
            var l3 = TerrainLayer.CreateDefault();
            l3.Name = "Layer 3"; l3.AlbedoPath = TerrainTextureGrassPath;
            l3.HeightMin = TerrainLayerDirtTop; l3.HeightMax = TerrainLayerGrassTop;
            l3.TilingX = TerrainTexTiling; l3.TilingY = TerrainTexTiling;
            TerrainLayerList.Add(l3);
        }
        if (!string.IsNullOrEmpty(TerrainTextureSnowPath))
        {
            var l4 = TerrainLayer.CreateDefault();
            l4.Name = "Layer 4"; l4.AlbedoPath = TerrainTextureSnowPath;
            l4.HeightMin = TerrainLayerGrassTop; l4.HeightMax = TerrainLayerSnowTop;
            l4.TilingX = TerrainTexTiling; l4.TilingY = TerrainTexTiling;
            TerrainLayerList.Add(l4);
        }

        // Migrate slope
        if (TerrainSlopeEnabled && !string.IsNullOrEmpty(TerrainTextureSlopePath))
        {
            TerrainSlopeLayer = TerrainLayer.CreateSlope();
            TerrainSlopeLayer.AlbedoPath = TerrainTextureSlopePath;
            TerrainSlopeLayer.TilingX = TerrainSlopeTexTiling;
            TerrainSlopeLayer.TilingY = TerrainSlopeTexTiling;
            TerrainSlopeLayer.SlopeThreshold = TerrainSlopeThreshold;
        }

        Console.WriteLine($"[EditorObject] Migrated {TerrainLayerList.Count} terrain layers from legacy format");
    }

    /// <summary>Brush radius in world units (viewport paint tool).</summary>
    public float TerrainBrushSize { get; set; } = 10f; 
    /// <summary>Height delta per painted frame, in world units (viewport paint tool).</summary>
    public float TerrainBrushStrength { get; set; } = 1f;
    /// <summary>Brush edge falloff 0..1 (0 = hard edge, 1 = very soft).</summary>
    public float TerrainBrushSoftness { get; set; } = 1f;
    /// <summary>Layer painted with the texture brush: 0-3.</summary>
    public int TerrainPaintLayerIndex { get; set; } = 0;
    /// <summary>Weight added to the painted layer per brush stamp (0..1).</summary>
    public float TerrainPaintStrength { get; set; } = 0.45f;

    // ── Per-paint-layer textures (independent from terrain auto-layers) ──
    public const int MaxPaintLayers = 4;
    /// <summary>Texture path for paint layer 0.</summary>
    public string PaintLayerTexture0 { get; set; } = "";
    /// <summary>Texture path for paint layer 1.</summary>
    public string PaintLayerTexture1 { get; set; } = "";
    /// <summary>Texture path for paint layer 2.</summary>
    public string PaintLayerTexture2 { get; set; } = "";
    /// <summary>Texture path for paint layer 3.</summary>
    public string PaintLayerTexture3 { get; set; } = "";
    /// <summary>Per-paint-layer tiling (X, Y).</summary>
    public Vector2[] PaintLayerTiling { get; set; } = [new(0.5f, 0.5f), new(0.5f, 0.5f), new(0.5f, 0.5f), new(0.5f, 0.5f)];
    /// <summary>Per-paint-layer random tile (stochastic sampling) flag.</summary>
    public bool[] PaintLayerStochastic { get; set; } = [false, false, false, false];
    /// <summary>Number of active paint layers (1..4). Splat map RGBA limits us to 4.</summary>
    public int PaintLayerCount { get; set; } = 1;
    /// <summary>Helper: get/set paint layer texture by index.</summary>
    public string GetPaintLayerTexture(int idx) => idx switch { 0 => PaintLayerTexture0, 1 => PaintLayerTexture1, 2 => PaintLayerTexture2, 3 => PaintLayerTexture3, _ => "" };
    public void SetPaintLayerTexture(int idx, string path)
    {
        switch (idx) { case 0: PaintLayerTexture0 = path; break; case 1: PaintLayerTexture1 = path; break; case 2: PaintLayerTexture2 = path; break; case 3: PaintLayerTexture3 = path; break; }
    }
    /// <summary>Brush falloff curve used by every brush tool: 0=Linear, 1=Smooth,
    /// 2=Sharp, 3=Spherical, 4=Soft.</summary>
    public int TerrainBrushFalloff { get; set; } = 1;
    /// <summary>Bitmask controlling which terrain layers the brush affects.
    /// Bit 0 = layer 0, bit 1 = layer 1, etc. 0 = all layers.</summary>
    public int TerrainBrushMask { get; set; } = 0;
    /// <summary>Editor-only overlay: colorize the terrain by height (low=blue → high=red)
    /// with contour lines so the relief reads clearly. Transient — not saved to the scene.</summary>
    public bool TerrainShowHeatmap { get; set; } = false;
    /// <summary>Editor-only overlay: draw dark topographic contour lines every 10% height
    /// WITHOUT the heatmap colors — the terrain texture stays fully visible while the
    /// relief reads clearly. Transient — not saved to the scene.</summary>
    public bool TerrainShowContours { get; set; } = false;

    // ── Brush ring indicator (set by ViewportPanel each frame while the brush tool hovers
    // this terrain; color + alpha are user-editable and SAVED with the scene) ──
    /// <summary>World-space brush center on this terrain's surface (null = hide ring).</summary>
    public Vector3? BrushIndicatorPos { get; set; }
    /// <summary>Ring color: green = height brush, layer color = 🎨 paint, red = Ctrl (lower/erase).</summary>
    public Vector3 BrushIndicatorColor { get; set; } = new(0.3f, 0.9f, 0.5f);
    /// <summary>Ring transparency 0..1 (0 = invisible, 1 = opaque). Default 0.35 = translucent highlight.</summary>
    public float BrushIndicatorAlpha { get; set; } = 0.35f;
    /// <summary>Whether the brush ring should be drawn on this terrain.</summary>
    public bool ShowBrushIndicator { get; set; }

    // ── Sky (only used when PrimitiveType == Sky) ──
    /// <summary>Time of day in hours (0..24). 12 = midday.</summary>
    public float SkyTimeOfDay { get; set; } = 12f;
    /// <summary>Sun elevation override in degrees (-90..90). null = follow SkyTimeOfDay.
    /// Setting this lets the user aim the sun independently of the time-of-day cycle.</summary>
    public float? SkySunPitch { get; set; } = null;
    /// <summary>Sun azimuth override in degrees (0..360). null = follow SkyTimeOfDay.</summary>
    public float? SkySunYaw { get; set; } = null;
    /// <summary>Cloud coverage/intensity 0..1 (drives the sky shader's weather mode).</summary>
    public float SkyCloudCoverage { get; set; } = 0.3f;
    /// <summary>Sun brightness multiplier (applied to the sky light color).</summary>
    public float SkySunIntensity { get; set; } = 1f;
    /// <summary>Time-of-day animation speed in hours per second (0 = static). While > 0 and
    /// not paused, SkyTimeOfDay advances automatically and the sun orbits the scene.</summary>
    public float SkyTimeAnimSpeed { get; set; } = 0f;
    /// <summary>Pause the time-of-day animation (keeps the current SkyTimeOfDay).</summary>
    public bool SkyTimeAnimPaused { get; set; } = false;
    /// <summary>Whether the horizon-circle + sun-icon gizmo is drawn in the viewport.</summary>
    public bool ShowSkyGizmo { get; set; } = true;

    // ── New Sky System (3 types: Procedural, Skybox, Dome) ──
    /// <summary>Master sky settings container for the 3 sky types.</summary>
    public SkySettings SkySettings { get; set; } = new();

    /// <summary>Snapshot of sky settings taken at save/load time, for 'Load from Settings' restore.</summary>
    [JsonIgnore]
    public SkySettings? SavedSkySettings { get; set; }

    // ── Internal rendering resources (lazy-init) ──
    private Object3D? _object3D;
    private uint _textureID = 0;
    private bool _dirty = true;

    // ── PBR material GPU resources (Box/Sphere/flat plane; index 0=albedo, 1=normal,
    //    2=metallic, 3=roughness, 4=AO, 5=height, 6=emission) ──
    private readonly uint[] _pbrTex = new uint[7];
    private string _pbrCacheKey = "";
    private static uint _pbrWhiteTex = 0;

    // ── Advanced terrain resources (Plane only) ──
    private EditorTerrainMesh? _terrainMesh;
    private string _terrainCacheKey = "";
    /// <summary>Base64-encoded painted heightmap blob (persisted in the scene file so
    /// brush edits survive save/load). Empty = no user edits.</summary>
    private byte[]? _terrainPaintedCache;
    /// <summary>Heightmap path the painted cache belongs to — only restored when the
    /// terrain still points at the same heightmap (switching maps discards old edits).</summary>
    private string? _terrainPaintedSourcePath;
    /// <summary>Heightmap path the CURRENT mesh was actually built from. Used when
    /// stashing the painted cache on rebuild so the cache is keyed to the OLD path,
    /// not the (possibly changed) TerrainHeightmapPath property.</summary>
    private string _terrainLoadedPath = "";
    /// <summary>Serialized manual layer-paint blob (persisted in the scene file). Carried
    /// across mesh rebuilds independently of the heightmap path.</summary>
    private byte[]? _terrainSplatCache;

    // ── Vertex cache for wireframe outline rendering ──
    private Vertex[]? _vertexCache;

    // ── Gizmo state (snapshots for undo) ──
    public Vector3 LastGizmoPosition { get; set; }
    public Vector3 LastGizmoRotation { get; set; }
    public Vector3 LastGizmoScale { get; set; }

    // ── Gizmo pivot override snapshot (so undo/redo also restores the pivot position) ──
    /// <summary>Snapshot of <see cref="GizmoPivotOverride"/> taken at gizmo drag start,
    /// used by undo/redo to prevent the gizmo floating detached from the object.</summary>
    public Vector3? LastGizmoPivot { get; set; }

    // ── Per-object gizmo pivot override (set by middle-click in viewport) ──
    /// <summary>When set, the gizmo renders at this world position instead of the object's Position.
    /// Persists across selection changes — each object remembers its own pivot override.</summary>
    public Vector3? GizmoPivotOverride { get; set; }

    // ── 2D Map (only used when PrimitiveType == Map2D) ──
    /// <summary>Reference to the Tilemap2D data this object renders.</summary>
    [JsonIgnore] public Tilemap2D? Map2dTilemap { get; set; }
    /// <summary>Tileset texture GPU ID (loaded from Map2dTilemap.TilesetImagePath).</summary>
    [JsonIgnore] private uint _map2dTilesetTex = 0;
    [JsonIgnore] private string _map2dTilesetPath = "";
    /// <summary>Tileset grid layout.</summary>
    public int Map2dTilesetCols { get; set; } = 8;
    public int Map2dTilesetRows { get; set; } = 8;
    public bool Map2dTilesetFlipV { get; set; } = false;
    /// <summary>Whether to show the grid overlay on the map.</summary>
    /// <summary>Show the tile grid overlay (editor-only aid). Automatically suppressed
    /// while the IDE is in Play-in-Preview / in-game mode so the running game renders
    /// clean without editor grid lines.</summary>
    public bool Map2dShowGrid
    {
        get => _map2dShowGrid && !Editor2DAidsHidden;
        set => _map2dShowGrid = value;
    }
    private bool _map2dShowGrid = true;

    /// <summary>True while the IDE is in Play-in-Preview / in-game mode. Hides all
    /// editor-only 2D aids (tile grid overlay, collision helper boxes) so the running
    /// game renders clean. Toggled by the IDE's InGameMode setter.</summary>
    public static bool Editor2DAidsHidden { get; set; }
    /// <summary>Grid overlay color (RGB = line color, A = line alpha). Used by
    /// DrawMap2D so the Map Editor "Grid Color" picker really tints the 3D grid.</summary>
    public Vector4 Map2dGridColor { get; set; } = new(0.4f, 0.5f, 0.68f, 0.5f);
    /// <summary>
    /// Which layer index this Map2D object renders (-1 = render all visible layers, >=0 = render single layer only).
    /// </summary>
    public int Map2dLayerIndex { get; set; } = -1;
    /// <summary>
    /// The ACTIVE (selected) layer in the Map Editor. When >= 0, only that layer's tiles
    /// are baked into the mesh; non-active layers are NOT rendered even if visible. When
    /// -1 the editor falls back to Map2dLayerIndex (all visible layers) for older scenes.
    /// Kept separate from Map2dLayerIndex so the canonical plane stays at z=0 (paint/hover
    /// math) while the rendered layer follows the editor selection.
    /// </summary>
    public int Map2dActiveLayer { get; set; } = -1;
    /// <summary>Show FULL 3D collision helper boxes over tiles flagged for collision
    /// (like Unreal's collision previews): one shaded box with bright edges per
    /// collision tile, sticking OUT of the grid plane toward the viewer so the player
    /// can "stand" on it. A tile whose ID has NO collision flag draws NO box —
    /// no box = no collision.</summary>
    public bool Map2dShowCollision { get; set; } = true;
    /// <summary>RGBA color of the collision helper boxes (default: translucent green).</summary>
    public Vector4 Map2dCollisionColor { get; set; } = new(0.25f, 0.85f, 0.45f, 0.35f);
    /// <summary>Parallax background/foreground layers to render with this map. Each layer
    /// is a textured upright plane offset in world Z by its Y position (positive = in
    /// front of the grid, negative = behind it). Pushed from the Map Editor panel.</summary>
    [JsonIgnore] public List<MapParallaxRenderLayer>? Map2dParallaxLayers { get; set; }
    /// <summary>Cached VAO/VBO for the tilemap mesh (rebuilt when tiles change).</summary>
    [JsonIgnore] private uint _map2dVAO, _map2dVBO;
    [JsonIgnore] private int _map2dVertCount = 0;
    [JsonIgnore] private string _map2dMeshCacheKey = "";
    /// <summary>Scratch VAO/VBO used to stream parallax layer quads (created lazily).</summary>
    [JsonIgnore] private uint _parallaxVAO, _parallaxVBO;

    // ── Shader uniform locations (cached for Draw overloads) ──
#pragma warning disable CS0414
    private int _modelLoc = -1, _viewLoc = -1, _projLoc = -1;
    private int _sunDirLoc = -1, _lightColorLoc = -1, _viewPosLoc = -1;
    private int _useFogLoc = -1, _fogColorLoc = -1;

    /// <summary>
    /// Create a new EditorObject with the given primitive type and optional name.
    /// </summary>
    public EditorObject(EditorPrimitiveType type, string? name = null)
    {
        PrimitiveType = type;
        if (name != null)
            Name = name;
        // Set reasonable defaults based on type
        Scale = type switch
        {
            EditorPrimitiveType.Plane => new Vector3(500f, 0.05f, 500f),
            EditorPrimitiveType.Camera => new Vector3(0.5f, 0.4f, 0.6f),
            EditorPrimitiveType.Map2D => new Vector3(1f, 1f, 1f),
            EditorPrimitiveType.Player2D => new Vector3(1f, 2f, 1f),
            _ => Vector3.One,
        };
        Color = type switch
        {
            EditorPrimitiveType.Plane => new Vector3(0.3f, 0.6f, 0.3f),
            EditorPrimitiveType.Box => new Vector3(0.8f, 0.4f, 0.2f),
            EditorPrimitiveType.Sphere => new Vector3(0.2f, 0.4f, 0.8f),
            EditorPrimitiveType.GlbReference => new Vector3(0.6f, 0.6f, 0.8f),
            EditorPrimitiveType.Camera => new Vector3(0.2f, 0.7f, 0.8f),
            EditorPrimitiveType.Light => new Vector3(1.0f, 0.85f, 0.3f),
            EditorPrimitiveType.Sky => new Vector3(0.5f, 0.7f, 1.0f),
            EditorPrimitiveType.Map2D => new Vector3(0.8f, 0.8f, 0.9f),
            EditorPrimitiveType.Player2D => new Vector3(0.2f, 0.9f, 0.4f),
            EditorPrimitiveType.CameraStart2D => new Vector3(0.25f, 0.85f, 1f),
            _ => new Vector3(0.8f, 0.8f, 0.9f),
        };
    }

    public Matrix4x4 WorldMatrix
    {
        get
        {
            return Matrix4x4.CreateScale(Scale)
                 * Matrix4x4.CreateFromYawPitchRoll(
                     RotationEuler.Y * MathF.PI / 180f,
                     RotationEuler.X * MathF.PI / 180f,
                     RotationEuler.Z * MathF.PI / 180f)
                 * Matrix4x4.CreateTranslation(Position);
        }
    }

    /// <summary>Pick the Light marker that should drive the global sun: the first DIRECT
    /// light, or null when there is none (the procedural sun takes over). Point/Spot
    /// markers never drive the sun — they are local lights. Scenes saved before the
    /// light-type feature load with LightTypeEnum = Direct, so they still work.</summary>
    public static EditorObject? PickSunLight(IEnumerable<EditorObject> objects)
    {
        foreach (var obj in objects)
        {
            if (obj != null && obj.PrimitiveType == EditorPrimitiveType.Light
                && obj.LightTypeEnum == LightType.Direct)
                return obj;
        }
        return null;
    }

    /// <summary>
    /// Apply the first placed Light + Sky marker settings to the given Lights/Skybox pair.
    /// Shared by SceneManager (editor viewport) and GameScene (in-game mode) so both render
    /// with the same sun direction, brightness, cloud coverage and time of day.
    /// </summary>
    /// <param name="lightObj">First Light marker (or null). Its direction/color/intensity win over the sky sun.</param>
    /// <param name="skyObj">First Sky marker (or null). Drives time of day, optional sun pitch/yaw override, cloud coverage, sun brightness.</param>
    /// <param name="lights">The Lights instance to mutate.</param>
    /// <param name="skybox">The Skybox to mutate (cloud override), may be null.</param>
    public static void ApplyEnvironmentMarkers(EditorObject? lightObj, EditorObject? skyObj, Lights lights, Skybox? skybox, float deltaTime)
    {
        // ── Light override: the Light marker's direction/color take priority over the sky sun ──
        if (lightObj != null)
        {
            lights.SunDirOverride = lightObj.WorldLightDirection;
            lights.LightColorOverride = lightObj.Color;
            lights.LightIntensity = lightObj.LightIntensity;
        }
        else
        {
            lights.SunDirOverride = null;
            lights.LightColorOverride = null;
            lights.LightIntensity = 1f;
        }

        // ── Sky override: time of day + optional sun pitch/yaw + brightness ──
        if (skyObj != null)
        {
            // Time-of-day animation: while enabled (speed > 0) and not paused, advance the
            // stored SkyTimeOfDay so the sun orbits, the gizmo follows, the Inspector slider
            // moves and the saved scene keeps the current time. Paused/static pins the time.
            if (skyObj.SkyTimeAnimSpeed > 0f && !skyObj.SkyTimeAnimPaused)
            {
                skyObj.SkyTimeOfDay = (skyObj.SkyTimeOfDay + skyObj.SkyTimeAnimSpeed * deltaTime) % 24f;
                if (skyObj.SkyTimeOfDay < 0f) skyObj.SkyTimeOfDay += 24f;
            }

            float hours = Math.Clamp(skyObj.SkyTimeOfDay, 0f, 24f);
            lights.WorldTime = (hours / 24f) * (MathF.PI * 2f);

            // ── Day/night cycle with a Direct light: while the sky FOLLOWS time-of-day
            // (no manual pitch/yaw override) keep the Direct light in sync with the sky's
            // current sun, so ▶ Play Day/Night really orbits the light and the marker
            // arrow always shows where the sun is. A manual pitch/yaw override pins the
            // sun on purpose — and crucially, it must NOT snap a Direct light to a
            // below-horizon (night) direction at startup: that would turn the whole
            // scene black until the user presses Play again. The direction is written
            // back through the inverse marker rotation so WorldLightDirection (what the
            // gizmo and the Lighting use) matches the sky sun even for a rotated marker.
            if (lightObj != null
                && lightObj.LightTypeEnum == LightType.Direct
                && !(skyObj.SkySunPitch.HasValue && skyObj.SkySunYaw.HasValue))
            {
                Vector3 skyDir = skyObj.GetSkySunDirection();
                var rot = Matrix4x4.CreateFromYawPitchRoll(
                    lightObj.RotationEuler.Y * MathF.PI / 180f,
                    lightObj.RotationEuler.X * MathF.PI / 180f,
                    lightObj.RotationEuler.Z * MathF.PI / 180f);
                lightObj.LightDirection = Matrix4x4.Invert(rot, out var inv)
                    ? Vector3.Transform(skyDir, inv)
                    : skyDir;
                lights.SunDirOverride = lightObj.WorldLightDirection;
            }

            // Optional sun pitch/yaw override: aim the sun independently of the time-of-day
            // cycle. Only applied when there is no Light object (the Light marker's direction
            // takes priority) and only when BOTH pitch and yaw are set (null keeps time-of-day).
            if (lightObj == null
                && skyObj.SkySunPitch.HasValue && skyObj.SkySunYaw.HasValue)
            {
                float pitch = skyObj.SkySunPitch.Value * MathF.PI / 180f;
                float yaw = skyObj.SkySunYaw.Value * MathF.PI / 180f;
                // Same convention as the camera: front = (sin yaw cos pitch, sin pitch, cos yaw cos pitch)
                var sunDir = new Vector3(
                    MathF.Sin(yaw) * MathF.Cos(pitch),
                    MathF.Sin(pitch),
                    MathF.Cos(yaw) * MathF.Cos(pitch));
                lights.SunDirOverride = Vector3.Normalize(sunDir);
            }

            // Sun brightness multiplier from the sky object
            lights.SunBrightness = Math.Max(0f, skyObj.SkySunIntensity);
        }
        else
        {
            // No sky object → restore default sun brightness
            lights.SunBrightness = 1f;
        }

        // ── Cloud coverage → skybox weather override ──
        if (skybox != null)
        {
            skybox.WeatherOverride = skyObj != null
                ? Math.Clamp(skyObj.SkyCloudCoverage, 0f, 1f)
                : null;

            // ── New Sky System: pass SkySettings from the editor Sky object ──
            skybox.ActiveSkySettings = skyObj?.SkySettings;
        }
    }

    /// <summary>World-space forward (look) direction derived from <see cref="RotationEuler"/>
    /// — same convention as <see cref="WorldMatrix"/> and the camera/light gizmos.
    /// Default (no rotation) looks down -Z.</summary>
    public Vector3 Forward
    {
        get
        {
            var rot = Matrix4x4.CreateFromYawPitchRoll(
                RotationEuler.Y * MathF.PI / 180f,
                RotationEuler.X * MathF.PI / 180f,
                RotationEuler.Z * MathF.PI / 180f);
            return Vector3.Transform(-Vector3.UnitZ, rot);
        }
    }

    /// <summary>Model matrix for the advanced terrain mesh: XZ footprint follows
    /// Scale.X/Z (the thin Scale.Y must NOT flatten the terrain), Y keeps world height.
    /// Shared by rendering, AABB and brush raycasting so they all agree.</summary>
    private Matrix4x4 TerrainModelMatrix =>
        Matrix4x4.CreateScale(Scale.X, 1f, Scale.Z)
        * Matrix4x4.CreateFromYawPitchRoll(
            RotationEuler.Y * MathF.PI / 180f,
            RotationEuler.X * MathF.PI / 180f,
            RotationEuler.Z * MathF.PI / 180f)
        * Matrix4x4.CreateTranslation(Position);

    /// <summary>Compute world-space AABB for selection/culling.
    /// Accounts for scale and rotation (transforms 8 corners through WorldMatrix).</summary>
    public AABB WorldAABB
    {
        get
        {
            // Advanced terrain: heightmap mesh spans the full Scale footprint and rises up
            // to TerrainHeightScale — transform with the same model used for rendering.
            if (PrimitiveType == EditorPrimitiveType.Plane && TerrainEnabled && _terrainMesh is { IsReady: true })
            {
                var terrainModel = TerrainModelMatrix;
                float top = Math.Max(1f, TerrainHeightScale);
                return new AABB(new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, top, 0.5f))
                    .Transform(terrainModel);
            }

            // Local-space AABB for each primitive type (before transform)
            AABB localAABB = PrimitiveType switch
            {
                EditorPrimitiveType.Plane => new AABB(
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f,  0.5f,  0.5f)),
                EditorPrimitiveType.Box => new AABB(
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f,  0.5f,  0.5f)),
                EditorPrimitiveType.Sphere => new AABB(
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f,  0.5f,  0.5f)),
                EditorPrimitiveType.GlbReference => new AABB(
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f,  0.5f,  0.5f)),
                // Player2D/Start2D: feet-anchored capsule AABB — Position.Y is the
                // capsule BOTTOM (matches DrawPlayer2DCapsule + Player2DSystem).
                EditorPrimitiveType.Player2D or EditorPrimitiveType.Start2D or EditorPrimitiveType.CameraStart2D => new AABB(
                    new Vector3(-Player2DCapsuleRadius, 0f, -Player2DCapsuleRadius),
                    new Vector3( Player2DCapsuleRadius, Player2DCapsuleHeight,  Player2DCapsuleRadius)),
                _ => new AABB(
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f,  0.5f,  0.5f)),
            };
            return localAABB.Transform(WorldMatrix);
        }
    }

    /// <summary>Compute a cache key from the mesh-affecting terrain settings so the mesh is
    /// only rebuilt when something actually changed (Inspector edits, scale, textures).
    /// Uniform-only settings (slope, tiling, height bands) are deliberately excluded — they
    /// don't change the mesh, so editing them must NOT trigger an expensive rebuild.</summary>
    private string TerrainMeshCacheKey
    {
        get
        {
            string dynLayerKey = string.Join("|", TerrainLayerList.Select(l => l.AlbedoPath ?? ""));
            string slopeKey = TerrainSlopeEnabled && TerrainSlopeLayer != null ? TerrainSlopeLayer.AlbedoPath ?? "" : "";
            return $"{TerrainEnabled}|{TerrainHeightmapPath}|{TerrainChunkSize}|{TerrainChunksPerSide}|{TerrainHeightScale:F2}|"
                 + $"{Scale.X:F2}|{Scale.Z:F2}|{dynLayerKey}|{slopeKey}";
        }
    }

    /// <summary>Build (or rebuild) the advanced terrain mesh when TerrainEnabled.
    /// Returns true if a valid terrain mesh is available, false if it fell back to flat.</summary>
    private bool EnsureTerrainMesh()
    {
        string key = TerrainMeshCacheKey;
        if (_terrainMesh != null && _terrainCacheKey == key)
            return true;

        // Keep user brush edits across mesh rebuilds (chunk size / scale / texture edits):
        // stash the painted blob before tearing down, restore it after the fresh load
        // as long as the heightmap path didn't change (switching maps discards edits).
        // NOTE: tag the cache with the OLD loaded path — TerrainHeightmapPath may already
        // point at a NEW file (e.g. the user just changed it in the Inspector).
        if (_terrainMesh is { IsModified: true } oldMesh)
        {
            _terrainPaintedCache = oldMesh.GetModifiedRaw();
            _terrainPaintedSourcePath = _terrainLoadedPath;
        }
        // Layer paint (splat) is independent of the heightmap — always carry it over.
        if (_terrainMesh is { SplatModified: true } oldMesh2)
            _terrainSplatCache = oldMesh2.GetModifiedSplatRaw();

        _terrainMesh?.Dispose();
        _terrainMesh = null;
        _terrainCacheKey = key;

        if (!TerrainEnabled) return false;

        var mesh = new EditorTerrainMesh();
        if (!mesh.LoadHeightmap(TerrainHeightmapPath))
        {
            Console.WriteLine($"[EditorObject] '{Name}': heightmap not found — rendering flat plane ({TerrainHeightmapPath})");
            mesh.Dispose();
            _terrainCacheKey = "";
            return false;
        }
        if (_terrainPaintedCache != null && _terrainPaintedSourcePath == TerrainHeightmapPath)
            mesh.RestoreModifiedRaw(_terrainPaintedCache);
        if (_terrainSplatCache != null)
            mesh.RestoreModifiedSplatRaw(_terrainSplatCache);
        _terrainLoadedPath = TerrainHeightmapPath;
        mesh.SetLayerTextures(TerrainTextureAirPath, TerrainTextureDirtPath, TerrainTextureGrassPath, TerrainTextureSnowPath);
        mesh.SetDynLayerTextures(TerrainLayerList, TerrainSlopeEnabled ? TerrainSlopeLayer : null);
        mesh.SetDynLayerPbrTextures(TerrainLayerList, TerrainSlopeEnabled ? TerrainSlopeLayer : null);
        mesh.Generate(TerrainChunkSize, TerrainChunksPerSide, TerrainHeightScale, Math.Max(0.1f, Scale.X), Math.Max(0.1f, Scale.Z));
        mesh.ApplyTextureSettings(TerrainLayerSettings);
        _terrainMesh = mesh;

        // When switching a plane to terrain mode, release the flat-plane Object3D so it
        // doesn't linger on the GPU until Dispose().
        if (_object3D != null)
        {
            if (_object3D.VAO != 0)
            {
                uint vao = _object3D.VAO;
                GL.DeleteVertexArrays(1, &vao);
            }
            if (_object3D.VBO != 0)
            {
                uint vbo = _object3D.VBO;
                GL.DeleteBuffers(1, &vbo);
            }
            _object3D = null;
        }
        return true;
    }

    /// <summary>Initialize GPU resources (VAO, VBO) if needed.</summary>
    public void EnsureResources()
    {
        // Advanced terrain planes use a dedicated mesh + shader, not Object3D.
        // If the heightmap is missing we fall back to a flat plane (EnsureTerrainMesh
        // returns false), so continue into the normal primitive path below.
        if (PrimitiveType == EditorPrimitiveType.Plane && TerrainEnabled && EnsureTerrainMesh())
            return;

        if (_object3D != null && !_dirty) return;

        // Cleanup old GPU resources (VAO/VBO) so repeated color changes don't leak buffers.
        // EditorObjectManager.Remove() also disposes the Object3D, but here we're re-creating
        // in place, so delete the GL handles ourselves before dropping the reference.
        if (_object3D != null)
        {
            if (_object3D.VAO != 0)
            {
                uint vao = _object3D.VAO;
                GL.DeleteVertexArrays(1, &vao);
            }
            if (_object3D.VBO != 0)
            {
                uint vbo = _object3D.VBO;
                GL.DeleteBuffers(1, &vbo);
            }
            _object3D = null;
        }

        var shader = Shader.GetShaderProgram();

        switch (PrimitiveType)
        {
            case EditorPrimitiveType.Plane:
            {
                // If terrain mode is active AND a valid terrain mesh exists, the mesh is
                // managed by EnsureTerrainMesh() — no flat Object3D is needed. Otherwise
                // (terrain disabled, or heightmap missing) build the classic flat plane.
                if (TerrainEnabled && _terrainMesh is { IsReady: true })
                {
                    _object3D = null;
                    _vertexCache = null;
                    break;
                }
                var verts = Object3D.CreatePlaneVertices(1f, 1f, Color);
                _object3D = new Object3D(0, 0, 0);
                _object3D.Generate(shader, verts);
                _vertexCache = verts;
                break;
            }
            case EditorPrimitiveType.Box:
            {
                var verts = Object3D.CreateBoxVertices(1f, 1f, 1f, Color);
                _object3D = new Object3D(0, 0, 0);
                _object3D.Generate(shader, verts);
                _vertexCache = verts;
                break;
            }
            case EditorPrimitiveType.Sphere:
            {
                var verts = Object3D.CreateSphereVertices(0.5f, Color);
                _object3D = new Object3D(0, 0, 0);
                _object3D.Generate(shader, verts);
                _vertexCache = verts;
                break;
            }
            case EditorPrimitiveType.Camera:
            case EditorPrimitiveType.Light:
            case EditorPrimitiveType.Sky:
            {
                // Camera/Light/Sky markers are 2D billboard icons (drawn via Draw2DMarker) —
                // no solid mesh (the camera shows a wireframe frustum gizmo instead).
                // Keep _object3D null so they don't render as 3D boxes/spheres.
                _vertexCache = null;
                break;
            }
            case EditorPrimitiveType.Map2D:
            {
                // Map2D builds a custom quad mesh; tileset texture loaded separately.
                _vertexCache = null;
                break;
            }
            case EditorPrimitiveType.Player2D:
            case EditorPrimitiveType.Start2D:
            {
                // Player/Start markers have no solid mesh — the player renders as an
                // animated sprite quad (DrawPlayer2D) and both draw gizmo outlines
                // via Draw2DMarker. Keep _object3D null.
                _vertexCache = null;
                break;
            }
            case EditorPrimitiveType.GlbReference:
                // glb objects are handled by EditorObjectManager externally
                break;
        }

        // Load texture if specified
        if (!string.IsNullOrEmpty(TexturePath) && File.Exists(TexturePath))
        {
            var tex = new Texture(TexturePath);
            _textureID = tex.ID;
            TexSettings.Apply(_textureID);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        }
        else
        {
            _textureID = 0;
        }

        _dirty = false;
    }

    /// <summary>Alias for EnsureResources() for API compatibility with EditorObjectManager.</summary>
    public void InitGPU()
    {
        EnsureResources();
    }

    // ════════════════════════════════════════════════════════════════════
    //  PBR material (Box/Sphere/flat plane) — GPU textures + dedicated shader
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Force PBR textures to reload on the next draw (after a map path changed).</summary>
    public void InvalidatePbrTextures() => _pbrCacheKey = "";

    /// <summary>Re-apply the per-texture sampling settings (filter / wrapping / mipmapping)
    /// to every GPU texture this object owns: the simple texture uses <see cref="TexSettings"/>,
    /// each PBR map uses its own slot in <see cref="PbrTexSettings"/> and each terrain layer
    /// uses its own slot in <see cref="TerrainLayerSettings"/>. Called by the Inspector when
    /// the settings change — no reload needed.</summary>
    public void ApplyTextureSettings()
    {
        if (_textureID != 0)
        {
            TexSettings.Apply(_textureID);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        }
        for (int i = 0; i < 7; i++)
        {
            if (_pbrTex[i] == 0) continue;
            PbrTexSettings[i].Apply(_pbrTex[i]);
            GL.BindTexture(Const.GL_TEXTURE_2D, 0);
        }
        if (_terrainMesh != null)
            _terrainMesh.ApplyTextureSettings(TerrainLayerSettings);
    }

    private void EnsurePbrTextures()
    {
        string key = $"{PbrAlbedoPath}|{PbrNormalPath}|{PbrMetallicPath}|{PbrRoughnessPath}|{PbrAoPath}|{PbrHeightPath}|{PbrEmissionPath}";
        if (key == _pbrCacheKey) return;
        DisposePbrTextures();
        _pbrCacheKey = key;

        string[] paths = [PbrAlbedoPath, PbrNormalPath, PbrMetallicPath, PbrRoughnessPath, PbrAoPath, PbrHeightPath, PbrEmissionPath];
        string[] names = ["albedo", "normal", "metallic", "roughness", "ao", "height", "emission"];
        var loaded = new List<string>();
        for (int i = 0; i < 7; i++)
        {
            if (string.IsNullOrEmpty(paths[i])) continue;
            string resolved = PathHelpers.Resolve(paths[i]);
            if (!File.Exists(resolved))
            {
                Console.WriteLine($"[EditorObject] '{Name}' PBR {names[i]} map missing: {paths[i]}");
                continue;
            }
            try
            {
                _pbrTex[i] = new Texture(resolved).ID;
                PbrTexSettings[i].Apply(_pbrTex[i]);
                GL.BindTexture(Const.GL_TEXTURE_2D, 0);
                loaded.Add($"{names[i]}:{Path.GetFileName(resolved)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EditorObject] '{Name}' PBR {names[i]} map load failed: {ex.Message}");
            }
        }
        Console.WriteLine($"[EditorObject] '{Name}' PBR maps loaded: {(loaded.Count > 0 ? string.Join(", ", loaded) : "none")}");
    }

    private void DisposePbrTextures()
    {
        for (int i = 0; i < 7; i++)
        {
            if (_pbrTex[i] == 0) continue;
            fixed (uint* p = &_pbrTex[i]) GL.DeleteTextures(1, p);
            _pbrTex[i] = 0;
        }
    }

    /// <summary>Shared 1×1 white texture bound to map units that have no texture, so
    /// sampling an inactive unit never reads stale geometry data from another object.</summary>
    private static uint EnsurePbrWhiteTex()
    {
        if (_pbrWhiteTex != 0) return _pbrWhiteTex;
        uint t;
        GL.GenTextures(1, &t);
        GL.BindTexture(Const.GL_TEXTURE_2D, t);
        byte[] white = [255, 255, 255, 255]; // RGBA — one full texel
        fixed (byte* w = white)
            GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA, 1, 1, 0, Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, w);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_REPEAT);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_REPEAT);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);
        _pbrWhiteTex = t;
        return t;
    }

    /// <summary>Cached uniform locations of the object PBR shader (one-time init, like
    /// the GlbUniforms pattern used by EditorObjectManager).</summary>
    private static class PbrUniforms
    {
        public static bool Ready;
        public static uint Program;
        public static int View, Proj, Model, SunDir, LightColor, ViewPos, FogColor, UseFog;
        public static readonly int[] UvScale = new int[7];   // per-map u_uvScale[i]
        public static readonly int[] UvOffset = new int[7];  // per-map u_uvOffset[i]
        public static readonly int[] Maps = new int[7];     // albedo..emission (units 0-6)
        public static readonly int[] UseMaps = new int[7];  // useAlbedo..useEmission
        public static int AlbedoTune, NormalTune, MetallicTune, RoughnessTune, AoTune, HeightTune, EmissionIntensity;
        public static int ShadowFilter, ShadowDir, ShadowMap0, ShadowMap1, ShadowMap2;
        public static int LightSpace0, LightSpace1, LightSpace2, CascadeEnds0, CascadeEnds1, CascadeEnds2;
        public static int ShowCSMCascadeColor;
        public static int ParallaxScale;

        public static void Ensure()
        {
            if (Ready) return;
            Program = Shader.GetObjectPbrShaderProgram();
            View = GL.GetUniformLocation(Program, "view");
            Proj = GL.GetUniformLocation(Program, "projection");
            Model = GL.GetUniformLocation(Program, "model");
            SunDir = GL.GetUniformLocation(Program, "sunDir");
            LightColor = GL.GetUniformLocation(Program, "lightColor");
            ViewPos = GL.GetUniformLocation(Program, "viewPos");
            FogColor = GL.GetUniformLocation(Program, "fogColor");
            UseFog = GL.GetUniformLocation(Program, "useFog");
            for (int i = 0; i < 7; i++)
            {
                UvScale[i] = GL.GetUniformLocation(Program, $"u_uvScale[{i}]");
                UvOffset[i] = GL.GetUniformLocation(Program, $"u_uvOffset[{i}]");
            }
            string[] mapNames = ["albedoMap", "normalMap", "metallicMap", "roughnessMap", "aoMap", "heightMap", "emissionMap"];
            string[] useNames = ["useAlbedo", "useNormal", "useMetallic", "useRoughness", "useAo", "useHeight", "useEmission"];
            for (int i = 0; i < 7; i++)
            {
                Maps[i] = GL.GetUniformLocation(Program, mapNames[i]);
                UseMaps[i] = GL.GetUniformLocation(Program, useNames[i]);
            }
            AlbedoTune = GL.GetUniformLocation(Program, "u_albedoTuning");
            NormalTune = GL.GetUniformLocation(Program, "u_normalTuning");
            MetallicTune = GL.GetUniformLocation(Program, "u_metallicTuning");
            RoughnessTune = GL.GetUniformLocation(Program, "u_roughnessTuning");
            AoTune = GL.GetUniformLocation(Program, "u_aoTuning");
            HeightTune = GL.GetUniformLocation(Program, "u_heightTuning");
            EmissionIntensity = GL.GetUniformLocation(Program, "u_emissionIntensity");
            ShadowFilter = GL.GetUniformLocation(Program, "shadowFilterMode");
            ShadowDir = GL.GetUniformLocation(Program, "shadowDir");
            ShadowMap0 = GL.GetUniformLocation(Program, "shadowMap0");
            ShadowMap1 = GL.GetUniformLocation(Program, "shadowMap1");
            ShadowMap2 = GL.GetUniformLocation(Program, "shadowMap2");
            LightSpace0 = GL.GetUniformLocation(Program, "lightSpaceMatrices[0]");
            LightSpace1 = GL.GetUniformLocation(Program, "lightSpaceMatrices[1]");
            LightSpace2 = GL.GetUniformLocation(Program, "lightSpaceMatrices[2]");
            CascadeEnds0 = GL.GetUniformLocation(Program, "cascadeEnds[0]");
            CascadeEnds1 = GL.GetUniformLocation(Program, "cascadeEnds[1]");
            CascadeEnds2 = GL.GetUniformLocation(Program, "cascadeEnds[2]");
            ShowCSMCascadeColor = GL.GetUniformLocation(Program, "showCSMCascadeColor");
            ParallaxScale = GL.GetUniformLocation(Program, "parallaxScale");
            Ready = true;
        }
    }

    /// <summary>Render this primitive with the PBR material shader (maps on units 0-6,
    /// CSM shadows on units 7/8/9). Restores the main shader and its shadow bindings so
    /// the next object in the editor pass renders exactly as before.</summary>
    private void DrawPbrPrimitive(Camera camera, Lights light, CSM? csm)
    {
        PbrUniforms.Ensure();
        uint pbr = PbrUniforms.Program;
        if (pbr == 0) return;

        EnsurePbrTextures();
        GL.UseProgram(pbr);

        var model = WorldMatrix;
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix();
        GL.UniformMatrix4fv(PbrUniforms.Model, 1, false, (float*)&model);
        GL.UniformMatrix4fv(PbrUniforms.View, 1, false, (float*)&view);
        GL.UniformMatrix4fv(PbrUniforms.Proj, 1, false, (float*)&proj);
        GL.Uniform3f(PbrUniforms.SunDir, light.SunDir.X, light.SunDir.Y, light.SunDir.Z);
        GL.Uniform3f(PbrUniforms.LightColor, light.LightColor.X, light.LightColor.Y, light.LightColor.Z);
        GL.Uniform3f(PbrUniforms.ViewPos, camera.Position.X, camera.Position.Y, camera.Position.Z);

        // ── Fog (enable, mode, color, density, start/end, height — Config.FogSettings) ──
        Visual.FogUniforms.UploadMain(PbrUniforms.Program, light);

        if (PbrUniforms.ShowCSMCascadeColor >= 0)
            GL.Uniform1i(PbrUniforms.ShowCSMCascadeColor, Keyboard.GetshowCSMCascadeColor() ? 1 : 0);

        // ── Local point/spot lights (from editor Light markers) ──
        light.UploadLocalLights(pbr);

        // Live shadow bias / blend tuning (Shadow Settings panel).
        Visual.ShadowUniforms.UploadMain(pbr);

        // ── CSM shadow uniforms → units 7/8/9 ──
        if (csm != null)
        {
            if (PbrUniforms.ShadowFilter >= 0) GL.Uniform1i(PbrUniforms.ShadowFilter, Keyboard.GetIsHardShadow());
            if (PbrUniforms.ShadowDir >= 0) GL.Uniform3f(PbrUniforms.ShadowDir, light.ShadowDirStable.X, light.ShadowDirStable.Y, light.ShadowDirStable.Z);
            unsafe
            {
                fixed (float* p0 = &csm.LightSpaceMatrices[0].M11)
                    GL.UniformMatrix4fv(PbrUniforms.LightSpace0, 1, false, p0);
                fixed (float* p1 = &csm.LightSpaceMatrices[1].M11)
                    GL.UniformMatrix4fv(PbrUniforms.LightSpace1, 1, false, p1);
                fixed (float* p2 = &csm.LightSpaceMatrices[2].M11)
                    GL.UniformMatrix4fv(PbrUniforms.LightSpace2, 1, false, p2);
            }
            GL.Uniform1f(PbrUniforms.CascadeEnds0, csm.CascadeEnds[0]);
            GL.Uniform1f(PbrUniforms.CascadeEnds1, csm.CascadeEnds[1]);
            GL.Uniform1f(PbrUniforms.CascadeEnds2, csm.CascadeEnds[2]);
            GL.Uniform1i(PbrUniforms.ShadowMap0, 7);
            GL.Uniform1i(PbrUniforms.ShadowMap1, 8);
            GL.Uniform1i(PbrUniforms.ShadowMap2, 9);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 7);
            GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[0]);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 8);
            GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[1]);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 9);
            GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[2]);
        }

        // ── PBR maps (units 0-6); missing maps get the shared white texture ──
        uint white = EnsurePbrWhiteTex();
        for (int i = 0; i < 7; i++)
        {
            GL.ActiveTexture(Const.GL_TEXTURE0 + (uint)i);
            GL.BindTexture(Const.GL_TEXTURE_2D, _pbrTex[i] != 0 ? _pbrTex[i] : white);
            GL.Uniform1i(PbrUniforms.Maps[i], i);
            GL.Uniform1i(PbrUniforms.UseMaps[i], _pbrTex[i] != 0 ? 1 : 0);
        }

        // ── UV tiling + offset per map (uniform-only, no texture reload) ──
        // Global PbrTexTiling multiplies into each per-map tiling so the slider
        // scales all maps uniformly in real-time.
        float globalTiling = PbrTexTiling;
        for (int i = 0; i < 7; i++)
        {
            if (PbrUniforms.UvScale[i] >= 0)
                GL.Uniform2f(PbrUniforms.UvScale[i], PbrTexSettings[i].TilingX * globalTiling, PbrTexSettings[i].TilingY * globalTiling);
            if (PbrUniforms.UvOffset[i] >= 0)
                GL.Uniform2f(PbrUniforms.UvOffset[i], PbrTexSettings[i].OffsetX, PbrTexSettings[i].OffsetY);
        }
        GL.Uniform3f(PbrUniforms.AlbedoTune, TerrainPbrAlbedoBrightness, TerrainPbrAlbedoSaturation, TerrainPbrAlbedoContrast);
        GL.Uniform2f(PbrUniforms.NormalTune, TerrainPbrNormalStrength, TerrainPbrNormalBlur);
        GL.Uniform3f(PbrUniforms.MetallicTune, TerrainPbrMetallicThreshold, TerrainPbrMetallicSoftness, TerrainPbrMetallicStrength);
        GL.Uniform2f(PbrUniforms.RoughnessTune, TerrainPbrRoughnessStrength, TerrainPbrRoughnessInvert ? 1f : 0f);
        GL.Uniform2f(PbrUniforms.AoTune, TerrainPbrAoStrength, TerrainPbrAoBrightness);
        GL.Uniform3f(PbrUniforms.HeightTune, TerrainPbrHeightStrength, TerrainPbrHeightInvert ? 1f : 0f, TerrainPbrHeightBlur);
        GL.Uniform1f(PbrUniforms.EmissionIntensity, TerrainPbrEmissionIntensity);
        if (PbrUniforms.ParallaxScale >= 0) GL.Uniform1f(PbrUniforms.ParallaxScale, PbrParallaxScale);

        GL.BindVertexArray(_object3D!.VAO);
        GL.DrawArrays(Const.GL_TRIANGLES, 0, _object3D.VertexCount);
        GL.BindVertexArray(0);

        // ── Restore: main shader + its shadow bindings at units 6/7/8 (the main pass
        //    bound them before this object; our maps clobbered 6 and shadows 7/8/9). ──
        if (csm != null)
        {
            GL.ActiveTexture(Const.GL_TEXTURE0 + 6);
            GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[0]);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 7);
            GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[1]);
            GL.ActiveTexture(Const.GL_TEXTURE0 + 8);
            GL.BindTexture(Const.GL_TEXTURE_2D, csm.ShadowTextures[2]);
        }
        GL.ActiveTexture(Const.GL_TEXTURE0);
        GL.UseProgram(Shader.GetShaderProgram());
    }

    // ════════════════════════════════════════════════════════════════════
    //  Terrain brush paint (viewport tool) + painted-data persistence
    // ════════════════════════════════════════════════════════════════════

    /// <summary>True when this terrain has been edited with the paint brush.</summary>
    public bool TerrainIsModified => _terrainMesh is { IsModified: true };

    /// <summary>Total triangles of this terrain mesh (sum over ALL chunk sub-meshes).
    /// 0 when this object is not an active terrain.</summary>
    public int TerrainTriangleCount => _terrainMesh is { IsReady: true } m ? m.TriangleCount : 0;


    /// <summary>Base64-encoded painted heightmap blob for scene persistence.
    /// Empty string = no user edits (nothing to save).</summary>
    public string TerrainPaintedData
    {
        get
        {
            // Live-capture from the mesh so edits made this session are always current.
            if (_terrainMesh is { IsModified: true } m)
            {
                _terrainPaintedCache = m.GetModifiedRaw();
                _terrainPaintedSourcePath = TerrainHeightmapPath;
            }
            // Never persist a cache that belongs to a different heightmap file.
            if (_terrainPaintedCache == null || _terrainPaintedSourcePath != TerrainHeightmapPath)
                return "";
            return Convert.ToBase64String(_terrainPaintedCache);
        }
        set
        {
            _terrainPaintedCache = null;
            if (string.IsNullOrEmpty(value)) return;
            try
            {
                _terrainPaintedCache = Convert.FromBase64String(value);
                // Only restored while the terrain points at the same heightmap file.
                _terrainPaintedSourcePath = TerrainHeightmapPath;
            }
            catch
            {
                _terrainPaintedCache = null;
            }
        }
    }

    /// <summary>Raycast this terrain's surface. Returns the world-space surface point
    /// (brush cursor / placement) or null when the ray misses the footprint.</summary>
    /// <summary>Transform the ray into this terrain's local space and intersect the Y=0 base
    /// plane. Returns the local XZ point (in [-0.5, 0.5]²) when the ray hits the footprint.
    /// Shared by every brush operation so ray math stays consistent.</summary>
    private bool TryGetTerrainLocalPoint(Vector3 rayOrigin, Vector3 rayDir, out Vector2 local)
    {
        local = Vector2.Zero;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true })
            return false;
        var model = TerrainModelMatrix;
        if (!Matrix4x4.Invert(model, out var invModel))
            return false;
        var localOrigin = Vector3.Transform(rayOrigin, invModel);
        var localDir = Vector3.TransformNormal(rayDir, invModel);
        if (Math.Abs(localDir.Y) < 1e-6f)
            return false;
        float t = -localOrigin.Y / localDir.Y;
        if (t <= 0f)
            return false;
        var localHit = localOrigin + localDir * t;
        if (localHit.X < -0.5f || localHit.X > 0.5f || localHit.Z < -0.5f || localHit.Z > 0.5f)
            return false;
        local = new Vector2(localHit.X, localHit.Z);
        return true;
    }

    /// <summary>Brush radius converted to local units (world brush size ÷ footprint).</summary>
    private float LocalBrushRadius =>
        Math.Max(0.02f, TerrainBrushSize) / MathF.Max(MathF.Max(0.1f, Scale.X), MathF.Max(0.1f, Scale.Z));

    /// <summary>World-space surface point under the ray (brush cursor / placement).</summary>
    public Vector3? RaycastTerrainSurface(Vector3 rayOrigin, Vector3 rayDir)
    {
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return null;
        // Quick reject against the world AABB before the precise intersection.
        if (!AABB.RayIntersectsAABB(rayOrigin, rayDir, GetWorldAABB(), out _, out _))
            return null;
        if (!TryGetTerrainLocalPoint(rayOrigin, rayDir, out var local))
            return null;
        float h = m.SampleLocalHeight(local.X, local.Y);
        return Vector3.Transform(new Vector3(local.X, h, local.Y), TerrainModelMatrix);
    }

    /// <summary>Paint one brush stamp into the terrain at the ray's intersection.
    /// <paramref name="deltaWorld"/> is the height delta in world units (positive =
    /// raise, negative = lower). Returns true + the world hit point when painted.</summary>
    public bool TryPaintTerrainSurface(Vector3 rayOrigin, Vector3 rayDir, float deltaWorld, out Vector3 worldHitPoint)
    {
        worldHitPoint = Vector3.Zero;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return false;
        if (!TryGetTerrainLocalPoint(rayOrigin, rayDir, out var local))
            return false;
        // Convert the world delta (sculpt strength in world units) to normalized height.
        float deltaNorm = deltaWorld / Math.Max(1f, TerrainHeightScale);
        m.PaintHeight(local.X, local.Y, LocalBrushRadius, deltaNorm, TerrainBrushSoftness, TerrainBrushFalloff);

        float h = m.SampleLocalHeight(local.X, local.Y);
        worldHitPoint = Vector3.Transform(new Vector3(local.X, h, local.Y), TerrainModelMatrix);
        return true;
    }

    /// <summary>Smooth one brush stamp at the ray's terrain intersection (averages the
    /// heights in the brush area). <paramref name="strength"/> 0..1 blend per stamp.</summary>
    public bool TrySmoothTerrainSurface(Vector3 rayOrigin, Vector3 rayDir, float strength, out Vector3 worldHitPoint)
    {
        worldHitPoint = Vector3.Zero;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return false;
        if (!TryGetTerrainLocalPoint(rayOrigin, rayDir, out var local))
            return false;
        m.SmoothHeight(local.X, local.Y, LocalBrushRadius, strength, TerrainBrushFalloff);

        float h = m.SampleLocalHeight(local.X, local.Y);
        worldHitPoint = Vector3.Transform(new Vector3(local.X, h, local.Y), TerrainModelMatrix);
        return true;
    }

    /// <summary>Flatten one brush stamp at the ray's terrain intersection toward
    /// <paramref name="targetNorm"/> (normalized 0..1 height). <paramref name="strength"/>
    /// 0..1 blend per stamp.</summary>
    public bool TryFlattenTerrainSurface(Vector3 rayOrigin, Vector3 rayDir, float targetNorm, float strength, out Vector3 worldHitPoint)
    {
        worldHitPoint = Vector3.Zero;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return false;
        if (!TryGetTerrainLocalPoint(rayOrigin, rayDir, out var local))
            return false;
        m.FlattenHeight(local.X, local.Y, LocalBrushRadius, targetNorm, strength, TerrainBrushFalloff);

        float h = m.SampleLocalHeight(local.X, local.Y);
        worldHitPoint = Vector3.Transform(new Vector3(local.X, h, local.Y), TerrainModelMatrix);
        return true;
    }

    /// <summary>Smooth the ENTIRE terrain heightmap in one pass.
    /// Used by the "Smooth All" suggestion button in the Terrain Brush panel.</summary>
    public void SmoothAllTerrain(int passes = 2, float strength = 0.4f)
    {
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return;
        m.SmoothAllHeights(passes, strength);
        Console.WriteLine($"[TerrainBrush] Smoothed entire heightmap on '{Name}' ({passes} passes, strength={strength:F2})");
    }

    /// <summary>Normalized (0..1) terrain height under the ray — the flatten tool captures
    /// this from the first stamp of a stroke as its level target.</summary>
    public bool TryGetTerrainNormalizedHeight(Vector3 rayOrigin, Vector3 rayDir, out float targetNorm)
    {
        targetNorm = 0f;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return false;
        if (!TryGetTerrainLocalPoint(rayOrigin, rayDir, out var local))
            return false;
        targetNorm = m.SampleLocalHeightNorm(local.X, local.Y);
        return true;
    }

    /// <summary>Snapshot the terrain's height array (undo support).</summary>
    public float[]? CaptureTerrainHeights() => _terrainMesh?.GetHeightSnapshot();

    /// <summary>Restore a height snapshot (undo/redo) and rebuild the GPU mesh.</summary>
    public void RestoreTerrainHeights(float[]? heights)
    {
        if (_terrainMesh != null && heights != null)
            _terrainMesh.RestoreHeightSnapshot(heights);
    }

    /// <summary>Write the current painted heights to a .raw file (returns success).</summary>
    public bool SaveTerrainHeightmap(string path)
    {
        if (_terrainMesh == null || string.IsNullOrEmpty(path)) return false;
        bool ok = _terrainMesh.SaveHeightmapFile(path);
        if (ok)
        {
            // Point the terrain at the saved file so future rebuilds read the edits.
            TerrainHeightmapPath = path;
            MarkDirty();
        }
        return ok;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Manual layer paint (splat) — 🎨 brush
    // ════════════════════════════════════════════════════════════════════

    /// <summary>True when this terrain has manual layer paint to show.</summary>
    public bool TerrainSplatIsModified => _terrainMesh is { SplatModified: true };

    /// <summary>Serialized splat blob (base64) for scene persistence. Empty = no paint.</summary>
    public string TerrainSplatData
    {
        get
        {
            if (_terrainMesh is { SplatModified: true } m)
                _terrainSplatCache = m.GetModifiedSplatRaw();
            return _terrainSplatCache == null ? "" : Convert.ToBase64String(_terrainSplatCache);
        }
        set
        {
            _terrainSplatCache = null;
            if (string.IsNullOrEmpty(value)) return;
            try { _terrainSplatCache = Convert.FromBase64String(value); }
            catch { _terrainSplatCache = null; }
        }
    }

    /// <summary>Paint one splat stamp (layer 0..3) at the ray's terrain intersection.
    /// <paramref name="erase"/> decays painted weights back toward automatic texturing.
    /// Returns true + the world hit point when painted.</summary>
    public bool TryPaintLayerSurface(Vector3 rayOrigin, Vector3 rayDir, int layerIndex, float strength, bool erase, out Vector3 worldHitPoint)
    {
        worldHitPoint = Vector3.Zero;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return false;
        if (!TryGetTerrainLocalPoint(rayOrigin, rayDir, out var local))
            return false;
        m.PaintLayer(local.X, local.Y, LocalBrushRadius, layerIndex, strength, TerrainBrushSoftness, TerrainBrushFalloff, erase);

        float h = m.SampleLocalHeight(local.X, local.Y);
        worldHitPoint = Vector3.Transform(new Vector3(local.X, h, local.Y), TerrainModelMatrix);
        return true;
    }

    /// <summary>Snapshot the splat bytes (undo support).</summary>
    public byte[]? CaptureTerrainSplat() => _terrainMesh?.GetSplatSnapshot();

    /// <summary>Restore a splat snapshot (undo/redo).</summary>
    public void RestoreTerrainSplat(byte[]? splat)
    {
        if (_terrainMesh != null && splat != null)
            _terrainMesh.RestoreSplatSnapshot(splat);
    }

    /// <summary>Clear ALL manual layer paint (back to automatic height+slope texturing).</summary>
    public void ClearTerrainLayerPaint()
    {
        _terrainMesh?.ClearSplatPaint();
    }

    /// <summary>
    /// Draw the brush ring ON this terrain's surface — a translucent highlight disc that
    /// follows the heightmap (sampled every segment), so the user sees exactly which area
    /// of the plane the brush will affect. Ring radius = TerrainBrushSize (same footprint
    /// mapping as painting). Depth test is disabled so the ring never z-fights with the
    /// terrain mesh. Color + transparency come from BrushIndicatorColor/BrushIndicatorAlpha
    /// (editable in the Terrain Brush panel and saved with the scene).
    /// </summary>
    public void DrawTerrainBrushIndicator(Camera camera)
    {
        if (!ShowBrushIndicator || BrushIndicatorPos is not Vector3 center)
            return;
        if (PrimitiveType != EditorPrimitiveType.Plane || !TerrainEnabled || _terrainMesh is not { IsReady: true } m)
            return;

        var model = TerrainModelMatrix;
        if (!Matrix4x4.Invert(model, out var invModel))
            return;
        var localCenter = Vector3.Transform(center, invModel);

        float footprint = MathF.Max(MathF.Max(0.1f, Scale.X), MathF.Max(0.1f, Scale.Z));
        float rLocal = Math.Max(0.02f, TerrainBrushSize) / footprint;

        const int Segments = 48;

        // Sample the ring points ON the terrain surface (height-following).
        var ringPts = new List<Vector3>(Segments + 1);
        for (int i = 0; i <= Segments; i++)
        {
            float a = (float)i / Segments * MathF.PI * 2f;
            float lx = Math.Clamp(localCenter.X + MathF.Cos(a) * rLocal, -0.5f, 0.5f);
            float lz = Math.Clamp(localCenter.Z + MathF.Sin(a) * rLocal, -0.5f, 0.5f);
            float h = m.SampleLocalHeight(lx, lz);
            ringPts.Add(Vector3.Transform(new Vector3(lx, h, lz), model));
        }
        // The last point (segment Segments == point 0) closes the loop.

        // ── Translucent fill: spokes from the brush center to every ring point. Drawing
        // them as semi-transparent lines makes the whole disc read as a soft highlight
        // instead of a hard wire circle. The center height is sampled at the hover point.
        float hCenter = m.SampleLocalHeight(localCenter.X, localCenter.Z);
        var centerWorld = Vector3.Transform(new Vector3(localCenter.X, hCenter, localCenter.Z), model);
        var fill = new List<Vector3>(Segments * 2);
        for (int i = 0; i < Segments; i++)
        {
            fill.Add(centerWorld);
            fill.Add(ringPts[i]);
        }

        // ── Bright outline: the ring edge itself, drawn on top of the fill. ──
        var outline = new List<Vector3>(Segments * 2);
        for (int i = 0; i < Segments; i++)
        {
            outline.Add(ringPts[i]);
            outline.Add(ringPts[i + 1]);
        }

        GL.Disable(Const.GL_DEPTH_TEST);
        float fillAlpha = Math.Clamp(BrushIndicatorAlpha * 0.6f, 0f, 0.85f);
        float outlineAlpha = Math.Clamp(BrushIndicatorAlpha + 0.35f, 0f, 1f);
        Terrains.TerrainChunk.DrawLineSegments(fill, BrushIndicatorColor, camera, fillAlpha);
        Terrains.TerrainChunk.DrawLineSegments(outline, BrushIndicatorColor, camera, outlineAlpha);
        GL.Enable(Const.GL_DEPTH_TEST);
    }
    /// <summary>Draw using individual uniform locations (matching EditorObjectManager's call pattern).</summary>
        public void Draw(
            int modelLoc, int viewLoc, int projLoc,
            int sunDirLoc, int lightColorLoc, int viewPosLoc,
            int useFogLoc, int fogColorLoc,
            Camera camera, Lights light, CSM? csm = null)
        {
            if (!IsVisible) return;

            // Rebuild GPU resources if MarkDirty() was called (e.g. color changed in the
            // Inspector) — otherwise the old vertices/color would keep rendering forever.
            EnsureResources();

            // ── Advanced terrain plane: render with the dedicated terrain shader. ──
            if (PrimitiveType == EditorPrimitiveType.Plane && TerrainEnabled && _terrainMesh is { IsReady: true })
            {
                // XZ footprint follows Scale.X/Z; Y stays at real world height (the
                // plane's thin Scale.Y must NOT flatten the terrain).
                _terrainMesh.Draw(TerrainModelMatrix, camera, light, this, csm);

                // ── Brush ring indicator ON the terrain surface (while the brush tool
                // hovers this terrain) — drawn after the mesh so it's always on top. ──
                DrawTerrainBrushIndicator(camera);

                // The terrain shader switches the active program — restore the main
                // shader so subsequent editor objects render correctly.
                GL.UseProgram(Shader.GetShaderProgram());
                return;
            }

            // Terrain enabled but no valid mesh (missing heightmap): fall back to the
            // flat plane mesh so the object stays visible and editable.

            // ── 2D Map: render as textured plane with tile images ──
            if (PrimitiveType == EditorPrimitiveType.Map2D)
            {
                DrawMap2D(modelLoc, viewLoc, projLoc, sunDirLoc, lightColorLoc, viewPosLoc, useFogLoc, fogColorLoc, camera, light, csm);
                return;
            }

            if (_object3D == null) return;

            // ── PBR material (Box/Sphere/flat plane): dedicated PBR shader with the
            // optional maps + tuning. Only used when the shader compiled — otherwise
            // fall through to the plain vertex-color path so the object never vanishes
            // (and empty/absent maps are simply not applied). ──
            if (HasPbrMaterial && Shader.GetObjectPbrShaderProgram() != 0)
            {
                DrawPbrPrimitive(camera, light, csm);
                return;
            }

            // Set correct model matrix (WorldMatrix includes position, scale, rotation)
            var model = WorldMatrix;
            GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&model);

            // Primitives are CCW-wound, so the scene's culling/winding state (applied via
            // SceneRenderProperties) is respected here — do NOT force culling on/off.

            // Set useTexture=0 so fragment shader uses vertex color instead of textures
            int useTexLoc = GL.GetUniformLocation(Shader.GetShaderProgram(), "useTexture");
            GL.Uniform1i(useTexLoc, 0);

            // Render VAO directly (bypass Object3D.Draw to avoid model matrix override)
            GL.BindVertexArray(_object3D.VAO);
            GL.DrawArrays(Const.GL_TRIANGLES, 0, _object3D.VertexCount);
            GL.BindVertexArray(0);
        }

    /// <summary>
    /// Draw a real-camera gizmo for this placed camera: a small camera body box plus a
    /// view-frustum wireframe (near plane, far plane, connector lines and a center ray)
    /// built from <see cref="CameraFov"/>, <see cref="CameraNear"/> and <see cref="CameraFar"/>.
    /// The frustum follows the object's rotation (Yaw/Pitch/Roll) so rotating the marker
    /// points the frustum the same way. Depth test is disabled so the wireframe shows
    /// through terrain and other geometry (like other editor helpers).
    /// </summary>
    public unsafe void DrawCameraFrustum(Camera camera)
    {
        if (!IsVisible || PrimitiveType != EditorPrimitiveType.Camera) return;

        float yaw = RotationEuler.Y * MathF.PI / 180f;
        float pitch = RotationEuler.X * MathF.PI / 180f;
        float roll = RotationEuler.Z * MathF.PI / 180f;
        var rot = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);

        // Camera looks down -Z (same convention as the engine Camera + gizmo rotation).
        var forward = Vector3.Transform(-Vector3.UnitZ, rot);
        var up = Vector3.Transform(Vector3.UnitY, rot);
        var right = Vector3.Transform(Vector3.UnitX, rot);

        float fovRad = CameraFov * MathF.PI / 180f;
        float aspect = 16f / 9f; // assumed viewport aspect for the preview frustum

        // Display distances: keep the real near plane exact, but clamp the *displayed*
        // far distance so a 500-unit far clip doesn't produce a giant wireframe that
        // dominates the scene. Near is floored so the near quad stays readable.
        float displayNear = MathF.Max(CameraNear, 0.25f);
        float displayFar = MathF.Min(CameraFar, 100f);

        float nearHalfH = MathF.Tan(fovRad * 0.5f) * displayNear;
        float nearHalfW = nearHalfH * aspect;
        float farHalfH = MathF.Tan(fovRad * 0.5f) * displayFar;
        float farHalfW = farHalfH * aspect;

        Vector3 nearCenter = Position + forward * displayNear;
        Vector3 farCenter = Position + forward * displayFar;

        // 8 frustum corners: near quad then far quad
        Vector3 n0 = nearCenter - right * nearHalfW + up * nearHalfH;
        Vector3 n1 = nearCenter + right * nearHalfW + up * nearHalfH;
        Vector3 n2 = nearCenter + right * nearHalfW - up * nearHalfH;
        Vector3 n3 = nearCenter - right * nearHalfW - up * nearHalfH;
        Vector3 f0 = farCenter - right * farHalfW + up * farHalfH;
        Vector3 f1 = farCenter + right * farHalfW + up * farHalfH;
        Vector3 f2 = farCenter + right * farHalfW - up * farHalfH;
        Vector3 f3 = farCenter - right * farHalfW - up * farHalfH;

        var verts = new List<Vector3>(48);
        void Line(Vector3 a, Vector3 b) { verts.Add(a); verts.Add(b); }

        // Near plane quad
        Line(n0, n1); Line(n1, n2); Line(n2, n3); Line(n3, n0);
        // Far plane quad
        Line(f0, f1); Line(f1, f2); Line(f2, f3); Line(f3, f0);
        // Connectors near → far
        Line(n0, f0); Line(n1, f1); Line(n2, f2); Line(n3, f3);
        // Center ray (from camera origin to far center, with a small crosshair dot at the
        // far center so the look target is obvious)
        Line(Position, farCenter);
        float r = farHalfW * 0.06f;
        Line(farCenter - right * r, farCenter + right * r);
        Line(farCenter - up * r, farCenter + up * r);

        // Small camera body box right at the origin (wireframe, slightly larger than the
        // solid marker so the lens direction reads clearly)
        float b = 0.28f * MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));
        Vector3[] body =
        [
            Position - right * b - up * b - forward * b, Position + right * b - up * b - forward * b,
            Position + right * b + up * b - forward * b, Position - right * b + up * b - forward * b,
            Position - right * b - up * b + forward * b, Position + right * b - up * b + forward * b,
            Position + right * b + up * b + forward * b, Position - right * b + up * b + forward * b,
        ];
        int[] boxEdges = [0,1, 1,2, 2,3, 3,0, 4,5, 5,6, 6,7, 7,4, 0,4, 1,5, 2,6, 3,7];
        for (int i = 0; i < boxEdges.Length; i += 2)
            Line(body[boxEdges[i]], body[boxEdges[i + 1]]);

        // Lens: small quad on the +forward face to show which way the camera points
        float l = b * 0.55f;
        Vector3 lensC = Position + forward * b;
        Vector3[] lens =
        [
            lensC - right * l - up * l, lensC + right * l - up * l,
            lensC + right * l + up * l, lensC - right * l + up * l,
        ];
        Line(lens[0], lens[1]); Line(lens[1], lens[2]); Line(lens[2], lens[3]); Line(lens[3], lens[0]);

        DrawEditorLines(verts, camera);
    }

    /// <summary>Render editor wireframe lines (depth-test off so they show through
    /// geometry), shared by the camera frustum and light gizmos.</summary>
    private void DrawEditorLines(List<Vector3> verts, Camera camera)
    {
        DrawEditorLines(verts, camera, Color);
    }

    /// <summary>Render editor wireframe lines in a custom color (depth-test off so they
    /// show through geometry). Used by the light gizmo to color-code its parts.</summary>
    private void DrawEditorLines(List<Vector3> verts, Camera camera, Vector3 lineColor)
    {
        if (verts == null || verts.Count == 0) return;
        GL.Disable(Const.GL_DEPTH_TEST);
        Terrains.TerrainChunk.DrawLineSegments(verts, lineColor, camera);
        GL.Enable(Const.GL_DEPTH_TEST);
    }

    /// <summary>
    /// Draw a real-light gizmo for this placed light: a bright direction axis showing
    /// where <see cref="LightDirection"/> points (the beam the user must "see" first),
    /// a dimmer spotlight cone visualizing <see cref="LightConeAngle"/>, a sun marker
    /// at the anchor, and a small RGB axis tripod so the marker's local orientation is
    /// always readable. Each part is drawn in its own color so the beam never blends
    /// into the cone. Depth test disabled so lines show through geometry.
    /// </summary>
    public unsafe void DrawLightGizmo(Camera camera)
    {
        if (!IsVisible || PrimitiveType != EditorPrimitiveType.Light) return;

        float yaw = RotationEuler.Y * MathF.PI / 180f;
        float pitch = RotationEuler.X * MathF.PI / 180f;
        float roll = RotationEuler.Z * MathF.PI / 180f;
        var rot = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);

        // The light direction is a world-space vector on the object; apply the marker's
        // rotation so rotating the object re-aims the beam (matches WorldMatrix convention).
        var dir = Vector3.Transform(LightDirection, rot);
        float len = dir.Length();
        if (len < 1e-4f) len = 1f;
        var beam = dir / len; // normalized aim direction

        // Display beam length: respect intensity a bit, but keep it bounded so it never
        // dominates the scene (intensity is a multiplier, not distance).
        float displayLen = Math.Clamp(4f + LightIntensity * 3f, 4f, 24f);

        // Build an orthonormal basis around the beam for the cone circle
        var upRef = MathF.Abs(beam.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
        var right = Vector3.Normalize(Vector3.Cross(beam, upRef));
        var up = Vector3.Normalize(Vector3.Cross(right, beam));

        // Separate vertex lists — one per color — so the axis reads instantly.
        var beamVerts = new List<Vector3>(32); // bright gold: THE direction axis
        var coneVerts = new List<Vector3>(64); // dim amber: spotlight spread
        var sunVerts  = new List<Vector3>(40); // object color: light anchor
        var rangeVerts = new List<Vector3>(64); // point-light sphere / range

        void Line(List<Vector3> list, Vector3 a, Vector3 b) { list.Add(a); list.Add(b); }

        // ── Direction axis (the beam): origin → tip with a prominent arrowhead. ──
        // This is the line the user must spot first, so it gets the brightest color.
        // Direct lights always show the beam; spotlights show it too (cone on top).
        if (LightTypeEnum != LightType.Point)
        {
            Vector3 tip = Position + beam * displayLen;
            Line(beamVerts, Position, tip);
            float head = displayLen * 0.10f;
            Line(beamVerts, tip, tip - beam * head + right * head * 0.7f);
            Line(beamVerts, tip, tip - beam * head - right * head * 0.7f);
            Line(beamVerts, tip, tip - beam * head + up * head * 0.7f);
            Line(beamVerts, tip, tip - beam * head - up * head * 0.7f);
        }

        // ── Spotlight cone (dim): a circle at the beam tip whose radius grows with
        // distance, plus cone edge lines from the origin to that circle. ──
        if (LightTypeEnum == LightType.Spotlight)
        {
            Vector3 tip = Position + beam * displayLen;
            float coneHalf = Math.Clamp(LightConeAngle, 1f, 89f) * MathF.PI / 180f;
            float coneRadius = MathF.Tan(coneHalf) * displayLen;
            const int segs = 12;
            var ring = new Vector3[segs];
            for (int i = 0; i < segs; i++)
            {
                float a = i * MathF.PI * 2f / segs;
                ring[i] = tip + right * (MathF.Cos(a) * coneRadius)
                              + up * (MathF.Sin(a) * coneRadius);
            }
            for (int i = 0; i < segs; i++)
            {
                Line(coneVerts, ring[i], ring[(i + 1) % segs]); // ring edge
                Line(coneVerts, Position, ring[i]);             // cone side
            }
        }

        // ── Point light: wireframe sphere showing the falloff range (LightPointRadius),
        // drawn in the object's color so the reach of the light is obvious. ──
        if (LightTypeEnum == LightType.Point)
        {
            float pr = MathF.Max(0.5f, LightPointRadius > 0f ? LightPointRadius : 50f);
            // Three great circles around the anchor (XY, XZ, YZ planes).
            const int psegs = 20;
            void Ring(int axis)
            {
                for (int i = 0; i < psegs; i++)
                {
                    float a0 = i * MathF.PI * 2f / psegs;
                    float a1 = (i + 1) * MathF.PI * 2f / psegs;
                    var p0 = Position + new Vector3(MathF.Cos(a0), MathF.Sin(a0), 0f) * pr;
                    var p1 = Position + new Vector3(MathF.Cos(a1), MathF.Sin(a1), 0f) * pr;
                    if (axis == 1) { p0 = Position + new Vector3(MathF.Cos(a0), 0f, MathF.Sin(a0)) * pr; p1 = Position + new Vector3(MathF.Cos(a1), 0f, MathF.Sin(a1)) * pr; }
                    else if (axis == 2) { p0 = Position + new Vector3(0f, MathF.Cos(a0), MathF.Sin(a0)) * pr; p1 = Position + new Vector3(0f, MathF.Cos(a1), MathF.Sin(a1)) * pr; }
                    Line(rangeVerts, p0, p1);
                }
            }
            Ring(0); Ring(1); Ring(2);
        }

        // ── Sun marker around the origin (circle + cross) in the object's color so the
        // light anchor is obvious even when zoomed far out. ──
        float r = 0.35f * MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));
        const int sunSegs = 8;
        var sun = new Vector3[sunSegs];
        for (int i = 0; i < sunSegs; i++)
        {
            float a = i * MathF.PI * 2f / sunSegs;
            sun[i] = Position + right * (MathF.Cos(a) * r) + up * (MathF.Sin(a) * r);
        }
        for (int i = 0; i < sunSegs; i++)
            Line(sunVerts, sun[i], sun[(i + 1) % sunSegs]);
        Line(sunVerts, Position - right * r, Position + right * r);
        Line(sunVerts, Position - up * r, Position + up * r);

        // ── RGB local-axis tripod at the anchor (X red, Y green, Z blue, matching the
        // transform gizmo colors) so the marker's rotation state is readable at a glance
        // — Y is always up-ish before you rotate the marker. Each axis is a separate
        // list so it can be colored individually. ──
        float aLen = MathF.Max(0.9f, r * 2.2f);
        var basis = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);
        var ax = Vector3.Transform(Vector3.UnitX, basis);
        var ay = Vector3.Transform(Vector3.UnitY, basis);
        var az = Vector3.Transform(Vector3.UnitZ, basis);
        var xVerts = new List<Vector3>(2) { Position, Position + ax * aLen };
        var yVerts = new List<Vector3>(2) { Position, Position + ay * aLen };
        var zVerts = new List<Vector3>(2) { Position, Position + az * aLen };

        // Draw order matters (depth test is off, so later draws overwrite earlier ones):
        // dim cone/range + sun + tripod first, then the bright beam LAST so the direction
        // axis always wins where its line crosses the cone.
        DrawEditorLines(coneVerts, camera, new Vector3(0.45f, 0.38f, 0.14f));
        DrawEditorLines(rangeVerts, camera, Color);
        DrawEditorLines(sunVerts, camera, Color);
        DrawEditorLines(xVerts, camera, new Vector3(0.85f, 0.25f, 0.2f));  // X = red
        DrawEditorLines(yVerts, camera, new Vector3(0.3f, 0.8f, 0.3f));    // Y = green
        DrawEditorLines(zVerts, camera, new Vector3(0.3f, 0.45f, 0.9f));   // Z = blue
        DrawEditorLines(beamVerts, camera, new Vector3(1.0f, 0.92f, 0.35f)); // beam on top
    }

    // ════════════════════════════════════════════════════════════════════
    //  Sky sun manipulation (gizmo drag handle)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>World-space sun direction this sky object currently renders — honors the
    /// SkySunPitch/SkySunYaw override when set, otherwise follows the time-of-day cycle.
    /// Shared by the gizmo drawing and the sun-handle drag math so they always agree.</summary>
    public Vector3 GetSkySunDirection()
    {
        if (SkySunPitch.HasValue && SkySunYaw.HasValue)
        {
            float p = SkySunPitch.Value * MathF.PI / 180f;
            float y = SkySunYaw.Value * MathF.PI / 180f;
            return Vector3.Normalize(new Vector3(
                MathF.Sin(y) * MathF.Cos(p), MathF.Sin(p), MathF.Cos(y) * MathF.Cos(p)));
        }
        float hours = Math.Clamp(SkyTimeOfDay, 0f, 24f);
        float sunAngle = (hours / 24f) * (MathF.PI * 2f) - (MathF.PI * 0.5f);
        return Vector3.Normalize(new Vector3(MathF.Cos(sunAngle), MathF.Sin(sunAngle), 0.3f));
    }

    /// <summary>World-space center of the sun handle drawn by the sky gizmo (null when this
    /// object is not a Sky). Matches DrawSkyGizmo's sun icon placement exactly.</summary>
    public Vector3? GetSkySunHandleCenter()
    {
        if (PrimitiveType != EditorPrimitiveType.Sky) return null;
        float horizonR = 3.5f * MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));
        return Position + GetSkySunDirection() * (horizonR * 0.85f);
    }

    /// <summary>World-space radius of the sun disc drawn by the sky gizmo. Shared by
    /// DrawSkyGizmo (rendering) and the viewport hit test (grabbing the handle) so they
    /// can never drift apart.</summary>
    public float SkySunDiscRadius =>
        0.35f + 0.25f * Math.Clamp(SkySunIntensity, 0.1f, 3f);

    /// <summary>Set the SkySunPitch/SkySunYaw override from a world-space direction (used by
    /// the sun-handle drag). When <paramref name="snap"/> is set, pitch snaps to 5° and yaw
    /// to 15° increments for precise alignment.</summary>
    public void SetSkySunFromDirection(Vector3 worldDir, bool snap = false)
    {
        if (worldDir.LengthSquared() < 1e-6f) return;
        var d = Vector3.Normalize(worldDir);
        float pitch = MathF.Asin(Math.Clamp(d.Y, -1f, 1f)) * 180f / MathF.PI;
        float yaw = MathF.Atan2(d.X, d.Z) * 180f / MathF.PI;
        if (yaw < 0f) yaw += 360f;
        if (snap)
        {
            pitch = MathF.Round(pitch / 5f) * 5f;
            yaw = MathF.Round(yaw / 15f) * 15f;
            if (yaw >= 360f) yaw -= 360f;
        }
        SkySunPitch = Math.Clamp(pitch, -90f, 90f);
        SkySunYaw = yaw % 360f;
    }

    /// <summary>Aim the sun at the given world ray (dragging the sun handle): intersect the
    /// sun orbit sphere around the marker; on a miss (viewed edge-on) fall back to the
    /// closest point on the ray to the sphere center so the drag never freezes.</summary>
    public bool AimSkySunFromRay(Vector3 rayOrigin, Vector3 rayDir, bool snap = false)
    {
        if (PrimitiveType != EditorPrimitiveType.Sky) return false;
        Vector3 center = Position;
        float horizonR = 3.5f * MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));
        float r = MathF.Max(0.5f, horizonR * 0.85f);

        var dir = Vector3.Normalize(rayDir);
        var oc = rayOrigin - center;
        float b = Vector3.Dot(oc, dir);
        float c = Vector3.Dot(oc, oc) - r * r;
        float disc = b * b - c;

        Vector3 toHit;
        if (disc >= 0f)
        {
            float t = -b - MathF.Sqrt(disc);
            if (t < 0f) t = -b + MathF.Sqrt(disc);
            if (t < 0f) return false;
            toHit = rayOrigin + dir * t - center;
        }
        else
        {
            // Ray misses the small orbit sphere — aim along the direction from the marker to
            // the closest point on the cursor ray. This keeps the drag ALIVE across the whole
            // sweep (previously a miss with the closest point behind the camera returned false,
            // freezing the sun at its last position mid-drag). At the sphere silhouette this
            // matches the tangent-hit direction, so there is no visible jump.
            float t = Vector3.Dot(center - rayOrigin, dir);
            toHit = rayOrigin + dir * t - center;
            if (toHit.LengthSquared() < 1e-6f)
                toHit = dir; // cursor exactly at the marker — fall back to the raw ray direction
        }

        SetSkySunFromDirection(toHit, snap);
        return true;
    }

    /// <summary>
    /// Draw a sky gizmo for this placed sky marker: a horizon circle in the XZ plane
    /// around the marker, a small vertical zenith axis, and a sun icon placed in the
    /// direction of the sun (driven by SkyTimeOfDay, or by the SkySunPitch/SkySunYaw
    /// override when set) whose radius/brightness scales with SkySunIntensity.
    /// </summary>
    public unsafe void DrawSkyGizmo(Camera camera)
    {
        if (!IsVisible || PrimitiveType != EditorPrimitiveType.Sky) return;

        // ── Sun direction: same math as SceneManager/editor lights ──
        Vector3 sunDir = GetSkySunDirection();

        var verts = new List<Vector3>(96);
        void Line(Vector3 a, Vector3 b) { verts.Add(a); verts.Add(b); }

        // ── Horizon circle (XZ plane, radius 3.5, center = marker) ──
        const int segs = 24;
        float horizonR = 3.5f * MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));
        var prev = new Vector3(Position.X + horizonR, Position.Y, Position.Z);
        for (int i = 1; i <= segs; i++)
        {
            float a = i * MathF.PI * 2f / segs;
            var cur = new Vector3(Position.X + MathF.Cos(a) * horizonR, Position.Y, Position.Z + MathF.Sin(a) * horizonR);
            Line(prev, cur);
            prev = cur;
        }

        // ── Zenith axis (straight up through the marker) ──
        float zAxis = horizonR * 0.6f;
        Line(Position, Position + Vector3.UnitY * zAxis);
        Line(Position + Vector3.UnitY * (zAxis - 0.25f), Position + Vector3.UnitY * zAxis + Vector3.UnitX * 0.15f);
        Line(Position + Vector3.UnitY * (zAxis - 0.25f), Position + Vector3.UnitY * zAxis - Vector3.UnitX * 0.15f);

        // ── Sun icon: small circle + rays placed along the sun direction, radius scaled
        //    by intensity (brighter/larger sun = more intense) ──
        float sunRadius = SkySunDiscRadius;
        Vector3 sunCenter = Position + sunDir * (horizonR * 0.85f);

        // Build an orthonormal basis around the sun direction for the circle/rays
        var upRef = MathF.Abs(sunDir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
        var sunRight = Vector3.Normalize(Vector3.Cross(sunDir, upRef));
        var sunUp = Vector3.Normalize(Vector3.Cross(sunRight, sunDir));

        const int sunSegs = 10;
        var ring = new Vector3[sunSegs];
        for (int i = 0; i < sunSegs; i++)
        {
            float a = i * MathF.PI * 2f / sunSegs;
            ring[i] = sunCenter + sunRight * (MathF.Cos(a) * sunRadius)
                                + sunUp * (MathF.Sin(a) * sunRadius);
        }
        for (int i = 0; i < sunSegs; i++)
            Line(ring[i], ring[(i + 1) % sunSegs]);

        // Rays around the sun (4 spokes, brighter color separately? — same color for now)
        float rayLen = sunRadius * 0.9f;
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.PI / 2f;
            var d = sunRight * MathF.Cos(a) + sunUp * MathF.Sin(a);
            Line(sunCenter + d * (sunRadius + 0.05f), sunCenter + d * (sunRadius + rayLen));
        }

        DrawEditorLines(verts, camera);
    }

    /// <summary>
    /// Draw a 2D billboard icon for Camera (camera glyph), Light (sun with rays) and
    /// Sky (sun + cloud) markers. The icon always faces the camera (built on the camera's
    /// right/up vectors), so it reads as a flat 2D badge in the viewport instead of a
    /// solid 3D box/sphere.
    /// </summary>
    public unsafe void Draw2DMarker(Camera camera)
    {
        if (!IsVisible || (PrimitiveType != EditorPrimitiveType.Camera
            && PrimitiveType != EditorPrimitiveType.Light && PrimitiveType != EditorPrimitiveType.Sky
            && PrimitiveType != EditorPrimitiveType.Player2D && PrimitiveType != EditorPrimitiveType.Start2D
            && PrimitiveType != EditorPrimitiveType.CameraStart2D)) return;

        // Derive a camera-facing basis from Front (Right/Up fields can be stale in fly
        // mode). Same upRef fallback as the light/sky gizmos so the icon always faces you.
        var front = Vector3.Normalize(camera.Front);
        var upRef = MathF.Abs(front.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
        var right = Vector3.Normalize(Vector3.Cross(front, upRef));
        var up = Vector3.Normalize(Vector3.Cross(right, front));
        float r = 0.45f * MathF.Max(Scale.X, MathF.Max(Scale.Y, Scale.Z));

        var verts = new List<Vector3>(96);
        void Line(Vector3 a, Vector3 b) { verts.Add(a); verts.Add(b); }
        Vector3 P(float u, float v) => Position + right * (u * r) + up * (v * r);

        if (PrimitiveType == EditorPrimitiveType.Light)
        {
            // Sun icon: circle + 8 rays
            const int segs = 16;
            var prev = P(MathF.Cos(0f), MathF.Sin(0f));
            for (int i = 1; i <= segs; i++)
            {
                float a = i * MathF.PI * 2f / segs;
                var cur = P(MathF.Cos(a), MathF.Sin(a));
                Line(prev, cur);
                prev = cur;
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i * MathF.PI / 4f;
                float c = MathF.Cos(a), s = MathF.Sin(a);
                Line(P(c * 0.8f, s * 0.8f), P(c * 1.25f, s * 1.25f));
            }
        }
        else if (PrimitiveType == EditorPrimitiveType.Player2D)
        {
            // Player icon: capsule outline (body) — matches the collider shape.
            // The capsule is drawn world-upright (not billboarded) so its orientation
            // matches the physics capsule exactly.
            DrawPlayer2DCapsule(camera, new Vector3(0.2f, 0.95f, 1f), 0.95f);
        }
        else if (PrimitiveType == EditorPrimitiveType.Start2D)
        {
            // Start icon: downward arrow into a ground line ("player spawns here").
            Line(P(-0.7f, -0.5f), P(0.7f, -0.5f));   // ground
            Line(P(0f, 0.8f), P(0f, 0.15f));         // arrow shaft
            Line(P(-0.35f, 0.3f), P(0f, 0.15f));     // arrow head left
            Line(P(0.35f, 0.3f), P(0f, 0.15f));      // arrow head right
            Line(P(-0.35f, -0.35f), P(-0.35f, -0.5f)); // spawn bracket left
            Line(P(0.35f, -0.35f), P(0.35f, -0.5f));   // spawn bracket right
        }
        else if (PrimitiveType == EditorPrimitiveType.CameraStart2D)
        {
            // Camera start icon: eye glyph (lens circle + pupil + view rays).
            const int eyeSegs = 16;
            var eprev = P(MathF.Cos(0f), MathF.Sin(0f));
            for (int i = 1; i <= eyeSegs; i++)
            {
                float a = i * MathF.PI * 2f / eyeSegs;
                var ecur = P(MathF.Cos(a), MathF.Sin(a));
                Line(eprev, ecur);
                eprev = ecur;
            }
            const int pupilSegs = 10;
            var pprev = P(0.3f * MathF.Cos(0f), 0.3f * MathF.Sin(0f));
            for (int i = 1; i <= pupilSegs; i++)
            {
                float a = i * MathF.PI * 2f / pupilSegs;
                var pcur = P(0.3f * MathF.Cos(a), 0.3f * MathF.Sin(a));
                Line(pprev, pcur);
                pprev = pcur;
            }
            // View rays: left + right
            Line(P(-1.3f, 0f), P(-0.85f, 0f));
            Line(P(0.85f, 0f), P(1.3f, 0f));
            // Ground dashes under the icon
            Line(P(-0.6f, -1.1f), P(-0.2f, -1.1f));
            Line(P(0.2f, -1.1f), P(0.6f, -1.1f));
        }
        else if (PrimitiveType == EditorPrimitiveType.Camera)
        {
            // Camera glyph: body rectangle + top viewfinder bump + lens circle.
            // The lens sits toward +right of the icon, echoing the frustum's forward
            // direction (which points down -Z in world space before any rotation).
            Line(P(-0.85f, -0.55f), P(0.85f, -0.55f)); // bottom
            Line(P(0.85f, -0.55f), P(0.85f, 0.35f));   // right
            Line(P(0.85f, 0.35f), P(-0.85f, 0.35f));   // top
            Line(P(-0.85f, 0.35f), P(-0.85f, -0.55f)); // left

            // Viewfinder bump on top
            Line(P(-0.45f, 0.35f), P(-0.45f, 0.6f));
            Line(P(-0.45f, 0.6f), P(0.45f, 0.6f));
            Line(P(0.45f, 0.6f), P(0.45f, 0.35f));

            // Lens circle (centered on the body)
            const int lensSegs = 12;
            const float lcx = 0.28f, lcy = -0.1f, lr = 0.28f;
            var lprev = P(lcx + lr, lcy);
            for (int i = 1; i <= lensSegs; i++)
            {
                float a = i * MathF.PI * 2f / lensSegs;
                var lcur = P(lcx + MathF.Cos(a) * lr, lcy + MathF.Sin(a) * lr);
                Line(lprev, lcur);
                lprev = lcur;
            }
        }
        else
        {
            // Sky icon: small sun circle + cloud arcs
            const int segs = 12;
            var prev = P(MathF.Cos(0f), MathF.Sin(0f));
            for (int i = 1; i <= segs; i++)
            {
                float a = i * MathF.PI * 2f / segs;
                var cur = P(MathF.Cos(a), MathF.Sin(a));
                Line(prev, cur);
                prev = cur;
            }
            // Cloud: three overlapping arcs on the lower half
            for (int arc = 0; arc < 3; arc++)
            {
                float cx = -0.45f + arc * 0.45f;
                float cy = -0.15f;
                Vector3? prevCloud = null;
                for (int i = 0; i <= 4; i++)
                {
                    float a = MathF.PI + i * MathF.PI / 4f; // lower half arc
                    var p = P(cx + MathF.Cos(a) * 0.35f, cy + MathF.Sin(a) * 0.35f);
                    if (prevCloud.HasValue) Line(prevCloud.Value, p);
                    prevCloud = p;
                }
            }
        }

        DrawEditorLines(verts, camera);
    }

    /// <summary>Draw the Player2D capsule outline world-upright (NOT billboarded) so it
    /// exactly matches the physics capsule: two semicircle caps + two side lines.
    /// The capsule is FEET-ANCHORED: Position.Y is the capsule bottom (same anchor as
    /// the sprite quad), matching Player2DSystem's AABB. Spawn places the feet.</summary>
    private void DrawPlayer2DCapsule(Camera camera, Vector3 lineColor, float alpha)
    {
        float r = Player2DCapsuleRadius;
        float H = Player2DCapsuleHeight;
        // Capsule offset: shifts the whole capsule relative to the object position so
        // the collider can hug the visible character (left-bottom anchor ⇒ the body
        // usually sits right of Position.X). Same offset is applied by Player2DSystem
        // physics — the gizmo always matches the real collision body.
        float capX = Position.X + Player2DCapsuleOffsetX;
        float baseY = Position.Y + Player2DCapsuleOffsetY;
        // Cap centers: bottom cap at baseY + r, top cap at baseY + H - r. The straight
        // cylinder wall spans between the two centers; the semicircle caps bulge OUTWARD
        // (down at the feet, up at the head). Clamp for the degenerate H < 2r pill.
        float cBot = baseY + r;
        float cTop = baseY + MathF.Max(H - r, r);

        var verts = new List<Vector3>(64);
        const int segs = 12;
        // Top cap: 180° bulging UP (sin 0..PI is positive).
        var prev = new Vector3(capX + r, cTop, Position.Z);
        for (int i = 1; i <= segs; i++)
        {
            float a = i * MathF.PI / segs; // 0..PI (right → left over the top)
            var cur = new Vector3(capX + MathF.Cos(a) * r, cTop + MathF.Sin(a) * r, Position.Z);
            verts.Add(prev); verts.Add(cur); prev = cur;
        }
        // Bottom cap: 180° bulging DOWN (sin PI..2PI is negative).
        prev = new Vector3(capX - r, cBot, Position.Z);
        for (int i = 1; i <= segs; i++)
        {
            float a = MathF.PI + i * MathF.PI / segs; // PI..2PI (left → right under the bottom)
            var cur = new Vector3(capX + MathF.Cos(a) * r, cBot + MathF.Sin(a) * r, Position.Z);
            verts.Add(prev); verts.Add(cur); prev = cur;
        }
        // Cylinder side lines connect the cap centers at x = ±r.
        verts.Add(new Vector3(capX - r, cBot, Position.Z));
        verts.Add(new Vector3(capX - r, cTop, Position.Z));
        verts.Add(new Vector3(capX + r, cBot, Position.Z));
        verts.Add(new Vector3(capX + r, cTop, Position.Z));

        bool depth = GL.IsEnabled(Const.GL_DEPTH_TEST);
        GL.Disable(Const.GL_DEPTH_TEST);
        Terrains.TerrainChunk.DrawLineSegments(verts, lineColor, camera, alpha);
        if (depth) GL.Enable(Const.GL_DEPTH_TEST);
    }

    // ── Player2D sprite rendering (animated sheet frame on an upright quad) ──
    private uint _player2dVAO, _player2dVBO;
    /// <summary>Name of the clip DrawPlayer2D played last frame — used to detect idle ↔
    /// walk switches and restart the animation clock from frame 0 (transient).</summary>
    private string? _player2dLastClip;

    /// <summary>Look up the Sprite Editor's sheet+clip by name via the IDEBridge static
    /// registry (set every frame by SpriteEditorPanel.SyncToBridge). Returns false when
    /// the sheet/clip no longer exists.</summary>
    public bool TryGetPlayer2DClip(out SpriteSheet? sheet, out AnimationClip2D? clip)
    {
        sheet = null; clip = null;
        return IDEBridge.TryGetSpriteClip(Player2DSpriteSheet, Player2DAnimationClip, out sheet, out clip) && sheet != null && clip != null;
    }

    /// <summary>Which locomotion action the player's CURRENT PHYSICS STATE wants
    /// (Idle/Walk/Run/Jump/Jump Start/Jump End/Fall). Shared by ResolveLocomotion
    /// Action and the non-loop release check in DrawPlayer2D.</summary>
    private string ComputeLocomotionDesired()
    {
        bool moving = MathF.Abs(Player2DVelocityX) > 0.1f;
        bool grounded = Player2DGrounded;
        if (!grounded && Player2DVelocityY < -0.5f)
        {
            // Descending: prefer the split "Jump End" action when it has its own
            // clip; otherwise fall back to the single "Jump"/"Fall" actions.
            bool hasJumpEnd = HasActionOwnClip("Jump End");
            return hasJumpEnd ? "Jump End"
                : HasActionOwnClip("Fall") ? "Fall"
                : "Jump";
        }
        if (!grounded)
        {
            // Rising: prefer the split "Jump Start" action when it has its own clip;
            // its non-loop playback holds the LAST frame while still rising.
            bool hasJumpStart = HasActionOwnClip("Jump Start");
            return hasJumpStart ? "Jump Start"
                : HasActionOwnClip("Jump") ? "Jump"
                : "Fall";
        }
        if (moving) return Player2DVelocityX > 0 ? "Run" : "Walk";
        return "Idle";
    }

    /// <summary>True when the named action exists AND resolves to its own clip (not a
    /// fallback to the player's base clip). Used to decide between the split Jump
    /// Start/Jump End actions and the single Jump/Fall actions.</summary>
    public bool HasActionOwnClip(string name)
    {
        var act = Actions.FirstOrDefault(a => a.Name == name);
        if (act == null) return false;
        string sheetName = string.IsNullOrEmpty(act.SpriteSheet) ? Player2DSpriteSheet : act.SpriteSheet;
        string clipName = string.IsNullOrEmpty(act.Clip) ? Player2DAnimationClip : act.Clip;
        if (string.IsNullOrEmpty(clipName)) return false;
        // "Own clip" = the clip name differs from the player's base clip (a Jump Start
        // action still pointing at the idle clip is not a real jump-start animation).
        return !string.IsNullOrEmpty(act.Clip) && act.Clip != Player2DAnimationClip
            && IDEBridge.TryGetSpriteClip(sheetName, clipName, out _, out var clip) && clip != null;
    }

    /// <summary>Resolve the base (idle) clip to sample when no action is active.
    /// State animations (Walk/Run/Jump/custom) override it through the Animation Actions
    /// system (GetActiveActionClip).</summary>
    private bool TryGetPlayer2DActiveClip(out SpriteSheet? sheet, out AnimationClip2D? clip)
    {
        return TryGetPlayer2DClip(out sheet, out clip);
    }

    /// <summary>Auto-select the locomotion action that matches the current state (idle when
    /// grounded+still, walk/run by velocity, jump/fall by vertical state). Called every
    /// frame so the character switches action smoothly without user input. Actions that
    /// don't bind a key are locomotion candidates; user-bound actions (Attack/J/...)
    /// are only started by their key or by priority-gated events (Jump on takeoff).
    /// 
    /// When a key-bound action is active and its key is released, the action is cleared so
    /// locomotion can take over (e.g. Run bound to J: hold J = Run, release J = Idle/Walk).
    /// Pass <paramref name="keyStillHeld"/> = true when the bound key is currently down.
    /// Returns true when the action changed this frame.</summary>
    public bool ResolveLocomotionAction(bool keyStillHeld)
    {
        // If a key-bound action is currently active, check whether its key is still
        // held. If released (and the action is loopable), let locomotion take over.
        // Non-loop key-bound actions release on their own via the action-clock check in
        // DrawPlayer2D; here we only handle the loop case (e.g. Run bound to J while held).
        if (!string.IsNullOrEmpty(Player2DCurrentAction))
        {
            var cur = Actions.FirstOrDefault(a => a.Name == Player2DCurrentAction);
            if (cur != null && !string.IsNullOrEmpty(cur.KeyBinding) && cur.KeyBinding != "None")
            {
                if (!keyStillHeld && cur.Loop)
                {
                    // Key released while a looping key-bound action was active — fall back
                    // to locomotion. Clears the action; the next frame's locomotion switch
                    // will pick Idle/Walk/Run based on state.
                    Player2DCurrentAction = "";
                    Player2DActionTime = 0f;
                }
                return false; // don't override a key-bound action (held or not)
            }
        }

        string desired = ComputeLocomotionDesired();

        if (desired == Player2DCurrentAction) return false;
        var act = Actions.FirstOrDefault(a => a.Name == desired);
        if (act == null) return false;

        // Auto-fill the action's clip from the player's current clip when the sheet
        // matches (designer-friendly: one sheet, one clip name per action).
        if (string.IsNullOrEmpty(act.SpriteSheet) || act.SpriteSheet == Player2DSpriteSheet)
        {
            if (string.IsNullOrEmpty(act.SpriteSheet))
                act.SpriteSheet = Player2DSpriteSheet;
            if (string.IsNullOrEmpty(act.Clip))
                act.Clip = Player2DAnimationClip;
        }

        // When switching between actions that share the same clip (e.g. Idle↔Walk↔Run
        // all using the player's base clip), keep the action clock running so the frame
        // doesn't snap back to 0 — that snap is the "blink" on transition. Only reset the
        // clock when the clip actually changes (different action clip) or when entering a
        // new non-locomotion action (Jump/Fall/custom).
        bool sameClip = act.Clip == Player2DAnimationClip || (string.IsNullOrEmpty(act.Clip) && string.IsNullOrEmpty(Player2DAnimationClip));
        Player2DCurrentAction = desired;
        if (!sameClip)
            Player2DActionTime = 0f;
        return true;
    }

    /// <summary>Advance the animation clock and draw the player as an upright textured
    /// quad showing the current clip frame. Works in edit mode (preview) and in-game.
    /// Called from Draw() after the Map2D branch so the player draws over the level.</summary>
    public unsafe void DrawPlayer2D(Camera camera)
    {
        if (!IsVisible || PrimitiveType != EditorPrimitiveType.Player2D) return;
        if (!TryGetPlayer2DActiveClip(out var sheet, out var clip) || sheet == null || clip == null) return;

        // ── Animation action system: a bound action with an own clip overrides the base clip. ──
        // DrawPlayer2D still owns the clock (single source of truth) — but when an action is
        // playing we keep its clock separate (Player2DActionTime) so the base idle clock
        // doesn't skip while actions fire.
        // Auto-switch locomotion action (idle/walk/run/jump/fall) to match current state;
        // pick up before resolving the clip so the action's auto-filled clip is used.
        // In edit mode we can check the key directly.
        bool keyHeld = false;
        if (!string.IsNullOrEmpty(Player2DCurrentAction))
        {
            var cur = Actions.FirstOrDefault(a => a.Name == Player2DCurrentAction);
            if (cur != null && !string.IsNullOrEmpty(cur.KeyBinding) && cur.KeyBinding != "None")
            {
                if (Enum.TryParse<ImGuiKey>(cur.KeyBinding, out var k) && k != ImGuiKey.None)
                    keyHeld = ImGui.IsKeyDown(k);
            }
        }
        ResolveLocomotionAction(keyHeld);

        var actionClip = GetActiveActionClip(out var activeAction, out var actionSheet);
        bool actionActive = actionClip != null;
        if (actionClip != null)
        {
            // Advance the ACTION clock while an action plays. The frame below is
            // ALWAYS computed from Player2DActionTime — advancing any other clock
            // here desyncs the two and the frame freezes on index 0 (the idle bug).
            // Continuity across Idle↔Walk↔Run is preserved because ResolveLocomotion
            // Action only resets Player2DActionTime when the clip actually changes
            // (same-clip switches keep the clock running — the "no blink" fix).
            Player2DActionTime += Glfw.GetDeltaTime();
            // Non-looping actions release when finished — BUT only when the physics
            // state no longer wants this action. Locomotion-driven non-loop actions
            // (Jump Start/Jump End) must HOLD their last frame: releasing them while
            // the state still matches makes the resolver re-pick the same action next
            // frame, the clock resets, and the clip replays — a fake loop even with
            // Loop = false. The frame index below already clamps past the end.
            if (activeAction != null && !activeAction.Loop && Player2DActionTime >= actionClip.Duration)
            {
                if (ComputeLocomotionDesired() != activeAction.Name)
                    Player2DCurrentAction = "";
            }
        }

        // Resolve the sheet image + grid. The bridge's registry carries a live texture.
        // When an ACTION is active, sample the ACTION's sheet texture — walk/run clips
        // usually live on a different sheet than idle, and sampling run frame indices
        // against the idle sheet's texture renders the wrong (or no) animation.
        var drawSheet = actionClip != null && actionSheet != null ? actionSheet : sheet;
        if (!IDEBridge.TryGetSpriteSheetTexture(drawSheet.Name, out uint texId, out int imgW, out int imgH))
            return;
        if (texId == 0) return;

        // Advance the animation clock HERE (once per frame per player) — this is the
        // single source of truth so the sprite animates in edit mode too. The clock
        // RESETS when the active clip changes (idle ↔ walk) so each state starts on
        // its first frame instead of resuming mid-cycle. While an action is active,
        // its own clock (Player2DActionTime) advances instead of the base clock.
        if (!actionActive)
        {
            string activeClipName = sheet.Name + "/" + clip.Name;
            if (!string.Equals(_player2dLastClip, activeClipName, StringComparison.Ordinal))
            {
                _player2dLastClip = activeClipName;
                Player2DAnimTime = 0f;
            }
            Player2DAnimTime += Glfw.GetDeltaTime();
        }

        // Frame index: from the action clock (honoring the ACTION's loop flag without
        // mutating the shared clip) or the base locomotion clock.
        int frameIdx;
        if (actionClip != null)
        {
            float frameDur = 1f / MathF.Max(0.01f, actionClip.FPS * actionClip.SpeedMultiplier);
            int f = (int)(Player2DActionTime / frameDur);
            int count = actionClip.FrameIndices.Count;
            if (activeAction!.Loop && count > 0)
                f = ((f % count) + count) % count;
            else
                f = Math.Clamp(f, 0, Math.Max(0, count - 1));
            frameIdx = count > 0 ? actionClip.FrameIndices[f] : 0;
        }
        else
        {
            frameIdx = clip.GetSpriteFrameAtTime(Player2DAnimTime);
        }
        var (uvMinRaw, uvMaxRaw) = drawSheet.GetFrameUV(frameIdx);
        // GetFrameUV assumes a flipped upload (v=0=image bottom), but textures upload
        // top-row-first (v=0=image TOP). Convert: v' = 1 - v_raw. Raw uvMin.Y is the
        // frame BOTTOM (small raw v = lower in the flipped convention) → after the
        // 1-v conversion it becomes the LARGER GL v, so:
        //   frame bottom → svBot (mapped to the quad's bottom vertex)
        //   frame top    → svTop (mapped to the quad's top vertex)
        // Crossing these two makes the sprite render upside down.
        float su0 = uvMinRaw.X, su1 = uvMaxRaw.X;
        float svBot = 1f - uvMinRaw.Y;
        float svTop = 1f - uvMaxRaw.Y;

        // Facing: sprite art is assumed right-facing. Facing left → swap U so the frame
        // mirrors horizontally (per-object runtime state, never saved to disk).
        // This is the ONLY mirror — do not swap U again at the vertex build below,
        // a second swap cancels this one and the sprite would never face left.
        if (!Player2DFacingRight)
            (su0, su1) = (su1, su0);

        // ── Sizing: SAVED-CLIP snapshot only — sheet values never reach the viewport ──
        // The Render Normalization panel (Master W/H + Offset) is a SPRITE EDITOR tool:
        // it exists there purely to align frames while authoring. Once the clip is
        // saved, the clip carries its own snapshot (master W/H + base offsets) and the
        // viewport renders from that: pxToWorld = SpriteHeight / masterH, the frame
        // drawn AS-IS (native px), centered on Position.X with its bottom on
        // Position.Y, nudged by the clip's base offsets + this frame's own offsets.
        // A clip with no snapshot (never saved since this feature) renders native size.
        float cellH = drawSheet.FrameHeight > 0 ? drawSheet.FrameHeight : drawSheet.ImageHeight;
        if (cellH <= 0) cellH = 64;
        float cellW = drawSheet.FrameWidth > 0 ? drawSheet.FrameWidth : cellH;
        SpriteFrame? drawFrame = null;
        if (drawSheet.CustomFrames != null && frameIdx < drawSheet.CustomFrames.Count)
        {
            drawFrame = drawSheet.CustomFrames[frameIdx];
            cellW = drawFrame.Width;
            cellH = drawFrame.Height;
            if (cellH <= 0) cellH = 1;
        }
        // ONLY the playing clip's snapshot — no sheet fallback in the viewport.
        AnimationClip2D? sizingClip = actionClip ?? clip;
        float snapH = sizingClip?.MasterHeight ?? 0f;
        bool normalized = snapH > 0f;
        float pxToWorld;
        if (normalized)
            pxToWorld = Player2DHeight / snapH;
        else
            pxToWorld = Player2DHeight / cellH; // unsaved clip → native proportions
        float w = MathF.Max(0.05f, cellW * pxToWorld);   // frame as-is (native px)
        float h = MathF.Max(0.05f, cellH * pxToWorld);
        float mirror = Player2DFacingRight ? 1f : -1f;
        // Base offsets come from the clip snapshot only; sheet offsets don't apply here.
        float offX = ((sizingClip?.SpriteOffsetX ?? 0f) + (drawFrame?.RenderOffsetX ?? 0f)) * pxToWorld * mirror;
        float offY = ((sizingClip?.SpriteOffsetY ?? 0f) + (drawFrame?.RenderOffsetY ?? 0f)) * pxToWorld;
        // Render AS-IS: centered on Position.X, bottom on Position.Y. The capsule
        // (offset 0) centers on the same axis — sprite and collider align.
        float x0 = Position.X - w * 0.5f + offX;
        float x1 = x0 + w;
        float y0 = Position.Y + offY;
        float y1 = y0 + h;
        float z = Position.Z + 0.05f;

        EnsureMap2DShader();
        if (_map2dShader == 0) return;

        GL.UseProgram(_map2dShader);
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix();
        GL.UniformMatrix4fv(_map2dLocView, 1, false, &view.M11);
        GL.UniformMatrix4fv(_map2dLocProj, 1, false, &proj.M11);
        var identity = Matrix4x4.Identity;
        GL.UniformMatrix4fv(_map2dLocModel, 1, false, &identity.M11);

        GL.ActiveTexture(Const.GL_TEXTURE0);
        GL.BindTexture(Const.GL_TEXTURE_2D, texId);
        GL.Uniform1i(_map2dLocTex, 0);
        GL.Enable(Const.GL_BLEND);
        GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);
        bool cull = GL.IsEnabled(Const.GL_CULL_FACE);
        GL.Disable(Const.GL_CULL_FACE);
        bool depth = GL.IsEnabled(Const.GL_DEPTH_TEST);

        // Two triangles in WORLD space (identity model): pos(3) uv(2) tint(4).
        // The horizontal mirror was already applied to su0/su1 above (single flip
        // via Player2DFacingRight) — use them directly. Swapping again here would
        // cancel the first flip (sprite stuck facing right when moving left).
        float uL = su0;
        float uR = su1;
        float tR = Color.X, tG = Color.Y, tB = Color.Z, tA = 1f;
        var verts = stackalloc Map2DVertex[6]
        {
            new(x0, y0, z, uL, svBot, tR, tG, tB, tA),
            new(x1, y0, z, uR, svBot, tR, tG, tB, tA),
            new(x1, y1, z, uR, svTop, tR, tG, tB, tA),
            new(x0, y0, z, uL, svBot, tR, tG, tB, tA),
            new(x1, y1, z, uR, svTop, tR, tG, tB, tA),
            new(x0, y1, z, uL, svTop, tR, tG, tB, tA),
        };

        if (_player2dVAO == 0)
        {
            uint vao = 0, vbo = 0;
            GL.GenVertexArrays(1, &vao);
            GL.BindVertexArray(vao);
            GL.GenBuffers(1, &vbo);
            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, sizeof(Map2DVertex), (void*)0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, sizeof(Map2DVertex), (void*)(3 * sizeof(float)));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 4, Const.GL_FLOAT, false, sizeof(Map2DVertex), (void*)(5 * sizeof(float)));
            GL.BindVertexArray(0);
            _player2dVAO = vao; _player2dVBO = vbo;
        }

        GL.BindVertexArray(_player2dVAO);
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, _player2dVBO);
        GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(6 * sizeof(Map2DVertex)), verts, Const.GL_DYNAMIC_DRAW);
        GL.DrawArrays(Const.GL_TRIANGLES, 0, 6);
        GL.BindVertexArray(0);

        if (depth) GL.Enable(Const.GL_DEPTH_TEST);
        if (cull) GL.Enable(Const.GL_CULL_FACE);
        GL.Disable(Const.GL_BLEND);
        GL.BindTexture(Const.GL_TEXTURE_2D, 0);

        // Collider guide in edit mode (hidden in-game via Editor2DAidsHidden).
        // No master-box outline here: the saved clip's sizing IS the render — the
        // master/reference box stays a Sprite Editor-only visualization.
        if (Player2DShowCapsule && !Editor2DAidsHidden)
        {
            DrawPlayer2DCapsule(camera, new Vector3(0.2f, 0.95f, 1f), 0.9f);
        }

        // Restore main shader.
        GL.UseProgram(Shader.GetShaderProgram());
    }

    /// <summary>Resolve the animation clip for the currently-playing action (priority
    /// system), or null when the base locomotion clip should play. The matched action is
    /// returned via <paramref name="action"/> (for its Loop flag — the shared clip object
    /// is never mutated).
    /// 
    /// Resolution order: (1) action's own sheet+clip if resolvable; (2) action's clip name
    /// looked up in the PLAYER's sheet (so Walk/Run can share the player's clip even when
    /// their Sheet field points elsewhere); (3) fall back to the player's base clip.</summary>
    private AnimationClip2D? GetActiveActionClip(out Player2DAction? action, out SpriteSheet? actionSheet)
    {
        action = null;
        actionSheet = null;
        if (string.IsNullOrEmpty(Player2DCurrentAction) || Actions.Count == 0)
            return null;
        var act = Actions.FirstOrDefault(a => a.Name == Player2DCurrentAction);
        if (act == null) { Player2DCurrentAction = ""; return null; }

        string playerSheet = Player2DSpriteSheet;
        string playerClip = Player2DAnimationClip;

        // (1) Action's own sheet + clip. The owning sheet is returned too — the caller
        //     must sample THIS sheet's texture (walk/run often live on a different
        //     sheet than idle; sampling run frames against the idle texture breaks).
        string sheetName = string.IsNullOrEmpty(act.SpriteSheet) ? playerSheet : act.SpriteSheet;
        string clipName = string.IsNullOrEmpty(act.Clip) ? playerClip : act.Clip;
        if (IDEBridge.TryGetSpriteClip(sheetName, clipName, out var sheet, out var clip) && clip != null)
        {
            action = act;
            actionSheet = sheet;
            return clip;
        }

        // (2) Action's clip name in the PLAYER's sheet — lets Walk/Run share the player's
        //     clip even when their Sheet field points at a different (possibly missing) sheet.
        if (!string.IsNullOrEmpty(act.Clip) && act.Clip != playerClip
            && IDEBridge.TryGetSpriteClip(playerSheet, act.Clip, out sheet, out clip) && clip != null)
        {
            action = act;
            actionSheet = sheet;
            return clip;
        }

        // (3) Player's base clip as last resort. When the action's own clip can't be
        //     resolved at all (missing sheet or clip name), fall back to the player's
        //     current clip so the action still animates.
        if (IDEBridge.TryGetSpriteClip(playerSheet, playerClip, out sheet, out clip) && clip != null)
        {
            action = act;
            actionSheet = sheet;
            return clip;
        }

        return null;
    }

    /// <summary>Trigger an action by name if its priority allows (respects the
    /// interrupt rules: Dead=100 cancels all; equal/lower priority is ignored).</summary>
    public bool TryStartAction(string name)
    {
        var act = Actions.FirstOrDefault(a => a.Name == name);
        if (act == null) return false;
        if (!string.IsNullOrEmpty(Player2DCurrentAction))
        {
            var cur = Actions.FirstOrDefault(a => a.Name == Player2DCurrentAction);
            if (cur != null && act.Priority < cur.Priority)
                return false; // a higher-priority action is playing
        }
        if (Player2DCurrentAction != name)
        {
            Player2DCurrentAction = name;
            Player2DActionTime = 0f;
        }
        return true;
    }

    /// <summary>
    /// Draw the object's mesh as a wireframe line outline in the given color.
    /// Walks the triangle edges from the cached vertex data and draws them as
    /// line segments using the line shader.
    /// Deduplicates shared edges by their world-space position to reduce line vertices.
    /// Capped at 100k triangles for safety.
    /// </summary>
    public void DrawWireframe(Camera camera, Vector3 lineColor)
    {
        if (!IsVisible) return;

        // ── GLB reference: walk the model's CPU triangle edges (capped at 100k). ──
        if (PrimitiveType == EditorPrimitiveType.GlbReference)
        {
            EnsureGlb();
            if (_glbObject == null) return;
            SyncGlbTransform();
            _glbObject.DrawWireframe(camera, lineColor);
            return;
        }

        if (_vertexCache == null || _vertexCache.Length < 3) return;

        var worldMatrix = WorldMatrix;
        int triCount = _vertexCache.Length / 3;

        // Cap wireframe triangle count for safety
        const int maxTri = 100_000;
        if (triCount > maxTri)
        {
            Console.WriteLine($"[EditorObject] Wireframe skipped: {triCount} triangles exceeds max {maxTri}");
            return;
        }

        // Pack a Vector3 into a long with 2 decimal precision (±1000 range)
        long PackPos(Vector3 v)
        {
            long x = ((long)(v.X * 100 + 100000) & 0x3FFFF);
            long y = ((long)(v.Y * 100 + 100000) & 0x3FFFF);
            long z = ((long)(v.Z * 100 + 100000) & 0x3FFFF);
            return (x << 40) | (y << 20) | z;
        }

        var lineVerts = new List<Vector3>(triCount * 3); // ~50% reduction vs 6 per tri
        var edgeSet = new HashSet<(long, long)>(triCount * 3 / 2);

        for (int t = 0; t < triCount; t++)
        {
            int i = t * 3;
            var p0 = Vector3.Transform(_vertexCache[i].Position, worldMatrix);
            var p1 = Vector3.Transform(_vertexCache[i + 1].Position, worldMatrix);
            var p2 = Vector3.Transform(_vertexCache[i + 2].Position, worldMatrix);

            long h0 = PackPos(p0), h1 = PackPos(p1), h2 = PackPos(p2);

            // Edge (p0, p1) — sorted key so both directions hash the same
            var e1 = h0 < h1 ? (h0, h1) : (h1, h0);
            if (edgeSet.Add(e1)) { lineVerts.Add(p0); lineVerts.Add(p1); }

            // Edge (p1, p2)
            var e2 = h1 < h2 ? (h1, h2) : (h2, h1);
            if (edgeSet.Add(e2)) { lineVerts.Add(p1); lineVerts.Add(p2); }

            // Edge (p2, p0)
            var e3 = h2 < h0 ? (h2, h0) : (h0, h2);
            if (edgeSet.Add(e3)) { lineVerts.Add(p2); lineVerts.Add(p0); }
        }

        if (lineVerts.Count == 0) return;

        GL.Disable(Const.GL_DEPTH_TEST);
        Terrains.TerrainChunk.DrawLineSegments(lineVerts, lineColor, camera);
        GL.Enable(Const.GL_DEPTH_TEST);
    }

    /// <summary>
    /// First pass of the inverted-hull outline technique.
    /// Renders the object to the stencil buffer only (no color output)
    /// with depth test enabled. Stencil is set to 1 where depth passes.
    /// Face culling is intentionally disabled so both front and back faces
    /// write to the stencil (needed for a complete silhouette outline).
    /// </summary>
    public void DrawOutlineStencil(Camera camera)
    {
        if (!IsVisible) return;

        // Player2D/Start2D/CameraStart2D have no solid mesh — selection shows via their line gizmos.
        if (PrimitiveType == EditorPrimitiveType.Player2D || PrimitiveType == EditorPrimitiveType.Start2D
            || PrimitiveType == EditorPrimitiveType.CameraStart2D)
            return;

        // ── GLB reference: draw the model's own meshes into the stencil mask. ──
        if (PrimitiveType == EditorPrimitiveType.GlbReference)
        {
            EnsureGlb();
            if (_glbObject == null) return;
            SyncGlbTransform();
            _glbObject.DrawOutlineStencil(camera);
            return;
        }

        if (_object3D == null) return;

        uint prog = Shader.GetOutlineShaderProgram();
        if (prog == 0) return;

        GL.UseProgram(prog);

        var model = WorldMatrix;
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix();

        int modelLoc = GL.GetUniformLocation(prog, "model");
        int viewLoc = GL.GetUniformLocation(prog, "view");
        int projLoc = GL.GetUniformLocation(prog, "projection");

        GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&model);
        GL.UniformMatrix4fv(viewLoc, 1, false, (float*)&view);
        GL.UniformMatrix4fv(projLoc, 1, false, (float*)&proj);

        // Disable face culling so all triangles write to stencil
        // (both front and back faces needed for a complete silhouette)
        OpenGL.EnableFaceCulling(false);

        GL.BindVertexArray(_object3D.VAO);
        GL.DrawArrays(Const.GL_TRIANGLES, 0, _object3D.VertexCount);
        GL.BindVertexArray(0);

        GL.UseProgram(0);
    }

    /// <summary>
    /// Second pass of the inverted-hull outline technique.
    /// Renders the object slightly scaled up (centered on its position)
    /// with face culling disabled, letting the stencil test (NOTEQUAL, 1)
    /// block the original object area so only the expanded border shows.
    /// Should be called after DrawOutlineStencil() with stencil test enabled.
    /// FIXED: Uses Scale * outlineScale directly in the world matrix
    /// instead of scaleAboutPos to keep the outline centered on the object.
    /// </summary>
    public void DrawOutline(Camera camera, Vector3 outlineColor, float outlineScale = 1.05f)
    {
        if (!IsVisible) return;

        // Player2D/Start2D/CameraStart2D have no solid mesh — selection shows via their line gizmos.
        if (PrimitiveType == EditorPrimitiveType.Player2D || PrimitiveType == EditorPrimitiveType.Start2D
            || PrimitiveType == EditorPrimitiveType.CameraStart2D)
            return;

        // ── GLB reference: inverted-hull outline over the model's meshes. ──
        if (PrimitiveType == EditorPrimitiveType.GlbReference)
        {
            EnsureGlb();
            if (_glbObject == null) return;
            SyncGlbTransform();
            _glbObject.DrawOutline(camera, outlineColor, outlineScale);
            return;
        }

        if (_object3D == null) return;

        uint prog = Shader.GetOutlineShaderProgram();
        if (prog == 0) return;

        GL.UseProgram(prog);

        // Use Scale * outlineScale directly so the outline stays centered
        // on the object's Position (unlike the old scaleAboutPos approach
        // which caused a positional drift proportional to Position * 0.05).
        float rotY = RotationEuler.Y * MathF.PI / 180f;
        float rotX = RotationEuler.X * MathF.PI / 180f;
        float rotZ = RotationEuler.Z * MathF.PI / 180f;
        var model = Matrix4x4.CreateScale(Scale * outlineScale)
                  * Matrix4x4.CreateFromYawPitchRoll(rotY, rotX, rotZ)
                  * Matrix4x4.CreateTranslation(Position);
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix();

        int modelLoc = GL.GetUniformLocation(prog, "model");
        int viewLoc = GL.GetUniformLocation(prog, "view");
        int projLoc = GL.GetUniformLocation(prog, "projection");
        int colorLoc = GL.GetUniformLocation(prog, "outlineColor");

        GL.UniformMatrix4fv(modelLoc, 1, false, (float*)&model);
        GL.UniformMatrix4fv(viewLoc, 1, false, (float*)&view);
        GL.UniformMatrix4fv(projLoc, 1, false, (float*)&proj);
        GL.Uniform3f(colorLoc, outlineColor.X, outlineColor.Y, outlineColor.Z);

        // Disable face culling: all triangles render, stencil test (NOTEQUAL, 1)
        // blocks the original area so only the expanded border is visible.
        OpenGL.EnableFaceCulling(false);

        GL.BindVertexArray(_object3D.VAO);
        GL.DrawArrays(Const.GL_TRIANGLES, 0, _object3D.VertexCount);
        GL.BindVertexArray(0);

        GL.UseProgram(0);
    }

    /// <summary>Get world-space AABB (method version for API compatibility).</summary>
    public AABB GetWorldAABB() => WorldAABB;

    // ════════════════════════════════════════════════════════════════════
    //  GLB reference (PrimitiveType == GlbReference)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Lazily load the GLB model referenced by <see cref="GlbFilePath"/> into a
    /// reusable <see cref="GltfObject"/>. GPU data is cached per file (flyweight) so many
    /// references to the same model share one set of buffers.</summary>
    private void EnsureGlb()
    {
        if (PrimitiveType != EditorPrimitiveType.GlbReference) return;

        // Compare against the RESOLVED path so different relative/absolute spellings of the
        // same file (e.g. after a load/save roundtrip) don't trigger a needless reload.
        string resolvedPath = string.IsNullOrEmpty(GlbFilePath) ? "" : PathHelpers.Resolve(GlbFilePath);
        if (_glbObject != null && _glbLoadedPath == resolvedPath) return;

        _glbObject = null;
        _glbLoadedPath = null;
        if (string.IsNullOrEmpty(resolvedPath)) return;

        try
        {
            if (!File.Exists(resolvedPath))
            {
                Console.WriteLine($"[EditorObject] GLB not found: {resolvedPath}");
                return;
            }
            if (!GlbGpuCache.TryGetValue(resolvedPath, out var gpuData))
            {
                var data = GltfLoader.Load(resolvedPath);
                gpuData = new GltfModelGpuData(data);
                GlbGpuCache[resolvedPath] = gpuData;
            }
            _glbObject = new GltfObject(gpuData, Position, Quaternion.Identity, 1f)
            {
                IsStatic = true,
            };
            _glbObject.Update(0f); // bake node hierarchy transforms for the bind pose
            _glbLoadedPath = resolvedPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EditorObject] Failed to load GLB '{GlbFilePath}': {ex.Message}");
            _glbObject = null;
        }
    }

    /// <summary>Push the current editor transform (position/rotation/scale) into the GLB
    /// object so Draw/DrawShadow/outline render at the gizmo transform. GLB scale is uniform
    /// (uses Scale.X — non-uniform editor scale is approximated by the X component).</summary>
    private void SyncGlbTransform()
    {
        if (_glbObject == null) return;
        _glbObject.Position = Position;
        _glbObject.Rotation = Quaternion.CreateFromYawPitchRoll(
            RotationEuler.Y * MathF.PI / 180f,
            RotationEuler.X * MathF.PI / 180f,
            RotationEuler.Z * MathF.PI / 180f);
        _glbObject.Scale = Math.Max(0.001f, Scale.X);
    }

    /// <summary>Approximate world-space bounding radius of the GLB mesh (for shadow-frustum
    /// culling). Falls back to a scale-based sphere when the model isn't loaded yet.</summary>
    public float GetGlbBoundRadius()
    {
        EnsureGlb();
        if (_glbObject?.GpuData != null)
        {
            var aabb = _glbObject.GpuData.LocalAABB;
            float modelRadius = (aabb.Max - aabb.Min).Length() * 0.5f;
            float maxScale = Math.Max(Scale.X, Math.Max(Scale.Y, Scale.Z));
            return Math.Max(1f, modelRadius * Math.Max(1f, maxScale));
        }
        return Math.Max(1f, 1.5f * Scale.Length() * 0.5f);
    }

    /// <summary>Skinned joint matrices of the loaded GLB (or null when not skinned/loaded).</summary>
    public Matrix4x4[]? GetGlbJointMatrices()
    {
        EnsureGlb();
        return _glbObject?.GetJointMatrices();
    }

    /// <summary>Render the GLB reference meshes with the gltf shader. The caller
    /// (EditorObjectManager) has already bound the gltf program and uploaded the shared
    /// view/proj/light/shadow/sampler uniforms.</summary>
    public void DrawGlb(
        int modelLoc, int baseColorFactorLoc, int useAlbedoLoc, int albedoMapLoc,
        int metallicFactorLoc = -1, int roughnessFactorLoc = -1, int normalScaleLoc = -1,
        int occlusionStrengthLoc = -1, int emissiveFactorLoc = -1,
        int hasNormalTextureLoc = -1, int hasMetallicRoughnessTextureLoc = -1,
        int hasOcclusionTextureLoc = -1, int hasEmissiveTextureLoc = -1)
    {
        if (!IsVisible) return;
        EnsureGlb();
        if (_glbObject == null) return;
        SyncGlbTransform();
        _glbObject.Draw(modelLoc, baseColorFactorLoc, useAlbedoLoc, albedoMapLoc,
            metallicFactorLoc, roughnessFactorLoc, normalScaleLoc,
            occlusionStrengthLoc, emissiveFactorLoc,
            hasNormalTextureLoc, hasMetallicRoughnessTextureLoc,
            hasOcclusionTextureLoc, hasEmissiveTextureLoc);
    }

    /// <summary>Render shadow using individual uniform locations (matching EditorObjectManager's call pattern).</summary>
    public void RenderShadow(int shadowModelLoc, Camera camera, CSM csm, int cascadeIndex)
    {
        if (!IsVisible || !CastShadow) return;

        // ── Advanced terrain planes cast shadows through their dedicated mesh. ──
        if (PrimitiveType == EditorPrimitiveType.Plane && TerrainEnabled && _terrainMesh is { IsReady: true })
        {
            _terrainMesh.RenderShadow(TerrainModelMatrix, csm, cascadeIndex);
            return;
        }

        // ── GLB reference: skinned shadow shader (matches the animated pose). ──
        if (PrimitiveType == EditorPrimitiveType.GlbReference)
        {
            DrawGlbShadow(csm, cascadeIndex);
            return;
        }

        if (_object3D == null) return;

        // Use the internally stored shadow shader (provided by manager)
        uint shadowShader = Shader.GetShadowShaderProgram();
        GL.UseProgram(shadowShader);

        var lightSpace = csm.LightSpaceMatrices[cascadeIndex];
        GL.UniformMatrix4fv(GL.GetUniformLocation(shadowShader, "lightSpaceMatrix"), 1, false, (float*)&lightSpace);

        // CRITICAL: Object3D.RenderShadow uploads its INTERNAL modelMatrix to the "model"
        // uniform. That matrix is only kept fresh by the legacy Draw(float dt, ...) path,
        // which the manager no longer calls — so shadows were rendered with the stale
        // constructor-default matrix (identity at the origin) and the stale-matrix frustum
        // culling culled everything out. Sync it with the current editor transform here.
        _object3D.UpdateModelMatriC(WorldMatrix);
        _object3D.RenderShadow(camera, csm, cascadeIndex, shadowShader, shadowModelLoc);
    }

    /// <summary>Mark object as needing resource refresh (color/texture changed).</summary>
    public void MarkDirty()
    {
        _dirty = true;
        _vertexCache = null; // Invalidate wireframe cache until EnsureResources() rebuilds it
        // Terrain cache key is compared against the live settings, so a rebuild happens
        // automatically next time EnsureResources() runs if anything changed.
    }

    /// <summary>Get (creating if needed) the PBR data for terrain layer 0..4.</summary>
    public TerrainPbrLayerData EnsureTerrainLayer(int index)
    {
        if (TerrainLayers == null || TerrainLayers.Length != 5)
            TerrainLayers = [new(), new(), new(), new(), new()];
        return TerrainLayers[index] ??= new TerrainPbrLayerData();
    }


    /// <summary>Draw the primitive using Object3D's rendering pipeline.</summary>
    public void Draw(float dt, nint window, float moveSpeed)
    {
        if (!IsVisible) return;

        if (_object3D == null) return;

        // Apply world transform via model matrix
        var model = WorldMatrix;
        _object3D.UpdateModelMatriC(model);

        // Apply color
        // Update vertex colors if changed (simplified - we re-create on color change via MarkDirty)
        if (PrimitiveType != EditorPrimitiveType.GlbReference)
        {
            _object3D.Draw(dt, window, moveSpeed);
        }
    }

    /// <summary>Render shadow for this object.</summary>
    public void RenderShadow(Camera camera, CSM csm, int cascadeIndex, uint shadowShader, int modelLoc)
    {
        if (!IsVisible || !CastShadow) return;

        // ── Advanced terrain planes cast shadows through their dedicated mesh. ──
        if (PrimitiveType == EditorPrimitiveType.Plane && TerrainEnabled && _terrainMesh is { IsReady: true })
        {
            _terrainMesh.RenderShadow(TerrainModelMatrix, csm, cascadeIndex);
            return;
        }

        // ── GLB reference: skinned shadow shader (matches the animated pose). ──
        if (PrimitiveType == EditorPrimitiveType.GlbReference)
        {
            DrawGlbShadow(csm, cascadeIndex);
            return;
        }

        if (_object3D == null) return;

        // Keep the internal model matrix in sync (see the other overload for details) so
        // the shadow pass renders at the object's current position/scale/rotation.
        _object3D.UpdateModelMatriC(WorldMatrix);
        _object3D.RenderShadow(camera, csm, cascadeIndex, shadowShader, modelLoc);
    }

    /// <summary>Create a new EditorObject of the given type with default values.</summary>
    public static EditorObject CreateDefault(EditorPrimitiveType type, string name, Vector3 position)
    {
        var obj = new EditorObject(type, name)
        {
            Position = position,
            Scale = type == EditorPrimitiveType.Plane
                ? new Vector3(500f, 0.05f, 500f)
                : Vector3.One,
            CastShadow = true,
            IsVisible = true,
        };
        // Snapshot initial state for undo
        obj.LastGizmoPosition = obj.Position;
        obj.LastGizmoRotation = obj.RotationEuler;
        obj.LastGizmoScale = obj.Scale;
        obj.LastGizmoPivot = obj.GizmoPivotOverride;
        return obj;
    }

    // ════════════════════════════════════════════════════════════════════
    //  2D Map rendering — textured plane with per-tile UV mapping
    //  Uses a dedicated inline shader (similar to Sprite2D) for simple
    //  textured rendering without the complex terrain lighting.
    // ════════════════════════════════════════════════════════════════════

    // Dedicated shader for Map2D (simple textured quad with per-vertex alpha)
    private static uint _map2dShader;
    private static int _map2dLocView, _map2dLocProj, _map2dLocModel, _map2dLocTex;

    private static unsafe void EnsureMap2DShader()
    {
        if (_map2dShader != 0) return;

        string vertSrc = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec2 aUV;
layout(location=2) in vec4 aTint;
uniform mat4 view;
uniform mat4 projection;
uniform mat4 model;
out vec2 vUV;
out vec4 vTint;
void main() {
    gl_Position = projection * view * model * vec4(aPos, 1.0);
    vUV = aUV;
    vTint = aTint;
}";

        string fragSrc = @"#version 330 core
in vec2 vUV;
in vec4 vTint;
uniform sampler2D tex;
out vec4 FragColor;
void main() {
    vec4 c = texture(tex, vUV) * vTint;
    if (c.a < 0.01) discard;
    FragColor = c;
}";

        uint vert = GL.CreateShader(Const.GL_VERTEX_SHADER);
        GL.ShaderSource(vert, vertSrc);
        GL.CompileShader(vert);

        uint frag = GL.CreateShader(Const.GL_FRAGMENT_SHADER);
        GL.ShaderSource(frag, fragSrc);
        GL.CompileShader(frag);

        _map2dShader = GL.CreateProgram();
        GL.AttachShader(_map2dShader, vert);
        GL.AttachShader(_map2dShader, frag);
        GL.LinkProgram(_map2dShader);
        GL.DeleteShader(vert);
        GL.DeleteShader(frag);

        _map2dLocView = GL.GetUniformLocation(_map2dShader, "view");
        _map2dLocProj = GL.GetUniformLocation(_map2dShader, "projection");
        _map2dLocModel = GL.GetUniformLocation(_map2dShader, "model");
        _map2dLocTex = GL.GetUniformLocation(_map2dShader, "tex");
    }

    private struct Map2DVertex
    {
        // layout: pos(vec3) + uv(vec2) + tint(vec4)
        public float X, Y, Z;      // location 0: position
        public float U, V;         // location 1: texcoord
        public float R, G, B, A;   // location 2: tint (RGBA)
        public Map2DVertex(float x, float y, float z, float u, float v, float r, float g, float b, float a)
        {
            X = x; Y = y; Z = z;
            U = u; V = v;
            R = r; G = g; B = b; A = a;
        }
    }

    /// <summary>Draw the parallax layers pushed from the Map Editor as upright textured
    /// quads around the tile grid. Each layer's ZPosition maps to a world-Z offset:
    /// &gt; 0 renders IN FRONT of the grid, &lt; 0 renders BEHIND it, 0 sits on the grid
    /// plane itself. Layers are drawn back-to-front so alpha blending stacks correctly.
    /// Must be called while extentW/extentH (map world size) are known — reuses the same
    /// Map2D shader + vertex layout as the tile mesh.</summary>
    /// <summary>Build a VBO with one quad per non-empty tile, UV-mapped into the tileset grid.
    /// Vertices are in local space; the WorldMatrix positions/scales the whole mesh.
    /// Renders ALL visible layers at once (layered in world Z by index so upper layers
    /// draw over lower ones) — the Map Editor's "active layer" only controls painting.
    /// Layer visibility comes from TileLayer.IsVisible, so a visible layer always shows.</summary>
    private unsafe void BuildMap2DMesh()
    {
        var map = Map2dTilemap;
        if (map == null) return;

        // Render EVERY visible layer in one mesh. Layer visibility is respected
        // (IsVisible=false layers are skipped), so a visible layer always renders.
        // Upper layers must draw OVER lower ones: all layers bake coplanar at world
        // z=0, so each layer gets a tiny local-Y lift (local Y maps to world depth
        // through the -90° X rotation) — enough to win the depth test without any
        // visible offset.
        const float layerLiftStep = 0.01f; // world depth units between stacked layers
        string cacheKey = $"{map.Width}|{map.Height}|{map.TileSize}|{Map2dTilesetCols}|{Map2dTilesetRows}|{map.Layers.Count}|{Map2dLayerIndex}";
        foreach (var l in map.Layers)
        {
            cacheKey += $"|{l.IsVisible}|{l.Opacity}";
            for (int i = 0; i < map.Width * map.Height; i++)
                cacheKey += $"|{l.GetTile(i % map.Width, i / map.Width)}";
        }

        if (_map2dMeshCacheKey == cacheKey && _map2dVAO != 0) return;
        _map2dMeshCacheKey = cacheKey;

        if (_map2dVAO != 0) { uint v = _map2dVAO; GL.DeleteVertexArrays(1, &v); _map2dVAO = 0; }
        if (_map2dVBO != 0) { uint v = _map2dVBO; GL.DeleteBuffers(1, &v); _map2dVBO = 0; }
        if (_player2dVAO != 0) { uint v = _player2dVAO; GL.DeleteVertexArrays(1, &v); _player2dVAO = 0; }
        if (_player2dVBO != 0) { uint v = _player2dVBO; GL.DeleteBuffers(1, &v); _player2dVBO = 0; }

        int mapW = map.Width;
        int mapH = map.Height;
        int tsCols = Math.Max(1, Map2dTilesetCols);
        int tsRows = Math.Max(1, Map2dTilesetRows);
        float tileUW = 1f / tsCols;
        float tileVH = 1f / tsRows;

        var verts = new List<Map2DVertex>();

        // Bake every visible layer; deeper layers get a slightly smaller world-Z so
        // upper layers composite over them (Z-fighting-free ordering).
        for (int li = 0; li < map.Layers.Count; li++)
        {
            var layer = map.Layers[li];
            if (!layer.IsVisible) continue;
            float liftY = -li * layerLiftStep; // higher layer index → closer to the front camera
            for (int ty = 0; ty < mapH; ty++)
            {
                for (int tx = 0; tx < mapW; tx++)
                {
                    int tileId = layer.GetTile(tx, ty);
                    if (tileId < 0) continue;

                    int tc = tileId % tsCols;
                    int tr = tileId / tsCols;
                    if (tr >= tsRows) continue;

                    float vFlip = Map2dTilesetFlipV ? -1f : 1f;
                    float u0 = tc * tileUW;
                    float v0 = (tr + (vFlip < 0f ? 0f : 1f)) * tileVH * vFlip;
                    float u1 = u0 + tileUW;
                    float v1 = (tr + (vFlip < 0f ? 1f : 0f)) * tileVH * vFlip;

                    float worldTs = map.TileSize * Tilemap2D.WorldScale;
                    float x0 = tx * worldTs;
                    float x1 = x0 + worldTs;
                    // Row 0 at the top (matches Tilemap2D.GridToWorld, where y is
                    // flipped); the -90° X rotation turns this into upright +Y.
                    float z0 = (mapH - 1 - ty) * worldTs;
                    float z1 = z0 + worldTs;
                    float a = layer.Opacity;

                    verts.Add(new Map2DVertex(x0, liftY, z0, u0, v0, 1, 1, 1, a));
                    verts.Add(new Map2DVertex(x1, liftY, z0, u1, v0, 1, 1, 1, a));
                    verts.Add(new Map2DVertex(x1, liftY, z1, u1, v1, 1, 1, 1, a));

                    verts.Add(new Map2DVertex(x0, liftY, z0, u0, v0, 1, 1, 1, a));
                    verts.Add(new Map2DVertex(x1, liftY, z1, u1, v1, 1, 1, 1, a));
                    verts.Add(new Map2DVertex(x0, liftY, z1, u0, v1, 1, 1, 1, a));
                }
            }
        }

        _map2dVertCount = verts.Count;
        if (_map2dVertCount == 0) return;

        uint vao = 0, vbo = 0;
        GL.GenVertexArrays(1, &vao);
        GL.BindVertexArray(vao);

        GL.GenBuffers(1, &vbo);
        GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);

        int stride = sizeof(Map2DVertex);
        GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(_map2dVertCount * stride), (void*)0, Const.GL_DYNAMIC_DRAW);

        fixed (Map2DVertex* p = verts.ToArray())
        {
            GL.BufferSubData(Const.GL_ARRAY_BUFFER, (nuint)0, (nuint)(_map2dVertCount * stride), p);
        }

        // location 0 = aPos (vec3)
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, stride, (void*)0);
        // location 1 = aUV (vec2)
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));
        // location 2 = aTint (vec4)
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, Const.GL_FLOAT, false, stride, (void*)(5 * sizeof(float)));

        GL.BindVertexArray(0);

        _map2dVAO = vao;
        _map2dVBO = vbo;
    }

    /// <summary>Draw the 2D map as a textured plane in the 3D scene.</summary>
    private unsafe void DrawMap2D(
        int modelLoc, int viewLoc, int projLoc,
        int sunDirLoc, int lightColorLoc, int viewPosLoc,
        int useFogLoc, int fogColorLoc,
        Camera camera, Lights light, CSM? csm)
    {
        if (Map2dTilemap == null) return;

        // Load tileset texture if needed
        string tilesetPath = Map2dTilemap.TilesetImagePath ?? "";
        if (!string.IsNullOrEmpty(tilesetPath) && tilesetPath != _map2dTilesetPath)
        {
            if (_map2dTilesetTex != 0) { uint t = _map2dTilesetTex; GL.DeleteTextures(1, &t); _map2dTilesetTex = 0; }
            if (File.Exists(tilesetPath))
            {
                var tex = new Texture(tilesetPath);
                _map2dTilesetTex = tex.ID;
                GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            }
            _map2dTilesetPath = tilesetPath;
        }

        BuildMap2DMesh();

        // The map is drawn as ONE canonical upright plane at the world origin so it
        // lines up exactly with the editor paint/hover math (Tilemap2D.WorldToGrid /
        // GridToWorld operate on the XY plane at z = layer index). The object's own
        // Position/Scale/Rotation are deliberately NOT applied: the mesh below is baked
        // in world units (px × Tilemap2D.WorldScale) and layer stacking is an explicit
        // world-Z offset (the layer index), matching the paint raycast plane.
        int mapW = Map2dTilemap.Width;
        int mapH = Map2dTilemap.Height;
        int ts = Map2dTilemap.TileSize;
        float cell = ts * Tilemap2D.WorldScale;
        float extentW = mapW * cell;
        float extentH = mapH * cell;
        float layerZ = Map2dLayerIndex >= 0 ? (float)Map2dLayerIndex : 0f;
        var model = Matrix4x4.CreateTranslation(0f, 0f, layerZ)
                  * Matrix4x4.CreateRotationX(-MathF.PI / 2f);

        // Draw the tiles only when the map has some; the grid pass below still runs
        // for an empty map so "New Map" immediately shows the upright plane outline
        // with square cells in the 3D viewport.
        if (_map2dVAO != 0 && _map2dVertCount != 0)
        {
            EnsureMap2DShader();
            if (_map2dShader != 0)
            {
                GL.UseProgram(_map2dShader);

                var view = camera.GetViewMatrix();
                var proj = camera.GetProjectionMatrix();
                GL.UniformMatrix4fv(_map2dLocView, 1, false, &view.M11);
                GL.UniformMatrix4fv(_map2dLocProj, 1, false, &proj.M11);
                GL.UniformMatrix4fv(_map2dLocModel, 1, false, &model.M11);

                GL.ActiveTexture(Const.GL_TEXTURE0);
                GL.BindTexture(Const.GL_TEXTURE_2D, _map2dTilesetTex);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_NEAREST);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_NEAREST);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
                GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
                GL.Uniform1i(_map2dLocTex, 0);

                GL.Enable(Const.GL_BLEND);
                GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);

                // Map is visible from both sides (layers stack along Z; the editor
                // camera can orbit either side).
                bool cullEnabled = GL.IsEnabled(Const.GL_CULL_FACE);
                GL.Disable(Const.GL_CULL_FACE);

                GL.BindVertexArray(_map2dVAO);
                GL.DrawArrays(Const.GL_TRIANGLES, 0, _map2dVertCount);
                GL.BindVertexArray(0);

                if (cullEnabled)
                    GL.Enable(Const.GL_CULL_FACE);
                GL.Disable(Const.GL_BLEND);
                GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            }
        }

        // ── Parallax layers: upright textured quads offset in world Z by their ZPosition
        //    (positive = in front of the grid, negative = behind it). Drawn back-to-front
        //    so depth + alpha blend stack correctly against the tile plane at Z = 0.
        //    Bottom edge anchored at the grid bottom (world Y = 0); horizontally tiled
        //    to cover the map extent when TileHorizontal is set. ──
        if (Map2dParallaxLayers is { Count: > 0 })
        {
            bool pCull = GL.IsEnabled(Const.GL_CULL_FACE);
            GL.Disable(Const.GL_CULL_FACE);

            var sorted = Map2dParallaxLayers
                .Where(l => l != null && l.IsVisible && !string.IsNullOrEmpty(l.ImagePath) && l.TextureId != 0)
                .OrderBy(l => l.ZPosition) // farthest (most negative) first
                .ToList();

            if (sorted.Count > 0)
            {
                EnsureMap2DShader();
                GL.UseProgram(_map2dShader);

                // ── Parallax scroll preview ──
                // Plain absolute camera X: offset = -camX × ScrollFactor. No anchor —
                // the home offset is divided into a fractional UV phase (seamless via
                // GL_REPEAT) plus whole-width copies, so layers stay glued to their
                // Left/Top position while panning still previews the depth illusion.
                float camX = camera.Position.X;

                // Parallax quads are baked directly in world space → identity model.
                var ident = Matrix4x4.Identity;
                GL.UniformMatrix4fv(_map2dLocModel, 1, false, &ident.M11);

                GL.Enable(Const.GL_BLEND);
                GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);

                float px2world = Tilemap2D.WorldScale;
                var pVerts = new List<Map2DVertex>(24);
                foreach (var pl in sorted)
                {
                    float a = Math.Clamp(pl.Alpha, 0f, 1f);
                    if (a <= 0.01f) continue;

                    // Quad size: default = the GRID extent (user rule), regardless of the
                    // image's natural size — WidthPx/HeightPx only override when > 0.
                    // An unset axis never falls back to the raw image size, which is what
                    // made layers render huge/misaligned before.
                    //
                    // Aspect-ratio preservation: when exactly ONE axis is set, the other
                    // axis scales from the image's natural aspect ratio (relative to the
                    // set axis) instead of stretching to the grid. Setting BOTH axes
                    // always stretches exactly as specified; setting NEITHER uses the
                    // grid extent.
                    bool wSet = pl.WidthPx > 0.5f;
                    bool hSet = pl.HeightPx > 0.5f;
                    float w, h;
                    if (wSet && hSet)
                    {
                        w = pl.WidthPx * px2world;
                        h = pl.HeightPx * px2world;
                    }
                    else if (wSet)
                    {
                        // Only width set → height keeps the image aspect ratio.
                        w = pl.WidthPx * px2world;
                        float imgAspect = pl.ImageHeight > 0 && pl.ImageWidth > 0
                            ? (float)pl.ImageHeight / pl.ImageWidth : 1f;
                        h = w * imgAspect;
                    }
                    else if (hSet)
                    {
                        // Only height set → width keeps the image aspect ratio.
                        h = pl.HeightPx * px2world;
                        float imgAspect = pl.ImageWidth > 0 && pl.ImageHeight > 0
                            ? (float)pl.ImageWidth / pl.ImageHeight : 1f;
                        w = h * imgAspect;
                    }
                    else
                    {
                        // Neither set → proportional to the image's ORIGINAL aspect ratio:
                        // height fits the grid height, width follows the image ratio (so
                        // panoramas stay wide, tall skies stay tall — never stretched to
                        // the grid). Falls back to the grid extent only when the image
                        // dimensions are unknown.
                        h = extentH;
                        w = pl.ImageWidth > 0 && pl.ImageHeight > 0
                            ? h * ((float)pl.ImageWidth / pl.ImageHeight)
                            : extentW;
                    }
                    w = MathF.Max(1f, w);
                    h = MathF.Max(1f, h);
                    float z = pl.ZPosition * cell; // 1 ZPosition unit = one tile cell of depth
                    // Texture repeats across the quad. RepeatX/Y = explicit UV repeats
                    // (requires GL_REPEAT wrapping — parallax textures use it for S).
                    // 0 = auto: one natural copy; TileHorizontal still tiles whole copies
                    // across the grid width.
                    int uvRepeatX = Math.Max(0, pl.RepeatX);
                    int uvRepeatY = Math.Max(0, pl.RepeatY);

                    GL.ActiveTexture(Const.GL_TEXTURE0);
                    GL.BindTexture(Const.GL_TEXTURE_2D, pl.TextureId);
                    // Stretch wrap mode when explicit repeats are used so UV &gt; 1 wraps.
                    if (uvRepeatX > 0)
                        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_REPEAT);
                    if (uvRepeatY > 0)
                        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_REPEAT);
                    GL.Uniform1i(_map2dLocTex, 0);

                    // Horizontal scroll preview, RELATIVE to the layer's home position:
                    // the home offset (-camX × ScrollFactor) is what makes layers slide
                    // at different speeds; subtracting the fractional part keeps the
                    // visible texture anchored (no surprise half-screen jumps at load —
                    // the previous absolute offset made scrolled layers look broken).
                    float uPhase = 0f;
                    float xShift = 0f;
                    if (pl.ScrollFactor != 0f && w > 0.01f)
                    {
                        float scrollWorld = -camX * pl.ScrollFactor;
                        float frac = (scrollWorld / w) % 1f;
                        if (frac < 0f) frac += 1f;
                        uPhase = frac;
                        xShift = scrollWorld - frac * w; // whole widths back → home pos
                    }

                    // Static offsets: Left DISABLED (pinned to grid left edge); Top is
                    // ACTIVE — the quad is TOP-anchored at the grid's top edge
                    // (world Y = extentH) and TopPx pushes it DOWN (negative TopPx
                    // extends above the grid, e.g. for tall skies).
                    float leftWorld = 0f;
                    float topY = extentH - pl.TopPx * px2world;

                    // Copy range: cover the grid extent AND the camera neighborhood so
                    // panning (any scroll factor) never reveals the quads' edges.
                    int rStart, rEnd;
                    if (pl.TileHorizontal && w > 0.01f)
                    {
                        float leftNeeded = MathF.Min(0f, camX - extentW);
                        float rightNeeded = MathF.Max(extentW, camX + extentW);
                        rStart = (int)MathF.Floor((leftNeeded - xShift - leftWorld) / w);
                        rEnd = (int)MathF.Ceiling((rightNeeded - xShift - leftWorld) / w);
                    }
                    // (leftNeeded/rightNeeded still use ABSOLUTE camX — only the layer
                    // offset is relative — so coverage math stays in the same space the
                    // camera actually renders.)
                    else
                    {
                        rStart = 0;
                        rEnd = 0;
                    }

                    pVerts.Clear();
                    for (int r = rStart; r <= rEnd; r++)
                    {
                        float x0 = r * w + xShift + leftWorld;
                        float x1 = x0 + w;
                        float yTop = topY;
                        float yBot = topY - h;
                        // UV spans (uvRepeat + phase) so RepeatX and the scroll wrap
                        // combine; 0 keeps one natural copy per quad.
                        float u0 = -uPhase * (uvRepeatX > 0 ? uvRepeatX : 1f);
                        float u1 = u0 + (uvRepeatX > 0 ? uvRepeatX : 1f);
                        float v1 = uvRepeatY > 0 ? uvRepeatY : 1f;
                        // Top vertex samples v=0: the image top row was uploaded first,
                        // so v=0 IS the top — this keeps the picture upright.
                        // Vertex alpha carries the layer opacity — the shader multiplies
                        // the per-vertex tint (the "tint" uniform has no location in this
                        // shader, so per-vertex is the only channel that reaches the GPU).
                        pVerts.Add(new Map2DVertex(x0, yBot, z, u0, v1, 1, 1, 1, a));
                        pVerts.Add(new Map2DVertex(x1, yBot, z, u1, v1, 1, 1, 1, a));
                        pVerts.Add(new Map2DVertex(x1, yTop, z, u1, 0f, 1, 1, 1, a));
                        pVerts.Add(new Map2DVertex(x0, yBot, z, u0, v1, 1, 1, 1, a));
                        pVerts.Add(new Map2DVertex(x1, yTop, z, u1, 0f, 1, 1, 1, a));
                        pVerts.Add(new Map2DVertex(x0, yTop, z, u0, 0f, 1, 1, 1, a));
                    }

                    if (_parallaxVAO == 0)
                    {
                        uint vao = 0, vbo = 0;
                        GL.GenVertexArrays(1, &vao);
                        GL.BindVertexArray(vao);
                        GL.GenBuffers(1, &vbo);
                        GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
                        GL.EnableVertexAttribArray(0);
                        GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, sizeof(Map2DVertex), (void*)0);
                        GL.EnableVertexAttribArray(1);
                        GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, sizeof(Map2DVertex), (void*)(3 * sizeof(float)));
                        GL.EnableVertexAttribArray(2);
                        GL.VertexAttribPointer(2, 4, Const.GL_FLOAT, false, sizeof(Map2DVertex), (void*)(5 * sizeof(float)));
                        GL.BindVertexArray(0);
                        _parallaxVAO = vao;
                        _parallaxVBO = vbo;
                    }

                    fixed (Map2DVertex* p = pVerts.ToArray())
                    {
                        GL.BindVertexArray(_parallaxVAO);
                        GL.BindBuffer(Const.GL_ARRAY_BUFFER, _parallaxVBO);
                        GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(pVerts.Count * sizeof(Map2DVertex)), p, Const.GL_DYNAMIC_DRAW);
                        GL.DrawArrays(Const.GL_TRIANGLES, 0, pVerts.Count);
                        GL.BindVertexArray(0);
                    }

                    // Restore default wrap so other passes (tileset uses CLAMP) are
                    // unaffected by the repeat settings above.
                    if (uvRepeatX > 0)
                        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
                    if (uvRepeatY > 0)
                        GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
                }

                // Restore the tile model matrix so later passes stay aligned.
                GL.UniformMatrix4fv(_map2dLocModel, 1, false, &model.M11);
                GL.BindTexture(Const.GL_TEXTURE_2D, 0);
            }

            if (pCull)
                GL.Enable(Const.GL_CULL_FACE);
        }

        // ── Collision helper boxes: one translucent box per tile flagged for collision,
        //    sticking OUT of the grid plane toward the viewer so the player can "stand"
        //    on it (like Unreal's collision previews). A tile whose ID has NO collision
        //    flag draws NO box. Uses the Map2D tint shader with the shared white texture.
        if (Map2dShowCollision && !Editor2DAidsHidden && Map2dTilemap != null)
        {
            var colLayer = Map2dActiveLayer >= 0 && Map2dActiveLayer < Map2dTilemap.Layers.Count
                ? Map2dTilemap.Layers[Map2dActiveLayer]
                : (Map2dTilemap.Layers.Count > 0 ? Map2dTilemap.Layers[0] : null);

            if (colLayer != null && colLayer.CollisionTileIds.Count > 0)
            {
                EnsureMap2DShader();
                if (_map2dShader != 0)
                {
                    EnsurePbrWhiteTex();

                    GL.UseProgram(_map2dShader);
                    var view = camera.GetViewMatrix();
                    var proj = camera.GetProjectionMatrix();
                    GL.UniformMatrix4fv(_map2dLocView, 1, false, &view.M11);
                    GL.UniformMatrix4fv(_map2dLocProj, 1, false, &proj.M11);

                    GL.ActiveTexture(Const.GL_TEXTURE0);
                    GL.BindTexture(Const.GL_TEXTURE_2D, _pbrWhiteTex);
                    GL.Uniform1i(_map2dLocTex, 0);
                    // NOTE: the map2d shader has no "tint" uniform — color reaches the
                    // GPU only via the per-vertex tint attribute (baked below).

                    GL.Enable(Const.GL_BLEND);
                    GL.BlendFunc(Const.GL_SRC_ALPHA, Const.GL_ONE_MINUS_SRC_ALPHA);

                    bool cullCol = GL.IsEnabled(Const.GL_CULL_FACE);
                    GL.Disable(Const.GL_CULL_FACE);

                    // One FULL 3D box per collision tile: translucent shaded faces +
                    // bright edges so the collision volume reads from any angle (like
                    // Unreal's collision previews). The box is CENTERED on the grid
                    // plane — the 2D tile sits exactly in the middle of the box, half
                    // the depth in front of the plane, half behind it. Local -Y maps
                    // to world +Z through the -90° X rotation. Vertices stay in the
                    // plane's local space so the same translate+rotate model matrix as
                    // the tile mesh positions them.
                    float boxDepth = cell * 0.5f;
                    float halfDepth = boxDepth * 0.5f;
                    float cR = Map2dCollisionColor.X, cG = Map2dCollisionColor.Y, cB = Map2dCollisionColor.Z;
                    float cA = Math.Clamp(Map2dCollisionColor.W, 0.05f, 1f);
                    var boxVerts = new List<Map2DVertex>(36 * 16);
                    var edgeVerts = new List<Vector3>(24 * 16);

                    void ColFace(float m, float ax, float ay, float az, float bx, float by, float bz,
                                 float cx, float cy, float cz, float dx, float dy, float dz)
                    {
                        boxVerts.Add(new Map2DVertex(ax, ay, az, 0, 0, cR * m, cG * m, cB * m, cA));
                        boxVerts.Add(new Map2DVertex(bx, by, bz, 0, 0, cR * m, cG * m, cB * m, cA));
                        boxVerts.Add(new Map2DVertex(cx, cy, cz, 0, 0, cR * m, cG * m, cB * m, cA));
                        boxVerts.Add(new Map2DVertex(ax, ay, az, 0, 0, cR * m, cG * m, cB * m, cA));
                        boxVerts.Add(new Map2DVertex(cx, cy, cz, 0, 0, cR * m, cG * m, cB * m, cA));
                        boxVerts.Add(new Map2DVertex(dx, dy, dz, 0, 0, cR * m, cG * m, cB * m, cA));
                    }

                    for (int ty = 0; ty < mapH; ty++)
                    {
                        for (int tx = 0; tx < mapW; tx++)
                        {
                            int tileId = colLayer.GetTile(tx, ty);
                            if (tileId < 0 || !colLayer.TileHasCollision(tileId)) continue;

                            float wx0 = tx * cell;
                            float wx1 = wx0 + cell;
                            // Row 0 = top row → world Y flipped (same as the tile mesh)
                            float wy0 = (mapH - 1 - ty) * cell;
                            float wy1 = wy0 + cell;
                            float yN = -halfDepth; // near face → world +Z (in front of grid)
                            float yF = +halfDepth; // far face → world -Z (behind grid) — tile centered

                            // 6 shaded faces (top brightest, sides dimmer → 3D depth cue)
                            ColFace(1.25f, wx0, yN, wy1, wx1, yN, wy1, wx1, yF, wy1, wx0, yF, wy1); // top
                            ColFace(0.55f, wx0, yN, wy0, wx1, yN, wy0, wx1, yF, wy0, wx0, yF, wy0); // bottom
                            ColFace(0.80f, wx0, yN, wy0, wx0, yN, wy1, wx0, yF, wy1, wx0, yF, wy0); // left
                            ColFace(0.80f, wx1, yN, wy0, wx1, yN, wy1, wx1, yF, wy1, wx1, yF, wy0); // right
                            ColFace(1.00f, wx0, yN, wy0, wx1, yN, wy0, wx1, yN, wy1, wx0, yN, wy1); // near
                            ColFace(0.45f, wx0, yF, wy0, wx1, yF, wy0, wx1, yF, wy1, wx0, yF, wy1); // far

                            // 12 world-space edges (bright outline, drawn after the faces)
                            float zF = layerZ - halfDepth, zN = layerZ + halfDepth;
                            edgeVerts.Add(new Vector3(wx0, wy0, zF)); edgeVerts.Add(new Vector3(wx1, wy0, zF));
                            edgeVerts.Add(new Vector3(wx1, wy0, zF)); edgeVerts.Add(new Vector3(wx1, wy1, zF));
                            edgeVerts.Add(new Vector3(wx1, wy1, zF)); edgeVerts.Add(new Vector3(wx0, wy1, zF));
                            edgeVerts.Add(new Vector3(wx0, wy1, zF)); edgeVerts.Add(new Vector3(wx0, wy0, zF));
                            edgeVerts.Add(new Vector3(wx0, wy0, zN)); edgeVerts.Add(new Vector3(wx1, wy0, zN));
                            edgeVerts.Add(new Vector3(wx1, wy0, zN)); edgeVerts.Add(new Vector3(wx1, wy1, zN));
                            edgeVerts.Add(new Vector3(wx1, wy1, zN)); edgeVerts.Add(new Vector3(wx0, wy1, zN));
                            edgeVerts.Add(new Vector3(wx0, wy1, zN)); edgeVerts.Add(new Vector3(wx0, wy0, zN));
                            edgeVerts.Add(new Vector3(wx0, wy0, zF)); edgeVerts.Add(new Vector3(wx0, wy0, zN));
                            edgeVerts.Add(new Vector3(wx1, wy0, zF)); edgeVerts.Add(new Vector3(wx1, wy0, zN));
                            edgeVerts.Add(new Vector3(wx1, wy1, zF)); edgeVerts.Add(new Vector3(wx1, wy1, zN));
                            edgeVerts.Add(new Vector3(wx0, wy1, zF)); edgeVerts.Add(new Vector3(wx0, wy1, zN));
                        }
                    }

                    if (boxVerts.Count > 0)
                    {
                        if (_parallaxVAO == 0)
                        {
                            uint vao = 0, vbo = 0;
                            GL.GenVertexArrays(1, &vao);
                            GL.BindVertexArray(vao);
                            GL.GenBuffers(1, &vbo);
                            GL.BindBuffer(Const.GL_ARRAY_BUFFER, vbo);
                            GL.EnableVertexAttribArray(0);
                            GL.VertexAttribPointer(0, 3, Const.GL_FLOAT, false, sizeof(Map2DVertex), (void*)0);
                            GL.EnableVertexAttribArray(1);
                            GL.VertexAttribPointer(1, 2, Const.GL_FLOAT, false, sizeof(Map2DVertex), (void*)(3 * sizeof(float)));
                            GL.EnableVertexAttribArray(2);
                            GL.VertexAttribPointer(2, 4, Const.GL_FLOAT, false, sizeof(Map2DVertex), (void*)(5 * sizeof(float)));
                            GL.BindVertexArray(0);
                            _parallaxVAO = vao;
                            _parallaxVBO = vbo;
                        }

                        // Boxes are baked in the plane's local space → the same
                        // translate+rotate model matrix as the tile mesh positions them.
                        GL.UniformMatrix4fv(_map2dLocModel, 1, false, &model.M11);

                        fixed (Map2DVertex* p = boxVerts.ToArray())
                        {
                            GL.BindVertexArray(_parallaxVAO);
                            GL.BindBuffer(Const.GL_ARRAY_BUFFER, _parallaxVBO);
                            GL.BufferData(Const.GL_ARRAY_BUFFER, (nuint)(boxVerts.Count * sizeof(Map2DVertex)), p, Const.GL_DYNAMIC_DRAW);
                            GL.DrawArrays(Const.GL_TRIANGLES, 0, boxVerts.Count);
                            GL.BindVertexArray(0);
                        }

                        // Bright edges: solid lines over the translucent faces so each
                        // box outline is clearly visible from any camera angle.
                        if (edgeVerts.Count > 0)
                        {
                            var edgeCol = new Vector3(
                                MathF.Min(1f, cR * 1.5f + 0.2f),
                                MathF.Min(1f, cG * 1.5f + 0.2f),
                                MathF.Min(1f, cB * 1.5f + 0.2f));
                            Terrains.TerrainChunk.DrawLineSegments(edgeVerts, edgeCol, camera, 0.95f);
                        }

                        // Restore the tile model matrix so later passes stay aligned.
                        GL.UniformMatrix4fv(_map2dLocModel, 1, false, &model.M11);
                    }

                    GL.BindTexture(Const.GL_TEXTURE_2D, 0);
                    GL.Disable(Const.GL_BLEND);
                    if (cullCol)
                        GL.Enable(Const.GL_CULL_FACE);
                }
            }
        }

        // ── Tile grid: one square per tile, drawn in world space on the same upright
        //    plane (and depth) the paint/hover highlight uses, so the grid is always
        //    exactly under the mouse boxes. Depth test is disabled while drawing so the
        //    grid reads over the tiles like an editor overlay. Drawn for the whole-map
        //    object (layer -1) and for layer 0 so a freshly created map always shows it.
        if (Map2dShowGrid && !Editor2DAidsHidden && Map2dLayerIndex <= 0)
        {
            var gridVerts = new List<Vector3>((mapW + mapH + 2) * 2);
            for (int gx = 0; gx <= mapW; gx++)
            {
                float px = gx * cell;
                gridVerts.Add(new Vector3(px, 0f, layerZ));
                gridVerts.Add(new Vector3(px, extentH, layerZ));
            }
            for (int gy = 0; gy <= mapH; gy++)
            {
                float py = gy * cell;
                gridVerts.Add(new Vector3(0f, py, layerZ));
                gridVerts.Add(new Vector3(extentW, py, layerZ));
            }

            bool depthEnabled = GL.IsEnabled(Const.GL_DEPTH_TEST);
            GL.Disable(Const.GL_DEPTH_TEST);
            var gridRgb = new Vector3(Map2dGridColor.X, Map2dGridColor.Y, Map2dGridColor.Z);
            float gridA = Math.Clamp(Map2dGridColor.W, 0.05f, 1f);
            Terrains.TerrainChunk.DrawLineSegments(gridVerts, gridRgb, camera, gridA);

            // Outer border: same hue pushed brighter so the map extent reads clearly.
            var borderRgb = new Vector3(
                MathF.Min(1f, gridRgb.X + 0.45f),
                MathF.Min(1f, gridRgb.Y + 0.45f),
                MathF.Min(1f, gridRgb.Z + 0.45f));
            var border = new List<Vector3>(8)
            {
                new(0f, 0f, layerZ), new(extentW, 0f, layerZ),
                new(extentW, 0f, layerZ), new(extentW, extentH, layerZ),
                new(extentW, extentH, layerZ), new(0f, extentH, layerZ),
                new(0f, extentH, layerZ), new(0f, 0f, layerZ)
            };
            Terrains.TerrainChunk.DrawLineSegments(border, borderRgb, camera, 0.8f);
            if (depthEnabled)
                GL.Enable(Const.GL_DEPTH_TEST);
        }

        // ── Player spawn marker: a small cyan cross + box ring at the map's spawn point
        //    (editor aid — hidden in-game). Y here is height above the map's bottom
        //    edge, matching Tilemap2D.PlayerSpawn. ──
        if (Map2dTilemap.HasPlayerSpawn && !Editor2DAidsHidden)
        {
            var sp = Map2dTilemap.PlayerSpawn;
            float sx = Math.Clamp(sp.X, 0f, extentW);
            float sy = Math.Clamp(sp.Y, 0f, extentH);
            float arm = cell * 0.4f;
            var spVerts = new List<Vector3>
            {
                new(sx - arm, sy, layerZ), new(sx + arm, sy, layerZ),
                new(sx, sy - arm, layerZ), new(sx, sy + arm, layerZ)
            };
            bool depthSp = GL.IsEnabled(Const.GL_DEPTH_TEST);
            GL.Disable(Const.GL_DEPTH_TEST);
            Terrains.TerrainChunk.DrawLineSegments(spVerts, new Vector3(0.2f, 0.95f, 1f), camera, 0.95f);
            if (depthSp)
                GL.Enable(Const.GL_DEPTH_TEST);
        }

        // Restore main shader
        GL.UseProgram(Shader.GetShaderProgram());
    }

    public void Dispose()
    {
        if (_textureID != 0)
        {
            fixed (uint* texPtr = &_textureID)
            {
                GL.DeleteTextures(1, texPtr);
            }
            _textureID = 0;
        }
        if (_map2dTilesetTex != 0)
        {
            uint t = _map2dTilesetTex;
            GL.DeleteTextures(1, &t);
            _map2dTilesetTex = 0;
        }
        if (_map2dVAO != 0) { uint v = _map2dVAO; GL.DeleteVertexArrays(1, &v); _map2dVAO = 0; }
        if (_map2dVBO != 0) { uint v = _map2dVBO; GL.DeleteBuffers(1, &v); _map2dVBO = 0; }
        if (_player2dVAO != 0) { uint v = _player2dVAO; GL.DeleteVertexArrays(1, &v); _player2dVAO = 0; }
        if (_player2dVBO != 0) { uint v = _player2dVBO; GL.DeleteBuffers(1, &v); _player2dVBO = 0; }
        _terrainMesh?.Dispose();
        _terrainMesh = null;
        DisposePbrTextures();
        _object3D = null;
    }
}
