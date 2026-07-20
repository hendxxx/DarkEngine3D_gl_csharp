using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;
using System.IO;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Viewport panel — displays the game's rendered scene texture inside an ImGui panel.
/// Supports aspect-ratio-correct scaling and click-to-select.
/// Includes interactive UI element editing: drag to move, resize from corners.
/// </summary>
public class ViewportPanel
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

    // ── Drag state for UI element editing ──
    private enum DragMode { None, Move, ResizeTL, ResizeTR, ResizeBL, ResizeBR }
    private DragMode _dragMode = DragMode.None;
    // Starting state when drag began (scene coords)
    private float _dragStartX, _dragStartY, _dragStartW, _dragStartH;
    private Vector2 _dragStartMouseScene; // mouse position in scene coords when drag started

    // ── Cached conversion data (set each frame in overlay) ──
    private Vector2 _imageMin, _imageMax, _imageSize;
    private float _texW = 1f, _texH = 1f;

    // ── Diagnostic file logging (TEMPORARY for debugging) ──
    private static StreamWriter? _debugWriter;
    private static bool _debugWriterReady = false;

    private static void WriteDebugLog(string message)
    {
        try
        {
            if (!_debugWriterReady)
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "viewport_debug.txt");
                _debugWriter = new StreamWriter(path, append: false) { AutoFlush = true };
                _debugWriter.WriteLine("— Viewport Debug Log —");
                _debugWriterReady = true;
            }
            _debugWriter?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
        }
        catch
        {
            // Silently ignore file write failures in debug code
        }
    }

    /// <summary>Convert ImGui screen coordinates to scene pixel coordinates.</summary>
    private Vector2 ScreenToScene(Vector2 screenPos)
    {
        float relX = screenPos.X - _imageMin.X;
        float relY = screenPos.Y - _imageMin.Y;
        float u = relX / _imageSize.X;
        float v = 1f - (relY / _imageSize.Y); // UV flip
        return new Vector2(u * _texW, v * _texH);
    }

    /// <summary>Convert scene pixel coordinates to ImGui screen coordinates.</summary>
    private Vector2 SceneToScreen(float sceneX, float sceneY)
    {
        float u = sceneX / _texW;
        float v = 1f - (sceneY / _texH);
        return new Vector2(_imageMin.X + u * _imageSize.X, _imageMin.Y + v * _imageSize.Y);
    }

    /// <summary>Draw a live preview of editor scene UI elements using ImGui draw list.
    /// Renders backgrounds, borders, text with hover effects — click to select.</summary>
    private void DrawEditorUIPreview(ImDrawListPtr drawList, IReadOnlyList<UIElement> elements, Vector2 mouseScreen, bool leftClicked)
    {
        for (int ei = 0; ei < elements.Count; ei++)
        {
            var elem = elements[ei];
            if (!elem.IsVisible) continue;

            // Convert scene coords to screen coords (with UV flip for Y)
            float sx0 = _imageMin.X + (elem.X / _texW) * _imageSize.X;
            float sy0 = _imageMin.Y + (1f - (elem.Y / _texH)) * _imageSize.Y;
            float sx1 = _imageMin.X + ((elem.X + elem.Width) / _texW) * _imageSize.X;
            float sy1 = _imageMin.Y + (1f - ((elem.Y + elem.Height) / _texH)) * _imageSize.Y;

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

            // Pick colors: hover or normal
            var bgColor = isHovered ? elem.HoverBgColor : elem.BgColor;
            var borderColor = isHovered ? elem.HoverBorderColor : elem.BorderColor;
            var textColor = isHovered ? elem.HoverTextColor : elem.TextColor;

            // Draw background (filled rect)
            drawList.AddRectFilled(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                ImGui.ColorConvertFloat4ToU32(new Vector4(bgColor.X, bgColor.Y, bgColor.Z, 0.85f)),
                4f); // rounded corners

            // Draw border
            drawList.AddRect(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                ImGui.ColorConvertFloat4ToU32(new Vector4(borderColor.X, borderColor.Y, borderColor.Z, 1f)),
                4f, ImDrawFlags.None, 1.5f);

            // Draw text label
            if (!string.IsNullOrEmpty(elem.Text))
            {
                string label = elem.Text;
                var labelSize = ImGui.CalcTextSize(label);

                // Position text based on alignment
                float textX, textY;
                float textPad = 8f;
                float availW = (csx1 - csx0) - textPad * 2f;
                float textW = Math.Min(labelSize.X, availW);

                switch (elem.Alignment)
                {
                    case TextAlignment.Left:
                        textX = csx0 + textPad;
                        break;
                    case TextAlignment.Right:
                        textX = csx1 - textPad - textW;
                        break;
                    default: // Center
                        textX = csx0 + (csx1 - csx0) * 0.5f - textW * 0.5f;
                        break;
                }

                textY = csy0 + (csy1 - csy0) * 0.5f - labelSize.Y * 0.5f;

                // Clamp text position
                textX = Math.Max(csx0 + 2f, Math.Min(textX, csx1 - textW - 2f));

                drawList.AddText(new Vector2(textX, textY),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(textColor.X, textColor.Y, textColor.Z, 1f)),
                    label);
            }

            // Click to select element in the preview — only take the LAST (top-most) element
            if (isHovered && leftClicked && _dragMode == DragMode.None)
            {
                _bridge.SelectedUIElements?.Clear();
                _bridge.SelectedUIElements?.Add(elem);
                _bridge.SelectedUIElement = elem;
                // DON'T set _dragMode — wireframe section handles drag initiation
            }

            // Recursively render children
            if (elem.Children.Count > 0)
                DrawEditorUIPreview(drawList, elem.Children, mouseScreen, leftClicked);
        }
    }

    public ViewportPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Viewport", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGui.Begin("Viewport", ref _visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();

        // Track whether the viewport is focused — used by GameScene to manage cursor visibility
        _bridge.IsViewportFocused = ImGui.IsWindowFocused();

        // ── Snap-to-grid toggle + grid size selector ──
        {
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
                    $"{selReadout.GetIcon()} ({selReadout.X:F0}, {selReadout.Y:F0}) [{selReadout.Width:F0}×{selReadout.Height:F0}]");
            }
        }

        var avail = ImGui.GetContentRegionAvail();
        bool hasSceneTexture = avail.X > 0 && avail.Y > 0 && _bridge.SceneTextureID != 0;
        bool hasGameScene = _bridge.SceneManager?.CurrentScene != null;

        if (hasSceneTexture || (!hasGameScene && _bridge.SelectedEditorScene != null))
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
                // No game scene — draw a dark canvas for UI editing
                ImGui.Dummy(new Vector2(canvasW, canvasH));
                var drawList = ImGui.GetWindowDrawList();
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                drawList.AddRectFilled(min, max,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.10f, 1f)));

                _imageMin = min;
                _imageMax = max;
                _imageSize = new Vector2(canvasW, canvasH);
                // Use a default canvas size of 1920×1080 for coordinate conversion
                _texW = 1920f;
                _texH = 1080f;
            }

            var viewportMouseScreen = ImGui.GetMousePos();

            // ── Cache mouse state ONCE before any click handling ──
            // (DrawEditorUIPreview calls IsMouseClicked for each element; caching
            //  here ensures the wireframe section gets the same reliable value)
            bool cachedLeftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            bool cachedLeftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
            bool cachedLeftReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

            // ── Live Editor Scene Preview ──
            if (!hasGameScene && _bridge.SelectedEditorScene != null && _bridge.SceneRoot != null)
            {
                var drawList = ImGui.GetWindowDrawList();
                DrawEditorUIPreview(drawList, _bridge.SceneRoot.Children, viewportMouseScreen, cachedLeftClicked);
            }

            // ── UI Element Wireframe & Interactive Editing ──
            var selUiElem = _bridge.SelectedUIElement;
            var allSelected = _bridge.SelectedUIElements;

            // ── DIAGNOSTIC: log element state every ~10 frames (even when not dragging) ──
            if (selUiElem != null && (ImGui.GetFrameCount() % 10 == 0))
            {
                // Log every 10 frames to detect position resets or object replacement
                int sceneHash = _bridge.SceneRoot?.GetHashCode() ?? 0;
                int editorSceneCount = _bridge.EditorScenes?.Count ?? 0;
                string editorSceneName = _bridge.SelectedEditorScene ?? "(null)";
                int elemHash = selUiElem.GetHashCode();
                string curScene = _bridge.SceneManager?.CurrentScene?.Name ?? "(null)";
                string sceneRootName = _bridge.SceneRoot?.Name ?? "(null)";
                WriteDebugLog($"ELEM_STATE: elem=#{elemHash:X8}[id={selUiElem.InstanceId}] pos=({selUiElem.X:F0},{selUiElem.Y:F0}) size=({selUiElem.Width:F0}×{selUiElem.Height:F0}) root=#{sceneHash:X8}('{sceneRootName}') editor='{editorSceneName}'({editorSceneCount}) curScene='{curScene}'");
            }

            if (selUiElem != null)
            {
                var drawList = ImGui.GetWindowDrawList();
                float pulse = 0.6f + 0.4f * MathF.Sin((float)ImGui.GetTime() * 3f);

                // ── Helper: draw a wireframe for a single element ──
                void DrawElemWireframe(UIElement elem, bool isPrimary)
                {
                    float sx0 = _imageMin.X + (elem.X / _texW) * _imageSize.X;
                    float sy0 = _imageMin.Y + (1f - (elem.Y / _texH)) * _imageSize.Y;
                    float sx1 = _imageMin.X + ((elem.X + elem.Width) / _texW) * _imageSize.X;
                    float sy1 = _imageMin.Y + (1f - ((elem.Y + elem.Height) / _texH)) * _imageSize.Y;

                    float csx0 = Math.Clamp(sx0, _imageMin.X, _imageMax.X);
                    float csy0 = Math.Clamp(sy0, _imageMin.Y, _imageMax.Y);
                    float csx1 = Math.Clamp(sx1, _imageMin.X, _imageMax.X);
                    float csy1 = Math.Clamp(sy1, _imageMin.Y, _imageMax.Y);
                    bool fullyVis = csx0 == sx0 && csy0 == sy0 && csx1 == sx1 && csy1 == sy1;

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
                        if (fullyVis)
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
                            // TL corner: ↘ arrow
                            drawList.AddLine(
                                new Vector2(sx0 + arrowInset, sy0 + arrowInset),
                                new Vector2(sx0 + handleHalf - arrowInset, sy0 + handleHalf - arrowInset),
                                arrowColor, 1.8f);
                            drawList.AddLine(
                                new Vector2(sx0 + handleHalf - arrowInset * 2f, sy0 + arrowInset),
                                new Vector2(sx0 + handleHalf - arrowInset, sy0 + handleHalf - arrowInset),
                                arrowColor, 1.8f);
                            // TR corner: ↙ arrow
                            drawList.AddLine(
                                new Vector2(sx1 - arrowInset, sy0 + arrowInset),
                                new Vector2(sx1 - handleHalf + arrowInset, sy0 + handleHalf - arrowInset),
                                arrowColor, 1.8f);
                            drawList.AddLine(
                                new Vector2(sx1 - handleHalf + arrowInset * 2f, sy0 + arrowInset),
                                new Vector2(sx1 - handleHalf + arrowInset, sy0 + handleHalf - arrowInset),
                                arrowColor, 1.8f);
                            // BL corner: ↗ arrow
                            drawList.AddLine(
                                new Vector2(sx0 + arrowInset, sy1 - arrowInset),
                                new Vector2(sx0 + handleHalf - arrowInset, sy1 - handleHalf + arrowInset),
                                arrowColor, 1.8f);
                            drawList.AddLine(
                                new Vector2(sx0 + handleHalf - arrowInset * 2f, sy1 - arrowInset),
                                new Vector2(sx0 + handleHalf - arrowInset, sy1 - handleHalf + arrowInset),
                                arrowColor, 1.8f);
                            // BR corner: ↖ arrow
                            drawList.AddLine(
                                new Vector2(sx1 - arrowInset, sy1 - arrowInset),
                                new Vector2(sx1 - handleHalf + arrowInset, sy1 - handleHalf + arrowInset),
                                arrowColor, 1.8f);
                            drawList.AddLine(
                                new Vector2(sx1 - handleHalf + arrowInset * 2f, sy1 - arrowInset),
                                new Vector2(sx1 - handleHalf + arrowInset, sy1 - handleHalf + arrowInset),
                                arrowColor, 1.8f);

                            // Mini bracket corners at each handle
                            float miniBracket = 4f;
                            uint miniBrColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.5f));
                            // Mini bracket around TL handle
                            drawList.AddLine(new Vector2(sx0 - miniBracket, sy0 - miniBracket), new Vector2(sx0 - miniBracket, sy0 + miniBracket), miniBrColor, 1f);
                            drawList.AddLine(new Vector2(sx0 - miniBracket, sy0 - miniBracket), new Vector2(sx0 + miniBracket, sy0 - miniBracket), miniBrColor, 1f);
                            // Mini bracket around TR handle
                            drawList.AddLine(new Vector2(sx1 + miniBracket, sy0 - miniBracket), new Vector2(sx1 + miniBracket, sy0 + miniBracket), miniBrColor, 1f);
                            drawList.AddLine(new Vector2(sx1 + miniBracket, sy0 - miniBracket), new Vector2(sx1 - miniBracket, sy0 - miniBracket), miniBrColor, 1f);
                            // Mini bracket around BL handle
                            drawList.AddLine(new Vector2(sx0 - miniBracket, sy1 + miniBracket), new Vector2(sx0 - miniBracket, sy1 - miniBracket), miniBrColor, 1f);
                            drawList.AddLine(new Vector2(sx0 - miniBracket, sy1 + miniBracket), new Vector2(sx0 + miniBracket, sy1 + miniBracket), miniBrColor, 1f);
                            // Mini bracket around BR handle
                            drawList.AddLine(new Vector2(sx1 + miniBracket, sy1 + miniBracket), new Vector2(sx1 + miniBracket, sy1 - miniBracket), miniBrColor, 1f);
                            drawList.AddLine(new Vector2(sx1 + miniBracket, sy1 + miniBracket), new Vector2(sx1 - miniBracket, sy1 + miniBracket), miniBrColor, 1f);
                        }

                        // ── Info label at top-left corner of element ──
                        {
                            string info = $"{elem.GetIcon()} {elem.Name}  [{elem.Width:F0}×{elem.Height:F0}] @ ({elem.X:F0}, {elem.Y:F0})";
                            var infoSize = ImGui.CalcTextSize(info);
                            float infoPad = 5f;
                            float ix = csx0 + 4f;
                            float iy = csy0 + 4f;
                            uint ibg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.1f, 0.15f, 0.85f));
                            uint iborder = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.8f, 1.0f, 0.5f));
                            uint itext = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.9f, 1.0f, 1f));
                            drawList.AddRectFilled(
                                new Vector2(ix - infoPad, iy - 2f),
                                new Vector2(ix + infoSize.X + infoPad, iy + infoSize.Y + 2f),
                                ibg, 3f);
                            drawList.AddRect(
                                new Vector2(ix - infoPad, iy - 2f),
                                new Vector2(ix + infoSize.X + infoPad, iy + infoSize.Y + 2f),
                                iborder, 3f, ImDrawFlags.None, 1f);
                            drawList.AddText(new Vector2(ix, iy), itext, info);
                        }

                        // ── Top-center move handle ──
                        if (fullyVis)
                        {
                            float mhx = (sx0 + sx1) * 0.5f;
                            float mhy = sy0; // top edge
                            float mHandleSz = 12f, mHandleHalf = mHandleSz * 0.5f;
                            // Glow
                            uint mGlow = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.8f, 1.0f, pulse * 0.25f));
                            drawList.AddCircleFilled(new Vector2(mhx, mhy - mHandleHalf), mHandleSz * 0.7f, mGlow, 12);
                            // Square extending upward from top edge
                            drawList.AddRectFilled(
                                new Vector2(mhx - mHandleHalf, mhy - mHandleSz),
                                new Vector2(mhx + mHandleHalf, mhy),
                                handleColor, 3f);
                            drawList.AddRect(
                                new Vector2(mhx - mHandleHalf, mhy - mHandleSz),
                                new Vector2(mhx + mHandleHalf, mhy),
                                handleBorder, 3f, ImDrawFlags.None, 2f);
                            // Cross icon (4 arms)
                            uint xColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.2f, 0.9f));
                            float xArm = mHandleHalf * 0.5f;
                            float xcx = mhx, xcy = mhy - mHandleHalf;
                            drawList.AddLine(new Vector2(xcx - xArm, xcy), new Vector2(xcx + xArm, xcy), xColor, 1.8f);
                            drawList.AddLine(new Vector2(xcx, xcy - xArm), new Vector2(xcx, xcy + xArm), xColor, 1.8f);
                        }
                    }
                }

                if (allSelected != null)
                {
                    foreach (var elem in allSelected)
                        if (elem != selUiElem)
                            DrawElemWireframe(elem, false);
                }

                DrawElemWireframe(selUiElem, true);

                // ── Interactive drag handling ──
                float psx0 = _imageMin.X + (selUiElem.X / _texW) * _imageSize.X;
                float psy0 = _imageMin.Y + (1f - (selUiElem.Y / _texH)) * _imageSize.Y;
                float psx1 = _imageMin.X + ((selUiElem.X + selUiElem.Width) / _texW) * _imageSize.X;
                float psy1 = _imageMin.Y + (1f - ((selUiElem.Y + selUiElem.Height) / _texH)) * _imageSize.Y;
                float pcsx0 = Math.Clamp(psx0, _imageMin.X, _imageMax.X);
                float pcsy0 = Math.Clamp(psy0, _imageMin.Y, _imageMax.Y);
                float pcsx1 = Math.Clamp(psx1, _imageMin.X, _imageMax.X);
                float pcsy1 = Math.Clamp(psy1, _imageMin.Y, _imageMax.Y);
                bool primaryFullyVisible = pcsx0 == psx0 && pcsy0 == psy0 && pcsx1 == psx1 && pcsy1 == psy1;

                // ── Corner detection radius: proportional to element screen size ──
                float elemScreenW = psx1 - psx0;
                float elemScreenH = psy1 - psy0;
                float cornerRadius = Math.Max(6f, Math.Min(10f, Math.Min(elemScreenW, elemScreenH) * 0.25f));

                // Normal drag end — always process even if selUiElem became null
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

                if (overMoveHandle && _dragMode == DragMode.None)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                    ImGui.BeginTooltip();
                    ImGui.Text("✥ Drag to move");
                    ImGui.EndTooltip();
                }
                else if (overTL || overBR)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNWSE);
                    if (_dragMode == DragMode.None)
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("↔ Drag corner to resize");
                        ImGui.EndTooltip();
                    }
                }
                else if (overTR || overBL)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNESW);
                    if (_dragMode == DragMode.None)
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("↕ Drag corner to resize");
                        ImGui.EndTooltip();
                    }
                }
                else if (overBody && _dragMode == DragMode.None)
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                    ImGui.BeginTooltip();
                    ImGui.Text("✥ Drag body to move");
                    ImGui.EndTooltip();
                }

                // Click handling: move handle > corner > body
                if (_dragMode == DragMode.None && cachedLeftClicked)
                {
                    if (overMoveHandle)
                    {
                        _dragMode = DragMode.Move;
                        WriteDebugLog($"DRAG START Move (handle) on '{selUiElem.Name}' at ({selUiElem.X:F1},{selUiElem.Y:F1})");
                    }
                    else
                    {
                        bool anyCorner = primaryFullyVisible && (overTL || overTR || overBL || overBR);
                        if (anyCorner)
                        {
                            if (overTL) { _dragMode = DragMode.ResizeTL; WriteDebugLog($"DRAG START ResizeTL on '{selUiElem.Name}'"); }
                            else if (overTR) { _dragMode = DragMode.ResizeTR; WriteDebugLog($"DRAG START ResizeTR on '{selUiElem.Name}'"); }
                            else if (overBL) { _dragMode = DragMode.ResizeBL; WriteDebugLog($"DRAG START ResizeBL on '{selUiElem.Name}'"); }
                            else { _dragMode = DragMode.ResizeBR; WriteDebugLog($"DRAG START ResizeBR on '{selUiElem.Name}'"); }
                        }
                        else if (overBody)
                        {
                            _dragMode = DragMode.Move;
                            WriteDebugLog($"DRAG START Move (body) on '{selUiElem.Name}' at ({selUiElem.X:F1},{selUiElem.Y:F1})");
                        }
                    }

                    if (_dragMode != DragMode.None)
                    {
                        _dragStartX = selUiElem.X; _dragStartY = selUiElem.Y;
                        _dragStartW = selUiElem.Width; _dragStartH = selUiElem.Height;
                        _dragStartMouseScene = ScreenToScene(viewportMouseScreen);
                        WriteDebugLog($"Drag start state: pos=({_dragStartX:F1},{_dragStartY:F1}) size=({_dragStartW:F1}×{_dragStartH:F1}) mouseScene=({_dragStartMouseScene.X:F1},{_dragStartMouseScene.Y:F1})");
                    }
                }

                // ── DIAGNOSTIC: log mouse state every frame during drag (to file) ──
                if (_dragMode != DragMode.None)
                    WriteDebugLog($"DRAG_FRAME: mode={_dragMode} clicked={cachedLeftClicked} down={cachedLeftDown} released={cachedLeftReleased} mouse=({viewportMouseScreen.X:F0},{viewportMouseScreen.Y:F0})");

                // Apply drag movement/resize while mouse is held
                // Note: using !cachedLeftReleased instead of cachedLeftDown for robustness,
                // because IsMouseDown may return false on some frames during fast drags.
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
                    WriteDebugLog($"DRAG_APPLY: mode={_dragMode} dx={dx:F1} dy={dy:F1} → pos=({newX:F1},{newY:F1}) size=({newW:F1}×{newH:F1})");
                }

                // ── Post-apply safety: reset if mouse is neither down nor being released ──
                // (This runs AFTER apply so apply always fires; prevents runaway drags)
                if (_dragMode != DragMode.None && !cachedLeftDown && !cachedLeftReleased)
                {
                    _dragMode = DragMode.None;
                    WriteDebugLog("Drag mode reset (post-apply)");
                }
            }

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

            // ── Viewport click/hover detection ──
            bool mouseOverImage = viewportMouseScreen.X >= _imageMin.X && viewportMouseScreen.X <= _imageMax.X &&
                                  viewportMouseScreen.Y >= _imageMin.Y && viewportMouseScreen.Y <= _imageMax.Y;

            if (mouseOverImage)
            {
                float relX = viewportMouseScreen.X - _imageMin.X;
                float relY = viewportMouseScreen.Y - _imageMin.Y;
                float sceneU = relX / _imageSize.X;
                float sceneV = 1f - (relY / _imageSize.Y);

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
