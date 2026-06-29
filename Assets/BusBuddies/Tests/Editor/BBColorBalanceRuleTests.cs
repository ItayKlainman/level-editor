using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BBColorBalanceRuleTests
    {
        private static LevelDocument MakeDoc(GridData<ICellData> grid, BusQueueData queue) => new LevelDocument
        {
            SchemaVersion = "busbuddies",
            Grid = grid,
            TopSection = JObject.FromObject(queue),
        };

        private static List<ValidationEntry> Evaluate(LevelDocument doc)
        {
            var rule = ScriptableObject.CreateInstance<BBColorBalanceRule>();
            rule.Configure("busbuddies.color_balance");
            return rule.Evaluate(new ValidationContext(doc, null)).ToList();
        }

        [Test]
        public void BalancedColor_IsInfo()
        {
            // 2 A blocks, bus A capacity 2 -> balanced.
            var grid = new GridData<ICellData>(2, 1);
            grid.Set(0, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(1, 0, new BBPixelCell { ColorId = "A" });
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ColorId = "A", Capacity = 2 });
            q.Columns.Add(c0);

            var entries = Evaluate(MakeDoc(grid, q));
            var a = entries.Single(e => e.Message.StartsWith("A:"));
            Assert.AreEqual(ValidationSeverity.Info, a.Severity);
        }

        [Test]
        public void UnbalancedColor_IsError()
        {
            // 3 A blocks, bus A capacity 2 -> error.
            var grid = new GridData<ICellData>(3, 1);
            grid.Set(0, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(1, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(2, 0, new BBPixelCell { ColorId = "A" });
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ColorId = "A", Capacity = 2 });
            q.Columns.Add(c0);

            var entries = Evaluate(MakeDoc(grid, q));
            var a = entries.Single(e => e.Message.StartsWith("A:"));
            Assert.AreEqual(ValidationSeverity.Error, a.Severity);
        }

        [Test]
        public void Scope_IsColor()
        {
            var rule = ScriptableObject.CreateInstance<BBColorBalanceRule>();
            Assert.AreEqual(ValidationScope.Color, rule.Scope);
        }
    }
}
