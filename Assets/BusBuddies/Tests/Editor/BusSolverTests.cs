using NUnit.Framework;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusSolverTests
    {
        [Test]
        public void Solve_SmallBalancedLevel_SolvableAndReplaysToWin()
        {
            // 2x2: y0=[A,B], y1=[B,A]. col0 = A cap2, col1 = B cap2, 2 active slots.
            var grid = new[] { 0, 1, 1, 0 };
            var m = BusLevelModel.FromArrays(grid, 2, 2,
                busColors: new[] { new[] { 0 }, new[] { 1 } },
                busCaps:   new[] { new[] { 2 }, new[] { 2 } },
                activeSlots: 2, numColors: 2, colorNames: new[] { "A", "B" });

            var solver = new BusSolver(maxNodes: 100_000, timeoutMs: 5_000);
            var res = solver.Solve(m);

            Assert.AreEqual(BusSolver.Outcome.Solvable, res.Outcome);
            Assert.IsNotNull(res.WinPath);
            Assert.Greater(res.WinPath.Length, 0);

            // Replay the path through a fresh state -> must win.
            var s = new BusSimState(m);
            foreach (int col in res.WinPath)
            {
                Assert.IsTrue(s.CanPull(col), $"replay col {col} not pullable");
                Assert.GreaterOrEqual(s.FreeSlot(), 0, $"replay col {col} has no free slot");
                s.ApplyMove(col);
            }
            Assert.IsTrue(s.IsWin(), "replaying the win-path must win the level");
        }

        [Test]
        public void Solve_UnbalancedLevel_Unsolvable()
        {
            // 3 A blocks but only 2 capacity of A -> can never clear all -> Unsolvable
            // (the tiny state space is fully explored, not a budget cut).
            var m = BusLevelModel.FromArrays(new[] { 0, 0, 0 }, 3, 1,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 2 } },
                activeSlots: 1, numColors: 1, colorNames: new[] { "A" });

            var solver = new BusSolver(maxNodes: 100_000, timeoutMs: 5_000);
            var res = solver.Solve(m);

            Assert.AreEqual(BusSolver.Outcome.Unsolvable, res.Outcome);
            Assert.IsNull(res.WinPath);
        }

        [Test]
        public void Solve_BudgetExhausted_NeverReturnsUnsolvable()
        {
            // A genuinely solvable level (2x2 same as above) run through a solver
            // with maxNodes=1 — the budget is exhausted before the search completes.
            // The result MUST be BudgetExceeded, NEVER Unsolvable (honesty rule).
            // WinPath must be null on a budget cut.
            var grid = new[] { 0, 1, 1, 0 };
            var m = BusLevelModel.FromArrays(grid, 2, 2,
                busColors: new[] { new[] { 0 }, new[] { 1 } },
                busCaps:   new[] { new[] { 2 }, new[] { 2 } },
                activeSlots: 2, numColors: 2, colorNames: new[] { "A", "B" });

            var solver = new BusSolver(maxNodes: 1, timeoutMs: 5_000);
            var res = solver.Solve(m);

            Assert.AreEqual(BusSolver.Outcome.BudgetExceeded, res.Outcome,
                "a budget cut on a solvable level must return BudgetExceeded, not Unsolvable");
            Assert.IsNull(res.WinPath, "WinPath must be null when budget is exhausted");
        }
    }
}
