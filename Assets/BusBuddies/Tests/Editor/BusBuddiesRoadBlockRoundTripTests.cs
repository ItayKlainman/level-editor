using System.Linq;
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesRoadBlockRoundTripTests
    {
        private static JObject GameDataWithBlocks(params (int slot, int amount)[] blocks)
        {
            var doc = new LevelDocument { GameData = new JObject() };
            foreach (var b in blocks) BusBuddiesSlotConfigs.SetBlocked(doc, b.slot, b.amount);
            return doc.GameData;
        }

        [Test]
        public void RoadBlock_SurvivesRoundTrip_SlotIndexAndAmount()
        {
            var gameData = GameDataWithBlocks((4, 10), (0, 3));

            var root = new JObject
            {
                ["SlotsAmount"] = 5, ["Width"] = 1, ["Height"] = 1,
                ["PixelColors"] = new JArray { 0 },
                ["SlotConfigs"] = BusBuddiesGameLevelExporter.BuildSlotConfigsForTest(gameData),
            };

            var imported = BusBuddiesGameLevelImporter.Import(root.ToString(), "level_1");
            var blocks = BusBuddiesSlotConfigs.All(imported.Document)
                .OrderBy(s => s.SlotIndex).ToList();

            Assert.AreEqual(2, blocks.Count);
            Assert.AreEqual(0, blocks[0].SlotIndex);
            Assert.AreEqual(3, blocks[0].Amount);
            Assert.AreEqual(4, blocks[1].SlotIndex);
            Assert.AreEqual(10, blocks[1].Amount);
        }

        [Test]
        public void Import_AcceptsSlotTypeAsIntOrdinal_One()
        {
            var root = new JObject
            {
                ["SlotsAmount"] = 5, ["Width"] = 1, ["Height"] = 1,
                ["PixelColors"] = new JArray { 0 },
                ["SlotConfigs"] = new JArray
                {
                    new JObject { ["SlotIndex"] = 2, ["SlotType"] = 1, ["RoadBlockAmount"] = 7 },
                },
            };

            var imported = BusBuddiesGameLevelImporter.Import(root.ToString(), "level_1");
            Assert.IsTrue(BusBuddiesSlotConfigs.TryGet(imported.Document, 2, out var c));
            Assert.AreEqual(7, c.Amount);
        }

        [Test]
        public void Import_NoSlotConfigs_NothingBlocked()
        {
            var root = new JObject
            {
                ["SlotsAmount"] = 5, ["Width"] = 1, ["Height"] = 1,
                ["PixelColors"] = new JArray { 0 },
            };
            var imported = BusBuddiesGameLevelImporter.Import(root.ToString(), "level_1");
            Assert.IsEmpty(BusBuddiesSlotConfigs.All(imported.Document));
        }
    }
}
