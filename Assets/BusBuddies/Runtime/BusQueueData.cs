using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hoppa.BusBuddies
{
    public sealed class BusEntry
    {
        [JsonProperty("colorId")]
        public string ColorId { get; set; } = "blue";

        [JsonProperty("capacity")]
        public int Capacity { get; set; } = 10;

        [JsonProperty("hidden")]
        public bool Hidden { get; set; }

        // -1 = not part of a connected pair. Pairing modeling is deferred (1a treats
        // connected buses as independent); the field is carried so authoring/export survive.
        [JsonProperty("connectedId")]
        public int ConnectedId { get; set; } = -1;
    }

    public sealed class BusColumn
    {
        // Head (index 0) is the only tappable bus; back of the queue is the last element.
        [JsonProperty("buses")]
        public List<BusEntry> Buses { get; set; } = new List<BusEntry>();
    }

    public sealed class BusQueueData
    {
        [JsonProperty("columns")]
        public List<BusColumn> Columns { get; set; } = new List<BusColumn>();
    }
}
