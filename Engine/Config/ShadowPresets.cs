namespace DarkEngine3D_gl_csharp.Engine.Config
{
    /// <summary>
    /// Shared shadow quality cascade presets used by both MainMenuScene and GameScene.
    /// Indexed by quality level: 0=Low, 1=Medium, 2=High, 3=Ultra.
    /// </summary>
    public static class ShadowPresets
    {
        /// <summary>
        /// Cascade sizes for each shadow quality level.
        /// [0]=Low, [1]=Medium, [2]=High, [3]=Ultra.
        /// </summary>
        public static readonly int[][] CascadeSizes =
        [
            [2048, 1024, 512],   // Low
            [4096, 2048, 4096],  // Medium (cascade 2 raised 2048→4096: smaller far texels →
                                 //   consistent anti-acne bias without peter-panning outline)
            [4096, 4096, 2048],  // High
            [8192, 4096, 2048],  // Ultra
        ];
    }
}
