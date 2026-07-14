using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor
{
    // Builds a solvable-by-construction bus queue. Simulates a border-inward peel
    // against the real N-slot sim using a one-bus-per-column scratch model, then
    // distributes that winning order round-robin across the requested columns
    // (within-column order preserved, so the next needed bus is always at some
    // column's head). Verified by exact replay. Deterministic given `rng`.
    public static class BusBuddiesConstructiveArranger
    {
        public struct Result
        {
            public BusQueueData Queue;
            public bool Solvable;
        }

        public static Result Arrange(
            GridData<ICellData> grid,
            IReadOnlyList<BusEntry> buses,
            int columns, int activeSlots, int difficulty,
            ISet<string> mainColors, System.Random rng)
        {
            columns = Math.Max(1, columns);
            rng ??= new System.Random(0);
            var queue = new BusQueueData();
            for (int i = 0; i < columns; i++) queue.Columns.Add(new BusColumn());
            if (buses == null || buses.Count == 0)
                return new Result { Queue = queue, Solvable = true };

            // Scratch model: one bus per column -> the scheduler may pick ANY bus next;
            // the Active-Row slot count is still enforced.
            var scratchQueue = new BusQueueData();
            foreach (var b in buses)
            {
                var col = new BusColumn();
                col.Buses.Add(b);
                scratchQueue.Columns.Add(col);
            }
            var model = BusLevelModel.Build(grid, scratchQueue, Math.Max(1, activeSlots));

            var order = Schedule(model, mainColors, difficulty, rng);
            if (order == null)
                return new Result { Queue = queue, Solvable = false };

            for (int i = 0; i < order.Count; i++)
                queue.Columns[i % columns].Buses.Add(buses[order[i]]);

            bool solvable = ReplayWins(grid, queue, columns, activeSlots);
            return new Result { Queue = queue, Solvable = solvable };
        }

        // Greedy peel. Returns scratch-column indices (== bus identities) in head->tail
        // order, or null if it stalls before the board is cleared.
        private static List<int> Schedule(
            BusLevelModel model, ISet<string> mainColors, int difficulty, System.Random rng)
        {
            var s = new BusSimState(model);
            var order = new List<int>(model.Columns);
            int guard = model.Columns + 2;
            for (int step = 0; step < guard; step++)
            {
                if (s.IsWin()) return order;
                if (s.FreeSlot() < 0) return null;
                int chosen = PickColumn(s, model, mainColors, difficulty, rng);
                if (chosen < 0) return null;
                s.ApplyMove(chosen);
                order.Add(chosen);
            }
            return s.IsWin() ? order : null;
        }

        // Choose the next scratch column to pull, among pullable columns whose bus
        // color currently has an accessible block. Difficulty scales how strongly MAIN
        // colors are deferred (1 = neutral flow-preserving; 5 = defer main as late as
        // accessibility allows). Flow (accessible-block count) breaks ties so slots keep
        // emptying. Deferral only ranks among READY colors -> solvability is preserved.
        private static int PickColumn(
            BusSimState s, BusLevelModel model, ISet<string> mainColors, int difficulty, System.Random rng)
        {
            var accCount = AccessibleCountByColor(s);
            float f = (Math.Clamp(difficulty, 1, 5) - 1) / 4f; // 0..1
            int best = -1; long bestKey = long.MinValue;
            for (int col = 0; col < model.Columns; col++)
            {
                if (!s.CanPull(col)) continue;
                int color = model.BusColor[col][0];
                if (!accCount.TryGetValue(color, out var flow) || flow <= 0) continue;

                bool isMain = mainColors != null && color >= 0 && model.ColorNames != null
                              && color < model.ColorNames.Length && mainColors.Contains(model.ColorNames[color]);
                // Rank: background above main (weighted by difficulty), then flow, then rng.
                long rank = isMain ? -(long)(f * 1_000_000f) : 0L;
                long score = rank + flow * 1000 + rng.Next(0, 1000);
                if (score > bestKey) { bestKey = score; best = col; }
            }
            return best;
        }

        // color index -> number of currently-accessible remaining blocks of that color.
        private static Dictionary<int, int> AccessibleCountByColor(BusSimState s)
        {
            var map = new Dictionary<int, int>();
            int w = s.M.W;
            for (int i = 0; i < s.Cell.Length; i++)
            {
                int c = s.Cell[i];
                if (c < 0) continue;
                if (s.IsAccessible(i % w, i / w))
                { map.TryGetValue(c, out var n); map[c] = n + 1; }
            }
            return map;
        }

        // Exact replay of the round-robin pull sequence through the real arrangement.
        // Reproduces the derived order (real step i pulls column i%columns), so it wins
        // iff the scratch schedule won; also guards the distribution logic.
        private static bool ReplayWins(
            GridData<ICellData> grid, BusQueueData queue, int columns, int activeSlots)
        {
            var model = BusLevelModel.Build(grid, queue, Math.Max(1, activeSlots));
            var s = new BusSimState(model);
            int total = 0;
            foreach (var col in queue.Columns) total += col.Buses.Count;
            for (int i = 0; i < total; i++)
            {
                int col = i % columns;
                if (!s.CanPull(col) || s.FreeSlot() < 0) return false;
                s.ApplyMove(col);
            }
            return s.IsWin();
        }
    }
}
