using System.Collections.Generic;
using Hoppa.BusBuddies.Editor;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusConnectionTests
    {
        private static BusQueueData TwoColumns()
        {
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry()); c0.Buses.Add(new BusEntry());
            var c1 = new BusColumn(); c1.Buses.Add(new BusEntry()); c1.Buses.Add(new BusEntry());
            q.Columns.Add(c0); q.Columns.Add(c1);
            return q;
        }

        [Test]
        public void AllocId_StartsAtZero_ThenIncrements()
        {
            var q = TwoColumns();
            Assert.AreEqual(0, BusConnection.AllocId(q));
            q.Columns[0].Buses[0].ConnectedId = 0;
            Assert.AreEqual(1, BusConnection.AllocId(q));
        }

        [Test]
        public void BuildConnInfo_GroupsMembers_AndFindsPending()
        {
            var q = TwoColumns();
            q.Columns[0].Buses[0].ConnectedId = 0; // pending single
            q.Columns[0].Buses[1].ConnectedId = 1;
            q.Columns[1].Buses[1].ConnectedId = 1; // complete pair
            BusConnection.BuildConnInfo(q, out var members, out var pending);

            Assert.AreEqual(2, members.Count);
            Assert.AreEqual(1, members[0].Count);
            Assert.AreEqual(2, members[1].Count);
            Assert.AreEqual(0, pending);
        }

        [Test]
        public void DisconnectGroup_ClearsBothMembers_ToSentinel()
        {
            var q = TwoColumns();
            q.Columns[0].Buses[0].ConnectedId = 3;
            q.Columns[1].Buses[0].ConnectedId = 3;
            BusConnection.DisconnectGroup(null, q, 3);
            Assert.AreEqual(-1, q.Columns[0].Buses[0].ConnectedId);
            Assert.AreEqual(-1, q.Columns[1].Buses[0].ConnectedId);
        }
    }
}
