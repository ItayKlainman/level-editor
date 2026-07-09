using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Task 8: the panel's logic-only state <-> GameData round-trip. The GUI itself
    // is not unit-tested.
    public sealed class BusBuddiesDifficultyPanelTests
    {
        private static LevelDocument Doc() => new LevelDocument
        {
            SchemaVersion = "busbuddies", LevelId = "t",
            Grid = new GridData<ICellData>(1, 1),
            GameData = new JObject(),
        };

        [Test]
        public void PanelState_RoundTrips_ThroughGameData()
        {
            var panel = new BusBuddiesDifficultyPanel
            {
                Settings = new BusBuddiesDifficultySettings
                {
                    BusesChunks = 5, DeviationPercent = 0.2f, Columns = 2,
                    Difficulty = 4, NoSingleBusColor = true, RoundToFive = true,
                }
            };
            var doc = Doc();
            panel.WriteTo(doc);

            var reopened = new BusBuddiesDifficultyPanel();
            reopened.LoadFrom(doc, null);

            var s = reopened.Settings;
            Assert.AreEqual(5, s.BusesChunks);
            Assert.AreEqual(0.2f, s.DeviationPercent, 1e-5f);
            Assert.AreEqual(2, s.Columns);
            Assert.AreEqual(4, s.Difficulty);
            Assert.IsTrue(s.NoSingleBusColor);
            Assert.IsTrue(s.RoundToFive);
        }

        [Test]
        public void LoadFrom_EmptyGameData_UsesDefaults()
        {
            var panel = new BusBuddiesDifficultyPanel();
            panel.LoadFrom(Doc(), null);
            Assert.AreEqual(3, panel.Settings.BusesChunks);
            Assert.AreEqual(3, panel.Settings.Difficulty);
        }
    }
}
