using NUnit.Framework;
using Hoppa.YAK.Sim;

namespace Hoppa.YAK.Tests
{
    public class YakAveragePlayerComplexityTests
    {
        // Trivially-solvable single-color level: two grid columns of color 0, one
        // spool column with one capacity-2 spool. Balanced (2 wool = cap 2).
        private static YakLevelModel TrivialModel()
        {
            var grid = new int[][] { new[] { 0 }, new[] { 0 } }; // 2 columns, 1 tall each
            var spoolColors = new int[][] { new[] { 0 } };
            var spoolCaps   = new int[][] { new[] { 2 } };
            return YakLevelModel.FromArrays(grid, spoolColors, spoolCaps, conveyorSlots: 3, numColors: 1,
                colorNames: new[] { "c0" });
        }

        [Test]
        public void Estimate_WithoutMeasure_LeavesComplexityZero()
        {
            var m = TrivialModel();
            var r = new YakAveragePlayer().Estimate(m,
                new YakAveragePlayer.Config { Runs = 50, Seed = 7, MeasureComplexity = false });
            Assert.AreEqual(0f, r.ComplexityEstimate);
            Assert.AreEqual(0, r.ComplexitySamples);
        }

        [Test]
        public void Estimate_WithMeasure_ReportsInRangeComplexityOnWins()
        {
            var m = TrivialModel();
            var r = new YakAveragePlayer().Estimate(m,
                new YakAveragePlayer.Config { Runs = 50, Seed = 7, MeasureComplexity = true });
            Assert.Greater(r.ComplexitySamples, 0, "trivial level should win some runs");
            Assert.That(r.ComplexityEstimate, Is.InRange(1f, 10f));
        }
    }
}
