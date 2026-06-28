using System.Collections.Generic;
using NUnit.Framework;
using Hoppa.YAK.Sim;

namespace Hoppa.YAK.Tests
{
    public class YakClickPatternTests
    {
        [TestCase(1, 2)] [TestCase(2, 2)]
        [TestCase(3, 3)] [TestCase(4, 3)] [TestCase(5, 3)]
        [TestCase(6, 4)] [TestCase(7, 4)] [TestCase(8, 4)]
        [TestCase(9, 5)] [TestCase(10, 5)]
        public void MaxRepeat_MatchesBossTable(int complexity, int expected)
            => Assert.AreEqual(expected, YakClickPattern.MaxRepeat(complexity));

        [Test]
        public void MaxRepeat_ClampsOutOfRange()
        {
            Assert.AreEqual(2, YakClickPattern.MaxRepeat(0));
            Assert.AreEqual(5, YakClickPattern.MaxRepeat(99));
        }

        [Test]
        public void Score_PureRoundRobin_IsMinimal()
        {
            var taps = new List<int> { 0, 1, 2, 0, 1, 2, 0, 1, 2 };
            float s = YakClickPattern.Score(taps, 3);
            Assert.LessOrEqual(s, 2.0f, "pure round-robin must read as minimal complexity");
        }

        [Test]
        public void Score_LongRunsAndJumps_IsHigh()
        {
            // long same-column runs + non-successor jumps
            var taps = new List<int> { 0, 0, 0, 2, 2, 2, 0, 0, 2, 2 };
            float s = YakClickPattern.Score(taps, 3);
            Assert.GreaterOrEqual(s, 6.0f, "long runs + jumps must read as high complexity");
        }

        [Test]
        public void Score_AlwaysInRange_AndDegenerateInputs()
        {
            Assert.AreEqual(1f, YakClickPattern.Score(new List<int>(), 3));
            Assert.AreEqual(1f, YakClickPattern.Score(new List<int> { 0 }, 3));
            Assert.AreEqual(1f, YakClickPattern.Score(new List<int> { 0, 0, 0 }, 1)); // 1 column → no choice
            float s = YakClickPattern.Score(new List<int> { 0, 2, 1, 2, 0 }, 3);
            Assert.That(s, Is.InRange(1f, 10f));
        }

        [Test]
        public void Score_MonotoneOnConstructedRamp()
        {
            // round-robin < mild-deviation < runs+jumps
            var rr   = new List<int> { 0, 1, 2, 0, 1, 2 };
            var mild = new List<int> { 0, 2, 1, 0, 2, 1 };           // non-successor, runs of 1
            var hard = new List<int> { 0, 0, 2, 2, 1, 1 };           // runs of 2 + jumps
            float a = YakClickPattern.Score(rr, 3);
            float b = YakClickPattern.Score(mild, 3);
            float c = YakClickPattern.Score(hard, 3);
            Assert.Less(a, c, "round-robin must score below runs+jumps");
            Assert.LessOrEqual(a, b, "round-robin must not exceed mild-deviation");
        }
    }
}
