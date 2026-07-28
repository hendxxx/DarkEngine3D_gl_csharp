
using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using StbImageSharp;
using System.Numerics;
using System.IO;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Viewport panel — displays the game's rendered scene texture inside an ImGui panel.
/// Supports aspect-ratio-correct scaling and click-to-select.
/// Includes interactive UI element editing: drag to move, resize from corners.
/// </summary>
public unsafe class ViewportPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    // ── Snap-to-grid state ──
    private bool _snapEnabled = true;
    private float _snapGridSize = 20f;
    private static readonly float[] SnapOptions = [5f, 10f, 20f, 40f, 50f];

    /// <summary>Snap a value to the nearest grid increment.</summary>
    private float SnapToGrid(float value) =>
        _snapEnabled ? MathF.Round(value / _snapGridSize) * _snapGridSize : value;

    // ── Preview mode: hides all editor helpers, shows scene as-in-game ──
    private bool _previewMode = false;
    // ── Fullscreen mode: used by In-Game Mode (F8), skips toolbar, fullscreen window ──
    private bool _fullscreenMode = false;

    // ── Spinning triangle demo ──
    private float _triRotation = 0f;
    private float _bgTotalTime = 0f;

    // ── In-game mode: last element clicked by mouse (for syncing keyboard focus) ──
    /// <summary>Set by DrawEditorUIPreview when a mouse click occurs in preview mode.
    /// Read and reset by IDE.RenderInGameMode() to sync keyboard focus to the clicked element.</summary>
    public UIElement? LastInGameClickedElement { get; set; }

    // ── Drag state for UI element editing ──
    private enum DragMode { None, Move, ResizeTL, ResizeTR, ResizeBL, ResizeBR }
    private DragMode _dragMode = DragMode.None;
    // Starting state when drag began (scene coords)
    private float _dragStartX, _dragStartY, _dragStartW, _dragStartH;
    private Vector2 _dragStartMouseScene; // mouse position in scene coords when drag started

    // ── Model Editor Gizmo ──
    private readonly TransformGizmo _gizmo = new();
    private TransformGizmo.Axis _gizmoHoveredAxis = TransformGizmo.Axis.None;
    private Vector3 _gizmoDragStartPosition;
    private Vector3 _gizmoDragStartRotation;
    private Vector3 _gizmoDragStartScale;

    // ── Cached conversion data (set each frame in overlay) ──
    private Vector2 _imageMin, _imageMax, _imageSize;
    private float _texW = 1f, _texH = 1f;

    /// <summary>Convert ImGui screen coordinates to scene pixel coordinates.</summary>
    private Vector2 ScreenToScene(Vector2 screenPos)
    {
        float relX = screenPos.X - _imageMin.X;
        float relY = screenPos.Y - _imageMin.Y;
        float u = relX / _imageSize.X;
        float v = relY / _imageSize.Y; // No Y-flip — scene Y=0 is top, same as ImGui
        return new Vector2(u * _texW, v * _texH);
    }

    /// <summary>Convert scene pixel coordinates to ImGui screen coordinates.</summary>
    private Vector2 SceneToScreen(float sceneX, float sceneY)
    {
        float u = sceneX / _texW;
        float v = sceneY / _texH; // No Y-flip — scene Y=0 is top, same as ImGui
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
            // Auto-center: center the element in the viewport
            if (elem.AutoCenter)
            {
                elem.X = (_texW - elem.Width) * 0.5f;
                elem.Y = (_texH - elem.Height) * 0.5f;
            }

            // Convert scene coords to screen coords (no Y-flip — scene Y=0 is top)
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

            // ── Label default: skip background if BgColor is still default (0,0,0) ──
            // This makes new Labels transparent by default, but still allows users to
            // customize BgColor/BorderColor for visible backgrounds.
            bool isLabel = elem.Type == UIElementType.Label;
            bool labelDefaultBg = isLabel && bgColor.X < 0.001f && bgColor.Y < 0.001f && bgColor.Z < 0.001f;

            // ── Draw background (filled rect) — skip for Labels with default transparent colors ──
            // Uses elemOpacity directly as alpha so the element's Opacity property is the sole
            // control for transparency (no hardcoded multiplier).
            if (!labelDefaultBg)
            {
                drawList.AddRectFilled(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(bgColor.X, bgColor.Y, bgColor.Z, 1.0f * elemOpacity)),
                    4f);
            }

            // ── Draw image element on top of background ──
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
                    // Image not loaded — show fallback text (only if set)
                    drawList.AddRectFilled(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.2f, 0.2f, 0.6f * elemOpacity)));
                    drawList.AddText(new Vector2(csx0 + 4f, csy0 + 4f),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.6f, 0.3f, 1f * elemOpacity)),
                        elem.FallbackText);
                }
            }

            // ── Draw text label with element's FontSize (skip for image elements & checkbox — checkbox has its own label rendering) ──
            if (!hasImage && elem.Type != UIElementType.Checkbox && !string.IsNullOrEmpty(elem.Text))
            {
                string label = elem.Text;
                float previewFontSize = elem.FontSize > 0f ? Math.Max(8f, elem.FontSize) : 13f;
                var textColor = useHover ? elem.HoverTextColor : elem.TextColor;

                float baseFontSize = 13f;
                float fontSizeScale = previewFontSize / baseFontSize;
                var baseSize = ImGui.CalcTextSize(label);
                float scaledW = baseSize.X * fontSizeScale;
                float scaledH = baseSize.Y * fontSizeScale;

                float textX, textY;
                float textPad = 8f;
                float availW = (csx1 - csx0) - textPad * 2f;
                float textW = Math.Min(scaledW, availW);

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
                textY = csy0 + (csy1 - csy0) * 0.5f - scaledH * 0.5f;
                textX = Math.Max(csx0 + 2f, Math.Min(textX, csx1 - textW - 2f));

                drawList.AddText(ImGui.GetFont(), previewFontSize, new Vector2(textX, textY),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(textColor.X, textColor.Y, textColor.Z, 1f * elemOpacity)),
                    label);
            }

            // ── Draw border — skip for Labels with default transparent border colors ──
            bool labelDefaultBorder = isLabel && borderColor.X < 0.001f && borderColor.Y < 0.001f && borderColor.Z < 0.001f;
            if (!labelDefaultBorder)
            {
                drawList.AddRect(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(borderColor.X, borderColor.Y, borderColor.Z, 1f * elemOpacity)),
                    4f, ImDrawFlags.None, 1.5f);
            }

            // ── Focus highlight (keyboard navigation) — glowing cyan border ──
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

            // ════════════════════════════════════════════
            //  Type-Specific Element Rendering
            // ════════════════════════════════════════════
            float elemScreenW = sx1 - sx0;
            float elemScreenH = sy1 - sy0;
            float innerPad = 6f;

            if (elem.Type == UIElementType.SliderNumber)
            {
                // ── SliderNumber: track + filled portion + thumb + value label ──
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

                // ── Interactive slider drag (preview mode only) ──
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
                // ── SliderText: track + filled portion + thumb + text label ──
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

                // ── Interactive slider drag (preview mode only) ──
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
                // ── Checkbox: square + checkmark + label ──
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
                float lblY = csy0 + (elemScreenH - ImGui.CalcTextSize(chkLabel).Y) * 0.5f;
                uint lblCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.85f, 0.95f, 1f * elemOpacity));
                drawList.AddText(ImGui.GetFont(), 13f, new Vector2(lblX, lblY), lblCol, chkLabel);
            }
            else if (elem.Type == UIElementType.Dropdown)
            {
                // ── Dropdown: box + selected text + dropdown arrow ──
                float arrowSize = 10f;
                float arrowX = csx1 - innerPad - arrowSize;
                float arrowY = csy0 + (elemScreenH - arrowSize) * 0.5f;

                // Selected value text
                string selText = elem.SelectedIndex >= 0 && elem.SelectedIndex < elem.Options.Count
                    ? elem.Options[elem.SelectedIndex]
                    : "(select)";
                uint ddTextCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.85f, 0.95f, 1f * elemOpacity));

                // Truncate if too wide
                float maxTextW = (csx1 - csx0) - innerPad * 3f - arrowSize;
                var ddTextSize = ImGui.CalcTextSize(selText);
                if (ddTextSize.X > maxTextW)
                {
                    while (selText.Length > 1 && ImGui.CalcTextSize(selText + "...").X > maxTextW)
                        selText = selText[..^1];
                    selText += "...";
                }

                float ddTextX = csx0 + innerPad;
                float ddTextY = csy0 + (elemScreenH - ddTextSize.Y) * 0.5f;
                drawList.AddText(new Vector2(ddTextX, ddTextY), ddTextCol, selText);

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
                // ── TextBox: input field with placeholder or current text ──
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

                // Truncate to fit
                var tbTextSize = ImGui.CalcTextSize(displayText);
                float maxTextW = inputW - 8f;
                if (tbTextSize.X > maxTextW)
                {
                    while (displayText.Length > 1 && ImGui.CalcTextSize(displayText + "...").X > maxTextW)
                        displayText = displayText[..^1];
                    displayText += "...";
                }

                uint tbTextCol = ImGui.ColorConvertFloat4ToU32(isPlaceholder
                    ? new Vector4(0.5f, 0.5f, 0.5f, 0.7f * elemOpacity)
                    : new Vector4(0.85f, 0.85f, 0.95f, 1f * elemOpacity));
                float tbTextX = inputX + 6f;
                float tbTextY = inputY + (inputH - tbTextSize.Y) * 0.5f;
                drawList.AddText(new Vector2(tbTextX, tbTextY), tbTextCol, displayText);

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

            // ── Click handling ──
            // Preview mode: trigger behavior; Editor mode: select element
            // blockedByOverlay already computed above — blocks clicks on elements behind an overlay
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
                    // ── Checkbox: ALWAYS toggle first (primary action), then run OnClick if present ──
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
                    // ── Default interactive element behaviors (fallback when no custom handler) ──
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


            // ── Always recurse for children (so they render regardless of click state) ──
            if (elem.Children.Count > 0)
                DrawEditorUIPreview(drawList, elem.Children, mouseScreen, leftClicked, isPreview, isMouseDown, focusedElement, keyboardActivate);
        }
    }

    // ── Preview texture cache for viewport editor ──
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
        if (_previewTextureCache.TryGetValue(path, out uint cached))
            return cached;

        if (!File.Exists(path))
            return 0;

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
        catch
        {
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
                if (!string.IsNullOrEmpty(param) && _bridge.EditorScenes.TryGetValue(param, out var targetScene))
                {
                    Console.WriteLine($"[Viewport] scene:{param} → switching to editor scene");
                    // Switch the editor scene root to the target scene
                    _bridge.SelectedEditorScene = param;
                    _bridge.SceneRoot = targetScene.Root;
                    _bridge.SceneRootElements = new List<UIElement> { targetScene.Root }.AsReadOnly();
                    // Reset overlay visibility for the new scene
                    ResetSceneOverlays();
                }
                else if (!string.IsNullOrEmpty(param))
                {
                    Console.WriteLine($"[Viewport] scene:{param} → scene not found in editor scenes");
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
        // ── In-game mode (F8 fullscreen): close the app entirely ──
        if (_fullscreenMode)
        {
            Console.WriteLine("[Viewport] exit → closing app (in-game mode, via GLFW)");
            nint window = Glfw.GetWindow();
            if (window != nint.Zero)
                Glfw.SetWindowShouldClose(window, 1);
        }
        // ── Viewport preview mode (F5): back to editor ──
        else if (_previewMode)
        {
            Console.WriteLine("[Viewport] exit → exiting preview mode, resetting overlays");
            _previewMode = false;
            ResetSceneOverlays();
        }
        // ── In-game input mode (F9 active): back to editor ──
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
        // ── Otherwise: close the app ──
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

        // Dark overlay behind the dialog — absolute position (full screen)
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

        // Title label — use ABSOLUTE coordinates
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

        // Message label — use ABSOLUTE coordinates
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

        // Cancel button (left) — use ABSOLUTE coordinates
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

        // Yes, Exit button (right) — use ABSOLUTE coordinates
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

    // ── Public API for main menu bar integration ──
    /// <summary>Whether preview mode is active (hides editor helpers, shows scene as-in-game).</summary>
    public bool PreviewMode
    {
        get => _previewMode;
        set
        {
            if (value && !_previewMode)
                ResetSceneOverlays();
            _previewMode = value;
        }
    }
    /// <summary>Whether snap-to-grid is enabled.</summary>
    public bool SnapEnabled { get => _snapEnabled; set => _snapEnabled = value; }

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

    // ──────────────────────────────────────────────
    //  ImGui-based background gradient + spinning triangle
    // ──────────────────────────────────────────────

    /// <summary>Draw animated gradient background using ImGui draw list.
    /// Renders only when no scene texture is present (dark canvas mode).</summary>
    private void RenderImGuiGradient(ImDrawListPtr drawList, Vector2 min, Vector2 max)
    {
        float w = max.X - min.X;
        float h = max.Y - min.Y;
        if (w < 1f || h < 1f) return;

        // Animated gradient: 20 horizontal strips with pulsing colors
        int gradSteps = 20;
        float stepH = h / gradSteps;
        float pulse = MathF.Sin(_bgTotalTime * 0.3f) * 0.015f;

        for (int i = 0; i < gradSteps; i++)
        {
            float t = (float)i / gradSteps;
            float r = 0.06f + t * 0.03f + pulse * 0.5f;
            float g = 0.06f + t * 0.02f + pulse * 0.3f;
            float b = 0.10f + t * 0.04f + pulse;
            float y0 = min.Y + stepH * i;
            float y1 = y0 + stepH + 1f;
            drawList.AddRectFilled(
                new Vector2(min.X, y0),
                new Vector2(max.X, y1),
                ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, 1f)));
        }
    }

    /// <summary>Draw the spinning triangle using ImGui draw list primitives.
    /// Computes vertex rotation on CPU, draws colored edges + glowing vertices.</summary>
    private void RenderImGuiTriangle(ImDrawListPtr drawList, Vector2 min, Vector2 max)
    {
        float dt = ImGui.GetIO().DeltaTime;
        _triRotation += dt * 1.5f;
        if (_triRotation > MathF.PI * 2f) _triRotation -= MathF.PI * 2f;

        float w = max.X - min.X;
        float h = max.Y - min.Y;
        if (w < 1f || h < 1f) return;

        // Center of canvas
        float cx = (min.X + max.X) * 0.5f;
        float cy = (min.Y + max.Y) * 0.5f;
        float scale = Math.Min(w, h) * 0.30f;

        // Triangle vertices in local coords (centered at origin)
        float cosA = MathF.Cos(_triRotation);
        float sinA = MathF.Sin(_triRotation);

        // Three vertices: top, bottom-left, bottom-right (in local -1 to 1 space)
        float[] lx = [0f, -0.5f, 0.5f];
        float[] ly = [0.5f, -0.5f, -0.5f];
        Vector3[] colors = [
            new(1f, 0.2f, 0.2f),
            new(0.2f, 1f, 0.2f),
            new(0.2f, 0.2f, 1f),
        ];

        // Compute screen positions
        var sx = new Vector2[3];
        for (int i = 0; i < 3; i++)
        {
            float rx = lx[i] * cosA - ly[i] * sinA;
            float ry = lx[i] * sinA + ly[i] * cosA;
            sx[i] = new(cx + rx * scale, cy + ry * scale);
        }

        // ── Semi-transparent triangle body ──
        uint bodyCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 1f, 0.10f));
        drawList.AddTriangleFilled(sx[0], sx[1], sx[2], bodyCol);

        // ── Colored edges + vertex glow ──
        for (int i = 0; i < 3; i++)
        {
            int next = (i + 1) % 3;
            var colV = colors[i];

            // Line from vertex i to next vertex
            uint edgeCol = ImGui.ColorConvertFloat4ToU32(new Vector4(colV.X, colV.Y, colV.Z, 0.85f));
            drawList.AddLine(sx[i], sx[next], edgeCol, 2.5f);

            // Outer glow
            uint glowCol = ImGui.ColorConvertFloat4ToU32(new Vector4(colV.X, colV.Y, colV.Z, 0.25f));
            drawList.AddCircleFilled(sx[i], 10f, glowCol, 16);

            // Inner bright dot
            uint dotCol = ImGui.ColorConvertFloat4ToU32(new Vector4(
                Math.Clamp(colV.X * 1.3f, 0f, 1f),
                Math.Clamp(colV.Y * 1.3f, 0f, 1f),
                Math.Clamp(colV.Z * 1.3f, 0f, 1f),
                1f));
            drawList.AddCircleFilled(sx[i], 4f, dotCol, 12);
        }
    }



    public void Render()
    {
        // In fullscreen mode (In-Game Mode F8), always render regardless of _visible
        if (!_fullscreenMode)
        {
            if (!_visible) return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

        // ── Fullscreen mode: add NoTitleBar|NoResize flags ──
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

        if (!_fullscreenMode)
        {
            // Cache viewport window position for tooltip positioning (top-left)
            var viewportTopLeft = ImGui.GetWindowPos();

            // ── Snap-to-grid toggle + grid size selector (skipped in fullscreen) ──
            {
                // ── Preview mode toggle ──
                bool previewNow = _previewMode;
                ImGui.PushStyleColor(ImGuiCol.Button, previewNow
                    ? new Vector4(0.15f, 0.55f, 0.25f, 1f)    // green = preview ON
                    : new Vector4(0.35f, 0.35f, 0.35f, 1f)); // grey = editor
                if (ImGui.Button(previewNow ? "▶ Preview" : "◼ Edit"))
                {
                    if (!previewNow)
                        ResetSceneOverlays();
                    _previewMode = !_previewMode;
                }
                ImGui.PopStyleColor(1);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(_previewMode
                        ? "Preview mode: hides editor helpers — shows scene as in-game"
                        : "Edit mode: shows wireframes, handles, and info labels");
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();

                ImGui.Checkbox("Snap", ref _snapEnabled);
                ImGui.SameLine();

                string gridLabel = _snapEnabled ? $"{_snapGridSize:F0}px" : "—";
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
                        $"{selReadout.GetIcon()} ({selReadout.X:F0},{selReadout.Y:F0}) [{selReadout.Width:F0}×{selReadout.Height:F0}] S:{selReadout.FontSize:F0}");
                }

                // ── Model Editor Gizmo Mode Buttons ──
                if (_bridge.EditorObjectManager != null)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled("|");
                    ImGui.SameLine();

                    int gizmoMode = _bridge.GizmoMode;
                    string[] gizmoLabels = ["W: Move", "E: Rotate", "R: Scale"];
                    for (int gi = 0; gi < 3; gi++)
                    {
ImGui.PushStyleColor(ImGuiCol.Button, gizmoMode == gi
                            ? new Vector4(0.25f, 0.50f, 0.80f, 1f)
                            : new Vector4(0.25f, 0.25f, 0.30f, 1f));
                        if (ImGui.Button(gizmoLabels[gi]))
                        {
                            _bridge.GizmoMode = gi;
                            _gizmo.Mode = (TransformGizmo.GizmoMode)gi;
                        }
                        ImGui.PopStyleColor(1);
                        if (gi < 2) ImGui.SameLine();
                    }

                    // Primitive creation buttons
                    ImGui.SameLine();
                    ImGui.TextDisabled("|");
                    ImGui.SameLine();
if (ImGui.Button("+Box"))
                    {
                        var cam = _bridge.Camera;
                        var pos = cam != null ? cam.Position + cam.Front * 5f : new Vector3(0f, 1f, -5f);
                        var obj = _bridge.EditorObjectManager.AddPrimitive(EditorPrimitiveType.Box, pos);
                        if (obj != null) _bridge.SelectedEditorObject = obj;
                    }
                    ImGui.SameLine();
if (ImGui.Button("+Sphere"))
                    {
                        var cam = _bridge.Camera;
                        var pos = cam != null ? cam.Position + cam.Front * 5f : new Vector3(0f, 1f, -5f);
                        var obj = _bridge.EditorObjectManager.AddPrimitive(EditorPrimitiveType.Sphere, pos);
                        if (obj != null) _bridge.SelectedEditorObject = obj;
                    }
ImGui.SameLine();
                    if (ImGui.Button("+Plane"))
                    {
                        var cam = _bridge.Camera;
                        var pos = cam != null ? cam.Position + cam.Front * 5f : new Vector3(0f, 1f, -5f);
                        var obj = _bridge.EditorObjectManager.AddPrimitive(EditorPrimitiveType.Plane, pos);
                        if (obj != null) _bridge.SelectedEditorObject = obj;
                    }
                }
            } // end toolbar block
        } // end if (!_fullscreenMode)

        var avail = ImGui.GetContentRegionAvail();
        bool hasSceneTexture = avail.X > 0 && avail.Y > 0 && _bridge.SceneTextureID != 0;
        bool hasGameScene = _bridge.SceneManager?.CurrentScene != null;

        if (hasSceneTexture || _bridge.SceneRoot != null)
        {
            // ── Canvas area (fill available space) ──
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
                // No game scene — draw dark animated canvas for UI editing
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

            // ── Draw spinning triangle via ImGui (on top of scene/background) ──
            {
                var drawList = ImGui.GetWindowDrawList();
                RenderImGuiTriangle(drawList, _imageMin, _imageMax);
            }

            var viewportMouseScreen = ImGui.GetMousePos();

            // ── Cache mouse state ONCE before any click handling ──
            // (DrawEditorUIPreview calls IsMouseClicked for each element; caching
            //  here ensures the wireframe section gets the same reliable value)
            bool cachedLeftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            bool cachedLeftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
            bool cachedLeftReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

            // ── Detect editor scene switch → clear preview texture cache ──
            if (!hasGameScene && _bridge.SceneRoot != null)
            {
                if (_bridge.SceneRoot != _lastSceneRoot)
                {
                    if (_previewTextureCache.Count > 0)
                    {
                        int cleared = _previewTextureCache.Count;
                        ClearPreviewTextures();
                        Console.WriteLine($"[Viewport] Scene root changed — cleared {cleared} preview textures");
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

            // ── Live Editor Scene Preview (draws UI elements on top of the scene texture or dark canvas) ──
            // Uses SceneRoot directly (not SelectedEditorScene) so the preview works even when
            // no editor scene is explicitly selected — the active game scene's root is sufficient.
            // ── Render UI elements in both editor and preview mode ──
            // In editor mode: elements are rendered with click-to-select behavior.
            // In preview mode: elements are rendered with click-to-interact behavior (game-like).
            if (_bridge.SceneRoot != null && _bridge.SceneRoot.Children.Count > 0)
            {
                var drawList = ImGui.GetWindowDrawList();
                DrawEditorUIPreview(drawList, _bridge.SceneRoot.Children, viewportMouseScreen, cachedLeftClicked, isPreview: _previewMode, isMouseDown: cachedLeftDown);
            }

            // ── Preview mode indicator badge (bottom-right corner) ──
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

                // ── Clickable invisible button over badge → exit preview mode ──
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
                    ResetSceneOverlays();
                    Console.WriteLine("[Viewport] Exited preview mode via badge click");
                }
            } // end if (_previewMode) badge block

            // ── UI Element Wireframe & Interactive Editing ──
            // In Preview mode, skip ALL editor overlays (wireframe, handles, info labels, drag)
            // For Loading and GameScene editor types, skip helpers (only MainMenu needs UI layout editing)
            if (!_previewMode)
            {
            bool showHelpers = IsEditorSceneType(IDEBridge.SceneType.MainMenu);

            if (showHelpers)
            {
            var selUiElem = _bridge.SelectedUIElement;
            var allSelected = _bridge.SelectedUIElements;

            if (selUiElem != null)
            {
                var drawList = ImGui.GetWindowDrawList();
                float pulse = 0.6f + 0.4f * MathF.Sin((float)ImGui.GetTime() * 3f);

                // ── Helper: draw a wireframe for a single element ──
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

                // ── Scene-type elements: NO wireframe (just skip the wireframe draw) ──
                bool isSceneElem = selUiElem.Type == UIElementType.Scene;

                if (!isSceneElem)
                    DrawElemWireframe(selUiElem, true);

                // ── Scene-type elements: NO resize/move handlers ──
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
                // ── Interactive drag handling ──
                float psx0 = _imageMin.X + (selUiElem.X / _texW) * _imageSize.X;
                float psy0 = _imageMin.Y + (selUiElem.Y / _texH) * _imageSize.Y;
                float psx1 = _imageMin.X + ((selUiElem.X + selUiElem.Width) / _texW) * _imageSize.X;
                float psy1 = _imageMin.Y + ((selUiElem.Y + selUiElem.Height) / _texH) * _imageSize.Y;
                float pcsx0 = Math.Clamp(psx0, _imageMin.X, _imageMax.X);
                float pcsy0 = Math.Clamp(psy0, _imageMin.Y, _imageMax.Y);
                float pcsx1 = Math.Clamp(psx1, _imageMin.X, _imageMax.X);
                float pcsy1 = Math.Clamp(psy1, _imageMin.Y, _imageMax.Y);
                bool primaryFullyVisible = pcsx0 == psx0 && pcsy0 == psy0 && pcsx1 == psx1 && pcsy1 == psy1;

                // ── Corner detection radius: proportional to element screen size ──
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

                // ── Top-center move handle detection ──
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

                // ── Post-apply safety: reset if mouse is neither down nor being released ──
                if (_dragMode != DragMode.None && !cachedLeftDown && !cachedLeftReleased)
                {
                    _dragMode = DragMode.None;
                }
                } // end if (!isSceneElem)
                } // end if (selUiElem != null)
                } // end if (showHelpers)
            } // end if (!_previewMode)

            // ── Safety reset: handle interrupted drag even when selUiElem became null ──
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

            // ── Drag-drop target: Asset Browser image → selected element ──
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

            // ── Viewport click/hover detection ──
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

                if (hasSceneTexture && ImGui.IsItemClicked() && _dragMode == DragMode.None)
                {
                    _bridge.IsViewportClicked = true;
                    _bridge.ViewportClickX = sceneU * _bridge.SceneTextureWidth;
                    _bridge.ViewportClickY = sceneV * _bridge.SceneTextureHeight;
                }
                else
                {
                    _bridge.IsViewportClicked = false;
                }
            }
            else
            {
                _bridge.ViewportMouseX = -1;
                _bridge.ViewportMouseY = -1;
                _bridge.IsViewportClicked = false;
            }
        }
        else
        {
            // No texture and no editor scene — show placeholder
            var center = ImGui.GetCursorScreenPos() + avail * 0.5f;
            var textSize = ImGui.CalcTextSize("No Scene");
            ImGui.GetWindowDrawList().AddText(
                center - textSize * 0.5f,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.4f, 1f)),
                "No Scene");
        }

        ImGui.End();
    }
}
