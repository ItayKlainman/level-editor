namespace Hoppa.YarnTwist
{
    public sealed class YarnWallCell : Hoppa.LevelEditor.Core.ICellData
    {
        [Newtonsoft.Json.JsonIgnore]
        public string CellTypeId => "yt.wall";
    }
}
