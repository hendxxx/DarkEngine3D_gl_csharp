
using DarkEngine3D_gl_csharp.Engine.Config;
using DarkEngine3D_gl_csharp.Engine.Helpers;
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using StbImageSharp;
using System.Numerics;
using System.IO;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Viewport panel  displays the game's rendered scene texture inside an ImGui panel.
/// Supports aspect-ratio-correct scaling and click-to-select.
/// Includes interactive UI element editing: drag to move, resize from corners.
/// </summary>
public unsafe class ViewportPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    //  Snap-to-grid state 
    private bool _snapEnabled = true;
    private float _snapGridSize = 20f;
    private static readonly float[] SnapOptions = [5f, 10f, 20f, 40f, 50f];

    /// <summary>Snap a value to the nearest grid increment.</summary>
    private float SnapToGrid(float value) =>
        _snapEnabled ? MathF.Round(value / _snapGridSize) * _snapGridSize : value;

    //  Preview mode: hides all editor helpers, shows scene as-in-game 
    private bool _previewMode = false;
    //  Fullscreen mode: used by In-Game Mode (F8), skips toolbar, fullscreen window 
    private bool _fullscreenMode = false;

    //  Initial sync guard: ensures InGameActive matches _previewMode on first frame 
    private bool _initialSyncDone = false;

    //  In-game mode: last element clicked by mouse (for syncing keyboard focus) 
    /// <summary>Set by DrawEditorUIPreview when a mouse click occurs in preview mode.
    /// Read and reset by IDE.RenderInGameMode() to sync keyboard focus to the clicked element.</summary>
    public UIElement? LastInGameClickedElement { get; set; }

    //  Drag state for UI element editing 
    private enum DragMode { None, Move, ResizeTL, ResizeTR, ResizeBL, ResizeBR }
    private DragMode _dragMode = DragMode.None;
    // Starting state when drag began (scene coords)
    private float _dragStartX, _dragStartY, _dragStartW, _dragStartH;
    private Vector2 _dragStartMouseScene; // mouse position in scene coords when drag started

    //  Model Editor Gizmo 
    private readonly TransformGizmo _gizmo = new();

    //  Marquee (rubber-band) multi-select state 
    /// <summary>Scene-space (0..texW, 0..texH, Y-down) position where the left button
    /// was pressed to start a marquee selection. Null when no marquee is in progress.</summary>
    private Vector2? _marqueeStart = null;
    private Vector2 _marqueeCurrent;
    private bool _marqueeActive = false;
    // Post-popup suppress: after any popup/menu closes, suppress scene interactions
    // for a few frames so the click that closed the menu doesn't leak into the scene.
    private int _postPopupFrames = 0;
    private bool _wasPopupOpen = false;

    //  Terrain brush paint state 
    /// <summary>Object being painted in the current brush stroke (null = no stroke).</summary>
    private EditorObject? _brushObj = null;
    /// <summary>Height snapshot taken when the stroke began (for undo, // modes).</summary>
    private float[]? _brushBefore = null;
    /// <summary>Target (normalized 0..1) height captured from the first stamp of the current
    ///  flatten stroke  the terrain is leveled toward it (Unreal-style flatten).</summary>
    private float _flattenTargetNorm = 0f;
    /// <summary>True once <see cref="_flattenTargetNorm"/> was captured for the current stroke
    /// (guard: never flatten toward a stale/zero target if the capture ray missed).</summary>
    private bool _flattenTargetReady = false;

    //  Sky sun gizmo drag state 
    /// <summary>Sky object whose sun handle is being dragged (null = not dragging).</summary>
    private EditorObject? _skySunDragObj = null;
    /// <summary>Pitch/yaw captured when the sun drag started (for undo; null = followed time of day).</summary>
    private float? _skySunDragOldPitch = null;
    private float? _skySunDragOldYaw = null;
    /// <summary>First Light marker kept in sync with the sun drag (null = none). A placed
    /// Light marker overrides the sky sun for actual lighting, so its direction follows
    /// the sun being dragged  lighting then matches the gizmo in real time.</summary>
    private EditorObject? _skySunDragLightObj = null;
    /// <summary>Light marker direction captured when the drag started (for undo).</summary>
    private Vector3? _skySunDragOldLightDir = null;
    /// <summary>Splat snapshot taken when the stroke began (for undo,  mode).</summary>
    private byte[]? _brushSplatBefore = null;
    /// <summary>Terrain currently showing the 3D brush ring (cleared when the hover moves
    /// or the brush tool is turned off, so no stale ring is left behind).</summary>
    private EditorObject? _brushIndicatorObj = null;

    /// <summary>Colors of the 4 paintable layers  used for the
    /// toolbar chips and the brush cursor while painting.</summary>
    private static readonly Vector4[] TerrainLayerColors =
    [
        new(0.20f, 0.50f, 0.85f, 1f), // Layer 1
        new(0.60f, 0.45f, 0.28f, 1f), // Layer 2
        new(0.30f, 0.65f, 0.30f, 1f), // Layer 3
        new(0.90f, 0.93f, 0.98f, 1f), // Layer 4
    ];

    /// <summary>Hide the 3D brush ring on whichever terrain is currently showing it.</summary>
    private void ClearBrushIndicator()
    {
        if (_brushIndicatorObj != null)
        {
            _brushIndicatorObj.ShowBrushIndicator = false;
            _brushIndicatorObj = null;
        }
    }

    /// <summary>Activate the terrain brush tool in the given mode (0= sculpt, 1= layer
    /// paint, 2= smooth, 3= flatten), clearing any in-progress stroke and disabling fly
    /// mouse-look (both use the mouse, so painting must never rotate the camera).</summary>
    private void EnableTerrainBrush(int mode)
    {
        _bridge.TerrainBrushMode = mode;
        _bridge.TerrainBrushActive = true;
        _brushObj = null;
        _brushBefore = null;
        _brushSplatBefore = null;
        _flattenTargetNorm = 0f;
        _flattenTargetReady = false;
        // Abort any in-flight sky sun drag  the brush owns the mouse now.
        _skySunDragObj = null;
        _skySunDragOldPitch = null;
        _skySunDragOldYaw = null;
        _skySunDragLightObj = null;
        _skySunDragOldLightDir = null;
        ClearBrushIndicator();
        if (_bridge.Camera != null)
            _bridge.Camera.FlyMouseLook = false;
    }

    /// <summary>Toggle a terrain brush tool on/off (mode: 0= sculpt, 1= paint,
    /// 2= smooth, 3= flatten). Turning the active tool off clears the stroke state.</summary>
    private void ToggleTerrainBrushMode(int mode)
    {
        if (_bridge.TerrainBrushActive && _bridge.TerrainBrushMode == mode)
        {
            _bridge.TerrainBrushActive = false;
            _brushObj = null;
            _brushBefore = null;
            _brushSplatBefore = null;
            _flattenTargetNorm = 0f;
            _flattenTargetReady = false;
            ClearBrushIndicator();
        }
        else
        {
            EnableTerrainBrush(mode);
        }
    }


    //  Cached conversion data (set each frame in overlay) 
    private Vector2 _imageMin, _imageMax, _imageSize;
    private float _texW = 1f, _texH = 1f;

    //  Left-edge floating toolbar bounds (edit mode, drawn over the image) 
    private Vector2 _leftToolbarMin, _leftToolbarMax;


    /// <summary>Convert ImGui screen coordinates to scene pixel coordinates.</summary>
    private Vector2 ScreenToScene(Vector2 screenPos)
    {
        float relX = screenPos.X - _imageMin.X;
        float relY = screenPos.Y - _imageMin.Y;
        float u = relX / _imageSize.X;
        float v = relY / _imageSize.Y; // No Y-flip  scene Y=0 is top, same as ImGui
        return new Vector2(u * _texW, v * _texH);
    }

    /// <summary>Convert scene pixel coordinates to ImGui screen coordinates.</summary>
    private Vector2 SceneToScreen(float sceneX, float sceneY)
    {
        float u = sceneX / _texW;
        float v = sceneY / _texH; // No Y-flip  scene Y=0 is top, same as ImGui
        return new Vector2(_imageMin.X + u * _imageSize.X, _imageMin.Y + v * _imageSize.Y);
    }

    /// <summary>Draw a live preview of editor scene UI elements using ImGui draw list.
    /// Renders backgrounds, borders, text/images with hover effects.
    /// When isPreview=true: click triggers element's OnClick behavior (game-like).
    /// When isPreview=false: click selects element in the editor.
    /// Pass isMouseDown=true when the mouse button is held (for slider dragging).
    /// focusedElement is highlighted with a glow border for keyboard navigation.
    /// keyboardActivate signals that Enter/Space was pressed for the focused element.</summary>
    private void DrawEditorUIPreview(ImDrawListPtr drawList, IReadOnlyList<UIElement> elements, Vector2 mouseScreen, bool leftClicked, bool isPreview = false, bool isMouseDown = false, UIElement? focusedElement = null, bool keyboardActivate = false)
    {
        for (int ei = 0; ei < elements.Count; ei++)
        {
            var elem = elements[ei];
            if (!elem.IsVisible) continue;
            float elemOpacity = Math.Clamp(elem.Opacity, 0f, 1f);

            // Auto-fill window: force element to cover the entire viewport
            if (elem.AutoFillWindow)
            {
                elem.X = 0f;
                elem.Y = 0f;
                elem.Width = _texW;
                elem.Height = _texH;
            }

            // Auto-center: center the element in the viewport (in texture coordinates)
            if (elem.AutoCenterX)
            {
                elem.X = Math.Max(0f, (_texW - elem.Width) * 0.5f);
            }
            if (elem.AutoCenterY)
            {
                elem.Y = Math.Max(0f, (_texH - elem.Height) * 0.5f);
            }

            // Anchor: recalculate position from viewport edges
            if (elem.Anchor != UIAnchor.None && !elem.AutoFillWindow)
            {
                var (ax, ay) = elem.GetAnchoredPosition(_texW, _texH);
                elem.X = ax;
                elem.Y = ay;
            }

            // Convert scene coords to screen coords (no Y-flip  scene Y=0 is top)
            float sx0 = _imageMin.X + (elem.X / _texW) * _imageSize.X;
            float sy0 = _imageMin.Y + (elem.Y / _texH) * _imageSize.Y;
            float sx1 = _imageMin.X + ((elem.X + elem.Width) / _texW) * _imageSize.X;
            float sy1 = _imageMin.Y + ((elem.Y + elem.Height) / _texH) * _imageSize.Y;

            // Skip elements completely outside the image bounds
            if (sx1 < _imageMin.X || sx0 > _imageMax.X || sy1 < _imageMin.Y || sy0 > _imageMax.Y)
                continue;

            // Clamp to image bounds
            float csx0 = Math.Clamp(sx0, _imageMin.X, _imageMax.X);
            float csy0 = Math.Clamp(sy0, _imageMin.Y, _imageMax.Y);
            float csx1 = Math.Clamp(sx1, _imageMin.X, _imageMax.X);
            float csy1 = Math.Clamp(sy1, _imageMin.Y, _imageMax.Y);

            // Check if mouse is hovering over this element
            bool isHovered = mouseScreen.X >= csx0 && mouseScreen.X <= csx1 &&
                             mouseScreen.Y >= csy0 && mouseScreen.Y <= csy1;

            // Check if this element is blocked by an open overlay above it
            bool blockedByOverlay = IsBlockedByOverlay(elem);

            // Pick colors: hover or normal
            // UseHover controls whether hover colors are applied (per-element toggle).
            // Elements behind an active overlay never show hover.
            bool useHover = isHovered && elem.UseHover && !blockedByOverlay;
            var bgColor = useHover ? elem.HoverBgColor : elem.BgColor;
            var borderColor = useHover ? elem.HoverBorderColor : elem.BorderColor;

            //  Label default: skip background if BgColor is still default (0,0,0) 
            // This makes new Labels transparent by default, but still allows users to
            // customize BgColor/BorderColor for visible backgrounds.
            bool isLabel = elem.Type == UIElementType.Label;
            bool labelDefaultBg = isLabel && bgColor.X < 0.001f && bgColor.Y < 0.001f && bgColor.Z < 0.001f;

            //  Draw background (filled rect)  skip for Labels with default transparent colors 
            // Uses elemOpacity directly as alpha so the element's Opacity property is the sole
            // control for transparency (no hardcoded multiplier).
            if (!labelDefaultBg)
            {
                drawList.AddRectFilled(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(bgColor.X, bgColor.Y, bgColor.Z, 1.0f * elemOpacity)),
                    4f);
            }

            //  Draw image element on top of background 
            bool hasImage = !string.IsNullOrEmpty(elem.ImagePath);
            if (hasImage)
            {
                // Try to load and cache the image texture for preview
                uint texId = LoadOrGetPreviewTexture(elem.ImagePath);
                if (texId != 0)
                {
                    // Get image dimensions for aspect ratio
                    var dims = GetPreviewTextureDimensions(elem.ImagePath);
                    float imgW = dims.Item1 > 0 ? dims.Item1 : 1f;
                    float imgH = dims.Item2 > 0 ? dims.Item2 : 1f;

                    // Calculate draw rect based on ImageMode
                    float drawX, drawY, drawW, drawH;
                    float elemW = sx1 - sx0;
                    float elemH = sy1 - sy0;

                    switch (elem.ImageMode)
                    {
                        case ImageMode.Zoom:
                            {
                                float aspect = imgW / imgH;
                                float elemAspect = elemW / elemH;
                                if (aspect > elemAspect)
                                {
                                    drawW = elemW;
                                    drawH = elemW / aspect;
                                }
                                else
                                {
                                    drawH = elemH;
                                    drawW = elemH * aspect;
                                }
                                drawX = sx0 + (elemW - drawW) * 0.5f;
                                drawY = sy0 + (elemH - drawH) * 0.5f;
                                break;
                            }
                        case ImageMode.Fill:
                            {
                                float aspect = imgW / imgH;
                                float elemAspect = elemW / elemH;
                                if (aspect > elemAspect)
                                {
                                    drawH = elemH;
                                    drawW = elemH * aspect;
                                }
                                else
                                {
                                    drawW = elemW;
                                    drawH = elemW / aspect;
                                }
                                drawX = sx0 + (elemW - drawW) * 0.5f;
                                drawY = sy0 + (elemH - drawH) * 0.5f;
                                break;
                            }
                        default: // Stretch
                            drawX = sx0;
                            drawY = sy0;
                            drawW = elemW;
                            drawH = elemH;
                            break;
}

                    // Draw image
                    drawList.AddImage((nint)texId,
                        new Vector2(drawX, drawY),
                        new Vector2(drawX + drawW, drawY + drawH));

                    // Overlay subtle hover tint (skip when blocked by overlay)
                    if (isHovered && !blockedByOverlay)
                    {
                        drawList.AddRectFilled(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.08f * elemOpacity)));
                    }
                }
                else if (!string.IsNullOrEmpty(elem.FallbackText))
                {
                    // Image not loaded  show fallback text (only if set)
                    drawList.AddRectFilled(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.2f, 0.2f, 0.6f * elemOpacity)));
                    drawList.AddText(new Vector2(csx0 + 4f, csy0 + 4f),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.6f, 0.3f, 1f * elemOpacity)),
                        elem.FallbackText);
                }
            }

            //  Draw text label with element's FontSize (skip for image elements & checkbox  checkbox has its own label rendering) 
            if (!hasImage && elem.Type != UIElementType.Checkbox && !string.IsNullOrEmpty(elem.Text))
            {
                string label = elem.Text;
                float previewFontSize = elem.FontSize > 0f ? Math.Max(8f, elem.FontSize) : 13f;
                var textColor = useHover ? elem.HoverTextColor : elem.TextColor;

                // Use element's custom font if available, fallback to ImGui default
                var rawFont = _bridge.ImGuiCtrl?.GetFont(elem.FontPath, previewFontSize);
                bool hasCustomFont = rawFont != null && (nint)rawFont != IntPtr.Zero;
                var fontToUse = hasCustomFont ? new ImFontPtr(rawFont!) : ImGui.GetFont();

                // Calculate text size using the ACTUAL font for correct alignment
                var textSize = hasCustomFont
                    ? fontToUse.CalcTextSizeA(previewFontSize, float.MaxValue, 0f, label)
                    : ImGui.CalcTextSize(label);

                float textX, textY;
                float textPad = 8f;
                float availW = (csx1 - csx0) - textPad * 2f;
                float textW = Math.Min(textSize.X, availW);
                float textH = textSize.Y;

                switch (elem.Alignment)
                {
                    case TextAlignment.Left:
                        textX = csx0 + textPad;
                        break;
                    case TextAlignment.Right:
                        textX = csx1 - textPad - textW;
                        break;
                    default:
                        textX = csx0 + (csx1 - csx0) * 0.5f - textW * 0.5f;
                        break;
                }
                textY = csy0 + (csy1 - csy0) * 0.5f - textH * 0.5f;
                textX = Math.Max(csx0 + 2f, Math.Min(textX, csx1 - textW - 2f));
                textY = Math.Max(csy0 + 2f, Math.Min(textY, csy1 - textH - 2f));

                if (elem.WordWrap && elem.Type == UIElementType.Label)
                {
                    // Word wrap: split text into lines that fit within the element width
                    var lines = new List<string>();
                    var words = label.Split(' ');
                    string currentLine = "";
                    foreach (var word in words)
                    {
                        string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                        var testSize = hasCustomFont
                            ? fontToUse.CalcTextSizeA(previewFontSize, float.MaxValue, 0f, testLine)
                            : ImGui.CalcTextSize(testLine);
                        if (testSize.X > availW && !string.IsNullOrEmpty(currentLine))
                        {
                            lines.Add(currentLine);
                            currentLine = word;
                        }
                        else
                        {
                            currentLine = testLine;
                        }
                    }
                    if (!string.IsNullOrEmpty(currentLine))
                        lines.Add(currentLine);
                    // Render each line with per-line alignment
                    float lineHeight = previewFontSize * 1.2f;
                    float startY = csy0 + (csy1 - csy0) * 0.5f - (lines.Count * lineHeight) * 0.5f;
                    for (int li = 0; li < lines.Count; li++)
                    {
                        var lineSize = hasCustomFont
                            ? fontToUse.CalcTextSizeA(previewFontSize, float.MaxValue, 0f, lines[li])
                            : ImGui.CalcTextSize(lines[li]);
                        float lineX;
                        switch (elem.Alignment)
                        {
                            case TextAlignment.Left:
                                lineX = csx0 + textPad;
                                break;
                            case TextAlignment.Right:
                                lineX = csx1 - textPad - lineSize.X;
                                break;
                            default: // Center
                                lineX = csx0 + (csx1 - csx0) * 0.5f - lineSize.X * 0.5f;
                                break;
                        }
                        lineX = Math.Max(csx0 + 2f, Math.Min(lineX, csx1 - lineSize.X - 2f));
                        drawList.AddText(fontToUse, previewFontSize, new Vector2(lineX, startY + li * lineHeight),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(textColor.X, textColor.Y, textColor.Z, 1f * elemOpacity)),
                            lines[li]);
                    }
                }
                else
                {
                    drawList.AddText(fontToUse, previewFontSize, new Vector2(textX, textY),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(textColor.X, textColor.Y, textColor.Z, 1f * elemOpacity)),
                        label);
                }
            }

            //  Draw border  skip for Labels with default transparent border colors 
            bool labelDefaultBorder = isLabel && borderColor.X < 0.001f && borderColor.Y < 0.001f && borderColor.Z < 0.001f;
            if (!labelDefaultBorder)
            {
                drawList.AddRect(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(borderColor.X, borderColor.Y, borderColor.Z, 1f * elemOpacity)),
                    4f, ImDrawFlags.None, 1.5f);
            }

            //  Focus highlight (keyboard navigation)  glowing cyan border 
            if (focusedElement != null && elem == focusedElement && isPreview)
            {
                float glowExtra = 3f;
                uint focusCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.8f, 1.0f, 0.9f * elemOpacity));
                uint focusGlow = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.8f, 1.0f, 0.25f * elemOpacity));
                // Outer glow
                drawList.AddRect(new Vector2(csx0 - glowExtra, csy0 - glowExtra), new Vector2(csx1 + glowExtra, csy1 + glowExtra),
                    focusGlow, 4f, ImDrawFlags.None, 4f);
                // Inner bright border
                drawList.AddRect(new Vector2(csx0 - 1f, csy0 - 1f), new Vector2(csx1 + 1f, csy1 + 1f),
                    focusCol, 4f, ImDrawFlags.None, 2f);
            }

            // 
            //  Type-Specific Element Rendering
            // 
            float elemScreenW = sx1 - sx0;
            float elemScreenH = sy1 - sy0;
            float innerPad = 6f;

            if (elem.Type == UIElementType.SliderNumber)
            {
                //  SliderNumber: track + filled portion + thumb + value label 
                float trackY = csy0 + elemScreenH * 0.5f - 3f;
                float trackHStyle = Math.Max(2f, elem.SliderTrackHeight);
                float trackX = csx0 + innerPad;
                float trackW = (csx1 - csx0) - innerPad * 2f;

                // Track background
                float thumbSizeStyle = Math.Max(6f, elem.SliderThumbSize);
                uint trackBgCol = ImGui.ColorConvertFloat4ToU32(new Vector4(elem.SliderTrackColor.X, elem.SliderTrackColor.Y, elem.SliderTrackColor.Z, 0.9f * elemOpacity));
                drawList.AddRectFilled(new Vector2(trackX, trackY), new Vector2(trackX + trackW, trackY + trackHStyle), trackBgCol, 3f);

                // Filled portion
                float t = (elem.MaxValue - elem.MinValue) > 0.001f
                    ? Math.Clamp((elem.CurrentValue - elem.MinValue) / (elem.MaxValue - elem.MinValue), 0f, 1f)
                    : 0f;
                float fillW = trackW * t;
                uint fillColorUi = ImGui.ColorConvertFloat4ToU32(new Vector4(elem.SliderFillColor.X, elem.SliderFillColor.Y, elem.SliderFillColor.Z, 0.9f * elemOpacity));
                drawList.AddRectFilled(new Vector2(trackX, trackY), new Vector2(trackX + fillW, trackY + trackHStyle), fillColorUi, 3f);

                // Thumb handle
                float thumbX = trackX + fillW;
                uint thumbCol = ImGui.ColorConvertFloat4ToU32(new Vector4(elem.SliderThumbColor.X, elem.SliderThumbColor.Y, elem.SliderThumbColor.Z, 1f * elemOpacity));
                uint thumbBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(elem.SliderThumbBorderColor.X, elem.SliderThumbBorderColor.Y, elem.SliderThumbBorderColor.Z, 1f * elemOpacity));
                drawList.AddCircleFilled(new Vector2(thumbX, trackY + trackHStyle * 0.5f), thumbSizeStyle * 0.5f, thumbCol, 16);
                drawList.AddCircle(new Vector2(thumbX, trackY + trackHStyle * 0.5f), thumbSizeStyle * 0.5f, thumbBorder, 16, 1.5f);

                // Value label with position controlled by SliderValuePosition
                string valStr = $"{elem.CurrentValue:F1}";
                var valSize = ImGui.CalcTextSize(valStr);
                if (elem.SliderValuePosition != SliderLabelPosition.None)
                {
                    float valX, valY;
                    switch (elem.SliderValuePosition)
                    {
                        case SliderLabelPosition.Left:
                            valX = csx0 + innerPad;
                            valY = csy0 + (elemScreenH - valSize.Y) * 0.5f;
                            break;
                        case SliderLabelPosition.Top:
                            valX = csx0 + (elemScreenW - valSize.X) * 0.5f;
                            valY = csy0 + 2f;
                            break;
                        case SliderLabelPosition.Bottom:
                            valX = csx0 + (elemScreenW - valSize.X) * 0.5f;
                            valY = csy1 - valSize.Y - 2f;
                            break;
                        default: // Right
                            valX = csx1 - innerPad - valSize.X;
                            valY = csy0 + (elemScreenH - valSize.Y) * 0.5f;
                            break;
                    }
                    uint valColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.9f, 1.0f, 1f * elemOpacity));
                    drawList.AddText(new Vector2(valX, valY), valColor, valStr);
                }

                // Draw step tick marks (when step is significant)
                if (elem.Step > 0.1f && trackW > 80f)
                {
                    int tickCount = Math.Min(20, (int)((elem.MaxValue - elem.MinValue) / elem.Step));
                    if (tickCount > 1 && tickCount <= 30)
                    {
                        uint tickCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.6f, 0.4f));
                        for (int ti = 1; ti < tickCount; ti++)
                        {
                            float frac = (float)ti / tickCount;
                            float tickX = trackX + trackW * frac;
                            drawList.AddLine(
                                new Vector2(tickX, trackY + 1f),
                                new Vector2(tickX, trackY + trackHStyle - 1f),
                                tickCol, 1f);
                        }
                    }
                }

                //  Interactive slider drag (preview mode only) 
                if (isPreview && isHovered && isMouseDown && !blockedByOverlay && trackW > 1f)
                {
                    float mouseRelX = mouseScreen.X - trackX;
                    float frac = Math.Clamp(mouseRelX / trackW, 0f, 1f);
                    float newVal = elem.MinValue + frac * (elem.MaxValue - elem.MinValue);
                    if (elem.Step > 0.001f)
                        newVal = MathF.Round(newVal / elem.Step) * elem.Step;
                    elem.CurrentValue = Math.Clamp(newVal, elem.MinValue, elem.MaxValue);
                }
            }
            else if (elem.Type == UIElementType.SliderText)
            {
                //  SliderText: track + filled portion + thumb + text label 
                float trackY = csy0 + elemScreenH * 0.5f - 3f;
                float trackHStyle = Math.Max(2f, elem.SliderTrackHeight);
                float trackX = csx0 + innerPad;
                float trackW = (csx1 - csx0) - innerPad * 2f;

                // Track background
                float thumbSizeStyle = Math.Max(6f, elem.SliderThumbSize);
                uint trackBgCol = ImGui.ColorConvertFloat4ToU32(new Vector4(elem.SliderTrackColor.X, elem.SliderTrackColor.Y, elem.SliderTrackColor.Z, 0.9f * elemOpacity));
                drawList.AddRectFilled(new Vector2(trackX, trackY), new Vector2(trackX + trackW, trackY + trackHStyle), trackBgCol, 3f);

                // Filled portion (based on selected text index)
                float t = elem.TextOptions.Count > 1
                    ? Math.Clamp((float)elem.SelectedTextIndex / (elem.TextOptions.Count - 1), 0f, 1f)
                    : 0f;
                float fillW = trackW * t;
                uint fillColorUi = ImGui.ColorConvertFloat4ToU32(new Vector4(elem.SliderFillColor.X, elem.SliderFillColor.Y, elem.SliderFillColor.Z, 0.9f * elemOpacity));
                drawList.AddRectFilled(new Vector2(trackX, trackY), new Vector2(trackX + fillW, trackY + trackHStyle), fillColorUi, 3f);

                // Thumb handle
                float thumbX = trackX + fillW;
                uint thumbCol = ImGui.ColorConvertFloat4ToU32(new Vector4(elem.SliderThumbColor.X, elem.SliderThumbColor.Y, elem.SliderThumbColor.Z, 1f * elemOpacity));
                uint thumbBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(elem.SliderThumbBorderColor.X, elem.SliderThumbBorderColor.Y, elem.SliderThumbBorderColor.Z, 1f * elemOpacity));
                drawList.AddCircleFilled(new Vector2(thumbX, trackY + trackHStyle * 0.5f), thumbSizeStyle * 0.5f, thumbCol, 16);
                drawList.AddCircle(new Vector2(thumbX, trackY + trackHStyle * 0.5f), thumbSizeStyle * 0.5f, thumbBorder, 16, 1.5f);

                // Selected text label with position controlled by SliderValuePosition
                string selText = elem.SelectedTextIndex >= 0 && elem.SelectedTextIndex < elem.TextOptions.Count
                    ? elem.TextOptions[elem.SelectedTextIndex]
                    : "?";
                var selSize = ImGui.CalcTextSize(selText);
                if (elem.SliderValuePosition != SliderLabelPosition.None)
                {
                    float selX, selY;
                    switch (elem.SliderValuePosition)
                    {
                        case SliderLabelPosition.Left:
                            selX = csx0 + innerPad;
                            selY = csy0 + (elemScreenH - selSize.Y) * 0.5f;
                            break;
                        case SliderLabelPosition.Top:
                            selX = csx0 + (elemScreenW - selSize.X) * 0.5f;
                            selY = csy0 + 2f;
                            break;
                        case SliderLabelPosition.Bottom:
                            selX = csx0 + (elemScreenW - selSize.X) * 0.5f;
                            selY = csy1 - selSize.Y - 2f;
                            break;
                        default: // Right
                            selX = csx1 - innerPad - selSize.X;
                            selY = csy0 + (elemScreenH - selSize.Y) * 0.5f;
                            break;
                    }
                    uint selColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.7f, 1.0f, 1f * elemOpacity));
                    drawList.AddText(new Vector2(selX, selY), selColor, selText);
                }

                // Tick marks for each text option
                if (elem.TextOptions.Count > 1 && trackW > 80f)
                {
                    uint tickCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.7f, 0.4f * elemOpacity));
                    for (int ti = 0; ti < elem.TextOptions.Count; ti++)
                    {
                        float frac = elem.TextOptions.Count > 1 ? (float)ti / (elem.TextOptions.Count - 1) : 0f;
                        float tickX = trackX + trackW * frac;
                        drawList.AddLine(
                            new Vector2(tickX, trackY + 1f),
                            new Vector2(tickX, trackY + trackHStyle - 1f),
                            tickCol, 1.5f);
                    }
                }

                //  Interactive slider drag (preview mode only) 
                if (isPreview && isHovered && isMouseDown && !blockedByOverlay && trackW > 1f && elem.TextOptions.Count > 0)
                {
                    float mouseRelX = mouseScreen.X - trackX;
                    float frac = Math.Clamp(mouseRelX / trackW, 0f, 1f);
                    int newIdx = (int)Math.Round(frac * (elem.TextOptions.Count - 1));
                    newIdx = Math.Clamp(newIdx, 0, elem.TextOptions.Count - 1);
                    elem.SelectedTextIndex = newIdx;
                }
            }
            else if (elem.Type == UIElementType.Checkbox)
            {
                //  Checkbox: square + checkmark + label 
                float boxSize = Math.Min(24f, elemScreenH - innerPad * 2f);
                float boxX = csx0 + innerPad;
                float boxY = csy0 + (elemScreenH - boxSize) * 0.5f;

                // Checkbox square
                uint chkBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.55f, 1f * elemOpacity));
                var chkCheckedBg = elem.CheckedBgColor;
                var chkUncheckedBg = elem.UncheckedBgColor;
                uint chkBg = ImGui.ColorConvertFloat4ToU32(elem.IsChecked
                    ? new Vector4(chkCheckedBg.X, chkCheckedBg.Y, chkCheckedBg.Z, 0.9f * elemOpacity)
                    : new Vector4(chkUncheckedBg.X, chkUncheckedBg.Y, chkUncheckedBg.Z, 0.9f * elemOpacity));
                drawList.AddRectFilled(new Vector2(boxX, boxY), new Vector2(boxX + boxSize, boxY + boxSize), chkBg, 4f);
                drawList.AddRect(new Vector2(boxX, boxY), new Vector2(boxX + boxSize, boxY + boxSize), chkBorder, 4f, ImDrawFlags.None, 1.5f);

                // Checkmark (when checked)
                if (elem.IsChecked)
                {
                    var cmCol = elem.CheckmarkColor;
                    uint checkCol = ImGui.ColorConvertFloat4ToU32(new Vector4(cmCol.X, cmCol.Y, cmCol.Z, 1f * elemOpacity));
                    float cx = boxX + boxSize * 0.5f;
                    float cy = boxY + boxSize * 0.5f;
                    float cs = boxSize * 0.25f;
                    drawList.AddLine(new Vector2(cx - cs, cy), new Vector2(cx - cs * 0.2f, cy + cs * 0.7f), checkCol, 2.5f);
                    drawList.AddLine(new Vector2(cx - cs * 0.2f, cy + cs * 0.7f), new Vector2(cx + cs * 0.8f, cy - cs * 0.5f), checkCol, 2.5f);
                }

                // Label text
                string chkLabel = !string.IsNullOrEmpty(elem.Text) ? elem.Text : elem.Name;
                float lblX = boxX + boxSize + innerPad;
                var chkFontRaw = _bridge.ImGuiCtrl?.GetFont(elem.FontPath, 13f);
                bool hasChkFont = chkFontRaw != null && (nint)chkFontRaw != IntPtr.Zero;
                var chkFont = hasChkFont ? new ImFontPtr(chkFontRaw!) : ImGui.GetFont();
                float chkFontSize = elem.FontSize > 0f ? Math.Max(8f, elem.FontSize) : 13f;
                var chkTextSize = hasChkFont
                    ? chkFont.CalcTextSizeA(chkFontSize, float.MaxValue, 0f, chkLabel)
                    : ImGui.CalcTextSize(chkLabel);
                float lblY = csy0 + (elemScreenH - chkTextSize.Y) * 0.5f;
                uint lblCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.85f, 0.95f, 1f * elemOpacity));
                drawList.AddText(chkFont, chkFontSize, new Vector2(lblX, lblY), lblCol, chkLabel);
            }
            else if (elem.Type == UIElementType.Dropdown)
            {
                //  Dropdown: box + selected text + dropdown arrow 
                float arrowSize = 10f;
                float arrowX = csx1 - innerPad - arrowSize;
                float arrowY = csy0 + (elemScreenH - arrowSize) * 0.5f;

                // Selected value text
                string selText = elem.SelectedIndex >= 0 && elem.SelectedIndex < elem.Options.Count
                    ? elem.Options[elem.SelectedIndex]
                    : "(select)";
                uint ddTextCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.85f, 0.95f, 1f * elemOpacity));

                float ddFontSize = elem.FontSize > 0f ? Math.Max(8f, elem.FontSize) : 13f;
                var ddFontRaw = _bridge.ImGuiCtrl?.GetFont(elem.FontPath, ddFontSize);
                bool hasDdFont = ddFontRaw != null && (nint)ddFontRaw != IntPtr.Zero;
                var ddFont = hasDdFont ? new ImFontPtr(ddFontRaw!) : ImGui.GetFont();

                // Truncate if too wide
                float maxTextW = (csx1 - csx0) - innerPad * 3f - arrowSize;
                var ddTextSize = hasDdFont
                    ? ddFont.CalcTextSizeA(ddFontSize, float.MaxValue, 0f, selText)
                    : ImGui.CalcTextSize(selText);
                if (ddTextSize.X > maxTextW)
                {
                    while (selText.Length > 1)
                    {
                        var testSize = hasDdFont
                            ? ddFont.CalcTextSizeA(ddFontSize, float.MaxValue, 0f, selText + "...")
                            : ImGui.CalcTextSize(selText + "...");
                        if (testSize.X <= maxTextW) break;
                        selText = selText[..^1];
                    }
                    selText += "...";
                }

                float ddTextX = csx0 + innerPad;
                float ddTextY = csy0 + (elemScreenH - ddTextSize.Y) * 0.5f;
                drawList.AddText(ddFont, ddFontSize, new Vector2(ddTextX, ddTextY), ddTextCol, selText);

                // Dropdown arrow icon
                var arrCol = elem.ArrowColor;
                uint arrowCol = ImGui.ColorConvertFloat4ToU32(new Vector4(arrCol.X, arrCol.Y, arrCol.Z, 1f * elemOpacity));
                float arrowHalf = arrowSize * 0.5f;
                drawList.AddTriangleFilled(
                    new Vector2(arrowX, arrowY),
                    new Vector2(arrowX + arrowSize, arrowY),
                    new Vector2(arrowX + arrowHalf, arrowY + arrowSize * 0.7f),
                    arrowCol);

                // Placeholder when no selection
                if (elem.SelectedIndex < 0 || elem.SelectedIndex >= elem.Options.Count)
                {
                    string phText = "Select...";
                    uint phCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 0.6f * elemOpacity));
                    drawList.AddText(new Vector2(ddTextX, ddTextY), phCol, phText);
                }
            }
            else if (elem.Type == UIElementType.TextBox)
            {
                //  TextBox: input field with placeholder or current text 
                float inputPadX = 10f;
                float inputX = csx0 + inputPadX;
                float inputY = csy0 + 4f;
                float inputW = (csx1 - csx0) - inputPadX * 2f;
                float inputH = elemScreenH - 8f;

                // Input background (slightly lighter)
                uint inputBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.12f, 0.12f, 0.18f, 0.9f * elemOpacity));
                drawList.AddRectFilled(new Vector2(inputX, inputY), new Vector2(inputX + inputW, inputY + inputH), inputBg, 3f);

                // Text content
                string displayText = !string.IsNullOrEmpty(elem.InputText) ? elem.InputText : elem.Placeholder;
                bool isPlaceholder = string.IsNullOrEmpty(elem.InputText);

                float tbFontSize = elem.FontSize > 0f ? Math.Max(8f, elem.FontSize) : 13f;
                var tbFontRaw = _bridge.ImGuiCtrl?.GetFont(elem.FontPath, tbFontSize);
                bool hasTbFont = tbFontRaw != null && (nint)tbFontRaw != IntPtr.Zero;
                var tbFont = hasTbFont ? new ImFontPtr(tbFontRaw!) : ImGui.GetFont();

                // Truncate to fit
                var tbTextSize = hasTbFont
                    ? tbFont.CalcTextSizeA(tbFontSize, float.MaxValue, 0f, displayText)
                    : ImGui.CalcTextSize(displayText);
                float maxTextW = inputW - 8f;
                if (tbTextSize.X > maxTextW)
                {
                    while (displayText.Length > 1)
                    {
                        var testSize = hasTbFont
                            ? tbFont.CalcTextSizeA(tbFontSize, float.MaxValue, 0f, displayText + "...")
                            : ImGui.CalcTextSize(displayText + "...");
                        if (testSize.X <= maxTextW) break;
                        displayText = displayText[..^1];
                    }
                    displayText += "...";
                }

                uint tbTextCol = ImGui.ColorConvertFloat4ToU32(isPlaceholder
                    ? new Vector4(0.5f, 0.5f, 0.5f, 0.7f * elemOpacity)
                    : new Vector4(0.85f, 0.85f, 0.95f, 1f * elemOpacity));
                float tbTextX = inputX + 6f;
                float tbTextY = inputY + (inputH - tbTextSize.Y) * 0.5f;
                drawList.AddText(tbFont, tbFontSize, new Vector2(tbTextX, tbTextY), tbTextCol, displayText);

                // Blinking cursor indicator (when text is entered and element is hovered)
                if (!string.IsNullOrEmpty(elem.InputText) && isHovered)
                {
                    float cursorX = tbTextX + tbTextSize.X + 2f;
                    float cursorH = tbTextSize.Y * 0.8f;
                    float cursorY = tbTextY + (tbTextSize.Y - cursorH) * 0.5f;
                    var curCol = elem.CursorColor;
                    uint cursorCol = ImGui.ColorConvertFloat4ToU32(new Vector4(curCol.X, curCol.Y, curCol.Z, 0.8f * elemOpacity));
                    drawList.AddLine(
                        new Vector2(cursorX, cursorY),
                        new Vector2(cursorX, cursorY + cursorH),
                        cursorCol, 1.5f);
                }

                // Length indicator
                int maxLen = elem.MaxLength;
                if (maxLen > 0)
                {
                    string lenStr = $"{elem.InputText.Length}/{maxLen}";
                    var lenSize = ImGui.CalcTextSize(lenStr);
                    float lenX = csx1 - inputPadX - lenSize.X;
                    float lenY = inputY + inputH - lenSize.Y - 2f;
                    uint lenCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.6f, 0.7f * elemOpacity));
                    drawList.AddText(new Vector2(lenX, lenY), lenCol, lenStr);
                }
            }

            //  Click handling 
            // Preview mode: trigger behavior; Editor mode: select element
            // blockedByOverlay already computed above  blocks clicks on elements behind an overlay
            // Use leftClicked directly AND also check IsMouseClicked for in-game mode (no ImGui windows)
            bool clickActive = leftClicked || ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            // Drag guard: in editor mode, skip clicks while dragging; preview mode: always allow
            bool dragOk = isPreview || _dragMode == DragMode.None;
            // Mouse click: must be hovered. Keyboard activation: bypass hover check for focused element.
            bool mouseClick = isHovered && clickActive;
            bool keyActivate = isPreview && keyboardActivate && focusedElement != null && elem == focusedElement;
            if ((mouseClick || keyActivate) && dragOk && !blockedByOverlay)
            {
                // Sync mouse click to keyboard focus (in-game mode only)
                if (mouseClick && isPreview)
                    LastInGameClickedElement = elem;

                if (isPreview)
                {
                    //  Checkbox: ALWAYS toggle first (primary action), then run OnClick if present 
                    if (elem.Type == UIElementType.Checkbox)
                    {
                        elem.IsChecked = !elem.IsChecked;
                        Console.WriteLine($"[Viewport] Checkbox '{elem.Name}' toggled: {elem.IsChecked}");

                        // Still run OnClick delegate if set (custom handler)
                        if (elem.OnClick != null)
                        {
                            try { elem.OnClick.Invoke(); }
                            catch (Exception ex) { Console.WriteLine($"[Viewport] OnClick error for '{elem.Name}': {ex.Message}"); }
                        }
                    }
                    // Custom handler (OnClick delegate) takes priority for non-checkbox elements
                    else if (elem.OnClick != null)
                    {
                        try { elem.OnClick.Invoke(); }
                        catch (Exception ex) { Console.WriteLine($"[Viewport] OnClick error for '{elem.Name}': {ex.Message}"); }
                    }
                    // Custom ClickBehaviorLabel takes priority over defaults
                    else if (!string.IsNullOrEmpty(elem.ClickBehaviorLabel))
                    {
                        string behavior = elem.ClickBehaviorLabel.ToLowerInvariant();
                        if (behavior == "closeoverlay" || behavior == "cancel")
                        {
                            HandleCloseOverlay(elem);
                        }
                        else
                        {
                            HandlePreviewBehavior(elem);
                        }
                    }
                    //  Default interactive element behaviors (fallback when no custom handler) 
                    else if (elem.Type == UIElementType.Dropdown && elem.Options.Count > 0)
                    {
                        elem.SelectedIndex = (elem.SelectedIndex + 1) % elem.Options.Count;
                        Console.WriteLine($"[Viewport] Dropdown '{elem.Name}' → '{elem.Options[elem.SelectedIndex]}'");
                    }


                }
                else
                {
                    // Editor mode: select element for inspection
                    _bridge.SelectedUIElement = elem;
                    _bridge.SelectedUIElements.Clear();
                    _bridge.SelectedUIElements.Add(elem);
                    Console.WriteLine($"[Viewport] Selected '{elem.Name}' in editor");
                }
            }


            //  Always recurse for children (so they render regardless of click state) 
            if (elem.Children.Count > 0)
                DrawEditorUIPreview(drawList, elem.Children, mouseScreen, leftClicked, isPreview, isMouseDown, focusedElement, keyboardActivate);
        }
    }



    //  Preview texture cache for viewport editor 
    private readonly Dictionary<string, uint> _previewTextureCache = [];
    private readonly Dictionary<string, (int w, int h)> _previewTextureDims = [];
    // Tracks the last scene root to detect scene switches and clear the cache
    private UIElement? _lastSceneRoot = null;

    /// <summary>Delete all cached preview textures and clear caches to prevent GPU leaks on scene switch.</summary>
    private void ClearPreviewTextures()
    {
        foreach (var kvp in _previewTextureCache)
        {
            uint tex = kvp.Value;
            if (tex != 0)
                GL.DeleteTextures(1, &tex);
        }
        _previewTextureCache.Clear();
        _previewTextureDims.Clear();
    }

    private unsafe uint LoadOrGetPreviewTexture(string path)
    {
        // Resolve relative image paths against the exe folder.
        path = PathHelpers.Resolve(path);

        if (_previewTextureCache.TryGetValue(path, out uint cached))
            return cached;

        if (!File.Exists(path))
        {
            Console.WriteLine($"[Viewport] Image file not found: {path}");
            return 0;
        }
        Console.WriteLine($"[Viewport] Loading image: {path}");

        try
        {
            uint texID;
            GL.GenTextures(1, &texID);
            GL.BindTexture(Const.GL_TEXTURE_2D, texID);

            using var stream = File.OpenRead(path);
            var image = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);

            fixed (byte* ptr = image.Data)
            {
                GL.TexImage2D(Const.GL_TEXTURE_2D, 0, (int)Const.GL_RGBA,
                              image.Width, image.Height, 0,
                              Const.GL_RGBA, Const.GL_UNSIGNED_BYTE, ptr);
            }

            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_S, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_WRAP_T, (int)Const.GL_CLAMP_TO_EDGE);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MIN_FILTER, (int)Const.GL_LINEAR);
            GL.TexParameteri(Const.GL_TEXTURE_2D, Const.GL_TEXTURE_MAG_FILTER, (int)Const.GL_LINEAR);

            _previewTextureCache[path] = texID;
            _previewTextureDims[path] = (image.Width, image.Height);
            return texID;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Viewport] Failed to load image '{path}': {ex.Message}");
            return 0;
        }
    }

    private (int w, int h) GetPreviewTextureDimensions(string path)
    {
        if (_previewTextureDims.TryGetValue(path, out var dims))
            return dims;
        return (0, 0);
    }

    /// <summary>Handle a UI element click in preview mode.
    /// Parses ClickBehaviorLabel and executes the corresponding action.
    /// Supported formats:
    /// - "overlay:DialogName" → toggle overlay visibility
    /// - "closeoverlay" → close parent overlay/dialog
    /// - "scene:SceneName" → switch to named scene
    /// - "exit" → preview→edit mode or close app
    /// Legacy formats (cancel, confirmexit, showexitconfirm, etc.) are also supported.</summary>
    private void HandlePreviewBehavior(UIElement elem)
    {
        if (string.IsNullOrEmpty(elem.ClickBehaviorLabel))
            return;

        var (type, param) = IDEBridge.ParseBehavior(elem.ClickBehaviorLabel);

        switch (type)
        {
            case "overlay":
                HandleOverlayToggle(param);
                break;

            case "closeoverlay":
            case "cancel":
                HandleCloseOverlay(elem);
                break;

            case "scene":
                if (!string.IsNullOrEmpty(param))
                {
                    // Try exact match first, then fall back to case-insensitive lookup to
                    // tolerate differences in naming/casing between saved behavior strings
                    // and the editor's scene keys.
                    if (_bridge.EditorScenes.TryGetValue(param, out var targetScene))
                    {
                        Console.WriteLine($"[Viewport] scene:{param} → switching to editor scene (exact)");
                    }
                    else
                    {
                        // Case-insensitive search
                        string? foundKey = null;
                        foreach (var k in _bridge.EditorScenes.Keys)
                        {
                            if (string.Equals(k, param, StringComparison.OrdinalIgnoreCase))
                            {
                                foundKey = k;
                                break;
                            }
                        }
                        if (foundKey != null)
                        {
                            targetScene = _bridge.EditorScenes[foundKey];
                            param = foundKey; // normalize to actual key
                            Console.WriteLine($"[Viewport] scene:{param} → switching to editor scene (case-insensitive)");
                        }
                    }

                    if (targetScene != null)
                    {
                        // Switch the editor scene root to the target scene
                        _bridge.SelectedEditorScene = param;
                        _bridge.SceneRoot = targetScene.Root;
                        _bridge.SceneRootElements = new List<UIElement> { targetScene.Root }.AsReadOnly();
                        // Reset overlay visibility for the new scene
                        ResetSceneOverlays();
                    }
                    else
                    {
                        Console.WriteLine($"[Viewport] scene:{param} → scene not found in editor scenes");
                    }
                }
                break;

            case "exit":
            case "exitgame":
            case "confirmexit":
            case "yes":
                HandleExit();
                break;

            case "showexitconfirm":
                HandleOverlayToggle("ExitConfirm");
                break;

            case "cancelsettings":
            case "discardchanges":
            case "keepediting":
                // Legacy: close all containers
                if (_bridge.SceneRoot != null)
                {
                    foreach (var child in _bridge.SceneRoot.Children)
                    {
                        if (child.Type == UIElementType.Container)
                            child.IsVisible = false;
                    }
                }
                break;

            default:
                Console.WriteLine($"[Viewport] Unknown behavior: '{type}' (raw: '{elem.ClickBehaviorLabel}')");
                break;
        }
    }

    /// <summary>Toggle an overlay's visibility. Finds the overlay by name and toggles it.
    /// If the overlay doesn't exist and it's "ExitConfirm", auto-creates it.</summary>
    private void HandleOverlayToggle(string overlayName)
    {
        if (string.IsNullOrEmpty(overlayName) || _bridge.SceneRoot == null)
            return;

        // Determine current visibility
        bool isCurrentlyVisible = false;
        foreach (var child in _bridge.SceneRoot.Children)
        {
            if (child.Name == overlayName)
            {
                isCurrentlyVisible = child.IsVisible;
                break;
            }
        }

        // Toggle: close if open, open if closed
        bool newVisible = !isCurrentlyVisible;

        bool found = FindAndToggleDialog(_bridge.SceneRoot.Children, overlayName, newVisible);
        if (!found && newVisible && overlayName == "ExitConfirm")
        {
            CreateDefaultExitConfirmDialog();
            FindAndToggleDialog(_bridge.SceneRoot.Children, overlayName, true);
            Console.WriteLine($"[Viewport] overlay:{overlayName} → auto-created and shown");
        }
        else
        {
            Console.WriteLine($"[Viewport] overlay:{overlayName} → IsVisible={newVisible} (found={found})");
        }
    }

    /// <summary>Exit action:
    /// - Viewport preview mode → back to editor
    /// - In-game mode (F8 fullscreen) → close the app
    /// - In-game input mode (F9 active) → back to editor
    /// - Otherwise → close the app</summary>
    private void HandleExit()
    {
        //  In-game mode (F8 fullscreen): close the app entirely 
        if (_fullscreenMode)
        {
            Console.WriteLine("[Viewport] exit → closing app (in-game mode, via GLFW)");
            nint window = Glfw.GetWindow();
            if (window != nint.Zero)
                Glfw.SetWindowShouldClose(window, 1);
        }
        //  Viewport preview mode (F5): back to editor 
        else if (_previewMode)
        {
            Console.WriteLine("[Viewport] exit → exiting preview mode, resetting overlays");
            _previewMode = false;
            _bridge.IsPreviewMode = false; // Back to edit mode: show editor gizmos/helpers
            ResetSceneOverlays();
            // Reset all edit-mode actions to default/off
            _bridge.TerrainBrushActive = false;
            _bridge.TerrainBrushMode = 0;
            _bridge.GizmoMode = 0; // Translate (default)
            if (_bridge.EditorGizmo != null)
            {
                _bridge.EditorGizmo.Mode = TransformGizmo.GizmoMode.Translate;
                _bridge.EditorGizmo.EndDrag();
            }
            ClearBrushIndicator();
        }
        //  In-game input mode (F9 active): back to editor 
        else if (_bridge.InGameActive && _bridge.SceneManager != null)
        {
            Console.WriteLine("[Viewport] exit → back to edit mode");
            _bridge.InGameActive = false;
            if (_bridge.SceneRoot != null)
            {
                foreach (var child in _bridge.SceneRoot.Children)
                    child.IsVisible = false;
            }
        }
        //  Otherwise: close the app 
        else
        {
            Console.WriteLine("[Viewport] exit → stopping app");
            _bridge.SceneManager?.Stop();
        }
    }

    /// <summary>Close the parent overlay/container of the clicked element.
    /// Walks up the parent chain to find the nearest Container and hides it.
    /// If no parent container found, closes all containers in the scene root.</summary>
    private void HandleCloseOverlay(UIElement elem)
    {
        // Find the parent container and close it
        var parent = elem.Parent;
        while (parent != null)
        {
            if (parent.Type == UIElementType.Container)
            {
                parent.IsVisible = false;
                foreach (var child in parent.Children)
                    child.IsVisible = false;
                Console.WriteLine($"[Viewport] closeoverlay → closed '{parent.Name}'");
                return;
            }
            parent = parent.Parent;
        }

        // If no parent container found, close all containers in scene root
        if (_bridge.SceneRoot != null)
        {
            foreach (var child in _bridge.SceneRoot.Children)
            {
                if (child.Type == UIElementType.Container)
                    child.IsVisible = false;
            }
            Console.WriteLine("[Viewport] closeoverlay → closed all containers (no parent found)");
        }
    }

    /// <summary>Create the default ExitConfirm dialog elements inside SceneRoot for preview mode.
    /// Mirrors the same structure that MainMenuScene.EnsureDefaultUI() would create at runtime.
    /// IMPORTANT: Children get ABSOLUTE scene coordinates (not relative to dialog parent),
    /// because DrawEditorUIPreview renders all elements with absolute positions.</summary>
    private void CreateDefaultExitConfirmDialog()
    {
        if (_bridge.SceneRoot == null) return;

        // Check if already exists (double-check)
        foreach (var child in _bridge.SceneRoot.Children)
            if (child.Type == UIElementType.Container && child.Name == "ExitConfirm")
                return;

        int w = _bridge.SceneTextureWidth > 0 ? _bridge.SceneTextureWidth : 1920;
        int h = _bridge.SceneTextureHeight > 0 ? _bridge.SceneTextureHeight : 1080;
        float dlgW = 440f;
        float dlgH = 210f;
        float dlgX = (w - dlgW) * 0.5f;
        float dlgY = (h - dlgH) * 0.5f;

        var exitDlg = new UIElement
        {
            Name = "ExitConfirm",
            Type = UIElementType.Container,
            Text = "",
            IsVisible = true,
            X = dlgX,
            Y = dlgY,
            Width = dlgW,
            Height = dlgH,
            BgColor = new Vector3(0.12f, 0.13f, 0.19f),
            BorderColor = new Vector3(0.5f, 0.3f, 0.3f),
        };

        // Dark overlay behind the dialog  absolute position (full screen)
        var overlay = new UIElement
        {
            Name = "ExitDlgOverlay",
            Type = UIElementType.Container,
            Text = "",
            IsVisible = true,
            BgColor = new Vector3(0f, 0f, 0f) * 0.55f,
            X = 0, Y = 0, Width = w, Height = h,
        };
        exitDlg.AddChild(overlay);

        // Title label  use ABSOLUTE coordinates
        var title = new UIElement
        {
            Name = "ExitDlgTitle",
            Type = UIElementType.Label,
            Text = "Exit Game?",
            FontSize = 32f,
            FontPath = "Artifacts\\fonts\\Worldstar.ttf",
            TextColor = new Vector3(1f, 1f, 1f),
            Alignment = TextAlignment.Center,
            IsVisible = true,
            X = dlgX + (dlgW - 160f) * 0.5f,
            Y = dlgY + 20f,
            Width = 160f,
            Height = 40f,
        };
        exitDlg.AddChild(title);

        // Message label  use ABSOLUTE coordinates
        var message = new UIElement
        {
            Name = "ExitDlgMessage",
            Type = UIElementType.Label,
            Text = "Are you sure you want to exit?",
            FontSize = 18f,
            FontPath = "Artifacts\\fonts\\Worldstar.ttf",
            TextColor = new Vector3(0.85f, 0.85f, 0.9f),
            Alignment = TextAlignment.Center,
            IsVisible = true,
            X = dlgX + (dlgW - 250f) * 0.5f,
            Y = dlgY + 65f,
            Width = 250f,
            Height = 30f,
        };
        exitDlg.AddChild(message);

        // Cancel button (left)  use ABSOLUTE coordinates
        var cancelBtn = new UIElement
        {
            Name = "ExitDlgCancel",
            Type = UIElementType.Button,
            Text = "Cancel",
            Width = 150f, Height = 44f,
            X = dlgX + dlgW * 0.5f - 150f - 10f,
            Y = dlgY + 115f,
            FontSize = 20f,
            FontPath = "Artifacts\\fonts\\Worldstar.ttf",
            TextColor = new Vector3(0.95f, 0.95f, 1f),
            BgColor = new Vector3(0.12f, 0.13f, 0.18f),
            HoverBgColor = new Vector3(0.22f, 0.28f, 0.45f),
            BorderColor = new Vector3(0.15f, 0.18f, 0.25f),
            HoverBorderColor = new Vector3(0.5f, 0.6f, 1.0f),
            Alignment = TextAlignment.Center,
            IsVisible = true,
            ClickBehaviorLabel = "cancel",
        };
        exitDlg.AddChild(cancelBtn);

        // Yes, Exit button (right)  use ABSOLUTE coordinates
        var exitBtn = new UIElement
        {
            Name = "ExitDlgConfirm",
            Type = UIElementType.Button,
            Text = "Yes, Exit",
            Width = 150f, Height = 44f,
            X = dlgX + dlgW * 0.5f + 10f,
            Y = dlgY + 115f,
            FontSize = 20f,
            FontPath = "Artifacts\\fonts\\Worldstar.ttf",
            TextColor = new Vector3(1f, 1f, 1f),
            BgColor = new Vector3(0.35f, 0.10f, 0.12f),
            HoverBgColor = new Vector3(0.55f, 0.20f, 0.22f),
            BorderColor = new Vector3(0.5f, 0.2f, 0.2f),
            HoverBorderColor = new Vector3(0.8f, 0.4f, 0.4f),
            Alignment = TextAlignment.Center,
            IsVisible = true,
            ClickBehaviorLabel = "exit",
        };
        exitDlg.AddChild(exitBtn);

        _bridge.SceneRoot.AddChild(exitDlg);
        Console.WriteLine("[Viewport] Created default ExitConfirm dialog for preview mode.");
    }

    /// <summary>Check if an element is blocked by an open overlay (Dialog/Container) above it.
    /// Elements outside the overlay are blocked; elements inside (or the overlay itself) are not.</summary>
    private bool IsBlockedByOverlay(UIElement elem)
    {
        if (_bridge.SceneRoot == null) return false;

        // Find the first visible overlay (Container) at root level
        UIElement? activeOverlay = null;
        foreach (var child in _bridge.SceneRoot.Children)
        {
            if (child.IsVisible && child.Type == UIElementType.Container)
            {
                activeOverlay = child;
                break;
            }
        }

        if (activeOverlay == null) return false; // no overlay open
        if (elem == activeOverlay) return false;  // the overlay itself is clickable

        // Check if elem is a descendant of the active overlay
        var parent = elem.Parent;
        while (parent != null)
        {
            if (parent == activeOverlay) return false; // descendant → not blocked
            parent = parent.Parent;
        }

        // Element is outside the overlay → blocked
        return true;
    }

    /// <summary>Reset overlay visibility and activate first-level children.
    /// Non-container children → visible. Containers keep their current state.</summary>
    private void ResetSceneOverlays()
    {
        if (_bridge.SceneRoot == null) return;
        foreach (var child in _bridge.SceneRoot.Children)
        {
            if (child.Type != UIElementType.Scene && child.Type != UIElementType.Container)
            {
                child.IsVisible = true;
            }
        }
    }

    /// <summary>Check if the currently selected editor scene is of the given type.</summary>
    private bool IsEditorSceneType(IDEBridge.SceneType type)
    {
        if (_bridge.SelectedEditorScene == null) return false;
        if (_bridge.EditorScenes.TryGetValue(_bridge.SelectedEditorScene, out var editorScene))
            return editorScene.Type == type;
        return false;
    }

    /// <summary>Recursively find a dialog element by name and set its visibility.
    /// Also syncs ALL children visibility to match the dialog.</summary>
    private static bool FindAndToggleDialog(List<UIElement> elements, string name, bool visible)
    {
        foreach (var child in elements)
        {
            if (child.Type == UIElementType.Container && child.Name == name)
            {
                child.IsVisible = visible;
                // Sync ALL children visibility to match parent
                foreach (var sub in child.Children)
                    sub.IsVisible = visible;
                return true;
            }
            if (child.Children.Count > 0)
            {
                if (FindAndToggleDialog(child.Children, name, visible))
                    return true;
            }
        }
        return false;
    }

    public ViewportPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Viewport", null, ref _visible);

    //  Public API for main menu bar integration 
    /// <summary>Whether preview mode is active (hides editor helpers, shows scene as-in-game).</summary>
    public bool PreviewMode
    {
        get => _previewMode;
        set
        {
            if (value && !_previewMode)
            {
                //  Entering Preview mode 
                ResetSceneOverlays();
                ClearBrushIndicator();
                _bridge.TerrainBrushActive = false;
                _bridge.TerrainBrushMode = 0;
            }
            else if (!value && _previewMode)
            {
                //  Exiting Preview mode (back to Edit) 
                // Reset all edit-mode actions to default/off
                _bridge.TerrainBrushActive = false;
                _bridge.TerrainBrushMode = 0;
                _bridge.GizmoMode = 0; // Translate (default)
                if (_bridge.EditorGizmo != null)
                {
                    _bridge.EditorGizmo.Mode = TransformGizmo.GizmoMode.Translate;
                    _bridge.EditorGizmo.EndDrag();
                }
                ClearBrushIndicator();
            }
            _previewMode = value;
            // Set preview mode flag  hides editor gizmos/helpers without changing camera behavior.
            _bridge.IsPreviewMode = value;
        }
    }
    /// <summary>Whether snap-to-grid is enabled.</summary>
    public bool SnapEnabled { get => _snapEnabled; set => _snapEnabled = value; }

    /// <summary>Current snap grid size (px).</summary>
    public float SnapGridSize { get => _snapGridSize; set => _snapGridSize = value; }

    /// <summary>Persist the viewport grid/snap prefs to settings.json so they survive restarts.
    /// The grid on/off and snap values previously reset to their defaults (on/20px) every launch.</summary>
    public void PersistViewportPrefs()
    {
        try
        {
            var settings = SettingsSave.Load();
            settings.ShowDebugGrid = _bridge.ShowDebugGrid;
            settings.ShowShadows = _bridge.ShowShadows;
            settings.SnapEnabled = _snapEnabled;
            settings.SnapGridSize = _snapGridSize;
            SettingsSave.Save(settings);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Viewport] Failed to persist viewport prefs: {ex.Message}");
        }
    }

    /// <summary>Toggle preview mode on/off.</summary>
    public void TogglePreviewMode()
    {
        PreviewMode = !_previewMode;
    }

    /// <summary>Set fullscreen mode (used by In-Game Mode F8).
    /// When true, toolbar is hidden and window gets NoTitleBar|NoResize flags.</summary>
    public void SetFullscreen(bool fullscreen) => _fullscreenMode = fullscreen;

    /// <summary>Render UI elements to a draw list at the specified canvas coordinates.
    /// Used by In-Game Mode (F8) to display loaded scenes without any ImGui windows.
    /// Optional focusedElement is highlighted with a glow border for keyboard navigation.
    /// keyboardActivate signals that Enter/Space was pressed for the focused element.</summary>
    public void RenderUIElements(
        ImDrawListPtr drawList,
        Vector2 canvasMin, Vector2 canvasMax,
        float texW, float texH,
        IReadOnlyList<UIElement> elements,
        Vector2 mouseScreen, bool leftClicked, bool isPreview = false, bool isMouseDown = false,
        UIElement? focusedElement = null, bool keyboardActivate = false)
    {
        var savedMin = _imageMin;
        var savedMax = _imageMax;
        var savedSize = _imageSize;
        float savedTexW = _texW;
        float savedTexH = _texH;

        _imageMin = canvasMin;
        _imageMax = canvasMax;
        _imageSize = new Vector2(canvasMax.X - canvasMin.X, canvasMax.Y - canvasMin.Y);
        _texW = texW;
        _texH = texH;

        DrawEditorUIPreview(drawList, elements, mouseScreen, leftClicked, isPreview, isMouseDown, focusedElement, keyboardActivate);

        _imageMin = savedMin;
        _imageMax = savedMax;
        _imageSize = savedSize;
        _texW = savedTexW;
        _texH = savedTexH;
    }

    

    /// <summary>True when the mouse currently hovers the SINGLE selection gizmo
    /// (group-center gizmo for multi-select). Used to keep marquee selection from
    /// stealing a click that is really meant to grab the gizmo.</summary>
    private bool IsGizmoHitAtMouse()
    {
        if (_bridge.SelectedEditorObject == null || _bridge.EditorGizmo == null || _bridge.Camera == null)
            return false;
        if (_bridge.SceneTextureWidth <= 0 || _bridge.SceneTextureHeight <= 0)
            return false;
        if (_bridge.GetEditorGizmoCenter() is not Vector3 gz)
            return false;

        int vpw = _bridge.SceneTextureWidth;
        int vph = _bridge.SceneTextureHeight;
        // Flip Y: ImGui Y=0=top → GL Y=0=bottom
        float glY = vph - _bridge.ViewportMouseY;
        var mouseScreen = new Vector2(_bridge.ViewportMouseX, glY);
        return _bridge.EditorGizmo.HitTest(mouseScreen, _bridge.Camera, gz, vpw, vph) != TransformGizmo.Axis.None;
    }

    /// <summary>Return the Sky object whose sun handle (the gold sun disc on the sky gizmo)
    /// is under the mouse  null when none. Checks EVERY placed Sky marker, so the sun can
    /// be grabbed even when the Sky object isn't currently selected (grabbing selects it).
    /// Keeps click-to-select / marquee / transform gizmo from stealing a sun grab.</summary>
    private EditorObject? SkySunHandleAtMouse()
    {
        if (_bridge.Camera == null || _bridge.SceneTextureWidth <= 0 || _bridge.SceneTextureHeight <= 0) return null;
        if (_bridge.ViewportMouseX < 0f || _bridge.ViewportMouseY < 0f) return null;
        var mgr = _bridge.EditorObjectManager;
        if (mgr == null) return null;

        int vpw = _bridge.SceneTextureWidth;
        int vph = _bridge.SceneTextureHeight;
        // Flip Y: ImGui Y=0=top → GL Y=0=bottom
        float glY = vph - _bridge.ViewportMouseY;
        var mouseScreen = new Vector2(_bridge.ViewportMouseX, glY);

        EditorObject? best = null;
        float bestDist = float.MaxValue;
        foreach (var obj in mgr.Objects)
        {
            if (obj == null || obj.PrimitiveType != EditorPrimitiveType.Sky || !obj.IsVisible) continue;
            if (obj.GetSkySunHandleCenter() is not Vector3 sunWorld) continue;
            var p = TransformGizmo.ProjectToScreen(_bridge.Camera, sunWorld, vpw, vph);
            if (p.X < -40f || p.X > vpw + 40f || p.Y < -40f || p.Y > vph + 40f) continue;

            // Hit tolerance = the sun disc's PROJECTED radius (same size math as
            // DrawSkyGizmo's sun icon, shared via SkySunDiscRadius) plus a comfortable
            // margin. Previously a fixed 18px around the center point was used, so clicking
            // the visible gold disc surface  but not its exact center  missed and the
            // drag never started.
            float sunRadius = obj.SkySunDiscRadius;
            var sunDir = obj.GetSkySunDirection();
            var upRef = MathF.Abs(sunDir.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            var sunRight = Vector3.Normalize(Vector3.Cross(sunDir, upRef));
            var pEdge = TransformGizmo.ProjectToScreen(_bridge.Camera, sunWorld + sunRight * sunRadius, vpw, vph);
            float discRadiusPx = Vector2.Distance(p, pEdge);
            float tol = MathF.Max(18f, discRadiusPx + 10f);

            float dist = Vector2.Distance(mouseScreen, p);
            if (dist <= tol && dist < bestDist) { bestDist = dist; best = obj; }
        }
        return best;
    }

    /// <summary>Select every editor object whose projected screen position falls inside
    /// the marquee rectangle (scene coords, Y-down). Shift/Ctrl held at release ADDS the
    /// marquee result to the current selection; otherwise the selection is replaced.</summary>
    private void ApplyMarqueeSelection(Vector2 startScene, Vector2 endScene)
    {
        var mgr = _bridge.EditorObjectManager;
        var cam = _bridge.Camera;
        if (mgr == null || cam == null) return;
        int texW = _bridge.SceneTextureWidth;
        int texH = _bridge.SceneTextureHeight;
        if (texW <= 0 || texH <= 0) return;

        float x0 = MathF.Max(0f, MathF.Min(startScene.X, endScene.X));
        float x1 = MathF.Min(texW, MathF.Max(startScene.X, endScene.X));
        float y0 = MathF.Max(0f, MathF.Min(startScene.Y, endScene.Y));
        float y1 = MathF.Min(texH, MathF.Max(startScene.Y, endScene.Y));
        // Degenerate (zero-area) marquee  nothing to select
        if (x1 - x0 < 1f || y1 - y0 < 1f) return;

        bool additive = ImGui.GetIO().KeyCtrl || ImGui.GetIO().KeyShift;
        if (!additive)
            _bridge.SelectEditorObject(null);

        int hitCount = 0;
        foreach (var obj in mgr.Objects)
        {
            if (obj == null) continue;
            // Project to viewport pixels (Y=0=bottom in GL), then flip to scene Y-down
            Vector2 p = TransformGizmo.ProjectToScreen(cam, obj.GizmoPivotOverride ?? obj.Position, texW, texH);
            float sy = texH - p.Y;
            if (p.X >= x0 && p.X <= x1 && sy >= y0 && sy <= y1)
            {
                _bridge.SelectEditorObject(obj, additive: true);
                hitCount++;
            }
        }
        Console.WriteLine($"[Viewport] Marquee selected {hitCount} object(s)");
    }

    public void Render()
    {
        //  Initial sync: ensure IsPreviewMode matches _previewMode on first frame 
        if (!_initialSyncDone)
        {
            _bridge.IsPreviewMode = _previewMode;
            _initialSyncDone = true;
        }

        // In fullscreen mode (In-Game Mode F8), always render regardless of _visible
        if (!_fullscreenMode)
        {
            if (!_visible) return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

        //  Fullscreen mode: add NoTitleBar|NoResize flags 
        var windowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        if (_fullscreenMode)
            windowFlags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                           ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;

        if (_fullscreenMode)
        {
            // Fullscreen: no close button (no ref _visible)
            ImGui.Begin("Viewport", windowFlags);
        }
        else
        {
            ImGui.Begin("Viewport", ref _visible, windowFlags);
        }
        ImGui.PopStyleVar();

        // Track whether the viewport is focused
        _bridge.IsViewportFocused = ImGui.IsWindowFocused();

        //  Camera view preset shortcuts (17 on the main row / numpad) 
        // Only active while the viewport window has focus and we're NOT in preview
        // mode, so number keys never hijack gameplay input.
        HandleCameraViewShortcuts();

        if (!_fullscreenMode)
        {
            // Cache viewport window position for tooltip positioning (top-left)
            var viewportTopLeft = ImGui.GetWindowPos();

            //  Snap-to-grid toggle + grid size selector (skipped in fullscreen) 
            {
                //  Preview mode toggle 
                bool previewNow = _previewMode;
                ImGui.PushStyleColor(ImGuiCol.Button, previewNow
                    ? new Vector4(0.15f, 0.55f, 0.25f, 1f)    // green = preview ON
                    : new Vector4(0.35f, 0.35f, 0.35f, 1f)); // grey = editor
                if (ImGui.Button(previewNow ? " Preview" : "▲ Edit"))
                {
                    if (!previewNow)
                    {
                        //  Entering Preview mode 
                        ResetSceneOverlays();
                        // Hide brush ring so it can't leak into game view
                        ClearBrushIndicator();
                        // Turn off terrain brush in preview
                        _bridge.TerrainBrushActive = false;
                        _bridge.TerrainBrushMode = 0;
                    }
                    else
                    {
                        //  Exiting Preview mode (back to Edit) 
                        // Reset all edit-mode actions to default/off
                        _bridge.TerrainBrushActive = false;
                        _bridge.TerrainBrushMode = 0;
                        _bridge.GizmoMode = 0; // Translate (default)
                        if (_bridge.EditorGizmo != null)
                        {
                            _bridge.EditorGizmo.Mode = TransformGizmo.GizmoMode.Translate;
                            _bridge.EditorGizmo.EndDrag();
                        }
                        ClearBrushIndicator();
                    }
                    _previewMode = !_previewMode;
                    // Set preview mode flag  hides editor gizmos/helpers without
                    // changing camera behavior (WASD fly still works).
                    _bridge.IsPreviewMode = _previewMode;
                }
                ImGui.PopStyleColor(1);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_previewMode
                        ? "Preview mode: hides editor helpers  shows scene as in-game"
                        : "Edit mode: shows wireframes, handles, and info labels");
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();

                bool snapBefore = _snapEnabled;
                ImGui.Checkbox("Snap", ref _snapEnabled);
                if (_snapEnabled != snapBefore) PersistViewportPrefs();
                ImGui.SameLine();

                string gridLabel = _snapEnabled ? $"{_snapGridSize:F0}px" : "";
                ImGui.SetNextItemWidth(70f);
                if (ImGui.BeginCombo("##grid_size", gridLabel))
                {
                    for (int si = 0; si < SnapOptions.Length; si++)
                    {
                        bool isSel = Math.Abs(_snapGridSize - SnapOptions[si]) < 0.01f;
                        if (ImGui.Selectable($"{SnapOptions[si]}px", isSel))
                        {
                            _snapGridSize = SnapOptions[si];
                            _snapEnabled = true;
                            PersistViewportPrefs();
                        }
                    }
                    ImGui.EndCombo();
                }

ImGui.SameLine();
                ImGui.TextDisabled("|  " + (_snapEnabled ? $"Grid {_snapGridSize:F0}px" : "Free"));

                // Show element position readout when selected
                var selReadout = _bridge.SelectedUIElement;
                if (selReadout != null)
                {
                    ImGui.SameLine();
ImGui.TextColored(new Vector4(0.3f, 0.9f, 1.0f, 1f),
                        $"{selReadout.GetIcon()} ({selReadout.X:F0},{selReadout.Y:F0}) [{selReadout.Width:F0}{selReadout.Height:F0}] S:{selReadout.FontSize:F0}");
                }

                //  Editor tool buttons (gizmo mode, fly, reset, snap, terrain brushes,
                // shade/contours, grid, shadow) moved to the floating LEFT toolbar 
                // see DrawViewportLeftToolbar(). Camera view presets also live there via
                // the floating  Views overlay. 
                if (_bridge.EditorObjectManager != null)
                {
                    // Primitive creation buttons
                    ImGui.SameLine();
                    ImGui.TextDisabled("|");
                    ImGui.SameLine();
if (ImGui.Button("+Box"))
                    {
                        var pos = IDEBridge.GetGridSpawnPosition(_bridge.Camera, EditorPrimitiveType.Box);
                        var obj = _bridge.EditorObjectManager.AddPrimitive(EditorPrimitiveType.Box, pos);
                        if (obj != null) _bridge.SelectEditorObject(obj);
                    }
                    ImGui.SameLine();
if (ImGui.Button("+Sphere"))
                    {
                        var pos = IDEBridge.GetGridSpawnPosition(_bridge.Camera, EditorPrimitiveType.Sphere);
                        var obj = _bridge.EditorObjectManager.AddPrimitive(EditorPrimitiveType.Sphere, pos);
                        if (obj != null) _bridge.SelectEditorObject(obj);
                    }
ImGui.SameLine();
                    if (ImGui.Button("+Plane"))
                    {
                        var pos = IDEBridge.GetGridSpawnPosition(_bridge.Camera, EditorPrimitiveType.Plane);
                        var obj = _bridge.EditorObjectManager.AddPrimitive(EditorPrimitiveType.Plane, pos);
                        if (obj != null) _bridge.SelectEditorObject(obj);
                    }

                    //  Camera / Light / Sky scene elements 
                    ImGui.SameLine();
                    if (ImGui.Button("+Cam"))
                    {
                        var pos = IDEBridge.GetGridSpawnPosition(_bridge.Camera, EditorPrimitiveType.Camera);
                        var obj = _bridge.EditorObjectManager.AddPrimitive(EditorPrimitiveType.Camera, pos);
                        if (obj != null) _bridge.SelectEditorObject(obj);
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Add a Camera marker (eye-height spawn, teal)");
                    ImGui.SameLine();
                    if (ImGui.Button("+Light"))
                    {
                        var pos = IDEBridge.GetGridSpawnPosition(_bridge.Camera, EditorPrimitiveType.Light);
                        var obj = _bridge.EditorObjectManager.AddPrimitive(EditorPrimitiveType.Light, pos);
                        if (obj != null) _bridge.SelectEditorObject(obj);
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Add a Light marker (overrides editor sun color/direction, yellow)");
                    ImGui.SameLine();
                    if (ImGui.Button("+Sky"))
                    {
                        var pos = IDEBridge.GetGridSpawnPosition(_bridge.Camera, EditorPrimitiveType.Sky);
                        var obj = _bridge.EditorObjectManager.AddPrimitive(EditorPrimitiveType.Sky, pos);
                        if (obj != null)
                        {
                            // Sky automatically drives a DIRECT light  reuse or create one.
                            _bridge.EditorObjectManager.EnsureDirectLightForSky(obj);
                            _bridge.SelectEditorObject(obj);
                        }
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Add a Sky marker (renders the procedural skybox in the viewport, blue)");

                    //  Duplicate selected 3D object(s)  duplicates ALL selected 
                    ImGui.SameLine();
                    ImGui.BeginDisabled(_bridge.SelectedEditorObjects.Count == 0);
                    if (ImGui.Button(_bridge.SelectedEditorObjects.Count > 1 ? $" Duplicate ({_bridge.SelectedEditorObjects.Count})" : "→ Duplicate"))
                    {
                        var mgr = _bridge.EditorObjectManager;
                        if (mgr != null)
                        {
                            var dups = new List<EditorObject>();
                            int dupIdx = 0;
                            foreach (var obj in _bridge.SelectedEditorObjects.ToArray())
                            {
                                var dup = mgr.Duplicate(obj);
                                if (dup != null)
                                {
                                    // Spread clones out so they don't stack on top of each other
                                    dup.Position += new Vector3(dupIdx, 0f, 0f);
                                    dupIdx++;
                                    dups.Add(dup);
                                }
                            }
                            if (dups.Count > 0)
                            {
                                _bridge.SelectEditorObject(null);
                                foreach (var d in dups)
                                    _bridge.SelectEditorObject(d, additive: true);
                            }
                        }
                    }
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Duplicate the selected 3D object(s)");
                }
            } // end toolbar block
        } // end if (!_fullscreenMode)

        var avail = ImGui.GetContentRegionAvail();
        bool hasSceneTexture = avail.X > 0 && avail.Y > 0 && _bridge.SceneTextureID != 0;
        bool hasGameScene = _bridge.SceneManager?.CurrentScene != null;

        if (hasSceneTexture || _bridge.SceneRoot != null)
        {
            //  Canvas area (fill available space) 
            float canvasW = avail.X;
            float canvasH = avail.Y;

            if (hasSceneTexture)
            {
                // Calculate aspect-ratio-correct image size to fill the panel
                float panelAspect = avail.X / avail.Y;
                float sceneAspect = _bridge.SceneTextureWidth / (float)_bridge.SceneTextureHeight;

                Vector2 imageSize;
                if (panelAspect > sceneAspect)
                    imageSize = new Vector2(avail.Y * sceneAspect, avail.Y);
                else
                    imageSize = new Vector2(avail.X, avail.X / sceneAspect);

                // Center the image in the panel
                float offsetX = (avail.X - imageSize.X) * 0.5f;
                float offsetY = (avail.Y - imageSize.Y) * 0.5f;
                ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(offsetX, offsetY));

                // Display the scene texture as an ImGui image
                var uv0 = new Vector2(0, 1);
                var uv1 = new Vector2(1, 0);
                ImGui.Image((nint)(nint)_bridge.SceneTextureID, imageSize, uv0, uv1);

                _imageMin = ImGui.GetItemRectMin();
                _imageMax = ImGui.GetItemRectMax();
                _imageSize = imageSize;
                _texW = _bridge.SceneTextureWidth > 0 ? _bridge.SceneTextureWidth : 1f;
                _texH = _bridge.SceneTextureHeight > 0 ? _bridge.SceneTextureHeight : 1f;

            }
            else
            {
                // No game scene  draw dark animated canvas for UI editing
                ImGui.Dummy(new Vector2(canvasW, canvasH));
                var drawList = ImGui.GetWindowDrawList();
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();

                // Animated gradient background (via ImGui draw list)
                RenderImGuiGradient(drawList, min, max);

                _imageMin = min;
                _imageMax = max;
                _imageSize = new Vector2(canvasW, canvasH);
                _texW = 1920f;
                _texH = 1080f;
            }


            var viewportMouseScreen = ImGui.GetMousePos();

            //  Cache mouse state ONCE before any click handling 
            // (DrawEditorUIPreview calls IsMouseClicked for each element; caching
            //  here ensures the wireframe section gets the same reliable value)
            bool cachedLeftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            bool cachedLeftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
            bool cachedLeftReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

            //  Detect editor scene switch → clear preview texture cache 
            if (!hasGameScene && _bridge.SceneRoot != null)
            {
                if (_bridge.SceneRoot != _lastSceneRoot)
                {
                    if (_previewTextureCache.Count > 0)
                    {
                        int cleared = _previewTextureCache.Count;
                        ClearPreviewTextures();
                        Console.WriteLine($"[Viewport] Scene root changed  cleared {cleared} preview textures");
                    }
                    _lastSceneRoot = _bridge.SceneRoot;
                }
            }
            else if (_lastSceneRoot != null)
            {
                // Game scene started → clear any remaining editor preview textures
                if (_previewTextureCache.Count > 0)
                    ClearPreviewTextures();
                _lastSceneRoot = null;
            }

            //  Live Editor Scene Preview (draws UI elements on top of the scene texture or dark canvas) 
            // Uses SceneRoot directly (not SelectedEditorScene) so the preview works even when
            // no editor scene is explicitly selected  the active game scene's root is sufficient.
            //  Render UI elements in both editor and preview mode 
            // In editor mode: elements are rendered with click-to-select behavior.
            // In preview mode: elements are rendered with click-to-interact behavior (game-like).
            if (_bridge.SceneRoot != null && _bridge.SceneRoot.Children.Count > 0)
            {
                var drawList = ImGui.GetWindowDrawList();
                DrawEditorUIPreview(drawList, _bridge.SceneRoot.Children, viewportMouseScreen, cachedLeftClicked, isPreview: _previewMode, isMouseDown: cachedLeftDown);
            }

            //  Preview mode indicator badge (bottom-right corner) 
            if (_previewMode)
            {
                var drawList = ImGui.GetWindowDrawList();
                string badge = "PREVIEW MODE";
                var badgeSize = ImGui.CalcTextSize(badge);
                float badgePad = 8f;
                float bx = _imageMax.X - badgeSize.X - badgePad * 2f - 6f;
                float by = _imageMax.Y - badgeSize.Y - badgePad * 2f - 6f;
                uint badgeBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.40f, 0.15f, 0.80f));
                uint badgeText = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 1.0f, 0.5f, 1f));
                uint badgeBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.7f, 0.2f, 0.50f));
                drawList.AddRectFilled(new Vector2(bx, by),
                    new Vector2(bx + badgeSize.X + badgePad * 2f, by + badgeSize.Y + badgePad * 2f),
                    badgeBg, 5f);
                drawList.AddRect(new Vector2(bx, by),
                    new Vector2(bx + badgeSize.X + badgePad * 2f, by + badgeSize.Y + badgePad * 2f),
                    badgeBorder, 5f, ImDrawFlags.None, 1.5f);
                drawList.AddText(new Vector2(bx + badgePad, by + badgePad), badgeText, badge);

                //  Clickable invisible button over badge → exit preview mode 
                ImGui.SetCursorScreenPos(new Vector2(bx, by));
                float badgeTotalW = badgeSize.X + badgePad * 2f;
                float badgeTotalH = badgeSize.Y + badgePad * 2f;
                ImGui.InvisibleButton("##preview_exit", new Vector2(badgeTotalW, badgeTotalH));
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.SetTooltip("Click to exit preview mode");
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    _previewMode = false;
                    _bridge.IsPreviewMode = false;
                    ResetSceneOverlays();
                    // Reset all edit-mode actions to default/off
                    _bridge.TerrainBrushActive = false;
                    _bridge.TerrainBrushMode = 0;
                    _bridge.GizmoMode = 0; // Translate (default)
                    if (_bridge.EditorGizmo != null)
                    {
                        _bridge.EditorGizmo.Mode = TransformGizmo.GizmoMode.Translate;
                        _bridge.EditorGizmo.EndDrag();
                    }
                    ClearBrushIndicator();
                    Console.WriteLine("[Viewport] Exited preview mode via badge click");
                }
            } // end if (_previewMode) badge block

            //  UI Element Wireframe & Interactive Editing 
            // In Preview mode, skip ALL editor overlays (wireframe, handles, info labels, drag)
            if (!_previewMode)
            {
            bool showHelpers = true; // Show helpers for all scene types (MainMenu, GameScene, Loading)

            if (showHelpers)
            {
            var selUiElem = _bridge.SelectedUIElement;
            var allSelected = _bridge.SelectedUIElements;

            if (selUiElem != null)
            {
                var drawList = ImGui.GetWindowDrawList();
                float pulse = 0.6f + 0.4f * MathF.Sin((float)ImGui.GetTime() * 3f);

                //  Helper: draw a wireframe for a single element 
                void DrawElemWireframe(UIElement elem, bool isPrimary)
                {
                    float sx0 = _imageMin.X + (elem.X / _texW) * _imageSize.X;
                    float sy0 = _imageMin.Y + (elem.Y / _texH) * _imageSize.Y;
                    float sx1 = _imageMin.X + ((elem.X + elem.Width) / _texW) * _imageSize.X;
                    float sy1 = _imageMin.Y + ((elem.Y + elem.Height) / _texH) * _imageSize.Y;

                    float csx0 = Math.Clamp(sx0, _imageMin.X, _imageMax.X);
                    float csy0 = Math.Clamp(sy0, _imageMin.Y, _imageMax.Y);
                    float csx1 = Math.Clamp(sx1, _imageMin.X, _imageMax.X);
                    float csy1 = Math.Clamp(sy1, _imageMin.Y, _imageMax.Y);
                    bool fullyVis = csx0 == sx0 && csy0 == sy0 && csx1 == sx1 && csy1 == sy1;
                    bool isFitToWindow = isPrimary && elem.ClickBehaviorLabel == "fittowindow";

                    uint wireColor, fillColor, handleColor, handleBorder, bracketColor;

                    if (isPrimary)
                    {
                        wireColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.8f, 1.0f, pulse * 0.9f));
                        fillColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.8f, 1.0f, 0.08f));
                        handleColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f));
                        handleBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.8f, 1.0f, 1f));
                        bracketColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.9f, 1.0f, pulse * 1.0f));
                    }
                    else
                    {
                        wireColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.6f, 0.9f, pulse * 0.4f));
                        fillColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.6f, 0.9f, 0.04f));
                        handleColor = 0;
                        handleBorder = 0;
                        bracketColor = 0;
                    }

                    drawList.AddRectFilled(new Vector2(csx0, csy0), new Vector2(csx1, csy1), fillColor);
                    drawList.AddRect(new Vector2(csx0, csy0), new Vector2(csx1, csy1), wireColor, 0f, ImDrawFlags.None, isPrimary ? 3f : 1.5f);

                    if (isPrimary)
                    {
                        float bracketLen = Math.Min(16f, (csx1 - csx0) * 0.25f);
                        drawList.AddLine(new Vector2(csx0, csy0), new Vector2(csx0 + bracketLen, csy0), bracketColor, 2f);
                        drawList.AddLine(new Vector2(csx0, csy0), new Vector2(csx0, csy0 + bracketLen), bracketColor, 2f);
                        drawList.AddLine(new Vector2(csx1, csy0), new Vector2(csx1 - bracketLen, csy0), bracketColor, 2f);
                        drawList.AddLine(new Vector2(csx1, csy0), new Vector2(csx1, csy0 + bracketLen), bracketColor, 2f);
                        drawList.AddLine(new Vector2(csx0, csy1), new Vector2(csx0 + bracketLen, csy1), bracketColor, 2f);
                        drawList.AddLine(new Vector2(csx0, csy1), new Vector2(csx0, csy1 - bracketLen), bracketColor, 2f);
                        drawList.AddLine(new Vector2(csx1, csy1), new Vector2(csx1 - bracketLen, csy1), bracketColor, 2f);
                        drawList.AddLine(new Vector2(csx1, csy1), new Vector2(csx1, csy1 - bracketLen), bracketColor, 2f);

                        float handleSz = 12f, handleHalf = handleSz * 0.5f;
                        if (!isFitToWindow && fullyVis)
                        {
                            Vector2[] corners = [
                                new(sx0, sy0), new(sx1, sy0),
                                new(sx0, sy1), new(sx1, sy1)];

                            // Draw glow behind each handle
                            uint glowColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.8f, 1.0f, pulse * 0.25f));
                            foreach (var c in corners)
                            {
                                drawList.AddCircleFilled(
                                    new Vector2(c.X, c.Y),
                                    handleSz * 0.7f, glowColor, 12);
                            }

                            // Draw handle squares
                            foreach (var c in corners)
                            {
                                drawList.AddRectFilled(
                                    new Vector2(c.X - handleHalf, c.Y - handleHalf),
                                    new Vector2(c.X + handleHalf, c.Y + handleHalf),
                                    handleColor, 3f);
                                drawList.AddRect(
                                    new Vector2(c.X - handleHalf, c.Y - handleHalf),
                                    new Vector2(c.X + handleHalf, c.Y + handleHalf),
                                    handleBorder, 3f, ImDrawFlags.None, 2f);
                            }

                            // Draw directional arrow inside each handle
                            uint arrowColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.2f, 0.9f));
                            float arrowInset = handleHalf * 0.3f;
                            drawList.AddLine(
                                new Vector2(sx0 + arrowInset, sy0 + arrowInset),
                                new Vector2(sx0 + handleHalf - arrowInset, sy0 + handleHalf - arrowInset),
                                arrowColor, 1.8f);
                            drawList.AddLine(
                                new Vector2(sx0 + handleHalf - arrowInset * 2f, sy0 + arrowInset),
                                new Vector2(sx0 + handleHalf - arrowInset, sy0 + handleHalf - arrowInset),
                                arrowColor, 1.8f);
                            drawList.AddLine(
                                new Vector2(sx1 - arrowInset, sy0 + arrowInset),
                                new Vector2(sx1 - handleHalf + arrowInset, sy0 + handleHalf - arrowInset),
                                arrowColor, 1.8f);
                            drawList.AddLine(
                                new Vector2(sx1 - handleHalf + arrowInset * 2f, sy0 + arrowInset),
                                new Vector2(sx1 - handleHalf + arrowInset, sy0 + handleHalf - arrowInset),
                                arrowColor, 1.8f);
                        } // end if (!isFitToWindow && fullyVis)
                    } // end if (isPrimary)
                } // end DrawElemWireframe

                //  Scene-type elements: NO wireframe (just skip the wireframe draw) 
                bool isSceneElem = selUiElem.Type == UIElementType.Scene;

                if (!isSceneElem)
                    DrawElemWireframe(selUiElem, true);

                //  Scene-type elements: NO resize/move handlers 
                bool isFitToWindowElem = selUiElem.ClickBehaviorLabel == "fittowindow";

                if (isFitToWindowElem)
                {
                    selUiElem.X = 0;
                    selUiElem.Y = 0;
                    selUiElem.Width = _texW;
                    selUiElem.Height = _texH;
                }

                if (isSceneElem)
                {
                    // Ensure any lingering drag mode from a previously-selected element is cleared
                    _dragMode = DragMode.None;
                }
                else
                {
                //  Interactive drag handling 
                float psx0 = _imageMin.X + (selUiElem.X / _texW) * _imageSize.X;
                float psy0 = _imageMin.Y + (selUiElem.Y / _texH) * _imageSize.Y;
                float psx1 = _imageMin.X + ((selUiElem.X + selUiElem.Width) / _texW) * _imageSize.X;
                float psy1 = _imageMin.Y + ((selUiElem.Y + selUiElem.Height) / _texH) * _imageSize.Y;
                float pcsx0 = Math.Clamp(psx0, _imageMin.X, _imageMax.X);
                float pcsy0 = Math.Clamp(psy0, _imageMin.Y, _imageMax.Y);
                float pcsx1 = Math.Clamp(psx1, _imageMin.X, _imageMax.X);
                float pcsy1 = Math.Clamp(psy1, _imageMin.Y, _imageMax.Y);
                bool primaryFullyVisible = pcsx0 == psx0 && pcsy0 == psy0 && pcsx1 == psx1 && pcsy1 == psy1;

                //  Corner detection radius: proportional to element screen size 
                float elemScreenW = psx1 - psx0;
                float elemScreenH = psy1 - psy0;
                float cornerRadius = Math.Max(6f, Math.Min(10f, Math.Min(elemScreenW, elemScreenH) * 0.25f));

                // Normal drag end
                if (cachedLeftReleased && _dragMode != DragMode.None)
                {
                    if (selUiElem != null && _bridge.RecordTransformUndo != null)
                    {
                        _bridge.RecordTransformUndo(
                            selUiElem,
                            _dragStartX, _dragStartY, _dragStartW, _dragStartH,
                            selUiElem.X, selUiElem.Y, selUiElem.Width, selUiElem.Height);
                    }
                    _dragMode = DragMode.None;
                }

                //  Top-center move handle detection 
                float mhx = (psx0 + psx1) * 0.5f;
                float mhy = psy0;
                bool overMoveHandle = primaryFullyVisible &&
                    Math.Abs(viewportMouseScreen.X - mhx) <= cornerRadius &&
                    viewportMouseScreen.Y >= mhy - cornerRadius * 2f &&
                    viewportMouseScreen.Y <= mhy + cornerRadius * 0.5f;

                // Corner detection (only when fully visible)
                bool overTL = primaryFullyVisible && Math.Abs(viewportMouseScreen.X - psx0) <= cornerRadius && Math.Abs(viewportMouseScreen.Y - psy0) <= cornerRadius;
                bool overTR = primaryFullyVisible && Math.Abs(viewportMouseScreen.X - psx1) <= cornerRadius && Math.Abs(viewportMouseScreen.Y - psy0) <= cornerRadius;
                bool overBL = primaryFullyVisible && Math.Abs(viewportMouseScreen.X - psx0) <= cornerRadius && Math.Abs(viewportMouseScreen.Y - psy1) <= cornerRadius;
                bool overBR = primaryFullyVisible && Math.Abs(viewportMouseScreen.X - psx1) <= cornerRadius && Math.Abs(viewportMouseScreen.Y - psy1) <= cornerRadius;
                // Body = anywhere inside the element that is NOT a corner/move-handle zone
                bool overBody = viewportMouseScreen.X >= pcsx0 && viewportMouseScreen.X <= pcsx1 &&
                                viewportMouseScreen.Y >= pcsy0 && viewportMouseScreen.Y <= pcsy1 &&
                                !overTL && !overTR && !overBL && !overBR && !overMoveHandle;

                if (!isFitToWindowElem)
                {
                    if (overMoveHandle && _dragMode == DragMode.None)
                    {
                        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                        ImGui.BeginTooltip();
                        ImGui.Text("Drag to move");
                        ImGui.EndTooltip();
                    }
                    else if (overTL || overBR)
                    {
                        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);
                        if (_dragMode == DragMode.None)
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text("Drag corner to resize");
                            ImGui.EndTooltip();
                        }
                    }
                    else if (overTR || overBL)
                    {
                        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNESW);
                        if (_dragMode == DragMode.None)
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text("Drag corner to resize");
                            ImGui.EndTooltip();
                        }
                    }
                    else if (overBody && _dragMode == DragMode.None)
                    {
                        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                        ImGui.BeginTooltip();
                        ImGui.Text("Drag body to move");
                        ImGui.EndTooltip();
                    }

                    // Click handling: move handle > corner > body
                    if (_dragMode == DragMode.None && cachedLeftClicked)
                    {
                        bool dragStarted = false;
                        if (overMoveHandle)
                        {
                            _dragMode = DragMode.Move;
                            dragStarted = true;
                        }
                        else
                        {
                            bool anyCorner = primaryFullyVisible && (overTL || overTR || overBL || overBR);
                            if (anyCorner)
                            {
                                if (overTL) { _dragMode = DragMode.ResizeTL; dragStarted = true; }
                                else if (overTR) { _dragMode = DragMode.ResizeTR; dragStarted = true; }
                                else if (overBL) { _dragMode = DragMode.ResizeBL; dragStarted = true; }
                                else { _dragMode = DragMode.ResizeBR; dragStarted = true; }
                            }
                            else if (overBody)
                            {
                                _dragMode = DragMode.Move;
                                dragStarted = true;
                            }
                        }

                        if (dragStarted)
                        {
                            _dragStartX = selUiElem.X; _dragStartY = selUiElem.Y;
                            _dragStartW = selUiElem.Width; _dragStartH = selUiElem.Height;
                            _dragStartMouseScene = ScreenToScene(viewportMouseScreen);

                            // Auto-center owns the position — manually dragging the element
                            // turns auto-center OFF so the drag isn't fought every frame.
                            if (selUiElem.AutoCenterX)
                            {
                                selUiElem.AutoCenterX = false;
                                Console.WriteLine($"[Viewport] Auto-center X disabled on '{selUiElem.Name}' (manual drag)");
                            }
                            if (selUiElem.AutoCenterY)
                            {
                                selUiElem.AutoCenterY = false;
                                Console.WriteLine($"[Viewport] Auto-center Y disabled on '{selUiElem.Name}' (manual drag)");
                            }
                            if (selUiElem.Anchor != UIAnchor.None)
                            {
                                selUiElem.Anchor = UIAnchor.None;
                                Console.WriteLine($"[Viewport] Anchor disabled on '{selUiElem.Name}' (manual drag)");
                            }
                        }
                    }
                }

                // Apply drag movement/resize while mouse is held
                if (_dragMode != DragMode.None && !cachedLeftReleased)
                {
                    var currentMouseScene = ScreenToScene(viewportMouseScreen);
                    float dx = currentMouseScene.X - _dragStartMouseScene.X;
                    float dy = currentMouseScene.Y - _dragStartMouseScene.Y;
                    const float minSize = 10f;

                    float newX = selUiElem.X, newY = selUiElem.Y;
                    float newW = selUiElem.Width, newH = selUiElem.Height;

                    switch (_dragMode)
                    {
                        case DragMode.Move:
                            newX = _dragStartX + dx; newY = _dragStartY + dy;
                            newX = SnapToGrid(newX); newY = SnapToGrid(newY);
                            break;
                        case DragMode.ResizeTL:
                            newX = Math.Min(_dragStartX + _dragStartW - minSize, _dragStartX + dx);
                            newW = Math.Max(minSize, _dragStartW - dx);
                            newY = Math.Min(_dragStartY + _dragStartH - minSize, _dragStartY + dy);
                            newH = Math.Max(minSize, _dragStartH - dy);
                            newX = SnapToGrid(newX); newY = SnapToGrid(newY);
                            newW = SnapToGrid(newW); newH = SnapToGrid(newH);
                            break;
                        case DragMode.ResizeTR:
                            newW = Math.Max(minSize, _dragStartW + dx);
                            newY = Math.Min(_dragStartY + _dragStartH - minSize, _dragStartY + dy);
                            newH = Math.Max(minSize, _dragStartH - dy);
                            newY = SnapToGrid(newY);
                            newW = SnapToGrid(newW); newH = SnapToGrid(newH);
                            break;
                        case DragMode.ResizeBL:
                            newX = Math.Min(_dragStartX + _dragStartW - minSize, _dragStartX + dx);
                            newW = Math.Max(minSize, _dragStartW - dx);
                            newH = Math.Max(minSize, _dragStartH + dy);
                            newX = SnapToGrid(newX);
                            newW = SnapToGrid(newW); newH = SnapToGrid(newH);
                            break;
                        case DragMode.ResizeBR:
                            newW = Math.Max(minSize, _dragStartW + dx);
                            newH = Math.Max(minSize, _dragStartH + dy);
                            newW = SnapToGrid(newW); newH = SnapToGrid(newH);
                            break;
                    }

                    selUiElem.X = newX; selUiElem.Y = newY;
                    selUiElem.Width = newW; selUiElem.Height = newH;
                }

                //  Post-apply safety: reset if mouse is neither down nor being released 
                if (_dragMode != DragMode.None && !cachedLeftDown && !cachedLeftReleased)
                {
                    _dragMode = DragMode.None;
                }
                } // end if (!isSceneElem)
                } // end if (selUiElem != null)
                } // end if (showHelpers)
            } // end if (!_previewMode)

            //  Safety reset: handle interrupted drag even when selUiElem became null 
            if (_dragMode != DragMode.None)
            {
                bool mouseDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
                bool mouseReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
                if (!mouseDown && !mouseReleased)
                {
                    _dragMode = DragMode.None;
                    Console.WriteLine("[Viewport] Drag mode reset (interrupted, no element)");
                }
            }

            //  Gizmo safety reset: handle interrupted gizmo drag even when mouse leaves viewport 
            if (_bridge.EditorGizmo != null && _bridge.EditorGizmo.IsDragging)
            {
                bool mouseDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
                bool mouseReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
                if (!mouseDown && !mouseReleased)
                {
                    var draggedSet = _bridge.EditorGizmo.DragTargets.ToArray();
                    _bridge.EditorGizmo.EndDrag();
                    // Record undo for the partial movement that happened before the interruption
                    _bridge.OnGizmoDragEnded?.Invoke(draggedSet);
                    Console.WriteLine("[Viewport] Gizmo drag reset (interrupted)");
                }
            }

            //  Drag-drop target: Asset Browser image → selected element 
            if (_bridge.SelectedUIElement != null && ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                {
                    string imgPath = AssetBrowserPanel._dragImagePath;
                    _bridge.SelectedUIElement.ImagePath = imgPath;
                    Console.WriteLine($"[Viewport] Set ImagePath on '{_bridge.SelectedUIElement.Name}' → {imgPath}");
                    AssetBrowserPanel._dragImagePath = null;
                }
                ImGui.EndDragDropTarget();
            }

            //  Popup suppress: block all scene interactions when a popup/menu
            // is open, AND for 2 frames after it closes (prevents the click that
            // closed the menu from leaking into the terrain brush, gizmo, etc.) 
            bool anyPopupOpen = ImGui.IsPopupOpen(null, ImGuiPopupFlags.AnyPopupId);
            if (anyPopupOpen) _wasPopupOpen = true;
            if (_wasPopupOpen && !anyPopupOpen && _postPopupFrames <= 0)
            {
                _postPopupFrames = 2;
                _wasPopupOpen = false;
            }
            if (_postPopupFrames > 0) _postPopupFrames--;
            bool suppressInput = anyPopupOpen || _postPopupFrames > 0;
            _bridge.SuppressViewportInput = suppressInput;

            //  Viewport click/hover detection 
            bool mouseOverImage = viewportMouseScreen.X >= _imageMin.X && viewportMouseScreen.X <= _imageMax.X &&
                                  viewportMouseScreen.Y >= _imageMin.Y && viewportMouseScreen.Y <= _imageMax.Y;

            if (mouseOverImage)
            {
                float relX = viewportMouseScreen.X - _imageMin.X;
                float relY = viewportMouseScreen.Y - _imageMin.Y;
                float sceneU = relX / _imageSize.X;
                float sceneV = relY / _imageSize.Y;

                _bridge.ViewportMouseX = sceneU * _texW;
                _bridge.ViewportMouseY = sceneV * _texH;

                // Reset click flag each frame  set to true below if left-click occurs
                _bridge.IsViewportClicked = false;

                // Track Ctrl/Shift state each frame so click-to-select can do additive multi-select
                _bridge.ViewportCtrlHeld = ImGui.GetIO().KeyCtrl;
                _bridge.ViewportShiftHeld = ImGui.GetIO().KeyShift;

                // Clicks on the floating "▲ Views" overlay button must NOT count as
                // viewport clicks (no raycast select / deselect on empty space).
                // Also block when ImGui wants mouse capture (menus, popups, drag-drop targets).
                // Block viewport clicks when any ImGui popup/menu is open (View menu,
                // Save As dialog, context menus, etc.)  so clicks on menus never
                // accidentally modify the scene or trigger raycast selection.
                // suppressInput is computed above (before mouseOverImage block).
                if (hasSceneTexture && ImGui.IsItemClicked() && _dragMode == DragMode.None
                    && !IsMouseOverViewportViewsButton() && !IsMouseOverLeftToolbar()
                    && SkySunHandleAtMouse() == null && !suppressInput)
                {
                    _bridge.IsViewportClicked = true;
                    _bridge.ViewportClickX = sceneU * _bridge.SceneTextureWidth;
                    _bridge.ViewportClickY = sceneV * _bridge.SceneTextureHeight;
                }

                //  Middle click: reposition gizmo pivot 
                // Skipped when any popup/menu is open or just closed (modal mode).
                if (hasSceneTexture && ImGui.IsItemClicked(ImGuiMouseButton.Middle) && !_previewMode && _bridge.Camera != null
                    && _bridge.SceneTextureWidth > 0 && _bridge.SceneTextureHeight > 0
                    && !suppressInput)
                {
                    // Flip Y: ImGui Y=0=top → OpenGL Y=0=bottom
                    float midClickY = _bridge.SceneTextureHeight - sceneV * _bridge.SceneTextureHeight;
                    _bridge.Camera.ScreenToRay(
                        sceneU * _bridge.SceneTextureWidth, midClickY,
                        _bridge.SceneTextureWidth, _bridge.SceneTextureHeight,
                        out Vector3 rayOrigin, out Vector3 rayDir);

                    Vector3? hitPoint = null;

                    // Try editor objects first
                    if (_bridge.EditorObjectManager != null)
                    {
                        var edObj = _bridge.EditorObjectManager.Raycast(rayOrigin, rayDir, out float edDist, out Vector3 edPoint);
                        if (edObj != null)
                        {
                            hitPoint = edPoint;
                        }
                    }

                    // If no hit, raycast against Y=0 ground plane
                    if (hitPoint == null && Math.Abs(rayDir.Y) > 0.0001f)
                    {
                        float t = -rayOrigin.Y / rayDir.Y;
                        if (t > 0f)
                            hitPoint = rayOrigin + rayDir * t;
                    }

                    if (hitPoint.HasValue && _bridge.SelectedEditorObject != null)
                    {
                        var pivotObj = _bridge.SelectedEditorObject;
                        var oldPivot = pivotObj.GizmoPivotOverride;
                        pivotObj.GizmoPivotOverride = hitPoint.Value;
                        // Record undo so Ctrl+Z reverts the pivot placement (consistent with gizmo drags)
                        _bridge.OnGizmoPivotChanged?.Invoke(pivotObj, oldPivot, hitPoint.Value);
                        Console.WriteLine($"[Viewport] Gizmo pivot for '{pivotObj.Name}' set to {hitPoint.Value:F2}");
                    }
                }
                // IsViewportClicked is reset on the next frame (set to false at start of each
                // frame before the left-click check). The old `else` block that reset it here
                // was a bug: it belonged to the middle-click `if` above, so it would immediately
                // cancel the left-click flag set by the left-click handler.
            }
            else
            {
                _bridge.ViewportMouseX = -1;
                _bridge.ViewportMouseY = -1;
                _bridge.IsViewportClicked = false;
            }

            //  Terrain brush: click-drag to raise/lower terrain height in real-time 
            // Runs before marquee/select/gizmo so a paint stroke never changes the selection.
            // Skipped when: popup/menu is open or just closed (modal mode),
            // or mouse is over the left toolbar / Views button (toolbar clicks must not sculpt).
            if (!_previewMode && _bridge.TerrainBrushActive && hasSceneTexture
                && _bridge.EditorObjectManager != null && _bridge.Camera != null
                && _bridge.SceneTextureWidth > 0 && _bridge.SceneTextureHeight > 0
                && !suppressInput && !IsMouseOverLeftToolbar() && !IsMouseOverViewportViewsButton())
            {
                var cam = _bridge.Camera;
                var mgr = _bridge.EditorObjectManager;
                int vpw = _bridge.SceneTextureWidth;
                int vph = _bridge.SceneTextureHeight;

                // Brush tool on → clicks never select/deselect.
                _bridge.IsViewportClicked = false;

                // Ray under the cursor (GL Y-up).
                float glMouseY = vph - _bridge.ViewportMouseY;
                cam.ScreenToRay(_bridge.ViewportMouseX, glMouseY, vpw, vph,
                    out Vector3 rayOrigin, out Vector3 rayDir);

                // Closest advanced-terrain surface under the cursor (ignores other objects).
                EditorObject? hoverTerrain = null;
                Vector3? hoverPoint = null;
                float bestDist = float.MaxValue;
                foreach (var o in mgr.Objects)
                {
                    if (o == null || !o.IsVisible || !o.TerrainEnabled) continue;
                    if (o.RaycastTerrainSurface(rayOrigin, rayDir) is Vector3 pt)
                    {
                        float d = Vector3.DistanceSquared(cam.Position, pt);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            hoverTerrain = o;
                            hoverPoint = pt;
                        }
                    }
                }

                bool mouseInView = mouseOverImage && _bridge.ViewportMouseX >= 0f && _bridge.ViewportMouseY >= 0f;
                bool leftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
                bool leftReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
                int brushMode = _bridge.TerrainBrushMode;
                bool paintMode = brushMode == 1;

                // Frame-rate independent stamping (Unreal-style): strength is defined per
                // 60fps-frame, so the same drag speed sculpts the same amount at any FPS.
                // Holding Shift scales strength down for fine, precise strokes.
                float brushDt = Math.Clamp(ImGui.GetIO().DeltaTime, 0.004f, 0.1f);
                float fineMult = ImGui.GetIO().KeyShift ? 0.15f : 1f;
                float brushSpeed = brushDt * 60f * fineMult;

                // Begin a paint stroke on first press over a terrain.
                if (leftDown && mouseInView && hoverPoint.HasValue && hoverTerrain != null)
                {
                    if (_brushObj == null)
                    {
                        _brushObj = hoverTerrain;
                        if (paintMode)
                        {
                            _brushSplatBefore = hoverTerrain.CaptureTerrainSplat();
                            // Remember the active layer on this terrain too.
                            hoverTerrain.TerrainPaintLayerIndex = _bridge.TerrainPaintLayerIndex;
                        }
                        else
                        {
                            _brushBefore = hoverTerrain.CaptureTerrainHeights();
                        }
                        // Flatten levels toward the height of the FIRST stamp of the stroke.
                        if (brushMode == 3 && hoverTerrain.TryGetTerrainNormalizedHeight(rayOrigin, rayDir, out float flattenNorm))
                        {
                            _flattenTargetNorm = flattenNorm;
                            _flattenTargetReady = true;
                        }
                        // Select the painted terrain so its settings show in the Inspector.
                        _bridge.SelectEditorObject(hoverTerrain);
                        _bridge.SelectedUIElement = null;
                        Console.WriteLine($"[Viewport] Brush stroke started on '{hoverTerrain.Name}' (mode {brushMode})");
                    }
                    if (_brushObj == hoverTerrain)
                    {
                        switch (brushMode)
                        {
                            case 1: //  layer paint  Ctrl erases (decays weights).
                            {
                                bool erase = ImGui.GetIO().KeyCtrl;
                                hoverTerrain.TryPaintLayerSurface(rayOrigin, rayDir,
                                    _bridge.TerrainPaintLayerIndex, hoverTerrain.TerrainPaintStrength, erase, out _);
                                break;
                            }
                            case 2: //  smooth  blend heights toward their local average.
                            {
                                hoverTerrain.TrySmoothTerrainSurface(rayOrigin, rayDir,
                                    hoverTerrain.TerrainBrushStrength * brushSpeed, out _);
                                break;
                            }
                            case 3: //  flatten  blend toward the stroke's target height.
                            {
                                if (_flattenTargetReady)
                                    hoverTerrain.TryFlattenTerrainSurface(rayOrigin, rayDir, _flattenTargetNorm,
                                        hoverTerrain.TerrainBrushStrength * brushSpeed, out _);
                                break;
                            }
                            default: //  sculpt  Ctrl lowers, plain drag raises.
                            {
                                bool lowering = ImGui.GetIO().KeyCtrl;
                                float delta = (lowering ? -1f : 1f) * hoverTerrain.TerrainBrushStrength * brushSpeed;
                                hoverTerrain.TryPaintTerrainSurface(rayOrigin, rayDir, delta, out _);
                                break;
                            }
                        }
                    }
                }

                // End the stroke: capture the after-state and record undo.
                if (_brushObj != null && (!leftDown || leftReleased))
                {
                    if (paintMode)
                    {
                        var afterSplat = _brushObj.CaptureTerrainSplat();
                        if (_brushSplatBefore != null && afterSplat != null)
                            _bridge.OnTerrainLayerPainted?.Invoke(_brushObj, _brushSplatBefore, afterSplat);
                    }
                    else
                    {
                        var after = _brushObj.CaptureTerrainHeights();
                        if (_brushBefore != null && after != null)
                            _bridge.OnTerrainPainted?.Invoke(_brushObj, _brushBefore, after);
                    }
                    _brushObj = null;
                    _brushBefore = null;
                    _brushSplatBefore = null;
                }

                //  3D brush ring ON the terrain surface + Ctrl+scroll resize 
                {
                    // The ring color + transparency come from the terrain's own properties
                    // (editable in the Terrain Brush panel and saved with the scene)  the
                    // viewport no longer overrides them per tool.
                    if (hoverTerrain != null && hoverPoint.HasValue)
                    {
                        hoverTerrain.BrushIndicatorPos = hoverPoint.Value;
                        hoverTerrain.ShowBrushIndicator = true;
                        if (_brushIndicatorObj != null && _brushIndicatorObj != hoverTerrain)
                            _brushIndicatorObj.ShowBrushIndicator = false;
                        _brushIndicatorObj = hoverTerrain;

                        // Ctrl + scroll = bigger / smaller brush. Only intercept scroll when
                        // the brush tool is actually active AND Ctrl is held; otherwise let
                        // scroll pass through to camera zoom.
                        bool brushScroll = _bridge.TerrainBrushActive && _bridge.ViewportCtrlHeld;
                        if (brushScroll)
                        {
                            float wheel = ImGui.GetIO().MouseWheel;
                            if (wheel != 0f)
                            {
                                hoverTerrain.TerrainBrushSize = Math.Clamp(
                                    hoverTerrain.TerrainBrushSize * (1f + wheel * 0.08f), 0.5f, 200f);
                                Console.WriteLine($"[Viewport] Brush size → {hoverTerrain.TerrainBrushSize:F1}");
                            }
                        }
                    }
                    else if (_brushIndicatorObj != null)
                    {
                        _brushIndicatorObj.ShowBrushIndicator = false;
                        _brushIndicatorObj = null;
                    }
                }

                //  Brush cursor overlay: the ortho (screen-space) circle is GONE  only
                // the 3D translucent ring on the terrain surface (DrawTerrainBrushIndicator)
                // shows the brush area, using the user-editable ring color. A tiny center
                // dot + size readout remain so the exact hover point and radius are visible.
                if (hoverPoint.HasValue && hoverTerrain != null)
                {
                    var p = TransformGizmo.ProjectToScreen(cam, hoverPoint.Value, vpw, vph);
                    if (p.X >= 0f && p.X <= vpw && p.Y >= 0f && p.Y <= vph)
                    {
                        var center = SceneToScreen(p.X, vph - p.Y);
                        uint col = ImGui.ColorConvertFloat4ToU32(
                            new Vector4(hoverTerrain.BrushIndicatorColor, 0.95f));
                        var dl = ImGui.GetWindowDrawList();
                        dl.AddCircleFilled(center, 2.5f, col);

                        // Brush size readout under the cursor (resize with Ctrl+scroll).
                        string sizeLabel = $"Brush {hoverTerrain.TerrainBrushSize:F1}  Ctrl+Scroll";
                        var sizeSize = ImGui.CalcTextSize(sizeLabel);
                        dl.AddText(center + new Vector2(-sizeSize.X * 0.5f, 14f),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f)), sizeLabel);
                    }
                }
            }

            //  Marquee (rubber-band) multi-select for 3D editor objects 
            // Left-press on empty viewport space starts a drag rectangle; on release
            // every object whose projected screen position lands inside the rect is
            // selected (Shift/Ctrl held = added to the current selection).
            if (!_previewMode && hasSceneTexture && _bridge.EditorObjectManager != null
                && _bridge.Camera != null && _bridge.SceneTextureWidth > 0 && _bridge.SceneTextureHeight > 0)
            {
                bool leftPressedNow = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
                bool leftDownNow = ImGui.IsMouseDown(ImGuiMouseButton.Left);
                bool leftReleasedNow = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

                // Start the marquee: press on the image while NOT grabbing the gizmo,
                // NOT hovering the views button, NOT dragging a UI element, and NOT
                // painting with the terrain brush.
                if (_marqueeStart == null && leftPressedNow && mouseOverImage && _dragMode == DragMode.None
                    && !_bridge.TerrainBrushActive && _brushObj == null
                    && !IsGizmoHitAtMouse() && SkySunHandleAtMouse() == null
                    && !IsMouseOverViewportViewsButton() && !IsMouseOverLeftToolbar())
                {
                    _marqueeStart = new Vector2(_bridge.ViewportMouseX, _bridge.ViewportMouseY);
                    _marqueeCurrent = _marqueeStart.Value;
                    _marqueeActive = false;
                }

                if (_marqueeStart != null)
                {
                    if (leftDownNow && _bridge.ViewportMouseX >= 0f && _bridge.ViewportMouseY >= 0f)
                    {
                        _marqueeCurrent = new Vector2(_bridge.ViewportMouseX, _bridge.ViewportMouseY);
                        // Only treat it as a marquee once the mouse actually moves a few px
                        if (!_marqueeActive && Vector2.Distance(_marqueeCurrent, _marqueeStart.Value) > 4f)
                            _marqueeActive = true;
                    }

                    if (leftReleasedNow || (!leftDownNow && !leftReleasedNow))
                    {
                        if (_marqueeActive)
                        {
                            ApplyMarqueeSelection(_marqueeStart.Value, _marqueeCurrent);
                            // The marquee already handled selection  don't let the click
                            // raycast below also deselect on this empty-space release.
                            _bridge.IsViewportClicked = false;
                        }
                        _marqueeStart = null;
                        _marqueeActive = false;
                    }
                }

                //  Draw the marquee rectangle overlay (screen space) 
                if (_marqueeActive && _marqueeStart != null)
                {
                    var dl = ImGui.GetWindowDrawList();
                    var a = SceneToScreen(_marqueeStart.Value.X, _marqueeStart.Value.Y);
                    var b = SceneToScreen(_marqueeCurrent.X, _marqueeCurrent.Y);
                    uint fill = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.6f, 1f, 0.12f));
                    uint border = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.7f, 1f, 0.9f));
                    dl.AddRectFilled(a, b, fill);
                    dl.AddRect(a, b, border, 0f, ImDrawFlags.None, 1.5f);
                }
            }

            //  3D Object click-to-select (raycast) 
            // Note: does NOT check SelectedUIElement==null  clicking the viewport should always
            // attempt to select a 3D/editor object, even when a UI element is selected in the Hierarchy.
            // If a 3D object is hit, SelectedUIElement is cleared below (line ~1895).
            // IMPORTANT: If the click hits the currently-selected object's gizmo, the gizmo WINS
            // (selection stays on the already-selected object) so you can drag it even when it
            // overlaps another object. Only when the gizmo is NOT hit does the nearest ray win.
            if (!_previewMode && _bridge.EditorObjectManager != null && _bridge.Camera != null
                && _bridge.IsViewportClicked && !_bridge.TerrainBrushActive
                && _bridge.SceneTextureWidth > 0 && _bridge.SceneTextureHeight > 0)
            {
                var cam = _bridge.Camera;
                var mgr = _bridge.EditorObjectManager;

                //  Step 1: gizmo priority  if the click lands on the SINGLE gizmo
                // (one gizmo at the group center for multi-select), keep the current
                // selection (gizmo wins over overlapping objects).
                bool gizmoClaimedClick = false;
                if (_bridge.SelectedEditorObject != null && _bridge.EditorGizmo != null)
                {
                    int vpwG = _bridge.SceneTextureWidth > 0 ? _bridge.SceneTextureWidth : 1920;
                    int vphG = _bridge.SceneTextureHeight > 0 ? _bridge.SceneTextureHeight : 1080;
                    // Flip Y: ImGui Y=0=top → GL Y=0=bottom
                    float glClickYG = vphG - _bridge.ViewportClickY;
                    var clickScreen = new Vector2(_bridge.ViewportClickX, glClickYG);
                    if (_bridge.GetEditorGizmoCenter() is Vector3 gizmoCenterG)
                    {
                        if (_bridge.EditorGizmo.HitTest(clickScreen, cam, gizmoCenterG, vpwG, vphG) != TransformGizmo.Axis.None)
                            gizmoClaimedClick = true;
                    }
                }

                if (!gizmoClaimedClick)
                {
                    // Flip Y: ImGui Y=0=top → OpenGL Y=0=bottom
                    float clickY = _bridge.SceneTextureHeight - _bridge.ViewportClickY;
                    cam.ScreenToRay(
                        _bridge.ViewportClickX, clickY,
                        _bridge.SceneTextureWidth, _bridge.SceneTextureHeight,
                        out Vector3 rayOrigin, out Vector3 rayDir);

                    if (mgr.Raycast(rayOrigin, rayDir, out float hitDist, out Vector3 hitPoint) is EditorObject hitObj)
                    {
                        // Don't clear gizmo pivot  each object stores its own.
                        // Ctrl/Shift+Click toggles the object in the multi-selection set;
                        // plain click replaces the selection with just this object.
                        bool ctrlHeld = _bridge.ViewportCtrlHeld;
                        bool shiftHeld = _bridge.ViewportShiftHeld;
                        if (ctrlHeld || shiftHeld)
                            _bridge.ToggleEditorObjectSelection(hitObj);
                        else
                            _bridge.SelectEditorObject(hitObj);
                        _bridge.SelectedUIElement = null;
                        _bridge.SelectedUIElements.Clear();
                        _bridge.SelectedObject = null;
                        _bridge.SelectedAgent = null;
                        Console.WriteLine($"[Viewport] Raycast selected 3D object: {hitObj.Name}");
                    }
                    else if (_bridge.SelectedEditorObjects.Count > 0 && !_bridge.ViewportCtrlHeld && !_bridge.ViewportShiftHeld)
                    {
                        // No object hit and no gizmo hit → deselect (Ctrl/Shift-click keeps the selection)
                        _bridge.SelectEditorObject(null);
                        Console.WriteLine("[Viewport] Deselected 3D object (empty click)");
                    }
                }
            }

            //  Sky sun gizmo: drag the gold sun handle to aim the sun 
            // Runs before the transform gizmo so grabbing the sun never moves the marker.
            // The drag continues even if the cursor leaves the image (release always ends it).
            if (!_previewMode && _bridge.Camera != null
                && !_bridge.TerrainBrushActive && _brushObj == null
                && _bridge.SceneTextureWidth > 0 && _bridge.SceneTextureHeight > 0)
            {
                var cam = _bridge.Camera;
                int vpw = _bridge.SceneTextureWidth;
                int vph = _bridge.SceneTextureHeight;
                bool leftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
                bool leftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
                bool leftReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

                if (_skySunDragObj != null)
                {
                    // Continue dragging: aim the sun at the cursor ray (Shift = snap to 5/15).
                    if (mouseOverImage)
                    {
                        float glMouseY = vph - _bridge.ViewportMouseY;
                        cam.ScreenToRay(_bridge.ViewportMouseX, glMouseY, vpw, vph,
                            out Vector3 sunOrigin, out Vector3 sunDir);
                        _skySunDragObj.AimSkySunFromRay(sunOrigin, sunDir, ImGui.GetIO().KeyShift);
                        // Keep any placed Light marker's direction in sync with the sun being
                        // dragged, so the actual lighting follows the gizmo live (the Light
                        // marker overrides the sky sun in ApplyEnvironmentMarkers).
                        if (_skySunDragLightObj != null)
                            _skySunDragLightObj.LightDirection = _skySunDragObj.GetSkySunDirection();
                    }
                    if (leftReleased || !leftDown)
                    {
                        var skyEnd = _skySunDragObj;
                        var lightEnd = _skySunDragLightObj;
                        // Record undo (old pitch/yaw → new) so Ctrl+Z reverts the drag. When a
                        // Light marker was synced, its old/new direction rides along too.
                        _bridge.OnSkySunChanged?.Invoke(skyEnd, _skySunDragOldPitch, _skySunDragOldYaw,
                            skyEnd.SkySunPitch, skyEnd.SkySunYaw,
                            lightEnd, _skySunDragOldLightDir, lightEnd?.LightDirection);
                        Console.WriteLine($"[Viewport] Sun aimed on '{skyEnd.Name}' → pitch={skyEnd.SkySunPitch:F1}, yaw={skyEnd.SkySunYaw:F1}");
                        _skySunDragObj = null;
                        _skySunDragOldPitch = null;
                        _skySunDragOldYaw = null;
                        _skySunDragLightObj = null;
                        _skySunDragOldLightDir = null;
                    }
                }
                else if (mouseOverImage && leftClicked && SkySunHandleAtMouse() is { } skyDrag)
                {
                    // Grabbing the sun handle also selects the Sky marker when it isn't
                    // already part of the selection, so the drag works even when the Sky
                    // wasn't the active object  without clobbering an existing multi-select.
                    if (!_bridge.SelectedEditorObjects.Contains(skyDrag))
                        _bridge.SelectEditorObject(skyDrag);
                    _skySunDragObj = skyDrag;
                    _skySunDragOldPitch = skyDrag.SkySunPitch;
                    _skySunDragOldYaw = skyDrag.SkySunYaw;
                    // If the sun was following time-of-day, seed the override at its current
                    // position so the drag starts exactly where the sun is drawn.
                    if (!skyDrag.SkySunPitch.HasValue || !skyDrag.SkySunYaw.HasValue)
                        skyDrag.SetSkySunFromDirection(skyDrag.GetSkySunDirection());
                    // A placed Light marker overrides the sky sun for the actual lighting 
                    // capture it so the drag keeps its direction in sync (lighting follows the
                    // gizmo) and undo can restore its pre-drag direction.
                    _skySunDragLightObj = null;
                    _skySunDragOldLightDir = null;
                    if (_bridge.EditorObjectManager != null)
                    {
                        // The sky drives a DIRECT light  sync that one (matching
                        // EditorObject.PickSunLight). No Direct light → nothing to sync.
                        var sunLight = EditorObject.PickSunLight(_bridge.EditorObjectManager.Objects);
                        if (sunLight != null)
                        {
                            _skySunDragLightObj = sunLight;
                            _skySunDragOldLightDir = sunLight.LightDirection;
                        }
                    }
                    Console.WriteLine($"[Viewport] Sun drag started on '{skyDrag.Name}'"
                        + (_skySunDragLightObj != null
                            ? $" (Light marker '{_skySunDragLightObj.Name}' direction will follow)"
                            : ""));
                }

                //  Sun handle hover highlight (also shown while dragging) 
                var hoverSky = _skySunDragObj ?? SkySunHandleAtMouse();
                if (mouseOverImage && hoverSky is { } skyHover
                    && skyHover.GetSkySunHandleCenter() is Vector3 sunHoverWorld)
                {
                    var p = TransformGizmo.ProjectToScreen(cam, sunHoverWorld, vpw, vph);
                    if (p.X >= -20f && p.X <= vpw + 20f && p.Y >= -20f && p.Y <= vph + 20f)
                    {
                        var center = SceneToScreen(p.X, vph - p.Y);
                        var dl = ImGui.GetWindowDrawList();
                        uint ring = ImGui.ColorConvertFloat4ToU32(_skySunDragObj != null
                            ? new Vector4(1f, 0.85f, 0.30f, 1f)
                            : new Vector4(1f, 0.85f, 0.30f, 0.65f));
                        uint glow = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.85f, 0.30f, 0.22f));
                        dl.AddCircle(center, 15f, ring, 32, 2f);
                        dl.AddCircle(center, 20f, glow, 32, 1f);
                        if (_skySunDragObj == null)
                            dl.AddText(center + new Vector2(22f, -9f),
                                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.9f, 0.5f, 0.95f)),
                                "Drag to aim the sun");
                    }
                }
            }

            //  Gizmo mouse interaction (drag to transform selected editor object) 
            // Uses screen-space coordinates  gizmo renders at bottom-center of viewport
            if (!_previewMode && _bridge.Camera != null && _bridge.EditorGizmo != null
                && _bridge.SelectedEditorObject != null && mouseOverImage
                && !_bridge.TerrainBrushActive && _brushObj == null
                && _skySunDragObj == null && SkySunHandleAtMouse() == null
                && _bridge.SceneTextureWidth > 0 && _bridge.SceneTextureHeight > 0)
            {
                var cam = _bridge.Camera;
                var gizmo = _bridge.EditorGizmo;
                var selected = _bridge.SelectedEditorObject;

                int vpw = _bridge.SceneTextureWidth;
                int vph = _bridge.SceneTextureHeight;

                // Sky markers have no meaningful rotation/scale (rotation is fixed and
                // scale is ignored), so when a Sky object is in the selection the gizmo is
                // locked to Translate  rotate/scale are neither drawn nor hit-tested.
                bool skySelected = _bridge.SelectionHasSky;
                gizmo.AllowRotate = !skySelected;
                gizmo.AllowScale = !skySelected;

                // Set gizmo mode from bridge (EffectiveMode clamps it to Translate for Sky)
                gizmo.Mode = (TransformGizmo.GizmoMode)_bridge.GizmoMode;

                // Flip Y: ImGui Y=0=top → GL Y=0=bottom
                float glMouseY = vph - _bridge.ViewportMouseY;
                Vector2 mouseScreen = new(_bridge.ViewportMouseX, glMouseY);

                bool leftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
                bool leftReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

                if (gizmo.IsDragging)
                {
                    // If a popup/menu opened mid-drag, cancel the drag immediately
                    // so clicks inside the popup don't move scene objects.
                    if (suppressInput)
                    {
                        gizmo.EndDrag();
                    }
                    else
                    {
                        // Update gizmo drag with screen-space mouse (moves ALL dragged objects together)
                        gizmo.UpdateDrag(mouseScreen, cam, vpw, vph);
                        if (leftReleased)
                        {
                            // Capture the full dragged set BEFORE EndDrag clears it, so undo can
                            // restore every object that was moved (multi-select aware).
                            var draggedSet = gizmo.DragTargets.ToArray();
                            gizmo.EndDrag();
                            // Record undo for the gizmo transform change(s)
                            _bridge.OnGizmoDragEnded?.Invoke(draggedSet);
                            Console.WriteLine($"[Viewport] Gizmo drag ended on {draggedSet.Length} object(s)");
                        }
                    }
                }
                else
                {
                    // Hit test the SINGLE gizmo (one gizmo at the group center for
                    // multi-select). Dragging it moves ALL selected objects together.
                    TransformGizmo.Axis hitAxis = TransformGizmo.Axis.None;
                    if (_bridge.GetEditorGizmoCenter() is Vector3 gizmoPos2)
                        hitAxis = gizmo.HitTest(mouseScreen, cam, gizmoPos2, vpw, vph);
                    if (leftClicked && hitAxis != TransformGizmo.Axis.None && selected != null && !suppressInput)
                    {
                        // Snapshot transform + pivot override BEFORE dragging for EVERY selected
                        // object so undo can restore the whole multi-select drag.
                        foreach (var obj in _bridge.SelectedEditorObjects)
                        {
                            obj.LastGizmoPosition = obj.Position;
                            obj.LastGizmoRotation = obj.RotationEuler;
                            obj.LastGizmoScale = obj.Scale;
                            obj.LastGizmoPivot = obj.GizmoPivotOverride;
                        }
                        gizmo.StartDrag(hitAxis, mouseScreen, selected, _bridge.SelectedEditorObjects.ToArray());
                        Console.WriteLine($"[Viewport] Gizmo drag started on '{selected.Name}' (+{_bridge.SelectedEditorObjects.Count - 1} selected) axis={hitAxis}");
                    }
                }
            }

        }
        else
        {
            // No texture and no editor scene  show placeholder
            var center = ImGui.GetCursorScreenPos() + avail * 0.5f;
            var textSize = ImGui.CalcTextSize("No Scene");
            ImGui.GetWindowDrawList().AddText(
                center - textSize * 0.5f,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.4f, 1f)),
                "No Scene");
        }

        //  Camera view menu overlay (top-left corner of the viewport image) 
        DrawViewportCameraOverlay(hasSceneTexture);

        //  Editor tool toolbar (left edge of the viewport image) 
        DrawViewportLeftToolbar(hasSceneTexture);

        //  Active view label (Top / Front / Left / )  top-right corner 
        DrawViewportViewLabel(hasSceneTexture);

        //  Terrain triangle-count HUD (bottom-left corner, edit mode only) 
        DrawViewportTerrainStats(hasSceneTexture);

        //  Type labels above placed Camera markers (edit mode only) 
        DrawEditorObjectTypeLabels(hasSceneTexture);

        ImGui.End();
    }

    /// <summary>Draw a small type tag ("Camera" / "Light" / "Sky") above every placed
    /// camera, light and sky marker so it's obvious which editor object is which.
    /// Edit mode only.</summary>
    private void DrawEditorObjectTypeLabels(bool hasSceneTexture)
    {
        if (_previewMode || !hasSceneTexture || _bridge.Camera == null) return;
        if (_imageSize.X <= 0f || _imageSize.Y <= 0f) return;

        var mgr = _bridge.EditorObjectManager;
        if (mgr == null || mgr.Objects.Count == 0) return;

        var cam = _bridge.Camera;
        var dl = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        float fontSize = _bridge.ViewportFontSize > 0f ? _bridge.ViewportFontSize * 0.85f : 13f;

        foreach (var obj in mgr.Objects)
        {
            if (obj == null) continue;
            if (obj.PrimitiveType != EditorPrimitiveType.Camera &&
                obj.PrimitiveType != EditorPrimitiveType.Light &&
                obj.PrimitiveType != EditorPrimitiveType.Sky) continue;
            if (!obj.IsVisible) continue;

            // Project the marker's world position to scene pixel coords (Y-up from GL).
            // Uses the same pivot logic as marquee selection so the tag follows the gizmo.
            var p = TransformGizmo.ProjectToScreen(cam, obj.GizmoPivotOverride ?? obj.Position, (int)_texW, (int)_texH);
            if (p.X < 0 || p.X > _texW || p.Y < 0 || p.Y > _texH) continue;

            // Flip to scene Y-down, then to ImGui screen space
            float sceneY = _texH - p.Y;
            var screen = SceneToScreen(p.X, sceneY);

            bool isCamera = obj.PrimitiveType == EditorPrimitiveType.Camera;
            bool isLight = obj.PrimitiveType == EditorPrimitiveType.Light;
            string tag = isCamera ? "Camera" : (isLight ? "Light" : "Sky");
            var textSize = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, tag);
            float padX = 5f, padY = 2f;
            var tagMin = new Vector2(screen.X - textSize.X * 0.5f - padX, screen.Y - textSize.Y - 16f);
            var tagMax = new Vector2(screen.X + textSize.X * 0.5f + padX, tagMin.Y + textSize.Y + padY * 2f);

            // Skip if the tag would be drawn outside the visible image
            if (tagMax.X < _imageMin.X || tagMin.X > _imageMax.X ||
                tagMax.Y < _imageMin.Y || tagMin.Y > _imageMax.Y) continue;

            // Camera → teal, Light → warm amber, Sky → sky blue
            uint bg = ImGui.ColorConvertFloat4ToU32(isCamera
                ? new Vector4(0.1f, 0.35f, 0.4f, 0.85f)
                : isLight
                    ? new Vector4(0.45f, 0.32f, 0.06f, 0.85f)
                    : new Vector4(0.08f, 0.22f, 0.42f, 0.85f));
            uint border = ImGui.ColorConvertFloat4ToU32(isCamera
                ? new Vector4(0.3f, 0.8f, 1f, 0.9f)
                : isLight
                    ? new Vector4(1f, 0.75f, 0.3f, 0.9f)
                    : new Vector4(0.4f, 0.65f, 1f, 0.9f));
            uint textCol = ImGui.ColorConvertFloat4ToU32(isCamera
                ? new Vector4(0.7f, 1f, 1f, 1f)
                : isLight
                    ? new Vector4(1f, 0.95f, 0.7f, 1f)
                    : new Vector4(0.75f, 0.85f, 1f, 1f));
            dl.AddRectFilled(tagMin, tagMax, bg, 3f);
            dl.AddRect(tagMin, tagMax, border, 3f, ImDrawFlags.None, 1f);
            dl.AddText(font, fontSize, tagMin + new Vector2(padX, padY),
                textCol, tag);
        }
    }

    /// <summary>Static mapping of (main-row key, numpad key) → view preset, allocated once
    /// so the per-frame shortcut check doesn't allocate a fresh array each frame.</summary>
    private static readonly (ImGuiKey main, ImGuiKey numpad, Camera.EditorViewPreset preset)[] CameraViewShortcuts =
    [
        (ImGuiKey._1, ImGuiKey.Keypad1, Camera.EditorViewPreset.Top),
        (ImGuiKey._2, ImGuiKey.Keypad2, Camera.EditorViewPreset.Front),
        (ImGuiKey._3, ImGuiKey.Keypad3, Camera.EditorViewPreset.Left),
        (ImGuiKey._4, ImGuiKey.Keypad4, Camera.EditorViewPreset.Right),
        (ImGuiKey._5, ImGuiKey.Keypad5, Camera.EditorViewPreset.Back),
        (ImGuiKey._6, ImGuiKey.Keypad6, Camera.EditorViewPreset.Bottom),
        (ImGuiKey._7, ImGuiKey.Keypad7, Camera.EditorViewPreset.Perspective),
    ];

    /// <summary>Handle number-key shortcuts for camera view presets while the viewport
    /// is focused in editor mode: 1=Top, 2=Front, 3=Left, 4=Right, 5=Back, 6=Bottom,
    /// 7=Perspective. Works with both the main number row and the numpad.</summary>
    private void HandleCameraViewShortcuts()
    {
        if (_previewMode || _fullscreenMode || _bridge.Camera == null) return;
        if (!_bridge.IsViewportFocused) return;

        // Skip when an ImGui text input / editing field is focused (don't steal typing)
        if (ImGui.GetIO().WantTextInput) return;
        // Don't snap the camera while the user is mid-gizmo-drag
        if (_bridge.EditorGizmo?.IsDragging == true) return;
        // Don't snap the camera while terrain brush tools are active
        if (_bridge.TerrainBrushActive) return;
        // Don't snap while any popup/menu is open or just closed
        if (_bridge.SuppressViewportInput) return;

        // Terrain brush tools are toolbar-only (no keyboard shortcuts)  the previous
        // B/C/S/F toggles were removed because S collided with fly-camera movement and the
        // user wanted terrain-editor key assignments disabled entirely.

        for (int i = 0; i < CameraViewShortcuts.Length; i++)
        {
            var (main, numpad, preset) = CameraViewShortcuts[i];
            if (ImGui.IsKeyPressed(main) || ImGui.IsKeyPressed(numpad))
            {
                _bridge.Camera?.SetEditorViewPreset(preset);
                Console.WriteLine($"[Viewport] Shortcut camera preset: {preset}");
                return;
            }
        }
    }

    /// <summary>Determine the editor view label from the current camera orientation.
    /// Uses the camera's Front vector: near-vertical pitch → Top/Bottom; otherwise the
    /// dominant horizontal axis decides Front/Back/Left/Right; anything in between is
    /// reported as Perspective (a 3/4 view).</summary>
    private string GetActiveViewLabel(out Vector4 labelColor)
    {
        var cam = _bridge.Camera;
        if (cam == null)
        {
            labelColor = new Vector4(0.9f, 0.9f, 0.9f, 1f);
            return "Perspective";
        }

        Vector3 f = cam.Front;
        float ay = MathF.Abs(f.Y);
        float ax = MathF.Abs(f.X);
        float az = MathF.Abs(f.Z);

        var axisCol = new Vector4(0.55f, 0.9f, 1f, 1f);
        var diagCol = new Vector4(0.9f, 0.9f, 0.9f, 1f);

        if (ay > 0.7f)
        {
            labelColor = axisCol;
            return f.Y < 0f ? "Top" : "Bottom";
        }
        if (ay < 0.3f)
        {
            if (az > 0.7f)
            {
                labelColor = axisCol;
                return f.Z < 0f ? "Front" : "Back";
            }
            if (ax > 0.7f)
            {
                labelColor = axisCol;
                return f.X > 0f ? "Right" : "Left";
            }
        }

        labelColor = diagCol;
        return "Perspective";
    }

    /// <summary>Draw the active view label (e.g. "Top", "Front", "Left") in the top-right
    /// corner of the viewport image, so the user always knows which orientation the camera
    /// is in  even after free-flying around. Hidden in preview mode.</summary>
    private void DrawViewportViewLabel(bool hasSceneTexture)
    {
        if (_previewMode || !hasSceneTexture || _bridge.Camera == null) return;
        if (_imageSize.X <= 0f || _imageSize.Y <= 0f) return;

        string label = GetActiveViewLabel(out Vector4 col);
        bool isOrtho = _bridge.Camera?.IsOrthographic ?? false;
        string text = isOrtho ? $"{label}  Ortho" : $"{label}  Persp";

        var font = ImGui.GetFont();
        float fontSize = _bridge.ViewportFontSize > 0f ? _bridge.ViewportFontSize : 16f;
        var textSize = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, text);

        var dl = ImGui.GetWindowDrawList();
        // Top-right corner of the rendered image, inside the image bounds
        float pad = 8f;
        var bgMin = new Vector2(_imageMax.X - textSize.X - pad * 2f, _imageMin.Y + pad);
        var bgMax = new Vector2(_imageMax.X - pad, bgMin.Y + textSize.Y + pad);

        dl.AddRectFilled(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.45f)), 4f);
        dl.AddRect(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.15f)), 4f, ImDrawFlags.None, 1f);
        dl.AddText(font, fontSize, bgMin + new Vector2(pad, pad * 0.5f),
            ImGui.ColorConvertFloat4ToU32(col), text);
    }

    /// <summary>Draw a compact terrain-stats HUD in the bottom-left corner of the viewport
    /// image (edit mode only): total triangles of ALL terrain planes in the editor scene,
    /// plus a per-terrain breakdown when a plane is selected. Hidden in preview mode.</summary>
    private void DrawViewportTerrainStats(bool hasSceneTexture)
    {
        if (_previewMode || !hasSceneTexture) return;
        if (_imageSize.X <= 0f || _imageSize.Y <= 0f) return;

        var mgr = _bridge.EditorObjectManager;
        if (mgr == null || mgr.Objects.Count == 0) return;

        // Collect terrain planes: total triangles + the selected one (if any).
        int totalTri = 0;
        int terrainCount = 0;
        EditorObject? selected = null;
        foreach (var obj in mgr.Objects)
        {
            if (obj.PrimitiveType != EditorPrimitiveType.Plane || !obj.TerrainEnabled) continue;
            terrainCount++;
            totalTri += obj.TerrainTriangleCount;
            if (_bridge.SelectedEditorObjects.Contains(obj))
                selected = obj;
        }
        if (terrainCount == 0) return;

        var font = ImGui.GetFont();
        float fontSize = _bridge.ViewportFontSize > 0f ? _bridge.ViewportFontSize : 15f;
        float lineHeight = fontSize * 1.3f;
        float pad = 6f;

        string[] lines =
        [
            $"Terrain TRIS: {totalTri:N0}",
            $"Planes: {terrainCount}",
        ];
        if (selected != null)
        {
            lines = [
                $"Terrain TRIS: {totalTri:N0}",
                $"Planes: {terrainCount}",
                $"Selected '{selected.Name}': {selected.TerrainTriangleCount:N0}  ({selected.TerrainChunksPerSide}{selected.TerrainChunksPerSide} chunks)",
            ];
        }

        float panelW = 0f;
        for (int li = 0; li < lines.Length; li++)
            panelW = MathF.Max(panelW, font.CalcTextSizeA(fontSize, float.MaxValue, 0f, lines[li]).X);
        float panelH = lineHeight * lines.Length + pad * 2f;

        // Bottom-left corner of the rendered image.
        var dl = ImGui.GetWindowDrawList();
        var bgMin = new Vector2(_imageMin.X + 8f, _imageMax.Y - panelH - 8f);
        var bgMax = new Vector2(bgMin.X + panelW + pad * 2f, bgMin.Y + panelH);
        dl.AddRectFilled(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.45f)), 5f);
        dl.AddRect(bgMin, bgMax, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.12f)), 5f, ImDrawFlags.None, 1f);

        float ty = bgMin.Y + pad;
        for (int li = 0; li < lines.Length; li++)
        {
            var col = li == 0
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.7f, 0.3f, 1f))   // total tris (orange, like TRIS stat)
                : ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.8f, 1f));
            dl.AddText(font, fontSize, new Vector2(bgMin.X + pad, ty), col, lines[li]);
            ty += lineHeight;
        }
    }

    /// <summary>Render a compact "▲ Views" menu floating in the top-left corner of the
    /// viewport image (editor mode only). Lets the user snap the fly-camera to top-down,
    /// bottom-up, front/back, left/right or perspective views without leaving the viewport.
    /// Uses SetCursorScreenPos so it overlays the scene without disturbing the layout.</summary>
    /// <summary>Screen-space rect of the floating "▲ Views" button (top-left of the image).
    /// Shared by the overlay renderer and the marquee/click guards so a click on the
    /// button never also starts a marquee or a viewport raycast.</summary>
    private (Vector2 min, Vector2 max) GetViewportViewsButtonRect()
    {
        float padX = 8f, padY = 6f;
        var font = ImGui.GetFont();
        float vpFs = _bridge.ViewportFontSize > 0f ? _bridge.ViewportFontSize : 15f;
        var textSize = font.CalcTextSizeA(vpFs, float.MaxValue, 0f, "▲ Views");
        var min = new Vector2(_imageMin.X + 8f, _imageMin.Y + 8f);
        var max = min + new Vector2(textSize.X + padX * 2f, textSize.Y + padY * 1.6f);
        return (min, max);
    }

    /// <summary>True when the mouse currently hovers the floating "▲ Views" button.
    /// Used to suppress marquee/raycast selection while interacting with the overlay.</summary>
    private bool IsMouseOverViewportViewsButton()
    {
        var (min, max) = GetViewportViewsButtonRect();
        var mouse = ImGui.GetMousePos();
        return mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;
    }

    private void DrawViewportCameraOverlay(bool hasSceneTexture)
    {
        if (_previewMode || !hasSceneTexture || _bridge.Camera == null) return;
        if (_imageSize.X <= 0f || _imageSize.Y <= 0f) return;

        //  Draw the floating button with the draw list (NO SetCursorScreenPos  that
        // triggers ImGui's "extend window boundaries" assertion). Hit-testing is manual. 
        var (btnMin, btnMax) = GetViewportViewsButtonRect();
        var mouse = ImGui.GetMousePos();
        bool hovered = mouse.X >= btnMin.X && mouse.X <= btnMax.X &&
                       mouse.Y >= btnMin.Y && mouse.Y <= btnMax.Y;
        bool clicked = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        var dl = ImGui.GetWindowDrawList();
        uint bgCol = ImGui.ColorConvertFloat4ToU32(hovered
            ? new Vector4(0.30f, 0.34f, 0.50f, 0.92f)
            : new Vector4(0.14f, 0.16f, 0.24f, 0.88f));
        uint borderCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.55f, 0.8f, 0.7f));
        dl.AddRectFilled(btnMin, btnMax, bgCol, 5f);
        dl.AddRect(btnMin, btnMax, borderCol, 5f, ImDrawFlags.None, 1f);

        var font = ImGui.GetFont();
        float fontSize = _bridge.ViewportFontSize > 0f ? _bridge.ViewportFontSize : 15f;
        var textSize = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, "▲ Views");
        dl.AddText(font, fontSize, btnMin + new Vector2(8f, (btnMax.Y - btnMin.Y - textSize.Y) * 0.5f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 1f, 1f)), "▲ Views");

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip("Camera view presets\nShortcuts: 1=Top 2=Front 3=Left 4=Right 5=Back 6=Bottom 7=Perspective");
        }

        if (clicked)
            ImGui.OpenPopup("viewport_camera_views");

        // Position the popup just below the button
        ImGui.SetNextWindowPos(new Vector2(btnMin.X, btnMax.Y + 2f), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("viewport_camera_views"))
        {
            string[] viewLabels =
            [
                " Perspective    \t7", " Top-Down       \t1", " Bottom-Up      \t6",
                " Front          \t2", " Back           \t5", "→ Left            \t3", "← Right           \t4",
            ];
            Camera.EditorViewPreset[] viewPresets =
            [
                Camera.EditorViewPreset.Perspective, Camera.EditorViewPreset.Top, Camera.EditorViewPreset.Bottom,
                Camera.EditorViewPreset.Front, Camera.EditorViewPreset.Back, Camera.EditorViewPreset.Left, Camera.EditorViewPreset.Right,
            ];
            for (int vi = 0; vi < viewPresets.Length; vi++)
            {
                if (ImGui.MenuItem(viewLabels[vi]))
                {
                    _bridge.Camera?.SetEditorViewPreset(viewPresets[vi]);
                    Console.WriteLine($"[Viewport] Camera preset: {viewPresets[vi]}");
                }
            }
            ImGui.Separator();

            //  Projection: Perspective vs Orthographic 
            bool isOrtho = _bridge.Camera?.IsOrthographic ?? false;
            if (ImGui.MenuItem(" Perspective", null, !isOrtho))
            {
                if (isOrtho) _bridge.Camera?.ToggleProjection();
                Console.WriteLine("[Viewport] Projection: perspective");
            }
            if (ImGui.MenuItem(" Orthographic", null, isOrtho))
            {
                if (!isOrtho) _bridge.Camera?.ToggleProjection();
                Console.WriteLine("[Viewport] Projection: orthographic");
            }
            if (isOrtho)
            {
                // Ortho zoom  adjusts the ortho view volume half-height
                float orthoSize = _bridge.Camera?.OrthoSize ?? 20f;
                if (ImGui.SliderFloat("Ortho Zoom", ref orthoSize, 2f, 100f, "%.0f"))
                {
                    if (_bridge.Camera != null) _bridge.Camera.OrthoSize = orthoSize;
                }
            }
            ImGui.Separator();

            if (ImGui.MenuItem(" Focus Selection", _bridge.SelectedEditorObjects.Count > 0))
            {
                _bridge.FocusCameraOnSelected?.Invoke();
            }
            ImGui.EndPopup();
        }
    }

    /// <summary>True when the mouse currently hovers the floating left-edge toolbar.
    /// Used to suppress marquee/raycast selection while interacting with the overlay.</summary>
    private bool IsMouseOverLeftToolbar()
    {
        var mouse = ImGui.GetMousePos();
        return mouse.X >= _leftToolbarMin.X && mouse.X <= _leftToolbarMax.X &&
               mouse.Y >= _leftToolbarMin.Y && mouse.Y <= _leftToolbarMax.Y;
    }

    /// <summary>Draw the editor tool toolbar as a floating vertical strip on the LEFT edge
    /// of the viewport image (edit mode only). Uses the window draw list + manual
    /// hit-testing  same pattern as the floating  Views button.</summary>
    private void DrawViewportLeftToolbar(bool hasSceneTexture)
    {
        if (_previewMode || !hasSceneTexture || _bridge.EditorObjectManager == null) return;
        if (_imageSize.X <= 0f || _imageSize.Y <= 0f) return;

        // Clear bounds until drawn below (guards stay inert if we bail early)
        _leftToolbarMin = _leftToolbarMax = new Vector2(-1f, -1f);

        const float btnW = 118f, btnH = 25f, gap = 5f, padX = 8f;
        float x = _imageMin.X + padX;
        // Start below the floating "▲ Views" button (top-left corner)
        float y = GetViewportViewsButtonRect().max.Y + 6f;

        var dl = ImGui.GetWindowDrawList();
        var mouse = ImGui.GetMousePos();
        var font = ImGui.GetFont();

        // Small helper: draws one button, returns true when clicked this frame.
        bool ToolButton(string label, bool active, Vector4 activeCol, string tooltip, out float nextY)
        {
            var min = new Vector2(x, y);
            var max = new Vector2(x + btnW, y + btnH);
            nextY = max.Y + gap;

            bool hovered = mouse.X >= min.X && mouse.X <= max.X &&
                           mouse.Y >= min.Y && mouse.Y <= max.Y;
            // Block toolbar clicks when any popup/menu is open (modal mode).
            bool anyPopup = ImGui.IsPopupOpen(null, ImGuiPopupFlags.AnyPopupId);
            bool clicked = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !anyPopup;

            uint bg = ImGui.ColorConvertFloat4ToU32(hovered
                ? (active ? activeCol : new Vector4(0.34f, 0.38f, 0.55f, 0.95f))
                : (active ? activeCol : new Vector4(0.14f, 0.16f, 0.24f, 0.90f)));
            uint border = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.55f, 0.8f, 0.75f));
            dl.AddRectFilled(min, max, bg, 4f);
            dl.AddRect(min, max, border, 4f, ImDrawFlags.None, 1f);

            float toolFs = _bridge.ViewportFontSize > 0f ? _bridge.ViewportFontSize * 0.83f : 12.5f;
            var textSize = font.CalcTextSizeA(toolFs, float.MaxValue, 0f, label);
            dl.AddText(font, toolFs, min + new Vector2((btnW - textSize.X) * 0.5f, (btnH - textSize.Y) * 0.5f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.92f, 0.92f, 1f, 1f)), label);

            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (!string.IsNullOrEmpty(tooltip))
                    ImGui.SetTooltip(tooltip);
            }
            return clicked;
        }

        //  Gizmo mode 
        int gizmoMode = _bridge.GizmoMode;
        bool gizmoLocked = _bridge.SelectionHasSky;
        string[] gizmoLabels = ["Move", "Rotate", "Scale"];
        Vector4[] gizmoCols =
        [
            new(0.25f, 0.50f, 0.80f, 0.95f),
            new(0.30f, 0.72f, 0.40f, 0.95f),
            new(0.85f, 0.55f, 0.25f, 0.95f),
        ];
        for (int gi = 0; gi < 3; gi++)
        {
            bool locked = gizmoLocked && gi != 0;
            bool isActive = (gizmoMode == gi) || (gizmoLocked && gi == 0);
            if (ToolButton(gizmoLabels[gi], isActive, gizmoCols[gi],
                locked ? "Sky markers can only be moved" : "Gizmo mode", out y))
            {
                if (!locked)
                {
                    _bridge.GizmoMode = gi;
                    _gizmo.Mode = (TransformGizmo.GizmoMode)gi;
                }
            }
        }

        //  Freefly mouse-look toggle 
        bool flyLook = _bridge.Camera?.FlyMouseLook ?? false;
        if (ToolButton(flyLook ? "✈ Fly ON" : "✈ Fly", flyLook, new Vector4(0.20f, 0.45f, 0.75f, 0.95f),
            flyLook
                ? "Freefly mouse-look ON  click to turn off"
                : "Freefly mouse-look OFF  click to turn on, or hold Right-Click in the viewport for a temporary look", out y))
        {
            if (_bridge.Camera != null)
                _bridge.Camera.FlyMouseLook = !_bridge.Camera.FlyMouseLook;
        }

        //  Camera reset to origin 
        if (ToolButton("▲ Reset", false, new Vector4(0.35f, 0.35f, 0.50f, 0.95f),
            "Reset camera to origin (0,0,0) with default orientation  works with all camera modes including freefly", out y))
        {
            if (_bridge.Camera != null)
            {
                _bridge.Camera.ResetToOrigin();
                Console.WriteLine("[Viewport] Camera reset to origin (0,0,0)");
            }
        }

        //  Gizmo translate snap toggle 
        bool gizmoSnap = _bridge.EditorGizmo?.SnapEnabled ?? false;
        if (ToolButton(gizmoSnap ? "Snap 1u" : "Snap off", gizmoSnap, new Vector4(0.15f, 0.55f, 0.30f, 0.95f),
            "Toggle gizmo movement snap (1 world unit grid)", out y))
        {
            if (_bridge.EditorGizmo != null)
                _bridge.EditorGizmo.SnapEnabled = !gizmoSnap;
        }

        //  Terrain brush tools 
        bool sculptTool = _bridge.TerrainBrushActive && _bridge.TerrainBrushMode == 0;
        if (ToolButton(sculptTool ? "▲ Sculpt ON" : "▲ Sculpt", sculptTool, new Vector4(0.80f, 0.55f, 0.15f, 0.95f),
            "Sculpt: left-drag RAISES, Ctrl+left-drag LOWERS.\nHold Shift for fine control  Ctrl+scroll resizes the brush.", out y))
        {
            ToggleTerrainBrushMode(0);
            Console.WriteLine($"[Viewport]  Sculpt brush → {_bridge.TerrainBrushActive}");
        }

        bool paintTool = _bridge.TerrainBrushActive && _bridge.TerrainBrushMode == 1;
        if (ToolButton(paintTool ? "▲ Paint ON" : "▲ Paint", paintTool, new Vector4(0.85f, 0.35f, 0.45f, 0.95f),
            "Layer paint: paints the selected layer texture.\nLeft-drag = paint, Ctrl+left-drag = erase  Ctrl+scroll resizes the brush.", out y))
        {
            ToggleTerrainBrushMode(1);
            Console.WriteLine($"[Viewport]  Layer paint → {_bridge.TerrainBrushActive}");
        }

        bool smoothTool = _bridge.TerrainBrushActive && _bridge.TerrainBrushMode == 2;
        if (ToolButton(smoothTool ? "▲ Smooth ON" : "▲ Smooth", smoothTool, new Vector4(0.45f, 0.30f, 0.75f, 0.95f),
            "Smooth: averages the heights in the brush area  removes spikes and terraced steps.\nHold Shift for fine control.", out y))
        {
            ToggleTerrainBrushMode(2);
            Console.WriteLine($"[Viewport]  Smooth brush → {_bridge.TerrainBrushActive}");
        }

        bool flattenTool = _bridge.TerrainBrushActive && _bridge.TerrainBrushMode == 3;
        if (ToolButton(flattenTool ? "▲ Flatten ON" : "▲ Flatten", flattenTool, new Vector4(0.75f, 0.60f, 0.15f, 0.95f),
            "Flatten: levels the terrain to the height of the FIRST click of the stroke,\nlike Unreal's flatten tool. Hold Shift for fine control.", out y))
        {
            ToggleTerrainBrushMode(3);
            Console.WriteLine($"[Viewport]  Flatten brush → {_bridge.TerrainBrushActive}");
        }

        //  Height shading + contours overlays 
        var shadedSel = _bridge.SelectedEditorObject is { TerrainEnabled: true } sObj ? sObj : null;
        bool shadeOn = shadedSel?.TerrainShowHeatmap ?? false;
        if (ToolButton(shadeOn ? "☀ Shade ON" : "☀ Shade", shadeOn, new Vector4(0.75f, 0.55f, 0.15f, 0.95f),
            "Colorize the selected terrain by height (low=blue → high=red) + contour lines,\nlit by the sun  makes high/low areas obvious while sculpting.\nAuto-selects the first terrain plane if none is selected (also in the Inspector).", out y))
        {
            var shadeTerrain = _bridge.ResolveTerrainForOverlay();
            if (shadeTerrain != null)
            {
                shadeTerrain.TerrainShowHeatmap = !shadeTerrain.TerrainShowHeatmap;
                Console.WriteLine($"[Viewport] Height shading on '{shadeTerrain.Name}' → {shadeTerrain.TerrainShowHeatmap}");
            }
            else
            {
                Console.WriteLine("[Viewport] No terrain plane found to toggle height shading");
            }
        }

        var contourSel = _bridge.SelectedEditorObject is { TerrainEnabled: true } cObj ? cObj : null;
        bool contourOn = contourSel?.TerrainShowContours ?? false;
        if (ToolButton(contourOn ? "☁ Contours ON" : "☁ Contours", contourOn, new Vector4(0.45f, 0.55f, 0.30f, 0.95f),
            "Draw dark topographic contour lines every 10% height on the selected terrain\n(no heatmap colors  the texture stays fully visible).\nGreat for sculpting precision  auto-selects the first terrain plane if none is selected.", out y))
        {
            var contourTerrain = _bridge.ResolveTerrainForOverlay();
            if (contourTerrain != null)
            {
                contourTerrain.TerrainShowContours = !contourTerrain.TerrainShowContours;
                Console.WriteLine($"[Viewport] Height contours on '{contourTerrain.Name}' → {contourTerrain.TerrainShowContours}");
            }
            else
            {
                Console.WriteLine("[Viewport] No terrain plane found to toggle height contours");
            }
        }

        //  Paint tool hint (paint texture is set in Terrain Brush panel) 
        if (paintTool)
        {
            Vector4 chipCol = TerrainLayerColors[0];
            ToolButton("Paint", true, chipCol, "Paint texture assigned in Terrain Brush panel. Left-drag to paint.", out y);
        }

        //  Debug grid + shadow toggles 
        bool debugGrid = _bridge.ShowDebugGrid;
        if (ToolButton(debugGrid ? "Grid: On" : "Grid: Off", debugGrid, new Vector4(0.25f, 0.45f, 0.30f, 0.95f),
            "Toggle the editor debug grid (XZ plane at Y=0, major lines every 5 units)", out y))
        {
            _bridge.ShowDebugGrid = !debugGrid;
            PersistViewportPrefs();
            Console.WriteLine($"[Viewport] Debug grid {(debugGrid ? "disabled" : "enabled")}");
        }

        bool shadowsOn = _bridge.ShowShadows;
        if (ToolButton(shadowsOn ? "☀ Shadow: On" : "☀ Shadow: Off", shadowsOn, new Vector4(0.55f, 0.45f, 0.20f, 0.95f),
            "Toggle CSM shadows in the viewport\nOff = skip the shadow pass (fully lit, faster)", out y))
        {
            _bridge.ShowShadows = !shadowsOn;
            PersistViewportPrefs();
            Console.WriteLine($"[Viewport] Shadows {(shadowsOn ? "disabled" : "enabled")}");
        }

        // Record the full toolbar bounds for the click-suppression guard.
        _leftToolbarMin = new Vector2(x, GetViewportViewsButtonRect().max.Y + 6f);
        _leftToolbarMax = new Vector2(x + btnW, y - gap + btnH);
    }

    /// <summary>Render an animated gradient background for the viewport canvas when no scene texture is available.</summary>
    private void RenderImGuiGradient(ImDrawListPtr drawList, Vector2 min, Vector2 max)
    {
        int w = (int)(max.X - min.X);
        int h = (int)(max.Y - min.Y);
        if (w <= 0 || h <= 0) return;

        float time = (float)ImGui.GetTime();
        
        // Dark gradient colors
        var col1 = ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.12f, 1f));
        var col2 = ImGui.ColorConvertFloat4ToU32(new Vector4(0.12f, 0.10f, 0.16f, 1f));
        var col3 = ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.06f, 0.10f, 1f));
        
        // Top-to-bottom gradient
        drawList.AddRectFilledMultiColor(min, max, col1, col1, col2, col2);
        
        // Subtle animated horizontal band
        float bandY = min.Y + h * (0.3f + 0.2f * MathF.Sin(time * 0.5f));
        float bandH = h * 0.15f;
        uint bandCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.12f, 0.20f, 0.3f));
        drawList.AddRectFilled(
            new Vector2(min.X, bandY - bandH * 0.5f),
            new Vector2(max.X, bandY + bandH * 0.5f),
            bandCol);
    }
}
