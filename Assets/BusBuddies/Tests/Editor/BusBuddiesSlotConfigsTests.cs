using System.Linq;
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesSlotConfigsTests
    {
        private static LevelDocument Doc() =>
            new LevelDocument { SchemaVersion = "busbuddies.v1", GameData = new JObject() };

        [Test]
        public void Default_NothingBlocked()
        {
            var doc = Doc();
            Assert.IsEmpty(BusBuddiesSlotConfigs.All(doc));
            Assert.IsFalse(BusBuddiesSlotConfigs.IsBlocked(doc, 4));
        }

        [Test]
        public void SetBlocked_AddsSparseEntry_RoundTrips()
        {
            var doc = Doc();
            BusBuddiesSlotConfigs.SetBlocked(doc, 4, 10);

            var all = BusBuddiesSlotConfigs.All(doc);
            Assert.AreEqual(1, all.Count, "only the blocked slot is stored (sparse)");
            Assert.AreEqual(4, all[0].SlotIndex);
            Assert.AreEqual(10, all[0].Amount);
            Assert.IsTrue(BusBuddiesSlotConfigs.IsBlocked(doc, 4));

            // Stored shape matches the documented JArray of {slotIndex, amount}.
            var arr = (JArray)doc.GameData["slotConfigs"];
            Assert.AreEqual(4, (int)arr[0]["slotIndex"]);
            Assert.AreEqual(10, (int)arr[0]["amount"]);
        }

        [Test]
        public void SetBlocked_Twice_UpdatesAmount_NoDuplicate()
        {
            var doc = Doc();
            BusBuddiesSlotConfigs.SetBlocked(doc, 2, 5);
            BusBuddiesSlotConfigs.SetBlocked(doc, 2, 8);

            var all = BusBuddiesSlotConfigs.All(doc);
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(8, all[0].Amount);
        }

        [Test]
        public void Clear_RemovesEntry()
        {
            var doc = Doc();
            BusBuddiesSlotConfigs.SetBlocked(doc, 1, 3);
            BusBuddiesSlotConfigs.SetBlocked(doc, 3, 7);
            BusBuddiesSlotConfigs.Clear(doc, 1);

            var all = BusBuddiesSlotConfigs.All(doc);
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(3, all[0].SlotIndex);
            Assert.IsFalse(BusBuddiesSlotConfigs.IsBlocked(doc, 1));
        }

        [Test]
        public void SetBlocked_ClampsAmountToAtLeastOne()
        {
            var doc = Doc();
            BusBuddiesSlotConfigs.SetBlocked(doc, 0, 0);
            BusBuddiesSlotConfigs.TryGet(doc, 0, out var c0);
            Assert.AreEqual(1, c0.Amount);

            BusBuddiesSlotConfigs.SetBlocked(doc, 0, -5);
            BusBuddiesSlotConfigs.TryGet(doc, 0, out var c1);
            Assert.AreEqual(1, c1.Amount);
        }

        [Test]
        public void SetAmount_ClampsAndUpdates_OnlyWhenBlocked()
        {
            var doc = Doc();
            // No-op when not blocked.
            BusBuddiesSlotConfigs.SetAmount(doc, 4, 9);
            Assert.IsFalse(BusBuddiesSlotConfigs.IsBlocked(doc, 4));

            BusBuddiesSlotConfigs.SetBlocked(doc, 4, 10);
            BusBuddiesSlotConfigs.SetAmount(doc, 4, 0);
            BusBuddiesSlotConfigs.TryGet(doc, 4, out var c);
            Assert.AreEqual(1, c.Amount, "amount clamps to >= 1");
        }

        [Test]
        public void All_Sparse_OnlyBlockedSlotsPresent()
        {
            var doc = Doc();
            BusBuddiesSlotConfigs.SetBlocked(doc, 0, 2);
            BusBuddiesSlotConfigs.SetBlocked(doc, 4, 6);
            var indices = BusBuddiesSlotConfigs.All(doc).Select(s => s.SlotIndex).OrderBy(i => i).ToList();
            CollectionAssert.AreEqual(new[] { 0, 4 }, indices);
        }
    }
}
