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

        [Test]
        public void BuildConnectedBuses_OrdersByAscendingConnectionId_Deterministically()
        {
            // Two complete pairs, authored with the HIGHER id (5) appearing first in bus scan
            // order and the LOWER id (2) second — a Dictionary iteration order would emit id 5
            // first. Assert the export is nonetheless ordered ascending by connection id.
            var q = new BusQueueData();
            var c0 = new BusColumn();
            c0.Buses.Add(new BusEntry { ConnectedId = 5 }); // pair "5", bus 1 of 2
            c0.Buses.Add(new BusEntry { ConnectedId = 2 }); // pair "2", bus 1 of 2
            var c1 = new BusColumn();
            c1.Buses.Add(new BusEntry { ConnectedId = 5 }); // pair "5", bus 2 of 2
            c1.Buses.Add(new BusEntry { ConnectedId = 2 }); // pair "2", bus 2 of 2
            q.Columns.Add(c0); q.Columns.Add(c1);

            var arr = BusBuddiesGameLevelExporter.BuildConnectedBuses(JObject.FromObject(q));
            Assert.AreEqual(2, arr.Count);

            // Pair "2" (lower id) must come first: BusA at (col0,pos1), BusB at (col1,pos1).
            Assert.AreEqual(0, (int)arr[0]["BusA"]["ColumnIndex"]);
            Assert.AreEqual(1, (int)arr[0]["BusA"]["Index"]);
            Assert.AreEqual(1, (int)arr[0]["BusB"]["ColumnIndex"]);
            Assert.AreEqual(1, (int)arr[0]["BusB"]["Index"]);

            // Pair "5" (higher id) second: BusA at (col0,pos0), BusB at (col1,pos0).
            Assert.AreEqual(0, (int)arr[1]["BusA"]["ColumnIndex"]);
            Assert.AreEqual(0, (int)arr[1]["BusA"]["Index"]);
            Assert.AreEqual(1, (int)arr[1]["BusB"]["ColumnIndex"]);
            Assert.AreEqual(0, (int)arr[1]["BusB"]["Index"]);
        }
    }
}
