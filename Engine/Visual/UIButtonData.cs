using System.Numerics;
using DarkEngine3D_gl_csharp.Engine.Scene;

namespace DarkEngine3D_gl_csharp.Engine.Visual;

/// <summary>Text alignment modes for UI buttons.</summary>
public enum TextAlignment
{
    Left,
    Center,
    Right
}

/// <summary>Anchor position relative to viewport edges.
/// When anchored, element position is recalculated from the anchor point each frame.
/// X/Y become offsets from the anchor point.</summary>
public enum UIAnchor
{
    None,
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

/// <summary>Slider value label position.</summary>
public enum SliderLabelPosition
{
    /// <summary>No value label shown.</summary>
    None,
    /// <summary>Show label on the left side of the slider.</summary>
    Left,
    /// <summary>Show label on the right side of the slider (default).</summary>
    Right,
    /// <summary>Show label above the slider.</summary>
    Top,
    /// <summary>Show label below the slider.</summary>
    Bottom,
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
    /// <summary>A slider for numeric value selection (FOV, Volume, etc.).</summary>
    SliderNumber,
    /// <summary>A slider for cycling through text options.</summary>
    SliderText,
    /// <summary>A checkbox for boolean settings (Enable Shadow, Fullscreen, etc.).</summary>
    Checkbox,
    /// <summary>A dropdown/combo box for selecting one value from a list.</summary>
    Dropdown,
    /// <summary>A text input field (Save Name, Player Name, etc.).</summary>
    TextBox,
    /// <summary>A scrollable container that clips children and shows a vertical scrollbar.</summary>
    Placeholder,
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
    public string FontPath { get; set; } = "Artifacts\\fonts\\Worldstar.ttf";
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

    // ── Visibility & Opacity ──
    public bool IsVisible { get; set; } = true;
    /// <summary>Overall opacity (0.0 = fully transparent, 1.0 = fully opaque).
    /// Multiplied with all element rendering (background, border, text, child elements).</summary>
    public float Opacity { get; set; } = 1f;

    // ── Auto-fill window (for overlay/background elements) ──
    public bool AutoFillWindow { get; set; } = false;
    /// <summary>Auto-center this element horizontally in the viewport.</summary>
    public bool AutoCenterX { get; set; } = false;
    /// <summary>Auto-center this element vertically in the viewport.</summary>
    public bool AutoCenterY { get; set; } = false;

    // ── Anchor (element stays attached to viewport edges) ──
    /// <summary>Anchor position relative to viewport. X/Y become offsets from anchor point.</summary>
    public UIAnchor Anchor { get; set; } = UIAnchor.None;
    /// <summary>Legacy: auto-center both axes. Sets/clears both AutoCenterX and AutoCenterY.</summary>
    public bool AutoCenter
    {
        get => AutoCenterX && AutoCenterY;
        set { AutoCenterX = value; AutoCenterY = value; }
    }
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

    // ── Container trigger (for Container type) ──
    /// <summary>Keyboard key that toggles this container visible/hidden in in-game mode
    /// (e.g. "Escape", "F1"). When pressed, the container shows/hides.</summary>
    public string TriggeredByKeyboardButton { get; set; } = "";

    public Action? OnClick { get; set; }
    public Action? OnHoverEnter { get; set; }
    public Action? OnHoverExit { get; set; }

    // ── SliderNumber properties ──
    /// <summary>Minimum value for the slider range.</summary>
    public float MinValue { get; set; } = 0f;
    /// <summary>Maximum value for the slider range.</summary>
    public float MaxValue { get; set; } = 100f;
    /// <summary>Step/increment value for the slider.</summary>
    public float Step { get; set; } = 1f;
    /// <summary>Current value of the slider.</summary>
    public float CurrentValue { get; set; } = 50f;

    // ── SliderText properties ──
    /// <summary>Available text options for the text slider.</summary>
    public List<string> TextOptions { get; set; } = ["Option A", "Option B", "Option C"];
    /// <summary>Currently selected text option index.</summary>
    public int SelectedTextIndex { get; set; } = 0;

    // ── Checkbox properties (for Checkbox type) ──
    /// <summary>Whether the checkbox is checked.</summary>
    public bool IsChecked { get; set; } = false;

    // ── Dropdown properties (for Dropdown type) ──
    /// <summary>Available options for the dropdown.</summary>
    public List<string> Options { get; set; } = ["Option 1", "Option 2", "Option 3"];
    /// <summary>Currently selected option index (-1 = none).</summary>
    public int SelectedIndex { get; set; } = 0;

    // ── Fallback text when image fails to load ──
    /// <summary>Text shown when the element has an ImagePath but the image fails to load.
    /// Independent from the main Text property (which is shown when no image is set).</summary>
    public string FallbackText { get; set; } = "";

    // ── Word Wrap (for Label type) ──
    /// <summary>When true, text wraps to next line if it exceeds element width.</summary>
    public bool WordWrap { get; set; } = true;

    // ── Placeholder properties (for Placeholder type) ──
    /// <summary>Current vertical scroll offset in pixels (0 = top).</summary>
    public float ScrollY { get; set; } = 0f;
    /// <summary>Width of the vertical scrollbar track.</summary>
    public float ScrollBarWidth { get; set; } = 10f;
    /// <summary>Color of the scrollbar track background.</summary>
    public Vector3 ScrollBarTrackColor { get; set; } = new(0.15f, 0.15f, 0.20f);
    /// <summary>Color of the scrollbar thumb.</summary>
    public Vector3 ScrollBarThumbColor { get; set; } = new(0.45f, 0.45f, 0.55f);
    /// <summary>Color of the scrollbar thumb when hovered.</summary>
    public Vector3 ScrollBarThumbHoverColor { get; set; } = new(0.55f, 0.55f, 0.65f);
    /// <summary>Computed content height (sum of children bounds). Updated each frame during render.</summary>
    public float ContentHeight { get; set; } = 0f;

    // ── TextBox properties (for TextBox type) ──
    /// <summary>Placeholder text shown when input is empty.</summary>
    public string Placeholder { get; set; } = "Enter text...";
    /// <summary>Maximum length of input text (0 = unlimited).</summary>
    public int MaxLength { get; set; } = 0;
    /// <summary>Current text value of the input field.</summary>
    public string InputText { get; set; } = "";

    // ════════════════════════════════════════════════
    //  Visual Style Properties (for type-specific rendering)
    // ════════════════════════════════════════════════

    // ── Slider track/thumb colors ──
    /// <summary>Color of the slider track background (empty portion).</summary>
    public Vector3 SliderTrackColor { get; set; } = new(0.30f, 0.30f, 0.35f);
    /// <summary>Color of the slider filled/selected portion.</summary>
    public Vector3 SliderFillColor { get; set; } = new(0.3f, 0.6f, 1.0f);
    /// <summary>Color of the slider thumb handle.</summary>
    public Vector3 SliderThumbColor { get; set; } = new(0.9f, 0.9f, 1.0f);
    /// <summary>Color of the slider thumb handle border.</summary>
    public Vector3 SliderThumbBorderColor { get; set; } = new(0.3f, 0.6f, 1.0f);
    /// <summary>Size (diameter) of the slider thumb handle in scene pixels.</summary>
    public float SliderThumbSize { get; set; } = 14f;
    /// <summary>Height of the slider track bar in scene pixels.</summary>
    public float SliderTrackHeight { get; set; } = 6f;
    /// <summary>Position of the slider value label (None, Left, Right, Top, Bottom).</summary>
    public SliderLabelPosition SliderValuePosition { get; set; } = SliderLabelPosition.Right;

    // ── Checkbox colors ──
    /// <summary>Color of the checkbox checkmark.</summary>
    public Vector3 CheckmarkColor { get; set; } = new(0.9f, 0.9f, 1.0f);
    /// <summary>Background color when checkbox is checked.</summary>
    public Vector3 CheckedBgColor { get; set; } = new(0.25f, 0.55f, 1.0f);
    /// <summary>Background color when checkbox is unchecked.</summary>
    public Vector3 UncheckedBgColor { get; set; } = new(0.15f, 0.15f, 0.22f);

    // ── Dropdown colors ──
    /// <summary>Color of the dropdown arrow icon.</summary>
    public Vector3 ArrowColor { get; set; } = new(0.5f, 0.5f, 0.7f);

    // ── TextBox colors ──
    /// <summary>Color of the text box blinking cursor.</summary>
    public Vector3 CursorColor { get; set; } = new(0.5f, 0.8f, 1.0f);

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

    // ════════════════════════════════════════════════
    //  Anchor Position Calculation
    // ════════════════════════════════════════════════

    /// <summary>
    /// Calculate the actual position (ax, ay) based on the anchor setting.
    /// Call this each frame before rendering. X/Y are the element's base offset;
    /// the anchor shifts the origin to the appropriate viewport edge.
    /// </summary>
    public (float x, float y) GetAnchoredPosition(float virtualW, float virtualH)
    {
        if (Anchor == UIAnchor.None || AutoFillWindow)
            return (X, Y);

        float ax = X, ay = Y;
        switch (Anchor)
        {
            case UIAnchor.TopLeft:      ax = X;                    ay = Y; break;
            case UIAnchor.TopCenter:    ax = (virtualW - Width) / 2f + X; ay = Y; break;
            case UIAnchor.TopRight:     ax = virtualW - Width - X;  ay = Y; break;
            case UIAnchor.CenterLeft:   ax = X;                    ay = (virtualH - Height) / 2f + Y; break;
            case UIAnchor.Center:       ax = (virtualW - Width) / 2f + X; ay = (virtualH - Height) / 2f + Y; break;
            case UIAnchor.CenterRight:  ax = virtualW - Width - X;  ay = (virtualH - Height) / 2f + Y; break;
            case UIAnchor.BottomLeft:   ax = X;                    ay = virtualH - Height - Y; break;
            case UIAnchor.BottomCenter: ax = (virtualW - Width) / 2f + X; ay = virtualH - Height - Y; break;
            case UIAnchor.BottomRight:  ax = virtualW - Width - X;  ay = virtualH - Height - Y; break;
        }
        return (ax, ay);
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
            WordWrap = WordWrap,
            Alignment = Alignment,
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
            if (elem.Type == UIElementType.Button || elem.Type == UIElementType.Label ||
                elem.Type == UIElementType.SliderNumber || elem.Type == UIElementType.SliderText ||
                elem.Type == UIElementType.Checkbox ||
                elem.Type == UIElementType.Dropdown || elem.Type == UIElementType.TextBox)
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

    /// <summary>Create a deep clone of this element (all properties + children recursively).
    /// Uses SceneAssetSerializer serialization round-trip for a reliable full copy.
    /// The clone gets a fresh InstanceId but preserves all other values.</summary>
    public UIElement DeepClone()
    {
        // Serialize to data, then deserialize back to a new element tree
        var data = SceneAssetSerializer.ToData(this);
        return SceneAssetSerializer.ToUIElement(data);
    }

    /// <summary>Compute the effective X/Y/W/H after applying AutoFillWindow and
    /// AutoCenter against the given canvas size (does NOT mutate the element).
    /// Shared by the editor preview and the game renderer so auto-layout behaves
    /// identically everywhere.</summary>
    public (float X, float Y, float W, float H) GetLayoutBounds(float canvasW, float canvasH)
    {
        float x = X, y = Y, w = Width, h = Height;
        if (AutoFillWindow) { x = 0f; y = 0f; w = canvasW; h = canvasH; }
        else
        {
            // AutoCenter and Anchor are mutually exclusive — AutoCenter wins if both set.
            if (AutoCenterX)
            {
                x = Math.Max(0f, (canvasW - w) * 0.5f);
            }
            else if (Anchor != UIAnchor.None)
            {
                switch (Anchor)
                {
                    case UIAnchor.TopLeft:      x = X; break;
                    case UIAnchor.TopCenter:    x = (canvasW - w) / 2f + X; break;
                    case UIAnchor.TopRight:     x = canvasW - w - X; break;
                    case UIAnchor.CenterLeft:   x = X; break;
                    case UIAnchor.Center:       x = (canvasW - w) / 2f + X; break;
                    case UIAnchor.CenterRight:  x = canvasW - w - X; break;
                    case UIAnchor.BottomLeft:   x = X; break;
                    case UIAnchor.BottomCenter: x = (canvasW - w) / 2f + X; break;
                    case UIAnchor.BottomRight:  x = canvasW - w - X; break;
                }
            }

            if (AutoCenterY)
            {
                y = Math.Max(0f, (canvasH - h) * 0.5f);
            }
            else if (Anchor != UIAnchor.None)
            {
                switch (Anchor)
                {
                    case UIAnchor.TopLeft:      y = Y; break;
                    case UIAnchor.TopCenter:    y = Y; break;
                    case UIAnchor.TopRight:     y = Y; break;
                    case UIAnchor.CenterLeft:   y = (canvasH - h) / 2f + Y; break;
                    case UIAnchor.Center:       y = (canvasH - h) / 2f + Y; break;
                    case UIAnchor.CenterRight:  y = (canvasH - h) / 2f + Y; break;
                    case UIAnchor.BottomLeft:   y = canvasH - h - Y; break;
                    case UIAnchor.BottomCenter: y = canvasH - h - Y; break;
                    case UIAnchor.BottomRight:  y = canvasH - h - Y; break;
                }
            }
        }
        return (x, y, w, h);
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
            UIElementType.SliderNumber => "[sld]",
            UIElementType.SliderText => "[sldtxt]",
            UIElementType.Checkbox => "[chk]",
            UIElementType.Dropdown => "[drp]",
            UIElementType.TextBox => "[txt]",
            UIElementType.Placeholder => "[scroll]",
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

    public string FontPath { get; set; } = "Artifacts\\fonts\\Worldstar.ttf";
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
    public bool WordWrap { get; set; } = false;
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
            IsHovered = IsHovered, Tag = this, WordWrap = WordWrap,
            Alignment = Alignment,
        };
    }
}
