using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Hoppa.YarnTwist
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum YarnDirection { Up, Down, Left, Right }
}
