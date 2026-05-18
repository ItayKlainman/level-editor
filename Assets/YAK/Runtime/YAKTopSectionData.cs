using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hoppa.YAK
{
    public sealed class YAKSpoolEntry
    {
        [JsonProperty("colorId")]
        public string ColorId { get; set; } = "blue";

        [JsonProperty("capacity")]
        public int Capacity { get; set; } = 20;

        [JsonProperty("hidden")]
        public bool Hidden { get; set; }
    }

    public sealed class YAKSpoolColumn
    {
        [JsonProperty("spools")]
        public List<YAKSpoolEntry> Spools { get; set; } = new List<YAKSpoolEntry>();
    }

    public sealed class YAKTopSectionData
    {
        [JsonProperty("columns")]
        public List<YAKSpoolColumn> Columns { get; set; } = new List<YAKSpoolColumn>();
    }
}
