using Hoppa.LevelEditor.Core;
using Newtonsoft.Json;

namespace Hoppa.YAK
{
    public sealed class YAKWoolCell : IColoredCell
    {
        [JsonIgnore]
        public string CellTypeId => "yak.wool";

        [JsonProperty("colorId")]
        public string ColorId { get; set; } = "blue";
    }
}
