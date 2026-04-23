namespace Hoppa.LevelEditor.Core
{
    public interface ILevelSerializer
    {
        LevelDocument Load(string json, ICellTypeRegistry registry);
        string Save(LevelDocument document, ICellTypeRegistry registry);
    }
}
