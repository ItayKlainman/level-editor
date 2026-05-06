namespace Hoppa.LevelEditor.Core
{
    public interface IHideableCell : ICellData
    {
        bool Hidden { get; set; }
    }
}
