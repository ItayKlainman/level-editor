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
    // Mirrors YarnMasterLevelExporterTests. Verifies BusBuddiesLevelExporter emits the
    // YarnKingdom level_config schema:
    //   { "LevelConfigs": { "<key>": { levelId, ConveyorCount, Width, Height,
    //       SpoolColumnConfigs[SpoolConfigs[ColorType,Capacity,IsHidden]], PixelColors[] } } }
    public class BusBuddiesLevelExporterTests
    {
        private string _tempFile;
        private string _levelFile;
        private BusBuddiesLevelExporter _exporter;

        [SetUp]
        public void SetUp()
        {
            _tempFile  = Path.Combine(Path.GetTempPath(), "bb_level_config_test.json");
            _levelFile = Path.Combine(Path.GetTempPath(), "busbuddies_001.json");
            if (File.Exists(_tempFile)) File.Delete(_tempFile);

            _exporter = ScriptableObject.CreateInstance<BusBuddiesLevelExporter>();
            // Rely on the exporter's built-in 8-color default map.
            _exporter.SetTestDependencies(_tempFile, 5);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
        }

        // ── Filename → key parsing ──────────────────────────────────────

        [Test]
        public void Export_ParsesFileName_001_ToKey1()
        {
            var doc = MakeDoc("busbuddies_001", new[] { PixelCell("red") }, MakeQueue());
            Assert.IsTrue(_exporter.Export(doc, new CellTypeRegistry(), _levelFile));
            var configs = ReadOutput()["LevelConfigs"].ToObject<JObject>();
            Assert.IsTrue(configs.ContainsKey("1"));
            Assert.AreEqual("busbuddies_001", (string)configs["1"]["levelId"]);
        }

        [Test]
        public void Export_NullFilePath_ReturnsFalse()
        {
            var doc = MakeDoc("busbuddies_001", new[] { PixelCell("red") }, MakeQueue());
            Assert.IsFalse(_exporter.Export(doc, new CellTypeRegistry(), null));
        }

        [Test]
        public void Export_FilenameWithNoNumber_ReturnsFalse()
        {
            string noNumberFile = Path.Combine(Path.GetTempPath(), "my_bus_level.json");
            var doc = MakeDoc("my_bus_level", new[] { PixelCell("red") }, MakeQueue());
            Assert.IsFalse(_exporter.Export(doc, new CellTypeRegistry(), noNumberFile));
        }

        // ── Schema shape ────────────────────────────────────────────────

        [Test]
        public void Export_WritesExpectedSchemaShape()
        {
            var cells = new ICellData[] { PixelCell("red"), PixelCell("blue") };
            var doc = MakeDoc("busbuddies_007", cells, MakeQueue("green", 8, true), width: 2, height: 1);
            doc.GameData = new JObject { ["conveyorCount"] = 4 };
            _exporter.Export(doc, new CellTypeRegistry(), Path.Combine(Path.GetTempPath(), "busbuddies_007.json"));

            var entry = ReadOutput()["LevelConfigs"]["7"];
            Assert.AreEqual(4, (int)entry["ConveyorCount"]);
            Assert.AreEqual(2, (int)entry["Width"]);
            Assert.AreEqual(1, (int)entry["Height"]);
            Assert.AreEqual(2, ((JArray)entry["PixelColors"]).Count);
            var spoolCol = entry["SpoolColumnConfigs"][0]["SpoolConfigs"][0];
            Assert.AreEqual(4,    (int)spoolCol["ColorType"]);  // green
            Assert.AreEqual(8,    (int)spoolCol["Capacity"]);
            Assert.AreEqual(true, (bool)spoolCol["IsHidden"]);
        }

        // ── Color-id → int mapping ──────────────────────────────────────

        [Test]
        public void Export_PixelColor_MapsToOrdinal()
        {
            // red=9, blue=1, green=4
            var cells = new ICellData[] { PixelCell("red"), PixelCell("blue"), PixelCell("green") };
            var doc = MakeDoc("busbuddies_001", cells, MakeQueue(), width: 3, height: 1);
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            var px = (JArray)ReadOutput()["LevelConfigs"]["1"]["PixelColors"];
            Assert.AreEqual(9, (int)px[0]);
            Assert.AreEqual(1, (int)px[1]);
            Assert.AreEqual(4, (int)px[2]);
        }

        [Test]
        public void Export_ColorId_IsCaseInsensitive()
        {
            var cells = new ICellData[] { PixelCell("RED"), PixelCell("Blue") };
            var doc = MakeDoc("busbuddies_001", cells, MakeQueue("PURPLE", 5, false), width: 2, height: 1);
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            var entry = ReadOutput()["LevelConfigs"]["1"];
            var px = (JArray)entry["PixelColors"];
            Assert.AreEqual(9, (int)px[0]);   // RED → red → 9
            Assert.AreEqual(1, (int)px[1]);   // Blue → blue → 1
            Assert.AreEqual(8, (int)entry["SpoolColumnConfigs"][0]["SpoolConfigs"][0]["ColorType"]); // PURPLE → 8
        }

        [Test]
        public void Export_UnmappedColor_MapsToZero()
        {
            var cells = new ICellData[] { PixelCell("magenta"), PixelCell("red") };
            var doc = MakeDoc("busbuddies_001", cells, MakeQueue(), width: 2, height: 1);
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            var px = (JArray)ReadOutput()["LevelConfigs"]["1"]["PixelColors"];
            Assert.AreEqual(0, (int)px[0]);   // magenta unmapped
            Assert.AreEqual(9, (int)px[1]);
        }

        [Test]
        public void Export_EmptyCell_MapsToZero()
        {
            var cells = new ICellData[] { new BBEmptyCell(), PixelCell("red") };
            var doc = MakeDoc("busbuddies_001", cells, MakeQueue(), width: 2, height: 1);
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            var px = (JArray)ReadOutput()["LevelConfigs"]["1"]["PixelColors"];
            Assert.AreEqual(0, (int)px[0]);   // empty
            Assert.AreEqual(9, (int)px[1]);
        }

        [Test]
        public void Export_NullBusColorId_MapsToZero()
        {
            var queue = new BusQueueData
            {
                Columns = new List<BusColumn>
                {
                    new BusColumn { Buses = new List<BusEntry>
                        { new BusEntry { ColorId = null, Capacity = 5, Hidden = false } } },
                }
            };
            var doc = MakeDoc("busbuddies_001", new[] { PixelCell("red") }, JObject.FromObject(queue));
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);
            var spool = ReadOutput()["LevelConfigs"]["1"]["SpoolColumnConfigs"][0]["SpoolConfigs"][0];
            Assert.AreEqual(0, (int)spool["ColorType"]);
        }

        // ── Multi-row layout: row-major bottom-up (index = y*Width + x, y=0 = bottom) ──

        [Test]
        public void Export_MultiRowGrid_PixelColors_MatchRowMajorBottomUpLayout()
        {
            // 2x2 grid (width=2, height=2). Cells[] is already row-major bottom-up, so
            // Cells[i] corresponds to (x = i % W, y = i / W), y=0 = bottom row:
            //   index 0 -> (x=0,y=0) bottom-left  -> red   (9)
            //   index 1 -> (x=1,y=0) bottom-right -> blue  (1)
            //   index 2 -> (x=0,y=1) top-left     -> empty (0)
            //   index 3 -> (x=1,y=1) top-right    -> green (4)
            var cells = new ICellData[]
            {
                PixelCell("red"),
                PixelCell("blue"),
                new BBEmptyCell(),
                PixelCell("green"),
            };
            var doc = MakeDoc("busbuddies_001", cells, MakeQueue(), width: 2, height: 2);
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);

            var px = (JArray)ReadOutput()["LevelConfigs"]["1"]["PixelColors"];
            Assert.AreEqual(4, px.Count);
            // PixelColors[y*Width + x] must match the cell placed at (x,y).
            Assert.AreEqual(9, (int)px[0 * 2 + 0]); // (0,0) bottom-left  -> red
            Assert.AreEqual(1, (int)px[0 * 2 + 1]); // (1,0) bottom-right -> blue
            Assert.AreEqual(0, (int)px[1 * 2 + 0]); // (0,1) top-left     -> empty
            Assert.AreEqual(4, (int)px[1 * 2 + 1]); // (1,1) top-right    -> green
        }

        [Test]
        public void Export_MultiRowGrid_3x2_PixelColors_MatchRowMajorBottomUpLayout()
        {
            // 3x2 grid (width=3, height=2), one distinct color per cell, to pin down
            // ordering across a non-square grid too.
            var cells = new ICellData[]
            {
                PixelCell("red"),    PixelCell("blue"),  PixelCell("green"), // y=0 (bottom row)
                PixelCell("yellow"), new BBEmptyCell(),  PixelCell("cyan"),  // y=1 (top row)
            };
            var doc = MakeDoc("busbuddies_001", cells, MakeQueue(), width: 3, height: 2);
            _exporter.Export(doc, new CellTypeRegistry(), _levelFile);

            var px = (JArray)ReadOutput()["LevelConfigs"]["1"]["PixelColors"];
            Assert.AreEqual(6, px.Count);
            Assert.AreEqual(9, (int)px[0 * 3 + 0]); // (0,0) -> red
            Assert.AreEqual(1, (int)px[0 * 3 + 1]); // (1,0) -> blue
            Assert.AreEqual(4, (int)px[0 * 3 + 2]); // (2,0) -> green
            Assert.AreEqual(3, (int)px[1 * 3 + 0]); // (0,1) -> yellow
            Assert.AreEqual(0, (int)px[1 * 3 + 1]); // (1,1) -> empty
            Assert.AreEqual(2, (int)px[1 * 3 + 2]); // (2,1) -> cyan
        }

        // ── Upsert by levelId ───────────────────────────────────────────

        [Test]
        public void Export_Upsert_ByLevelId_PreservesExistingKey()
        {
            // Seed a master where levelId "busbuddies_777" already lives at key "5".
            var seeded = new JObject
            {
                ["LevelConfigs"] = new JObject
                {
                    ["5"] = new JObject { ["levelId"] = "busbuddies_777", ["Width"] = 1, ["Height"] = 1 }
                }
            };
            File.WriteAllText(_tempFile, seeded.ToString());

            string file777 = Path.Combine(Path.GetTempPath(), "busbuddies_777.json");
            var doc = MakeDoc("busbuddies_777", new[] { PixelCell("green") }, MakeQueue());
            _exporter.Export(doc, new CellTypeRegistry(), file777);

            var configs = ReadOutput()["LevelConfigs"].ToObject<JObject>();
            Assert.AreEqual(1, configs.Count);                               // no new slot created
            Assert.IsTrue(configs.ContainsKey("5"));                          // reused existing key
            Assert.IsFalse(configs.ContainsKey("777"));
            Assert.AreEqual(4, (int)configs["5"]["PixelColors"][0]);          // green → 4, updated in place
        }

        [Test]
        public void Export_Upsert_OverwritesExistingLevelData()
        {
            var doc1 = MakeDoc("busbuddies_001", new[] { PixelCell("red") }, MakeQueue());
            _exporter.Export(doc1, new CellTypeRegistry(), _levelFile);

            var doc2 = MakeDoc("busbuddies_001", new[] { PixelCell("green") }, MakeQueue());
            _exporter.Export(doc2, new CellTypeRegistry(), _levelFile);

            var configs = ReadOutput()["LevelConfigs"].ToObject<JObject>();
            Assert.AreEqual(1, configs.Count);
            Assert.AreEqual(4, (int)configs["1"]["PixelColors"][0]);  // overwritten with green
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static ICellData PixelCell(string color) => new BBPixelCell { ColorId = color };

        private static JObject MakeQueue(string busColor = "blue", int capacity = 10, bool hidden = false)
        {
            var data = new BusQueueData
            {
                Columns = new List<BusColumn>
                {
                    new BusColumn { Buses = new List<BusEntry>
                        { new BusEntry { ColorId = busColor, Capacity = capacity, Hidden = hidden } } },
                    new BusColumn { Buses = new List<BusEntry>() },
                }
            };
            return JObject.FromObject(data);
        }

        private static LevelDocument MakeDoc(string levelId, ICellData[] cells,
            JObject topSection, int width = 1, int height = 1)
        {
            var grid = new GridData<ICellData>(width, height);
            for (int i = 0; i < cells.Length; i++) grid.Cells[i] = cells[i];
            return new LevelDocument
            {
                LevelId       = levelId,
                SchemaVersion = "busbuddies.v1",
                Grid          = grid,
                TopSection    = topSection,
            };
        }

        private JObject ReadOutput() => JObject.Parse(File.ReadAllText(_tempFile));
    }
}
