using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesPlateRegionToolTests
    {
        private static LevelEditorSession MakeSession(int w, int h)
        {
            var grid = new GridData<ICellData>(w, h);
            for (int i = 0; i < grid.Cells.Length; i++) grid.Cells[i] = new BBPixelCell { ColorId = "red" };
            var doc = new LevelDocument { Grid = grid, GameData = new JObject() };
            return new LevelEditorSession(ScriptableObject.CreateInstance<GameProfile>(), doc);
        }

        [Test]
        public void OnRegionSelected_AddsPlate_WithDefaultAmount()
        {
            var session = MakeSession(10, 10);
            var tool = ScriptableObject.CreateInstance<BusBuddiesPlateRegionTool>();

            tool.OnRegionSelected(2, 3, 4, 5, session);

            var plates = BusBuddiesPlateConfigs.All(session.Document);
            Assert.AreEqual(1, plates.Count);
            Assert.AreEqual(2, plates[0].X);
            Assert.AreEqual(3, plates[0].Y);
            Assert.AreEqual(4, plates[0].W);
            Assert.AreEqual(5, plates[0].H);
            Assert.AreEqual(BusBuddiesPlateConfigs.DefaultAmount, plates[0].Amount);
            Assert.IsTrue(session.IsDirty);
        }

        [Test]
        public void OnRegionSelected_IgnoresSingleCellClick()
        {
            var session = MakeSession(10, 10);
            var tool = ScriptableObject.CreateInstance<BusBuddiesPlateRegionTool>();

            tool.OnRegionSelected(4, 4, 1, 1, session); // stray click, no drag → 1x1

            Assert.IsEmpty(BusBuddiesPlateConfigs.All(session.Document),
                "a zero-drag single-cell click must not create a plate");
            Assert.IsFalse(session.IsDirty);
        }

        [Test]
        public void OnRegionSelected_RejectsOutOfBounds()
        {
            var session = MakeSession(5, 5);
            var tool = ScriptableObject.CreateInstance<BusBuddiesPlateRegionTool>();

            tool.OnRegionSelected(3, 3, 4, 4, session); // extends past 5x5
            Assert.IsEmpty(BusBuddiesPlateConfigs.All(session.Document));
        }

        [Test]
        public void OnRegionSelected_RejectsOverlap()
        {
            var session = MakeSession(10, 10);
            var tool = ScriptableObject.CreateInstance<BusBuddiesPlateRegionTool>();

            tool.OnRegionSelected(2, 2, 3, 3, session);
            tool.OnRegionSelected(4, 4, 3, 3, session); // overlaps the first

            Assert.AreEqual(1, BusBuddiesPlateConfigs.All(session.Document).Count,
                "an overlapping drag must not add a second plate");
        }
    }
}
