using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesConnectedBusExportTests
    {
        [Test]
        public void BuildConnectedBuses_EmitsPairCoordinates()
        {
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry()); c0.Buses.Add(new BusEntry { ConnectedId = 5 });
            var c1 = new BusColumn(); c1.Buses.Add(new BusEntry { ConnectedId = 5 });
            q.Columns.Add(c0); q.Columns.Add(c1);

            var arr = BusBuddiesGameLevelExporter.BuildConnectedBuses(JObject.FromObject(q));
            Assert.AreEqual(1, arr.Count);
            var pair = arr[0];
            // members: (col0,pos1) and (col1,pos0), in BuildConnInfo column-then-pos order.
            Assert.AreEqual(0, (int)pair["BusA"]["ColumnIndex"]);
            Assert.AreEqual(1, (int)pair["BusA"]["Index"]);
            Assert.AreEqual(1, (int)pair["BusB"]["ColumnIndex"]);
            Assert.AreEqual(0, (int)pair["BusB"]["Index"]);
        }

        [Test]
        public void BuildConnectedBuses_SkipsIncompleteGroups()
        {
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ConnectedId = 2 });
            q.Columns.Add(c0);
            Assert.AreEqual(0, BusBuddiesGameLevelExporter.BuildConnectedBuses(JObject.FromObject(q)).Count);
        }
    }
}
