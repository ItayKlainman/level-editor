using Hoppa.LevelEditor.Core;
using Newtonsoft.Json;

namespace Hoppa.YarnTwist
{
    public sealed class YarnBoxCell : IColoredCell, IHideableCell
    {
        [JsonIgnore]
        public string CellTypeId => "yt.box";

        [JsonProperty("colorId")]
        public string ColorId { get; set; } = "pink";

        [JsonProperty("hidden")]
        public bool Hidden { get; set; }

        // Connected Boxes: when set, this box is linked to the adjacent box in this
        // direction; tapping either clears both in-game. null = not connected. The
        // partner holds the opposite direction (reciprocal). Mirrors the game's
        // per-box Direction (exported as BottomType=ConnectedBox + Direction string).
        [JsonProperty("connectedDir", NullValueHandling = NullValueHandling.Ignore)]
        public YarnDirection? ConnectedDir { get; set; }
    }
}
