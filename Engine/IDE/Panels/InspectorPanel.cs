using DarkEngine3D_gl_csharp.Engine.Libs;
using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Scene;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Inspector panel  shows properties of the selected object or UI button.
/// Supports editing transform, viewing health, editing button properties, etc.
/// </summary>
public class InspectorPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    //  UI editing state 
    private System.Numerics.Vector2 _editVec2 = new();

    //  Per-texture settings: which slot/layer is being edited. Reset when the selected
    //    object changes so a fresh selection always starts on the first layer. 
    private EditorObject? _texSettingsObj;
    private int _texSlotIdx;
    private int _terrainLayerIdx;
    private int _dynActiveLayerIdx = 0; // active layer in dynamic system

    //  Cached font list (scanned once) 
    private string[]? _availableFonts;
    private bool _fontsScanned = false;

    //  Element type labels (mirrors UIElementType order) 
    private static readonly string[] ElementTypeNames =
        ["Scene", "Container", "Button", "Label", "SliderNumber", "SliderText", "Checkbox", "Dropdown", "TextBox"];

    public InspectorPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Inspector", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;

        ImGui.Begin("Inspector", ref _visible);

        // Use a child region for scrollable content  ImGui handles scroll-on-hover
        // automatically for child regions, so no click is needed.
        var avail = ImGui.GetContentRegionAvail();
        ImGui.BeginChild("InspectorScroll", avail, ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);

        var uiElem = _bridge.SelectedUIElement;
        var obj = _bridge.SelectedObject;
        var agent = _bridge.SelectedAgent;
        var editorObj = _bridge.SelectedEditorObject;

        //  "Select Scene" button  shown when any object is selected, allows quick jump
        //     to scene render properties (BackgroundColor, Wireframe, etc.).
        bool hasSelection = editorObj != null || (uiElem != null && uiElem.Type != UIElementType.Scene) || obj != null;
        if (hasSelection)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.35f, 0.55f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.50f, 0.75f, 1f));
            if (ImGui.Button(" Select Scene", new Vector2(-1, 26)))
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

        ImGui.EndChild(); // InspectorScroll
        ImGui.End(); // Inspector
    }

    private unsafe void RenderUIElementInspector(UIElement elem)
    {
        // Unique ID scope per element instance  prevents ImGui ID collisions
        // when switching between elements (all InputText/DragFloat/Combo IDs are
        // scoped under elem.InstanceId, so each element gets its own ID space).
        ImGui.PushID(elem.InstanceId);

        bool isSceneType = elem.Type == UIElementType.Scene;

        // 
        //  Element Identity
        // 
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

        //  For Scene type, ONLY show Element + Children, skip everything else 
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

        // 
        //  Type-Specific Properties (Moved to top)
        // 
        RenderTypeSpecificProperties(elem);

        // 
        //  Image (replaces Text & Font when ImagePath is set)
        // 
        bool hasImage = !string.IsNullOrEmpty(elem.ImagePath);
        if (ImGui.CollapsingHeader("Image", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Image path with drag-drop target
            string imgPath = elem.ImagePath;
            ImGui.Text("Image Path:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##img_path", ref imgPath, 512))
                elem.ImagePath = imgPath;

            //  Drag-drop target for Asset Browser 
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

            //  Image sizing mode 
            string[] imgModes = ["Stretch", "Zoom", "Fill"];
            int imgModeIdx = (int)elem.ImageMode;
            if (ImGui.Combo("Image Mode", ref imgModeIdx, imgModes, imgModes.Length))
                elem.ImageMode = (ImageMode)imgModeIdx;

            // Image preview indicator
            if (hasImage)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.5f, 1f), $" Image: {Path.GetFileName(elem.ImagePath)}");
                ImGui.TextDisabled($"Mode: {elem.ImageMode}");
            }
            else
            {
                ImGui.TextDisabled("Drop image from Asset Browser");
                ImGui.TextDisabled("or type path above.");
            }

            ImGui.Separator();

            //  Fit to Window button 
            if (ImGui.Button(" Fit to Window", new Vector2(-1, 30)))
            {
                elem.X = 0;
                elem.Y = 0;
                elem.Width = 1920;
                elem.Height = 1080;
                Console.WriteLine($"[Inspector] Fit to Window: '{elem.Name}' → (0,0) [19201080]");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Resize element to fill the entire window (19201080)");

            //  Fallback Label (shown when image can't be loaded)  independent from main Text 
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

        // 
        //  Text & Font (shown only when no image)
        // 
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

        // 
        //  Transform (Position & Size)
        // 
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

        // 
        //  Colors (Normal + Hover)
        // 
        if (ImGui.CollapsingHeader("Colors", ImGuiTreeNodeFlags.DefaultOpen))
        {
            //  Normal state 
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

            //  Use Hover toggle 
            bool useHover = elem.UseHover;
            if (ImGui.Checkbox("Use Hover", ref useHover))
                elem.UseHover = useHover;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When enabled, this element shows different colors on mouse hover. When disabled, normal colors are always used.");

            //  Hover state (greyed out when Use Hover is disabled) 
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

        // 
        //  Behaviors (Click + Hover)
        // 
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

            //  Sub-parameter: overlay name (only when "overlay" type selected) 
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

            //  Sub-parameter: scene name (only when "scene" type selected) 
            if (string.Equals(currentType, "scene", StringComparison.OrdinalIgnoreCase))
            {
                ImGui.Spacing();
                ImGui.Indent();
                ImGui.Text("Target Scene:");
                ImGui.SetNextItemWidth(-1);

                string[] scenes = _bridge.AvailableSceneNames;
                if (scenes.Length == 0)
                {
                    ImGui.TextDisabled("(no scenes  add scenes in Scene Manager)");
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

            //  Hover behaviors (simplified: just show type combo, no sub-params) 
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

        // 
        //  Visibility & Flags
        // 
        if (ImGui.CollapsingHeader("Visibility", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool vis = elem.IsVisible;
            if (ImGui.Checkbox("Visible", ref vis))
                elem.IsVisible = vis;

            // Show a small preview chip
            if (vis)
                ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.4f, 1f), " Visible");
            else
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), " Hidden");

            //  Opacity / Transparency 
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

            //  Auto-fill window (for overlay/background elements) 
            bool autoFill = elem.AutoFillWindow;
            if (ImGui.Checkbox("Auto-fill Window", ref autoFill))
                elem.AutoFillWindow = autoFill;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When enabled, this element automatically fills the entire viewport (X=0, Y=0, W=viewport, H=viewport)");

            //  Auto-center (for overlays/dialogs) 
            bool autoCenter = elem.AutoCenter;
            if (ImGui.Checkbox("Auto-center", ref autoCenter))
                elem.AutoCenter = autoCenter;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When enabled, this element is automatically centered in the viewport");
        }

        // 
        //  Children list
        // 
        if (elem.Children.Count > 0 && ImGui.CollapsingHeader($"Children ({elem.Children.Count})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < elem.Children.Count; i++)
            {
                var child = elem.Children[i];
                // Clickable child entry  clicking selects it in the hierarchy
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
                        ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.4f, 1f), " Checked");
                    else
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), " Unchecked");

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
                            ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.4f, 1f), " Selected");
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
                            ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.4f, 1f), " Selected");
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
            // No scene selected but there are editor scenes  pick the first one
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

        // 
        //  Render Properties (per-scene)
        // 
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

            //  Background Color 
            var bgColor = renderProps.BackgroundColor;
            if (ImGui.ColorEdit3("Background Color", ref bgColor, ImGuiColorEditFlags.NoInputs))
            {
                renderProps.BackgroundColor = bgColor;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("GL.ClearColor  background color when rendering this scene");

            ImGui.Spacing();

            //  VSync 
            bool vsync = renderProps.VSync;
            if (ImGui.Checkbox("VSync", ref vsync))
            {
                renderProps.VSync = vsync;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Enable/disable vertical sync for this scene");

            ImGui.Spacing();

            //  Face Culling 
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

            //  Front Face Winding 
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

            //  Wireframe Mode 
            bool wireframe = renderProps.WireframeMode;
            if (ImGui.Checkbox("Wireframe Mode", ref wireframe))
            {
                renderProps.WireframeMode = wireframe;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Render polygons as lines (wireframe)");

            ImGui.Spacing();

            //  Depth Test 
            bool depthTest = renderProps.DepthTest;
            if (ImGui.Checkbox("Depth Test", ref depthTest))
            {
                renderProps.DepthTest = depthTest;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Enable/disable depth testing");

            //  Blending 
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

            //  Apply button 
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

            // 
            //  Fog (global  Config.FogSettings, applies to every scene & object)
            // 
            if (ImGui.CollapsingHeader("Fog", ImGuiTreeNodeFlags.DefaultOpen))
            {
                bool fogChanged = false;

                if (ImGui.Checkbox("Enable Fog", ref Config.FogSettings.Enabled))
                    fogChanged = true;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Master fog switch (same as the F key quick-toggle in-game).");

                string[] fogModes = ["Linear", "Exponential", "Exponential + Height"];
                int fogMode = Math.Clamp(Config.FogSettings.Mode - 1, 0, 2);
                if (ImGui.Combo("Mode", ref fogMode, fogModes, fogModes.Length))
                {
                    Config.FogSettings.Mode = fogMode + 1;
                    fogChanged = true;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Linear = fades between Start and End distance.\nExponential = smooth density falloff.\nExp + Height = the original terrain fog with height blending.");

                if (ImGui.Checkbox("Use Sky Color", ref Config.FogSettings.UseSkyColor))
                    fogChanged = true;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("When enabled the fog color follows the sun/horizon; disable to pick a manual color.");

                if (!Config.FogSettings.UseSkyColor)
                {
                    var fogColor = Config.FogSettings.Color;
                    if (ImGui.ColorEdit3("Fog Color", ref fogColor))
                    {
                        Config.FogSettings.Color = fogColor;
                        fogChanged = true;
                    }
                }

                float density = Config.FogSettings.Density;
                if (ImGui.SliderFloat("Density", ref density, 0f, 0.05f, "%.4f"))
                {
                    Config.FogSettings.Density = density;
                    fogChanged = true;
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Fog thickness (Exponential / Exp modes).");

                if (Config.FogSettings.Mode == 1)
                {
                    float start = Config.FogSettings.StartDistance;
                    if (ImGui.DragFloat("Start Distance", ref start, 1f, 0f, 2000f))
                    {
                        Config.FogSettings.StartDistance = Math.Max(0f, start);
                        fogChanged = true;
                    }
                    float end = Config.FogSettings.EndDistance;
                    if (ImGui.DragFloat("End Distance", ref end, 1f, 0f, 5000f))
                    {
                        Config.FogSettings.EndDistance = Math.Max(Config.FogSettings.StartDistance + 1f, end);
                        fogChanged = true;
                    }
                }

                if (Config.FogSettings.Mode == 3)
                {
                    float height = Config.FogSettings.Height;
                    if (ImGui.DragFloat("Fog Height", ref height, 0.5f, -100f, 500f))
                    {
                        Config.FogSettings.Height = height;
                        fogChanged = true;
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("World Y below which the fog is densest (height fog).");

                    float range = Config.FogSettings.HeightRange;
                    if (ImGui.DragFloat("Height Range", ref range, 0.5f, 1f, 500f))
                    {
                        Config.FogSettings.HeightRange = Math.Max(1f, range);
                        fogChanged = true;
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("How quickly the height fog fades above Fog Height.");
                }

                if (fogChanged)
                    Config.FogSettings.Persist();
            }

            // 
            //  Selection Highlight Colors (global IDE settings)
            // 
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

            // 
            //  Editor Settings (fly mode sensitivity, speed)
            // 
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

            //  Reset to defaults button 
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
    /// Uses ImGui ColorEdit3 with NoInputs flag  click the colored square to open the
    /// picker popup, then use the eyedropper pipette icon to sample from screen.
    /// The <paramref name="idSuffix"/> ensures unique ImGui IDs when multiple
    /// pickers share the same label (e.g. "N" for Normal, "H" for Hover).
    /// When the color differs from <paramref name="defaultColor"/>, a small  reset
    /// button appears to restore the default value.</summary>
    private static void DrawColorPicker(string label, string idSuffix, Vector3 color, Action<Vector3> onChanged, Vector3? defaultColor = null)
    {
        ImGui.Text(label);
        ImGui.SameLine();

        //  Reset button  only show when color differs from default 
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
            if (ImGui.SmallButton($"##reset_{label}_{idSuffix}"))
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
        //  Multi-selection indicator 
        if (_bridge.SelectedEditorObjects.Count > 1)
        {
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 1.0f, 1f),
                $"▲ {_bridge.SelectedEditorObjects.Count} objects selected (editing primary '{editorObj.Name}')");
            ImGui.Separator();
        }

        //  Identity 
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

        //  Transform 
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

        //  Gizmo Pivot 
        if (ImGui.CollapsingHeader("Gizmo Pivot", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool hasPivot = editorObj.GizmoPivotOverride.HasValue;
            ImGui.TextColored(hasPivot
                ? new Vector4(0.3f, 0.85f, 0.4f, 1f)
                : new Vector4(0.6f, 0.6f, 0.6f, 1f),
                hasPivot ? " Custom pivot active" : " Using object position");

            // Pivot world position  falls back to the object's position when no override set.
            // Dragging these inputs activates a custom pivot at the entered world position.
            var pivot = editorObj.GizmoPivotOverride ?? editorObj.Position;
            if (ImGui.DragFloat3("Pivot Position", ref pivot, 0.1f))
            {
                editorObj.GizmoPivotOverride = pivot;
                // Note: no MarkDirty() here  the pivot is a gizmo render position and
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
                ImGui.SetTooltip("Remove the custom pivot  the gizmo follows the object position");
        }

        //  Height overlays (heatmap / contours)  available for ANY editor object;
        // auto-selects the first terrain plane when none is selected (matches the
        // viewport toolbar's Shade/Contours buttons). 
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
                ImGui.SetTooltip("Draw dark contour lines every 10% of the height range  the texture stays\nfully visible while the relief reads clearly. Auto-selects the first terrain if none is selected. Not saved with the scene.");
        }

        //  Terrain properties (Plane only) 
        if (editorObj.PrimitiveType == EditorPrimitiveType.Plane)
        {
            RenderTerrainInspector(editorObj);
        }

        //  Type-specific properties (Camera / Light / Sky) 
        if (editorObj.PrimitiveType == EditorPrimitiveType.Camera &&
            ImGui.CollapsingHeader("Camera Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float fov = editorObj.CameraFov;
            if (ImGui.DragFloat("FOV", ref fov, 0.5f, 10f, 120f, "%.1f"))
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
            //  Preview from this camera: teleport the editor camera to the marker 
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
            //  Light type: Direct (sun-like, parallel) / Point (omnidirectional) /
            //    Spotlight (cone). The type drives which properties are shown below and
            //    how the light affects the scene (see Lights.CollectLocalLights). 
            string[] lightTypes = ["Direct (Sun)", "Point", "Spotlight"];
            int lightTypeIdx = (int)editorObj.LightTypeEnum;
            if (ImGui.Combo("Type", ref lightTypeIdx, lightTypes, lightTypes.Length))
            {
                editorObj.LightTypeEnum = (LightType)lightTypeIdx;
                Console.WriteLine($"[Inspector] '{editorObj.Name}' light type → {editorObj.LightTypeEnum}");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Direct = parallel rays like the sun (drives the scene's global light + shadows).\nPoint = omnidirectional from the marker position with distance falloff.\nSpotlight = cone-shaped beam with angle + distance falloff.");

            //  Per-type properties 
            if (editorObj.LightTypeEnum is LightType.Direct or LightType.Spotlight)
            {
                var dir = editorObj.LightDirection;
                if (ImGui.DragFloat3("Direction", ref dir, 0.05f))
                    editorObj.LightDirection = Vector3.Normalize(dir);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("World direction the light points toward (Direct overrides the editor sun)");
            }

            if (editorObj.LightTypeEnum is LightType.Point or LightType.Spotlight)
            {
                float range = editorObj.LightPointRadius;
                if (ImGui.DragFloat("Range", ref range, 0.5f, 1f, 500f, "%.1f"))
                    editorObj.LightPointRadius = Math.Max(1f, range);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Distance (world units) the light reaches before fading out (distance falloff)");
            }

            if (editorObj.LightTypeEnum == LightType.Spotlight)
            {
                float cone = editorObj.LightConeAngle;
                if (ImGui.SliderFloat("Cone Angle", ref cone, 1f, 89f, "%.0f"))
                    editorObj.LightConeAngle = cone;
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Spotlight cone half-angle  how wide the beam spreads");
            }

            float intensity = editorObj.LightIntensity;
            if (ImGui.DragFloat("Intensity", ref intensity, 0.05f, 0f, 10f, "%.2f"))
                editorObj.LightIntensity = Math.Max(0f, intensity);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Brightness multiplier applied to the light color");

            bool showLightGizmo = editorObj.ShowLightGizmo;
            if (ImGui.Checkbox("Show Light Gizmo", ref showLightGizmo))
                editorObj.ShowLightGizmo = showLightGizmo;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show/hide the light gizmo in the viewport (beam / sphere / cone per type)");

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.4f, 1f),
                "Light color = the object's Color in the Visual section below.");
            ImGui.Separator();
        }

        if (editorObj.PrimitiveType == EditorPrimitiveType.Sky &&
            ImGui.CollapsingHeader("Sky Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            //  Legacy Time of Day 
            float tod = editorObj.SkyTimeOfDay;
            if (ImGui.SliderFloat("Time of Day", ref tod, 0f, 24f, "%.1f h"))
                editorObj.SkyTimeOfDay = tod;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hours since midnight (12 = midday, 18 = sunset, 6 = sunrise)");

            //  Time-of-day animation: play/pause + speed 
            bool animating = editorObj.SkyTimeAnimSpeed > 0f && !editorObj.SkyTimeAnimPaused;
            if (ImGui.Button(animating ? " Pause Day/Night" : " Play Day/Night", new Vector2(-1, 26)))
            {
                if (animating)
                    editorObj.SkyTimeAnimPaused = true;
                else
                {
                    editorObj.SkyTimeAnimPaused = false;
                    if (editorObj.SkyTimeAnimSpeed <= 0f)
                        editorObj.SkyTimeAnimSpeed = 1f;
                    editorObj.SkySunPitch = null;
                    editorObj.SkySunYaw = null;
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Play/pause the day/night cycle  the sun orbits automatically");

            float speed = editorObj.SkyTimeAnimSpeed;
            if (ImGui.SliderFloat("Day Speed", ref speed, 0f, 24f, "%.1f h/s"))
                editorObj.SkyTimeAnimSpeed = speed;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("How many in-game hours pass per real second (0 = static, 24 = full day in 1s)");

            ImGui.Spacing();
            ImGui.Separator();

            //  Sun position override 
            bool hasSunOverride = editorObj.SkySunPitch.HasValue && editorObj.SkySunYaw.HasValue;
            ImGui.TextDisabled("Sun Position");
            float pitch = editorObj.SkySunPitch ?? 30f;
            float yaw = editorObj.SkySunYaw ?? 180f;
            ImGui.BeginDisabled(!hasSunOverride);
            if (ImGui.SliderFloat("Sun Pitch", ref pitch, -90f, 90f, "%.1f"))
                editorObj.SkySunPitch = pitch;
            if (ImGui.SliderFloat("Sun Yaw", ref yaw, 0f, 360f, "%.1f"))
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
                    float hours = Math.Clamp(editorObj.SkyTimeOfDay, 0f, 24f);
                    float sunAngle = (hours / 24f) * (MathF.PI * 2f) - (MathF.PI * 0.5f);
                    var sun = new Vector3(MathF.Cos(sunAngle), MathF.Sin(sunAngle), 0.3f);
                    sun = Vector3.Normalize(sun);
                    float seedPitch = MathF.Asin(Math.Clamp(sun.Y, -1f, 1f)) * 180f / MathF.PI;
                    float seedYaw = MathF.Atan2(sun.X, sun.Z) * 180f / MathF.PI;
                    editorObj.SkySunPitch = seedPitch;
                    editorObj.SkySunYaw = seedYaw;
                }
            }

            ImGui.Spacing();
            ImGui.Separator();

            //  Cloud coverage 
            float clouds = editorObj.SkyCloudCoverage;
            if (ImGui.SliderFloat("Cloud Coverage", ref clouds, 0f, 1f, "%.2f"))
                editorObj.SkyCloudCoverage = clouds;

            //  Sun brightness 
            float sunI = editorObj.SkySunIntensity;
            if (ImGui.SliderFloat("Sun Intensity", ref sunI, 0.1f, 3f, "%.2f"))
                editorObj.SkySunIntensity = sunI;

            bool showSkyGizmo = editorObj.ShowSkyGizmo;
            if (ImGui.Checkbox("Show Sky Gizmo", ref showSkyGizmo))
                editorObj.ShowSkyGizmo = showSkyGizmo;

            ImGui.Spacing();
            ImGui.Separator();

            // 
            // NEW 3-TYPE SKY SYSTEM
            // 
            var skySettings = editorObj.SkySettings;

            //  Sky Type Selector 
            int skyTypeIdx = (int)skySettings.Type;
            if (ImGui.Combo("Sky Type", ref skyTypeIdx, "Procedural\0Skybox\0Dome\0"))
                skySettings.Type = (SkyType)skyTypeIdx;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Choose sky rendering mode: Procedural (realtime), Skybox (6 textures), Dome (panoramic)");

            ImGui.Spacing();

            //  Randomize Button 
            if (skySettings.Type == SkyType.Procedural)
            {
                if (ImGui.Button(" Randomize All Values", new Vector2(-1, 30)))
                    skySettings.Randomize();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Randomize all procedural sky parameters for creative exploration");
                ImGui.Spacing();
            }

            //  SKYBOX SETTINGS 
            if (skySettings.Type == SkyType.Skybox &&
                ImGui.CollapsingHeader("Skybox Textures", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextDisabled("6 cubemap face textures (.png, .jpg)");
                ImGui.TextDisabled("Drag and drop from Asset Browser ");
                ImGui.Spacing();

                // Helper: InputText + DragDrop + Clear for each face
                string _sbR = skySettings.SkyboxFaces.Right;
                ImGui.Text("Right (+X):");
                ImGui.SetNextItemWidth(-30);
                if (ImGui.InputText("##sb_right", ref _sbR, 512)) skySettings.SkyboxFaces.Right = _sbR;
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                    { skySettings.SkyboxFaces.Right = AssetBrowserPanel._dragImagePath; AssetBrowserPanel._dragImagePath = null; }
                    ImGui.EndDragDropTarget();
                }
                ImGui.SameLine();
                if (ImGui.Button("X##r", new Vector2(22, 0))) skySettings.SkyboxFaces.Right = "";
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear");

                string _sbL = skySettings.SkyboxFaces.Left;
                ImGui.Text("Left (-X):");
                ImGui.SetNextItemWidth(-30);
                if (ImGui.InputText("##sb_left", ref _sbL, 512)) skySettings.SkyboxFaces.Left = _sbL;
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                    { skySettings.SkyboxFaces.Left = AssetBrowserPanel._dragImagePath; AssetBrowserPanel._dragImagePath = null; }
                    ImGui.EndDragDropTarget();
                }
                ImGui.SameLine();
                if (ImGui.Button("X##l", new Vector2(22, 0))) skySettings.SkyboxFaces.Left = "";
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear");

                string _sbT = skySettings.SkyboxFaces.Top;
                ImGui.Text("Top (+Y):");
                ImGui.SetNextItemWidth(-30);
                if (ImGui.InputText("##sb_top", ref _sbT, 512)) skySettings.SkyboxFaces.Top = _sbT;
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                    { skySettings.SkyboxFaces.Top = AssetBrowserPanel._dragImagePath; AssetBrowserPanel._dragImagePath = null; }
                    ImGui.EndDragDropTarget();
                }
                ImGui.SameLine();
                if (ImGui.Button("X##t", new Vector2(22, 0))) skySettings.SkyboxFaces.Top = "";
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear");

                string _sbB = skySettings.SkyboxFaces.Bottom;
                ImGui.Text("Bottom (-Y):");
                ImGui.SetNextItemWidth(-30);
                if (ImGui.InputText("##sb_bot", ref _sbB, 512)) skySettings.SkyboxFaces.Bottom = _sbB;
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                    { skySettings.SkyboxFaces.Bottom = AssetBrowserPanel._dragImagePath; AssetBrowserPanel._dragImagePath = null; }
                    ImGui.EndDragDropTarget();
                }
                ImGui.SameLine();
                if (ImGui.Button("X##b", new Vector2(22, 0))) skySettings.SkyboxFaces.Bottom = "";
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear");

                string _sbF = skySettings.SkyboxFaces.Front;
                ImGui.Text("Front (+Z):");
                ImGui.SetNextItemWidth(-30);
                if (ImGui.InputText("##sb_front", ref _sbF, 512)) skySettings.SkyboxFaces.Front = _sbF;
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                    { skySettings.SkyboxFaces.Front = AssetBrowserPanel._dragImagePath; AssetBrowserPanel._dragImagePath = null; }
                    ImGui.EndDragDropTarget();
                }
                ImGui.SameLine();
                if (ImGui.Button("X##f", new Vector2(22, 0))) skySettings.SkyboxFaces.Front = "";
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear");

                string _sbK = skySettings.SkyboxFaces.Back;
                ImGui.Text("Back (-Z):");
                ImGui.SetNextItemWidth(-30);
                if (ImGui.InputText("##sb_back", ref _sbK, 512)) skySettings.SkyboxFaces.Back = _sbK;
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                    { skySettings.SkyboxFaces.Back = AssetBrowserPanel._dragImagePath; AssetBrowserPanel._dragImagePath = null; }
                    ImGui.EndDragDropTarget();
                }
                ImGui.SameLine();
                if (ImGui.Button("X##k", new Vector2(22, 0))) skySettings.SkyboxFaces.Back = "";
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear");
                ImGui.Separator();
            }

            //  DOME SETTINGS 
            if (skySettings.Type == SkyType.Dome &&
                ImGui.CollapsingHeader("Dome Settings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextDisabled("Dome sphere with panoramic/equirectangular texture");
                ImGui.TextDisabled("Drag and drop from Asset Browser ");
                string domeTex = skySettings.Dome.TexturePath;
                ImGui.Text("Texture:");
                ImGui.SetNextItemWidth(-30);
                if (ImGui.InputText("##dome_tex", ref domeTex, 512)) skySettings.Dome.TexturePath = domeTex;
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                    { skySettings.Dome.TexturePath = AssetBrowserPanel._dragImagePath; AssetBrowserPanel._dragImagePath = null; }
                    ImGui.EndDragDropTarget();
                }
                ImGui.SameLine();
                if (ImGui.Button("X##dt", new Vector2(22, 0))) skySettings.Dome.TexturePath = "";
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear dome texture");
                float domeRad = skySettings.Dome.Radius;
                if (ImGui.SliderFloat("Radius", ref domeRad, 100f, 2000f, "%.0f")) skySettings.Dome.Radius = domeRad;
                var domeTint = skySettings.Dome.TintColor;
                if (ImGui.ColorEdit3("Tint Color", ref domeTint)) skySettings.Dome.TintColor = domeTint;
                float domeRot = skySettings.Dome.RotationY * (180f / MathF.PI);
                if (ImGui.SliderFloat("Rotation Y", ref domeRot, 0f, 360f, "%.1f"))
                    skySettings.Dome.RotationY = domeRot * (MathF.PI / 180f);

                ImGui.Spacing();
                ImGui.Separator();

                //  Auto Rotate 
                ImGui.TextDisabled("Auto Rotate");
                bool autoRot = skySettings.Dome.AutoRotate;
                if (ImGui.Checkbox("Enable##auto_rot", ref autoRot)) skySettings.Dome.AutoRotate = autoRot;

                ImGui.BeginDisabled(!autoRot);
                float rotSpeed = skySettings.Dome.RotateSpeed;
                if (ImGui.SliderFloat("Speed##dome", ref rotSpeed, 0.1f, 100f, "%.1f /s")) skySettings.Dome.RotateSpeed = rotSpeed;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rotation speed in degrees per second");

                int rotAxis = skySettings.Dome.RotateAxis;
                if (ImGui.Combo("Axis##dome", ref rotAxis, "Horizontal (Y)\0Vertical (X)\0Both (XY)\0"))
                    skySettings.Dome.RotateAxis = rotAxis;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Which axis to rotate around");

                float rotVar = skySettings.Dome.RotateVariation;
                if (ImGui.SliderFloat("Variation##dome", ref rotVar, 0f, 1f, "%.2f")) skySettings.Dome.RotateVariation = rotVar;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Speed variation (0=constant, 1=random)");

                bool pingPong = skySettings.Dome.PingPong;
                if (ImGui.Checkbox("Ping-Pong##dome", ref pingPong)) skySettings.Dome.PingPong = pingPong;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Oscillate back and forth instead of full rotation");

                ImGui.BeginDisabled(!pingPong);
                float ppAmp = skySettings.Dome.PingPongAmplitude;
                if (ImGui.SliderFloat("Amplitude##dome", ref ppAmp, 1f, 180f, "%.1f")) skySettings.Dome.PingPongAmplitude = ppAmp;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Max swing angle in ping-pong mode");
                ImGui.EndDisabled();

                ImGui.EndDisabled();
                ImGui.Separator();
            }

            //  PROCEDURAL REALTIME SETTINGS 
            if (skySettings.Type == SkyType.Procedural)
            {
                //  Sky Presets 
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Sky Presets");
                float btnW = (ImGui.GetContentRegionAvail().X - 4 * ImGui.GetStyle().ItemSpacing.X) / 4f;
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.15f, 0.55f, 0.35f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.7f, 0.45f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.1f, 0.45f, 0.3f, 1f));
                if (ImGui.Button("Reset", new Vector2(btnW, 0))) skySettings.ResetDefaults();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Bright blue sky, clouds OFF, sunrays OFF");
                ImGui.SameLine();
                ImGui.PopStyleColor(3);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.75f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.55f, 0.85f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.15f, 0.35f, 0.65f, 1f));
                if (ImGui.Button("Clear Sky", new Vector2(btnW, 0))) skySettings.ApplyClearSky();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear blue sky, no clouds, minimal haze");
                ImGui.SameLine();
                ImGui.PopStyleColor(3);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.75f, 0.45f, 0.15f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.85f, 0.55f, 0.25f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.65f, 0.35f, 0.10f, 1f));
                if (ImGui.Button("Sunset", new Vector2(btnW, 0))) skySettings.ApplySunset();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Warm golden hour with sun rays");
                ImGui.SameLine();
                ImGui.PopStyleColor(3);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.45f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.35f, 0.35f, 0.55f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.20f, 0.20f, 0.35f, 1f));
                if (ImGui.Button("Night", new Vector2(btnW, 0))) skySettings.ApplyNight();
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Dark sky with moon and stars");
                ImGui.PopStyleColor(3);
                ImGui.Spacing();

                // Randomize + Load from Settings on same row
                float halfW = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.3f, 0.6f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.65f, 0.4f, 0.75f, 1f));
                if (ImGui.Button(" Randomize", new Vector2(halfW, 0))) skySettings.Randomize();
                ImGui.PopStyleColor(2);
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.55f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.65f, 0.3f, 1f));
                bool hasSnapshot = editorObj.SavedSkySettings != null;
                ImGui.BeginDisabled(!hasSnapshot);
                if (ImGui.Button(" Load from Settings", new Vector2(halfW, 0)))
                {
                    if (editorObj.SavedSkySettings != null)
                    {
                        editorObj.SkySettings = editorObj.SavedSkySettings.Clone();
                        Console.WriteLine("[Inspector] Sky settings restored from last save");
                    }
                }
                ImGui.EndDisabled();
                ImGui.PopStyleColor(2);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(hasSnapshot ? "Restore sky settings from last saved snapshot" : "No snapshot available  save the scene first");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                //  Sun 
                if (ImGui.CollapsingHeader("☀ Sun", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    var sun = skySettings.Sun;
                    float sunSize = sun.Size;
                    if (ImGui.SliderFloat("Size##sun", ref sunSize, 0.1f, 3.0f, "%.2f")) sun.Size = sunSize;
                    float sunSoft = sun.Softness;
                    if (ImGui.SliderFloat("Softness##sun", ref sunSoft, 0.0f, 1.0f, "%.2f")) sun.Softness = sunSoft;
                    var sunCol = sun.Color;
                    if (ImGui.ColorEdit3("Color##sun", ref sunCol)) sun.Color = sunCol;
                    float glowInt = sun.GlowIntensity;
                    if (ImGui.SliderFloat("Glow Intensity##sun", ref glowInt, 0f, 3.0f, "%.2f")) sun.GlowIntensity = glowInt;
                    ImGui.Separator();
                }

                //  Atmospheric Scattering 
                if (ImGui.CollapsingHeader("☀ Atmospheric Scattering"))
                {
                    var atmo = skySettings.Scattering;
                    float atmoInt = atmo.Intensity;
                    if (ImGui.SliderFloat("Intensity##atmo", ref atmoInt, 0f, 30f, "%.2f")) atmo.Intensity = atmoInt;
                    float rayleigh = atmo.Rayleigh;
                    if (ImGui.SliderFloat("Rayleigh##atmo", ref rayleigh, 0f, 5f, "%.3f")) atmo.Rayleigh = rayleigh;
                    var rayCol = atmo.RayColor;
                    if (ImGui.ColorEdit3("Ray Color##atmo", ref rayCol)) atmo.RayColor = rayCol;
                    float rayH = atmo.RayHeight;
                    if (ImGui.SliderFloat("Ray Height##atmo", ref rayH, 0.1f, 20f, "%.3f")) atmo.RayHeight = rayH;
                    float mie = atmo.Mie;
                    if (ImGui.SliderFloat("Mie##atmo", ref mie, 0f, 3.0f, "%.2f")) atmo.Mie = mie;
                    var mieCol = atmo.MieColor;
                    if (ImGui.ColorEdit3("Mie Color##atmo", ref mieCol)) atmo.MieColor = mieCol;
                    float mieFoc = atmo.MieFocus;
                    if (ImGui.SliderFloat("Mie Focus##atmo", ref mieFoc, 0f, 0.99f, "%.3f")) atmo.MieFocus = mieFoc;
                    float mieH = atmo.MieHeight;
                    if (ImGui.SliderFloat("Mie Height##atmo", ref mieH, 0.1f, 5f, "%.2f")) atmo.MieHeight = mieH;
                    ImGui.Separator();
                }

                //  Clouds 
                if (ImGui.CollapsingHeader("☁ Cloud"))
                {
                    var vClouds = skySettings.Clouds;
                    bool vCloudsEn = vClouds.Enabled;
                    if (ImGui.Checkbox("Enabled##clouds", ref vCloudsEn)) vClouds.Enabled = vCloudsEn;
                    float vCDens = vClouds.Density;
                    if (ImGui.SliderFloat("Density##clouds", ref vCDens, 0f, 1f, "%.2f")) vClouds.Density = vCDens;
                    float vCAlt = vClouds.Altitude;
                    if (ImGui.SliderFloat("Altitude##clouds", ref vCAlt, 0.5f, 10f, "%.1f")) vClouds.Altitude = vCAlt;
                    float vCSpd = vClouds.Speed;
                    if (ImGui.SliderFloat("Speed##clouds", ref vCSpd, 0f, 0.2f, "%.3f")) vClouds.Speed = vCSpd;
                    float vCDet = vClouds.Detail;
                    if (ImGui.SliderFloat("Detail##clouds", ref vCDet, 0f, 1f, "%.2f")) vClouds.Detail = vCDet;
                    float vCEro = vClouds.Erosion;
                    if (ImGui.SliderFloat("Erosion##clouds", ref vCEro, 0f, 1f, "%.2f")) vClouds.Erosion = vCEro;
                    float vCShd = vClouds.ShadowStrength;
                    if (ImGui.SliderFloat("Shadow Strength##clouds", ref vCShd, 0f, 1f, "%.2f")) vClouds.ShadowStrength = vCShd;
                    float vCSca = vClouds.Scatter;
                    if (ImGui.SliderFloat("Scatter##clouds", ref vCSca, 0f, 1f, "%.2f")) vClouds.Scatter = vCSca;
                    var vCTint = vClouds.TintColor;
                    if (ImGui.ColorEdit3("Tint Color##clouds", ref vCTint)) vClouds.TintColor = vCTint;
                    float vCCir = vClouds.CirrusStrength;
                    if (ImGui.SliderFloat("Cirrus Strength##clouds", ref vCCir, 0f, 1f, "%.2f")) vClouds.CirrusStrength = vCCir;
                    ImGui.Separator();
                }

                //  Moon 
                if (ImGui.CollapsingHeader(" Moon"))
                {
                    var moon = skySettings.Moon;
                    float mBright = moon.Brightness;
                    if (ImGui.SliderFloat("Brightness##moon", ref mBright, 0f, 3f, "%.2f")) moon.Brightness = mBright;
                    float mSize = moon.Size;
                    if (ImGui.SliderFloat("Size##moon", ref mSize, 0.1f, 3f, "%.2f")) moon.Size = mSize;
                    float mGlow = moon.GlowRadius;
                    if (ImGui.SliderFloat("Glow Radius##moon", ref mGlow, 0f, 3f, "%.2f")) moon.GlowRadius = mGlow;
                    var mTint = moon.TintColor;
                    if (ImGui.ColorEdit3("Tint Color##moon", ref mTint)) moon.TintColor = mTint;
                    float mPhase = moon.PhaseOffset;
                    if (ImGui.SliderFloat("Phase Offset##moon", ref mPhase, 0f, 1f, "%.2f")) moon.PhaseOffset = mPhase;
                    float mRotSpd = moon.RotationSpeed;
                    if (ImGui.SliderFloat("Rotation Speed##moon", ref mRotSpd, -5f, 5f, "%.2f rad/s")) moon.RotationSpeed = mRotSpd;
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Moon texture rotation speed in radians/sec (0 = no rotation)");
                    string mTexPath = moon.TexturePath;
                    ImGui.Text("Texture:");
                    ImGui.TextDisabled("Drag & drop from Asset Browser ");
                    ImGui.SetNextItemWidth(-30);
                    if (ImGui.InputText("##moon_tex", ref mTexPath, 512)) moon.TexturePath = mTexPath;
                    if (ImGui.BeginDragDropTarget())
                    {
                        var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                        if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                        { moon.TexturePath = AssetBrowserPanel._dragImagePath; AssetBrowserPanel._dragImagePath = null; }
                        ImGui.EndDragDropTarget();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("X##mt", new Vector2(22, 0))) moon.TexturePath = "";
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear moon texture");
                    ImGui.Separator();
                }

                //  Stars 
                if (ImGui.CollapsingHeader(" Stars"))
                {
                    var stars = skySettings.Stars;
                    bool starsEn = stars.Enabled;
                    if (ImGui.Checkbox("Enabled##stars", ref starsEn)) stars.Enabled = starsEn;
                    float sBright = stars.Brightness;
                    if (ImGui.SliderFloat("Brightness##stars", ref sBright, 0f, 3f, "%.2f")) stars.Brightness = sBright;
                    float sDens = stars.Density;
                    if (ImGui.SliderFloat("Density##stars", ref sDens, 0f, 3f, "%.2f")) stars.Density = sDens;
                    float sTw = stars.TwinkleSpeed;
                    if (ImGui.SliderFloat("Twinkle Speed##stars", ref sTw, 0f, 5f, "%.2f")) stars.TwinkleSpeed = sTw;
                    var sCol = stars.Color;
                    if (ImGui.ColorEdit3("Color##stars", ref sCol)) stars.Color = sCol;
                    ImGui.Separator();
                }

                //  Eclipses 
                if (ImGui.CollapsingHeader(" Eclipses"))
                {
                    var ecl = skySettings.Eclipses;
                    float solEcl = ecl.SolarEclipse;
                    if (ImGui.SliderFloat("Solar Eclipse", ref solEcl, 0f, 1f, "%.2f")) ecl.SolarEclipse = solEcl;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("0 = no eclipse, 1 = total solar eclipse");
                    float lunEcl = ecl.LunarEclipse;
                    if (ImGui.SliderFloat("Lunar Eclipse", ref lunEcl, 0f, 1f, "%.2f")) ecl.LunarEclipse = lunEcl;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("0 = no eclipse, 1 = total lunar eclipse (blood moon)");
                    var eclGlow = ecl.GlowColor;
                    if (ImGui.ColorEdit3("Glow Color", ref eclGlow)) ecl.GlowColor = eclGlow;
                    ImGui.Separator();
                }

                //  Sun Rays 
                if (ImGui.CollapsingHeader("☀ Sun Rays"))
                {
                    var sr = skySettings.SunRays;
                    bool srEn = sr.Enabled;
                    if (ImGui.Checkbox("Enabled##sunrays", ref srEn)) sr.Enabled = srEn;
                    float srInt = sr.Intensity;
                    if (ImGui.SliderFloat("Intensity##sunrays", ref srInt, 0f, 1f, "%.2f")) sr.Intensity = srInt;
                    int srCount = sr.RayCount;
                    if (ImGui.SliderInt("Ray Count##sunrays", ref srCount, 3, 32)) sr.RayCount = srCount;
                    float srLen = sr.Length;
                    if (ImGui.SliderFloat("Length##sunrays", ref srLen, 0f, 3f, "%.2f")) sr.Length = srLen;
                    var srCol = sr.Color;
                    if (ImGui.ColorEdit3("Color##sunrays", ref srCol)) sr.Color = srCol;
                    ImGui.Separator();
                }
            }

            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1f),
                "Sky renders in the viewport while this object exists in the scene.");
            ImGui.Separator();
        }

        //  Visual 
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

            //  Drag-drop target for Asset Browser 
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

        //  Texture Settings: min/mag filter, mipmapping & advanced filters (anisotropy,
        //    LOD bias), common presets, wrapping, and UV tiling/offset. PER TEXTURE  pick
        //    which texture slot to edit: Simple (TexturePath) or one of the 7 PBR maps.
        //    Box/Sphere only  a Plane always renders as terrain, so its per-texture
        //    sampling lives in the Terrain section ("Texture Sampling", per layer). 
        if (editorObj.PrimitiveType is EditorPrimitiveType.Box or EditorPrimitiveType.Sphere
            && ImGui.CollapsingHeader("Texture Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (!ReferenceEquals(_texSettingsObj, editorObj))
            {
                _texSettingsObj = editorObj;
                _texSlotIdx = 0;
            }

            string[] slots =
            [
                "Simple (Texture Path)", "Albedo", "Normal", "Metallic",
                "Roughness", "AO", "Height", "Emission",
            ];
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("Texture##texslot", ref _texSlotIdx, slots, slots.Length))
            {
                // Slot switched  nothing to re-apply yet, the settings below are live.
            }

            var slotSettings = _texSlotIdx == 0
                ? editorObj.TexSettings
                : editorObj.PbrTexSettings[_texSlotIdx - 1];
            string slotPath = _texSlotIdx switch
            {
                0 => editorObj.TexturePath ?? "",
                1 => editorObj.PbrAlbedoPath,
                2 => editorObj.PbrNormalPath,
                3 => editorObj.PbrMetallicPath,
                4 => editorObj.PbrRoughnessPath,
                5 => editorObj.PbrAoPath,
                6 => editorObj.PbrHeightPath,
                7 => editorObj.PbrEmissionPath,
                _ => "",
            };
            if (DrawTextureSettings(slotSettings, showTiling: true, slotPath))
            {
                editorObj.ApplyTextureSettings();
                Console.WriteLine($"[Inspector] Updated {slots[_texSlotIdx]} texture settings on '{editorObj.Name}'");
            }
        }

        //  Flags 
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

        // Planes are always advanced heightmapped terrain  the old "Advanced Terrain"
        // toggle was removed (a Plane can no longer be switched back to a flat plane).
        ImGui.TextColored(new Vector4(0.3f, 0.9f, 1.0f, 1f),
            "Plane = advanced heightmapped terrain (always on).");
        ImGui.Spacing();
        ImGui.Separator();
            //  Heightmap 
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
            if (ImGui.Button(" Generate Random Heightmap", new Vector2(-1, 24)))
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

            //  Mesh detail 
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Mesh");
            int chunksSide = editorObj.TerrainChunksPerSide;
            if (ImGui.SliderInt("Chunks per Side", ref chunksSide, 1, 128))
            {
                editorObj.TerrainChunksPerSide = chunksSide;
                editorObj.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Split the terrain into NN chunk sub-meshes (1..128). More chunks = more sub-meshes = more total triangles (more detail).");

            // Triangles per Chunk  slider + editable field. The grid resolution always
            // snaps to a multiple of 4, so the triangle count stays a multiple of 32
            // (grid  2: 4→32, 8→128, 12→288, 16→512, ).
            int triPerChunk = editorObj.TerrainChunkSize * editorObj.TerrainChunkSize * 2;
            if (ImGui.SliderInt("Triangles per Chunk##sl", ref triPerChunk, 32, 32768))
            {
                editorObj.TerrainChunkSize = GridFromTriangles(triPerChunk);
                editorObj.MarkDirty();
            }
            ImGui.SameLine();
            if (ImGui.InputInt("##tri_per_chunk_edit", ref triPerChunk, 32, 128))
            {
                editorObj.TerrainChunkSize = GridFromTriangles(triPerChunk);
                editorObj.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Triangles in each chunk (edit box or slider)  snapped to multiples of 32.\n3232 grid  2k, 128128  32k triangles per chunk.");
            int totalTri = editorObj.TerrainChunkSize * editorObj.TerrainChunkSize * 2 * editorObj.TerrainChunksPerSide * editorObj.TerrainChunksPerSide;
            ImGui.TextDisabled($"Total: {totalTri:N0} triangles ({editorObj.TerrainChunksPerSide}{editorObj.TerrainChunksPerSide} chunks)");

            // ── Auto-recommend mesh density based on terrain footprint ──
            if (ImGui.Button("💡 Suggest Density", new Vector2(-1, 24)))
            {
                // Estimate terrain footprint from scale (Box = 1:1, Plane = Scale.X × Scale.Z)
                float footprint = MathF.Max(editorObj.Scale.X, editorObj.Scale.Z);
                int recChunks, recGrid;
                if (footprint < 50f) { recChunks = 4; recGrid = 8; }       // Small: ~256 tri total
                else if (footprint < 200f) { recChunks = 8; recGrid = 16; } // Medium: ~4k tri
                else if (footprint < 500f) { recChunks = 16; recGrid = 16; } // Large: ~16k tri
                else { recChunks = 16; recGrid = 32; }                       // Huge: ~65k tri
                editorObj.TerrainChunksPerSide = recChunks;
                editorObj.TerrainChunkSize = recGrid;
                editorObj.MarkDirty();
                Console.WriteLine($"[Terrain] Suggested density: {recChunks}×{recChunks} chunks, {recGrid}×{recGrid} grid ({recGrid*recGrid*2:N0} tri/chunk, footprint={footprint:F0})");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Automatically pick chunk count and grid resolution based on terrain footprint size.\nSmall terrains get fewer triangles; large ones get more detail.");

            float hScale = editorObj.TerrainHeightScale;
            if (ImGui.DragFloat("Height Scale", ref hScale, 0.5f, 1f, 500f, "%.1f"))
            {
                editorObj.TerrainHeightScale = Math.Max(1f, hScale);
                editorObj.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Vertical exaggeration  full-white heightmap pixels reach this height.");

            //  POM: Parallax Occlusion Mapping 
            float parallaxScale = editorObj.TerrainParallaxScale;
            if (ImGui.DragFloat("Parallax Depth", ref parallaxScale, 0.002f, 0f, 0.15f, "%.3f"))
            {
                editorObj.TerrainParallaxScale = Math.Clamp(parallaxScale, 0f, 0.15f);
                editorObj.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Height-map displacement strength (POM). 0 = off, 0.02 = subtle, 0.06 = strong.");

            //  Auto recommendation buttons 
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Quick Presets");
            float btnW = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.55f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.65f, 0.4f, 1f));
            if (ImGui.Button(" Quality", new Vector2(btnW, 24)))
            {
                editorObj.TerrainChunkSize = 32; // 3232 = 2048 tri/chunk (high detail)
                editorObj.MarkDirty();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("High quality: 32 triangles per chunk side (2048 tri/chunk)");
            ImGui.SameLine();
            ImGui.PopStyleColor(2);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.45f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.65f, 0.55f, 0.3f, 1f));
            if (ImGui.Button(" Performance", new Vector2(btnW, 24)))
            {
                editorObj.TerrainChunkSize = 8; // 88 = 128 tri/chunk (fast)
                editorObj.MarkDirty();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Performance: 8 triangles per chunk side (128 tri/chunk)");
            ImGui.PopStyleColor(2);

            ImGui.Spacing();
            ImGui.Separator();

            //  Brush settings now live in the dedicated Terrain Brush panel 
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.7f, 1f),
                "Brush settings → Terrain Brush panel (menu: Window  Terrain Brush).");
            ImGui.Spacing();
            ImGui.Separator();

            //  Save painted heights back to a .raw file 
            if (ImGui.Button(" Save Painted Heightmap", new Vector2(-1, 24)))
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

            // 
            //  DYNAMIC TERRAIN LAYERS
            // 
            var dynLayers = editorObj.TerrainLayerList;

            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Layers");
            ImGui.TextDisabled($"{dynLayers.Count} / {EditorObject.MaxTerrainLayers} layers");

            //  Add / Remove buttons 
            bool canAdd = dynLayers.Count < EditorObject.MaxTerrainLayers;
            bool canRemove = dynLayers.Count > 1;
            ImGui.BeginDisabled(!canAdd);
            if (ImGui.Button($"+ Add Layer", new Vector2(ImGui.GetContentRegionAvail().X * 0.5f, 24)))
            {
                var newLayer = TerrainLayer.CreateDefault();
                newLayer.Name = $"Layer {dynLayers.Count + 1}";
                newLayer.HeightMin = dynLayers.Count > 0 ? dynLayers[^1].HeightMax : 0f;
                newLayer.HeightMax = Math.Clamp(newLayer.HeightMin + 0.25f, 0f, 1f);
                dynLayers.Add(newLayer);
                editorObj.MarkDirty();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!canRemove);
            if (ImGui.Button($"- Remove Last", new Vector2(-1, 24)) && canRemove)
            {
                dynLayers.RemoveAt(dynLayers.Count - 1);
                if (_dynActiveLayerIdx >= dynLayers.Count) _dynActiveLayerIdx = dynLayers.Count - 1;
                editorObj.MarkDirty();
            }
            ImGui.EndDisabled();

            ImGui.Spacing();

            //  Layer list 
            Vector4[] layerColors = [
                new(0.25f, 0.55f, 0.9f, 1f),  // blue
                new(0.65f, 0.5f, 0.3f, 1f),   // brown
                new(0.3f, 0.7f, 0.35f, 1f),   // green
                new(0.9f, 0.92f, 0.98f, 1f),  // white
                new(0.55f, 0.45f, 0.38f, 1f), // rock
                new(0.8f, 0.4f, 0.8f, 1f),    // purple
                new(0.4f, 0.8f, 0.8f, 1f),    // cyan
                new(0.9f, 0.6f, 0.2f, 1f),    // orange
            ];

            for (int li = 0; li < dynLayers.Count; li++)
            {
                var layer = dynLayers[li];
                Vector4 col = layerColors[li % layerColors.Length];
                bool isActive = (li == _dynActiveLayerIdx);

                ImGui.PushID($"dyn_layer_{li}");

                // Selectable row
                if (ImGui.Selectable($"##sel_{li}", isActive, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, 28)))
                {
                    _dynActiveLayerIdx = li;
                }
                ImGui.SameLine();
                ImGui.TextColored(col, $"{li + 1}. {layer.Name}");
                ImGui.SameLine();
                ImGui.TextDisabled($"H:{layer.HeightMin:F2}-{layer.HeightMax:F2}  T:{layer.TilingX:F2}");

                ImGui.PopID();
            }

            ImGui.Spacing();
            ImGui.Separator();

            //  Active layer editing 
            if (_dynActiveLayerIdx >= 0 && _dynActiveLayerIdx < dynLayers.Count)
            {
                var activeLayer = dynLayers[_dynActiveLayerIdx];
                ImGui.PushID($"edit_layer_{_dynActiveLayerIdx}");
                ImGui.TextColored(layerColors[_dynActiveLayerIdx % layerColors.Length],
                    $"Editing Layer {_dynActiveLayerIdx + 1}: {activeLayer.Name}");

                // Name  PushID already provides unique ID context
                string name = activeLayer.Name;
                ImGui.Text("Name:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText("##layer_name_" + _dynActiveLayerIdx, ref name, 64))
                {
                    activeLayer.Name = name;
                    editorObj.MarkDirty();
                }

                // Albedo texture
                string albedo = activeLayer.AlbedoPath ?? "";
                ImGui.Text("Albedo:");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText("##albedo", ref albedo, 512))
                {
                    activeLayer.AlbedoPath = albedo;
                    editorObj.MarkDirty();
                }
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                    {
                        activeLayer.AlbedoPath = AssetBrowserPanel._dragImagePath;
                        editorObj.MarkDirty();
                        AssetBrowserPanel._dragImagePath = null;
                    }
                    ImGui.EndDragDropTarget();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Albedo texture (drag-drop from Asset Browser)");

                // Height range
                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Height Range");
                float hMin = activeLayer.HeightMin;
                float hMax = activeLayer.HeightMax;
                float sharp = activeLayer.BlendSharpness;
                if (ImGui.DragFloat("Min", ref hMin, 0.005f, 0f, 1f, "%.3f"))
                {
                    activeLayer.HeightMin = Math.Clamp(hMin, 0f, 1f);
                    editorObj.MarkDirty();
                }
                if (ImGui.DragFloat("Max", ref hMax, 0.005f, 0f, 1f, "%.3f"))
                {
                    activeLayer.HeightMax = Math.Clamp(hMax, 0f, 1f);
                    editorObj.MarkDirty();
                }
                if (ImGui.SliderFloat("Blend Sharpness", ref sharp, 0.5f, 10f, "%.1f"))
                {
                    activeLayer.BlendSharpness = Math.Clamp(sharp, 0.5f, 10f);
                    editorObj.MarkDirty();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Higher = sharper transitions between layers");

                // Tiling
                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1f), "Tiling");
                float tx = activeLayer.TilingX;
                float ty = activeLayer.TilingY;
                if (ImGui.DragFloat("Tiling X", ref tx, 0.01f, 0.01f, 5f, "%.2f"))
                {
                    activeLayer.TilingX = Math.Max(0.01f, tx);
                    editorObj.MarkDirty();
                }
                if (ImGui.DragFloat("Tiling Y", ref ty, 0.01f, 0.01f, 5f, "%.2f"))
                {
                    activeLayer.TilingY = Math.Max(0.01f, ty);
                    editorObj.MarkDirty();
                }
                bool linked = Math.Abs(activeLayer.TilingX - activeLayer.TilingY) < 0.001f;
                if (ImGui.Checkbox("Link X/Y", ref linked))
                {
                    if (linked) activeLayer.TilingY = activeLayer.TilingX;
                    editorObj.MarkDirty();
                }
                if (linked && Math.Abs(activeLayer.TilingX - activeLayer.TilingY) > 0.001f)
                {
                    activeLayer.TilingY = activeLayer.TilingX;
                    editorObj.MarkDirty();
                }
                bool stochastic = activeLayer.StochasticSampling;
                if (ImGui.Checkbox("Random Tile", ref stochastic))
                {
                    activeLayer.StochasticSampling = stochastic;
                    editorObj.MarkDirty();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Randomize sampling per tile to break up the repeating pattern.");

                // PBR maps (collapsible)
                if (ImGui.CollapsingHeader("PBR Maps"))
                {
                    string[] pbrNames = ["Normal", "Metallic", "Roughness", "AO", "Height", "Emission"];
                    for (int p = 0; p < TerrainLayer.MaxPbrMaps; p++)
                    {
                        string pbrPath = activeLayer.GetPbrPath(p) ?? "";
                        ImGui.Text($"{pbrNames[p]}:");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(-30);
                        ImGui.PushID($"pbr_{_dynActiveLayerIdx}_{p}");
                        if (ImGui.InputText($"##pbr", ref pbrPath, 512))
                        {
                            activeLayer.SetPbrPath(p, string.IsNullOrEmpty(pbrPath) ? null : pbrPath);
                            editorObj.MarkDirty();
                        }
                        if (ImGui.BeginDragDropTarget())
                        {
                            var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                            if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                            {
                                activeLayer.SetPbrPath(p, AssetBrowserPanel._dragImagePath);
                                editorObj.MarkDirty();
                                AssetBrowserPanel._dragImagePath = null;
                            }
                            ImGui.EndDragDropTarget();
                        }
                        ImGui.SameLine();
                        if (ImGui.Button($"X##pbr", new Vector2(24, 0)))
                        {
                            activeLayer.SetPbrPath(p, null);
                            editorObj.MarkDirty();
                        }
                        ImGui.PopID();
                    }
                }

                // PBR tuning (collapsible)
                if (ImGui.CollapsingHeader("PBR Tuning"))
                {
                    float ns = activeLayer.NormalStrength;
                    if (ImGui.SliderFloat("Normal Strength", ref ns, 0f, 2f, "%.2f")) { activeLayer.NormalStrength = ns; editorObj.MarkDirty(); }
                    float ms = activeLayer.MetallicStrength;
                    if (ImGui.SliderFloat("Metallic Strength", ref ms, 0f, 2f, "%.2f")) { activeLayer.MetallicStrength = ms; editorObj.MarkDirty(); }
                    float rs = activeLayer.RoughnessStrength;
                    if (ImGui.SliderFloat("Roughness Strength", ref rs, 0f, 2f, "%.2f")) { activeLayer.RoughnessStrength = rs; editorObj.MarkDirty(); }
                    bool ri = activeLayer.RoughnessInvert;
                    if (ImGui.Checkbox("Roughness Invert", ref ri)) { activeLayer.RoughnessInvert = ri; editorObj.MarkDirty(); }
                    float aos = activeLayer.AoStrength;
                    if (ImGui.SliderFloat("AO Strength", ref aos, 0f, 2f, "%.2f")) { activeLayer.AoStrength = aos; editorObj.MarkDirty(); }
                    float hs = activeLayer.HeightStrength;
                    if (ImGui.SliderFloat("Height Strength", ref hs, 0f, 2f, "%.2f")) { activeLayer.HeightStrength = hs; editorObj.MarkDirty(); }
                    bool hi = activeLayer.HeightInvert;
                    if (ImGui.Checkbox("Height Invert", ref hi)) { activeLayer.HeightInvert = hi; editorObj.MarkDirty(); }
                    float ei = activeLayer.EmissionIntensity;
                    if (ImGui.SliderFloat("Emission Intensity", ref ei, 0f, 5f, "%.2f")) { activeLayer.EmissionIntensity = ei; editorObj.MarkDirty(); }
                    float ab = activeLayer.AlbedoBrightness;
                    if (ImGui.SliderFloat("Albedo Brightness", ref ab, 0f, 2f, "%.2f")) { activeLayer.AlbedoBrightness = ab; editorObj.MarkDirty(); }
                    float asat = activeLayer.AlbedoSaturation;
                    if (ImGui.SliderFloat("Albedo Saturation", ref asat, 0f, 3f, "%.2f")) { activeLayer.AlbedoSaturation = asat; editorObj.MarkDirty(); }
                    float ac = activeLayer.AlbedoContrast;
                    if (ImGui.SliderFloat("Albedo Contrast", ref ac, 0f, 3f, "%.2f")) { activeLayer.AlbedoContrast = ac; editorObj.MarkDirty(); }
                }
                ImGui.PopID(); // edit_layer_
            }

            ImGui.Spacing();
            ImGui.Separator();

            //  Slope toggle 
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.4f, 1f), "PBR Material");
            float tMet = editorObj.TerrainPbrMetallic;
            if (ImGui.SliderFloat("Metallic", ref tMet, 0f, 1f, "%.2f")) { editorObj.TerrainPbrMetallic = tMet; editorObj.MarkDirty(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("0 = dielectric (non-metal), 1 = full metal. Affects Fresnel color and reflectivity.");
            float tRou = editorObj.TerrainPbrRoughness;
            if (ImGui.SliderFloat("Roughness", ref tRou, 0.04f, 1f, "%.2f")) { editorObj.TerrainPbrRoughness = tRou; editorObj.MarkDirty(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("0.04 = mirror-smooth, 1.0 = fully diffuse. Controls specular highlight sharpness.");

            ImGui.TextColored(new Vector4(0.8f, 0.6f, 0.3f, 1f), "Slope Layer");
            bool slopeOn = editorObj.TerrainSlopeEnabled;
            if (ImGui.Checkbox("Enable Slope Layer", ref slopeOn))
            {
                editorObj.TerrainSlopeEnabled = slopeOn;
                if (slopeOn && editorObj.TerrainSlopeLayer == null)
                    editorObj.TerrainSlopeLayer = TerrainLayer.CreateSlope();
                editorObj.MarkDirty();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Adds a rock/cliff texture on steep slopes");

            if (editorObj.TerrainSlopeEnabled && editorObj.TerrainSlopeLayer != null)
            {
                var slopeLayer = editorObj.TerrainSlopeLayer;
                string slopeAlbedo = slopeLayer.AlbedoPath ?? "";
                ImGui.Text("Slope Texture:");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText("##slope_albedo", ref slopeAlbedo, 512))
                {
                    slopeLayer.AlbedoPath = slopeAlbedo;
                    editorObj.MarkDirty();
                }
                if (ImGui.BeginDragDropTarget())
                {
                    var payload = ImGui.AcceptDragDropPayload("ASSET_IMAGE_PATH");
                    if (payload.NativePtr != null && AssetBrowserPanel._dragImagePath != null)
                    {
                        slopeLayer.AlbedoPath = AssetBrowserPanel._dragImagePath;
                        editorObj.MarkDirty();
                        AssetBrowserPanel._dragImagePath = null;
                    }
                    ImGui.EndDragDropTarget();
                }
                float st = slopeLayer.SlopeThreshold;
                if (ImGui.SliderFloat("Slope Threshold", ref st, 0.02f, 0.98f, "%.2f"))
                {
                    slopeLayer.SlopeThreshold = Math.Clamp(st, 0.02f, 0.98f);
                    editorObj.MarkDirty();
                }
                float stx = slopeLayer.TilingX;
                if (ImGui.DragFloat("Slope Tiling X", ref stx, 0.01f, 0.01f, 5f, "%.2f"))
                {
                    slopeLayer.TilingX = Math.Max(0.01f, stx);
                    if (Math.Abs(slopeLayer.TilingX - slopeLayer.TilingY) < 0.001f)
                        slopeLayer.TilingY = stx;
                    editorObj.MarkDirty();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Texture tiling on steep cliff/slope surfaces (X axis)");
                float sty = slopeLayer.TilingY;
                if (ImGui.DragFloat("Slope Tiling Y", ref sty, 0.01f, 0.01f, 5f, "%.2f"))
                {
                    slopeLayer.TilingY = Math.Max(0.01f, sty);
                    editorObj.MarkDirty();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Texture tiling on steep cliff/slope surfaces (Y axis)");
                bool slopeLinked = Math.Abs(slopeLayer.TilingX - slopeLayer.TilingY) < 0.001f;
                if (ImGui.Checkbox("Link X/Y##slope", ref slopeLinked))
                {
                    if (slopeLinked) slopeLayer.TilingY = slopeLayer.TilingX;
                    editorObj.MarkDirty();
                }
                if (slopeLinked && Math.Abs(slopeLayer.TilingX - slopeLayer.TilingY) > 0.001f)
                {
                    slopeLayer.TilingY = slopeLayer.TilingX;
                    editorObj.MarkDirty();
                }
                bool slopeStoch = slopeLayer.StochasticSampling;
                if (ImGui.Checkbox("Random Tile##slope", ref slopeStoch))
                {
                    slopeLayer.StochasticSampling = slopeStoch;
                    editorObj.MarkDirty();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Randomize sampling per tile to break up the repeating pattern.");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.7f, 1f),
                "Brush settings → Terrain Brush panel.");
    }

    /// <summary>Scan Artifacts/Maps for bundled heightmaps (.raw / images).</summary>
    /// <summary>Convert a "Triangles per Chunk" value to a grid resolution per side that
    /// is a multiple of 4  so the resulting triangle count (grid  2) is always a
    /// multiple of 32 (4→32, 8→128, 12→288, 16→512, 20→800, 24→1152, ).</summary>
    private static int GridFromTriangles(int triangles)
    {
        int grid = Math.Clamp((int)MathF.Round(MathF.Sqrt(MathF.Max(2, triangles) / 2f)), 4, 128);
        grid = ((grid + 2) / 4) * 4;               // snap to nearest multiple of 4
        return Math.Clamp(grid, 4, 128);
    }

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
            ImGui.TextColored(new Vector4(0.3f, 0.8f, 0.5f, 1f), $" {Path.GetFileName(path)}");
        else
            ImGui.TextDisabled("Drop image here or type path");
    }

    private void RenderObjectInspector(GltfObject obj, CharacterAgent? agent)
    {
        //  Focus Camera button (always at top) 
        if (ImGui.Button("Focus Camera", new Vector2(-1, 30)))
        {
            _bridge.FocusCameraOnSelected?.Invoke();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Move camera to look at this object");

        ImGui.Separator();

        //  Object Info 
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

        //  Transform 
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

        //  Agent Info 
        if (agent != null && ImGui.CollapsingHeader("Agent", ImGuiTreeNodeFlags.DefaultOpen))
        {
            float health = agent.Health;
            float maxHp = CharacterAgent.MaxHealth;
            ImGui.Text($"Health: {health:F1} / {maxHp:F1}");
            ImGui.ProgressBar(health / maxHp, new Vector2(-1, 0), $"{health:F1}/{maxHp:F1}");

            ImGui.Text($"State:  {(agent.Dead ? "Dead" : "Alive")}");
            ImGui.Text($"Dead:   {agent.Dead}");
            ImGui.Text($"Heading: {agent.Heading * 180f / MathF.PI:F1}");

            if (agent.Target != null)
                ImGui.Text($"Target: {agent.Target.GetHashCode():X8}");
            else
                ImGui.Text("Target: None");
        }
    }

    /// <summary>Shared "Texture Settings" editor used by primitives (with UV tiling/offset)
    /// and terrains (world-space triplanar  filters/wrapping only). Groups:
    /// 1) common filtering presets, 2) minification, 3) magnification, 4) mipmapping &
    /// advanced filters (anisotropy, LOD bias), 5) wrapping, 6) tiling & offset.
    /// Returns true when any value changed (caller re-applies the GL state).</summary>
    private static bool DrawTextureSettings(TextureSettings s, bool showTiling, string? texturePath = null)
    {
        bool changed = false;

        //  Auto Recommend button 
        if (!string.IsNullOrEmpty(texturePath) && System.IO.File.Exists(texturePath))
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.9f, 1f));
            if (ImGui.Button(" Auto Recommend Settings", new Vector2(-1, 26)))
            {
                s.RecommendForTexture(texturePath);
                changed = true;
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Analyze this texture and auto-set optimal filtering, mipmaps, anisotropy and wrapping.");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        //  Common Filtering Methods (preset drives min/mag/mipmap/aniso together) 
        int preset = (int)s.FilterPreset;
        if (ImGui.Combo("Filtering##texpreset", ref preset, TextureSettings.PresetNames, TextureSettings.PresetNames.Length))
        {
            s.FilterPreset = (TexFilterPreset)preset;
            s.ApplyPreset();
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Common filtering methods:\nNearest = crisp pixels (no filtering)\nBilinear = smooth, no mipmaps\nTrilinear = mipmapped smooth\nAnisotropic 2x16x = sharper at grazing angles");

        //  Minification / Magnification 
        int min = (int)s.MinFilter;
        if (ImGui.Combo("Minification", ref min, TextureSettings.MinFilterNames, TextureSettings.MinFilterNames.Length))
        {
            s.MinFilter = (TexMinFilter)min;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Filter used when the texture is smaller than its on-screen area (distant/angled surfaces).");

        int mag = (int)s.MagFilter;
        if (ImGui.Combo("Magnification", ref mag, TextureSettings.MagFilterNames, TextureSettings.MagFilterNames.Length))
        {
            s.MagFilter = (TexMagFilter)mag;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Filter used when the texture is larger than its on-screen area (close-up).");

        //  Mipmapping & Advanced Filters 
        if (ImGui.Checkbox("Generate Mipmaps", ref s.GenerateMipmaps))
            changed = true;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Build the mipmap chain. Off forces the min filter to Nearest/Linear (mipmap-based filters need mipmaps).");

        float bias = s.MipmapBias;
        if (ImGui.SliderFloat("Mipmap Bias", ref bias, -4f, 4f, "%.2f"))
        {
            s.MipmapBias = bias;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Shifts which mip level is picked (negative = sharper / more detail).");

        float aniso = s.Anisotropy;
        if (ImGui.SliderFloat("Anisotropy", ref aniso, 1f, 16f, "%.0fx"))
        {
            s.Anisotropy = aniso;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Anisotropic filtering strength  removes blur on surfaces viewed at an angle.");

        //  Texture Wrapping 
        int ws = (int)s.WrapS;
        if (ImGui.Combo("Wrap S", ref ws, TextureSettings.WrapNames, TextureSettings.WrapNames.Length))
        {
            s.WrapS = (TexWrap)ws;
            changed = true;
        }
        int wt = (int)s.WrapT;
        if (ImGui.Combo("Wrap T", ref wt, TextureSettings.WrapNames, TextureSettings.WrapNames.Length))
        {
            s.WrapT = (TexWrap)wt;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How UVs outside 0..1 are handled: Repeat = tile, Mirrored Repeat = tile mirrored, Clamp to Edge = stretch, Clamp to Border = border color.");

        //  Tiling & Offset (UV transform: uv  Tiling + Offset) 
        if (showTiling)
        {
            //  Random Tiling toggle: ON = tiling/offset randomized to break up the
            //    repeating tile pattern; OFF = back to manual tiling 11 / offset 0. 
            bool rand = s.RandomTiling;
            if (rand)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.75f, 0.45f, 0.10f, 1f));
            if (ImGui.Button(rand ? " Random Tiling: ON" : " Random Tiling: OFF", new Vector2(-1, 24)))
            {
                if (rand) s.ResetTiling(); else s.ApplyRandomTiling();
                changed = true;
            }
            if (rand)
                ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Randomize this texture's tiling/offset so the tile pattern looks varied instead of repeating\nin a perfect grid. Click again to turn OFF and restore tiling 11 / offset 0.");

            var tiling = new Vector2(s.TilingX, s.TilingY);
            if (ImGui.DragFloat2("Tiling (UV Scale)", ref tiling, 0.05f, 0.05f, 100f, "%.2f"))
            {
                s.TilingX = tiling.X;
                s.TilingY = tiling.Y;
                s.RandomTiling = false;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Repeat frequency of the texture across the UV range (1 = once, 2 = twice, ).");

            var offset = new Vector2(s.OffsetX, s.OffsetY);
            if (ImGui.DragFloat2("Offset (UV)", ref offset, 0.05f, -100f, 100f, "%.2f"))
            {
                s.OffsetX = offset.X;
                s.OffsetY = offset.Y;
                s.RandomTiling = false;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Shifts the texture start point in UV space.");
        }

        return changed;
    }
}
