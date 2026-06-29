using System;

namespace Hoppa.BusBuddies.Sim
{
    // Monte-Carlo "average player" difficulty estimator. Engine-agnostic plain C#.
    //
    // Difficulty is MEASURED, not labelled: run N seeded playouts of a myopic,
    // occasionally-careless bot and report the win-rate. APS (Attempts Per Solve)
    // ~= 1 / win-rate. Deadlock-proneness lowers win-rate naturally, so it folds
    // into APS.
    //
    // Policy (lookahead L, carelessness e): with probability e pull a random legal
    // column; otherwise pull the column that greedily removes the most blocks over
    // the next L plies, avoiding a pull that immediately self-deadlocks. Hidden /
    // connected buses are treated as known / independent in 1a.
    public sealed class BusAveragePlayer
    {
        public struct Config
        {
            public float Epsilon;   // 0..1 carelessness
            public int   Lookahead; // >=1 plies of greedy planning
            public int   Runs;      // playout count
            public int   Seed;      // base RNG seed (0 = time-based)

            public static Config Default =>
                new Config { Epsilon = 0.1f, Lookahead = 1, Runs = 400, Seed = 12345 };
        }

        public struct Result
        {
            public float WinRate; // wins / runs
            public int   Runs;
            public float Aps;     // 1/WinRate, capped at ApsCap (ApsCap when no win)
        }

        // Hard cap converting a zero win-rate to a finite APS for reporting.
        public const float ApsCap = 99f;

        public Result Estimate(BusLevelModel model, Config cfg)
        {
            int runs = cfg.Runs > 0 ? cfg.Runs : 400;
            int seed = cfg.Seed != 0 ? cfg.Seed : Environment.TickCount;
            int lookahead = cfg.Lookahead > 0 ? cfg.Lookahead : 1;
            float eps = cfg.Epsilon < 0f ? 0f : (cfg.Epsilon > 1f ? 1f : cfg.Epsilon);

            int wins = 0;
            for (int r = 0; r < runs; r++)
            {
                var rng = new Random(unchecked(seed * 1000003 + r));
                if (Playout(model, eps, lookahead, rng)) wins++;
            }

            float winRate = (float)wins / runs;
            float aps = winRate > 0f ? 1f / winRate : ApsCap;
            if (aps > ApsCap) aps = ApsCap;
            return new Result { WinRate = winRate, Runs = runs, Aps = aps };
        }

        private static bool Playout(BusLevelModel model, float eps, int lookahead, Random rng)
        {
            var s = new BusSimState(model);
            int maxMoves = model.TotalBuses + 2; // QHead is monotonic -> bounded
            for (int move = 0; move <= maxMoves; move++)
            {
                if (s.IsWin()) return true;
                if (!s.HasLegalMove()) return false; // deadlock / stuck

                Span<int> cand = stackalloc int[model.Columns];
                int nc = 0;
                for (int c = 0; c < model.Columns; c++)
                    if (s.CanPull(c) && s.FreeSlot() >= 0) cand[nc++] = c;
                if (nc == 0) return false;

                int chosen;
                if (rng.NextDouble() < eps)
                {
                    chosen = cand[rng.Next(nc)]; // careless pull
                }
                else
                {
                    int bestScore = int.MinValue, bestCount = 0;
                    Span<int> bestCols = stackalloc int[nc];
                    for (int j = 0; j < nc; j++)
                    {
                        var child = s.Clone();
                        int removed = child.ApplyMove(cand[j]);
                        int score = removed;
                        if (child.IsDeadlock()) score -= 1000;
                        if (lookahead > 1 && !child.IsWin())
                            score += GreedyScore(child, lookahead - 1);
                        if (score > bestScore) { bestScore = score; bestCount = 0; bestCols[bestCount++] = cand[j]; }
                        else if (score == bestScore) bestCols[bestCount++] = cand[j];
                    }
                    chosen = bestCols[rng.Next(bestCount)];
                }

                s.ApplyMove(chosen);
            }
            return s.IsWin();
        }

        // Greedy one-line lookahead: most blocks removed over `depth` more plies
        // (no carelessness inside the lookahead).
        private static int GreedyScore(BusSimState s, int depth)
        {
            if (depth <= 0 || !s.HasLegalMove()) return 0;
            int best = 0;
            for (int c = 0; c < s.M.Columns; c++)
            {
                if (!s.CanPull(c) || s.FreeSlot() < 0) continue;
                var child = s.Clone();
                int removed = child.ApplyMove(c);
                int score = removed + GreedyScore(child, depth - 1);
                if (score > best) best = score;
            }
            return best;
        }
    }
}
