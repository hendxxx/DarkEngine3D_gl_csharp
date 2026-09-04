using DarkEngine3D_gl_csharp.Engine.IDE;
using DarkEngine3D_gl_csharp.Engine.Visual;
using ImGuiNET;
using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

/// <summary>
/// Collision Editor panel for 2D sidescroller.
/// Draw, edit, and visualize collision shapes on sprites and tiles.
/// </summary>
public class CollisionEditorPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    // ── Collision shapes ──
    public List<CollisionShape2D> Shapes = new();
    private int _selectedShapeIdx = -1;
    private CollisionShape2D? SelectedShape => _selectedShapeIdx >= 0 && _selectedShapeIdx < Shapes.Count
        ? Shapes[_selectedShapeIdx] : null;

    // ── Drawing tool ──
    private enum DrawTool { Select, DrawRect, DrawCircle, DrawPolygon, Move, Delete }
    private DrawTool _currentTool = DrawTool.Select;

    // ── Draw state ──
    private bool _isDrawing;
    private Vector2 _drawStart;
    private Vector2 _drawCurrent;

    // ── Display settings ──
    private bool _showAllShapes = true;
    private bool _showCollisionGrid;
    private Vector4 _rectColor = new(0f, 1f, 0f, 0.4f);
    private Vector4 _triggerColor = new(1f, 1f, 0f, 0.4f);
    private Vector4 _circleColor = new(0f, 0.8f, 1f, 0.4f);

    // ── Physics defaults ──
    private bool _defaultIsStatic;
    private bool _defaultIsTrigger;
    private float _defaultFriction = 0.5f;
    private float _defaultRestitution;
    private int _defaultLayer = 1;
    private int _defaultMask = 1;

    // ── Layer masks ──
    private readonly string[] _layerNames = ["Default", "Player", "Enemy", "Platform", "Trigger", "Projectile", "Ground", "Wall"];

    public CollisionEditorPanel(IDEBridge bridge)
    {
        _bridge = bridge;
    }

    public void ShowInMenu() => ImGui.MenuItem("Collision Editor", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;

        ImGui.SetNextWindowSize(new Vector2(380, 550), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Collision Editor", ref _visible))
        {
            // ── Toolbar ──
            RenderToolbar();

            ImGui.Separator();

            // ── Shape list ──
            if (ImGui.CollapsingHeader($"Shapes ({Shapes.Count})", ImGuiTreeNodeFlags.DefaultOpen))
                RenderShapeList();

            ImGui.Separator();

            // ── Selected Shape Properties ──
            if (SelectedShape != null)
            {
                if (ImGui.CollapsingHeader("Shape Properties", ImGuiTreeNodeFlags.DefaultOpen))
                    RenderShapeProperties();
            }

            ImGui.Separator();

            // ── Physics Defaults ──
            if (ImGui.CollapsingHeader("Physics Defaults"))
                RenderPhysicsDefaults();

            ImGui.Separator();

            // ── Layer Mask Editor ──
            if (ImGui.CollapsingHeader("Collision Layers"))
                RenderLayerMaskEditor();
        }
        ImGui.End();
    }

    private void RenderToolbar()
    {
        var tools = new[] { ("Select", DrawTool.Select), ("Rect", DrawTool.DrawRect),
                           ("Circle", DrawTool.DrawCircle), ("Move", DrawTool.Move),
                           ("Delete", DrawTool.Delete) };

        foreach (var (label, tool) in tools)
        {
            bool isActive = _currentTool == tool;
            if (isActive) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.6f, 1f, 1f));
            if (ImGui.Button($"{label}##col", new Vector2(65, 24)))
                _currentTool = tool;
            if (isActive) ImGui.PopStyleColor();
            ImGui.SameLine();
        }
        ImGui.NewLine();

        ImGui.Checkbox("Show All", ref _showAllShapes);
        ImGui.SameLine();
        ImGui.Checkbox("Collision Grid", ref _showCollisionGrid);
    }

    private void RenderShapeList()
    {
        for (int i = 0; i < Shapes.Count; i++)
        {
            var shape = Shapes[i];
            bool isSelected = i == _selectedShapeIdx;
            string icon = shape.Type switch
            {
                CollisionShape2D.ShapeType.Rectangle => "□",
                CollisionShape2D.ShapeType.Circle => "○",
                CollisionShape2D.ShapeType.Polygon => "△",
                _ => "?"
            };
            string trigger = shape.IsTrigger ? " [T]" : "";
            string label = $"{icon} {shape.OwnerName}{trigger}##{i}";

            if (ImGui.Selectable(label, isSelected))
                _selectedShapeIdx = i;
        }

        // Add buttons
        if (ImGui.Button("+ Rect"))
            AddShape(CollisionShape2D.ShapeType.Rectangle);
        ImGui.SameLine();
        if (ImGui.Button("+ Circle"))
            AddShape(CollisionShape2D.ShapeType.Circle);
        ImGui.SameLine();
        if (ImGui.Button("+ Delete") && _selectedShapeIdx >= 0)
        {
            Shapes.RemoveAt(_selectedShapeIdx);
            _selectedShapeIdx = Math.Min(_selectedShapeIdx, Shapes.Count - 1);
        }
    }

    private void RenderShapeProperties()
    {
        var shape = SelectedShape!;

        ImGui.InputText("Owner", ref shape.OwnerName, 256);
        ImGui.Checkbox("Enabled", ref shape.IsEnabled);
        ImGui.Checkbox("Trigger", ref shape.IsTrigger);
        ImGui.Separator();

        switch (shape.Type)
        {
            case CollisionShape2D.ShapeType.Rectangle:
                ImGui.Text("Rectangle:");
                ImGui.InputFloat("Width", ref shape.RectWidth);
                ImGui.InputFloat("Height", ref shape.RectHeight);
                ImGui.InputFloat("Offset X", ref shape.RectOffset.X);
                ImGui.InputFloat("Offset Y", ref shape.RectOffset.Y);
                break;

            case CollisionShape2D.ShapeType.Circle:
                ImGui.Text("Circle:");
                ImGui.InputFloat("Radius", ref shape.CircleRadius);
                ImGui.InputFloat("Offset X", ref shape.CircleOffset.X);
                ImGui.InputFloat("Offset Y", ref shape.CircleOffset.Y);
                break;

            case CollisionShape2D.ShapeType.Polygon:
                ImGui.Text("Polygon:");
                ImGui.Text($"Vertices: {shape.PolygonVertices.Count}");
                if (ImGui.Button("Clear Vertices"))
                    shape.PolygonVertices.Clear();
                for (int i = 0; i < shape.PolygonVertices.Count; i++)
                {
                    var v = shape.PolygonVertices[i];
                    ImGui.PushID(i);
                    ImGui.InputFloat($"##vx{i}", ref v.X);
                    ImGui.SameLine();
                    ImGui.InputFloat($"##vy{i}", ref v.Y);
                    ImGui.SameLine();
                    if (ImGui.Button("X"))
                    {
                        shape.PolygonVertices.RemoveAt(i);
                        ImGui.PopID();
                        break;
                    }
                    shape.PolygonVertices[i] = v;
                    ImGui.PopID();
                }
                if (ImGui.Button("Add Vertex"))
                    shape.PolygonVertices.Add(Vector2.Zero);
                break;
        }

        ImGui.Separator();
        ImGui.Checkbox("Static", ref shape.IsStatic);
        ImGui.Checkbox("Kinematic", ref shape.IsKinematic);
        ImGui.SliderFloat("Friction", ref shape.Friction, 0f, 1f);
        ImGui.SliderFloat("Bounce", ref shape.Restitution, 0f, 1f);
    }

    private void RenderPhysicsDefaults()
    {
        ImGui.Checkbox("Default Static", ref _defaultIsStatic);
        ImGui.Checkbox("Default Trigger", ref _defaultIsTrigger);
        ImGui.SliderFloat("Default Friction", ref _defaultFriction, 0f, 1f);
        ImGui.SliderFloat("Default Bounce", ref _defaultRestitution, 0f, 1f);
        ImGui.InputInt("Default Layer", ref _defaultLayer);
        ImGui.InputInt("Default Mask", ref _defaultMask);
    }

    private void RenderLayerMaskEditor()
    {
        ImGui.TextDisabled("Collision layers control which shapes can interact.");
        ImGui.TextDisabled("Set bit flags in Layer and Mask fields above.");

        for (int i = 0; i < _layerNames.Length; i++)
        {
            ImGui.Text($"  {_layerNames[i]} (bit {i})");
        }
    }

    private void AddShape(CollisionShape2D.ShapeType type)
    {
        var shape = type switch
        {
            CollisionShape2D.ShapeType.Rectangle => CollisionShape2D.CreateRect(64f, 64f),
            CollisionShape2D.ShapeType.Circle => CollisionShape2D.CreateCircle(32f),
            CollisionShape2D.ShapeType.Polygon => CollisionShape2D.CreateBoxPolygon(64f, 64f),
            _ => CollisionShape2D.CreateRect(64f, 64f)
        };
        shape.IsStatic = _defaultIsStatic;
        shape.IsTrigger = _defaultIsTrigger;
        shape.Friction = _defaultFriction;
        shape.Restitution = _defaultRestitution;
        shape.LayerBits = _defaultLayer;
        shape.MaskBits = _defaultMask;
        shape.OwnerName = $"Shape_{Shapes.Count}";

        Shapes.Add(shape);
        _selectedShapeIdx = Shapes.Count - 1;
    }

    /// <summary>
    /// Handle draw start from viewport.
    /// </summary>
    public void OnDrawStart(Vector2 worldPos)
    {
        if (_currentTool == DrawTool.DrawRect || _currentTool == DrawTool.DrawCircle)
        {
            _isDrawing = true;
            _drawStart = worldPos;
            _drawCurrent = worldPos;
        }
    }

    /// <summary>
    /// Handle draw update from viewport.
    /// </summary>
    public void OnDrawUpdate(Vector2 worldPos)
    {
        if (_isDrawing)
            _drawCurrent = worldPos;
    }

    /// <summary>
    /// Handle draw end from viewport. Creates the shape.
    /// </summary>
    public void OnDrawEnd(Vector2 worldPos)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        _drawCurrent = worldPos;

        float dx = _drawCurrent.X - _drawStart.X;
        float dy = _drawCurrent.Y - _drawStart.Y;

        if (MathF.Abs(dx) < 2f && MathF.Abs(dy) < 2f) return; // Too small, ignore

        Vector2 center = (_drawStart + _drawCurrent) * 0.5f;

        if (_currentTool == DrawTool.DrawRect)
        {
            var shape = CollisionShape2D.CreateRect(MathF.Abs(dx), MathF.Abs(dy));
            shape.RectOffset = center;
            shape.IsStatic = _defaultIsStatic;
            shape.IsTrigger = _defaultIsTrigger;
            shape.OwnerName = $"Rect_{Shapes.Count}";
            Shapes.Add(shape);
            _selectedShapeIdx = Shapes.Count - 1;
        }
        else if (_currentTool == DrawTool.DrawCircle)
        {
            float radius = MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) * 0.5f;
            var shape = CollisionShape2D.CreateCircle(radius);
            shape.CircleOffset = center;
            shape.IsStatic = _defaultIsStatic;
            shape.IsTrigger = _defaultIsTrigger;
            shape.OwnerName = $"Circle_{Shapes.Count}";
            Shapes.Add(shape);
            _selectedShapeIdx = Shapes.Count - 1;
        }
    }

    /// <summary>
    /// Get the current draw rect (for viewport visualization while drawing).
    /// </summary>
    public (Vector2 min, Vector2 max)? GetCurrentDrawRect()
    {
        if (!_isDrawing) return null;
        float minX = MathF.Min(_drawStart.X, _drawCurrent.X);
        float minY = MathF.Min(_drawStart.Y, _drawCurrent.Y);
        float maxX = MathF.Max(_drawStart.X, _drawCurrent.X);
        float maxY = MathF.Max(_drawStart.Y, _drawCurrent.Y);
        return (new Vector2(minX, minY), new Vector2(maxX, maxY));
    }
}
