using Hoppa.LevelEditor.Core;
using Newtonsoft.Json;

namespace Hoppa.BusBuddies
{
    public sealed class BBPixelCell : IColoredCell
    {
        [JsonIgnore]
        public string CellTypeId => "bb.pixel";

        [JsonProperty("colorId")]
        public string ColorId { get; set; } = "blue";
    }
}
