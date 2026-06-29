using NUnit.Framework;
using Hoppa.BusBuddies;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusQueueDataTests
    {
        [Test]
        public void Entry_Defaults()
        {
            var e = new BusEntry();
            Assert.AreEqual(-1, e.ConnectedId);
            Assert.IsFalse(e.Hidden);
        }

        [Test]
        public void Queue_ColumnsAndBusesOrdered()
        {
            var q = new BusQueueData();
            var col = new BusColumn();
            col.Buses.Add(new BusEntry { ColorId = "A", Capacity = 3 });
            col.Buses.Add(new BusEntry { ColorId = "B", Capacity = 2, Hidden = true, ConnectedId = 7 });
            q.Columns.Add(col);

            Assert.AreEqual(1, q.Columns.Count);
            Assert.AreEqual(2, q.Columns[0].Buses.Count);
            Assert.AreEqual("A", q.Columns[0].Buses[0].ColorId);
            Assert.AreEqual(3, q.Columns[0].Buses[0].Capacity);
            Assert.IsTrue(q.Columns[0].Buses[1].Hidden);
            Assert.AreEqual(7, q.Columns[0].Buses[1].ConnectedId);
        }
    }
}
