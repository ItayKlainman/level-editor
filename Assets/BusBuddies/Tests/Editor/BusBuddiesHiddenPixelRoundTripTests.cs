using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesHiddenPixelRoundTripTests
    {
        [Test]
        public void HiddenPixels_SurviveImport_AtSameCells()
        {
            // Build a 4x4 game-schema JObject with two hidden cells, import it, assert Hidden.
            int w = 4, h = 4;
            var px = new JArray();
            for (int i = 0; i < w * h; i++) px.Add(9); // all Red
            // Hidden at (x=3,y=2) -> 3*4+2=14 ; (x=1,y=0) -> 4
            var hidden = new JArray { 14, 4 };

            var root = new JObject
            {
                ["SlotsAmount"] = 5, ["Width"] = w, ["Height"] = h,
                ["BusColumnConfigs"] = new JArray(),
                ["PixelColors"] = px,
                ["HiddenPixels"] = hidden,
            };

            var imported = BusBuddiesGameLevelImporter.Import(root.ToString(), "level_1");
            var grid = imported.Document.Grid;

            Assert.IsTrue(((BBPixelCell)grid.Get(3, 2)).Hidden);
            Assert.IsTrue(((BBPixelCell)grid.Get(1, 0)).Hidden);
            Assert.IsFalse(((BBPixelCell)grid.Get(0, 0)).Hidden);
        }
    }
}
