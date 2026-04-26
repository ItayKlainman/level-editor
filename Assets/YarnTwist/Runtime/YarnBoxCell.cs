using Hoppa.LevelEditor.Core;
using Newtonsoft.Json;

namespace Hoppa.YarnTwist
{
    public sealed class YarnBoxCell : IColoredCell
    {
        [Newtonsoft.Json.JsonIgnore]
        public string CellTypeId => "yt.box";

        [JsonProperty("colorId")]
        public string ColorId { get; set; } = "pink";

        [JsonProperty("hidden")]
        public bool Hidden { get; set; }
    }
}
