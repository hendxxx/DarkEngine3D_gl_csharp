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

    // ── UI button editing state ──
    private System.Numerics.Vector2 _editVec2 = new();

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
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No object selected");
            ImGui.TextDisabled("Click an object in the hierarchy to inspect");
        }

        ImGui.End();
    }

    private void RenderUIElementInspector(UIElement elem)
    {
        if (ImGui.CollapsingHeader("UI Element", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"ID: {elem.Name}");
            ImGui.Text($"Type: {elem.Type}");
            ImGui.Text($"Children: {elem.Children.Count}");
            ImGui.Separator();
        }

        // ── Text & Font ──
        if (ImGui.CollapsingHeader("Text", ImGuiTreeNodeFlags.DefaultOpen))
        {
            string text = elem.Text;
            if (ImGui.InputText("Label", ref text, 256))
                elem.Text = text;

            float fontSize = elem.FontSize;
            if (ImGui.SliderFloat("Font Size", ref fontSize, 8f, 72f))
                elem.FontSize = fontSize;

            string font = elem.FontPath;
            ImGui.InputText("Font", ref font, 256);
            if (font != elem.FontPath)
                elem.FontPath = font;

            string[] alignItems = ["Left", "Center", "Right"];
            int alignIdx = (int)elem.Alignment;
            if (ImGui.Combo("Alignment", ref alignIdx, alignItems, alignItems.Length))
                elem.Alignment = (TextAlignment)alignIdx;
        }

        // ── Transform (Position & Size) ──
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

        // ── Colors ──
        if (ImGui.CollapsingHeader("Colors", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var c = elem.TextColor;
            if (ImGui.ColorEdit3("Text Color", ref c))
                elem.TextColor = c;

            c = elem.HoverTextColor;
            if (ImGui.ColorEdit3("Hover Text", ref c))
                elem.HoverTextColor = c;

            ImGui.Spacing();

            c = elem.BgColor;
            if (ImGui.ColorEdit3("Background", ref c))
                elem.BgColor = c;

            c = elem.HoverBgColor;
            if (ImGui.ColorEdit3("Hover Bg", ref c))
                elem.HoverBgColor = c;

            ImGui.Spacing();

            c = elem.BorderColor;
            if (ImGui.ColorEdit3("Border", ref c))
                elem.BorderColor = c;

            c = elem.HoverBorderColor;
            if (ImGui.ColorEdit3("Hover Border", ref c))
                elem.HoverBorderColor = c;
        }

        // ── Behaviors ──
        if (ImGui.CollapsingHeader("Behaviors", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Build label array + find current index
            var behaviors = IDEBridge.AvailableBehaviors;
            string[] behaviorLabels = new string[behaviors.Length];
            int currentIdx = 0;
            for (int i = 0; i < behaviors.Length; i++)
            {
                behaviorLabels[i] = behaviors[i].Label;
                if (string.Equals(behaviors[i].Value, elem.ClickBehaviorLabel, StringComparison.OrdinalIgnoreCase))
                    currentIdx = i;
            }

            int chosenIdx = currentIdx;
            ImGui.Text("On Click:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##click_behavior", ref chosenIdx, behaviorLabels, behaviorLabels.Length))
            {
                elem.ClickBehaviorLabel = behaviors[chosenIdx].Value;
                // Clear OnClick delegate so the next .ing reload maps it fresh
                elem.OnClick = null;
            }

            ImGui.Spacing();

            // Hover behaviors (kept as text for now — could add combo later)
            ImGui.Text($"Hover Enter: {elem.HoverEnterLabel}");
            ImGui.Text($"Hover Exit:  {elem.HoverExitLabel}");
            ImGui.TextDisabled("Set behavior via SceneDetail, save to .ing, then reload");
        }

        // ── Visibility ──
        bool vis = elem.IsVisible;
        if (ImGui.Checkbox("Visible", ref vis))
            elem.IsVisible = vis;

        // ── Children list ──
        if (elem.Children.Count > 0 && ImGui.CollapsingHeader("Children", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < elem.Children.Count; i++)
            {
                var child = elem.Children[i];
                ImGui.Text($"  [{i}] {child.GetIcon()} {child.Name}");
            }
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
