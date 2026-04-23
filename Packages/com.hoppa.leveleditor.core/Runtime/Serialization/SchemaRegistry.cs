using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Hoppa.LevelEditor.Core
{
    public sealed class SchemaRegistry
    {
        private readonly Dictionary<string, ISchemaMigration> _migrations
            = new Dictionary<string, ISchemaMigration>();

        public void Register(ISchemaMigration migration)
        {
            _migrations[migration.FromVersion] = migration;
        }

        // Walks the migration chain until no further step exists.
        // Returns the (possibly mutated) document and whether any migration ran.
        public (JObject document, bool wasMigrated) MigrateToLatest(JObject document)
        {
            bool migrated = false;
            string version = document["schemaVersion"]?.Value<string>() ?? string.Empty;

            while (_migrations.TryGetValue(version, out var step))
            {
                document = step.Migrate(document);
                version = step.ToVersion;
                document["schemaVersion"] = version;
                migrated = true;
            }

            return (document, migrated);
        }
    }
}
