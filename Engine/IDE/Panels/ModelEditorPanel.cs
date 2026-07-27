using DarkEngine3D_gl_csharp.Engine.Objects;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Model Editor Panel — ImGui panel for managing editor-placed 3D objects.
/// Includes object list, create/delete buttons, gizmo mode selector, and property editing.
/// </summary>
public class ModelEditorPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    // ── Property editing state ──
    private float[] _editPosition = new float[3];
    private float[] _editRotation = new float[3];
    private float[] _editScale = new float[3];
    private float[] _editColor = new float[3];
    private bool _editCastShadow = true;

    // ── Selection tracking for syncing UI state ──
    private EditorObject? _lastSelection;

    public ModelEditorPanel(IDEBridge bridge)
    {
        _bridge = bridge;
    }

    public void ShowInMenu() => ImGui.MenuItem("Model Editor", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;

        ImGui.Begin("Model Editor", ref _visible);

        var mgr = _bridge.EditorObjectManager;

        // ── Create buttons ──
        if (ImGui.CollapsingHeader("Create", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.Button("Add Plane", new Vector2(-1, 30)))
            {
                var obj = mgr?.AddPrimitive(EditorPrimitiveType.Plane, GetSpawnPosition());
                if (obj != null)
                {
                    _bridge.SelectedEditorObject = obj;
                    SyncEditValues(obj);
                }
            }
            if (ImGui.Button("Add Box", new Vector2(-1, 30)))
            {
                var obj = mgr?.AddPrimitive(EditorPrimitiveType.Box, GetSpawnPosition());
                if (obj != null)
                {
                    _bridge.SelectedEditorObject = obj;
                    SyncEditValues(obj);
                }
            }
            if (ImGui.Button("Add Sphere", new Vector2(-1, 30)))
            {
                var obj = mgr?.AddPrimitive(EditorPrimitiveType.Sphere, GetSpawnPosition());
                if (obj != null)
                {
                    _bridge.SelectedEditorObject = obj;
                    SyncEditValues(obj);
                }
            }

            ImGui.Separator();
        }

        // ── Gizmo mode selector ──
        if (ImGui.CollapsingHeader("Gizmo", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int mode = _bridge.GizmoMode;

            if (ImGui.RadioButton("Translate (W)", mode == 0)) { _bridge.GizmoMode = 0; }
            ImGui.SameLine();
            if (ImGui.RadioButton("Rotate (E)", mode == 1)) { _bridge.GizmoMode = 1; }
            ImGui.SameLine();
            if (ImGui.RadioButton("Scale (R)", mode == 2)) { _bridge.GizmoMode = 2; }
        }

        // ── Object list ──
        if (ImGui.CollapsingHeader("Objects", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (mgr != null)
            {
                var objects = mgr.Objects;
                for (int i = 0; i < objects.Count; i++)
                {
                    var obj = objects[i];
                    bool isSelected = _bridge.SelectedEditorObject == obj;

                    ImGui.PushID(i);
                    if (ImGui.Selectable($"{obj.Name} ({GetTypeLabel(obj.PrimitiveType)})", isSelected))
                    {
                        _bridge.SelectedEditorObject = obj;
                        SyncEditValues(obj);
                    }

                    // Delete button for each object
                    ImGui.SameLine();
                    if (ImGui.SmallButton("X"))
                    {
                        mgr.Remove(obj);
                        if (_bridge.SelectedEditorObject == obj)
                        {
                            _bridge.SelectedEditorObject = null;
                        }
                    }
                    ImGui.PopID();
                }
            }
        }

        // ── Selected object properties ──
        var selected = _bridge.SelectedEditorObject;
        if (selected != null)
        {
            // Sync edit values if selection changed
            if (selected != _lastSelection)
            {
                SyncEditValues(selected);
                _lastSelection = selected;
            }

            if (ImGui.CollapsingHeader("Properties", ImGuiTreeNodeFlags.DefaultOpen))
            {
                // Name
                string name = selected.Name;
                if (ImGui.InputText("Name", ref name, 128))
                {
                    selected.Name = name;
                }

                // Position
                Vector3 pos = new(_editPosition[0], _editPosition[1], _editPosition[2]);
                if (ImGui.DragFloat3("Position", ref pos, 0.1f))
                {
                    _editPosition[0] = pos.X;
                    _editPosition[1] = pos.Y;
                    _editPosition[2] = pos.Z;
                    selected.Position = pos;
                }

                // Rotation (degrees)
                Vector3 rot = new(_editRotation[0], _editRotation[1], _editRotation[2]);
                if (ImGui.DragFloat3("Rotation", ref rot, 1f))
                {
                    _editRotation[0] = rot.X;
                    _editRotation[1] = rot.Y;
                    _editRotation[2] = rot.Z;
                    selected.RotationEuler = rot;
                }

                // Scale
                Vector3 scale = new(_editScale[0], _editScale[1], _editScale[2]);
                if (ImGui.DragFloat3("Scale", ref scale, 0.1f))
                {
                    _editScale[0] = scale.X;
                    _editScale[1] = scale.Y;
                    _editScale[2] = scale.Z;
                    selected.Scale = scale;
                }

                // Color
                Vector3 color = new(_editColor[0], _editColor[1], _editColor[2]);
                if (ImGui.ColorEdit3("Color", ref color))
                {
                    _editColor[0] = color.X;
                    _editColor[1] = color.Y;
                    _editColor[2] = color.Z;
                    selected.Color = color;
                    selected.MarkDirty();
                }

                // Texture path
                string texPath = selected.TexturePath ?? "";
                if (ImGui.InputText("Texture", ref texPath, 256))
                {
                    selected.TexturePath = string.IsNullOrEmpty(texPath) ? null : texPath;
                    selected.MarkDirty();
                }

                // Cast shadow
                if (ImGui.Checkbox("Cast Shadow", ref _editCastShadow))
                {
                    selected.CastShadow = _editCastShadow;
                }

                // Delete selected
                ImGui.Separator();
                if (ImGui.Button("Delete Selected", new Vector2(-1, 30)))
                {
                    mgr?.Remove(selected);
                    _bridge.SelectedEditorObject = null;
                    _lastSelection = null;
                }
            }
        }
        else
        {
            _lastSelection = null;
            ImGui.TextDisabled("No object selected");
        }

        ImGui.End();
    }

    /// <summary>
    /// Get a spawn position slightly in front of the camera, or a default offset.
    /// </summary>
    private Vector3 GetSpawnPosition()
    {
        var cam = _bridge.Camera;
        if (cam != null)
        {
            return cam.Position + cam.Front * 5f;
        }
        return new Vector3(0f, 1f, -5f);
    }

    /// <summary>
    /// Sync the float arrays from an EditorObject's properties.
    /// </summary>
    private void SyncEditValues(EditorObject obj)
    {
        _editPosition[0] = obj.Position.X;
        _editPosition[1] = obj.Position.Y;
        _editPosition[2] = obj.Position.Z;
        _editRotation[0] = obj.RotationEuler.X;
        _editRotation[1] = obj.RotationEuler.Y;
        _editRotation[2] = obj.RotationEuler.Z;
        _editScale[0] = obj.Scale.X;
        _editScale[1] = obj.Scale.Y;
        _editScale[2] = obj.Scale.Z;
        _editColor[0] = obj.Color.X;
        _editColor[1] = obj.Color.Y;
        _editColor[2] = obj.Color.Z;
        _editCastShadow = obj.CastShadow;
    }

    private static string GetTypeLabel(EditorPrimitiveType type) => type switch
    {
        EditorPrimitiveType.Plane => "Plane",
        EditorPrimitiveType.Box => "Box",
        EditorPrimitiveType.Sphere => "Sphere",
        EditorPrimitiveType.GlbReference => "glb",
        _ => "Unknown",
    };
}
