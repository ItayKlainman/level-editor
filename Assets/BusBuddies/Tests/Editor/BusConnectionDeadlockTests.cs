using Hoppa.BusBuddies.Editor;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusConnectionDeadlockTests
    {
        private static BusColumn Col(int n)
        {
            var c = new BusColumn();
            for (int i = 0; i < n; i++) c.Buses.Add(new BusEntry());
            return c;
        }

        [Test]
        public void NoConnections_NotDeadlocked()
        {
            var q = new BusQueueData(); q.Columns.Add(Col(2)); q.Columns.Add(Col(2));
            Assert.IsFalse(BusConnection.ConnectionsDeadlock(q));
        }

        [Test]
        public void HeadsPair_NotDeadlocked()
        {
            // Both partners at their column heads (pos 0) → clear together.
            var q = new BusQueueData(); q.Columns.Add(Col(2)); q.Columns.Add(Col(2));
            q.Columns[0].Buses[0].ConnectedId = 0;
            q.Columns[1].Buses[0].ConnectedId = 0;
            Assert.IsFalse(BusConnection.ConnectionsDeadlock(q));
        }

        [Test]
        public void CrossingPairs_Deadlocked()
        {
            // col0: [A(id0), B(id1)]  col1: [C(id1), D(id0)]
            // A(head col0) needs D(pos1 col1); C(head col1) needs B(pos1 col0). Neither
            // partner is at its head, and neither column can advance → soft-lock.
            var q = new BusQueueData(); q.Columns.Add(Col(2)); q.Columns.Add(Col(2));
            q.Columns[0].Buses[0].ConnectedId = 0;
            q.Columns[0].Buses[1].ConnectedId = 1;
            q.Columns[1].Buses[0].ConnectedId = 1;
            q.Columns[1].Buses[1].ConnectedId = 0;
            Assert.IsTrue(BusConnection.ConnectionsDeadlock(q));
        }

        [Test]
        public void SameColumnPair_Deadlocked()
        {
            // Two buses in one column connected → can never both be head.
            var q = new BusQueueData(); q.Columns.Add(Col(2));
            q.Columns[0].Buses[0].ConnectedId = 0;
            q.Columns[0].Buses[1].ConnectedId = 0;
            Assert.IsTrue(BusConnection.ConnectionsDeadlock(q));
        }
    }
}
