using Hoppa.LevelEditor.Core;
using Newtonsoft.Json;

namespace Hoppa.BusBuddies
{
    public sealed class BBEmptyCell : ICellData
    {
        [JsonIgnore]
        public string CellTypeId => "bb.empty";
    }
}
