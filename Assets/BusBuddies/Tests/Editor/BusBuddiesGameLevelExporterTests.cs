using System.Collections.Generic;
using System.IO;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Verifies BusBuddiesGameLevelExporter emits the REAL game LevelConfig schema,
    // ONE FILE PER LEVEL (level_<N>.json):
    //   { SlotsAmount, Width, Height,
    //     BusColumnConfigs[ BusConfigs[ {ColorType, Capacity, BusType?} ] ],
    //     PixelColors[ int ordinals, index = y*Width + x ] }
    public class BusBuddiesGameLevelExporterTests
    {
        private string _dir;
        private BusBuddiesGameLevelExporter _exporter;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "bb_game_levels_test");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            _exporter = ScriptableObject.CreateInstance<BusBuddiesGameLevelExporter>();
            _exporter.SetTestDependencies(_dir, 5);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private JObject ReadLevel(int n) => JObject.Parse(File.ReadAllText(Path.Combine(_dir, "level_" + n + ".json")));
        private static ICellData Pixel(string color) => new BBPixelCell { ColorId = color };

        private static JObject Queue(params (string color, int cap, bool hidden)[] buses)
        {
            var col = new BusColumn { Buses = new List<BusEntry>() };
            foreach (var b in buses)
                col.Buses.Add(new BusEntry { ColorId = b.color, Capacity = b.cap, Hidden = b.hidden });
            return JObject.FromObject(new BusQueueData { Columns = new List<BusColumn> { col } });
        }

        private static LevelDocument Doc(string levelId, ICellData[] cells, JObject top, int w = 1, int h = 1)
        {
            var grid = new GridData<ICellData>(w, h);
            for (int i = 0; i < cells.Length; i++) grid.Cells[i] = cells[i];
            return new LevelDocument { LevelId = levelId, SchemaVersion = "busbuddies.v1", Grid = grid, TopSection = top };
        }

        [Test]
        public void Export_WritesPerFile_level_N_json()
        {
            var doc = Doc("cupcake_007", new[] { Pixel("red") }, Queue(("blue", 40, false)));
            Assert.IsTrue(_exporter.Export(doc, new CellTypeRegistry(),
                Path.Combine(Path.GetTempPath(), "cupcake_007.json")));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "level_7.json")), "must write level_7.json into the output dir");
        }

        [Test]
        public void Export_SchemaShape_MatchesGameLevelConfig()
        {
            var cells = new ICellData[] { Pixel("red"), Pixel("blue") };
            var doc = Doc("level_003", cells, Queue(("green", 40, false), ("red", 60, true)), w: 2, h: 1);
            doc.GameData = new JObject { ["conveyorCount"] = 5 };
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_003.json"));

            var lc = ReadLevel(3);
            Assert.AreEqual(5, (int)lc["SlotsAmount"]);
            Assert.AreEqual(2, (int)lc["Width"]);
            Assert.AreEqual(1, (int)lc["Height"]);
            Assert.AreEqual(2, ((JArray)lc["PixelColors"]).Count);

            var busConfigs = (JArray)lc["BusColumnConfigs"][0]["BusConfigs"];
            Assert.AreEqual(4, (int)busConfigs[0]["ColorType"]);   // green
            Assert.AreEqual(40, (int)busConfigs[0]["Capacity"]);
            Assert.IsNull(busConfigs[0]["BusType"], "non-hidden bus omits BusType");
            Assert.AreEqual(9, (int)busConfigs[1]["ColorType"]);   // red
            Assert.AreEqual(1, (int)busConfigs[1]["BusType"]);     // hidden → BusType 1
        }

        [Test]
        public void Export_FullPaletteColors_MapToBUBOrdinals()
        {
            // Spans the extended palette to prove the 36-color map (not just the base 8).
            var cells = new ICellData[] { Pixel("Black"), Pixel("Brown"), Pixel("BrownVeryDark"), Pixel("Gold") };
            var doc = Doc("level_1", cells, Queue(("blue", 40, false)), w: 4, h: 1);
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_1.json"));
            var px = (JArray)ReadLevel(1)["PixelColors"];
            Assert.AreEqual(17, (int)px[0]); // Black
            Assert.AreEqual(18, (int)px[1]); // Brown
            Assert.AreEqual(36, (int)px[2]); // BrownVeryDark
            Assert.AreEqual(29, (int)px[3]); // Gold
        }

        [Test]
        public void Export_PixelColors_RowMajorBottomUp_IndexYWidthPlusX()
        {
            // 2x2: Cells[] is row-major bottom-up; PixelColors[y*Width+x] must match.
            var cells = new ICellData[]
            {
                Pixel("red"),      // (0,0) bottom-left
                Pixel("blue"),     // (1,0) bottom-right
                new BBEmptyCell(), // (0,1) top-left
                Pixel("green"),    // (1,1) top-right
            };
            var doc = Doc("level_1", cells, Queue(("blue", 40, false)), w: 2, h: 2);
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_1.json"));
            var px = (JArray)ReadLevel(1)["PixelColors"];
            Assert.AreEqual(9, (int)px[0 * 2 + 0]); // red
            Assert.AreEqual(1, (int)px[0 * 2 + 1]); // blue
            Assert.AreEqual(0, (int)px[1 * 2 + 0]); // empty
            Assert.AreEqual(4, (int)px[1 * 2 + 1]); // green
        }

        [Test]
        public void Export_SlotsAmount_FallsBackToDefault_WhenNoConveyorCount()
        {
            var doc = Doc("level_1", new[] { Pixel("red") }, Queue(("blue", 40, false)));
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_1.json"));
            Assert.AreEqual(5, (int)ReadLevel(1)["SlotsAmount"]); // default from SetTestDependencies
        }

        [Test]
        public void Export_RoadBlock_EmitsExactSlotConfigsJson()
        {
            var doc = Doc("level_1", new[] { Pixel("red") }, Queue(("blue", 40, false)));
            doc.GameData = new JObject { ["conveyorCount"] = 5 };
            // UI "slot 5" → 0-based internal index 4, amount 10.
            BusBuddiesSlotConfigs.SetBlocked(doc, 4, 10);
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_1.json"));

            var slotConfigs = (JArray)ReadLevel(1)["SlotConfigs"];
            Assert.AreEqual(1, slotConfigs.Count);
            Assert.AreEqual(4, (int)slotConfigs[0]["SlotIndex"]);
            Assert.AreEqual("RoadBlock", (string)slotConfigs[0]["SlotType"]);
            Assert.AreEqual(10, (int)slotConfigs[0]["RoadBlockAmount"]);
        }

        [Test]
        public void Export_NoRoadBlocks_OmitsSlotConfigsKey()
        {
            var doc = Doc("level_1", new[] { Pixel("red") }, Queue(("blue", 40, false)));
            doc.GameData = new JObject { ["conveyorCount"] = 5 };
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_1.json"));
            Assert.IsNull(ReadLevel(1)["SlotConfigs"], "no blocked slots → no SlotConfigs key");
        }

        [Test]
        public void Export_Plate_EmitsExactPlateConfigsJson()
        {
            var doc = Doc("level_1", new[] { Pixel("red") }, Queue(("blue", 40, false)), w: 20, h: 20);
            doc.GameData = new JObject { ["conveyorCount"] = 5 };
            BusBuddiesPlateConfigs.Add(doc, 5, 7, 10, 5, 80);
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_1.json"));

            var plates = (JArray)ReadLevel(1)["PlateConfigs"];
            Assert.AreEqual(1, plates.Count);
            // Position = MIN corner, lowercase x/y (Vector2Int).
            Assert.AreEqual(5,  (int)plates[0]["Position"]["x"]);
            Assert.AreEqual(7,  (int)plates[0]["Position"]["y"]);
            Assert.AreEqual(10, (int)plates[0]["Size"]["x"]);
            Assert.AreEqual(5,  (int)plates[0]["Size"]["y"]);
            Assert.AreEqual(80, (int)plates[0]["PixelAmount"]);
        }

        [Test]
        public void Export_Plate_SitsBetweenPixelColorsAndHiddenPixels()
        {
            var doc = Doc("level_1", new[] { Pixel("red") }, Queue(("blue", 40, false)), w: 20, h: 20);
            doc.GameData = new JObject { ["conveyorCount"] = 5 };
            BusBuddiesPlateConfigs.Add(doc, 0, 0, 2, 2, 3);
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_1.json"));

            var keys = new System.Collections.Generic.List<string>();
            foreach (var p in ReadLevel(1).Properties()) keys.Add(p.Name);
            int pixIdx  = keys.IndexOf("PixelColors");
            int plateIdx = keys.IndexOf("PlateConfigs");
            int hidIdx  = keys.IndexOf("HiddenPixels");
            Assert.Greater(plateIdx, pixIdx, "PlateConfigs comes after PixelColors");
            Assert.Greater(hidIdx, plateIdx, "PlateConfigs comes before HiddenPixels");
        }

        [Test]
        public void Export_NoPlates_OmitsPlateConfigsKey()
        {
            var doc = Doc("level_1", new[] { Pixel("red") }, Queue(("blue", 40, false)));
            doc.GameData = new JObject { ["conveyorCount"] = 5 };
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_1.json"));
            Assert.IsNull(ReadLevel(1)["PlateConfigs"], "no plates → no PlateConfigs key");
        }

        [Test]
        public void Export_UnmappedOrEmpty_MapsToZero()
        {
            var cells = new ICellData[] { new BBEmptyCell(), Pixel("notacolor"), Pixel("red") };
            var doc = Doc("level_1", cells, Queue(("blue", 40, false)), w: 3, h: 1);
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_1.json"));
            var px = (JArray)ReadLevel(1)["PixelColors"];
            Assert.AreEqual(0, (int)px[0]); // empty
            Assert.AreEqual(0, (int)px[1]); // unmapped
            Assert.AreEqual(9, (int)px[2]); // red
        }

        [Test]
        public void Export_LevelType_None_OmitsKey()
        {
            var doc = Doc("level_1", new[] { Pixel("red") }, Queue(("blue", 40, false)));
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_1.json"));
            Assert.IsNull(ReadLevel(1)["LevelType"], "None → no LevelType key (existing None-levels stay byte-identical)");
        }

        [Test]
        public void Export_LevelType_Hard_WritesStringName()
        {
            var doc = Doc("level_1", new[] { Pixel("red") }, Queue(("blue", 40, false)));
            BusBuddiesLevelType.Set(doc, BusLevelType.Hard);
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_1.json"));

            var token = ReadLevel(1)["LevelType"];
            Assert.AreEqual(JTokenType.String, token.Type, "wire form is the string name, not the int ordinal");
            Assert.AreEqual("Hard", (string)token);
        }

        [Test]
        public void Export_LevelType_SuperHard_WritesStringName()
        {
            var doc = Doc("level_1", new[] { Pixel("red") }, Queue(("blue", 40, false)));
            BusBuddiesLevelType.Set(doc, BusLevelType.SuperHard);
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "level_1.json"));

            var token = ReadLevel(1)["LevelType"];
            Assert.AreEqual(JTokenType.String, token.Type, "wire form is the string name, not the int ordinal");
            Assert.AreEqual("SuperHard", (string)token);
        }
    }
}
