using NUnit.Framework;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusDynamicsTests
    {
        [Test]
        public void Removal_HasNoGravity_OthersStayInPlace()
        {
            // 1x3 vertical strip: y0=A, y1=B, y2=A. One bus A cap1, 1 active slot.
            // Pulling A removes the first accessible A (i0); the top A must STAY at
            // index 2 (no gravity) and B at index 1 is untouched.
            var m = BusLevelModel.FromArrays(new[] { 0, 1, 0 }, 1, 3,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 1 } },
                activeSlots: 1, numColors: 2, colorNames: new[] { "A", "B" });
            var s = new BusSimState(m);

            int removed = s.ApplyMove(0);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(-1, s.Cell[0], "bottom A removed");
            Assert.AreEqual(1, s.Cell[1], "B unchanged");
            Assert.AreEqual(0, s.Cell[2], "top A did NOT fall (no gravity)");
            Assert.AreEqual(2, s.BlocksLeft);
        }

        [Test]
        public void ResolveReleases_PartialBus_LeftInRow()
        {
            // 1x2 strip [A,A]; one bus A cap5. Removes both A, then 3 passengers
            // remain so the bus stays in the Active Row. Not a win.
            var m = BusLevelModel.FromArrays(new[] { 0, 0 }, 1, 2,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 5 } },
                activeSlots: 5, numColors: 1, colorNames: new[] { "A" });
            var s = new BusSimState(m);

            s.ApplyMove(0);

            Assert.AreEqual(0, s.BlocksLeft);
            Assert.AreEqual(0, s.ActiveColor[0], "partial bus still occupies slot 0");
            Assert.AreEqual(3, s.ActiveRem[0]);
            Assert.IsFalse(s.IsWin(), "active row not empty -> not a win");
        }

        [Test]
        public void ResolveReleases_Coexistence_UnlocksAndWins()
        {
            // 3x3: centre (1,1) = A locked behind 4 B arms; corners empty.
            //   y2: [-1, B, -1]
            //   y1: [ B, A,  B]
            //   y0: [-1, B, -1]
            // col0 = bus A cap1, col1 = bus B cap4, 5 active slots.
            // Pull A first (stuck: A locked). Pull B: B clears 4 arms, which unlocks
            // the centre A, which the still-resident A bus then collects -> WIN.
            var grid = new[] { -1, 1, -1,  1, 0, 1,  -1, 1, -1 };
            var m = BusLevelModel.FromArrays(grid, 3, 3,
                busColors: new[] { new[] { 0 }, new[] { 1 } },
                busCaps:   new[] { new[] { 1 }, new[] { 4 } },
                activeSlots: 5, numColors: 2, colorNames: new[] { "A", "B" });
            var s = new BusSimState(m);

            s.ApplyMove(0); // pull A — locked, stays
            Assert.AreEqual(0, s.ActiveColor[0]);
            Assert.AreEqual(1, s.ActiveRem[0]);
            Assert.AreEqual(5, s.BlocksLeft);

            s.ApplyMove(1); // pull B — clears arms, unlocks centre, A collects it

            Assert.AreEqual(0, s.BlocksLeft);
            Assert.IsTrue(s.IsWin(), "all blocks cleared, both buses emptied, queues exhausted");
        }

        [Test]
        public void IsDeadlock_RowFull_NoActiveColorCanRelease()
        {
            // 2x1 grid [A,B]; two columns each a single bus of colour C (index 2,
            // not present on the grid). 2 active slots. Pull both -> row full, no C
            // block exists -> deadlock.
            var m = BusLevelModel.FromArrays(new[] { 0, 1 }, 2, 1,
                busColors: new[] { new[] { 2 }, new[] { 2 } },
                busCaps:   new[] { new[] { 1 }, new[] { 1 } },
                activeSlots: 2, numColors: 3, colorNames: new[] { "A", "B", "C" });
            var s = new BusSimState(m);

            s.ApplyMove(0);
            s.ApplyMove(1);

            Assert.IsTrue(s.IsDeadlock());
            Assert.IsFalse(s.IsWin());
        }

        [Test]
        public void Clone_IsIndependent()
        {
            var m = BusLevelModel.FromArrays(new[] { 0, 0 }, 1, 2,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 2 } },
                activeSlots: 1, numColors: 1, colorNames: new[] { "A" });
            var s = new BusSimState(m);
            var clone = s.Clone();

            clone.ApplyMove(0);

            Assert.AreEqual(2, s.BlocksLeft, "original untouched by clone's move");
            Assert.AreEqual(0, clone.BlocksLeft);
        }

        [Test]
        public void TargetChoice_NearestToHole_NotLowestIndex_Regression()
        {
            // Grid W=5, H=4, hx=2.0, hy=-1.0.
            //
            // y3: [ -,  -,  -,  -,  - ]  (top, all empty)
            // y2: [ -,  -,  A,  -,  - ]  A at (2,2)=flat12
            // y1: [ -,  A,  B,  A,  - ]  A at (1,1)=flat6, B at (2,1)=flat7, A at (3,1)=flat8
            // y0: [ A,  -,  A,  -,  - ]  A at (0,0)=flat0, A at (2,0)=flat2
            //
            // B at (2,1) is surrounded on all 4 sides by A blocks → LOCKED initially.
            // It unlocks ONLY when (2,0) is removed (its bottom neighbour).
            //
            // Two accessible A blocks:
            //   (0,0) flat=0  dist²=(0-2)²+(0+1)²=4+1=5  <- lowest flat index
            //   (2,0) flat=2  dist²=(2-2)²+(0+1)²=0+1=1  <- hole-NEAREST
            //
            // REGRESSION: under the old lowest-flat-index rule the first removal would
            // be (0,0), B stays locked; the test asserts (2,0) was removed first, which
            // FAILS under the old rule, proving this is a genuine regression guard.

            int[] grid = new int[20];
            for (int i = 0; i < 20; i++) grid[i] = -1;
            grid[0]  = 0; // (0,0)=A  flat0
            grid[2]  = 0; // (2,0)=A  flat2  <- hole-nearest
            grid[6]  = 0; // (1,1)=A  flat6
            grid[7]  = 1; // (2,1)=B  flat7  <- locked by 4 A neighbours
            grid[8]  = 0; // (3,1)=A  flat8
            grid[12] = 0; // (2,2)=A  flat12

            // Part (a): bus A cap=1 → removes exactly 1 block → must pick hole-nearest.
            var mTarget = BusLevelModel.FromArrays(
                grid: grid, w: 5, h: 4,
                busColors: new[] { new[] { 0 } },
                busCaps:   new[] { new[] { 1 } },
                activeSlots: 1, numColors: 2, colorNames: new[] { "A", "B" });
            var sTarget = new BusSimState(mTarget);
            sTarget.ApplyMove(0);

            Assert.AreEqual(-1, sTarget.Cell[2],
                "hole-nearest A at (2,0)=flat2 must be the first block removed");
            Assert.AreEqual(0, sTarget.Cell[0],
                "far A at (0,0)=flat0 must be untouched (old lowest-index rule would remove this)");
            Assert.IsTrue(sTarget.IsAccessible(2, 1),
                "removing A_near at (2,0) must unlock B at (2,1)");

            // Part (b): balanced level (bus A cap=5, bus B cap=1) → solver must win.
            var mSolver = BusLevelModel.FromArrays(
                grid: grid, w: 5, h: 4,
                busColors: new[] { new[] { 0 }, new[] { 1 } },
                busCaps:   new[] { new[] { 5 }, new[] { 1 } },
                activeSlots: 2, numColors: 2, colorNames: new[] { "A", "B" });
            var solver = new BusSolver(maxNodes: 500_000, timeoutMs: 10_000);
            var res = solver.Solve(mSolver);

            Assert.AreEqual(BusSolver.Outcome.Solvable, res.Outcome);
            Assert.IsNotNull(res.WinPath);

            var sReplay = new BusSimState(mSolver);
            foreach (int col in res.WinPath)
            {
                Assert.IsTrue(sReplay.CanPull(col), $"replay col {col} not pullable");
                Assert.GreaterOrEqual(sReplay.FreeSlot(), 0, $"replay col {col} no free slot");
                sReplay.ApplyMove(col);
            }
            Assert.IsTrue(sReplay.IsWin(), "replaying the win-path on the nearest-to-hole level must win");
        }

        [Test]
        public void Plate_CoveredBlock_ExcludedEarly_EvenWhenNearestToHole()
        {
            // 1x3 strip [A,A,A]. A plate covers the CENTRE cell (index 1) — which is the
            // hole-nearest block — requiring 1 pick before it opens. One bus A cap1.
            // The first pull must NOT take the covered centre (it's excluded until the
            // plate opens); it takes an uncovered edge A instead.
            var m = BusLevelModel.FromArrays(new[] { 0, 0, 0 }, 3, 1,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 1 } },
                activeSlots: 1, numColors: 1, colorNames: new[] { "A" },
                plateReq: new[] { 0, 1, 0 });
            var s = new BusSimState(m);

            s.ApplyMove(0);

            Assert.AreEqual(0, s.Cell[1], "covered centre A must NOT be picked first (plate not yet open)");
            Assert.AreEqual(1, s.Picked, "exactly one (uncovered) block picked");
            Assert.AreEqual(2, s.BlocksLeft);
        }

        [Test]
        public void Plate_Solvable_OnlyWhenThresholdReachable()
        {
            // 1x2 strip [A,A]; a plate covers the TOP cell (index 1). One bus A cap2.
            // Reachable threshold (1): pick the bottom A (Picked→1), plate opens, pick
            // the top A → win. Unreachable threshold (5): only 2 blocks exist so Picked
            // maxes at 1 < 5, the covered A never opens → provably unsolvable.
            int[] grid = { 0, 0 };

            var solvable = BusLevelModel.FromArrays(grid, 1, 2,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 2 } },
                activeSlots: 1, numColors: 1, colorNames: new[] { "A" },
                plateReq: new[] { 0, 1 });
            var rOk = new BusSolver(500_000, 10_000).Solve(solvable);
            Assert.AreEqual(BusSolver.Outcome.Solvable, rOk.Outcome,
                "plate opens after 1 pick → solvable");

            var stuck = BusLevelModel.FromArrays(grid, 1, 2,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 2 } },
                activeSlots: 1, numColors: 1, colorNames: new[] { "A" },
                plateReq: new[] { 0, 5 });
            var rBad = new BusSolver(500_000, 10_000).Solve(stuck);
            Assert.AreEqual(BusSolver.Outcome.Unsolvable, rBad.Outcome,
                "plate threshold (5) exceeds total pickable pixels → the covered A never opens");
        }

        [Test]
        public void Key_DeterministicForEqualStates()
        {
            var m = BusLevelModel.FromArrays(new[] { 0, 1 }, 2, 1,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 1 } },
                activeSlots: 2, numColors: 2, colorNames: new[] { "A", "B" });
            var a = new BusSimState(m);
            var b = new BusSimState(m);
            Assert.AreEqual(a.Key(), b.Key());

            a.ApplyMove(0);
            b.ApplyMove(0);
            Assert.AreEqual(a.Key(), b.Key());
        }
    }
}
