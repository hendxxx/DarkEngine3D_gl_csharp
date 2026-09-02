using ImGuiNET;
using System.Numerics;
using System.Linq;

namespace DarkEngine3D_gl_csharp.Engine.IDE.Panels;

public class TransitionPanel
{
    private readonly IDEBridge _bridge;
    private bool _visible = true;

    private int _selectedSceneIdx = 0;
    private int _selectedTypeIdx = 0;
    private float _duration = 0.6f;
    private Vector3 _color = new Vector3(0f, 0f, 0f);
    private int _selectedEasingIdx = 0;
    private bool _blockInput = false;

    private static readonly string[] _typeLabels = ["Fade", "Slide Left", "Slide Right"];

    public TransitionPanel(IDEBridge bridge) => _bridge = bridge;

    public void ShowInMenu() => ImGui.MenuItem("Transition", null, ref _visible);

    public void Render()
    {
        if (!_visible) return;
        ImGui.Begin("Transition", ref _visible);

        // Global default
        ImGui.Text("Global Default Transition");
        ImGui.Separator();
        ImGui.Combo("Type", ref _selectedTypeIdx, _typeLabels, _typeLabels.Length);
        ImGui.DragFloat("Duration (s)", ref _duration, 0.05f, 0.05f, 10f, "%.2f");
        var colv = _color;
        ImGui.ColorEdit3("Color", ref colv);
        _color = colv;

        string spec = BuildSpec();
        ImGui.TextDisabled($"Spec: {spec}");
        ImGui.Combo("Easing", ref _selectedEasingIdx, new[] { "linear", "easein", "easeout", "easeinout" }, 4);
        ImGui.Checkbox("Block Input", ref _blockInput);
        if (ImGui.Button("Apply Global Default"))
        {
            _bridge.DefaultTransitionType = (Engine.Scene.TransitionType)_selectedTypeIdx;
            _bridge.DefaultTransitionDuration = _duration;
            _bridge.DefaultTransitionColor = new float[3] { _color.X, _color.Y, _color.Z };
            _bridge.DefaultTransitionEasing = _selectedEasingIdx switch { 0 => "linear", 1 => "easein", 2 => "easeout", 3 => "easeinout", _ => "linear" };
            _bridge.DefaultTransitionBlockInput = _blockInput;
            Console.WriteLine($"[TransitionPanel] Set global default transition: {spec}");
        }
        ImGui.SameLine();
        if (ImGui.Button("Preview"))
        {
            // Build TransitionDefinition and ask SceneManager to preview it
            var def = new Engine.Scene.TransitionDefinition
            {
                Type = (Engine.Scene.TransitionType)_selectedTypeIdx,
                Duration = _duration,
                Color = _color,
                Easing = _selectedEasingIdx switch { 0 => Engine.Scene.TransitionEasing.Linear, 1 => Engine.Scene.TransitionEasing.EaseIn, 2 => Engine.Scene.TransitionEasing.EaseOut, 3 => Engine.Scene.TransitionEasing.EaseInOut, _ => Engine.Scene.TransitionEasing.Linear },
                BlockInput = _blockInput,
            };
            try
            {
                _bridge.SceneManager?.PreviewTransition(def);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TransitionPanel] Preview failed: {ex.Message}");
            }
        }

        ImGui.Separator();

        // Per-scene
        ImGui.Text("Per-Scene Transition");
        var scenes = _bridge.AvailableScenes;
        if (scenes.Count == 0)
        {
            ImGui.TextDisabled("No scenes registered");
            ImGui.End();
            return;
        }
        // ensure selected index valid
        if (_selectedSceneIdx < 0 || _selectedSceneIdx >= scenes.Count) _selectedSceneIdx = 0;
        var names = scenes.Select(s => s.Name).ToArray();
        ImGui.Combo("Scene", ref _selectedSceneIdx, names, names.Length);

        var cur = scenes[_selectedSceneIdx];
        ImGui.Text($"Current: type={cur.TransitionType} dur={cur.TransitionDuration:F2} ease={cur.TransitionEasing} block={cur.TransitionBlockInput}");
        if (ImGui.Button("Apply To Scene"))
        {
            var internalList = _bridge.AvailableScenesInternal;
            if (_selectedSceneIdx >= 0 && _selectedSceneIdx < internalList.Count)
            {
                var old = internalList[_selectedSceneIdx];
                internalList[_selectedSceneIdx] = old with
                {
                    TransitionType = (Engine.Scene.TransitionType)_selectedTypeIdx,
                    TransitionDuration = _duration,
                    TransitionColor = new float[3] { _color.X, _color.Y, _color.Z },
                    TransitionEasing = _selectedEasingIdx switch { 0 => "linear", 1 => "easein", 2 => "easeout", 3 => "easeinout", _ => "linear" },
                    TransitionBlockInput = _blockInput,
                };
                Console.WriteLine($"[TransitionPanel] Applied transition '{spec}' to scene '{old.Name}'");
            }
        }

        ImGui.End();
    }

    private string BuildSpec()
    {
        string typePart = _selectedTypeIdx switch
        {
            0 => "fade",
            1 => "slide:left",
            2 => "slide:right",
            _ => "fade",
        };
        return $"{typePart}:{_duration:F2}";
    }
}
