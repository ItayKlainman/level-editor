using System.Linq;
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BBConnectedBusRuleTests
    {
        private static ValidationContext Ctx(BusQueueData queue)
        {
            var doc = new LevelDocument { TopSection = JObject.FromObject(queue) };
            // Real ValidationContext ctor is (LevelDocument, IColorPalette); Grid is read-only.
            return new ValidationContext(doc, null);
        }

        private static BBConnectedBusRule Rule()
        {
            var r = ScriptableObject.CreateInstance<BBConnectedBusRule>();
            r.Configure("bb.connected");
            return r;
        }

        private static BusColumn Col(int n)
        {
            var c = new BusColumn();
            for (int i = 0; i < n; i++) c.Buses.Add(new BusEntry());
            return c;
        }

        [Test]
        public void ValidPair_NoErrors()
        {
            var q = new BusQueueData(); q.Columns.Add(Col(1)); q.Columns.Add(Col(1));
            q.Columns[0].Buses[0].ConnectedId = 0;
            q.Columns[1].Buses[0].ConnectedId = 0;
            var errs = Rule().Evaluate(Ctx(q)).Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsEmpty(errs);
        }

        [Test]
        public void IncompletePair_Errors()
        {
            var q = new BusQueueData(); q.Columns.Add(Col(1));
            q.Columns[0].Buses[0].ConnectedId = 0;
            var errs = Rule().Evaluate(Ctx(q)).Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsNotEmpty(errs);
        }

        [Test]
        public void CrossingPairs_ErrorsWithSoftLock()
        {
            var q = new BusQueueData(); q.Columns.Add(Col(2)); q.Columns.Add(Col(2));
            q.Columns[0].Buses[0].ConnectedId = 0; q.Columns[0].Buses[1].ConnectedId = 1;
            q.Columns[1].Buses[0].ConnectedId = 1; q.Columns[1].Buses[1].ConnectedId = 0;
            var errs = Rule().Evaluate(Ctx(q)).Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsNotEmpty(errs);
        }
    }
}
