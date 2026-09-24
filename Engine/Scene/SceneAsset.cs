using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Visual;

namespace DarkEngine3D_gl_csharp.Engine.Scene;

/// <summary>
/// Serializable representation of a UI element for .ing scene files.
/// This is the data format used by the IDE scene editor — NOT the game save system.
/// A .ing file contains one or more SceneAsset definitions.
/// </summary>
public class SceneElementData
{
    public string Name { get; set; } = "Element";
    public string Type { get; set; } = "Button"; // Scene, Container, Button, Label, SliderNumber, SliderText, Checkbox, Dropdown, TextBox
    public string Text { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 200f;
    public float Height { get; set; } = 50f;

    // ── Image (replaces Text/Font when set) ──
    public string ImagePath { get; set; } = "";
    public string ImageMode { get; set; } = "Stretch"; // Stretch, Zoom, Fill

    public float FontSize { get; set; } = 13f;
    public string FontPath { get; set; } = "Artifacts\\fonts\\Worldstar.ttf";

    // Colors as arrays [r, g, b] — Vector3 is not directly serializable
    public float[] TextColor { get; set; } = [0.95f, 0.95f, 1f];
    public float[] BgColor { get; set; } = [0.10f, 0.12f, 0.18f];
    public float[] BorderColor { get; set; } = [0.15f, 0.18f, 0.25f];
    public float[] HoverTextColor { get; set; } = [0.95f, 0.95f, 1f];
    public float[] HoverBgColor { get; set; } = [0.22f, 0.28f, 0.45f];
    public float[] HoverBorderColor { get; set; } = [0.5f, 0.6f, 1.0f];

    public string Alignment { get; set; } = "Center";
    public bool IsVisible { get; set; } = true;
    public float Opacity { get; set; } = 1f;
    public bool AutoFillWindow { get; set; } = false;
    public bool AutoCenterX { get; set; } = false;
    public bool AutoCenterY { get; set; } = false;
    /// <summary>Anchor position: None, TopLeft, TopCenter, etc.</summary>
    public string Anchor { get; set; } = "None";
    public bool UseHover { get; set; } = true;

    // Behavior string — mapped to actual Action<> at runtime
    public string ClickBehavior { get; set; } = "";
    public string HoverEnterBehavior { get; set; } = "";
    public string HoverExitBehavior { get; set; } = "";

    // ── SliderNumber / SliderText properties ──
    public float MinValue { get; set; } = 0f;
    public float MaxValue { get; set; } = 100f;
    public float Step { get; set; } = 1f;
    public float CurrentValue { get; set; } = 50f;
    public List<string> TextOptions { get; set; } = ["Option A", "Option B", "Option C"];
    public int SelectedTextIndex { get; set; } = 0;

    // ── Checkbox properties ──
    public bool IsChecked { get; set; } = false;

    // ── Dropdown properties ──
    public List<string> Options { get; set; } = ["Option 1", "Option 2", "Option 3"];
    public int SelectedIndex { get; set; } = 0;

    // ── Fallback text when image fails to load ──
    public string FallbackText { get; set; } = "";
    public bool WordWrap { get; set; } = true;
    public string TriggeredByKeyboardButton { get; set; } = "";

    // ── TextBox properties ──
    public string Placeholder { get; set; } = "Enter text...";
    public int MaxLength { get; set; } = 0;
    public string InputText { get; set; } = "";

    // ── Placeholder (scrollable container) properties ──
    public float ScrollY { get; set; } = 0f;
    public float ScrollBarWidth { get; set; } = 10f;
    public float[] ScrollBarTrackColor { get; set; } = [0.15f, 0.15f, 0.20f];
    public float[] ScrollBarThumbColor { get; set; } = [0.45f, 0.45f, 0.55f];
    public float[] ScrollBarThumbHoverColor { get; set; } = [0.55f, 0.55f, 0.65f];

    // ════════════════════════════════════════════════
    //  Visual Style Properties (for type-specific rendering)
    // ════════════════════════════════════════════════

    // ── Slider visual style ──
    public float[] SliderTrackColor { get; set; } = [0.30f, 0.30f, 0.35f];
    public float[] SliderFillColor { get; set; } = [0.3f, 0.6f, 1.0f];
    public float[] SliderThumbColor { get; set; } = [0.9f, 0.9f, 1.0f];
    public float[] SliderThumbBorderColor { get; set; } = [0.3f, 0.6f, 1.0f];
    public float SliderThumbSize { get; set; } = 14f;
    public float SliderTrackHeight { get; set; } = 6f;
    /// <summary>Position of the slider value label (0=None, 1=Left, 2=Right, 3=Top, 4=Bottom).</summary>
    public int SliderLabelPosition { get; set; } = 1;
    public float SliderLabelSpacing { get; set; } = 8f;
    public float LabelSpacing { get; set; } = 8f;

    // ── Checkbox visual style ──
    public float[] CheckmarkColor { get; set; } = [0.9f, 0.9f, 1.0f];
    public float[] CheckedBgColor { get; set; } = [0.25f, 0.55f, 1.0f];
    public float[] UncheckedBgColor { get; set; } = [0.15f, 0.15f, 0.22f];

    // ── Dropdown visual style ──
    public float[] ArrowColor { get; set; } = [0.5f, 0.5f, 0.7f];

    // ── TextBox visual style ──
    public float[] CursorColor { get; set; } = [0.5f, 0.8f, 1.0f];

    // ── RadioButton visual style ──
    public float[] RadioSelectedColor { get; set; } = [0.3f, 0.7f, 1.0f];
    public float[] RadioSelectedBgColor { get; set; } = [0.2f, 0.4f, 0.65f];
    public float[] RadioUnselectedBgColor { get; set; } = [0.15f, 0.15f, 0.22f];
    public string RadioGroup { get; set; } = "default";

    // ── Bar visual style ──
    public string BarBackgroundPath { get; set; } = "";
    public string BarEmptyPath { get; set; } = "";
    public string BarProgressPath { get; set; } = "";
    /// <summary>Solid fill color of each Bar layer (RGB 0..1).</summary>
    public float[] BarBgColor { get; set; } = [0.10f, 0.10f, 0.14f];
    public float[] BarEmptyColor { get; set; } = [0.05f, 0.05f, 0.08f];
    public float[] BarProgressColor { get; set; } = [0.30f, 0.70f, 1.00f];
    /// <summary>Legacy shared inset — migrated into the per-layer offsets on load.</summary>
    public float BarInset { get; set; } = 0f;
    /// <summary>0 = Left→Right, 1 = Right→Left, 2 = Bottom→Top, 3 = Top→Bottom.</summary>
    public int BarDirection { get; set; } = 0;
    /// <summary>Player2DStats slot this bar mirrors (None = manual CurrentValue).</summary>
    public string BarStatBinding { get; set; } = "None";

    // ── Bar per-layer edge offsets (scene px, + = outward, − = inward) ──
    public float BarBgOffsetLeft { get; set; }
    public float BarBgOffsetRight { get; set; }
    public float BarBgOffsetTop { get; set; }
    public float BarBgOffsetBottom { get; set; }
    public float BarEmptyOffsetLeft { get; set; }
    public float BarEmptyOffsetRight { get; set; }
    public float BarEmptyOffsetTop { get; set; }
    public float BarEmptyOffsetBottom { get; set; }
    public float BarProgOffsetLeft { get; set; }
    public float BarProgOffsetRight { get; set; }
    public float BarProgOffsetTop { get; set; }
    public float BarProgOffsetBottom { get; set; }

    // Recursive children
    public List<SceneElementData> Children { get; set; } = [];
}

/// <summary>
/// Serializable data for a 3D background object in a scene (e.g. MainMenu).
/// Stored in the SceneAsset.BackgroundObjects list.
/// </summary>
public class BackgroundObjectData
{
    public string Name { get; set; } = "BackgroundModel";
    public string ModelPath { get; set; } = "";
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float RotationYaw { get; set; }  // degrees
    public float Scale { get; set; } = 1f;
    public bool IsVisible { get; set; } = true;
}

/// <summary>
/// Serializable data for an editor-placed 3D primitive (Box, Sphere, Plane).
/// Stored in the SceneAsset.EditorObjects list for save/load persistence.
/// </summary>
public class EditorObjectData
{
    public string Name { get; set; } = "EditorObject";
    public string PrimitiveType { get; set; } = "Box"; // Box, Sphere, Plane, GlbReference, Map2D
    /// <summary>Serialized tilemap payload — only used when PrimitiveType == Map2D.
    /// Keeping the level inside the scene file means a scene only shows its level when
    /// the .ing actually contains it (no blanket auto-load).</summary>
    public Visual.Tilemap2DData? Tilemap { get; set; }
    public bool TilemapShowGrid { get; set; } = true;
    /// <summary>Whether the map's trigger areas render as editor aids (amber boxes).</summary>
    public bool TilemapShowTriggers { get; set; } = true;
    public float TilemapGridColorR { get; set; } = 0.4f;   // 3D map grid overlay color
    public float TilemapGridColorG { get; set; } = 0.5f;
    public float TilemapGridColorB { get; set; } = 0.68f;
    public float TilemapGridColorA { get; set; } = 0.5f;
    /// <summary>GLB model path (only used when PrimitiveType == GlbReference). Stored relative to the exe.</summary>
    public string GlbFilePath { get; set; } = "";
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public float RotX { get; set; }  // degrees
    public float RotY { get; set; }
    public float RotZ { get; set; }
    public float ScaleX { get; set; } = 1f;
    public float ScaleY { get; set; } = 1f;
    public float ScaleZ { get; set; } = 1f;
    public float ColorR { get; set; } = 0.8f;
    public float ColorG { get; set; } = 0.8f;
    public float ColorB { get; set; } = 0.9f;
    public bool CastShadow { get; set; } = true;
    public bool IsVisible { get; set; } = true;

    // ── NPC dialogue binding (Dialogue System) ──
    /// <summary>Dialogue asset id (Dialogue Editor) started when the player presses
    /// E within range. Empty = not an NPC. Persisted per object.</summary>
    public string NpcDialogueId { get; set; } = "";
    /// <summary>Optional alert/exclamation image drawn above the NPC instead of the
    /// text "!" bubble. Empty = default text marker. Persisted per object.</summary>
    public string NpcAlertImagePath { get; set; } = "";

    // ── Player2D (animated sprite + capsule collider) ──
    public string Player2DSpriteSheet { get; set; } = "";
    public string Player2DAnimationClip { get; set; } = "";
    public string Player2DWalkSheet { get; set; } = "";
    public string Player2DWalkClip { get; set; } = "";
    public float Player2DHeight { get; set; } = 2f;
    public float Player2DCapsuleRadius { get; set; } = 0.35f;
    public float Player2DCapsuleHeight { get; set; } = 1.8f;
    public float Player2DCapsuleOffsetX { get; set; } = 0f;
    public float Player2DCapsuleOffsetY { get; set; } = 0f;
    public bool Player2DShowCapsule { get; set; } = true;
    public float Player2DGravity { get; set; } = 25f;
    // ── Player2D movement tuning (Inspector-editable, used by Player2DSystem) ──
    public float Player2DMoveSpeed { get; set; } = 5f;
    public float Player2DRunSpeed { get; set; } = 9f;
    public float Player2DJumpForce { get; set; } = 11f;
    public float Player2DGravityScale { get; set; } = 1f;
    public float Player2DAcceleration { get; set; } = 60f;
    public float Player2DDeceleration { get; set; } = 80f;
    public float Player2DAirControl { get; set; } = 0.65f;
    // ── Platformer jump feel ──
    public float Player2DCoyoteTime { get; set; } = 0.1f;
    public float Player2DJumpBuffer { get; set; } = 0.12f;
    public float Player2DJumpCutMultiplier { get; set; } = 0.5f;
    // ── Sprite2D decorative sprite (Player2D fields reused for sheet/clip/height) ──
    public bool Sprite2DLoop { get; set; } = true;
    public float Sprite2DSpeed { get; set; } = 1f;
    public float Sprite2DStartOffset { get; set; } = 0f;
    public bool Sprite2DFacingRight { get; set; } = true;
    /// <summary>Per-sprite emissive boost (0 = none, 1 = full) — drives per-sprite
    /// bloom. Player objects use Player2DGlow.</summary>
    public float Sprite2DGlow { get; set; } = 0f;
    public float Player2DGlow { get; set; } = 0f;
    /// <summary>Glow color tint components (0-1). The brightest channel is
    /// normalized to 1 at render time; white (1,1,1) = natural sprite colors.
    /// Serialized per-component (X/Y/Z) like the rest of this class.</summary>
    public float Sprite2DGlowColorX { get; set; } = 1f;
    public float Sprite2DGlowColorY { get; set; } = 1f;
    public float Sprite2DGlowColorZ { get; set; } = 1f;
    public float Player2DGlowColorX { get; set; } = 1f;
    public float Player2DGlowColorY { get; set; } = 1f;
    public float Player2DGlowColorZ { get; set; } = 1f;
    /// <summary>Organic flicker for the per-sprite glow (fire breathing). Player
    /// objects use Player2DGlowFlicker.</summary>
    public bool Sprite2DGlowFlicker { get; set; }
    public bool Player2DGlowFlicker { get; set; }
    /// <summary>Render layer: higher layers draw on top (and 0.01 units nearer the camera per step).</summary>
    public int Sprite2DRenderLayer { get; set; } = 0;
    // ── Player2D camera-follow tuning ──
    public float CameraFollowSpeed { get; set; } = 6f;
    public float CameraDeadZoneWidth { get; set; } = 96f;
    public float CameraDeadZoneHeight { get; set; } = 64f;
    public float CameraVerticalThreshold { get; set; } = 64f;
    public float CameraReturnSpeed { get; set; } = 3f;
    public float CameraLookAhead { get; set; } = 150f;
    /// <summary>Optional per-camera 2D view offset (persisted with the object).</summary>
    public Vector3 CameraViewOffset { get; set; } = new(0, 0, 0);
    /// <summary>Animation actions (name + sheet/clip + key binding + priority). Persisted
    /// via the object data so designer-built action sets survive reloads.</summary>
    public List<Player2DActionData>? Actions { get; set; }

    // ── Type-specific properties (Camera / Light / Sky) ──
    /// <summary>Vertical FOV in degrees (Camera).</summary>
    public float CameraFov { get; set; } = 60f;
    /// <summary>Near clip distance (Camera).</summary>
    public float CameraNear { get; set; } = 0.1f;
    /// <summary>Far clip distance (Camera).</summary>
    public float CameraFar { get; set; } = 500f;
    /// <summary>Light direction (Light).</summary>
    public float LightDirX { get; set; } = -0.5f;
    public float LightDirY { get; set; } = 0.8f;
    public float LightDirZ { get; set; } = -0.3f;
    /// <summary>Light intensity multiplier (Light).</summary>
    public float LightIntensity { get; set; } = 1f;
    /// <summary>Light type: Direct / Point / Spotlight (Light). Default Direct (sun-like).</summary>
    public int LightType { get; set; } = 0;
    /// <summary>Spotlight cone half-angle in degrees (Light, Spotlight).</summary>
    public float LightConeAngle { get; set; } = 30f;
    /// <summary>Point/Spot falloff range in world units (Light, Point/Spotlight).</summary>
    public float LightPointRadius { get; set; } = 50f;
    /// <summary>Time of day in hours 0..24 (Sky).</summary>
    public float SkyTimeOfDay { get; set; } = 12f;
    /// <summary>Sun elevation override in degrees (Sky). null = follow time of day.</summary>
    public float? SkySunPitch { get; set; }
    /// <summary>Sun azimuth override in degrees (Sky). null = follow time of day.</summary>
    public float? SkySunYaw { get; set; }
    /// <summary>Cloud coverage 0..1 (Sky).</summary>
    public float SkyCloudCoverage { get; set; } = 0.3f;
    /// <summary>Sun brightness multiplier (Sky).</summary>
    public float SkySunIntensity { get; set; } = 1f;
    /// <summary>Time-of-day animation speed in hours/second (Sky, 0 = static).</summary>
    public float SkyTimeAnimSpeed { get; set; } = 0f;
    /// <summary>Pause the time-of-day animation (Sky).</summary>
    public bool SkyTimeAnimPaused { get; set; } = false;
    /// <summary>Whether the camera view-frustum gizmo is shown (Camera).</summary>
    public bool ShowFrustum { get; set; } = true;
    /// <summary>Whether the light direction/cone gizmo is shown (Light).</summary>
    public bool ShowLightGizmo { get; set; } = true;
    /// <summary>Whether the sky horizon/sun gizmo is shown (Sky).</summary>
    public bool ShowSkyGizmo { get; set; } = true;
    /// <summary>Master sky settings (3 types: Procedural/Skybox/Dome). Serialized with the scene.</summary>
    public SkySettings? SkySettings { get; set; }

    // ── Per-object gizmo pivot override (nullable — null means use object position) ──
    public float? PivotOverrideX { get; set; }
    public float? PivotOverrideY { get; set; }
    public float? PivotOverrideZ { get; set; }


    /// <summary>PBR material maps (Box/Sphere/flat plane) — relative to the exe.</summary>
    public string PbrAlbedoPath { get; set; } = "";
    public string PbrNormalPath { get; set; } = "";
    public string PbrMetallicPath { get; set; } = "";
    public string PbrRoughnessPath { get; set; } = "";
    public string PbrAoPath { get; set; } = "";
    public string PbrHeightPath { get; set; } = "";
    /// <summary>Terrain elevation heightmap (base shape — vertex displacement).
    /// SEPARATE from PbrHeightPath (POM surface detail). Legacy scenes may leave this
    /// empty and keep the heightmap in the PBR height slot (plane-only migration).</summary>
    public string TerrainHeightPath { get; set; } = "";

    public string PbrEmissionPath { get; set; } = "";
    public float PbrTexTiling { get; set; } = 1f;
    /// <summary>Steep POM height-map displacement depth (0 = off).</summary>
    public float PbrParallaxScale { get; set; } = 0.15f;
    /// <summary>Relief self-shadowing strength for the height-map POM (0 = off, 1 = hard).</summary>
    public float PbrPomShadowStrength { get; set; } = 0.6f;
    /// <summary>Marmoset-style height calibration: contrast (1 = off).</summary>
    public float PbrHeightContrast { get; set; } = 1f;
    /// <summary>Marmoset-style height calibration: contrast center (mid gray).</summary>
    public float PbrHeightContrastCenter { get; set; } = 0.5f;
    /// <summary>Marmoset-style height calibration: whole-field offset (−0.5..0.5).</summary>
    public float PbrHeightOffset { get; set; } = 0f;
    /// <summary>Marmoset-style height calibration: baseline gray treated as zero depth.</summary>
    public float PbrHeightScaleCenter { get; set; } = 0.5f;
    /// <summary>Legacy PBR vertex displacement height (0.15 default). Planes driven by
    /// a terrain elevation map use <see cref="TerrainHeightScale"/> instead.</summary>
    public float PbrVertexDisplaceScale { get; set; } = 0.15f;
    /// <summary>Legacy PBR vertex displacement offset. Planes driven by a terrain
    /// elevation map use <see cref="TerrainHeightOffset"/> instead.</summary>
    public float PbrVertexOffset { get; set; } = 0f;
    /// <summary>BASE heightmap amplitude (world units) — the terrain SHAPE slider.
    /// Absent = legacy scene → falls back to <see cref="PbrVertexDisplaceScale"/>.</summary>
    public float TerrainBaseHeight { get; set; } = -1f;
    /// <summary>VERTEX DISPLACEMENT height (world units) — extra relief on top of the
    /// base shape. Absent = legacy scene → 0 (pure base, no behavior change).</summary>
    public float TerrainHeightScale { get; set; } = -1f;
    /// <summary>Terrain BASE-SHAPE offset (world units). Absent = legacy →
    /// <see cref="PbrVertexOffset"/>.</summary>
    public float TerrainHeightOffset { get; set; } = -1f;
    /// <summary>Terrain relief intensity (0 = flat, 1 = as-authored, up to 3 = steep).
    /// Absent = legacy scene → 1 (as-authored).</summary>
    public float TerrainHeightStrength { get; set; } = -1f;
    /// <summary>Terrain elevation map's OWN tiling (X/Y) — decoupled from the PBR
    /// per-map tiling and the global Map Tiling. Absent = legacy scene → 1×1.</summary>
    public float TerrainHeightTilingX { get; set; } = 1f;
    public float TerrainHeightTilingY { get; set; } = 1f;
    /// <summary>ADDITIVE sculpt delta map (0.5-neutral grayscale TGA — brush edits
    /// ride ON TOP of the base heightmap; the base image file is never written).
    /// Empty = never sculpted. Absent = legacy scene → none.</summary>
    public string SculptDeltaPath { get; set; } = "";
    /// <summary>Sculpt delta amplitude (world units): (delta − 0.5)·2·this is added
    /// to the base elevation. Absent = legacy scene → 2.</summary>
    public float TerrainSculptAmp { get; set; } = -1f;
    /// <summary>Terrain SPLAT weight map (RGBA TGA — R/G/B/A = layers 0-3) written
    /// by the viewport paint brush. Empty = never painted (weights derive from the
    /// height bands / default layer 0). Absent = legacy scene → no splat.</summary>
    public string SplatMapPath { get; set; } = "";
    /// <summary>Splat texture layers 1-3 (layer 0 IS the base PBR material, its maps
    /// live in the Pbr*Path fields above).</summary>
    public string?[] SplatLayerAlbedo { get; set; }
    public string?[] SplatLayerNormal { get; set; }
    public string?[] SplatLayerMetallic { get; set; }
    public string?[] SplatLayerRoughness { get; set; }
    public string?[] SplatLayerAo { get; set; }
    public string?[] SplatLayerHeight { get; set; }
    /// <summary>Per-layer fallback tints when the layer's albedo slot is empty.</summary>
    public float[]? SplatLayerTints { get; set; }   // 12 floats: 4 layers × RGB
    /// <summary>AUTO height bands over the sculpted elevation (weights per layer).</summary>
    public bool SplatHeightBandsEnabled { get; set; } = false;
    public int SplatHeightLayerCount { get; set; } = -1;      // absent = legacy → 4
    public float SplatHeightLayerFeather { get; set; } = -1f; // absent = legacy → 2
    /// <summary>Per-layer band edges (world units): 8 floats — L0.low, L0.high, L1.low, …</summary>
    public float[]? SplatHeightBands { get; set; }
    /// <summary>Tessellation segments per side for the displaced plane grid (16..512).
    /// Null/absent = legacy scene → <see cref="EditorObject.PbrDisplaceSegments"/> (256).</summary>
    public int? PbrVertexSegments { get; set; }
    /// <summary>Split the displaced grid into chunk×chunk vertex blocks (1..16), each
    /// frustum-culled independently at draw time. Null/absent = 1 (single mesh).</summary>
    public int? PbrVertexChunk { get; set; }
    /// <summary>Sampling settings for the SIMPLE texture (min/mag filter, mipmapping,
    /// anisotropy, wrapping, UV tiling/offset). Null/absent = legacy scene → defaults
    /// (tiling falls back to <see cref="PbrTexTiling"/>).</summary>
    public TextureSettingsData? TexSettings { get; set; }
    /// <summary>Per-PBR-map sampling settings (7 entries: albedo, normal, metallic,
    /// roughness, ao, height, emission). Null/absent = legacy scene → each map falls back
    /// to <see cref="TexSettings"/> (or default).</summary>
    public TextureSettingsData[]? PbrTexSettings { get; set; }


}




/// <summary>
/// A complete scene definition stored in a .ing file.
/// One .ing file can contain multiple scene definitions (MainMenu, Loading, GameScene).
/// </summary>
public class SceneAsset
{
    public string SceneName { get; set; } = "Untitled";
    public string Description { get; set; } = "";
    /// <summary>Scene type: "MainMenu", "GameScene", "Loading" (persists editor type choice).</summary>
    public string SceneType { get; set; } = "MainMenu";
    /// <summary>Top-level UI elements (usually one Scene-type root per definition).</summary>
    public List<SceneElementData> Elements { get; set; } = [];
    /// <summary>3D background objects to render behind the UI (models, position, scale).</summary>
    public List<BackgroundObjectData> BackgroundObjects { get; set; } = [];
    /// <summary>Editor-placed 3D primitives (Box, Sphere, Plane) — saved per scene.</summary>
    public List<EditorObjectData> EditorObjects { get; set; } = [];

    // ── Transition metadata (optional) ──
    /// <summary>Transition type: "fade", "slideleft", "slideright" (optional).</summary>
    public string TransitionType { get; set; } = "fade";
    /// <summary>Transition duration in seconds.</summary>
    public float TransitionDuration { get; set; } = 0.6f;
    /// <summary>Transition color [r,g,b].</summary>
    public float[] TransitionColor { get; set; } = [0f, 0f, 0f];
    /// <summary>Easing: "linear", "easein", "easeout", "easeinout".</summary>
    public string TransitionEasing { get; set; } = "linear";
    /// <summary>Whether the transition should block input while active.</summary>
    public bool TransitionBlockInput { get; set; } = false;

    // ── Freefly camera (per scene, so each scene remembers its own view) ──
    /// <summary>Editor camera position [x, y, z] — null = keep the default camera.</summary>
    public float[]? EditorCameraPosition { get; set; }
    /// <summary>Editor camera yaw (degrees).</summary>
    public float? EditorCameraYaw { get; set; }
    /// <summary>Editor camera pitch (degrees).</summary>
    public float? EditorCameraPitch { get; set; }

    // ── Per-scene render properties (background color, wireframe, VSync, etc.) ──
    /// <summary>Serialized render properties. Null = use engine defaults.</summary>
    public SceneRenderPropertiesData? RenderProperties { get; set; }
}

/// <summary>
/// Serializable representation of SceneRenderProperties for .ing file persistence.
/// </summary>
public class SceneRenderPropertiesData
{
    public float[] BackgroundColor { get; set; } = [0f, 0f, 0f];
    public bool VSync { get; set; } = true;
    public string FaceCulling { get; set; } = "Back"; // None, Back, Front, FrontAndBack
    public string FrontFaceWinding { get; set; } = "CCW"; // CCW, CW
    public bool WireframeMode { get; set; } = false;
    public bool DepthTest { get; set; } = true;
    public bool Blending { get; set; } = false;
}

/// <summary>
/// Container for all scene definitions in game.ing or individual .ing files.
/// </summary>
public class SceneManifest
{
    /// <summary>Version stamp for forward compatibility.</summary>
    public string Version { get; set; } = "1.0";
    /// <summary>All scenes defined in this file.</summary>
    public List<SceneAsset> Scenes { get; set; } = [];

    // ── Global IDE settings (persisted with the .ing file) ──

    /// <summary>Selection highlight color for 3D objects [r, g, b]. Default: gold (1, 0.8, 0.1).</summary>
    public float[] SelectionHighlightColor { get; set; } = [1f, 0.8f, 0.1f];
    /// <summary>Selection highlight color for editor objects [r, g, b]. Default: cyan (0.1, 0.8, 1.0).</summary>
    public float[] EditorObjectHighlightColor { get; set; } = [0.1f, 0.8f, 1.0f];

    // ── LEGACY: pre-per-scene saves stored the freefly camera globally on the manifest.
    // Kept only so old .ing files can still restore a camera; new saves store it per scene.
    /// <summary>Legacy global editor camera position [x, y, z] (old .ing format).</summary>
    public float[]? EditorCameraPosition { get; set; }
    /// <summary>Legacy global editor camera yaw (degrees).</summary>
    public float? EditorCameraYaw { get; set; }
    /// <summary>Legacy global editor camera pitch (degrees).</summary>
    public float? EditorCameraPitch { get; set; }
}

/// <summary>Serializable form of <see cref="Player2DAction"/> (animation action binding).</summary>
public class Player2DActionData
{
    public string Name { get; set; } = "";
    public string SpriteSheet { get; set; } = "";
    public string Clip { get; set; } = "";
    public bool Loop { get; set; } = true;
    public bool StopOnFrameEnd { get; set; }
    public int Priority { get; set; } = 5;
    public string KeyBinding { get; set; } = "None";
    /// <summary>"KeyDown" (fire on press), "KeyUp" (fire on release), "KeyDownOnce"
    /// (fire once on press, requires release to fire again), or "KeyUpOnce"
    /// (fire once on release, requires re-press to fire again). Default keeps
    /// old scenes behaving exactly as before.</summary>
    public string KeyTrigger { get; set; } = "KeyDown";
}
