using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hoppa.YarnTwist
{
    public sealed class YarnSpoolData
    {
        [JsonProperty("colorId")]
        public string ColorId { get; set; }

        [JsonProperty("hidden")]
        public bool Hidden { get; set; }

        // Connected Spools: two spools in adjacent columns that share the same
        // ConnectionId are a linked pair — each stays locked until BOTH reach
        // their column's bottom active row. Null = unconnected. The id is a
        // stable authoring handle (survives reorder/move); the exporter
        // translates it to the game's partner (column, index) pointers.
        [JsonProperty("connId", NullValueHandling = NullValueHandling.Ignore)]
        public int? ConnectionId { get; set; }
    }

    public sealed class YarnSpoolColumn
    {
        [JsonProperty("spools")]
        public List<YarnSpoolData> Spools { get; set; } = new List<YarnSpoolData>();
    }

    public sealed class YarnTopSectionData
    {
        [JsonProperty("columns")]
        public List<YarnSpoolColumn> Columns { get; set; } = new List<YarnSpoolColumn>();
    }
}
