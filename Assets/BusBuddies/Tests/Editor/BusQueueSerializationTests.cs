using NUnit.Framework;
using Hoppa.BusBuddies;
using Newtonsoft.Json.Linq;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusQueueSerializationTests
    {
        [Test]
        public void BusQueueData_RoundTripsThroughTopSectionJObject()
        {
            var q = new BusQueueData();
            var c0 = new BusColumn();
            c0.Buses.Add(new BusEntry { ColorId = "red", Capacity = 12, Hidden = true, ConnectedId = 3 });
            c0.Buses.Add(new BusEntry { ColorId = "blue", Capacity = 7 });
            q.Columns.Add(c0);
            q.Columns.Add(new BusColumn()); // empty column survives

            JObject top = JObject.FromObject(q);     // emit (LevelDocument.TopSection payload)
            var back = top.ToObject<BusQueueData>();   // parse (exactly what the analyzer does)

            Assert.AreEqual(2, back.Columns.Count);
            Assert.AreEqual(2, back.Columns[0].Buses.Count);
            Assert.AreEqual("red", back.Columns[0].Buses[0].ColorId);
            Assert.AreEqual(12, back.Columns[0].Buses[0].Capacity);
            Assert.IsTrue(back.Columns[0].Buses[0].Hidden);
            Assert.AreEqual(3, back.Columns[0].Buses[0].ConnectedId);
            Assert.AreEqual("blue", back.Columns[0].Buses[1].ColorId);
            Assert.AreEqual(-1, back.Columns[0].Buses[1].ConnectedId); // default preserved
            Assert.AreEqual(0, back.Columns[1].Buses.Count);
        }
    }
}
