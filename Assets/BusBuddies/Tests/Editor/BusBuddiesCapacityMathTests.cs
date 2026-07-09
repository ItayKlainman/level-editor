using System.Collections.Generic;
using NUnit.Framework;
using Hoppa.BusBuddies.Editor;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Task 3 (capacity math) + Task 4 (rule-aware partition). The Excel mapping,
    // the deviation window, bus-count estimates, exact-sum invariant, and the
    // No-1-bus / Round-to-5 rules.
    public sealed class BusBuddiesCapacityMathTests
    {
        private static int Sum(IEnumerable<int> caps)
        {
            int s = 0; foreach (var c in caps) s += c; return s;
        }
        private static int NonMultiplesOf5(IEnumerable<int> caps)
        {
            int n = 0; foreach (var c in caps) if (c % 5 != 0) n++; return n;
        }

        // ── Chunks → avg (Excel row: 1..5 → 10,15,20,25,30) ──
        [Test]
        public void Avg_MatchesExcelMapping()
        {
            Assert.AreEqual(10, BusBuddiesCapacityMath.Avg(1, 10, 5));
            Assert.AreEqual(15, BusBuddiesCapacityMath.Avg(2, 10, 5));
            Assert.AreEqual(20, BusBuddiesCapacityMath.Avg(3, 10, 5));
            Assert.AreEqual(25, BusBuddiesCapacityMath.Avg(4, 10, 5));
            Assert.AreEqual(30, BusBuddiesCapacityMath.Avg(5, 10, 5));
        }

        // ── Deviation → [min,max] window (Excel: avg 30 @ 50% → 15/45) ──
        [Test]
        public void Window_FromDeviation()
        {
            BusBuddiesCapacityMath.Window(30, 0.5f, out int min, out int max);
            Assert.AreEqual(15, min);
            Assert.AreEqual(45, max);

            BusBuddiesCapacityMath.Window(20, 0f, out int min0, out int max0);
            Assert.AreEqual(20, min0);
            Assert.AreEqual(20, max0);
        }

        [Test]
        public void EstimateBusCount_MatchesExcel()
        {
            Assert.AreEqual(17, BusBuddiesCapacityMath.EstimateBusCount(500, 30)); // Purple 500 @ chunks5
            Assert.AreEqual(28, BusBuddiesCapacityMath.EstimateBusCount(850, 30)); // total ≈ 28 buses
            Assert.AreEqual(0,  BusBuddiesCapacityMath.EstimateBusCount(0, 30));
            Assert.AreEqual(1,  BusBuddiesCapacityMath.EstimateBusCount(3, 30));   // >=1 when any pixels
        }

        // ── Exact-sum invariant across all rule combinations ──
        [Test]
        public void PartitionColor_AlwaysSumsExactly()
        {
            var rng = new System.Random(5);
            int[] totals = { 1, 2, 3, 5, 7, 11, 13, 20, 23, 30, 100, 500, 850 };
            foreach (var total in totals)
                foreach (var noSingle in new[] { false, true })
                    foreach (var round5 in new[] { false, true })
                    {
                        var caps = BusBuddiesCapacityMath.PartitionColor(total, 15, 45, 30, noSingle, round5, rng);
                        Assert.AreEqual(total, Sum(caps),
                            $"total {total} noSingle {noSingle} round5 {round5} must sum exactly");
                        foreach (var c in caps) Assert.Greater(c, 0, "every bus positive");
                    }
        }

        // ── No 1-bus color: a single-bus color splits into >= 2 ──
        [Test]
        public void NoSingleBus_SplitsWhite20IntoTwo()
        {
            // avg 30 window → 20 would be a single bus; the rule forces >= 2.
            var caps = BusBuddiesCapacityMath.PartitionColor(20, 15, 45, 30, true, false, new System.Random(1));
            Assert.GreaterOrEqual(caps.Count, 2);
            Assert.AreEqual(20, Sum(caps));
        }

        [Test]
        public void NoSingleBus_SplitsSmallPrimeIntoTwo()
        {
            var caps = BusBuddiesCapacityMath.PartitionColor(7, 3, 12, 6, true, false, new System.Random(1));
            Assert.GreaterOrEqual(caps.Count, 2);
            Assert.AreEqual(7, Sum(caps));
        }

        // ── Round to 5: maximizes multiples while summing exactly ──
        [Test]
        public void RoundToFive_AllMultiplesWhenDivisible()
        {
            var caps = BusBuddiesCapacityMath.PartitionColor(40, 10, 30, 20, false, true, new System.Random(1));
            Assert.AreEqual(40, Sum(caps));
            Assert.AreEqual(0, NonMultiplesOf5(caps), "40 splits into pure multiples of 5");
        }

        [Test]
        public void RoundToFive_AtMostOneNonMultiple()
        {
            var caps = BusBuddiesCapacityMath.PartitionColor(23, 5, 15, 10, false, true, new System.Random(1));
            Assert.AreEqual(23, Sum(caps));
            Assert.AreEqual(1, NonMultiplesOf5(caps), "23 leaves exactly one non-multiple");
            Assert.GreaterOrEqual(caps.Count, 2, "avg 10 → two buses so a multiple-of-5 bus survives");
        }

        // ── Rules compose (No-1-bus wins, Round-to-5 preserved best-effort) ──
        [Test]
        public void BothRules_Compose_White20()
        {
            var caps = BusBuddiesCapacityMath.PartitionColor(20, 15, 45, 30, true, true, new System.Random(1));
            Assert.AreEqual(20, Sum(caps));
            Assert.GreaterOrEqual(caps.Count, 2, "No-1-bus forces a split");
            Assert.AreEqual(0, NonMultiplesOf5(caps), "the split stays on multiples of 5");
        }

        // ── MED-1: Deviation and Round-to-5 are INDEPENDENT knobs. Round-to-5
        // must not collapse the [min,max] window down to a fixed avg-derived bus
        // count — a wide deviation window must still produce a spread of bus
        // capacities, not a uniform ~avg size, while staying multiples of 5. ──
        [Test]
        public void RoundToFive_HighDeviation_ProducesSpreadOfCapacities_WithinWindow()
        {
            // Chunks 3 -> avg 20; Deviation 80% -> half=round(20*0.8)=16 -> [4,36].
            int avg = BusBuddiesCapacityMath.Avg(3, 10, 5);
            BusBuddiesCapacityMath.Window(avg, 0.8f, out int min, out int max);
            Assert.AreEqual(4, min);
            Assert.AreEqual(36, max);

            var caps = BusBuddiesCapacityMath.PartitionColor(200, min, max, avg, false, true, new System.Random(3));

            Assert.AreEqual(200, Sum(caps), "must still sum exactly");
            Assert.GreaterOrEqual(caps.Count, 2);

            int belowMin = 0;
            foreach (var c in caps)
            {
                Assert.LessOrEqual(c, max, "no bus exceeds the deviation window's max");
                if (c < min) belowMin++;
            }
            Assert.LessOrEqual(belowMin, 1, "at most the trailing remainder may fall below min (mirrors Partition)");

            Assert.GreaterOrEqual(new HashSet<int>(caps).Count, 2,
                "a wide deviation window must still produce a SPREAD of capacities, not a uniform avg-derived size");
            Assert.LessOrEqual(NonMultiplesOf5(caps), 1, "round-to-5 stays best-effort multiples of 5");
        }

        [Test]
        public void RoundToFive_ZeroDeviation_StillWorks_TightWindow()
        {
            // dev=0 -> min=max=avg (20). Round-to-5 has no multiple-of-5 room inside
            // a single-point window, so it degrades to the accepted non-multiple
            // exception while still summing exactly.
            var caps = BusBuddiesCapacityMath.PartitionColor(47, 20, 20, 20, false, true, new System.Random(1));
            Assert.AreEqual(47, Sum(caps));
            foreach (var c in caps) Assert.Greater(c, 0);
        }
    }
}
