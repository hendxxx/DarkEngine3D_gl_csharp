using DarkEngine3D_gl_csharp.Engine.Libs;
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
        var editorObj = _bridge.SelectedEditorObject;

        // ── "Select Scene" button — shown when any object is selected, allows quick jump
        //     to scene render properties (BackgroundColor, Wireframe, etc.).
        bool hasSelection = editorObj != null || (uiElem != null && uiElem.Type != UIElementType.Scene) || obj != null;
        if (hasSelection)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.35f, 0.55f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.50f, 0.75f, 1f));
            if (ImGui.Button("🎬 Select Scene", new Vector2(-1, 26)))
            {
                // Clear all selections to jump to scene properties
                _bridge.SelectedUIElement = null;
                _bridge.SelectedUIElements?.Clear();
                _bridge.SelectedEditorObject = null;
                _bridge.SelectedObject = null;
                _bridge.SelectedAgent = null;
            }
            ImGui.PopStyleColor(2);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Deselect all objects and show scene properties (BackgroundColor, Wireframe, etc.)");
            ImGui.Separator();
        }

        if (editorObj != null)
        {
            RenderEditorObjectInspector(editorObj);
        }
        else if (uiElem != null)
        {
            // Always render the UI element inspector (children list for Scene type)
            RenderUIElementInspector(uiElem);

            // If the selected UI element is a Scene type, ALSO show the scene's
            // render properties (BackgroundColor, Wireframe, etc.) below it.
            // This ensures users can always access per-scene render settings.
            if (uiElem.Type == UIElementType.Scene)
            {
                RenderScenePropertiesFromSelection();
            }
        }
        else if (obj != null)
        {
            RenderObjectInspector(obj, agent);
        }
        else
        {
            // When nothing is selected, show scene render properties
            RenderScenePropertiesFromSelection();
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

    /// <summary>Find an editor scene and render its properties. Used when nothing specific is selected,
    /// or when a Scene-type UI element is selected (shows render props below the children list).</summary>
    private void RenderScenePropertiesFromSelection()
    {
        var selectedScene = _bridge.SelectedEditorScene;
        IDEBridge.EditorScene? editorScene = null;

        if (selectedScene != null && _bridge.EditorScenes.TryGetValue(selectedScene, out var scene))
        {
            editorScene = scene;
        }
        else if (_bridge.EditorScenes.Count > 0)
        {
            // No scene selected but there are editor scenes — pick the first one
            foreach (var kvp in _bridge.EditorScenes)
            {
                editorScene = kvp.Value;
                break;
            }
        }

        if (editorScene != null)
        {
            RenderEditorSceneInfo(editorScene);
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No object selected");
            ImGui.TextDisabled("Click an element in the viewport or hierarchy");
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

        // ════════════════════════════════════════════
        //  Render Properties (per-scene)
        // ════════════════════════════════════════════
        // Ensure the scene has a RenderProperties instance
        var renderProps = editorScene.RenderProperties;
        if (renderProps == null)
        {
            renderProps = new SceneRenderProperties();
            editorScene.RenderProperties = renderProps;
        }

        if (ImGui.CollapsingHeader("Render Properties", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool changed = false;

            // ── Background Color ──
            var bgColor = renderProps.BackgroundColor;
            if (ImGui.ColorEdit3("Background Color", ref bgColor, ImGuiColorEditFlags.NoInputs))
            {
                renderProps.BackgroundColor = bgColor;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("GL.ClearColor — background color when rendering this scene");

            ImGui.Spacing();

            // ── VSync ──
            bool vsync = renderProps.VSync;
            if (ImGui.Checkbox("VSync", ref vsync))
            {
                renderProps.VSync = vsync;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Enable/disable vertical sync for this scene");

            ImGui.Spacing();

            // ── Face Culling ──
            string[] cullModes = ["None", "Back", "Front", "Front & Back"];
            int cullIdx = (int)renderProps.FaceCulling;
            if (cullIdx < 0 || cullIdx >= cullModes.Length) cullIdx = 1;
            if (ImGui.Combo("Face Culling", ref cullIdx, cullModes, cullModes.Length))
            {
                renderProps.FaceCulling = (CullMode)cullIdx;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Which faces to cull (Back = default, None = disable culling)");

            // ── Front Face Winding ──
            string[] windingModes = ["CCW (Counter-Clockwise)", "CW (Clockwise)"];
            int windingIdx = renderProps.FrontFaceWinding == WindingOrder.CCW ? 0 : 1;
            if (ImGui.Combo("Front Face Winding", ref windingIdx, windingModes, windingModes.Length))
            {
                renderProps.FrontFaceWinding = windingIdx == 0 ? WindingOrder.CCW : WindingOrder.CW;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Winding order for front faces (CCW = OpenGL default)");

            ImGui.Spacing();

            // ── Wireframe Mode ──
            bool wireframe = renderProps.WireframeMode;
            if (ImGui.Checkbox("Wireframe Mode", ref wireframe))
            {
                renderProps.WireframeMode = wireframe;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Render polygons as lines (wireframe)");

            ImGui.Spacing();

            // ── Depth Test ──
            bool depthTest = renderProps.DepthTest;
            if (ImGui.Checkbox("Depth Test", ref depthTest))
            {
                renderProps.DepthTest = depthTest;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Enable/disable depth testing");

            // ── Blending ──
            bool blending = renderProps.Blending;
            if (ImGui.Checkbox("Blending", ref blending))
            {
                renderProps.Blending = blending;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Enable/disable alpha blending");

            ImGui.Spacing();
            ImGui.Separator();

            // ── Apply button ──
            if (changed)
            {
                // Apply the render properties immediately
                renderProps.Apply();

                // Wireframe mode should ONLY affect the viewport (shared FBO rendering),
                // not the game scene. Reset PolygonMode to GL_FILL here so the game scene
                // isn't affected. Wireframe will be reapplied in SceneManager for the viewport.
                GL.PolygonMode(Const.GL_FRONT_AND_BACK, Const.GL_FILL);

                Console.WriteLine($"[Inspector] Applied render properties for scene '{editorScene.Name}'");
            }

            ImGui.Spacing();
            ImGui.Separator();

            // ════════════════════════════════════════════
            //  Selection Highlight Colors (global IDE settings)
            // ════════════════════════════════════════════
            if (ImGui.CollapsingHeader("Selection Highlight", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1f), "Customize the selection wireframe colors");
                ImGui.Spacing();

                var selColor = _bridge.SelectionHighlights.GltfObject;
                if (ImGui.ColorEdit3("3D Object", ref selColor, ImGuiColorEditFlags.NoInputs))
                {
                    _bridge.SelectionHighlights.GltfObject = selColor;
                    Console.WriteLine($"[Inspector] Selection highlight color changed: {selColor}");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Color of the pulsing inverted-hull outline around selected 3D objects (glTF)");

                var editorSelColor = _bridge.SelectionHighlights.EditorObject;
                if (ImGui.ColorEdit3("Editor Object", ref editorSelColor, ImGuiColorEditFlags.NoInputs))
                {
                    _bridge.SelectionHighlights.EditorObject = editorSelColor;
                    Console.WriteLine($"[Inspector] Editor highlight color changed: {editorSelColor}");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Color of the inverted-hull outline around selected editor primitives");

                // Reset selection colors to defaults
                ImGui.Spacing();
                if (ImGui.SmallButton("Reset Colors"))
                {
                    _bridge.SelectionHighlights = SelectionHighlightColors.Default;
                    Console.WriteLine("[Inspector] Selection highlight colors reset to defaults");
                }
            }

            // ════════════════════════════════════════════
            //  Editor Settings (fly mode sensitivity, speed)
            // ════════════════════════════════════════════
            if (ImGui.CollapsingHeader("Editor Settings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1f), "Fly Mode Camera");
                ImGui.Spacing();

                float flySens = Config.CameraConfig.FlyMouseSensitivity;
                if (ImGui.SliderFloat("Mouse Sensitivity", ref flySens, 0.01f, 2.0f, "%.2f"))
                {
                    Config.CameraConfig.FlyMouseSensitivity = flySens;
                    Console.WriteLine($"[Inspector] Fly mouse sensitivity changed: {flySens:F2}");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Mouse look sensitivity in editor fly mode (toggle with the ✈ Fly button in the viewport)");

                float flySpeed = Config.CameraConfig.CameraFlySpeed;
                if (ImGui.SliderFloat("Movement Speed", ref flySpeed, 1f, 500f, "%.0f"))
                {
                    Config.CameraConfig.CameraFlySpeed = flySpeed;
                    Console.WriteLine($"[Inspector] Fly movement speed changed: {flySpeed:F0}");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("WASD movement speed in editor fly mode");

                float flyZoom = Config.CameraConfig.FlyZoomSpeed;
                if (ImGui.SliderFloat("Zoom Speed", ref flyZoom, 1f, 500f, "%.0f"))
                {
                    Config.CameraConfig.FlyZoomSpeed = flyZoom;
                    Console.WriteLine($"[Inspector] Fly zoom speed changed: {flyZoom:F0}");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Scroll-wheel zoom speed in editor fly mode (independent of movement speed)");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1f), "Transform Gizmo");
                ImGui.Spacing();

                float gizmoSize = Config.CameraConfig.GizmoSize;
                if (ImGui.SliderFloat("Gizmo Size", ref gizmoSize, 0.25f, 3.0f, "%.2f"))
                {
                    Config.CameraConfig.GizmoSize = gizmoSize;
                    Console.WriteLine($"[Inspector] Gizmo size changed: {gizmoSize:F2}");
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Scale multiplier for the 3D transform gizmo (Move/Rotate/Scale)");

                ImGui.Spacing();
                ImGui.TextDisabled("Settings are applied immediately.");
            }

            // ── Reset to defaults button ──
            ImGui.Spacing();
            if (ImGui.Button("Reset to Defaults", new Vector2(-1, 28)))
            {
                renderProps.BackgroundColor = new Vector3(0f, 0f, 0f);
                renderProps.VSync = true;
                renderProps.FaceCulling = CullMode.Back;
                renderProps.FrontFaceWinding = WindingOrder.CCW;
                renderProps.WireframeMode = false;
                renderProps.DepthTest = true;
                renderProps.Blending = false;
                renderProps.Apply();
                // Reset wireframe so it doesn't affect game scene
                GL.PolygonMode(Const.GL_FRONT_AND_BACK, Const.GL_FILL);
                Console.WriteLine($"[Inspector] Reset render properties to defaults for scene '{editorScene.Name}'");
            }
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

    /// <summary>Render inspector for an EditorObject (primitives, glb references).</summary>
    private unsafe void RenderEditorObjectInspector(EditorObject editorObj)
    {
        // ── Multi-selection indicator ──
        if (_bridge.SelectedEditorObjects.Count > 1)
        {
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 1.0f, 1f),
                $"▲ {_bridge.SelectedEditorObjects.Count} objects selected (editing primary '{editorObj.Name}')");
            ImGui.Separator();
        }

        // ── Identity ──
        if (ImGui.CollapsingHeader("Editor Object", ImGuiTreeNodeFlags.DefaultOpen))
        {
            string name = editorObj.Name;
            if (ImGui.InputText("Name", ref name, 256))
                editorObj.Name = name;

            string typeStr = editorObj.PrimitiveType switch
            {
                EditorPrimitiveType.Plane => "Plane",
                EditorPrimitiveType.Box => "Box",
                EditorPrimitiveType.Sphere => "Sphere",
                EditorPrimitiveType.GlbReference => "GLB Reference",
                EditorPrimitiveType.Camera => "Camera",
                EditorPrimitiveType.Light => "Light",
                EditorPrimitiveType.Sky => "Sky",
                _ => "Unknown"
            };
            ImGui.Text($"Type: {typeStr}");
            if (editorObj.PrimitiveType == EditorPrimitiveType.GlbReference && !string.IsNullOrEmpty(editorObj.GlbFilePath))
            {
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.5f, 1f), $"GLB: {Path.GetFileName(editorObj.GlbFilePath)}");
            }
            ImGui.Separator();
        }

        // ── Transform ──
        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var pos = editorObj.Position;
            if (ImGui.DragFloat3("Position", ref pos, 0.1f))
            {
                editorObj.Position = pos;
                editorObj.MarkDirty();
            }

            var rot = editorObj.RotationEuler;
            if (ImGui.DragFloat3("Rotation (Euler)", ref rot, 1f))
            {
                editorObj.RotationEuler = rot;
                editorObj.MarkDirty();
            }

            var scale = editorObj.Scale;
            if (ImGui.DragFloat3("Scale", ref scale, 0.05f, 0.01f, 100f))
            {
                editorObj.Scale = scale;
                editorObj.MarkDirty();
            }
        }

        // ── Gizmo Pivot ──
        if (ImGui.CollapsingHeader("Gizmo Pivot", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool hasPivot = editorObj.GizmoPivotOverride.HasValue;
            ImGui.TextColored(hasPivot
                ? new Vector4(0.3f, 0.85f, 0.4f, 1f)
                : new Vector4(0.6f, 0.6f, 0.6f, 1f),
                hasPivot ? "● Custom pivot active" : "○ Using object position");

            // Pivot world position — falls back to the object's position when no override set.
            // Dragging these inputs activates a custom pivot at the entered world position.
            var pivot = editorObj.GizmoPivotOverride ?? editorObj.Position;
            if (ImGui.DragFloat3("Pivot Position", ref pivot, 0.1f))
            {
                editorObj.GizmoPivotOverride = pivot;
                // Note: no MarkDirty() here — the pivot is a gizmo render position and
                // does not affect the object's mesh/GPU resources.
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("World position where the gizmo renders. Set via middle-click in the viewport or drag here to place it manually.");

            ImGui.Spacing();

            // Snap the pivot to the object's current position
            if (ImGui.Button("Snap Pivot to Object", new Vector2(-1, 24)))
            {
                editorObj.GizmoPivotOverride = editorObj.Position;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Place the gizmo pivot exactly at the object's position");

            // Clear the pivot override so the gizmo follows the object position again
            ImGui.BeginDisabled(!hasPivot);
            if (ImGui.Button("Reset Pivot (follow object)", new Vector2(-1, 24)))
            {
                editorObj.GizmoPivotOverride = null;
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove the custom pivot — the gizmo follows the object position");
        }

        // ── Height overlays (heatmap / contours) — available for ANY editor object;
        // auto-selects the first terrain plane when none is selected (matches the
        // viewport toolbar's Shade/Contours buttons). ──
        if (ImGui.CollapsingHeader("Height Overlays", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var overlayTerrain = _bridge.SelectedEditorObject is { TerrainEnabled: true } ot ? ot : null;

            bool heatmap = overlayTerrain?.TerrainShowHeatmap ?? false;
            if (ImGui.Checkbox("Show Height Shading (heatmap)", ref heatmap))
            {
                var t = _bridge.ResolveTerrainForOverlay();
                if (t != null)
                {
                    t.TerrainShowHeatmap = heatmap;
                    Console.WriteLine($"[Inspector] Height shading on '{t.Name}' → {heatmap}");
                }
                else
                {
                    Console.WriteLine("[Inspector] No terrain plane found to toggle height shading");
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Overlay a height heatmap (low=blue → high=red) with contour lines, lit by the sun.\nMakes high/low areas obvious while sculpting. Auto-selects the first terrain if none is selected. Not saved with the scene.");

            bool contours = overlayTerrain?.TerrainShowContours ?? false;
            if (ImGui.Checkbox("Show Height Contours", ref contours))
            {
                var t = _bridge.ResolveTerrainForOverlay();
                if (t != null)
                {
                    t.TerrainShowContours = contours;
                    Console.WriteLine($"[Inspector] Height contours on '{t.Name}' → {contours}");
                }
                else
                {
                    Console.WriteLine("[Inspector] No terrain plane found to toggle height contours");
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Draw dark contour lines every 10% of the height range — the texture stays\nfully visible while the relief reads clearly. Auto-selects the first terrain if none is selected. Not saved with the scene.");
        }

        // ── Terrain properties (Plane only) ──
        if (editorObj.PrimitiveType == EditorPrimitiveType.Plane)
        {
            RenderTerrainInspector(editorObj);
        }

        // ── Type-specific properties (Camera / Light / Sky) ──
        if (editorObj.PrimitiveType == EditorPrimitiveType.Camera &&
            ImGui.CollapsingHeader("Camera Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float fov = editorObj.CameraFov;
            if (ImGui.DragFloat("FOV", ref fov, 0.5f, 10f, 120f, "%.1f°"))
                editorObj.CameraFov = fov;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Vertical field of view in degrees");

            float near = editorObj.CameraNear;
            if (ImGui.DragFloat("Near Clip", ref near, 0.01f, 0.01f, 10f, "%.2f"))
                editorObj.CameraNear = near;

            float far = editorObj.CameraFar;
            if (ImGui.DragFloat("Far Clip", ref far, 1f, 10f, 5000f, "%.0f"))
                editorObj.CameraFar = far;

            bool showFrustum = editorObj.ShowFrustum;
            if (ImGui.Checkbox("Show Frustum", ref showFrustum))
                editorObj.ShowFrustum = showFrustum;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show/hide the view-frustum wireframe gizmo for this camera in the viewport");

            ImGui.Spacing();
            // ── Preview from this camera: teleport the editor camera to the marker ──
            if (ImGui.Button("Preview from Camera", new Vector2(-1, 28)))
            {
                var cam = _bridge.Camera;
                if (cam != null)
                {
                    // Convert the marker's forward direction back to camera yaw/pitch
                    // (inverse of Front = (sin(yaw)cos(pitch), sin(pitch), cos(yaw)cos(pitch))).
                    var fwd = editorObj.Forward;
                    float pitchDeg = MathF.Asin(Math.Clamp(fwd.Y, -1f, 1f)) * 180f / MathF.PI;
                    float yawDeg = MathF.Atan2(fwd.X, fwd.Z) * 180f / MathF.PI;
                    cam.SetEditorViewTransform(editorObj.Position, yawDeg, pitchDeg, editorObj.CameraFov);
                    Console.WriteLine($"[Inspector] Previewed from camera marker '{editorObj.Name}' (pos {editorObj.Position})");
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Move the editor camera to this marker's position and look direction (also sets FOV)");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1f),
                "Marker at eye height. Use as the scene's spawn camera later.");
            ImGui.Separator();
        }

        if (editorObj.PrimitiveType == EditorPrimitiveType.Light &&
            ImGui.CollapsingHeader("Light Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var dir = editorObj.LightDirection;
            if (ImGui.DragFloat3("Direction", ref dir, 0.05f))
                editorObj.LightDirection = Vector3.Normalize(dir);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("World direction the light points toward (overrides the editor sun)");

            float intensity = editorObj.LightIntensity;
            if (ImGui.DragFloat("Intensity", ref intensity, 0.05f, 0f, 10f, "%.2f"))
                editorObj.LightIntensity = Math.Max(0f, intensity);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Brightness multiplier applied to the light color");

            float cone = editorObj.LightConeAngle;
            if (ImGui.SliderFloat("Cone Angle", ref cone, 1f, 89f, "%.0f°"))
                editorObj.LightConeAngle = cone;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Spotlight cone half-angle visualized by the light gizmo in the viewport");

            bool showLightGizmo = editorObj.ShowLightGizmo;
            if (ImGui.Checkbox("Show Light Gizmo", ref showLightGizmo))
                editorObj.ShowLightGizmo = showLightGizmo;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show/hide the direction ray + spotlight cone gizmo for this light in the viewport");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.4f, 1f),
                "Color = the object's Color in Visual section below.");
            ImGui.Separator();
        }

        if (editorObj.PrimitiveType == EditorPrimitiveType.Sky &&
            ImGui.CollapsingHeader("Sky Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float tod = editorObj.SkyTimeOfDay;
            if (ImGui.SliderFloat("Time of Day", ref tod, 0f, 24f, "%.1f h"))
                editorObj.SkyTimeOfDay = tod;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hours since midnight (12 = midday, 18 = sunset, 6 = sunrise)");

            // ── Time-of-day animation: play/pause + speed (hours per second) ──
            bool animating = editorObj.SkyTimeAnimSpeed > 0f && !editorObj.SkyTimeAnimPaused;
            if (ImGui.Button(animating ? "⏸ Pause Day/Night" : "▶ Play Day/Night", new Vector2(-1, 26)))
            {
                if (animating)
                    editorObj.SkyTimeAnimPaused = true;
                else
                {
                    editorObj.SkyTimeAnimPaused = false;
                    if (editorObj.SkyTimeAnimSpeed <= 0f)
                        editorObj.SkyTimeAnimSpeed = 1f; // sensible default when first enabled
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Play/pause the day/night cycle — the sun orbits automatically");

            float speed = editorObj.SkyTimeAnimSpeed;
            if (ImGui.SliderFloat("Day Speed", ref speed, 0f, 24f, "%.1f h/s"))
                editorObj.SkyTimeAnimSpeed = speed;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("How many in-game hours pass per real second (0 = static, 24 = full day in 1s)");

            ImGui.Spacing();
            ImGui.Separator();

            // ── Sun position override (pitch/yaw) — null = follow time of day ──
            bool hasSunOverride = editorObj.SkySunPitch.HasValue && editorObj.SkySunYaw.HasValue;
            ImGui.TextDisabled("Sun Position");
            float pitch = editorObj.SkySunPitch ?? 30f;
            float yaw = editorObj.SkySunYaw ?? 180f;
            ImGui.BeginDisabled(!hasSunOverride);
            if (ImGui.SliderFloat("Sun Pitch", ref pitch, -90f, 90f, "%.1f°"))
                editorObj.SkySunPitch = pitch;
            if (ImGui.SliderFloat("Sun Yaw", ref yaw, 0f, 360f, "%.1f°"))
                editorObj.SkySunYaw = yaw;
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered() && !hasSunOverride)
                ImGui.SetTooltip("Sun follows the Time of Day. Adjust pitch/yaw to aim it manually.");
            if (ImGui.Button(hasSunOverride ? "Reset to Time of Day" : "Override Sun Position", new Vector2(-1, 0)))
            {
                if (hasSunOverride)
                {
                    editorObj.SkySunPitch = null;
                    editorObj.SkySunYaw = null;
                }
                else
                {
                    // Seed the override from the current time-of-day sun position so the user
                    // keeps the sun where it already is, then fine-tunes pitch/yaw. Mirrors the
                    // SceneManager WorldTime→sunAngle math (sunAngle = hours/24*2π - π/2).
                    float hours = Math.Clamp(editorObj.SkyTimeOfDay, 0f, 24f);
                    float sunAngle = (hours / 24f) * (MathF.PI * 2f) - (MathF.PI * 0.5f);
                    var sun = new Vector3(MathF.Cos(sunAngle), MathF.Sin(sunAngle), 0.3f);
                    sun = Vector3.Normalize(sun);
                    // Convert to pitch/yaw using the same convention as the sky override:
                    // sun = (sin yaw cos pitch, sin pitch, cos yaw cos pitch)
                    float seedPitch = MathF.Asin(Math.Clamp(sun.Y, -1f, 1f)) * 180f / MathF.PI;
                    float seedYaw = MathF.Atan2(sun.X, sun.Z) * 180f / MathF.PI;
                    editorObj.SkySunPitch = seedPitch;
                    editorObj.SkySunYaw = seedYaw;
                }
            }

            ImGui.Spacing();
            ImGui.Separator();

            // ── Cloud coverage ──
            float clouds = editorObj.SkyCloudCoverage;
            if (ImGui.SliderFloat("Cloud Coverage", ref clouds, 0f, 1f, "%.2f"))
                editorObj.SkyCloudCoverage = clouds;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Cloud amount/intensity in the sky (0 = clear, 1 = heavy overcast)");

            // ── Sun brightness ──
            float sunI = editorObj.SkySunIntensity;
            if (ImGui.SliderFloat("Sun Intensity", ref sunI, 0.1f, 3f, "%.2f×"))
                editorObj.SkySunIntensity = sunI;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Sun brightness multiplier applied to the scene light color");

            bool showSkyGizmo = editorObj.ShowSkyGizmo;
            if (ImGui.Checkbox("Show Sky Gizmo", ref showSkyGizmo))
                editorObj.ShowSkyGizmo = showSkyGizmo;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show/hide the horizon circle + sun icon gizmo in the viewport");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1f),
                "Skybox renders in the viewport while this object exists in the scene.");
            ImGui.Separator();
        }

        // ── Visual ──
        if (ImGui.CollapsingHeader("Visual", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var color = editorObj.Color;
            if (ImGui.ColorEdit3("Color", ref color))
            {
                editorObj.Color = color;
                editorObj.MarkDirty();
            }

            string texPath = editorObj.TexturePath ?? "";
            ImGui.Text("Texture Path:");
            if (ImGui.InputText("##tex_path", ref texPath, 512))
            {
                editorObj.TexturePath = string.IsNullOrEmpty(texPath) ? null : texPath;
                editorObj.MarkDirty();
            }

            // ── Drag-drop target for Asset Browser ──
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                {
                    editorObj.TexturePath = AssetBrowserPanel._dragImagePath;
                    editorObj.MarkDirty();
                    Console.WriteLine($"[Inspector] Set TexturePath on '{editorObj.Name}' → {editorObj.TexturePath}");
                    AssetBrowserPanel._dragImagePath = null;
                }
                ImGui.EndDragDropTarget();
            }
        }

        // ── Flags ──
        if (ImGui.CollapsingHeader("Flags", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool vis = editorObj.IsVisible;
            if (ImGui.Checkbox("Visible", ref vis))
                editorObj.IsVisible = vis;

            bool shadow = editorObj.CastShadow;
            if (ImGui.Checkbox("Cast Shadow", ref shadow))
                editorObj.CastShadow = shadow;
        }
    }

    /// <summary>Render the advanced terrain settings for a Plane object: heightmap,
    /// 4 layer textures (air/dirt/grass/snow), slope, chunk size and height bands.
    /// Every change marks the object dirty so the terrain mesh rebuilds.</summary>
    private unsafe void RenderTerrainInspector(EditorObject editorObj)
    {
        if (!ImGui.CollapsingHeader("Terrain", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        // Planes are always advanced heightmapped terrain — the old "Advanced Terrain"
        // toggle was removed (a Plane can no longer be switched back to a flat plane).
        ImGui.TextColored(new Vector4(0.3f, 0.9f, 1.0f, 1f),
            "Plane = advanced heightmapped terrain (always on).");
        ImGui.Spacing();
        ImGui.Separator();
            // ── Heightmap ──
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Heightmap");
            string hmPath = editorObj.TerrainHeightmapPath;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##ter_hm", ref hmPath, 512))
            {
                editorObj.TerrainHeightmapPath = hmPath;
                editorObj.MarkDirty();
            }

            // Drag-drop from Asset Browser
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                {
                    editorObj.TerrainHeightmapPath = AssetBrowserPanel._dragImagePath;
                    editorObj.MarkDirty();
                    Console.WriteLine($"[Inspector] Set terrain heightmap → {editorObj.TerrainHeightmapPath}");
                    AssetBrowserPanel._dragImagePath = null;
                }
                ImGui.EndDragDropTarget();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Heightmap file (.raw 8-bit or any image). White = high, black = low.");

            // Pick from bundled maps
            string[] maps = ScanMapsFolder();
            if (maps.Length > 0)
            {
                int mapIdx = Array.FindIndex(maps, m =>
                    string.Equals(m, Path.GetFileName(editorObj.TerrainHeightmapPath), StringComparison.OrdinalIgnoreCase));
                if (mapIdx < 0) mapIdx = 0;
                if (ImGui.Combo("##ter_hm_combo", ref mapIdx, maps, maps.Length))
                {
                    string mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Artifacts", "Maps");
                    editorObj.TerrainHeightmapPath = Path.Combine(mapsDir, maps[mapIdx]);
                    editorObj.MarkDirty();
                }
            }

            // Generate a fresh heightmap with MapLoader's procedural generator
            if (ImGui.Button("🎲 Generate Random Heightmap", new Vector2(-1, 24)))
            {
                try
                {
                    string mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Artifacts", "Maps");
                    Directory.CreateDirectory(mapsDir);
                    string path = Path.Combine(mapsDir, $"editor_terrain_{DateTime.Now:HHmmss}.raw");
                    DarkEngine3D_gl_csharp.Engine.Terrains.MapLoader.GeneratePhotorealHeightmap(path, 257);
                    editorObj.TerrainHeightmapPath = path;
                    editorObj.MarkDirty();
                    Console.WriteLine($"[Inspector] Generated terrain heightmap → {path}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Inspector] Heightmap generation failed: {ex.Message}");
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Create a new procedural alpine heightmap and use it for this terrain.");

            ImGui.Spacing();
            ImGui.Separator();

            // ── Mesh detail ──
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Mesh");
            int chunk = editorObj.TerrainChunkSize;
            if (ImGui.SliderInt("Chunk Size", ref chunk, 4, 128))
            {
                editorObj.TerrainChunkSize = chunk;
                editorObj.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Grid resolution per side. 32 ≈ 2k triangles, 128 ≈ 32k triangles.");

            float hScale = editorObj.TerrainHeightScale;
            if (ImGui.DragFloat("Height Scale", ref hScale, 0.5f, 1f, 500f, "%.1f"))
            {
                editorObj.TerrainHeightScale = Math.Max(1f, hScale);
                editorObj.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Vertical exaggeration — full-white heightmap pixels reach this height.");

            float slope = editorObj.TerrainSlopeThreshold;
            if (ImGui.SliderFloat("Slope", ref slope, 0.02f, 0.98f, "%.2f"))
            {
                editorObj.TerrainSlopeThreshold = slope;
                editorObj.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Steepness threshold: steeper slopes show the dirt/rock layer (like cliffs).");

            float tiling = editorObj.TerrainTexTiling;
            if (ImGui.SliderFloat("Texture Tiling", ref tiling, 0.05f, 2.0f, "%.2f"))
            {
                editorObj.TerrainTexTiling = tiling;
                editorObj.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("World-space texture repetition frequency.");

            ImGui.Spacing();
            ImGui.Separator();

            // ── Brush painting (viewport tool) ──
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Brush (Viewport)");
            ImGui.TextDisabled("Tools: ⛰ Sculpt = raise/lower, 🌀 Smooth,\n⏹ Flatten = level to first-click height, 🎨 Paint.\nLeft-drag = apply · Ctrl = reverse · Shift = fine control.");

            float bSize = editorObj.TerrainBrushSize;
            if (ImGui.DragFloat("Brush Size", ref bSize, 0.1f, 0.5f, 50f, "%.1f"))
                editorObj.TerrainBrushSize = Math.Clamp(bSize, 0.5f, 50f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Brush radius in world units (shared by all brush tools). Ctrl+scroll in the viewport resizes it.");

            float bStr = editorObj.TerrainBrushStrength;
            if (ImGui.DragFloat("Brush Strength", ref bStr, 0.005f, 0.01f, 2f, "%.3f"))
                editorObj.TerrainBrushStrength = Math.Clamp(bStr, 0.01f, 2f);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("⛰ Height added/removed per 60fps-frame (world units); 🌀/⏹ blend amount per stamp (0..1).\nHold Shift in the viewport for 15% strength (fine strokes).");

            float bSoft = editorObj.TerrainBrushSoftness;
            if (ImGui.SliderFloat("Brush Softness", ref bSoft, 0f, 1f, "%.2f"))
                editorObj.TerrainBrushSoftness = bSoft;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Falloff amount: 0 = hard edge, 1 = the full falloff curve below.");

            // Falloff curve presets (Unreal-style brush falloff selection)
            string[] falloffNames = ["Linear", "Smooth", "Sharp", "Spherical", "Soft"];
            int falloffIdx = Math.Clamp(editorObj.TerrainBrushFalloff, 0, falloffNames.Length - 1);
            if (ImGui.Combo("Falloff Curve", ref falloffIdx, falloffNames, falloffNames.Length))
                editorObj.TerrainBrushFalloff = falloffIdx;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("How the brush weight falls off toward its edge (like Unreal's brush falloff presets).\nLinear = cone · Smooth = round center · Sharp = strong center · Spherical = classic · Soft = gentle edges.");

            // ── Layer paint (🎨 brush) ──
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Layer Paint (🎨 Brush)");
            string[] layerNames = ["1 · Air", "2 · Tanah", "3 · Rumput", "4 · Salju"];
            int layerIdx = Math.Clamp(editorObj.TerrainPaintLayerIndex, 0, 3);
            if (ImGui.Combo("Paint Layer", ref layerIdx, layerNames, layerNames.Length))
            {
                editorObj.TerrainPaintLayerIndex = layerIdx;
                _bridge.TerrainPaintLayerIndex = layerIdx; // sync the viewport tool
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Layer drawn by the 🎨 Paint brush. Pick the layer in the viewport toolbar too.");

            float pStr = editorObj.TerrainPaintStrength;
            if (ImGui.SliderFloat("Paint Strength", ref pStr, 0.05f, 1f, "%.2f"))
                editorObj.TerrainPaintStrength = pStr;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Weight added to the layer per 🎨 brush stamp (0..1). More stamps = stronger paint.");

            if (ImGui.Button("🧹 Clear Layer Paint", new Vector2(-1, 24)))
            {
                // Record undo (before = painted splat, after = cleared) so Ctrl+Z restores.
                var beforeSplat = editorObj.CaptureTerrainSplat();
                editorObj.ClearTerrainLayerPaint();
                var afterSplat = editorObj.CaptureTerrainSplat();
                if (beforeSplat != null && afterSplat != null && beforeSplat.Length == afterSplat.Length)
                    _bridge.OnTerrainLayerPainted?.Invoke(editorObj, beforeSplat, afterSplat);
                Console.WriteLine($"[Inspector] Cleared layer paint on '{editorObj.Name}' (back to auto texturing)");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(editorObj.TerrainSplatIsModified
                    ? "Remove ALL manual layer paint — terrain returns to automatic height+slope texturing."
                    : "No manual layer paint on this terrain yet.");

            // ── Save painted heights back to a .raw file ──
            if (ImGui.Button("💾 Save Painted Heightmap", new Vector2(-1, 24)))
            {
                if (editorObj.TerrainIsModified)
                {
                    string path = editorObj.TerrainHeightmapPath;
                    if (string.IsNullOrEmpty(path) || !Path.HasExtension(path))
                    {
                        string mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Artifacts", "Maps");
                        Directory.CreateDirectory(mapsDir);
                        path = Path.Combine(mapsDir, $"painted_{DateTime.Now:HHmmss}.raw");
                    }
                    else if (!path.EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
                    {
                        path = Path.ChangeExtension(path, ".raw");
                    }
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
                        if (editorObj.SaveTerrainHeightmap(path))
                            Console.WriteLine($"[Inspector] Saved painted heightmap → {path}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Inspector] Failed to save heightmap: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("[Inspector] No painted changes to save.");
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Write the current painted heights back to a .raw file and point this terrain at it.\nScene saves (.ing) already persist painted heights automatically.");

            ImGui.Spacing();
            ImGui.Separator();

            // ── Height bands (normalized 0..1) ──
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Height Bands (0..1)");
            float airTop = editorObj.TerrainLayerAirTop;
            if (ImGui.DragFloat("Air Top", ref airTop, 0.005f, 0f, 1f, "%.3f"))
            {
                editorObj.TerrainLayerAirTop = Math.Clamp(airTop, 0f, 1f);
                editorObj.MarkDirty();
            }
            float dirtTop = editorObj.TerrainLayerDirtTop;
            if (ImGui.DragFloat("Dirt Top", ref dirtTop, 0.005f, 0f, 1f, "%.3f"))
            {
                editorObj.TerrainLayerDirtTop = Math.Clamp(dirtTop, 0f, 1f);
                editorObj.MarkDirty();
            }
            float grassTop = editorObj.TerrainLayerGrassTop;
            if (ImGui.DragFloat("Grass Top", ref grassTop, 0.005f, 0f, 1f, "%.3f"))
            {
                editorObj.TerrainLayerGrassTop = Math.Clamp(grassTop, 0f, 1f);
                editorObj.MarkDirty();
            }
            float snowTop = editorObj.TerrainLayerSnowTop;
            if (ImGui.DragFloat("Snow Top", ref snowTop, 0.005f, 0f, 1f, "%.3f"))
            {
                editorObj.TerrainLayerSnowTop = Math.Clamp(snowTop, 0f, 1f);
                editorObj.MarkDirty();
            }

            ImGui.Spacing();
            ImGui.Separator();

            // ── 5 layer textures (air, tanah, rumput, salju, slope) ──
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Layer Textures (1–5)");
            ImGui.TextDisabled("Empty = solid color fallback. 5 · Slope = steep cliffs.");

            DrawTerrainLayerField(editorObj, "1 · Air",
                () => editorObj.TerrainTextureAirPath,
                v => editorObj.TerrainTextureAirPath = v,
                new Vector4(0.25f, 0.55f, 0.9f, 1f));
            DrawTerrainLayerField(editorObj, "2 · Tanah",
                () => editorObj.TerrainTextureDirtPath,
                v => editorObj.TerrainTextureDirtPath = v,
                new Vector4(0.65f, 0.5f, 0.3f, 1f));
            DrawTerrainLayerField(editorObj, "3 · Rumput",
                () => editorObj.TerrainTextureGrassPath,
                v => editorObj.TerrainTextureGrassPath = v,
                new Vector4(0.3f, 0.7f, 0.35f, 1f));
            DrawTerrainLayerField(editorObj, "4 · Salju",
                () => editorObj.TerrainTextureSnowPath,
                v => editorObj.TerrainTextureSnowPath = v,
                new Vector4(0.9f, 0.92f, 0.98f, 1f));
            DrawTerrainLayerField(editorObj, "5 · Slope (Lereng)",
                () => editorObj.TerrainTextureSlopePath,
                v => editorObj.TerrainTextureSlopePath = v,
                new Vector4(0.55f, 0.45f, 0.38f, 1f));
    }

    /// <summary>Scan Artifacts/Maps for bundled heightmaps (.raw / images).</summary>
    private static string[] ScanMapsFolder()
    {
        try
        {
            string mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Artifacts", "Maps");
            if (!Directory.Exists(mapsDir)) return [];
            var files = Directory.GetFiles(mapsDir)
                .Where(f => f.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .OrderBy(n => n)
                .ToArray();
            return files;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Draw a single terrain layer texture field (label chip + path + drag-drop + clear).
    /// Uses getter/setter delegates so callers can pass property-backed paths.</summary>
    private static unsafe void DrawTerrainLayerField(EditorObject editorObj, string label,
        Func<string> getter, Action<string> setter, Vector4 chipColor)
    {
        ImGui.Spacing();
        ImGui.TextColored(chipColor, label);
        string path = getter() ?? "";
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText($"##ter_tex_{label}", ref path, 512))
        {
            setter(path);
            editorObj.MarkDirty();
        }

        // Drag-drop from Asset Browser
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
            if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
            {
                setter(AssetBrowserPanel._dragImagePath);
                editorObj.MarkDirty();
                Console.WriteLine($"[Inspector] Set terrain layer '{label}' → {AssetBrowserPanel._dragImagePath}");
                AssetBrowserPanel._dragImagePath = null;
            }
            ImGui.EndDragDropTarget();
        }

        ImGui.SameLine();
        bool hasTexture = !string.IsNullOrEmpty(path) && File.Exists(path);
        if (ImGui.Button($"X##ter_tex_clear_{label}", new Vector2(24, 0)) && hasTexture)
        {
            setter("");
            editorObj.MarkDirty();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Clear texture (uses solid color)");

        if (hasTexture)
            ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.5f, 1f), $"✓ {Path.GetFileName(path)}");
        else
            ImGui.TextDisabled("Drop image here or type path");
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
