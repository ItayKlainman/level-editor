using System;
using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json;
using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Tests
{
    [TestFixture]
    public sealed class SerializationRoundTripTests
    {
        private JsonLevelSerializer _serializer;
        private TestCellTypeRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _serializer = new JsonLevelSerializer();
            _registry = new TestCellTypeRegistry();
            _registry.Register("test.empty", typeof(TestEmptyCell));
            _registry.Register("test.box", typeof(TestBoxCell));
            _registry.Register("test.wall", typeof(TestWallCell));
        }

        [Test]
        public void RoundTrip_BasicDocument_PreservesAllFields()
        {
            var original = BuildSampleDocument();
            var json = _serializer.Save(original, _registry);
            var reloaded = _serializer.Load(json, _registry);

            Assert.AreEqual(original.SchemaVersion, reloaded.SchemaVersion);
            Assert.AreEqual(original.LevelId, reloaded.LevelId);
            Assert.AreEqual(original.DisplayName, reloaded.DisplayName);
            Assert.AreEqual(original.Metadata.Author, reloaded.Metadata.Author);
            Assert.AreEqual(original.Grid.Width, reloaded.Grid.Width);
            Assert.AreEqual(original.Grid.Height, reloaded.Grid.Height);
            Assert.AreEqual(original.Grid.RowOrder, reloaded.Grid.RowOrder);
        }

        [Test]
        public void RoundTrip_CellTypes_PreservesDiscriminator()
        {
            var original = BuildSampleDocument();
            var json = _serializer.Save(original, _registry);
            var reloaded = _serializer.Load(json, _registry);

            Assert.IsInstanceOf<TestEmptyCell>(reloaded.Grid.Get(0, 0));
            Assert.IsInstanceOf<TestWallCell>(reloaded.Grid.Get(1, 0));
            var box = reloaded.Grid.Get(2, 0) as TestBoxCell;
            Assert.IsNotNull(box);
            Assert.AreEqual("red", box.ColorId);
        }

        [Test]
        public void RoundTrip_ColorId_RoundTrips()
        {
            var id = new ColorId("cyan");
            Assert.AreEqual("cyan", id.Value);
            Assert.AreEqual(new ColorId("cyan"), id);
            Assert.AreNotEqual(new ColorId("blue"), id);
        }

        [Test]
        public void RoundTrip_GridData_InBoundsCheck()
        {
            var grid = new GridData<ICellData>(3, 2);
            Assert.IsTrue(grid.InBounds(0, 0));
            Assert.IsTrue(grid.InBounds(2, 1));
            Assert.IsFalse(grid.InBounds(3, 0));
            Assert.IsFalse(grid.InBounds(0, 2));
        }

        [Test]
        public void SchemaRegistry_MigratesVersion()
        {
            var registry = new SchemaRegistry();
            registry.Register(new TestMigration("test.v1", "test.v2"));

            var doc = Newtonsoft.Json.Linq.JObject.Parse(
                @"{""schemaVersion"":""test.v1"",""levelId"":""x""}");

            var (migrated, wasMigrated) = registry.MigrateToLatest(doc);

            Assert.IsTrue(wasMigrated);
            Assert.AreEqual("test.v2", (string)migrated["schemaVersion"]);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static LevelDocument BuildSampleDocument()
        {
            var grid = new GridData<ICellData>(3, 1);
            grid.Set(0, 0, new TestEmptyCell());
            grid.Set(1, 0, new TestWallCell());
            grid.Set(2, 0, new TestBoxCell { ColorId = "red" });

            return new LevelDocument
            {
                SchemaVersion = "test.v1",
                LevelId = "level_001",
                DisplayName = "Test Level",
                Metadata = new LevelMetadata { Author = "tester" },
                Grid = grid,
            };
        }
    }

    // ── test doubles ─────────────────────────────────────────────────────────

    internal sealed class TestCellTypeRegistry : ICellTypeRegistry
    {
        private readonly Dictionary<string, Type> _byId = new Dictionary<string, Type>();
        private readonly Dictionary<Type, string> _byType = new Dictionary<Type, string>();

        public void Register(string cellTypeId, Type concreteType)
        {
            _byId[cellTypeId] = concreteType;
            _byType[concreteType] = cellTypeId;
        }

        public bool TryGetType(string cellTypeId, out Type concreteType)
            => _byId.TryGetValue(cellTypeId, out concreteType);

        public bool TryGetId(Type concreteType, out string cellTypeId)
            => _byType.TryGetValue(concreteType, out cellTypeId);
    }

    internal sealed class TestEmptyCell : ICellData
    {
        [JsonProperty("type")]
        public string CellTypeId => "test.empty";
    }

    internal sealed class TestWallCell : ICellData
    {
        [JsonProperty("type")]
        public string CellTypeId => "test.wall";
    }

    internal sealed class TestBoxCell : ICellData
    {
        [JsonProperty("type")]
        public string CellTypeId => "test.box";

        [JsonProperty("colorId")]
        public string ColorId { get; set; }
    }

    internal sealed class TestMigration : ISchemaMigration
    {
        public string FromVersion { get; }
        public string ToVersion { get; }

        public TestMigration(string from, string to) { FromVersion = from; ToVersion = to; }

        public Newtonsoft.Json.Linq.JObject Migrate(Newtonsoft.Json.Linq.JObject document)
        {
            document["schemaVersion"] = ToVersion;
            return document;
        }
    }
}
