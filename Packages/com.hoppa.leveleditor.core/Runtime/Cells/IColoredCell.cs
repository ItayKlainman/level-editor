namespace Hoppa.LevelEditor.Core
{
    // Opt-in interface for cell types that carry a color.
    // The generic validation rules (e.g. PaletteColorsExistRule) operate on this interface
    // rather than on game-specific cell types, keeping Layer 1 clean.
    public interface IColoredCell : ICellData
    {
        string ColorId { get; }
    }
}
