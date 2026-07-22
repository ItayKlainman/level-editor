using System;
using System.IO;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Designer round-trip: importing a game level must rebuild the editable bus queue,
    // the importer asset must recognize the game schema (and only that), and a
    // straight open→save must reproduce the game file (pixels + buses + dims).
    public class BusBuddiesGameRoundTripTests
    {
        // Same 2×2 as the importer tests: 2 columns (red/blue-hidden, green), pixels [9,1,0,4].
        private const string Sample2x2 = @"{
            ""SlotsAmount"": 5, ""Width"": 2, ""Height"": 2,
            ""BusColumnConfigs"": [
                { ""BusConfigs"": [ { ""ColorType"": 9, ""Capacity"": 20 }, { ""ColorType"": 1, ""Capacity"": 25, ""BusType"": 1 } ] },
                { ""BusConfigs"": [ { ""ColorType"": 4, ""Capacity"": 15 } ] }
            ],
            ""PixelColors"": [ 9, 1, 0, 4 ]
        }";

        [Test]
        public void Import_RebuildsBusQueue_IntoEditableTopSection()
        {
            var doc = BusBuddiesGameLevelImporter.Import(Sample2x2, "level_1").Document;
            Assert.IsNotNull(doc.TopSection, "imported level must carry an editable bus queue");

            var queue = doc.TopSection.ToObject<BusQueueData>();
            Assert.AreEqual(2, queue.Columns.Count, "two columns preserved");
            Assert.AreEqual(2, queue.Columns[0].Buses.Count);
            Assert.AreEqual(1, queue.Columns[1].Buses.Count);

            var red = queue.Columns[0].Buses[0];
            Assert.AreEqual("red", red.ColorId);
            Assert.AreEqual(20, red.Capacity);
            Assert.IsFalse(red.Hidden);

            var blue = queue.Columns[0].Buses[1];
            Assert.AreEqual("blue", blue.ColorId);
            Assert.IsTrue(blue.Hidden);

            Assert.AreEqual("green", queue.Columns[1].Buses[0].ColorId);
            Assert.AreEqual(15, queue.Columns[1].Buses[0].Capacity);
        }

        [Test]
        public void ImporterAsset_CanImport_GameSchemaTrue_NativeAndGarbageFalse()
        {
            var asset = ScriptableObject.CreateInstance<BusBuddiesGameLevelImporterAsset>();
            try
            {
                Assert.IsTrue(asset.CanImport(Sample2x2), "game schema recognized");
                // A native editor LevelDocument JSON must NOT be claimed.
                Assert.IsFalse(asset.CanImport(@"{ ""schemaVersion"": ""busbuddies.v1"", ""grid"": { ""cells"": [] } }"));
                Assert.IsFalse(asset.CanImport("not json at all"));
                Assert.IsFalse(asset.CanImport(""));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void Profile_Importers_EmptyByDefault()
        {
            var profile = ScriptableObject.CreateInstance<GameProfile>();
            try { Assert.IsNotNull(profile.Importers); Assert.AreEqual(0, profile.Importers.Count); }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void RoundTrip_Plates_SurviveExportThenImport()
        {
            // Start from an imported level, add a plate, export, then re-import and
            // confirm the plate rect + amount survive the trip byte-for-byte.
            var doc = BusBuddiesGameLevelImporter.Import(Sample2x2, "level_1").Document;
            BusBuddiesPlateConfigs.Add(doc, 0, 1, 2, 1, 3);

            var exporter = ScriptableObject.CreateInstance<BusBuddiesGameLevelExporter>();
            string dir = Path.Combine(Path.GetTempPath(), "bb_plate_rt_" + Guid.NewGuid().ToString("N"));
            try
            {
                exporter.SetTestDependencies(dir, 5);
                Assert.IsTrue(exporter.Export(doc, null, "level_1.json"));

                string outJson = File.ReadAllText(Path.Combine(dir, "level_1.json"));
                var reimported = BusBuddiesGameLevelImporter.Import(outJson, "level_1").Document;

                var plates = BusBuddiesPlateConfigs.All(reimported);
                Assert.AreEqual(1, plates.Count);
                Assert.AreEqual(0, plates[0].X);
                Assert.AreEqual(1, plates[0].Y);
                Assert.AreEqual(2, plates[0].W);
                Assert.AreEqual(1, plates[0].H);
                Assert.AreEqual(3, plates[0].Amount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(exporter);
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }
        }

        [Test]
        public void RoundTrip_OpenThenSave_ReproducesGameFile()
        {
            var doc = BusBuddiesGameLevelImporter.Import(Sample2x2, "level_1").Document;

            var exporter = ScriptableObject.CreateInstance<BusBuddiesGameLevelExporter>();
            string dir = Path.Combine(Path.GetTempPath(), "bb_rt_" + Guid.NewGuid().ToString("N"));
            try
            {
                exporter.SetTestDependencies(dir, 5);
                Assert.IsTrue(exporter.Export(doc, null, "level_1.json"));

                var outJson = JObject.Parse(File.ReadAllText(Path.Combine(dir, "level_1.json")));
                Assert.AreEqual(2, (int)outJson["Width"]);
                Assert.AreEqual(2, (int)outJson["Height"]);
                Assert.AreEqual(5, (int)outJson["SlotsAmount"]);

                // Pixels identical to the source.
                CollectionAssert.AreEqual(new[] { 9, 1, 0, 4 },
                    outJson["PixelColors"].ToObject<int[]>());

                // Bus columns identical: [ [red20, blue25 hidden], [green15] ].
                var cols = (JArray)outJson["BusColumnConfigs"];
                Assert.AreEqual(2, cols.Count);
                var c0 = (JArray)cols[0]["BusConfigs"];
                Assert.AreEqual(9, (int)c0[0]["ColorType"]);
                Assert.AreEqual(20, (int)c0[0]["Capacity"]);
                Assert.AreEqual(1, (int)c0[1]["ColorType"]);
                Assert.AreEqual(1, (int)c0[1]["BusType"]); // hidden preserved
                var c1 = (JArray)cols[1]["BusConfigs"];
                Assert.AreEqual(4, (int)c1[0]["ColorType"]);
                Assert.AreEqual(15, (int)c1[0]["Capacity"]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(exporter);
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
