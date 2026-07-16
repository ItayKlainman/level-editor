using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Task 1: the six designer knobs persist into GameData under stable bb.* keys
    // and round-trip; missing keys fall back to config defaults; values clamp.
    public sealed class BusBuddiesDifficultySettingsTests
    {
        private static LevelDocument Doc(JObject gd = null) => new LevelDocument
        {
            SchemaVersion = "busbuddies", LevelId = "t",
            Grid = new GridData<ICellData>(1, 1),
            GameData = gd,
        };

        [Test]
        public void WriteThenRead_IsIdentity()
        {
            var s = new BusBuddiesDifficultySettings
            {
                BusesChunks = 4, DeviationPercent = 0.25f, Columns = 2,
                Difficulty = 5, NoSingleBusColor = true, RoundToFive = true,
            };
            var doc = Doc(new JObject());
            s.WriteTo(doc);

            var r = BusBuddiesDifficultySettings.ReadFrom(doc, null);
            Assert.AreEqual(4, r.BusesChunks);
            Assert.AreEqual(0.25f, r.DeviationPercent, 1e-5f);
            Assert.AreEqual(2, r.Columns);
            Assert.AreEqual(5, r.Difficulty);
            Assert.IsTrue(r.NoSingleBusColor);
            Assert.IsTrue(r.RoundToFive);
        }

        [Test]
        public void MissingKeys_FallBackToConfigDefaults()
        {
            var cfg = ScriptableObject.CreateInstance<BusBuddiesAutofillConfig>();
            cfg.DefaultChunks = 2; cfg.DefaultDeviation = 0.3f; cfg.DefaultColumns = 4;
            cfg.DefaultDifficulty = 1; cfg.DefaultNoSingleBusColor = true; cfg.DefaultRoundToFive = true;

            var r = BusBuddiesDifficultySettings.ReadFrom(Doc(new JObject()), cfg);

            Assert.AreEqual(2, r.BusesChunks);
            Assert.AreEqual(0.3f, r.DeviationPercent, 1e-5f);
            Assert.AreEqual(4, r.Columns);
            Assert.AreEqual(1, r.Difficulty);
            Assert.IsTrue(r.NoSingleBusColor);
            Assert.IsTrue(r.RoundToFive);
        }

        [Test]
        public void NullGameData_UsesDefaults()
        {
            var r = BusBuddiesDifficultySettings.ReadFrom(Doc(null), null);
            Assert.AreEqual(3, r.BusesChunks);   // built-in defaults
            Assert.AreEqual(3, r.Columns);
            Assert.AreEqual(3, r.Difficulty);
        }

        [Test]
        public void ReadFrom_ClampsOutOfRangeValues()
        {
            var gd = new JObject
            {
                [BusBuddiesDifficultySettings.KeyBusesChunks] = 99,
                [BusBuddiesDifficultySettings.KeyColumns]     = 0,
                [BusBuddiesDifficultySettings.KeyDifficulty]  = -3,
                [BusBuddiesDifficultySettings.KeyDeviation]   = 4.5f,
            };
            var r = BusBuddiesDifficultySettings.ReadFrom(Doc(gd), null);
            Assert.AreEqual(10, r.BusesChunks);   // Buses Chunks now clamps to 1..10
            Assert.AreEqual(1, r.Columns);
            Assert.AreEqual(1, r.Difficulty);
            Assert.AreEqual(1f, r.DeviationPercent, 1e-5f);
        }

        [Test]
        public void WriteTo_CreatesGameDataWhenAbsent()
        {
            var doc = Doc(null);
            new BusBuddiesDifficultySettings { BusesChunks = 2 }.WriteTo(doc);
            Assert.IsNotNull(doc.GameData);
            Assert.AreEqual(2, (int)doc.GameData[BusBuddiesDifficultySettings.KeyBusesChunks]);
        }
    }
}
