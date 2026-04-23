namespace Hoppa.LevelEditor.Core
{
    // Provides rules read-only access to the current level state.
    public sealed class ValidationContext
    {
        public LevelDocument Document { get; }
        public GridData<ICellData> Grid => Document?.Grid;
        public IColorPalette Palette { get; }

        public ValidationContext(LevelDocument document, IColorPalette palette = null)
        {
            Document = document;
            Palette = palette;
        }
    }
}
