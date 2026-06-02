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

        [Test]
        public void Export_LevelType_Set_WritesEnumNameString()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            doc.GameData = new JObject { ["levelType"] = "Hard" };
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            Assert.AreEqual("Hard", (string)ReadOutput()["LevelConfigs"]["1"]["LevelType"]);
        }

        [Test]
        public void Export_LevelType_Unset_DefaultsToNone()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            Assert.AreEqual("None", (string)ReadOutput()["LevelConfigs"]["1"]["LevelType"]);
        }

        // ── Connected Boxes ─────────────────────────────────────────────

        [Test]
        public void Export_ConnectedBox_ProducesBottomType6_AndDirectionString()
        {
            var doc = MakeDoc("level_001",
                new[] { new YarnBoxCell { ColorId = "pink", ConnectedDir = YarnDirection.Right } },
                MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);

            var bottom = BottomConfigAt(0);
            Assert.AreEqual(6, (int)bottom["BottomType"]);      // ConnectedBox ordinal
            Assert.AreEqual(7, (int)bottom["ColorType"]);       // keeps its own color
            Assert.AreEqual("Right", (string)bottom["Direction"]); // PascalCase string
        }

        [Test]
        public void Export_ConnectedPair_WritesReciprocalDirections()
        {
            // (0,0) pink →Right ↔ (1,0) blue →Left. Each half exports its own colour and
            // the reciprocal Direction (matches Eliran's game schema exactly).
            var cells = new ICellData[]
            {
                new YarnBoxCell { ColorId = "pink", ConnectedDir = YarnDirection.Right },
                new YarnBoxCell { ColorId = "blue", ConnectedDir = YarnDirection.Left },
            };
            var doc = MakeDoc("level_001", cells, MakeTopSection(), width: 2, height: 1);
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);

            var b0 = BottomConfigAt(0);
            var b1 = BottomConfigAt(1);
            Assert.AreEqual(6, (int)b0["BottomType"]);
            Assert.AreEqual("Right", (string)b0["Direction"]);
            Assert.AreEqual(7, (int)b0["ColorType"]);
            Assert.AreEqual(6, (int)b1["BottomType"]);
            Assert.AreEqual("Left", (string)b1["Direction"]);
            Assert.AreEqual(1, (int)b1["ColorType"]);
        }

        [Test]
        public void Export_UnconnectedBox_HasNoDirectionKey_AndBottomType3()
        {
            var doc = MakeDoc("level_001", new[] { BoxCell("pink") }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);

            var bottom = BottomConfigAt(0);
            Assert.AreEqual(3, (int)bottom["BottomType"]);  // plain Color box
            Assert.IsNull(bottom["Direction"]);             // no Direction emitted
        }

        // ── Connected Spools ────────────────────────────────────────────

        [Test]
        public void Export_ConnectedSpoolPair_WritesReciprocalWinderPointers()
        {
            // col0 pink ↔ col1 blue (shared ConnectionId 1). Each winder points at the
            // other's (column, data index) — exactly Eliran's WinderConfig schema.
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, ConnectedTop(1, 1));
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);

            var w0 = TopConfigs()[0]["WinderConfigs"][0];
            var w1 = TopConfigs()[1]["WinderConfigs"][0];
            Assert.AreEqual("ConnectedWinders", (string)w0["WinderType"]);
            Assert.AreEqual(1, (int)w0["ConnectedColumnIndex"]);
            Assert.AreEqual(0, (int)w0["ConnectedWinderIndex"]);
            Assert.AreEqual("ConnectedWinders", (string)w1["WinderType"]);
            Assert.AreEqual(0, (int)w1["ConnectedColumnIndex"]);
            Assert.AreEqual(0, (int)w1["ConnectedWinderIndex"]);
        }

        [Test]
        public void Export_UnconnectedSpool_HasNoWinderTypeKey()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection(col0Spool: "pink"));
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);

            var w = TopConfigs()[0]["WinderConfigs"][0];
            Assert.IsNull(w["WinderType"]);
            Assert.IsNull(w["ConnectedColumnIndex"]);
            Assert.IsNull(w["ConnectedWinderIndex"]);
        }

        [Test]
        public void Export_IncompleteConnection_ExportsAsUnconnected()
        {
            // Only col0's spool carries the id → no partner → never emitted as connected.
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, ConnectedTop(1, null));
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);

            var w = TopConfigs()[0]["WinderConfigs"][0];
            Assert.IsNull(w["WinderType"]);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        // col0 = pink spool, col1 = blue spool, each carrying the given ConnectionId.
        private static JObject ConnectedTop(int? c0, int? c1)
        {
            var data = new YarnTopSectionData
            {
                Columns = new List<YarnSpoolColumn>
                {
                    new YarnSpoolColumn { Spools = new List<YarnSpoolData>
                        { new YarnSpoolData { ColorId = "pink", ConnectionId = c0 } } },
                    new YarnSpoolColumn { Spools = new List<YarnSpoolData>
                        { new YarnSpoolData { ColorId = "blue", ConnectionId = c1 } } },
                    new YarnSpoolColumn { Spools = new List<YarnSpoolData>() },
                    new YarnSpoolColumn { Spools = new List<YarnSpoolData>() }
                }
            };
            return JObject.FromObject(data);
        }

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
