using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hoppa.YarnTwist
{
    public sealed class YarnTunnelCell : Hoppa.LevelEditor.Core.ICellData
    {
        [Newtonsoft.Json.JsonIgnore]
        public string CellTypeId => "yt.tunnel";

        [JsonProperty("direction")]
        public YarnDirection OutputDirection { get; set; } = YarnDirection.Up;

        [JsonProperty("queue")]
        public List<string> Queue { get; set; } = new List<string>();
    }
}
