using Newtonsoft.Json.Linq;

namespace Hoppa.LevelEditor.Core
{
    public interface ISchemaMigration
    {
        string FromVersion { get; }
        string ToVersion { get; }
        JObject Migrate(JObject document);
    }
}
