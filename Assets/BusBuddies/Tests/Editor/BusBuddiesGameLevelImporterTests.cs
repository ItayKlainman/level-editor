using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Verifies BusBuddiesGameLevelImporter is the exact inverse of
    // BusBuddiesGameLevelExporter for ONE game-schema level file:
    //   PixelColors[y*Width+x] ordinals → grid cells (0 → BBEmptyCell,
    //   nonzero → BBPixelCell with the palette id), SlotsAmount → conveyorCount,
    //   BusColumnConfigs.Count recorded — reusing the exporter's color map.
    public class BusBuddiesGameLevelImporterTests
    {
        // A known 2×2 game level: PixelColors index = y*Width + x (y=0 bottom row).
        //   (0,0)=red(9)  (1,0)=blue(1)   [bottom row]
        //   (0,1)=empty(0) (1,1)=green(4) [top row]
        private const string Sample2x2 = @"{
            ""SlotsAmount"": 5,
            ""Width"": 2,
            ""Height"": 2,
            ""BusColumnConfigs"": [
                { ""BusConfigs"": [ { ""ColorType"": 9, ""Capacity"": 20 }, { ""ColorType"": 1, ""Capacity"": 25, ""BusType"": 1 } ] },
                { ""BusConfigs"": [ { ""ColorType"": 4, ""Capacity"": 15 } ] }
            ],
            ""PixelColors"": [ 9, 1, 0, 4 ]
        }";

        [Test]
        public void Import_ReconstructsGrid_WithYWidthPlusXOrdering()
        {
            var imported = BusBuddiesGameLevelImporter.Import(Sample2x2, "level_1");
            var grid = imported.Document.Grid;

            Assert.AreEqual(2, grid.Width);
            Assert.AreEqual(2, grid.Height);

            // Spot-check a few indices including the y*W+x ordering.
            Assert.AreEqual("red",  ((BBPixelCell)grid.Cells[0 * 2 + 0]).ColorId); // (0,0)
            Assert.AreEqual("blue", ((BBPixelCell)grid.Cells[0 * 2 + 1]).ColorId); // (1,0)
            Assert.IsInstanceOf<BBEmptyCell>(grid.Cells[1 * 2 + 0]);               // (0,1) empty
            Assert.AreEqual("green",((BBPixelCell)grid.Cells[1 * 2 + 1]).ColorId); // (1,1)
        }

        [Test]
        public void Import_Zero_MapsToEmptyCell()
        {
            var imported = BusBuddiesGameLevelImporter.Import(Sample2x2, "level_1");
            Assert.IsInstanceOf<BBEmptyCell>(imported.Document.Grid.Cells[2]); // the 0 ordinal
        }

        [Test]
        public void Import_SetsConveyorCountFromSlotsAmount_AndRecordsColumnCount()
        {
            var imported = BusBuddiesGameLevelImporter.Import(Sample2x2, "level_1");
            Assert.AreEqual(5, imported.SlotsAmount);
            Assert.AreEqual(5, (int)imported.Document.GameData["conveyorCount"]);
            Assert.AreEqual(2, imported.ColumnCount);                 // two BusColumnConfigs
            Assert.AreEqual(3, imported.OriginalBuses.Count);         // 2 + 1 buses
        }

        [Test]
        public void Import_OriginalBuses_CarryColorCapacityAndHidden()
        {
            var imported = BusBuddiesGameLevelImporter.Import(Sample2x2, "level_1");
            var b0 = imported.OriginalBuses[0];
            Assert.AreEqual("red", b0.ColorId);
            Assert.AreEqual(20, b0.Capacity);
            Assert.IsFalse(b0.Hidden);

            var b1 = imported.OriginalBuses[1];
            Assert.AreEqual("blue", b1.ColorId);
            Assert.IsTrue(b1.Hidden, "BusType==1 → hidden");
        }

        // The heart of the byte-consistent round-trip: the importer's ordinal→name
        // is the exact inverse of the exporter's name→ordinal, for every mapped
        // ordinal, and it stays inverse through the case-insensitive lowercased id.
        [Test]
        public void OrdinalNameRoundTrip_MatchesExporterMap()
        {
            for (int ordinal = 1; ordinal <= 36; ordinal++)
            {
                Assert.IsTrue(BusBuddiesGameLevelExporter.TryOrdinalToColorName(ordinal, out var name),
                    $"ordinal {ordinal} must map to a color name");
                // Canonical PascalCase name round-trips.
                Assert.AreEqual(ordinal, BusBuddiesGameLevelExporter.ColorIdToOrdinal(name),
                    $"name '{name}' must invert to ordinal {ordinal}");
                // Lowercased palette id (what the importer stamps on cells) also round-trips.
                Assert.AreEqual(ordinal, BusBuddiesGameLevelExporter.ColorIdToOrdinal(name.ToLowerInvariant()),
                    $"lowercased '{name}' must invert to ordinal {ordinal}");
            }
        }

        [Test]
        public void Import_ReadsPlateConfigs_IntoGameData()
        {
            const string json = @"{
                ""SlotsAmount"": 5, ""Width"": 20, ""Height"": 20,
                ""BusColumnConfigs"": [],
                ""PixelColors"": [],
                ""PlateConfigs"": [ { ""Position"": {""x"":5,""y"":7}, ""Size"": {""x"":10,""y"":5}, ""PixelAmount"": 80 } ]
            }";
            var imported = BusBuddiesGameLevelImporter.Import(json, "level_1");
            var plates = BusBuddiesPlateConfigs.All(imported.Document);
            Assert.AreEqual(1, plates.Count);
            Assert.AreEqual(5, plates[0].X);
            Assert.AreEqual(7, plates[0].Y);
            Assert.AreEqual(10, plates[0].W);
            Assert.AreEqual(5, plates[0].H);
            Assert.AreEqual(80, plates[0].Amount);
        }

        [Test]
        public void Import_NoPlateConfigs_NoPlates()
        {
            var imported = BusBuddiesGameLevelImporter.Import(Sample2x2, "level_1");
            Assert.IsEmpty(BusBuddiesPlateConfigs.All(imported.Document));
        }

        [Test]
        public void Import_UnmappedNonzeroOrdinal_FallsBackToEmpty()
        {
            const string json = @"{ ""SlotsAmount"": 5, ""Width"": 1, ""Height"": 1,
                ""BusColumnConfigs"": [], ""PixelColors"": [ 999 ] }";
            var imported = BusBuddiesGameLevelImporter.Import(json, "x");
            Assert.IsInstanceOf<BBEmptyCell>(imported.Document.Grid.Cells[0]);
        }
    }
}
