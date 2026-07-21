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
        ["Scene", "Container", "Button", "Label", "Dialog"];

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

    private void RenderUIElementInspector(UIElement elem)
    {
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

            ImGui.Text($"Children: {elem.Children.Count}");
            ImGui.Separator();
        }

        // ════════════════════════════════════════════
        //  Text & Font
        // ════════════════════════════════════════════
        if (ImGui.CollapsingHeader("Text & Font", ImGuiTreeNodeFlags.DefaultOpen))
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
                // Find current font index
                int fontIdx = 0;
                string currentFontName = Path.GetFileName(elem.FontPath);
                for (int i = 0; i < _availableFonts.Length; i++)
                {
                    if (string.Equals(_availableFonts[i], currentFontName, StringComparison.OrdinalIgnoreCase))
                    { fontIdx = i; break; }
                }

                if (ImGui.Combo("Font", ref fontIdx, _availableFonts, _availableFonts.Length))
                {
                    // Reconstruct font path
                    string fontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Artifacts", "fonts");
                    elem.FontPath = Path.Combine(fontsDir, _availableFonts[fontIdx]);
                }
            }
            else
            {
                // Fallback: raw path input
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
            var cNormal = elem.TextColor;
            if (ImGui.ColorEdit3("Text", ref cNormal, ImGuiColorEditFlags.NoInputs))
                elem.TextColor = cNormal;

            cNormal = elem.BgColor;
            if (ImGui.ColorEdit3("Background", ref cNormal, ImGuiColorEditFlags.NoInputs))
                elem.BgColor = cNormal;

            cNormal = elem.BorderColor;
            if (ImGui.ColorEdit3("Border", ref cNormal, ImGuiColorEditFlags.NoInputs))
                elem.BorderColor = cNormal;
            ImGui.Unindent();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // ── Hover state ──
            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.7f, 1f), "Hover");
            ImGui.Indent();
            var cHover = elem.HoverTextColor;
            if (ImGui.ColorEdit3("TextHover", ref cHover, ImGuiColorEditFlags.NoInputs))
                elem.HoverTextColor = cHover;

            cHover = elem.HoverBgColor;
            if (ImGui.ColorEdit3("BackgroundHover", ref cHover, ImGuiColorEditFlags.NoInputs))
                elem.HoverBgColor = cHover;

            cHover = elem.HoverBorderColor;
            if (ImGui.ColorEdit3("BorderHover", ref cHover, ImGuiColorEditFlags.NoInputs))
                elem.HoverBorderColor = cHover;
            ImGui.Unindent();
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

            // ── Helper: render a behavior combo ──
            static int FindBehaviorIdx(string value, IDEBridge.BehaviorOption[] opts)
            {
                for (int i = 0; i < opts.Length; i++)
                    if (string.Equals(opts[i].Value, value, StringComparison.OrdinalIgnoreCase))
                        return i;
                return 0; // default to (none)
            }

            // On Click
            int clickIdx = FindBehaviorIdx(elem.ClickBehaviorLabel, behaviors);
            ImGui.Text("On Click:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##click_bhv", ref clickIdx, behaviorLabels, behaviorLabels.Length))
            {
                elem.ClickBehaviorLabel = behaviors[clickIdx].Value;
                elem.OnClick = null; // Force re-map on next .ing reload
            }

            ImGui.Spacing();

            // Hover Enter
            int hoverEnterIdx = FindBehaviorIdx(elem.HoverEnterLabel, behaviors);
            ImGui.Text("On Hover Enter:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##hover_enter_bhv", ref hoverEnterIdx, behaviorLabels, behaviorLabels.Length))
            {
                elem.HoverEnterLabel = behaviors[hoverEnterIdx].Value;
                elem.OnHoverEnter = null;
            }

            ImGui.Spacing();

            // Hover Exit
            int hoverExitIdx = FindBehaviorIdx(elem.HoverExitLabel, behaviors);
            ImGui.Text("On Hover Exit:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##hover_exit_bhv", ref hoverExitIdx, behaviorLabels, behaviorLabels.Length))
            {
                elem.HoverExitLabel = behaviors[hoverExitIdx].Value;
                elem.OnHoverExit = null;
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Save to .ing and reload to apply behavior changes");
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
