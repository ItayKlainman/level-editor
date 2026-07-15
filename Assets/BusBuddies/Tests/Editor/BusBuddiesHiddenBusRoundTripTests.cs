using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesHiddenBusRoundTripTests
    {
        [Test]
        public void HiddenBus_ExportsBusType1_AndImportsHidden()
        {
            // One column: a normal red bus (cap 10) then a hidden blue bus (cap 5).
            var queue = new BusQueueData();
            var col = new BusColumn();
            col.Buses.Add(new BusEntry { ColorId = "red",  Capacity = 10, Hidden = false });
            col.Buses.Add(new BusEntry { ColorId = "blue", Capacity = 5,  Hidden = true  });
            queue.Columns.Add(col);
            var top = JObject.FromObject(queue);

            var busColumns = BusBuddiesGameLevelExporter.BuildBusColumnConfigsForTest(top);
            var busConfigs = (JArray)busColumns[0]["BusConfigs"];
            Assert.IsNull(busConfigs[0]["BusType"], "normal bus omits BusType");
            Assert.AreEqual(1, (int)busConfigs[1]["BusType"], "hidden bus → BusType 1");

            // Import a matching config and confirm Hidden survives.
            var root = new JObject
            {
                ["SlotsAmount"] = 5, ["Width"] = 1, ["Height"] = 1,
                ["PixelColors"] = new JArray { 0 },
                ["BusColumnConfigs"] = busColumns,
            };
            var imported = BusBuddiesGameLevelImporter.Import(root.ToString(), "level_1");
            var backTop = imported.Document.TopSection.ToObject<BusQueueData>();
            Assert.IsFalse(backTop.Columns[0].Buses[0].Hidden);
            Assert.IsTrue(backTop.Columns[0].Buses[1].Hidden);
        }
    }
}
