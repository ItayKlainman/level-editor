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
