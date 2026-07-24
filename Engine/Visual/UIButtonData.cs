using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>Text alignment modes for UI buttons.</summary>
public enum TextAlignment
{
    Left,
    Center,
    Right
}

/// <summary>Image sizing mode for image elements.</summary>
public enum ImageMode
{
    /// <summary>Stretch image to fill element bounds (default).</summary>
    Stretch,
    /// <summary>Fit image within element bounds maintaining aspect ratio (letterbox).</summary>
    Zoom,
    /// <summary>Cover element bounds maintaining aspect ratio (crop overflow).</summary>
    Fill,
}

/// <summary>Type of UI element — determines how it's rendered and displayed in the hierarchy.</summary>
public enum UIElementType
{
    /// <summary>A scene root node (invisible, holds children).</summary>
    Scene,
    /// <summary>A container/panel that can hold child elements.</summary>
    Container,
    /// <summary>A single interactive button.</summary>
    Button,
    /// <summary>A text label (no interaction).</summary>
    Label,
    /// <summary>A dialog box (e.g. confirm dialog with buttons).</summary>
    Dialog,
}

/// <summary>
/// Data-driven UI element. Can be a button, container, label, or dialog.
/// Supports parent-child hierarchy for scene tree display in IDE.
/// Scenes (e.g. MainMenu) own a tree of these and render them each frame.
/// </summary>
public class UIElement
{
    // ── Identity ──
    /// <summary>Unique instance ID (for debugging object recreation). Increments with each new instance.</summary>
    private static int _nextInstanceId = 1;
    public readonly int InstanceId;
    public string Name { get; set; } = "Element";

    public UIElement()
    {
        InstanceId = _nextInstanceId++;
    }
    public UIElementType Type { get; set; } = UIElementType.Button;

    // ── Visual ──
    public string Text { get; set; } = "Element";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 200f;
    public float Height { get; set; } = 50f;

    // ── Image (replaces Text/Font when set) ──
    public string ImagePath { get; set; } = "";
    public ImageMode ImageMode { get; set; } = ImageMode.Stretch;

    // ── Font ──
    public string FontPath { get; set; } = "Artifacts\\\\fonts\\\\Worldstar.ttf";
    public float FontSize { get; set; } = 13f;

    // ── Colors ──
    public Vector3 TextColor { get; set; } = new(0.95f, 0.95f, 1f);
    public Vector3 BgColor { get; set; } = new(0.10f, 0.12f, 0.18f);
    public Vector3 BorderColor { get; set; } = new(0.15f, 0.18f, 0.25f);

    // ── Hover Colors ──
    public Vector3 HoverTextColor { get; set; } = new(0.95f, 0.95f, 1f);
    public Vector3 HoverBgColor { get; set; } = new(0.22f, 0.28f, 0.45f);
    public Vector3 HoverBorderColor { get; set; } = new(0.5f, 0.6f, 1.0f);

    // ── Alignment ──
    public TextAlignment Alignment { get; set; } = TextAlignment.Center;

    // ── Visibility ──
    public bool IsVisible { get; set; } = true;

    // ── Auto-fill window (for overlay/background elements) ──
    public bool AutoFillWindow { get; set; } = false;
    /// <summary>Auto-center this element in the viewport (for overlays/dialogs).</summary>
    public bool AutoCenter { get; set; } = false;
    /// <summary>Whether hover colors are applied on mouse hover.
    /// When false, element always uses normal colors (no hover effect).
    /// Default per type: true for Button, false for Label/Container/Dialog.</summary>
    public bool UseHover { get; set; } = true;

    // ── Runtime state ──
    public bool IsHovered { get; set; }

    // ── Parent-child hierarchy ──
    /// <summary>Parent element (null for root nodes).</summary>
    public UIElement? Parent { get; set; }
    /// <summary>Child elements (for containers, dialogs, etc.).</summary>
    public List<UIElement> Children { get; set; } = [];

    // ── Behaviors ──
    public string ClickBehaviorLabel { get; set; } = "None";
    public string HoverEnterLabel { get; set; } = "None";
    public string HoverExitLabel { get; set; } = "None";

    public Action? OnClick { get; set; }
    public Action? OnHoverEnter { get; set; }
    public Action? OnHoverExit { get; set; }

    /// <summary>Add a child element and set its parent.</summary>
    public UIElement AddChild(UIElement child)
    {
        child.Parent = this;
        Children.Add(child);
        return child;
    }

    /// <summary>Remove all children.</summary>
    public void ClearChildren()
    {
        foreach (var c in Children)
            c.Parent = null;
        Children.Clear();
    }

    /// <summary>
    /// Create a UIButtonData snapshot from this element for backward compatibility.
    /// </summary>
    public UIButtonData ToButtonData()
    {
        return new UIButtonData
        {
            Name = Name,
            Text = Text,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            FontPath = FontPath,
            FontSize = FontSize,
            ImagePath = ImagePath,
            ImageMode = ImageMode,
            TextColor = TextColor,
            BgColor = BgColor,
            BorderColor = BorderColor,
            HoverTextColor = HoverTextColor,
            HoverBgColor = HoverBgColor,
            HoverBorderColor = HoverBorderColor,
            Alignment = Alignment,
            IsVisible = IsVisible,
            ClickBehaviorLabel = ClickBehaviorLabel,
            HoverEnterLabel = HoverEnterLabel,
            HoverExitLabel = HoverExitLabel,
            OnClick = OnClick,
            OnHoverEnter = OnHoverEnter,
            OnHoverExit = OnHoverExit,
        };
    }

    /// <summary>
    /// Convert this element (and its children recursively) into HUD.ButtonDef for rendering.
    /// </summary>
    public ButtonDef ToButtonDef(Action? onClickOverride = null)
    {
        return new ButtonDef
        {
            Label = Text,
            X = X,
            Y = Y,
            W = Width,
            H = Height,
            OnClick = onClickOverride ?? OnClick,
            OnHoverEnter = OnHoverEnter,
            OnHoverExit = OnHoverExit,
            IsHovered = IsHovered,
            Tag = this,
        };
    }

    /// <summary>
    /// Recursively find the topmost visible interactive element at the given scene-space coordinates.
    /// Returns null if no element is hit. Checks children first (for proper Z-ordering).
    /// </summary>
    public static UIElement? HitTestPoint(IReadOnlyList<UIElement> elements, float sceneX, float sceneY)
    {
        // Check in reverse order so last-rendered (topmost) elements take priority
        for (int i = elements.Count - 1; i >= 0; i--)
        {
            var elem = elements[i];
            if (!elem.IsVisible) continue;

            // Check children first (they render on top of parent)
            if (elem.Children.Count > 0)
            {
                var childHit = HitTestPoint(elem.Children, sceneX, sceneY);
                if (childHit != null) return childHit;
            }

            // Check self — only buttons are interactive
            if (elem.Type == UIElementType.Button || elem.Type == UIElementType.Label)
            {
                if (sceneX >= elem.X && sceneX <= elem.X + elem.Width &&
                    sceneY >= elem.Y && sceneY <= elem.Y + elem.Height)
                {
                    return elem;
                }
            }
        }
        return null;
    }

    /// <summary>Get the display icon for this element type (for hierarchy tree).</summary>
    public string GetIcon()
    {
        return Type switch
        {
            UIElementType.Scene => "[Scene]",
            UIElementType.Container => "[container]",
            UIElementType.Button => "[btn]",
            UIElementType.Label => "[lb]",
            UIElementType.Dialog => "[dialog]",
            _ => "❓",
        };
    }
}

// ── Keep UIButtonData for backward compatibility ──
/// <summary>
/// Legacy data-driven UI button. New code should use UIElement instead.
/// </summary>
public class UIButtonData
{
    public string Name { get; set; } = "Button";
    public string Text { get; set; } = "Button";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 200f;
    public float Height { get; set; } = 50f;

    // ── Image (replaces Text/Font when set) ──
    public string ImagePath { get; set; } = "";
    public ImageMode ImageMode { get; set; } = ImageMode.Stretch;

    public string FontPath { get; set; } = "Artifacts\\\\fonts\\\\Worldstar.ttf";
    public float FontSize { get; set; } = 13f;
    public Vector3 TextColor { get; set; } = new(0.95f, 0.95f, 1f);
    public Vector3 BgColor { get; set; } = new(0.10f, 0.12f, 0.18f);
    public Vector3 BorderColor { get; set; } = new(0.15f, 0.18f, 0.25f);
    public Vector3 HoverTextColor { get; set; } = new(0.95f, 0.95f, 1f);
    public Vector3 HoverBgColor { get; set; } = new(0.22f, 0.28f, 0.45f);
    public Vector3 HoverBorderColor { get; set; } = new(0.5f, 0.6f, 1.0f);
    public TextAlignment Alignment { get; set; } = TextAlignment.Center;
    public bool IsVisible { get; set; } = true;
    public bool IsHovered { get; set; }
    public string ClickBehaviorLabel { get; set; } = "None";
    public string HoverEnterLabel { get; set; } = "None";
    public string HoverExitLabel { get; set; } = "None";
    public Action? OnClick { get; set; }
    public Action? OnHoverEnter { get; set; }
    public Action? OnHoverExit { get; set; }

    public ButtonDef ToButtonDef(Action? onClickOverride = null)
    {
        return new ButtonDef
        {
            Label = Text, X = X, Y = Y, W = Width, H = Height,
            OnClick = onClickOverride ?? OnClick,
            OnHoverEnter = OnHoverEnter, OnHoverExit = OnHoverExit,
            IsHovered = IsHovered, Tag = this,
        };
    }
}
