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
            var pullsByColor = new Dictionary<int, int>();
            int guard = model.Columns + 2;
            for (int step = 0; step < guard; step++)
            {
                if (s.IsWin()) return order;
                if (s.FreeSlot() < 0) return null;
                int chosen = PickColumn(s, model, mainColors, difficulty, pullsByColor, rng);
                if (chosen < 0) return null;
                int chosenColor = model.BusColor[chosen][0];
                s.ApplyMove(chosen);
                order.Add(chosen);
                pullsByColor.TryGetValue(chosenColor, out var pn);
                pullsByColor[chosenColor] = pn + 1;
            }
            return s.IsWin() ? order : null;
        }

        // Choose the next scratch column to pull, among READY columns (pullable AND the
        // bus color currently has an accessible block, flow > 0). Difficulty scales an
        // interleave "spread" penalty: each prior pull of a color subtracts flow-units, so a
        // color we've been draining loses to a fresh ready color once a comparable color
        // opens -> the peel switches colors -> interleave. At difficulty 1 (f=0) the score is
        // exactly flow (today's flow-greedy behavior; no regression).
        //
        // The penalty scale is the max flow among the OTHER ready colors (>= 1), NOT the
        // global max including the candidate. This is the tuning that keeps the design's own
        // solvable-by-construction guarantee on a dithered picture: a dominant accessible
        // color (e.g. the background body) sees only a tiny competitor flow -> negligible
        // penalty -> it keeps draining instead of switching to a buried scattered-color trap
        // that would clog the Active Row (the shipped clog bug). Two comparable-flow colors
        // (an outline ring and its freshly-opened interior) penalize each other -> interleave.
        // The penalty ranks only among READY colors, so solvability is preserved.
        private static int PickColumn(
            BusSimState s, BusLevelModel model, ISet<string> mainColors, int difficulty,
            Dictionary<int, int> pullsByColor, System.Random rng)
        {
            var accCount = AccessibleCountByColor(s);
            float f = (Math.Clamp(difficulty, 1, 5) - 1) / 4f; // 0..1

            // Distinct ready colors this step and their flow (pullable AND flow > 0).
            var readyFlow = new Dictionary<int, int>();
            for (int col = 0; col < model.Columns; col++)
            {
                if (!s.CanPull(col)) continue;
                int color = model.BusColor[col][0];
                if (accCount.TryGetValue(color, out var flow) && flow > 0)
                    readyFlow[color] = flow; // same color -> same flow, idempotent
            }

            int best = -1; float bestKey = float.NegativeInfinity;
            for (int col = 0; col < model.Columns; col++)
            {
                if (!s.CanPull(col)) continue;
                int color = model.BusColor[col][0];
                if (!accCount.TryGetValue(color, out var flow) || flow <= 0) continue;

                // flowMax = max flow among the OTHER ready colors (>= 1).
                int flowMaxOther = 1;
                foreach (var kv in readyFlow)
                    if (kv.Key != color && kv.Value > flowMaxOther) flowMaxOther = kv.Value;

                pullsByColor.TryGetValue(color, out var pulls);
                // Terms scaled x1000 so flow dominates the rng tie-break exactly as the
                // shipped arranger did (flow*1000 + rng): at f=0 this is bit-identical
                // flow-greedy (no regression); at f=1 each prior pull costs f*flowMaxOther
                // flow-units, so a drained color loses to a fresh comparable ready color
                // -> the peel round-robins -> interleave. rng.Next(0,1000) breaks true ties.
                float score = (flow - f * pulls * flowMaxOther) * 1000f + rng.Next(0, 1000);
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

        // Pure ordering primitive: WITHIN EACH COLOR, round (multiple-of-5) buses first,
        // remainders after, stable within each color's own subgroup. The set of positions
        // each color occupies is preserved exactly — only that color's buses are permuted
        // among their own slots — so the column's inter-color interleave pattern is intact.
        // Reorders references only — never clones buses.
        //   [O20, I5, O25, I3] -> unchanged (each color already round-first in its slots)
        //   [O3, O20]          -> [O20, O3]  (round-first within O)
        public static List<BusEntry> StableRoundFirst(IReadOnlyList<BusEntry> buses)
        {
            var result = new List<BusEntry>();
            if (buses == null) return result;
            result.AddRange(buses);

            // Positions occupied by each color, in column order.
            var positionsByColor = new Dictionary<string, List<int>>();
            for (int i = 0; i < buses.Count; i++)
            {
                string key = buses[i]?.ColorId ?? "\0<null>";
                if (!positionsByColor.TryGetValue(key, out var lst))
                { lst = new List<int>(); positionsByColor[key] = lst; }
                lst.Add(i);
            }

            foreach (var kv in positionsByColor)
            {
                var positions = kv.Value;
                var rounds = new List<BusEntry>();
                var remainder = new List<BusEntry>();
                foreach (var pos in positions)
                {
                    var b = buses[pos];
                    (b != null && b.Capacity % 5 == 0 ? rounds : remainder).Add(b);
                }
                rounds.AddRange(remainder);
                for (int j = 0; j < positions.Count; j++)
                    result[positions[j]] = rounds[j]; // write back into this color's own slots
            }
            return result;
        }

        // Public seam over the exact-replay solvability check (ReplayWins stays private).
        public static bool IsSolvable(
            GridData<ICellData> grid, BusQueueData queue, int columns, int activeSlots)
            => ReplayWins(grid, queue, columns, activeSlots);

        // ── Cap consecutive same-color buses per column (the boss's rule) ──
        // No column may show more than `maxRun` buses of the same color in a row.
        // Each over-long run is fixed MOVE-FIRST (relocate a run bus to a column
        // where it won't form a new over-run) then MERGE-FALLBACK (fuse two adjacent
        // same-color buses into one bigger bus — literally "one of 50 instead of two
        // of 25"). Every candidate edit is kept only if a full solver STILL finds a
        // winning line — via BusSolver, which (unlike the round-robin ReplayWins)
        // handles the uneven columns that moving/merging produce. Per-color pixel
        // totals are preserved (a move keeps the bus, a merge sums it), so export
        // balance holds. Best-effort and deterministic: a run that neither a safe move
        // nor a safe merge can fix is left as-is rather than risk an unsolvable board.
        // Intended for autofiller output (buses have no ConnectedId / Hidden pairing); a
        // merge/move does not carry a reciprocal ConnectedId, so do not run this over a
        // hand-authored queue that contains connected pairs.
        public static BusQueueData CapColorRuns(
            BusQueueData queue, GridData<ICellData> grid, int activeSlots, int maxRun = 3)
        {
            maxRun = Math.Max(1, maxRun);
            var working = CloneQueueData(queue);
            if (working.Columns.Count == 0) return working;

            int slots = Math.Max(1, activeSlots);
            bool Solvable(BusQueueData q)
            {
                var model = BusLevelModel.Build(grid, q, slots);
                return new BusSolver(40_000, 400).Solve(model).Outcome == BusSolver.Outcome.Solvable;
            }

            int totalBuses = 0;
            foreach (var col in working.Columns) totalBuses += col.Buses.Count;

            bool progressed = true;
            int guard = 0, guardMax = totalBuses * 4 + 16;
            while (progressed && guard++ < guardMax)
            {
                progressed = false;
                for (int c = 0; c < working.Columns.Count; c++)
                {
                    if (!FindOverRun(working.Columns[c].Buses, maxRun, out int start, out int len))
                        continue;
                    if (TryMoveRunBus(working, c, start + len - 1, maxRun, Solvable)
                     || TryMergeInRun(working, c, start, len, Solvable))
                        progressed = true;
                    // else: no safe fix for this run — leave it.
                }
            }
            return working;
        }

        // First maximal same-color run longer than maxRun (head->tail). False if none.
        private static bool FindOverRun(List<BusEntry> buses, int maxRun, out int start, out int len)
        {
            start = 0; len = 0;
            int i = 0;
            while (i < buses.Count)
            {
                string color = buses[i]?.ColorId;
                int j = i + 1;
                while (j < buses.Count && string.Equals(buses[j]?.ColorId, color)) j++;
                if (j - i > maxRun) { start = i; len = j - i; return true; }
                i = j;
            }
            return false;
        }

        // Move the bus at `idx` in column c to the TAIL of another column where it
        // won't create a same-color over-run, keeping the whole queue solvable.
        private static bool TryMoveRunBus(
            BusQueueData q, int c, int idx, int maxRun, Func<BusQueueData, bool> solvable)
        {
            var src = q.Columns[c].Buses;
            if (idx < 0 || idx >= src.Count) return false;
            var bus = src[idx];
            string color = bus?.ColorId;

            for (int d = 0; d < q.Columns.Count; d++)
            {
                if (d == c) continue;
                var dst = q.Columns[d].Buses;
                int tailRun = 0;
                for (int k = dst.Count - 1; k >= 0 && string.Equals(dst[k]?.ColorId, color); k--) tailRun++;
                if (tailRun + 1 > maxRun) continue; // would just move the problem

                src.RemoveAt(idx);
                dst.Add(bus);
                if (solvable(q)) return true;
                dst.RemoveAt(dst.Count - 1);        // revert
                src.Insert(idx, bus);
            }
            return false;
        }

        // Merge two adjacent same-color buses within the run [start, start+len) into
        // one bus (capacities summed), keeping the queue solvable. Tail pair first.
        private static bool TryMergeInRun(
            BusQueueData q, int c, int start, int len, Func<BusQueueData, bool> solvable)
        {
            var buses = q.Columns[c].Buses;
            for (int i = start + len - 1; i > start; i--)
            {
                var a = buses[i - 1];
                var b = buses[i];
                if (a == null || b == null) continue;
                var merged = new BusEntry
                {
                    ColorId  = a.ColorId,
                    Capacity = a.Capacity + b.Capacity,
                    Hidden   = a.Hidden && b.Hidden,
                };
                buses[i - 1] = merged;
                buses.RemoveAt(i);
                if (solvable(q)) return true;
                buses[i - 1] = a;                   // revert
                buses.Insert(i, b);
            }
            return false;
        }

        private static BusQueueData CloneQueueData(BusQueueData src)
        {
            var q = new BusQueueData();
            if (src?.Columns != null)
                foreach (var col in src.Columns)
                {
                    var nc = new BusColumn();
                    if (col?.Buses != null) nc.Buses.AddRange(col.Buses);
                    q.Columns.Add(nc);
                }
            return q;
        }

        // Move round buses toward each column's head (remainders to the tail), guarded
        // by re-verification: an accepted per-column sort is kept only if the WHOLE
        // working queue still wins by exact replay; otherwise that column reverts to its
        // original order (other columns' accepted sorts stay). Reorders references only —
        // buses are never cloned or moved between columns. Result is guaranteed solvable
        // whenever the input queue was.
        public static BusQueueData SortRoundToHead(
            BusQueueData queue, GridData<ICellData> grid, int columns, int activeSlots)
        {
            columns = Math.Max(1, columns);
            var working = new BusQueueData();
            if (queue?.Columns != null)
                foreach (var col in queue.Columns)
                {
                    var clone = new BusColumn();
                    if (col?.Buses != null) clone.Buses.AddRange(col.Buses);
                    working.Columns.Add(clone);
                }

            for (int c = 0; c < working.Columns.Count; c++)
            {
                var current = working.Columns[c].Buses;
                var sorted = StableRoundFirst(current);
                if (SameOrder(current, sorted)) continue;

                var original = new List<BusEntry>(current);
                working.Columns[c].Buses = sorted;
                if (!ReplayWins(grid, working, columns, activeSlots))
                    working.Columns[c].Buses = original; // revert this column only
            }
            return working;
        }

        private static bool SameOrder(List<BusEntry> a, List<BusEntry> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
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
