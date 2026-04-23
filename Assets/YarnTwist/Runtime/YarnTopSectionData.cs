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
