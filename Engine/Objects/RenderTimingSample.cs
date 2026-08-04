namespace DarkEngine3D_gl_csharp.Engine.Objects;

/// <summary>One per-object render-time sample captured during the last frame's draw pass.
/// Used by the Render Time panel to show which objects cost the most to render.</summary>
public sealed class RenderTimingSample
{
    /// <summary>Display name (animated object clip name, or the static group label like "Trees"/"Wall").</summary>
    public required string Name { get; init; }
    /// <summary>Time spent drawing this object/group in the last frame, in milliseconds.</summary>
    public required float TimeMs { get; init; }
    /// <summary>Triangles drawn for this object/group in the last frame.</summary>
    public required int Triangles { get; init; }
    /// <summary>True when this entry is an animated (skinned) character, false for static groups.</summary>
    public required bool IsAnimated { get; init; }
}
