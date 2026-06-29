using NUnit.Framework;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusAveragePlayerTests
    {
        private static BusLevelModel SolvableTwoByTwo()
        {
            // 2x2 [A,B / B,A]; col0 A cap2, col1 B cap2, 2 slots. Any order wins.
            return BusLevelModel.FromArrays(new[] { 0, 1, 1, 0 }, 2, 2,
                busColors: new[] { new[] { 0 }, new[] { 1 } },
                busCaps:   new[] { new[] { 2 }, new[] { 2 } },
                activeSlots: 2, numColors: 2, colorNames: new[] { "A", "B" });
        }

        [Test]
        public void Estimate_SolvableLevel_HasPositiveWinRate()
        {
            var player = new BusAveragePlayer();
            var res = player.Estimate(SolvableTwoByTwo(),
                new BusAveragePlayer.Config { Epsilon = 0.1f, Lookahead = 1, Runs = 100, Seed = 12345 });

            Assert.Greater(res.WinRate, 0f);
            Assert.AreEqual(100, res.Runs);
            Assert.Less(res.Aps, BusAveragePlayer.ApsCap + 0.001f);
        }

        [Test]
        public void Estimate_IsDeterministicForFixedSeed()
        {
            var player = new BusAveragePlayer();
            var cfg = new BusAveragePlayer.Config { Epsilon = 0.2f, Lookahead = 1, Runs = 200, Seed = 777 };

            var a = player.Estimate(SolvableTwoByTwo(), cfg);
            var b = player.Estimate(SolvableTwoByTwo(), cfg);

            Assert.AreEqual(a.WinRate, b.WinRate);
            Assert.AreEqual(a.Aps, b.Aps);
        }
    }
}
