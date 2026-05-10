using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json;

namespace Hoppa.YarnTwist
{
    public sealed class YarnTunnelCell : Hoppa.LevelEditor.Core.ICellData
    {
        [JsonIgnore]
        public string CellTypeId => "yt.tunnel";

        [JsonProperty("direction")]
        public YarnDirection OutputDirection { get; set; } = YarnDirection.Up;

        [JsonProperty("queue")]
        public List<string> Queue { get; set; } = new List<string>();

        // Not serialized — stores what was in the output neighbor before the tunnel cleared it.
        // Used to restore the cell when the tunnel's direction is changed in the inspector.
        [JsonIgnore]
        public ICellData DisplacedCell { get; set; }
    }
}
