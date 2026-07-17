using DarkEngine3D_gl_csharp.Engine.Objects;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Inspector panel — shows properties of the selected object.
/// Supports editing transform, viewing health, etc.
/// </summary>
public class InspectorPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    public InspectorPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Inspector", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;

        ImGui.Begin("Inspector", ref _visible);

        var obj = _bridge.SelectedObject;
        var agent = _bridge.SelectedAgent;

        if (obj == null)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No object selected");
            ImGui.TextDisabled("Click an object in the hierarchy to inspect");

            // Show Focus button anyway (disabled) to hint it exists
            ImGui.BeginDisabled(true);
            if (ImGui.Button("Focus Camera", new Vector2(-1, 30))) { }
            ImGui.EndDisabled();

            ImGui.End();
            return;
        }

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
            ImGui.Text($"Name:    {(!string.IsNullOrEmpty(meshName) ? meshName : "Unnamed")}");
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

        ImGui.End();
    }
}
