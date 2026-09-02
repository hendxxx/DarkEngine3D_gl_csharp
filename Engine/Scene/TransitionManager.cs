using System.Numerics;
using ImGuiNET;

namespace DarkEngine3D_gl_csharp.Engine.Scene;

/// <summary>Simple frame-driven transition manager that supports fade and slide.
/// Uses ImGui draw list to render fullscreen overlays.</summary>
public class TransitionManager
{
    private TransitionDefinition? _def;
    private float _elapsed = 0f;
    private bool _active = false;
    private bool _midpointCalled = false;
    private Action? _onMidpoint;
    private Action? _onCompleted;

    public bool IsActive => _active;

    public void Start(TransitionDefinition def, Action? onMidpoint = null, Action? onCompleted = null)
    {
        _def = def;
        _elapsed = 0f;
        _active = true;
        _midpointCalled = false;
        _onMidpoint = onMidpoint;
        _onCompleted = onCompleted;
        try
        {
            Console.WriteLine($"[Transition] Start: type={def.Type} dur={def.Duration:F2} ease={def.Easing} block={def.BlockInput}");
        }
        catch { }
    }

    public void Update(float dt)
    {
        if (!_active || _def == null) return;
        _elapsed += dt;
        try { Console.WriteLine($"[Transition] Update: elapsed={_elapsed:F3}/{_def.Duration:F2} active={_active}"); } catch { }
        float half = _def.Duration * 0.5f;
        if (!_midpointCalled && _elapsed >= half)
        {
            try { _onMidpoint?.Invoke(); }
            catch (Exception ex) { Console.WriteLine($"[Transition] midpoint failed: {ex.Message}"); }
            try { Console.WriteLine("[Transition] Midpoint reached"); } catch { }
            _midpointCalled = true;
        }
        if (_elapsed >= _def.Duration)
        {
            _active = false;
            try { _onCompleted?.Invoke(); } catch (Exception ex) { Console.WriteLine($"[Transition] onCompleted failed: {ex.Message}"); }
            try { Console.WriteLine("[Transition] Completed"); } catch { }
        }
    }

        public void DrawOverlay()
    {
        if (!_active || _def == null) return;
        // Guard: ImGui may not have a valid frame/context at this point (calling
        // GetForegroundDrawList() outside an active ImGui frame can crash). Use
        // a safe try/catch and skip overlay when ImGui isn't available.
        ImDrawListPtr draw;
        ImGuiIOPtr io;
        try
        {
            io = ImGui.GetIO(); // will throw if context missing
            draw = ImGui.GetForegroundDrawList();
        }
        catch (Exception ex)
        {
            try { Console.WriteLine($"[Transition] DrawOverlay failed to get draw list: {ex.Message}"); } catch { }
            return;
        }
        try { Console.WriteLine($"[Transition] DrawOverlay: elapsed={_elapsed:F3} active={_active}"); } catch { }
        float w = io.DisplaySize.X;
        float h = io.DisplaySize.Y;
            float t = _elapsed / Math.Max(0.0001f, _def.Duration);
            if (t < 0f) t = 0f; if (t > 1f) t = 1f;
        // Apply easing (map 0..1 -> eased 0..1)
        t = ApplyEasing(t, _def.Easing);
        var c = _def.Color;
        uint col = ((uint)(c.X * 255) << 24) | ((uint)(c.Y * 255) << 16) | ((uint)(c.Z * 255) << 8) | 0u;
        // ImGui uses ABGR for AddRectFilled? We'll use ImGui.ColorConvertFloat4ToU32
        var colf = new System.Numerics.Vector4(c.X, c.Y, c.Z, 1f);

        switch (_def.Type)
        {
            case TransitionType.Fade:
                {
                    float alpha = (float)(1.0 - Math.Abs(2f * t - 1f)); // peak at midpoint
                    var fill = new System.Numerics.Vector4(c.X, c.Y, c.Z, alpha);
                    draw.AddRectFilled(new System.Numerics.Vector2(0, 0), new System.Numerics.Vector2(w, h), ImGui.ColorConvertFloat4ToU32(fill));
                }
                break;

            case TransitionType.SlideLeft:
                {
                    float frac = t; // 0..1 (already eased)
                    draw.AddRectFilled(new System.Numerics.Vector2(0, 0), new System.Numerics.Vector2(w * frac, h), ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(c.X, c.Y, c.Z, 1f)));
                }
                break;

            case TransitionType.SlideRight:
                {
                    float frac = t;
                    draw.AddRectFilled(new System.Numerics.Vector2(w * (1f - frac), 0), new System.Numerics.Vector2(w, h), ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(c.X, c.Y, c.Z, 1f)));
                }
                break;
        }
    }

    private static float ApplyEasing(float t, TransitionEasing e)
    {
        // Clamp
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return e switch
        {
            TransitionEasing.Linear => t,
            TransitionEasing.EaseIn => t * t,
            TransitionEasing.EaseOut => 1f - (1f - t) * (1f - t),
            TransitionEasing.EaseInOut => t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t),
            _ => t,
        };
    }
}
