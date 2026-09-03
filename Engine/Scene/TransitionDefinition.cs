using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.Scene;

public enum TransitionType
{
    Fade,
    SlideLeft,
    SlideRight,
}

public enum TransitionEasing
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
}

public class TransitionDefinition
{
    public TransitionType Type { get; set; } = TransitionType.Fade;
    public float Duration { get; set; } = 0.6f;
    public Vector3 Color { get; set; } = new Vector3(0f, 0f, 0f);
    public TransitionEasing Easing { get; set; } = TransitionEasing.Linear;
    public bool BlockInput { get; set; } = false;

    public static TransitionDefinition FromSpec(string spec)
    {
        // supported specs: "fade:0.6" or "slide:left:0.5" or "slide:right:0.5"
        var def = new TransitionDefinition();
        if (string.IsNullOrWhiteSpace(spec)) return def;
        var parts = spec.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return def;
        string a = parts[0].ToLowerInvariant();
        if (a == "fade") def.Type = TransitionType.Fade;
        else if (a == "slide")
        {
            if (parts.Length >= 2 && parts[1].ToLowerInvariant() == "left") def.Type = TransitionType.SlideLeft;
            else def.Type = TransitionType.SlideRight;
        }
        // duration may be last part
        if (float.TryParse(parts[^1], out float d)) def.Duration = MathF.Max(0.05f, d);
        return def;
    }

    public static TransitionEasing ParseEasing(string easing)
    {
        return easing?.ToLowerInvariant() switch
        {
            "easein" => TransitionEasing.EaseIn,
            "easeout" => TransitionEasing.EaseOut,
            "easeinout" => TransitionEasing.EaseInOut,
            _ => TransitionEasing.Linear,
        };
    }
}
