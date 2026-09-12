using DarkEngine3D_gl_csharp.Engine.Objects;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Scene View panel — displays game statistics, performance metrics,
/// and a hierarchy tree of all agents/objects with click-to-select.
/// </summary>
public class SceneViewPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    public SceneViewPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Scene View", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;

        ImGui.Begin("Scene View", ref _visible);
        IDE.PanelFocus.Notify("Scene View");

        // ── Performance ──
        if (ImGui.CollapsingHeader("Performance", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var fpsCol = _bridge.Fps switch
            {
                >= 55f => new Vector4(0.3f, 0.9f, 0.3f, 1f),
                >= 30f => new Vector4(0.9f, 0.8f, 0.2f, 1f),
                _ => new Vector4(0.9f, 0.3f, 0.2f, 1f)
            };
            ImGui.TextColored(fpsCol, $"FPS:           {_bridge.Fps:F1}");
            ImGui.Text($"Frame Time:    {_bridge.FrameMs:F1} ms");
            ImGui.Text($"Drawn Objects: {_bridge.DrawnObjects} / {_bridge.TotalObjects}");
            ImGui.Text($"Tris Rendered: {_bridge.RenderedTriangles:N0} / {_bridge.TotalTriangles:N0}");
        }

        // ── Scene Stats ──
        if (ImGui.CollapsingHeader("Scene Stats", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Animated: {_bridge.AnimatedObjectCount}");
            ImGui.Text($"Static:   {_bridge.StaticObjectCount}");
        }

        // ── Camera Info ──
        if (ImGui.CollapsingHeader("Camera", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var camPos = _bridge.CameraPosition;
            ImGui.Text($"Position:  {camPos.X:F2}, {camPos.Y:F2}, {camPos.Z:F2}");
            ImGui.Text($"Yaw:       {_bridge.CameraYaw:F2}°");
            ImGui.Text($"Pitch:     {_bridge.CameraPitch:F2}°");
        }

        // ── Hierarchy ──
        if (ImGui.CollapsingHeader("Hierarchy", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var agents = _bridge.AllAgents;
            var objects = _bridge.AllObjects;

            if (agents == null || agents.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "(no agents)");
            }
            else
            {
                if (ImGui.BeginTable("hierarchy_table", 3,
                        ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                {
                    // Header row
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1f);
                    ImGui.TableSetupColumn("HP", ImGuiTableColumnFlags.WidthFixed, 60f);
                    ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 80f);
                    ImGui.TableHeadersRow();

                    // ── Agents (animated characters) ──
                    for (int i = 0; i < agents.Count; i++)
                    {
                        var agent = agents[i];
                        bool isSelected = ReferenceEquals(_bridge.SelectedAgent, agent);
                        bool isPlayer = agent.IsPlayer;

                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();

                        // Clickable label
                        string prefix = isPlayer ? "► " : "  ";
                        string name = agent.GameObject.CurrentClipName;
                        if (string.IsNullOrEmpty(name)) name = $"Agent {i}";
                        string label = $"{prefix}{(isPlayer ? "Player" : name)}###h_agent_{i}";

                        // Highlight selected row
                        if (isSelected)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.2f, 0.35f, 0.6f, 0.6f));
                            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.25f, 0.4f, 0.7f, 0.7f));
                            ImGui.Selectable(label, true, ImGuiSelectableFlags.SpanAllColumns);
                            ImGui.PopStyleColor(2);
                        }
                        else
                        {
                            if (ImGui.Selectable(label, false, ImGuiSelectableFlags.SpanAllColumns))
                            {
                                _bridge.SelectedAgent = agent;
                                _bridge.SelectedObject = agent.GameObject;
                            }
                        }

                        // Hover tooltip
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip($"Hash: {agent.GetHashCode():X8}");
                        }

                        // HP column
                        ImGui.TableNextColumn();
                        float hp = agent.Health;
                        float maxHp = CharacterAgent.MaxHealth;
                        var hpColor = hp > 50f ? new Vector4(0.3f, 0.9f, 0.3f, 1f)
                                   : hp > 20f ? new Vector4(0.9f, 0.8f, 0.2f, 1f)
                                   : new Vector4(0.9f, 0.2f, 0.2f, 1f);
                        ImGui.TextColored(hpColor, $"{hp:F0}/{maxHp:F0}");

                        // State column
                        ImGui.TableNextColumn();
                        string state = agent.Dead ? "Dead" : agent.Mode.ToString();
                        var stateCol = agent.Dead ? new Vector4(0.8f, 0.2f, 0.2f, 1f) : new Vector4(0.6f, 0.8f, 1f, 1f);
                        ImGui.TextColored(stateCol, state);
                    }

                    // ── Objects without agents ──
                    if (objects != null && agents.Count < objects.Count)
                    {
                        for (int oi = 0; oi < objects.Count; oi++)
                        {
                            var obj = objects[oi];
                            // Skip objects that already have an agent
                            bool hasAgent = false;
                            for (int ai = 0; ai < agents.Count; ai++)
                            {
                                if (ReferenceEquals(agents[ai].GameObject, obj))
                                { hasAgent = true; break; }
                            }
                            if (hasAgent) continue;

                            bool isSelected = ReferenceEquals(_bridge.SelectedObject, obj);

                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();

                            string objName = obj.CurrentClipName;
                            if (string.IsNullOrEmpty(objName)) objName = $"Object {oi}";
                            string objLabel = $"  {objName}###h_obj_{oi}";

                            if (isSelected)
                            {
                                ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.2f, 0.35f, 0.6f, 0.6f));
                                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.25f, 0.4f, 0.7f, 0.7f));
                                ImGui.Selectable(objLabel, true, ImGuiSelectableFlags.SpanAllColumns);
                                ImGui.PopStyleColor(2);
                            }
                            else
                            {
                                if (ImGui.Selectable(objLabel, false, ImGuiSelectableFlags.SpanAllColumns))
                                {
                                    _bridge.SelectedObject = obj;
                                    _bridge.SelectedAgent = null; // No agent for this object
                                }
                            }

                            // HP column (empty for non-agent objects)
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled("-");

                            // State column
                            ImGui.TableNextColumn();
                            ImGui.TextDisabled("Object");
                        }
                    }

                    ImGui.EndTable();
                }
            }
        }

        ImGui.End();
    }
}
