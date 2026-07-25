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
        ["Scene", "Container", "Button", "Label", "SliderNumber", "SliderText", "Checkbox", "Dropdown", "TextBox"];

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
        // Unique ID scope per element instance — prevents ImGui ID collisions
        // when switching between elements (all InputText/DragFloat/Combo IDs are
        // scoped under elem.InstanceId, so each element gets its own ID space).
        ImGui.PushID(elem.InstanceId);

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
            ImGui.PopID();
            return; // Scene type: nothing else to show
        }

        // ════════════════════════════════════════════
        //  Type-Specific Properties (Moved to top)
        // ════════════════════════════════════════════
        RenderTypeSpecificProperties(elem);

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

            // ── Fallback Label (shown when image can't be loaded) — independent from main Text ──
            ImGui.Spacing();
            string fallbackText = elem.FallbackText;
            ImGui.Text("Fallback Label:");
            if (ImGui.InputText("##fallback_label", ref fallbackText, 256))
                elem.FallbackText = fallbackText;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Displayed when image fails to load");

            // Alignment
            string[] alignItems = ["Left", "Center", "Right"];
            int alignIdx = (int)elem.Alignment;
            if (ImGui.Combo("AlignmentFallback", ref alignIdx, alignItems, alignItems.Length))
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
            Vector3 borderDefN = (elem.Type == UIElementType.Container || elem.Type == UIElementType.Label ||
                                   elem.Type == UIElementType.SliderNumber || elem.Type == UIElementType.SliderText ||
                                   elem.Type == UIElementType.Checkbox ||
                                   elem.Type == UIElementType.Dropdown || elem.Type == UIElementType.TextBox)
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
            Vector3 borderDefH = (elem.Type == UIElementType.Container || elem.Type == UIElementType.Label ||
                                    elem.Type == UIElementType.SliderNumber || elem.Type == UIElementType.SliderText ||
                                    elem.Type == UIElementType.Checkbox ||
                                    elem.Type == UIElementType.Dropdown || elem.Type == UIElementType.TextBox)
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

            // ── Opacity / Transparency ──
            float opacity = elem.Opacity;
            if (ImGui.SliderFloat("Opacity", ref opacity, 0f, 1f, "%.2f"))
                elem.Opacity = Math.Clamp(opacity, 0f, 1f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Overall element transparency (0.0 = fully transparent, 1.0 = fully opaque)");

            // Preview bar
            var barCol = new Vector4(0.3f, 0.8f, 1.0f, 0.5f);
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barCol);
            ImGui.ProgressBar(elem.Opacity, new Vector2(-1, 6f), "");
            ImGui.PopStyleColor(1);

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
        ImGui.PopID();
    }

    /// <summary>Render type-specific properties for slider, checkbox, dropdown, textbox elements.</summary>
    private static void RenderTypeSpecificProperties(UIElement elem)
    {
        switch (elem.Type)
        {
            case UIElementType.SliderNumber:
                if (ImGui.CollapsingHeader("Slider Number Properties", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    float minVal = elem.MinValue;
                    if (ImGui.DragFloat("Min Value", ref minVal, 0.1f))
                        elem.MinValue = minVal;

                    float maxVal = elem.MaxValue;
                    if (ImGui.DragFloat("Max Value", ref maxVal, 0.1f))
                        elem.MaxValue = maxVal;

                    float step = elem.Step;
                    if (ImGui.DragFloat("Step", ref step, 0.01f, 0.001f, 1000f))
                        elem.Step = Math.Max(0.001f, step);

                    float curVal = elem.CurrentValue;
                    if (ImGui.SliderFloat("Current Value", ref curVal, elem.MinValue, elem.MaxValue))
                        elem.CurrentValue = curVal;

                    ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1f),
                        $"Value: {elem.CurrentValue:F2}  (Range: {elem.MinValue:F1} - {elem.MaxValue:F1}, Step: {elem.Step:F3})");

                    ImGui.Spacing();
                    string[] labelPositions = ["None", "Left", "Right", "Top", "Bottom"];
                    int posIdx = (int)elem.SliderValuePosition;
                    if (ImGui.Combo("Value Position", ref posIdx, labelPositions, labelPositions.Length))
                        elem.SliderValuePosition = (SliderLabelPosition)posIdx;

                    ImGui.Separator();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1f), "Style");
                    DrawColorPicker("Track Color", "st", elem.SliderTrackColor, c => elem.SliderTrackColor = c,
                        defaultColor: new(0.30f, 0.30f, 0.35f));
                    DrawColorPicker("Fill Color", "sf", elem.SliderFillColor, c => elem.SliderFillColor = c,
                        defaultColor: new(0.3f, 0.6f, 1.0f));
                    DrawColorPicker("Thumb Color", "stc", elem.SliderThumbColor, c => elem.SliderThumbColor = c,
                        defaultColor: new(0.9f, 0.9f, 1.0f));
                    DrawColorPicker("Thumb Border", "stb", elem.SliderThumbBorderColor, c => elem.SliderThumbBorderColor = c,
                        defaultColor: new(0.3f, 0.6f, 1.0f));

                    float thumbSz = elem.SliderThumbSize;
                    if (ImGui.DragFloat("Thumb Size", ref thumbSz, 0.5f, 4f, 40f, "%.1f"))
                        elem.SliderThumbSize = Math.Max(4f, thumbSz);

                    float trackHt = elem.SliderTrackHeight;
                    if (ImGui.DragFloat("Track Height", ref trackHt, 0.5f, 2f, 30f, "%.1f"))
                        elem.SliderTrackHeight = Math.Max(2f, trackHt);
                }
                break;

            case UIElementType.Checkbox:
                if (ImGui.CollapsingHeader("Checkbox Properties", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    bool checkedVal = elem.IsChecked;
                    if (ImGui.Checkbox("Checked", ref checkedVal))
                        elem.IsChecked = checkedVal;

                    if (elem.IsChecked)
                        ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.4f, 1f), "● Checked");
                    else
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "○ Unchecked");

                    ImGui.Separator();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1f), "Style");
                    DrawColorPicker("Checked Bg", "cb", elem.CheckedBgColor, c => elem.CheckedBgColor = c,
                        defaultColor: new(0.25f, 0.55f, 1.0f));
                    DrawColorPicker("Unchecked Bg", "ub", elem.UncheckedBgColor, c => elem.UncheckedBgColor = c,
                        defaultColor: new(0.15f, 0.15f, 0.22f));
                    DrawColorPicker("Checkmark", "cm", elem.CheckmarkColor, c => elem.CheckmarkColor = c,
                        defaultColor: new(0.9f, 0.9f, 1.0f));
                }
                break;

            case UIElementType.Dropdown:
                if (ImGui.CollapsingHeader("Dropdown Properties", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    // Options list editor
                    ImGui.Text("Options:");
                    ImGui.Spacing();

                    // Show current options with ability to edit/remove
                    int removeIdx = -1;
                    for (int i = 0; i < elem.Options.Count; i++)
                    {
                        string opt = elem.Options[i];
                        ImGui.PushID($"opt_{i}");

                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 60f);
                        if (ImGui.InputText("##opt", ref opt, 256))
                            elem.Options[i] = opt;

                        ImGui.SameLine();
                        if (ImGui.Button("X", new Vector2(24, 0)))
                            removeIdx = i;

                        // Mark as selected if index matches
                        if (i == elem.SelectedIndex)
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.4f, 1f), "◄ Selected");
                        }

                        ImGui.PopID();
                    }

                    if (removeIdx >= 0 && elem.Options.Count > 1)
                    {
                        elem.Options.RemoveAt(removeIdx);
                        if (elem.SelectedIndex >= elem.Options.Count)
                            elem.SelectedIndex = elem.Options.Count - 1;
                    }

                    // Add option button
                    if (ImGui.Button("+ Add Option", new Vector2(-1, 24)))
                    {
                        elem.Options.Add($"Option {elem.Options.Count + 1}");
                    }

                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    // Selected index combo
                    string[] optArray = [.. elem.Options];
                    int selIdx = elem.SelectedIndex;
                    if (selIdx < 0 || selIdx >= optArray.Length)
                        selIdx = 0;

                    string preview = selIdx >= 0 && selIdx < optArray.Length ? optArray[selIdx] : "(none)";
                    if (ImGui.BeginCombo("Selected Value", preview))
                    {
                        for (int i = 0; i < optArray.Length; i++)
                        {
                            bool isSelected = (i == selIdx);
                            if (ImGui.Selectable(optArray[i], isSelected))
                            {
                                elem.SelectedIndex = i;
                            }
                            if (isSelected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }

                    ImGui.Separator();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1f), "Style");
                    DrawColorPicker("Arrow Color", "da", elem.ArrowColor, c => elem.ArrowColor = c,
                        defaultColor: new(0.5f, 0.5f, 0.7f));
                }
                break;

            case UIElementType.SliderText:
                if (ImGui.CollapsingHeader("Slider Text Properties", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Text("Text Options:");
                    ImGui.Spacing();

                    int removeIdx = -1;
                    for (int i = 0; i < elem.TextOptions.Count; i++)
                    {
                        string opt = elem.TextOptions[i];
                        ImGui.PushID($"stxt_{i}");

                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 60f);
                        if (ImGui.InputText("##stxt", ref opt, 256))
                            elem.TextOptions[i] = opt;

                        ImGui.SameLine();
                        if (ImGui.Button("X", new Vector2(24, 0)))
                            removeIdx = i;

                        if (i == elem.SelectedTextIndex)
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.4f, 1f), "◄ Selected");
                        }

                        ImGui.PopID();
                    }

                    if (removeIdx >= 0 && elem.TextOptions.Count > 1)
                    {
                        elem.TextOptions.RemoveAt(removeIdx);
                        if (elem.SelectedTextIndex >= elem.TextOptions.Count)
                            elem.SelectedTextIndex = elem.TextOptions.Count - 1;
                    }

                    if (ImGui.Button("+ Add Option", new Vector2(-1, 24)))
                    {
                        elem.TextOptions.Add($"Option {elem.TextOptions.Count + 1}");
                    }

                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    string[] optArray = [.. elem.TextOptions];
                    int selIdx = elem.SelectedTextIndex;
                    if (selIdx < 0 || selIdx >= optArray.Length)
                        selIdx = 0;

                    string preview = selIdx >= 0 && selIdx < optArray.Length ? optArray[selIdx] : "(none)";
                    if (ImGui.BeginCombo("Selected Value", preview))
                    {
                        for (int i = 0; i < optArray.Length; i++)
                        {
                            bool isSelected = (i == selIdx);
                            if (ImGui.Selectable(optArray[i], isSelected))
                            {
                                elem.SelectedTextIndex = i;
                            }
                            if (isSelected)
                                ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }

                    ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1f),
                        $"Current: {elem.TextOptions[elem.SelectedTextIndex]}");

                    ImGui.Spacing();
                    string[] labelPositions = ["None", "Left", "Right", "Top", "Bottom"];
                    int posIdx = (int)elem.SliderValuePosition;
                    if (ImGui.Combo("Value Position", ref posIdx, labelPositions, labelPositions.Length))
                        elem.SliderValuePosition = (SliderLabelPosition)posIdx;

                    ImGui.Separator();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1f), "Style");
                    DrawColorPicker("Track Color", "stt", elem.SliderTrackColor, c => elem.SliderTrackColor = c,
                        defaultColor: new(0.30f, 0.30f, 0.35f));
                    DrawColorPicker("Fill Color", "stf", elem.SliderFillColor, c => elem.SliderFillColor = c,
                        defaultColor: new(0.3f, 0.6f, 1.0f));
                    DrawColorPicker("Thumb Color", "sttc", elem.SliderThumbColor, c => elem.SliderThumbColor = c,
                        defaultColor: new(0.9f, 0.9f, 1.0f));
                    DrawColorPicker("Thumb Border", "sttb", elem.SliderThumbBorderColor, c => elem.SliderThumbBorderColor = c,
                        defaultColor: new(0.3f, 0.6f, 1.0f));

                    float thumbSz = elem.SliderThumbSize;
                    if (ImGui.DragFloat("Thumb Size", ref thumbSz, 0.5f, 4f, 40f, "%.1f"))
                        elem.SliderThumbSize = Math.Max(4f, thumbSz);

                    float trackHt = elem.SliderTrackHeight;
                    if (ImGui.DragFloat("Track Height", ref trackHt, 0.5f, 2f, 30f, "%.1f"))
                        elem.SliderTrackHeight = Math.Max(2f, trackHt);
                }
                break;

            case UIElementType.TextBox:
                if (ImGui.CollapsingHeader("Text Box Properties", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    string placeholder = elem.Placeholder;
                    if (ImGui.InputText("Placeholder", ref placeholder, 256))
                        elem.Placeholder = placeholder;

                    int maxLen = elem.MaxLength;
                    if (ImGui.DragInt("Max Length", ref maxLen, 1, 0, 4096))
                        elem.MaxLength = Math.Max(0, maxLen);

                    string inputText = elem.InputText;
                    int maxInputLen = elem.MaxLength > 0 ? elem.MaxLength : 4096;
                    if (ImGui.InputText("Current Value", ref inputText, (uint)maxInputLen))
                        elem.InputText = inputText;

                    ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1f),
                        $"Length: {elem.InputText.Length}{(elem.MaxLength > 0 ? $" / {elem.MaxLength}" : "")}");

                    ImGui.Separator();
                    ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1f), "Style");
                    DrawColorPicker("Cursor Color", "tc", elem.CursorColor, c => elem.CursorColor = c,
                        defaultColor: new(0.5f, 0.8f, 1.0f));
                }
                break;
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
