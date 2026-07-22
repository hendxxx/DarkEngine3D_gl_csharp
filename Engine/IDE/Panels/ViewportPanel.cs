using DarkEngine3D_gl_csharp.Engine.Libs;
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
    /// When isPreview=false: click selects element in the editor.</summary>
    private void DrawEditorUIPreview(ImDrawListPtr drawList, IReadOnlyList<UIElement> elements, Vector2 mouseScreen, bool leftClicked, bool isPreview = false)
    {
        for (int ei = 0; ei < elements.Count; ei++)
        {
            var elem = elements[ei];
            if (!elem.IsVisible) continue;

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

            // Pick colors: hover or normal
            var bgColor = isHovered ? elem.HoverBgColor : elem.BgColor;
            var borderColor = isHovered ? elem.HoverBorderColor : elem.BorderColor;

            // ── Draw background (filled rect) — always drawn first as backdrop ──
            drawList.AddRectFilled(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                ImGui.ColorConvertFloat4ToU32(new Vector4(bgColor.X, bgColor.Y, bgColor.Z, 0.85f)),
                4f);

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

                    // Overlay subtle hover tint
                    if (isHovered)
                    {
                        drawList.AddRectFilled(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.08f)));
                    }
                }
                else
                {
                    // Image not loaded — show placeholder
                    drawList.AddRectFilled(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.2f, 0.2f, 0.6f)));
                    drawList.AddText(new Vector2(csx0 + 4f, csy0 + 4f),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.6f, 0.3f, 1f)),
                        "[Missing]");
                }
            }

            // ── Draw text label with element's FontSize ──
            if (!string.IsNullOrEmpty(elem.Text))
            {
                string label = elem.Text;
                float previewFontSize = elem.FontSize > 0f ? Math.Max(8f, elem.FontSize) : 13f;
                var textColor = isHovered ? elem.HoverTextColor : elem.TextColor;

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
                    ImGui.ColorConvertFloat4ToU32(new Vector4(textColor.X, textColor.Y, textColor.Z, 1f)),
                    label);
            }

            // Draw border
            drawList.AddRect(new Vector2(csx0, csy0), new Vector2(csx1, csy1),
                ImGui.ColorConvertFloat4ToU32(new Vector4(borderColor.X, borderColor.Y, borderColor.Z, 1f)),
                4f, ImDrawFlags.None, 1.5f);

// Click: in preview mode, trigger behavior; in editor mode, select element
            if (isHovered && leftClicked && _dragMode == DragMode.None)
            {
                if (isPreview)
                {
                    // Game-like interaction: trigger element's behavior
                    // Try: OnClick delegate > BehaviorActionType > ClickBehaviorLabel (legacy fallback)
                    if (elem.OnClick != null)
                    {
                        try { elem.OnClick.Invoke(); }
                        catch (Exception ex) { Console.WriteLine($"[Viewport] OnClick error for '{elem.Name}': {ex.Message}"); }
                    }
                    else if (!string.IsNullOrEmpty(elem.BehaviorActionType))
                    {
                        HandlePreviewBehavior(elem);
                    }
                    else if (!string.IsNullOrEmpty(elem.ClickBehaviorLabel))
                    {
                        // Legacy fallback: map ClickBehaviorLabel to behavior action
                        string legacy = elem.ClickBehaviorLabel.ToLowerInvariant();
                        if (legacy == "cancel" || legacy == "closeoverlay")
                        {
                            HandleCloseOverlay(elem);
                        }
                        else if (legacy == "exit" || legacy == "exitgame")
                        {
                            if (_bridge.InGameActive && _bridge.SceneManager != null)
                            {
                                _bridge.InGameActive = false;
                                if (_bridge.SceneRoot != null)
                                    foreach (var c in _bridge.SceneRoot.Children) c.IsVisible = false;
                            }
                            else { _bridge.SceneManager?.Stop(); }
                        }
                        else if (legacy.StartsWith("overlay:"))
                        {
                            string target = legacy.Substring("overlay:".Length);
                            FindAndToggleDialog(_bridge.SceneRoot?.Children ?? [], target, true);
                        }
                        else if (legacy == "playgame")
                        {
                            if (_bridge.SceneRoot != null)
                                foreach (var c in _bridge.SceneRoot.Children) c

            // Recursively render children
            if (elem.Children.Count > 0)
                DrawEditorUIPreview(drawList, elem.Children, mouseScreen, leftClicked);
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

    /// <summary>Handle a UI element click in preview mode when OnClick is null.
    /// Supports 3 universal behavior formats:
    /// 1. "overlay:DialogName" → toggle overlay visibility (open/close)
    /// 2. "scene:SceneName" → switch to named scene
    /// 3. "exit" → in preview: back to edit mode; in game: close app</summary>
    private void HandlePreviewBehavior(UIElement elem)
    {
        if (string.IsNullOrEmpty(elem.ClickBehaviorLabel))
            return;

        var lower = elem.ClickBehaviorLabel.ToLowerInvariant();

        // ── 1. Overlay toggle: "overlay:DialogName" ──
        if (lower.StartsWith("overlay:"))
        {
            string overlayName = lower["overlay:".Length..];
            if (string.IsNullOrEmpty(overlayName))
                return;

            if (_bridge.SceneRoot != null)
            {
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

                // For "cancel" inside dialog: always close regardless of label
                // For normal overlay toggle: invert
                bool newVisible = (lower == "cancel" || lower == "canceleexit") ? false : !isCurrentlyVisible;

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
            return;
        }

        // ── 2. Goto scene: "scene:SceneName" ──
        if (lower.StartsWith("scene:"))
        {
            string sceneName = lower["scene:".Length..];
            Console.WriteLine($"[Viewport] scene:{sceneName} → switching scene");
            if (_bridge.SceneRoot != null)
            {
                foreach (var child in _bridge.SceneRoot.Children)
                    child.IsVisible = false;
            }
            _bridge.InGameActive = true;
            return;
        }

        // ── 3. Exit: "exit" ──
        if (lower == "exit")
        {
            if (_bridge.InGameActive && _bridge.SceneManager != null)
            {
                Console.WriteLine("[Viewport] exit → back to edit mode");
                _bridge.InGameActive = false;
                if (_bridge.SceneRoot != null)
                {
                    foreach (var child in _bridge.SceneRoot.Children)
                        child.IsVisible = false;
                }
            }
            else
            {
                Console.WriteLine("[Viewport] exit → stopping app");
                _bridge.SceneManager?.Stop();
            }
            return;
        }

        // ── Legacy compatibility: map old behavior names ──
        // These are still used by existing .ing files
        if (lower == "showexitconfirm" || lower == "confirmexit" || lower == "yes")
        {
            if (lower == "confirmexit" || lower == "yes")
            {
                // Exit action
                if (_bridge.InGameActive && _bridge.SceneManager != null)
                {
                    Console.WriteLine("[Viewport] confirmexit → back to edit mode");
                    _bridge.InGameActive = false;
                    if (_bridge.SceneRoot != null)
                    {
                        foreach (var child in _bridge.SceneRoot.Children)
                            child.IsVisible = false;
                    }
                }
                else
                {
                    Console.WriteLine("[Viewport] confirmexit → stopping app");
                    _bridge.SceneManager?.Stop();
                }
            }
            else // showexitconfirm
            {
                FindAndToggleDialog(_bridge.SceneRoot?.Children ?? [], "ExitConfirm", true);
            }
            return;
        }

        // Legacy cancel/close
        if (lower == "cancel" || lower == "canceleexit" || lower == "cancelsettings" || lower == "discardchanges" || lower == "keepediting")
        {
            if (_bridge.SceneRoot != null)
            {
                foreach (var child in _bridge.SceneRoot.Children)
                {
                    if (child.Type == UIElementType.Dialog || child.Type == UIElementType.Container)
                        child.IsVisible = false;
                }
            }
            return;
        }

        Console.WriteLine($"[Viewport] Unknown behavior: '{lower}'");
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
            if (child.Type == UIElementType.Dialog && child.Name == "ExitConfirm")
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
            Type = UIElementType.Dialog,
            Text = "",
            IsVisible = false,
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

    /// <summary>Recursively find a dialog element by name and set its visibility.
    /// Also syncs ALL children visibility to match the dialog.</summary>
    private static bool FindAndToggleDialog(List<UIElement> elements, string name, bool visible)
    {
        foreach (var child in elements)
        {
            if ((child.Type == UIElementType.Dialog || child.Type == UIElementType.Container)
                && child.Name == name)
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

    public void Render()
    {
        if (!_visible) return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGui.Begin("Viewport", ref _visible, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();

        // Track whether the viewport is focused — used by GameScene to manage cursor visibility
        _bridge.IsViewportFocused = ImGui.IsWindowFocused();

        // Cache viewport window position for tooltip positioning (top-left)
        var viewportTopLeft = ImGui.GetWindowPos();

        // ── Snap-to-grid toggle + grid size selector ──
        {
            // ── Preview mode toggle ──
            bool previewNow = _previewMode;
            ImGui.PushStyleColor(ImGuiCol.Button, previewNow
                ? new Vector4(0.15f, 0.55f, 0.25f, 1f)    // green = preview ON
                : new Vector4(0.35f, 0.35f, 0.35f, 1f)); // grey = editor
            if (ImGui.Button(previewNow ? "▶ Preview" : "◼ Edit"))
                _previewMode = !_previewMode;
            ImGui.PopStyleColor(1);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(_previewMode
                    ? "Preview mode: hides editor helpers — shows scene as in-game"
                    : "Edit mode: shows wireframes, handles, and info labels");
            }
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
        }

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
                DrawEditorUIPreview(drawList, _bridge.SceneRoot.Children, viewportMouseScreen, cachedLeftClicked, isPreview: _previewMode);
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
                    Console.WriteLine("[Viewport] Exited preview mode via badge click");
                }
            } // end if (_previewMode) badge block

            // ── UI Element Wireframe & Interactive Editing ──
            // In Preview mode, skip ALL editor overlays (wireframe, handles, info labels, drag)
            if (!_previewMode)
            {
            var selUiElem = _bridge.SelectedUIElement;
            var allSelected = _bridge.SelectedUIElements;

            // ── DIAGNOSTIC: log element state every ~10 frames (even when not dragging) ──
            if (selUiElem != null && (ImGui.GetFrameCount() % 10 == 0))
            {
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

                // ── Call DrawElemWireframe for the selected element ──
                DrawElemWireframe(selUiElem, true);

                // ── Scene-type elements: NO resize/move handlers ──
                bool isSceneElem = selUiElem.Type == UIElementType.Scene;
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
                            WriteDebugLog($"DRAG START Move (handle) on '{selUiElem.Name}' at ({selUiElem.X:F1},{selUiElem.Y:F1})");
                        }
                        else
                        {
                            bool anyCorner = primaryFullyVisible && (overTL || overTR || overBL || overBR);
                            if (anyCorner)
                            {
                                if (overTL) { _dragMode = DragMode.ResizeTL; dragStarted = true; WriteDebugLog($"DRAG START ResizeTL on '{selUiElem.Name}'"); }
                                else if (overTR) { _dragMode = DragMode.ResizeTR; dragStarted = true; WriteDebugLog($"DRAG START ResizeTR on '{selUiElem.Name}'"); }
                                else if (overBL) { _dragMode = DragMode.ResizeBL; dragStarted = true; WriteDebugLog($"DRAG START ResizeBL on '{selUiElem.Name}'"); }
                                else { _dragMode = DragMode.ResizeBR; dragStarted = true; WriteDebugLog($"DRAG START ResizeBR on '{selUiElem.Name}'"); }
                            }
                            else if (overBody)
                            {
                                _dragMode = DragMode.Move;
                                dragStarted = true;
                                WriteDebugLog($"DRAG START Move (body) on '{selUiElem.Name}' at ({selUiElem.X:F1},{selUiElem.Y:F1})");
                            }
                        }

                        if (dragStarted)
                        {
                            _dragStartX = selUiElem.X; _dragStartY = selUiElem.Y;
                            _dragStartW = selUiElem.Width; _dragStartH = selUiElem.Height;
                            _dragStartMouseScene = ScreenToScene(viewportMouseScreen);
                            WriteDebugLog($"Drag start state: pos=({_dragStartX:F1},{_dragStartY:F1}) size=({_dragStartW:F1}×{_dragStartH:F1}) mouseScene=({_dragStartMouseScene.X:F1},{_dragStartMouseScene.Y:F1})");
                        }
                    }
                }

                // ── DIAGNOSTIC: log mouse state every frame during drag (to file) ──
                if (_dragMode != DragMode.None)
                    WriteDebugLog($"DRAG_FRAME: mode={_dragMode} clicked={cachedLeftClicked} down={cachedLeftDown} released={cachedLeftReleased} mouse=({viewportMouseScreen.X:F0},{viewportMouseScreen.Y:F0})");

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
                    WriteDebugLog($"DRAG_APPLY: mode={_dragMode} dx={dx:F1} dy={dy:F1} → pos=({newX:F1},{newY:F1}) size=({newW:F1}×{newH:F1})");
                }

                // ── Post-apply safety: reset if mouse is neither down nor being released ──
                if (_dragMode != DragMode.None && !cachedLeftDown && !cachedLeftReleased)
                {
                    _dragMode = DragMode.None;
                    WriteDebugLog("Drag mode reset (post-apply)");
                }
                } // end if (!isSceneElem)
                } // end if (selUiElem != null)
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
