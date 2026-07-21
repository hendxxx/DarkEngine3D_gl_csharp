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
    public string Type { get; set; } = "Button"; // Scene, Container, Button, Label, Dialog
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

    // Behavior string — mapped to actual Action<> at runtime
    public string ClickBehavior { get; set; } = "";
    public string HoverEnterBehavior { get; set; } = "";
    public string HoverExitBehavior { get; set; } = "";

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
}
