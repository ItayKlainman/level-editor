namespace Hoppa.YarnTwist
{
    public sealed class YarnEmptyCell : Hoppa.LevelEditor.Core.ICellData
    {
        [Newtonsoft.Json.JsonIgnore]
        public string CellTypeId => "yt.empty";
    }
}
