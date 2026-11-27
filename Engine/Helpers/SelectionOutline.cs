using System;

namespace DarkEngine3D_gl_csharp.Engine.Helpers
{
    /// <summary>
    /// Shared selection-outline settings for every outline draw path
    /// (EditorObjectManager for editor primitives, GameScene for the selected
    /// glTF object).
    ///
    /// Thickness is expressed in FRAMEBUFFER PIXELS. The outline shader expands
    /// the duplicated hull in clip space, so the same number always produces the
    /// same on-screen width — independent of the object's size, its distance from
    /// the camera, and the viewport aspect ratio.
    /// </summary>
    public static class SelectionOutline
    {
        public const float MinThicknessPx = 1f;
        public const float MaxThicknessPx = 12f;
        public const float DefaultThicknessPx = 3f;

        private static float _thicknessPx = DefaultThicknessPx;

        public static float ThicknessPx
        {
            get => _thicknessPx;
            set => _thicknessPx = Math.Clamp(value, MinThicknessPx, MaxThicknessPx);
        }
    }
}
