using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Scene;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Inspector panel — shows properties of the selected object or UI button.
/// Supports editing transform, viewing health, editing button properties, etc.
/// </summary>
public class InspectorPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    // ── UI editing state ──
    private System.Numerics.Vector2 _editVec2 = new();

    // ── Cached font list (scanned once) ──
    private string[]? _availableFonts;
    private bool _fontsScanned = false;

    // ── Element type labels (mirrors UIElementType order) ──
    private static readonly string[] ElementTypeNames =
        ["Scene", "Container", "Button", "Label"];

    public InspectorPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Inspector", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;

        ImGui.Begin("Inspector", ref _visible);

        var uiElem = _bridge.SelectedUIElement;
        var obj = _bridge.SelectedObject;
        var agent = _bridge.SelectedAgent;

        if (uiElem != null)
        {
            RenderUIElementInspector(uiElem);
        }
        else if (obj != null)
        {
            RenderObjectInspector(obj, agent);
        }
        else
        {
            // Check if an editor scene is selected — show scene info even without SelectedUIElement
            var selectedScene = _bridge.SelectedEditorScene;
            if (selectedScene != null && _bridge.EditorScenes.TryGetValue(selectedScene, out var editorScene))
            {
                RenderEditorSceneInfo(editorScene);
            }
            else
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No object selected");
                ImGui.TextDisabled("Click an element in the viewport or hierarchy");
            }
        }

        ImGui.End();
    }

    private unsafe void RenderUIElementInspector(UIElement elem)
    {
        bool isSceneType = elem.Type == UIElementType.Scene;

        // ════════════════════════════════════════════
        //  Element Identity
        // ════════════════════════════════════════════
        if (ImGui.CollapsingHeader("Element", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Editable name
            string name = elem.Name;
            if (ImGui.InputText("Name", ref name, 256))
                elem.Name = name;

            // Type selector
            int typeIdx = (int)elem.Type;
            if (ImGui.Combo("Type", ref typeIdx, ElementTypeNames, ElementTypeNames.Length))
                elem.Type = (UIElementType)typeIdx;

            if (!isSceneType)
            {
                ImGui.Text($"Children: {elem.Children.Count}");
            }
            ImGui.Separator();
        }

        // ── For Scene type, ONLY show Element + Children, skip everything else ──
        if (isSceneType)
        {
            // Children list (only section shown for Scene type)
            if (elem.Children.Count > 0 && ImGui.CollapsingHeader($"Children ({elem.Children.Count})", ImGuiTreeNodeFlags.DefaultOpen))
            {
                for (int i = 0; i < elem.Children.Count; i++)
                {
                    var child = elem.Children[i];
                    ImGui.BulletText($"{child.GetIcon()} {child.Name}");
                    if (ImGui.IsItemClicked())
                        _bridge.SelectedUIElement = child;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip($"Type: {child.Type} | Click to select");
                }
            }
            return; // Scene type: nothing else to show
        }

        // ════════════════════════════════════════════
        //  Image (replaces Text & Font when ImagePath is set)
        // ════════════════════════════════════════════
        bool hasImage = !string.IsNullOrEmpty(elem.ImagePath);
        if (ImGui.CollapsingHeader("Image", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Image path with drag-drop target
            string imgPath = elem.ImagePath;
            ImGui.Text("Image Path:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##img_path", ref imgPath, 512))
                elem.ImagePath = imgPath;

            // ── Drag-drop target for Asset Browser ──
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                {
                    elem.ImagePath = AssetBrowserPanel._dragImagePath;
                    Console.WriteLine($"[Inspector] Set ImagePath on '{elem.Name}' → {elem.ImagePath}");
                    AssetBrowserPanel._dragImagePath = null;
                }
                ImGui.EndDragDropTarget();
            }

            // Clear image button
            ImGui.SameLine();
            if (ImGui.Button("X", new Vector2(24, 0)) && hasImage)
            {
                elem.ImagePath = "";
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Clear image");

            // ── Image sizing mode ──
            string[] imgModes = ["Stretch", "Zoom", "Fill"];
            int imgModeIdx = (int)elem.ImageMode;
            if (ImGui.Combo("Image Mode", ref imgModeIdx, imgModes, imgModes.Length))
                elem.ImageMode = (ImageMode)imgModeIdx;

            // Image preview indicator
            if (hasImage)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.5f, 1f), $"✓ Image: {Path.GetFileName(elem.ImagePath)}");
                ImGui.TextDisabled($"Mode: {elem.ImageMode}");
            }
            else
            {
                ImGui.TextDisabled("Drop image from Asset Browser");
                ImGui.TextDisabled("or type path above.");
            }

            ImGui.Separator();

            // ── Fit to Window button ──
            if (ImGui.Button("⬜ Fit to Window", new Vector2(-1, 30)))
            {
                elem.X = 0;
                elem.Y = 0;
                elem.Width = 1920;
                elem.Height = 1080;
                Console.WriteLine($"[Inspector] Fit to Window: '{elem.Name}' → (0,0) [1920×1080]");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Resize element to fill the entire window (1920×1080)");

            // ── Label (text shown when image can't be loaded) ──
            ImGui.Spacing();
            string labelText = elem.Text;
            ImGui.Text("Fallback Label:");
            if (ImGui.InputText("##fallback_label", ref labelText, 256))
                elem.Text = labelText;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Displayed when image fails to load");

            // Alignment
            string[] alignItems = ["Left", "Center", "Right"];
            int alignIdx = (int)elem.Alignment;
            if (ImGui.Combo("Alignment", ref alignIdx, alignItems, alignItems.Length))
                elem.Alignment = (TextAlignment)alignIdx;
        }

        // ════════════════════════════════════════════
        //  Text & Font (shown only when no image)
        // ════════════════════════════════════════════
        if (!hasImage && ImGui.CollapsingHeader("Text & Font", ImGuiTreeNodeFlags.DefaultOpen))
        {
            string text = elem.Text;
            if (ImGui.InputText("Label", ref text, 256))
                elem.Text = text;

            // Font size slider with reset
            float fontSize = elem.FontSize;
            if (ImGui.SliderFloat("Font Size", ref fontSize, 8f, 72f, "%.0f"))
                elem.FontSize = fontSize;

            // Font picker (combo from scanned fonts)
            ScanFontsOnce();
            if (_availableFonts != null && _availableFonts.Length > 0)
            {
                int fontIdx = 0;
                string currentFontName = Path.GetFileName(elem.FontPath);
                for (int i = 0; i < _availableFonts.Length; i++)
                {
                    if (string.Equals(_availableFonts[i], currentFontName, StringComparison.OrdinalIgnoreCase))
                    { fontIdx = i; break; }
                }

                if (ImGui.Combo("Font", ref fontIdx, _availableFonts, _availableFonts.Length))
                {
                    string fontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Artifacts", "fonts");
                    elem.FontPath = Path.Combine(fontsDir, _availableFonts[fontIdx]);
                }
            }
            else
            {
                string font = elem.FontPath;
                ImGui.InputText("Font Path", ref font, 256);
                if (font != elem.FontPath)
                    elem.FontPath = font;
            }

            // Alignment
            string[] alignItems = ["Left", "Center", "Right"];
            int alignIdx = (int)elem.Alignment;
            if (ImGui.Combo("Alignment", ref alignIdx, alignItems, alignItems.Length))
                elem.Alignment = (TextAlignment)alignIdx;
        }

        // ════════════════════════════════════════════
        //  Transform (Position & Size)
        // ════════════════════════════════════════════
        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            _editVec2 = new System.Numerics.Vector2(elem.X, elem.Y);
            if (ImGui.DragFloat2("Position (X, Y)", ref _editVec2, 1f))
            {
                elem.X = _editVec2.X;
                elem.Y = _editVec2.Y;
            }

            _editVec2 = new System.Numerics.Vector2(elem.Width, elem.Height);
            if (ImGui.DragFloat2("Size (W, H)", ref _editVec2, 1f, 10f, 2000f))
            {
                elem.Width = _editVec2.X;
                elem.Height = _editVec2.Y;
            }
        }

        // ════════════════════════════════════════════
        //  Colors (Normal + Hover)
        // ════════════════════════════════════════════
        if (ImGui.CollapsingHeader("Colors", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // ── Normal state ──
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1f), "Normal");
            ImGui.Indent();

            DrawColorPicker("Text", "N", elem.TextColor, c => RecordColorUndo(elem, "TextColor", elem.TextColor, c),
                defaultColor: new(0.95f, 0.95f, 1f));
            DrawColorPicker("Background", "N", elem.BgColor, c => RecordColorUndo(elem, "BgColor", elem.BgColor, c),
                defaultColor: new(0.10f, 0.12f, 0.18f));
            // Border default: buttons use (0.15,0.18,0.25), overlays/labels match BgColor
            Vector3 borderDefN = (elem.Type == UIElementType.Container || elem.Type == UIElementType.Dialog || elem.Type == UIElementType.Label)
                ? elem.BgColor : new Vector3(0.15f, 0.18f, 0.25f);
            DrawColorPicker("Border", "N", elem.BorderColor, c => RecordColorUndo(elem, "BorderColor", elem.BorderColor, c),
                defaultColor: borderDefN);

            ImGui.Unindent();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // ── Use Hover toggle ──
            bool useHover = elem.UseHover;
            if (ImGui.Checkbox("Use Hover", ref useHover))
                elem.UseHover = useHover;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When enabled, this element shows different colors on mouse hover. When disabled, normal colors are always used.");

            // ── Hover state (greyed out when Use Hover is disabled) ──
            ImGui.BeginDisabled(!elem.UseHover);
            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.7f, 1f), "Hover");
            ImGui.Indent();

            DrawColorPicker("Text", "H", elem.HoverTextColor, c => RecordColorUndo(elem, "HoverTextColor", elem.HoverTextColor, c),
                defaultColor: new(0.95f, 0.95f, 1f));
            DrawColorPicker("Background", "H", elem.HoverBgColor, c => RecordColorUndo(elem, "HoverBgColor", elem.HoverBgColor, c),
                defaultColor: new(0.22f, 0.28f, 0.45f));
            // Border default matches BgColor for overlays/labels (invisible border by default)
            Vector3 borderDefH = (elem.Type == UIElementType.Container || elem.Type == UIElementType.Dialog || elem.Type == UIElementType.Label)
                ? elem.HoverBgColor : new Vector3(0.5f, 0.6f, 1.0f);
            DrawColorPicker("Border", "H", elem.HoverBorderColor, c => RecordColorUndo(elem, "HoverBorderColor", elem.HoverBorderColor, c),
                defaultColor: borderDefH);

            ImGui.Unindent();
            ImGui.EndDisabled();
        }

        // ════════════════════════════════════════════
        //  Behaviors (Click + Hover)
        // ════════════════════════════════════════════
        if (ImGui.CollapsingHeader("Behaviors", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var behaviors = IDEBridge.AvailableBehaviors;
            string[] behaviorLabels = new string[behaviors.Length];
            for (int i = 0; i < behaviors.Length; i++)
                behaviorLabels[i] = behaviors[i].Label;

            // Parse current behavior to get type and param
            string currentLabel = elem.ClickBehaviorLabel;
            var (currentType, currentParam) = IDEBridge.ParseBehavior(currentLabel);

            int clickIdx = 0;
            for (int i = 0; i < behaviors.Length; i++)
            {
                if (string.Equals(behaviors[i].Value, currentType, StringComparison.OrdinalIgnoreCase))
                { clickIdx = i; break; }
            }

            ImGui.Text("On Click:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##click_bhv", ref clickIdx, behaviorLabels, behaviorLabels.Length))
            {
                string newType = behaviors[clickIdx].Value;
                string newParam = currentParam;
                if (string.IsNullOrEmpty(newType))
                    newParam = "";
                else if (newType != currentType)
                    newParam = ""; // reset param when switching type
                elem.ClickBehaviorLabel = IDEBridge.BuildBehavior(newType, newParam);
                elem.OnClick = null;
                // Update current values so sub-combo appears immediately
                currentType = newType;
                currentParam = newParam;
            }

            // ── Sub-parameter: overlay name (only when "overlay" type selected) ──
            if (string.Equals(currentType, "overlay", StringComparison.OrdinalIgnoreCase))
            {
                ImGui.Spacing();
                ImGui.Indent();
                ImGui.Text("Target Overlay:");
                ImGui.SetNextItemWidth(-1);

                string[] overlays = IDEBridge.GetOverlayNamesFromScene(_bridge.SceneRoot);
                if (overlays.Length == 0) overlays = ["(create overlay first)"];
                int overlayIdx = 0;
                for (int i = 0; i < overlays.Length; i++)
                {
                    if (string.Equals(overlays[i], currentParam, StringComparison.OrdinalIgnoreCase))
                    { overlayIdx = i; break; }
                }

                // Use BeginCombo/EndCombo to allow selection even with 1 item
                string preview = overlayIdx >= 0 && overlayIdx < overlays.Length ? overlays[overlayIdx] : "";
                if (ImGui.BeginCombo("##overlay_target", preview))
                {
                    for (int i = 0; i < overlays.Length; i++)
                    {
                        bool isSelected = (i == overlayIdx);
                        if (ImGui.Selectable(overlays[i], isSelected))
                        {
                            overlayIdx = i;
                            elem.ClickBehaviorLabel = IDEBridge.BuildBehavior("overlay", overlays[overlayIdx]);
                            elem.OnClick = null;
                        }
                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                ImGui.Unindent();
            }

            // ── Sub-parameter: scene name (only when "scene" type selected) ──
            if (string.Equals(currentType, "scene", StringComparison.OrdinalIgnoreCase))
            {
                ImGui.Spacing();
                ImGui.Indent();
                ImGui.Text("Target Scene:");
                ImGui.SetNextItemWidth(-1);

                string[] scenes = _bridge.AvailableSceneNames;
                if (scenes.Length == 0)
                {
                    ImGui.TextDisabled("(no scenes — add scenes in Scene Manager)");
                }
                else
                {
                    int sceneIdx = 0;
                    for (int i = 0; i < scenes.Length; i++)
                    {
                        if (string.Equals(scenes[i], currentParam, StringComparison.OrdinalIgnoreCase))
                        { sceneIdx = i; break; }
                    }

                    // Use BeginCombo/EndCombo to allow selection even with 1 item
                    string preview = sceneIdx >= 0 && sceneIdx < scenes.Length ? scenes[sceneIdx] : "";
                    if (ImGui.BeginCombo("##scene_target", preview))
                    {
                        for (int i = 0; i < scenes.Length; i++)
                        {
                            bool isSelected = (i == sceneIdx);
                            if (ImGui.Selectable(scenes[i], isSelected))
                            {
                                sceneIdx = i;
                                elem.ClickBehaviorLabel = IDEBridge.BuildBehavior("scene", scenes[sceneIdx]);
                                elem.OnClick = null;
                            }
                            if (isSelected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }
                }
                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // ── Hover behaviors (simplified: just show type combo, no sub-params) ──
            int hoverEnterIdx = 0;
            var (hEnterType, _) = IDEBridge.ParseBehavior(elem.HoverEnterLabel);
            for (int i = 0; i < behaviors.Length; i++)
            {
                if (string.Equals(behaviors[i].Value, hEnterType, StringComparison.OrdinalIgnoreCase))
                { hoverEnterIdx = i; break; }
            }

            ImGui.Text("On Hover Enter:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##hover_enter_bhv", ref hoverEnterIdx, behaviorLabels, behaviorLabels.Length))
            {
                elem.HoverEnterLabel = behaviors[hoverEnterIdx].Value;
                elem.OnHoverEnter = null;
            }

            ImGui.Spacing();

            int hoverExitIdx = 0;
            var (hExitType, _) = IDEBridge.ParseBehavior(elem.HoverExitLabel);
            for (int i = 0; i < behaviors.Length; i++)
            {
                if (string.Equals(behaviors[i].Value, hExitType, StringComparison.OrdinalIgnoreCase))
                { hoverExitIdx = i; break; }
            }

            ImGui.Text("On Hover Exit:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##hover_exit_bhv", ref hoverExitIdx, behaviorLabels, behaviorLabels.Length))
            {
                elem.HoverExitLabel = behaviors[hoverExitIdx].Value;
                elem.OnHoverExit = null;
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Save scene to persist behavior changes");
        }

        // ════════════════════════════════════════════
        //  Visibility & Flags
        // ════════════════════════════════════════════
        if (ImGui.CollapsingHeader("Visibility", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool vis = elem.IsVisible;
            if (ImGui.Checkbox("Visible", ref vis))
                elem.IsVisible = vis;

            // Show a small preview chip
            if (vis)
                ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.4f, 1f), "● Visible");
            else
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "○ Hidden");

            ImGui.Spacing();
            ImGui.Separator();

            // ── Auto-fill window (for overlay/background elements) ──
            bool autoFill = elem.AutoFillWindow;
            if (ImGui.Checkbox("Auto-fill Window", ref autoFill))
                elem.AutoFillWindow = autoFill;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When enabled, this element automatically fills the entire viewport (X=0, Y=0, W=viewport, H=viewport)");

            // ── Auto-center (for overlays/dialogs) ──
            bool autoCenter = elem.AutoCenter;
            if (ImGui.Checkbox("Auto-center", ref autoCenter))
                elem.AutoCenter = autoCenter;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When enabled, this element is automatically centered in the viewport");
        }

        // ════════════════════════════════════════════
        //  Children list
        // ════════════════════════════════════════════
        if (elem.Children.Count > 0 && ImGui.CollapsingHeader($"Children ({elem.Children.Count})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < elem.Children.Count; i++)
            {
                var child = elem.Children[i];
                // Clickable child entry — clicking selects it in the hierarchy
                ImGui.BulletText($"{child.GetIcon()} {child.Name}");
                if (ImGui.IsItemClicked())
                    _bridge.SelectedUIElement = child;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Type: {child.Type} | Click to select");
            }
        }
    }

    /// <summary>Show scene overview info when no element is selected but an editor scene is active.</summary>
    private void RenderEditorSceneInfo(IDEBridge.EditorScene editorScene)
    {
        if (ImGui.CollapsingHeader("Scene Info", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 1.0f, 1f), $"Scene {editorScene.Name}");
            ImGui.Separator();

            // Count elements recursively
            int totalCount = CountElementsRecursive(editorScene.Root);
            int visibleCount = CountVisibleRecursive(editorScene.Root);

            ImGui.Text($"Total elements:  {totalCount}");
            ImGui.Text($"Visible elements: {visibleCount}");
            var typeLabel = editorScene.Type switch
            {
                IDEBridge.SceneType.MainMenu => "MainMenu",
                IDEBridge.SceneType.GameScene => "GameScene",
                IDEBridge.SceneType.Loading => "Loading",
                _ => "?",
            };
            ImGui.Text($"Type: {typeLabel}");
            ImGui.Text($"Root children: {editorScene.Root.Children.Count}");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.6f, 1f), "Click an element in the viewport");
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.6f, 1f), "or hierarchy tree to inspect it.");
        }
    }

    private static int CountElementsRecursive(UIElement elem)
    {
        int count = 1;
        foreach (var child in elem.Children)
            count += CountElementsRecursive(child);
        return count;
    }

    private static int CountVisibleRecursive(UIElement elem)
    {
        int count = elem.IsVisible ? 1 : 0;
        foreach (var child in elem.Children)
            count += CountVisibleRecursive(child);
        return count;
    }

    /// <summary>Record a color change undo, then apply the new color.
    /// Called from color picker callbacks.</summary>
    private void RecordColorUndo(UIElement elem, string propName, Vector3 oldColor, Vector3 newColor)
    {
        if (_bridge.RecordColorUndo != null && oldColor != newColor)
            _bridge.RecordColorUndo(elem, propName, oldColor, newColor);

        // Apply the color via the property setter
        switch (propName)
        {
            case "TextColor": elem.TextColor = newColor; break;
            case "BgColor": elem.BgColor = newColor; break;
            case "BorderColor": elem.BorderColor = newColor; break;
            case "HoverTextColor": elem.HoverTextColor = newColor; break;
            case "HoverBgColor": elem.HoverBgColor = newColor; break;
            case "HoverBorderColor": elem.HoverBorderColor = newColor; break;
        }
    }

    /// <summary>Draw a color picker with label + colored square + eyedropper.
    /// Uses ImGui ColorEdit3 with NoInputs flag — click the colored square to open the
    /// picker popup, then use the eyedropper pipette icon to sample from screen.
    /// The <paramref name="idSuffix"/> ensures unique ImGui IDs when multiple
    /// pickers share the same label (e.g. "N" for Normal, "H" for Hover).
    /// When the color differs from <paramref name="defaultColor"/>, a small ↺ reset
    /// button appears to restore the default value.</summary>
    private static void DrawColorPicker(string label, string idSuffix, Vector3 color, Action<Vector3> onChanged, Vector3? defaultColor = null)
    {
        ImGui.Text(label);
        ImGui.SameLine();

        // ── Reset button — only show when color differs from default ──
        bool hasDefault = defaultColor.HasValue;
        bool isDefault = false;
        if (hasDefault)
        {
            var def = defaultColor.Value;
            isDefault = Math.Abs(color.X - def.X) < 0.001f &&
                        Math.Abs(color.Y - def.Y) < 0.001f &&
                        Math.Abs(color.Z - def.Z) < 0.001f;
        }

        if (hasDefault && !isDefault)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.2f, 0.15f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.3f, 0.2f, 1f));
            if (ImGui.SmallButton($"↺##reset_{label}_{idSuffix}"))
                onChanged(defaultColor!.Value);
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Reset to default color");
            ImGui.SameLine();
        }

        ImGui.SetNextItemWidth(-1);

        var c = color;
        if (ImGui.ColorEdit3($"##color_{label}_{idSuffix}", ref c, ImGuiColorEditFlags.NoInputs))
            onChanged(c);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click to open color picker with eyedropper (pipette icon)");
    }

    /// <summary>Scan Artifacts/fonts/ once and cache the list of .ttf files.</summary>
    private void ScanFontsOnce()
    {
        if (_fontsScanned) return;
        _fontsScanned = true;

        try
        {
            string fontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Artifacts", "fonts");
            if (Directory.Exists(fontsDir))
            {
                var files = Directory.GetFiles(fontsDir, "*.ttf");
                _availableFonts = new string[files.Length];
                for (int i = 0; i < files.Length; i++)
                    _availableFonts[i] = Path.GetFileName(files[i]);
            }
        }
        catch
        {
            _availableFonts = null;
        }
    }

    private void RenderObjectInspector(GltfObject obj, CharacterAgent? agent)
    {
        // ── Focus Camera button (always at top) ──
        if (ImGui.Button("Focus Camera", new Vector2(-1, 30)))
        {
            _bridge.FocusCameraOnSelected?.Invoke();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Move camera to look at this object");

        ImGui.Separator();

        // ── Object Info ──
        if (ImGui.CollapsingHeader("Object Info", ImGuiTreeNodeFlags.DefaultOpen))
        {
            string meshName = obj.GpuData.Data.Meshes.Length > 0 ? obj.GpuData.Data.Meshes[0].Name : "";
            string nameDisplay = !string.IsNullOrEmpty(meshName) ? meshName : "Unnamed";
            ImGui.Text($"Name:    {nameDisplay}");
            ImGui.Text($"Type:    {(obj.IsStatic ? "Static" : "Animated")}");
            ImGui.Text($"Player:  {obj.IsPlayer}");
            ImGui.Text($"Visible: {obj.IsVisible}");
            ImGui.Text($"AnimLOD: {obj.AnimLOD}");
        }

        // ── Transform ──
        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var pos = obj.Position;
            if (ImGui.DragFloat3("Position", ref pos, 0.1f))
            {
                obj.Position = pos;
                if (agent != null) agent.Position = pos;
            }

            var scale = obj.Scale;
            if (ImGui.DragFloat("Scale", ref scale, 0.01f))
                obj.Scale = scale;
        }

        // ── Agent Info ──
        if (agent != null && ImGui.CollapsingHeader("Agent", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float health = agent.Health;
            float maxHp = CharacterAgent.MaxHealth;
            ImGui.Text($"Health: {health:F1} / {maxHp:F1}");
            ImGui.ProgressBar(health / maxHp, new Vector2(-1, 0), $"{health:F1}/{maxHp:F1}");

            ImGui.Text($"State:  {(agent.Dead ? "Dead" : "Alive")}");
            ImGui.Text($"Dead:   {agent.Dead}");
            ImGui.Text($"Heading: {agent.Heading * 180f / MathF.PI:F1}°");

            if (agent.Target != null)
                ImGui.Text($"Target: {agent.Target.GetHashCode():X8}");
            else
                ImGui.Text("Target: None");
        }
    }
}
