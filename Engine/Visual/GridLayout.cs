namespace DarkEngine3D_gl_csharp.Engine.Visual
{
    /// <summary>
    /// Responsive 12-column grid for consistent UI layout across all scenes.
    /// Adapts positions and sizes to any screen width automatically.
    /// 
    /// Layout: |Margin|Col0|Gap|Col1|Gap|...|Col11|Margin|
    /// 
    /// Usage:
    ///   var grid = new GridLayout(screenW, screenH);
    ///   float x = grid.ColX(4);               // left edge of column 4
    ///   float w = grid.SpanW(4, 8);            // width from col 4 to 8
    ///   float cx = grid.CenterX(4, 8);         // center X between col 4-8
    ///   float btnX = cx - buttonWidth * 0.5f;  // fixed-width button centered in span
    ///   float btnW = grid.SpanW(4, 8);         // fluid-width button filling the span
    /// </summary>
    public readonly struct GridLayout
    {
        public const int Columns = 12;
        public const float Gutter = 16f;

        public float ScreenWidth { get; }
        public float ScreenHeight { get; }
        public float Margin { get; }

        /// <summary>Content width after margins (screenWidth - 2 * margin).</summary>
        public float ContentWidth => ScreenWidth - 2f * Margin;

        /// <summary>Width of a single column.</summary>
        public float ColumnWidth => (ContentWidth - (Columns - 1) * Gutter) / Columns;

        public GridLayout(float screenW, float screenH)
        {
            ScreenWidth = screenW;
            ScreenHeight = screenH;
            Margin = screenW * 0.04f;
        }

        /// <summary>Left edge X of a 0-based column index.</summary>
        public float ColX(int col) => Margin + col * (ColumnWidth + Gutter);

        /// <summary>Width spanning from colStart up to (but not including) colEnd.</summary>
        public float SpanW(int colStart, int colEnd)
        {
            int cols = colEnd - colStart;
            return cols * ColumnWidth + (cols - 1) * Gutter;
        }

        /// <summary>Center X position within a column span.</summary>
        public float CenterX(int colStart, int colEnd) => ColX(colStart) + SpanW(colStart, colEnd) * 0.5f;
    }
}
