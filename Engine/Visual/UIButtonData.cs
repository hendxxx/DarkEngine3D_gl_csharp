using System;
using System.Drawing;
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
    /// <summary>A radio button for single-choice selection within a group.</summary>
    RadioButton,
    /// <summary>A progress/status bar drawn from 3 images: background (frame),
    /// empty (unfilled interior) and progress (filled portion). Driven by Min/Max/Current
    /// like a slider but non-interactive — e.g. a health bar.</summary>
    Bar,
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

    // ── Image Filter: 0 = None (Nearest / pixel-crisp, DEFAULT), 1 = Smooth (Linear) ──
    public int ImageFilter { get; set; } = 0;

    // ── Slice (9-slice source window) ──
    public bool SliceEnabled { get; set; } = false;
    public int SliceX { get; set; }
    public int SliceY { get; set; }
    public int SliceWidth { get; set; }
    public int SliceHeight { get; set; }
    public int SliceBorderLeft { get; set; }
    public int SliceBorderRight { get; set; }
    public int SliceBorderTop { get; set; }
    public int SliceBorderBottom { get; set; }

    // ── Parent atlas coordinates ──
    public bool UseAtlasCoordinates { get; set; } = false;
    public string AtlasImagePath { get; set; } = "";
    public int AtlasX { get; set; }
    public int AtlasY { get; set; }
    public int AtlasWidth { get; set; }
    public int AtlasHeight { get; set; }

    // ── Hover / Clicked state art (optional; each falls back to ImagePath) ──
    public string HoverImagePath { get; set; } = "";
    public bool HoverUseAtlasCoordinates { get; set; }
    public string HoverAtlasImagePath { get; set; } = "";
    public int HoverAtlasX { get; set; }
    public int HoverAtlasY { get; set; }
    public int HoverAtlasWidth { get; set; }
    public int HoverAtlasHeight { get; set; }
    public string ClickedImagePath { get; set; } = "";
    public bool ClickedUseAtlasCoordinates { get; set; }
    public string ClickedAtlasImagePath { get; set; } = "";
    public int ClickedAtlasX { get; set; }
    public int ClickedAtlasY { get; set; }
    public int ClickedAtlasWidth { get; set; }
    public int ClickedAtlasHeight { get; set; }

    // ── Selected state art (keyboard focus / arrow-key navigation) ──
    // Same authoring shape as Hover/Clicked: optional bitmap, optional atlas image and
    // optional atlas crop. A blank state falls back to the normal image, so it is fully
    // optional; arrow-key navigation and the in-game keyboard focus both drive it.
    public string SelectedImagePath { get; set; } = "";
    public bool SelectedUseAtlasCoordinates { get; set; }
    public string SelectedAtlasImagePath { get; set; } = "";
    public int SelectedAtlasX { get; set; }
    public int SelectedAtlasY { get; set; }
    public int SelectedAtlasWidth { get; set; }
    public int SelectedAtlasHeight { get; set; }

    /// <summary>
    /// Resolve the effective image path for this element.
    /// If UseAtlasCoordinates is true:
    ///   - If AtlasImagePath is explicitly set, use that (override).
    ///   - Otherwise, walk up the parent hierarchy to find the nearest ancestor with an ImagePath or AtlasImagePath.
    /// If UseAtlasCoordinates is false:
    ///   - Use ImagePath.
    /// </summary>
    public string GetEffectiveImagePath()
    {
        if (UseAtlasCoordinates)
        {
            if (!string.IsNullOrEmpty(AtlasImagePath))
                return AtlasImagePath;

            var curr = Parent;
            while (curr != null)
            {
                if (!string.IsNullOrEmpty(curr.ImagePath))
                    return curr.ImagePath;
                if (!string.IsNullOrEmpty(curr.AtlasImagePath))
                    return curr.AtlasImagePath;
                curr = curr.Parent;
            }
        }
        return ImagePath;
    }

    /// <summary>Bitmap + atlas crop for a visual state. A state with no image path falls
    /// back to the normal image, so hover/clicked/selected art is fully optional. A state in
    /// atlas mode with blank paths draws from the element's default atlas image.
    /// Priority: pressed (clicked) → selected (keyboard focus / arrow navigation) → hovered.</summary>
    public (string path, bool useAtlas, int ax, int ay, int aw, int ah) GetStateImage(
        bool pressed, bool hovered, bool selected = false)
    {
        if (pressed && HasStateArt(ClickedUseAtlasCoordinates, ClickedImagePath, ClickedAtlasImagePath))
            return ResolveStateArt(ClickedUseAtlasCoordinates, ClickedImagePath, ClickedAtlasImagePath,
                                   ClickedAtlasX, ClickedAtlasY, ClickedAtlasWidth, ClickedAtlasHeight);
        if (selected && HasStateArt(SelectedUseAtlasCoordinates, SelectedImagePath, SelectedAtlasImagePath))
            return ResolveStateArt(SelectedUseAtlasCoordinates, SelectedImagePath, SelectedAtlasImagePath,
                                   SelectedAtlasX, SelectedAtlasY, SelectedAtlasWidth, SelectedAtlasHeight);
        if (hovered && HasStateArt(HoverUseAtlasCoordinates, HoverImagePath, HoverAtlasImagePath))
            return ResolveStateArt(HoverUseAtlasCoordinates, HoverImagePath, HoverAtlasImagePath,
                                   HoverAtlasX, HoverAtlasY, HoverAtlasWidth, HoverAtlasHeight);
        return (GetEffectiveImagePath(), UseAtlasCoordinates, AtlasX, AtlasY, AtlasWidth, AtlasHeight);
    }

    /// <summary>A state overrides the normal art when it has its own bitmap, its own atlas
    /// image, or atlas sampling switched on (which can reuse the default atlas).</summary>
    private static bool HasStateArt(bool useAtlas, string ownPath, string atlasPath)
        => useAtlas || !string.IsNullOrEmpty(ownPath) || !string.IsNullOrEmpty(atlasPath);

    /// <summary>Builds one state's draw info. A blank state crop inherits the element's crop,
    /// and a blank atlas image inherits the element's default atlas image.</summary>
    private (string path, bool useAtlas, int ax, int ay, int aw, int ah) ResolveStateArt(
        bool useAtlas, string ownPath, string atlasPath, int ax, int ay, int aw, int ah)
    {
        string path = StateImagePath(useAtlas, ownPath, atlasPath);
        if (useAtlas && (aw <= 0 || ah <= 0))
            return (path, true, AtlasX, AtlasY, AtlasWidth, AtlasHeight);
        return (path, useAtlas, ax, ay, aw, ah);
    }

    /// <summary>Atlas crop source for a state: an explicit atlas path wins; in atlas mode with
    /// no path the element's default atlas image (own or inherited from a parent) is used;
    /// otherwise the state crops from its own bitmap.</summary>
    private string StateImagePath(bool useAtlas, string ownPath, string atlasPath)
    {
        if (!useAtlas)
            return string.IsNullOrEmpty(atlasPath) ? ownPath : atlasPath;
        if (!string.IsNullOrEmpty(atlasPath))
            return atlasPath;
        string defaultAtlas = GetEffectiveImagePath();
        return string.IsNullOrEmpty(defaultAtlas) ? ownPath : defaultAtlas;
    }

    // ── Helper quads (computed lazily from Slice geometry) ──
    public bool HasSliceQuads { get; private set; }
    public RectangleF[] SliceQuads { get; private set; } = Array.Empty<RectangleF>();
    public void ComputeSliceQuads(int imgW, int imgH)
    {
        if (!SliceEnabled || imgW <= 0 || imgH <= 0)
        {
            HasSliceQuads = false;
            SliceQuads = Array.Empty<RectangleF>();
            return;
        }
        var bx = SliceBorderLeft;
        var br = SliceBorderRight;
        var bt = SliceBorderTop;
        var bb = SliceBorderBottom;
        SliceQuads = new RectangleF[9];
        // top-left
        SliceQuads[0] = new RectangleF(bx, bt, bx, bt);
        // top
        SliceQuads[1] = new RectangleF(bx, bt, SliceWidth - bx - br, bt);
        // top-right
        SliceQuads[2] = new RectangleF(SliceWidth - br, bt, br, bt);
        // left
        SliceQuads[3] = new RectangleF(bx, bt, bx, SliceHeight - bt - bb);
        // center
        SliceQuads[4] = new RectangleF(bx, bt, SliceWidth - bx - br, SliceHeight - bt - bb);
        // right
         SliceQuads[5] = new RectangleF(SliceWidth - br, bt, br, SliceHeight - bt - bb);
        // bottom-left
        SliceQuads[6] = new RectangleF(bx, SliceHeight - bb, bx, bb);
        // bottom
        SliceQuads[7] = new RectangleF(bx, SliceHeight - bb, SliceWidth - bx - br, bb);
        // bottom-right
        SliceQuads[8] = new RectangleF(SliceWidth - br, SliceHeight - bb, br, bb);
        HasSliceQuads = true;
    }

    // ── DeepClone pasang field baru ──
    public void DeepCloneFrom(UIElement other)
    {
        ImageFilter = other.ImageFilter;
        SliceEnabled = other.SliceEnabled;
        SliceX = other.SliceX;
        SliceY = other.SliceY;
        SliceWidth = other.SliceWidth;
        SliceHeight = other.SliceHeight;
        SliceBorderLeft = other.SliceBorderLeft;
        SliceBorderRight = other.SliceBorderRight;
        SliceBorderTop = other.SliceBorderTop;
        SliceBorderBottom = other.SliceBorderBottom;
        CaptureInputWhenVisible = other.CaptureInputWhenVisible;
        UseAtlasCoordinates = other.UseAtlasCoordinates;
        AtlasImagePath = other.AtlasImagePath;
        HoverImagePath = other.HoverImagePath;
        HoverUseAtlasCoordinates = other.HoverUseAtlasCoordinates;
        HoverAtlasImagePath = other.HoverAtlasImagePath;
        HoverAtlasX = other.HoverAtlasX;
        HoverAtlasY = other.HoverAtlasY;
        HoverAtlasWidth = other.HoverAtlasWidth;
        HoverAtlasHeight = other.HoverAtlasHeight;
        ClickedImagePath = other.ClickedImagePath;
        ClickedUseAtlasCoordinates = other.ClickedUseAtlasCoordinates;
        ClickedAtlasImagePath = other.ClickedAtlasImagePath;
        ClickedAtlasX = other.ClickedAtlasX;
        ClickedAtlasY = other.ClickedAtlasY;
        ClickedAtlasWidth = other.ClickedAtlasWidth;
        ClickedAtlasHeight = other.ClickedAtlasHeight;
        SelectedImagePath = other.SelectedImagePath;
        SelectedUseAtlasCoordinates = other.SelectedUseAtlasCoordinates;
        SelectedAtlasImagePath = other.SelectedAtlasImagePath;
        SelectedAtlasX = other.SelectedAtlasX;
        SelectedAtlasY = other.SelectedAtlasY;
        SelectedAtlasWidth = other.SelectedAtlasWidth;
        SelectedAtlasHeight = other.SelectedAtlasHeight;
    }
    public string FontPath { get; set; } = "Artifacts\\fonts\\Worldstar.ttf";
    public float FontSize { get; set; } = 13f;

    // ── Colors ──
    public Vector4 TextColor { get; set; } = new(0.95f, 0.95f, 1f, 1f);
    public Vector4 BgColor { get; set; } = new(0.10f, 0.12f, 0.18f, 1f);
    public Vector4 BorderColor { get; set; } = new(0.15f, 0.18f, 0.25f, 1f);

    // ── Hover Colors ──
    public Vector4 HoverTextColor { get; set; } = new(0.95f, 0.95f, 1f, 1f);
    public Vector4 HoverBgColor { get; set; } = new(0.22f, 0.28f, 0.45f, 1f);
    public Vector4 HoverBorderColor { get; set; } = new(0.5f, 0.6f, 1.0f, 1f);

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

    // ── Container input capture ──
    /// <summary>While this container is visible it takes the input focus: all gameplay
    /// keyboard/mouse actions are disabled (2D player movement, bound action keys, camera
    /// look, interact). Menu elements inside it keep accepting input.</summary>
    public bool CaptureInputWhenVisible { get; set; } = false;

    /// <summary>True when this element is visible and has <see cref="CaptureInputWhenVisible"/>
    /// set, or contains a visible descendant that does. A hidden element never captures
    /// (its children are ignored too, so a closed menu cannot keep gameplay frozen).
    /// EXCEPTION: a Scene-root element is a hierarchy holder, not drawable UI — its own
    /// IsVisible flag is conventionally false (MainMenuScene sets root.IsVisible = false),
    /// so gating on it would hide every menu underneath and the capture would never fire.</summary>
    public bool HasVisibleInputCapture()
    {
        if (!IsVisible && Type != UIElementType.Scene) return false;
        if (CaptureInputWhenVisible) return true;
        foreach (var child in Children)
            if (child.HasVisibleInputCapture()) return true;
        return false;
    }

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

    // ── Container scroll properties ──
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
    public SliderLabelPosition SliderValuePosition { get; set; } = SliderLabelPosition.Left;
    /// <summary>Spacing (in pixels) between the label and the slider track when label is Left or Right.</summary>
    public float SliderLabelSpacing { get; set; } = 8f;
    /// <summary>Spacing (in pixels) between the element label and the interactive control.</summary>
    public float LabelSpacing { get; set; } = 8f;

    // ── Checkbox colors ──
    /// <summary>Color of the checkbox checkmark.</summary>
    public Vector3 CheckmarkColor { get; set; } = new(0.9f, 0.9f, 1.0f);
    /// <summary>Background color when checkbox is checked.</summary>
    public Vector3 CheckedBgColor { get; set; } = new(0.25f, 0.55f, 1.0f);
    /// <summary>Background color when checkbox is unchecked.</summary>
    public Vector3 UncheckedBgColor { get; set; } = new(0.15f, 0.15f, 0.22f);

    // ── RadioButton colors ──
    /// <summary>Color of the radio button inner dot when selected.</summary>
    public Vector3 RadioSelectedColor { get; set; } = new(0.3f, 0.7f, 1.0f);
    /// <summary>Background color when radio button is selected.</summary>
    public Vector3 RadioSelectedBgColor { get; set; } = new(0.2f, 0.4f, 0.65f);
    /// <summary>Background color when radio button is not selected.</summary>
    public Vector3 RadioUnselectedBgColor { get; set; } = new(0.15f, 0.15f, 0.22f);
    /// <summary>Group name for radio button (same group = mutual exclusion).</summary>
    public string RadioGroup { get; set; } = "default";

    // ── Bar properties ──
    /// <summary>Image for the bar's outer frame/background (drawn first, full element).</summary>
    public string BarBackgroundPath { get; set; } = "";
    /// <summary>Image for the EMPTY interior (drawn between background and progress fill).</summary>
    public string BarEmptyPath { get; set; } = "";
    /// <summary>Image for the PROGRESS fill (clipped to the current fraction).</summary>
    public string BarProgressPath { get; set; } = "";
    /// <summary>Legacy shared inset (all sides) — kept for old scene files; migrated on
    /// load into the per-layer offsets below. New content uses the per-layer offsets.</summary>
    public float BarInset { get; set; } = 0f;
    /// <summary>Fill direction: 0 = Left→Right, 1 = Right→Left, 2 = Bottom→Top, 3 = Top→Bottom.</summary>
    public int BarDirection { get; set; } = 0;
    /// <summary>When set (Health/Mana/Level/Experience/Fitness), the Bar's fraction is
    /// driven live by Player2DStats instead of the manual CurrentValue — the HUD bar
    /// follows gameplay. "None" = manual (panel slider).</summary>
    public string BarStatBinding { get; set; } = "None";

    // ── Bar per-layer edge offsets (scene px, + = grow outward, − = shrink inward) ──
    // Each layer rect starts at the element rect and its four edges are moved
    // independently, so the decorated frame can stick out while the fill insets.
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

    // ── Bar per-layer position (scene px translation, applied AFTER the edge offsets) ──
    // Offsets change a layer's size; position slides the whole (already resized) rect
    // without touching its dimensions — handy for nudging frame art into place.
    public float BarBgPosX { get; set; }
    public float BarBgPosY { get; set; }
    public float BarEmptyPosX { get; set; }
    public float BarEmptyPosY { get; set; }
    public float BarProgPosX { get; set; }
    public float BarProgPosY { get; set; }

    // ── Bar per-layer fallback colors ──
    // Each Bar layer draws its image when a path is set; when the path is empty
    // (or the image fails to load) the layer falls back to this flat color, so a
    // Bar is still fully visible while authoring.
    /// <summary>Fill color for the Background layer when BarBackgroundPath is empty.</summary>
    public Vector3 BarBgColor { get; set; } = new(0.10f, 0.10f, 0.14f);
    /// <summary>Fill color for the Empty layer when BarEmptyPath is empty.</summary>
    public Vector3 BarEmptyColor { get; set; } = new(0.05f, 0.05f, 0.08f);
    /// <summary>Fill color for the Progress fill when BarProgressPath is empty.</summary>
    public Vector3 BarProgressColor { get; set; } = new(0.30f, 0.70f, 1.00f);

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

    /// <summary>Resolve one Bar layer's X/Y translation (scene px). See <see cref="GetBarLayerRect"/>.</summary>
    public (float x, float y) GetBarLayerPosition(BarLayer layer) => layer switch
    {
        BarLayer.Background => (BarBgPosX, BarBgPosY),
        BarLayer.Empty => (BarEmptyPosX, BarEmptyPosY),
        BarLayer.Progress => (BarProgPosX, BarProgPosY),
        _ => (0f, 0f),
    };

    /// <summary>Bar layer identifiers, expressed as named z-indices. Draw order is
    /// back → front: Background (-3) → Empty (-2) → Progress (-1) → ImagePath (0, top).</summary>
    public enum BarLayer { Background = -3, Empty = -2, Progress = -1, ImagePath = 0 }

    /// <summary>Resolve the draw rect of one Bar layer: the element rect (in scene
    /// coordinates, parent/anchor already resolved by the caller) with that layer's
    /// four edge offsets applied independently — left/right shift the edges
    /// horizontally, top/bottom vertically. Positive offset = edge moves outward
    /// (bigger layer), negative = inward. Used identically by the editor preview and
    /// the in-game HUD renderer so bars are WYSIWYG.</summary>
    public (float x, float y, float w, float h) GetBarLayerRect(
        float elemX, float elemY, float elemW, float elemH, BarLayer layer)
    {
        float l, r, t, b, px, py;
        switch (layer)
        {
            case BarLayer.Background:
                l = BarBgOffsetLeft; r = BarBgOffsetRight; t = BarBgOffsetTop; b = BarBgOffsetBottom;
                px = BarBgPosX; py = BarBgPosY;
                break;
            case BarLayer.Empty:
                l = BarEmptyOffsetLeft; r = BarEmptyOffsetRight; t = BarEmptyOffsetTop; b = BarEmptyOffsetBottom;
                px = BarEmptyPosX; py = BarEmptyPosY;
                break;
            case BarLayer.Progress:
                l = BarProgOffsetLeft; r = BarProgOffsetRight; t = BarProgOffsetTop; b = BarProgOffsetBottom;
                px = BarProgPosX; py = BarProgPosY;
                break;
            default: // ImagePath (element's own art) — no edge offsets
                l = 0; r = 0; t = 0; b = 0; px = 0; py = 0;
                break;
        }
        // Edge offsets resize the layer, then the position offsets translate the whole
        // resized rect — the two are fully independent.
        return (elemX + px + l, elemY + py + t,
                MathF.Max(1f, elemW - l + r),
                MathF.Max(1f, elemH - t + b));
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
        var clone = new UIElement
        {
            Name = Name,
            Type = Type,
            Text = Text,
            X = X, Y = Y, Width = Width, Height = Height,
            ImagePath = ImagePath, ImageMode = ImageMode,
            ImageFilter = ImageFilter,
            SliceEnabled = SliceEnabled,
            SliceX = SliceX, SliceY = SliceY, SliceWidth = SliceWidth, SliceHeight = SliceHeight,
            SliceBorderLeft = SliceBorderLeft, SliceBorderRight = SliceBorderRight,
            SliceBorderTop = SliceBorderTop, SliceBorderBottom = SliceBorderBottom,
            UseAtlasCoordinates = UseAtlasCoordinates, AtlasImagePath = AtlasImagePath,
            AtlasX = AtlasX, AtlasY = AtlasY, AtlasWidth = AtlasWidth, AtlasHeight = AtlasHeight,
            HoverImagePath = HoverImagePath, HoverUseAtlasCoordinates = HoverUseAtlasCoordinates,
            HoverAtlasImagePath = HoverAtlasImagePath,
            HoverAtlasX = HoverAtlasX, HoverAtlasY = HoverAtlasY,
            HoverAtlasWidth = HoverAtlasWidth, HoverAtlasHeight = HoverAtlasHeight,
            ClickedImagePath = ClickedImagePath, ClickedUseAtlasCoordinates = ClickedUseAtlasCoordinates,
            ClickedAtlasImagePath = ClickedAtlasImagePath,
            ClickedAtlasX = ClickedAtlasX, ClickedAtlasY = ClickedAtlasY,
            ClickedAtlasWidth = ClickedAtlasWidth, ClickedAtlasHeight = ClickedAtlasHeight,
            SelectedImagePath = SelectedImagePath, SelectedUseAtlasCoordinates = SelectedUseAtlasCoordinates,
            SelectedAtlasImagePath = SelectedAtlasImagePath,
            SelectedAtlasX = SelectedAtlasX, SelectedAtlasY = SelectedAtlasY,
            SelectedAtlasWidth = SelectedAtlasWidth, SelectedAtlasHeight = SelectedAtlasHeight,
            FontSize = FontSize, FontPath = FontPath,
            TextColor = TextColor, BgColor = BgColor, BorderColor = BorderColor,
            HoverTextColor = HoverTextColor, HoverBgColor = HoverBgColor, HoverBorderColor = HoverBorderColor,
            Alignment = Alignment, IsVisible = IsVisible, Opacity = Opacity,
            AutoFillWindow = AutoFillWindow, AutoCenterX = AutoCenterX, AutoCenterY = AutoCenterY,
            Anchor = Anchor, UseHover = UseHover,
            ClickBehaviorLabel = ClickBehaviorLabel, HoverEnterLabel = HoverEnterLabel,            HoverExitLabel = HoverExitLabel,
            FallbackText = FallbackText, WordWrap = WordWrap,
            TriggeredByKeyboardButton = TriggeredByKeyboardButton,
            CaptureInputWhenVisible = CaptureInputWhenVisible,
            // Slider properties
            MinValue = MinValue, MaxValue = MaxValue, Step = Step, CurrentValue = CurrentValue,
            TextOptions = [.. TextOptions], SelectedTextIndex = SelectedTextIndex,
            SliderTrackColor = SliderTrackColor, SliderFillColor = SliderFillColor,
            SliderThumbColor = SliderThumbColor, SliderThumbBorderColor = SliderThumbBorderColor,
            SliderThumbSize = SliderThumbSize, SliderTrackHeight = SliderTrackHeight,
            SliderValuePosition = SliderValuePosition, SliderLabelSpacing = SliderLabelSpacing, LabelSpacing = LabelSpacing,
            // Checkbox/RadioButton properties
            IsChecked = IsChecked,
            CheckmarkColor = CheckmarkColor, CheckedBgColor = CheckedBgColor, UncheckedBgColor = UncheckedBgColor,
            RadioSelectedColor = RadioSelectedColor, RadioSelectedBgColor = RadioSelectedBgColor,
            RadioUnselectedBgColor = RadioUnselectedBgColor, RadioGroup = RadioGroup,
            // Dropdown properties
            Options = [.. Options], SelectedIndex = SelectedIndex, ArrowColor = ArrowColor,
            // TextBox properties
            Placeholder = Placeholder, MaxLength = MaxLength, InputText = InputText, CursorColor = CursorColor,
            // Container scroll properties
            ScrollBarWidth = ScrollBarWidth,
            ScrollBarTrackColor = ScrollBarTrackColor, ScrollBarThumbColor = ScrollBarThumbColor,
            ScrollBarThumbHoverColor = ScrollBarThumbHoverColor,
        };
        // Deep-clone children
        foreach (var child in Children)
            clone.AddChild(child.DeepClone());
        return clone;
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
            UIElementType.RadioButton => "[radio]",
            UIElementType.Bar => "[bar]",
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
    public Vector4 TextColor { get; set; } = new(0.95f, 0.95f, 1f, 1f);
    public Vector4 BgColor { get; set; } = new(0.10f, 0.12f, 0.18f, 1f);
    public Vector4 BorderColor { get; set; } = new(0.15f, 0.18f, 0.25f, 1f);
    public Vector4 HoverTextColor { get; set; } = new(0.95f, 0.95f, 1f, 1f);
    public Vector4 HoverBgColor { get; set; } = new(0.22f, 0.28f, 0.45f, 1f);
    public Vector4 HoverBorderColor { get; set; } = new(0.5f, 0.6f, 1.0f, 1f);
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
