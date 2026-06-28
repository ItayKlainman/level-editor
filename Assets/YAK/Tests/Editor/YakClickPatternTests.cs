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

        private static int[] ColumnCounts(int[] pattern, int k)
        {
            var counts = new int[k];
            foreach (int c in pattern) counts[c]++;
            return counts;
        }

        [Test]
        public void Build_HasExactLength_AndBalancedQuota()
        {
            var rng = new System.Random(123);
            int[] p = YakClickPattern.Build(numColumns: 3, numSpools: 10, complexity: 5, rng);
            Assert.AreEqual(10, p.Length);
            var counts = ColumnCounts(p, 3);
            foreach (int c in counts) Assert.That(c, Is.InRange(2, 4)); // 10/3 ≈ 3 ± 1
            Assert.AreEqual(10, counts[0] + counts[1] + counts[2]);
        }

        [Test]
        public void Build_NeverPureRoundRobin_EvenAtComplexity1()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var rng = new System.Random(seed);
                int[] p = YakClickPattern.Build(3, 9, 1, rng);
                bool isRr = true;
                for (int i = 1; i < p.Length; i++)
                    if (p[i] != (p[i - 1] + 1) % 3) { isRr = false; break; }
                Assert.IsFalse(isRr, $"seed {seed}: pattern must never be pure round-robin (R25)");
            }
        }

        private static bool IsPureSuccessorCycle(int[] p, int k)
        {
            for (int i = 1; i < p.Length; i++)
                if (p[i] != (p[i - 1] + 1) % k) return false;
            return p.Length >= 2;
        }

        [Test]
        public void Build_NeverPureRoundRobin_TinyPatterns()
        {
            // R25 must hold even for tiny patterns. The PerturbSwap fallback has to
            // break pure k=2 round-robin where every 2-apart pair is equal.
            // n == 2 over k == 2 is the one degenerate length that CANNOT be broken:
            // quota [1,1] forces [a,b], whose only swap [b,a] is also a successor
            // cycle — so it is intentionally excluded here.
            for (int seed = 1; seed <= 20; seed++)
            {
                foreach (int n in new[] { 4, 3 })
                {
                    int[] p = YakClickPattern.Build(2, n, 1, new System.Random(seed));
                    Assert.IsFalse(IsPureSuccessorCycle(p, 2),
                        $"seed {seed}, k=2 n={n}: pattern must never be pure round-robin (R25)");
                }
            }
        }

        [Test]
        public void Build_NullRng_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => YakClickPattern.Build(3, 9, 1, null));
        }

        [Test]
        public void Build_ComplexityRaisesMeasuredScore()
        {
            // Averaged over seeds, higher complexity → higher measured Score.
            float lowSum = 0f, highSum = 0f; int trials = 25;
            for (int seed = 1; seed <= trials; seed++)
            {
                lowSum  += YakClickPattern.Score(YakClickPattern.Build(4, 20, 1,  new System.Random(seed)), 4);
                highSum += YakClickPattern.Score(YakClickPattern.Build(4, 20, 10, new System.Random(seed)), 4);
            }
            Assert.Less(lowSum / trials, highSum / trials,
                "mean measured complexity at C=10 must exceed C=1");
        }

        [Test]
        public void Build_DegenerateInputs()
        {
            Assert.AreEqual(0, YakClickPattern.Build(3, 0, 5, new System.Random(1)).Length);
            int[] oneCol = YakClickPattern.Build(1, 5, 9, new System.Random(1));
            Assert.AreEqual(5, oneCol.Length);
            foreach (int c in oneCol) Assert.AreEqual(0, c); // only column available
        }
    }
}
