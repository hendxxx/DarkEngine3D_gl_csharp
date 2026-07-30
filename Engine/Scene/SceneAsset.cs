using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public string FontPath { get; set; } = "Artifacts\\\\\\\\fonts\\\\\\\\Worldstar.ttf";

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
    public bool AutoCenter { get; set; } = false;
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
}
