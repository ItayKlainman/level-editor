using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hoppa.LevelEditor.Core
{
    [Serializable]
    public sealed class LevelDocument
    {
        [JsonProperty("schemaVersion")]
        public string SchemaVersion { get; set; }

        [JsonProperty("levelId")]
        public string LevelId { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("metadata")]
        public LevelMetadata Metadata { get; set; } = new LevelMetadata();

        [JsonProperty("grid")]
        public GridData<ICellData> Grid { get; set; }

        // Game-specific top section; passed through untouched by the generic core.
        [JsonProperty("topSection")]
        public JObject TopSection { get; set; }

        // Game-specific extra fields; passed through untouched.
        [JsonProperty("gameData")]
        public JObject GameData { get; set; }
    }

    [Serializable]
    public sealed class LevelMetadata
    {
        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("createdAt")]
        public string CreatedAt { get; set; }

        [JsonProperty("modifiedAt")]
        public string ModifiedAt { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; } = new List<string>();

        [JsonProperty("notes")]
        public string Notes { get; set; } = string.Empty;
    }
}
