using Hoppa.LevelEditor.Core;
using Newtonsoft.Json;

namespace Hoppa.BusBuddies
{
    public sealed class BBPixelCell : IColoredCell
    {
        [JsonIgnore]
        public string CellTypeId => "bb.pixel";

        [JsonProperty("colorId")]
        public string ColorId { get; set; } = "blue";

        // Concealed-until-revealed flag. The pixel keeps its color; Hidden only changes
        // how it renders in-editor and exports to LevelConfig.HiddenPixels.
        [JsonProperty("hidden")]
        public bool Hidden { get; set; }
    }
}
