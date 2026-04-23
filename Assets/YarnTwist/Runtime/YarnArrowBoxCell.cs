using Hoppa.LevelEditor.Core;
using Newtonsoft.Json;

namespace Hoppa.YarnTwist
{
    public sealed class YarnArrowBoxCell : IColoredCell
    {
        public string CellTypeId => "yt.arrowbox";

        [JsonProperty("colorId")]
        public string ColorId { get; set; } = "pink";

        [JsonProperty("direction")]
        public YarnDirection Direction { get; set; } = YarnDirection.Right;
    }
}
