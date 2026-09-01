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
    public int SliderLabelPosition { get; set; } = 2;

    // ── Checkbox visual style ──
    public float[] CheckmarkColor { get; set; } = [0.9f, 0.9f, 1.0f];
    public float[] CheckedBgColor { get; set; } = [0.25f, 0.55f, 1.0f];
    public float[] UncheckedBgColor { get; set; } = [0.15f, 0.15f, 0.22f];

    // ── Dropdown visual style ──
    public float[] ArrowColor { get; set; } = [0.5f, 0.5f, 0.7f];

    // ── TextBox visual style ──
    public float[] CursorColor { get; set; } = [0.5f, 0.8f, 1.0f];

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
    public string PrimitiveType { get; set; } = "Box"; // Box, Sphere, Plane, GlbReference
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

    // ── Advanced terrain (Plane) ──
    /// <summary>Whether this plane renders as an advanced heightmapped terrain.</summary>
    public bool TerrainEnabled { get; set; } = false;
    /// <summary>Heightmap file path (.raw / image).</summary>
    public string TerrainHeightmapPath { get; set; } = "";
    /// <summary>Grid resolution per side.</summary>
    public int TerrainChunkSize { get; set; } = 32;
    /// <summary>Chunk sub-meshes per side (1..8).</summary>
    public int TerrainChunksPerSide { get; set; } = 1;
    /// <summary>Vertical height scale (world units).</summary>
    public float TerrainHeightScale { get; set; } = 30f;
    /// <summary>Slope threshold for the dirt/cliff layer.</summary>
    public float TerrainSlopeThreshold { get; set; } = 0.35f;
    /// <summary>World-space texture tiling.</summary>
    public float TerrainTexTiling { get; set; } = 0.5f;
    /// <summary>Texture tiling for steep slope/cliff surfaces.</summary>
    public float TerrainSlopeTexTiling { get; set; } = 0.3f;
    /// <summary>Parallax occlusion mapping strength (0 = off).</summary>
    public float TerrainParallaxScale { get; set; } = 0.0f;
    /// <summary>POM ray-march steps (8-32).</summary>
    public int TerrainPomSteps { get; set; } = 16;
    /// <summary>Stochastic (random per-tile) sampling toggle — OFF by default.</summary>
    public bool TerrainUseStochasticSampling { get; set; } = false;
    /// <summary>Normalized height bands for the 4 layers.</summary>
    public float TerrainLayerAirTop { get; set; } = 0.18f;
    public float TerrainLayerDirtTop { get; set; } = 0.45f;
    public float TerrainLayerGrassTop { get; set; } = 0.75f;
    public float TerrainLayerSnowTop { get; set; } = 1.0f;
    /// <summary>Legacy layer texture paths (Layer 1-4 + slope).</summary>
    public string TerrainTextureAirPath { get; set; } = "";
    public string TerrainTextureDirtPath { get; set; } = "";
    public string TerrainTextureGrassPath { get; set; } = "";
    public string TerrainTextureSnowPath { get; set; } = "";
    /// <summary>Slope / cliff texture — applied to steep faces (replaces dirt on cliffs).</summary>
    public string TerrainTextureSlopePath { get; set; } = "";
    // ── PBR map tuning (global per map type, applies to all layers) ──
    public float TerrainPbrAlbedoBrightness { get; set; } = 1f;
    public float TerrainPbrAlbedoSaturation { get; set; } = 1f;
    public float TerrainPbrAlbedoContrast { get; set; } = 1f;
    public float TerrainPbrNormalStrength { get; set; } = 1f;
    public float TerrainPbrNormalBlur { get; set; } = 0f;
    public float TerrainPbrMetallicThreshold { get; set; } = 0.5f;
    public float TerrainPbrMetallicSoftness { get; set; } = 0.1f;
    public float TerrainPbrMetallicStrength { get; set; } = 1f;
    public float TerrainPbrRoughnessStrength { get; set; } = 1f;
    public bool TerrainPbrRoughnessInvert { get; set; } = false;
    public float TerrainPbrAoStrength { get; set; } = 1f;
    public float TerrainPbrAoBrightness { get; set; } = 0f;
    public float TerrainPbrHeightStrength { get; set; } = 1f;
    public bool TerrainPbrHeightInvert { get; set; } = false;
    public float TerrainPbrHeightBlur { get; set; } = 0f;
    public float TerrainPbrEmissionIntensity { get; set; } = 1f;
    /// <summary>Per-layer PBR data — 6 companion maps + unique tuning per terrain layer
    /// (PBR is per texture). Null/absent = legacy scene: layers auto-discover maps next to
    /// their albedo and use the global tuning values above.</summary>
    public TerrainPbrLayerData[]? TerrainLayers { get; set; }
    /// <summary>PBR material maps (Box/Sphere/flat plane) — relative to the exe.</summary>
    public string PbrAlbedoPath { get; set; } = "";
    public string PbrNormalPath { get; set; } = "";
    public string PbrMetallicPath { get; set; } = "";
    public string PbrRoughnessPath { get; set; } = "";
    public string PbrAoPath { get; set; } = "";
    public string PbrHeightPath { get; set; } = "";
    public string PbrEmissionPath { get; set; } = "";
    public float PbrTexTiling { get; set; } = 1f;
    /// <summary>Sampling settings for the SIMPLE texture (min/mag filter, mipmapping,
    /// anisotropy, wrapping, UV tiling/offset). Null/absent = legacy scene → defaults
    /// (tiling falls back to <see cref="PbrTexTiling"/>).</summary>
    public TextureSettingsData? TexSettings { get; set; }
    /// <summary>Per-PBR-map sampling settings (7 entries: albedo, normal, metallic,
    /// roughness, ao, height, emission). Null/absent = legacy scene → each map falls back
    /// to <see cref="TexSettings"/> (or default).</summary>
    public TextureSettingsData[]? PbrTexSettings { get; set; }
    /// <summary>Per-terrain-layer sampling settings (4 entries: air, dirt, grass, snow).
    /// Null/absent = legacy scene → each layer falls back to <see cref="TexSettings"/>.</summary>
    public TextureSettingsData[]? TerrainLayerSettings { get; set; }
    /// <summary>Brush radius in world units (viewport paint tool).</summary>
    public float TerrainBrushSize { get; set; } = 10f;
    /// <summary>Height delta per painted frame (world units).</summary>
    public float TerrainBrushStrength { get; set; } = 1f;
    /// <summary>Brush edge falloff 0..1.</summary>
    public float TerrainBrushSoftness { get; set; } = 1f;
    /// <summary>Brush falloff curve: 0=Linear, 1=Smooth, 2=Sharp, 3=Spherical, 4=Soft.</summary>
    public int TerrainBrushFalloff { get; set; } = 1;
    /// <summary>Base64-encoded painted heightmap blob (only set after brush edits, so
    /// brush paint survives scene save/load without touching the source .raw file).</summary>
    public string TerrainPaintedData { get; set; } = "";
    /// <summary>Layer drawn by the paint brush (0-3).</summary>
    public int TerrainPaintLayerIndex { get; set; } = 0;
    /// <summary>Weight added to the painted layer per brush stamp (0..1).</summary>
    public float TerrainPaintStrength { get; set; } = 0.45f;
    /// <summary>Base64-encoded manual layer-paint splat blob (empty = no manual paint).
    /// Persisted so layer paint survives scene save/load.</summary>
    public string TerrainSplatData { get; set; } = "";
    /// <summary>Brush ring highlight color [r, g, b] — user-editable, saved with the scene.</summary>
    public float[]? TerrainBrushColor { get; set; }
    /// <summary>Brush ring highlight transparency 0..1 — user-editable, saved with the scene.</summary>
    public float TerrainBrushAlpha { get; set; } = 0.35f;

    // ── Per-paint-layer textures (independent from terrain auto-layers) ──
    public string[] PaintLayerTextures { get; set; } = ["", "", "", ""];
    public float[] PaintLayerTilingX { get; set; } = [0.5f, 0.5f, 0.5f, 0.5f];
    public float[] PaintLayerTilingY { get; set; } = [0.5f, 0.5f, 0.5f, 0.5f];
    public bool[] PaintLayerStochastic { get; set; } = [false, false, false, false];
    public int PaintLayerCount { get; set; } = 1;

    // ── Dynamic terrain layers (per-layer texture, tiling, height range, PBR, stochastic) ──
    /// <summary>Saved dynamic terrain layers. Null/empty = migrate from legacy 4-layer on load.</summary>
    public List<TerrainLayer>? TerrainLayerList { get; set; }
    /// <summary>Slope/cliff layer settings (optional override). Null = no slope layer.</summary>
    public TerrainLayer? TerrainSlopeLayer { get; set; }
    /// <summary>Whether the slope layer is enabled.</summary>
    public bool TerrainSlopeEnabled { get; set; } = false;

}


/// <summary>
/// A complete scene definition stored in a .ing file.
/// One .ing file can contain multiple scene definitions (MainMenu, Loading, GameScene).
/// </summary>
public class SceneAsset
{
    public string SceneName { get; set; } = "Untitled";
    public string Description { get; set; } = "";
    /// <summary>Top-level UI elements (usually one Scene-type root per definition).</summary>
    public List<SceneElementData> Elements { get; set; } = [];
    /// <summary>3D background objects to render behind the UI (models, position, scale).</summary>
    public List<BackgroundObjectData> BackgroundObjects { get; set; } = [];
    /// <summary>Editor-placed 3D primitives (Box, Sphere, Plane) — saved per scene.</summary>
    public List<EditorObjectData> EditorObjects { get; set; } = [];

    // ── Freefly camera (per scene, so each scene remembers its own view) ──
    /// <summary>Editor camera position [x, y, z] — null = keep the default camera.</summary>
    public float[]? EditorCameraPosition { get; set; }
    /// <summary>Editor camera yaw (degrees).</summary>
    public float? EditorCameraYaw { get; set; }
    /// <summary>Editor camera pitch (degrees).</summary>
    public float? EditorCameraPitch { get; set; }
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
