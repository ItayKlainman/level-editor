using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesLevelTypeTests
    {
        private static LevelDocument Doc() =>
            new LevelDocument { SchemaVersion = "busbuddies.v1", GameData = new JObject() };

        [Test]
        public void Default_IsNone()
        {
            var doc = Doc();
            Assert.AreEqual(BusLevelType.None, BusBuddiesLevelType.Get(doc));
        }

        [Test]
        public void Default_NoGameData_IsNone()
        {
            var doc = new LevelDocument { SchemaVersion = "busbuddies.v1" };
            Assert.AreEqual(BusLevelType.None, BusBuddiesLevelType.Get(doc));
        }

        [Test]
        public void Set_Hard_RoundTrips()
        {
            var doc = Doc();
            BusBuddiesLevelType.Set(doc, BusLevelType.Hard);

            Assert.AreEqual(BusLevelType.Hard, BusBuddiesLevelType.Get(doc));
            // Stored internally as the int ordinal, per the approved design.
            Assert.AreEqual(1, (int)doc.GameData["levelType"]);
        }

        [Test]
        public void Set_SuperHard_RoundTrips()
        {
            var doc = Doc();
            BusBuddiesLevelType.Set(doc, BusLevelType.SuperHard);

            Assert.AreEqual(BusLevelType.SuperHard, BusBuddiesLevelType.Get(doc));
            Assert.AreEqual(2, (int)doc.GameData["levelType"]);
        }

        [Test]
        public void Set_None_RemovesKey_Sparse()
        {
            var doc = Doc();
            BusBuddiesLevelType.Set(doc, BusLevelType.Hard);
            BusBuddiesLevelType.Set(doc, BusLevelType.None);

            Assert.IsNull(doc.GameData["levelType"], "None is stored sparsely — the key is removed");
            Assert.AreEqual(BusLevelType.None, BusBuddiesLevelType.Get(doc));
        }
    }
}
