using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public void Export_NewLevelIntoEmptyMaster_GetsKey1_RegardlessOfFilenameNumber()
        {
            // The filename's number no longer decides the slot — a new level lands at slot 1.
            string levelFile025 = Path.Combine(Path.GetTempPath(), "level_025.json");
            var doc = MakeDoc("level_025", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), levelFile025);
            var configs = ReadOutput()["LevelConfigs"].ToObject<JObject>();
            Assert.IsTrue(configs.ContainsKey("1"));
            Assert.IsFalse(configs.ContainsKey("25"));
            Assert.AreEqual("level_025", (string)configs["1"]["levelId"]);
        }

        [Test]
        public void Export_NullFilePath_ReturnsFalse()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            bool result = _exporter.Export(doc, new CellTypeRegistry(), null);
            Assert.IsFalse(result);
        }

        [Test]
        public void Export_FilenameWithNoNumber_NowSucceeds_AtSlot1()
        {
            // A number in the filename is now optional; the slot is assigned, not parsed.
            string noNumberFile = Path.Combine(Path.GetTempPath(), "my_awesome_level.json");
            var doc = MakeDoc("my_awesome_level", new[] { EmptyCell() }, MakeTopSection());
            bool result = _exporter.Export(doc, new CellTypeRegistry(), noNumberFile);
            Assert.IsTrue(result);
            Assert.AreEqual("my_awesome_level", (string)ReadOutput()["LevelConfigs"]["1"]["levelId"]);
        }

        [Test]
        public void Export_NewCollidingName_DoesNotOverwriteExistingSlot()
        {
            // Seed a master with level_001 + level_002, then export a NEW level whose name
            // collides on the number (level002_new). It must NOT clobber level_002 — both survive,
            // and the new level is inserted at the top.
            SeedMaster(("1", "level_001"), ("2", "level_002"));

            string collidingFile = Path.Combine(Path.GetTempPath(), "level002_new.json");
            var doc = MakeDoc("level002_new", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), collidingFile);

            var configs = ReadOutput()["LevelConfigs"].ToObject<JObject>();
            Assert.AreEqual(3, configs.Count);                                // nothing lost
            Assert.AreEqual("level002_new", (string)configs["1"]["levelId"]); // new at top
            var ids = configs.Properties().Select(p => (string)p.Value["levelId"]).ToList();
            CollectionAssert.Contains(ids, "level_001");
            CollectionAssert.Contains(ids, "level_002");
        }

        [Test]
        public void Export_NewLevel_ShiftsExistingDown_AndCarriesRewards()
        {
            // Seed level_001 at slot 1 with a recognizable reward; export a new level → it takes
            // slot 1, level_001 shifts to slot 2 and keeps its reward.
            SeedMaster(("1", "level_001"));
            var seeded = ReadOutput();
            ((JObject)seeded["LevelRewardConfigs"])["1"] = new JObject
            {
                ["WinReward"] = new JArray { new JObject { ["ScoreType"] = "Coin", ["ScoreAmount"] = 99 } }
            };
            File.WriteAllText(_tempFile, seeded.ToString());

            string newFile = Path.Combine(Path.GetTempPath(), "level_010.json");
            var doc = MakeDoc("level_010", new[] { EmptyCell() }, MakeTopSection());
            doc.GameData = new JObject { ["coinReward"] = 10 };
            _exporter.Export(doc, new CellTypeRegistry(), newFile);

            var output = ReadOutput();
            Assert.AreEqual("level_010", (string)output["LevelConfigs"]["1"]["levelId"]);
            Assert.AreEqual("level_001", (string)output["LevelConfigs"]["2"]["levelId"]);
            Assert.AreEqual(10, (int)output["LevelRewardConfigs"]["1"]["WinReward"][0]["ScoreAmount"]);
            Assert.AreEqual(99, (int)output["LevelRewardConfigs"]["2"]["WinReward"][0]["ScoreAmount"]); // carried
        }

        [Test]
        public void Export_ReExportExistingLevel_UpdatesInPlace_NoShift()
        {
            SeedMaster(("1", "level_001"), ("2", "level_002"));

            string file002 = Path.Combine(Path.GetTempPath(), "level_002.json");
            var doc = MakeDoc("level_002", new[] { BoxCell("pink") }, MakeTopSection());
            doc.GameData = new JObject { ["coinReward"] = 42 };
            _exporter.Export(doc, new CellTypeRegistry(), file002);

            var output = ReadOutput();
            Assert.AreEqual(2, output["LevelConfigs"].ToObject<JObject>().Count);             // no new slot
            Assert.AreEqual("level_002", (string)output["LevelConfigs"]["2"]["levelId"]);     // same slot
            Assert.AreEqual(3, (int)output["LevelConfigs"]["2"]["BottomConfigs"][0]["BottomType"]); // updated
            Assert.AreEqual(42, (int)output["LevelRewardConfigs"]["2"]["WinReward"][0]["ScoreAmount"]);
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
            doc.GameData = new JObject { ["coinReward"] = 10 }; // deterministic — don't rely on EditorPrefs
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            var rewards = ReadOutput()["LevelRewardConfigs"]["1"];
            Assert.IsNotNull(rewards);
            Assert.AreEqual("Coin", (string)rewards["WinReward"][0]["ScoreType"]);
            Assert.AreEqual(10,     (int)rewards["WinReward"][0]["ScoreAmount"]);
        }

        [Test]
        public void Export_AlwaysWritesReward_OverwritingExisting()
        {
            // The exporter always (re)writes the reward from the level's coinReward, so an
            // existing reward of a different type/amount is overwritten on every export.
            var existing = JObject.Parse(@"{
                ""LevelRewardConfigs"": { ""1"": { ""WinReward"": [{""ScoreType"":""Gem"",""ScoreAmount"":5}] } },
                ""LevelConfigs"": {}
            }");
            File.WriteAllText(_tempFile, existing.ToString());

            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            doc.GameData = new JObject { ["coinReward"] = 25 };
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);

            var reward = ReadOutput()["LevelRewardConfigs"]["1"]["WinReward"][0];
            Assert.AreEqual("Coin", (string)reward["ScoreType"]); // overwritten with the default type
            Assert.AreEqual(25,     (int)reward["ScoreAmount"]);
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

        // ── Palette ─────────────────────────────────────────────────────

        [Test]
        public void Export_PaletteCenter_GetsExtraFeatureBottomTypeAndAmount()
        {
            // 3x3 grid of boxes, palette centered at (1,1). Only the center cell gets
            // ExtraFeatureBottomType=Palette + PaletteAmount; the game derives the 3x3 cover.
            var cells = new ICellData[9];
            for (int i = 0; i < 9; i++) cells[i] = new YarnBoxCell { ColorId = "pink" };
            var doc = MakeDoc("level_001", cells, MakeTopSection(col0Spool: "pink"), width: 3, height: 3);
            YarnPalettes.Add(doc, new CellRef(1, 1));
            YarnPalettes.SetAmount(doc, new CellRef(1, 1), 7);

            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);

            var center = BottomConfigAt(4); // (x=1,y=1) → y*width+x = 4
            var corner = BottomConfigAt(0); // (0,0)
            Assert.AreEqual("Palette", (string)center["ExtraFeatureBottomType"]);
            Assert.AreEqual(7,         (int)center["PaletteAmount"]);
            Assert.IsNull(corner["ExtraFeatureBottomType"]);
            Assert.IsNull(corner["PaletteAmount"]);
        }

        [Test]
        public void Export_NoPalette_HasNoExtraFeatureKey()
        {
            var doc = MakeDoc("level_001", new[] { BoxCell("pink") }, MakeTopSection());
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            Assert.IsNull(BottomConfigAt(0)["ExtraFeatureBottomType"]);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        // Write a master JSON pre-populated with the given (key, levelId) entries, each with a
        // stub reward, so insert/shift behavior can be tested against an existing file.
        private void SeedMaster(params (string key, string levelId)[] entries)
        {
            var levels  = new JObject();
            var rewards = new JObject();
            foreach (var (key, levelId) in entries)
            {
                levels[key] = new JObject
                {
                    ["levelId"]       = levelId,
                    ["LevelType"]     = "None",
                    ["BottomConfigs"] = new JArray(),
                    ["TopConfigs"]    = new JArray()
                };
                rewards[key] = new JObject
                {
                    ["WinReward"] = new JArray { new JObject { ["ScoreType"] = "Coin", ["ScoreAmount"] = 1 } }
                };
            }
            var root = new JObject { ["LevelConfigs"] = levels, ["LevelRewardConfigs"] = rewards };
            File.WriteAllText(_tempFile, root.ToString());
        }

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
