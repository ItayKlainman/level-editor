using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesConnectedBusRoundTripTests
    {
        [Test]
        public void ConnectedBuses_SurviveRoundTrip_AsAPair()
        {
            // Two columns, one bus each, connected.
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ColorId = "red",  Capacity = 5, ConnectedId = 0 });
            var c1 = new BusColumn(); c1.Buses.Add(new BusEntry { ColorId = "blue", Capacity = 5, ConnectedId = 0 });
            q.Columns.Add(c0); q.Columns.Add(c1);
            var top = JObject.FromObject(q);

            var root = new JObject
            {
                ["SlotsAmount"] = 5, ["Width"] = 1, ["Height"] = 1,
                ["PixelColors"] = new JArray { 0 },
                ["BusColumnConfigs"] = BusBuddiesGameLevelExporter.BuildBusColumnConfigsForTest(top),
                ["ConnectedBuses"] = BusBuddiesGameLevelExporter.BuildConnectedBuses(top),
            };

            var imported = BusBuddiesGameLevelImporter.Import(root.ToString(), "level_1");
            var back = imported.Document.TopSection.ToObject<BusQueueData>();

            int idA = back.Columns[0].Buses[0].ConnectedId;
            int idB = back.Columns[1].Buses[0].ConnectedId;
            Assert.GreaterOrEqual(idA, 0, "bus A should be connected");
            Assert.AreEqual(idA, idB, "both buses share one connection id");
        }
    }
}
