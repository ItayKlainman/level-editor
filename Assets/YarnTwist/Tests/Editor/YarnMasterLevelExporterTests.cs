using System.Collections.Generic;
using System.IO;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YarnTwist;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor.Tests
{
    public class YarnMasterLevelExporterTests
    {
        private string _tempFile;
        private string _levelFile;
        private StringIntMapping _colorMap;
        private StringIntMapping _cellMap;
        private YarnMasterLevelExporter _exporter;

        [SetUp]
        public void SetUp()
        {
            _tempFile  = Path.Combine(Path.GetTempPath(), "level_config_test.json");
            _levelFile = Path.Combine(Path.GetTempPath(), "level_001.json");
            if (File.Exists(_tempFile)) File.Delete(_tempFile);

            _colorMap = ScriptableObject.CreateInstance<StringIntMapping>();
            _colorMap.Add("pink", 7);
            _colorMap.Add("blue", 1);
            _colorMap.Add("red",  9);

            _cellMap = ScriptableObject.CreateInstance<StringIntMapping>();
            _cellMap.Add("yt.empty",    2);
            _cellMap.Add("yt.wall",     1);
            _cellMap.Add("yt.box",      3);
            _cellMap.Add("yt.arrowbox", 4);
            _cellMap.Add("yt.tunnel",   5);

            _exporter = ScriptableObject.CreateInstance<YarnMasterLevelExporter>();
            _exporter.SetTestDependencies(_tempFile, _colorMap, _cellMap, "Coin", 10);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
        }

        [Test]
        public void Export_ParsesFileName_001_ToKey1()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            var config = ReadOutput();
            Assert.IsTrue(config["LevelConfigs"].ToObject<JObject>().ContainsKey("1"));
        }

        [Test]
        public void Export_ParsesFileName_025_ToKey25()
        {
            string levelFile025 = Path.Combine(Path.GetTempPath(), "level_025.json");
            var doc = MakeDoc("level_025", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), levelFile025);
            var config = ReadOutput();
            Assert.IsTrue(config["LevelConfigs"].ToObject<JObject>().ContainsKey("25"));
        }

        [Test]
        public void Export_NullFilePath_ReturnsFalse()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            bool result = _exporter.Export(doc, new CellTypeRegistry(), null);
            Assert.IsFalse(result);
        }

        [Test]
        public void Export_FilenameWithNoNumber_ReturnsFalse()
        {
            string noNumberFile = Path.Combine(Path.GetTempPath(), "my_awesome_level.json");
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            bool result = _exporter.Export(doc, new CellTypeRegistry(), noNumberFile);
            Assert.IsFalse(result);
        }

        [Test]
        public void Export_FilenameWithSuffixAfterNumber_UsesLastDigitGroup()
        {
            // e.g. "level_004_example.json" → last digit group "004" → key "4"
            string exampleFile = Path.Combine(Path.GetTempPath(), "level_004_example.json");
            var doc = MakeDoc("level_004_example", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), exampleFile);
            var config = ReadOutput();
            Assert.IsTrue(config["LevelConfigs"].ToObject<JObject>().ContainsKey("4"));
        }

        [Test]
        public void Export_EmptyCell_ProducesBottomType2_ColorType0()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            var bottom = BottomConfigAt(0);
            Assert.AreEqual(2, (int)bottom["BottomType"]);
            Assert.AreEqual(0, (int)bottom["ColorType"]);
        }

        [Test]
        public void Export_BoxCell_ProducesBottomType3_AndMappedColor()
        {
            var doc = MakeDoc("level_001", new[] { BoxCell("pink") }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            var bottom = BottomConfigAt(0);
            Assert.AreEqual(3, (int)bottom["BottomType"]);
            Assert.AreEqual(7, (int)bottom["ColorType"]);
        }

        [Test]
        public void Export_WallCell_ProducesBottomType1()
        {
            var doc = MakeDoc("level_001", new[] { WallCell() }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            Assert.AreEqual(1, (int)BottomConfigAt(0)["BottomType"]);
        }

        [Test]
        public void Export_BoxCell_Position_MatchesGridCoordinates()
        {
            // Grid 2x1: cell[0] is (x=0,y=0), cell[1] is (x=1,y=0)
            var cells = new ICellData[] { EmptyCell(), BoxCell("blue") };
            var doc   = MakeDoc("level_001", cells, MakeTopSection(), width: 2, height: 1);
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            var bottom1 = BottomConfigAt(1);
            Assert.AreEqual(1, (int)bottom1["Position"]["x"]);
            Assert.AreEqual(0, (int)bottom1["Position"]["y"]);
        }

        [Test]
        public void Export_TopSection_ProducesExactly4TopConfigs()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            Assert.AreEqual(4, TopConfigs().Count);
        }

        [Test]
        public void Export_SpoolColor_MapsToCorrectColorType()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection(col0Spool: "pink"));
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            var winder = TopConfigs()[0]["WinderConfigs"][0];
            Assert.AreEqual(7, (int)winder["ColorType"]);
        }

        [Test]
        public void Export_NewLevel_StubsRewardEntry()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            var rewards = ReadOutput()["LevelRewardConfigs"]["1"];
            Assert.IsNotNull(rewards);
            Assert.AreEqual("Coin", (string)rewards["WinReward"][0]["ScoreType"]);
            Assert.AreEqual(10,     (int)rewards["WinReward"][0]["ScoreAmount"]);
        }

        [Test]
        public void Export_ExistingReward_IsPreserved()
        {
            var existing = JObject.Parse(@"{
                ""LevelRewardConfigs"": { ""1"": { ""WinReward"": [{""ScoreType"":""Gem"",""ScoreAmount"":5}] } },
                ""LevelConfigs"": {}
            }");
            File.WriteAllText(_tempFile, existing.ToString());

            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);

            var reward = ReadOutput()["LevelRewardConfigs"]["1"]["WinReward"][0];
            Assert.AreEqual("Gem", (string)reward["ScoreType"]);
            Assert.AreEqual(5,     (int)reward["ScoreAmount"]);
        }

        [Test]
        public void Export_Upsert_OverwritesExistingLevelData()
        {
            var doc1 = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc1, new CellTypeRegistry(), _levelFile);

            var doc2 = MakeDoc("level_001", new[] { BoxCell("pink") }, MakeTopSection());
            _exporter.Export(doc2, new CellTypeRegistry(), _levelFile);

            Assert.AreEqual(3, (int)BottomConfigAt(0)["BottomType"]);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static ICellData EmptyCell()  => new YarnEmptyCell();
        private static ICellData WallCell()   => new YarnWallCell();
        private static ICellData BoxCell(string color) => new YarnBoxCell { ColorId = color };

        private static JObject MakeTopSection(string col0Spool = null)
        {
            var topData = new YarnTopSectionData
            {
                Columns = new List<YarnSpoolColumn>
                {
                    new YarnSpoolColumn { Spools = new List<YarnSpoolData>
                        { new YarnSpoolData { ColorId = col0Spool ?? "pink", Hidden = false } } },
                    new YarnSpoolColumn { Spools = new List<YarnSpoolData>() },
                    new YarnSpoolColumn { Spools = new List<YarnSpoolData>() },
                    new YarnSpoolColumn { Spools = new List<YarnSpoolData>() }
                }
            };
            return JObject.FromObject(topData);
        }

        private static LevelDocument MakeDoc(string levelId, ICellData[] cells,
            JObject topSection, int width = 1, int height = 1)
        {
            var grid = new GridData<ICellData>(width, height);
            for (int i = 0; i < cells.Length; i++) grid.Cells[i] = cells[i];
            return new LevelDocument
            {
                LevelId       = levelId,
                SchemaVersion = "yarn-twist.v1",
                Grid          = grid,
                TopSection    = topSection,
            };
        }

        private JObject ReadOutput() =>
            JObject.Parse(File.ReadAllText(_tempFile));

        private JToken BottomConfigAt(int idx, string levelKey = "1") =>
            ReadOutput()["LevelConfigs"][levelKey]["BottomConfigs"][idx];

        private JArray TopConfigs(string levelKey = "1") =>
            (JArray)ReadOutput()["LevelConfigs"][levelKey]["TopConfigs"];
    }
}
