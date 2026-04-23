using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hoppa.LevelEditor.Core
{
    public sealed class JsonLevelSerializer : ILevelSerializer
    {
        public LevelDocument Load(string json, ICellTypeRegistry registry)
        {
            var settings = BuildSettings(registry);
            return JsonConvert.DeserializeObject<LevelDocument>(json, settings);
        }

        public string Save(LevelDocument document, ICellTypeRegistry registry)
        {
            var settings = BuildSettings(registry);
            return JsonConvert.SerializeObject(document, Formatting.Indented, settings);
        }

        private static JsonSerializerSettings BuildSettings(ICellTypeRegistry registry) =>
            new JsonSerializerSettings
            {
                Converters = { new CellDataConverter(registry) },
                NullValueHandling = NullValueHandling.Ignore,
            };
    }

    // Discriminates ICellData subtypes by the "type" field in each cell JSON object.
    internal sealed class CellDataConverter : JsonConverter<ICellData>
    {
        private readonly ICellTypeRegistry _registry;

        public CellDataConverter(ICellTypeRegistry registry) => _registry = registry;

        public override ICellData ReadJson(
            JsonReader reader, Type objectType, ICellData existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var obj = JObject.Load(reader);
            var typeId = obj["type"]?.Value<string>();

            if (typeId == null || !_registry.TryGetType(typeId, out var concreteType))
                return null;

            // Use a fresh serializer without this converter to avoid infinite recursion.
            var inner = JsonSerializer.CreateDefault();
            return (ICellData)obj.ToObject(concreteType, inner);
        }

        public override void WriteJson(JsonWriter writer, ICellData value, JsonSerializer serializer)
        {
            var inner = JsonSerializer.CreateDefault();
            var obj = JObject.FromObject(value, inner);

            // Ensure "type" is the first field for readability and AI parseability.
            if (!obj.ContainsKey("type"))
                obj.AddFirst(new JProperty("type", value.CellTypeId));

            obj.WriteTo(writer);
        }
    }
}
