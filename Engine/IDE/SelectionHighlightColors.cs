using System.Numerics;

namespace DarkEngine3D_gl_csharp.Engine.IDE;

/// <summary>
/// Holds the two configurable selection highlight colors used by the IDE.
/// GltfObject = color of the pulsing outline around selected 3D glTF objects (default: yellow).
/// EditorObject = color of the pulsing outline around selected editor primitives (default: yellow).
/// </summary>
public struct SelectionHighlightColors
{
    /// <summary>Color of the pulsing outline around selected 3D glTF objects. Default: yellow (1, 0.84, 0.0).</summary>
    public Vector3 GltfObject;

    /// <summary>Color of the pulsing outline around selected editor primitives. Default: yellow (1, 0.84, 0.0).</summary>
    public Vector3 EditorObject;

    public static SelectionHighlightColors Default => new()
    {
        GltfObject = new Vector3(1f, 0.84f, 0f),   // yellow
        EditorObject = new Vector3(1f, 0.84f, 0f),  // yellow
    };
}
