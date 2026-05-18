using Hoppa.LevelEditor.Core;
using Newtonsoft.Json;

namespace Hoppa.YAK
{
    public sealed class YAKEmptyCell : ICellData
    {
        [JsonIgnore]
        public string CellTypeId => "yak.empty";
    }
}
