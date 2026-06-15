using System;
using System.Collections.Generic;

namespace Hoppa.YAK.Sim
{
    // Monte-Carlo "average player" difficulty estimator. Engine-agnostic plain C#.
    //
    // Difficulty is MEASURED, not labelled: run N seeded playouts of a myopic,
    // occasionally-careless player and report the win-rate. APS (Attempts Per
    // Solve) ≈ 1 / win-rate — how many tries an average player needs to win.
    //
    // Policy (lookahead L, carelessness ε): with probability ε make a careless
    // (random) send; otherwise pick the send that greedily clears the most wool
    // over the next L plies, avoiding a send that immediately deadlocks. ε and L
    // are the tunable knobs the calibration step fits to real player data — until
    // then the caller marks the APS "uncalibrated".
    //
    // Note (YAK v1): there are no hidden spools, so column heads are always
    // visible and lookahead-1 planning sees everything it needs; the Hidden flag
    // is carried by the model but not yet consulted here.
    public sealed class YakAveragePlayer
    {
        public struct Config
        {
            public float Epsilon;   // 0..1 carelessness
            public int   Lookahead; // >=1 plies of greedy planning
            public int   Runs;      // playout count
            public int   Seed;      // base RNG seed (0 = time-based)

            public static Config Default => new Config { Epsilon = 0.1f, Lookahead = 1, Runs = 400, Seed = 12345 };
        }

        public struct Result
        {
            public float WinRate; // wins / runs
            public int   Runs;
            public float Aps;     // 1/WinRate, or +Infinity when no playout won
        }

        // Hard cap used when converting a zero win-rate to a finite APS for
        // reporting/scoring (a level only an exact solver beats reads as "≥ cap").
        public const float ApsCap = 99f;

        public Result Estimate(YakLevelModel model, Config cfg)
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
            return new Result
            {
                WinRate = winRate,
                Runs    = runs,
                Aps     = winRate > 0f ? 1f / winRate : float.PositiveInfinity,
            };
        }

        private static bool Playout(YakLevelModel model, float eps, int lookahead, Random rng)
        {
            var s = new YakSimState(model);
            int maxMoves = model.TotalSpools + 2; // QHead is monotonic → bounded
            for (int move = 0; move <= maxMoves; move++)
            {
                if (s.IsWin()) return true;
                if (!s.HasLegalMove()) return false; // deadlock / stuck

                // Gather sendable columns (a free slot is guaranteed by HasLegalMove).
                Span<int> cand = stackalloc int[model.Columns];
                int nc = 0;
                for (int c = 0; c < model.Columns; c++)
                    if (s.CanSend(c)) cand[nc++] = c;

                int chosen;
                if (rng.NextDouble() < eps)
                {
                    chosen = cand[rng.Next(nc)]; // careless send
                }
                else
                {
                    // Greedy: highest L-ply clear score; random tie-break.
                    int bestScore = int.MinValue, bestCount = 0;
                    Span<int> bestCols = stackalloc int[nc];
                    for (int j = 0; j < nc; j++)
                    {
                        var child = s.Clone();
                        int consumed = child.ApplyMove(cand[j]);
                        int score = consumed;
                        if (child.IsDeadlock()) score -= 1000;      // avoid self-inflicted loss
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

        // Greedy one-line lookahead: best total wool cleared over `depth` more
        // plies (no carelessness inside the lookahead). Cheap: branching ≤ columns.
        private static int GreedyScore(YakSimState s, int depth)
        {
            if (depth <= 0 || !s.HasLegalMove()) return 0;
            int best = 0;
            for (int c = 0; c < s.M.Columns; c++)
            {
                if (!s.CanSend(c) || s.FreeSlot() < 0) continue;
                var child = s.Clone();
                int consumed = child.ApplyMove(c);
                int score = consumed + GreedyScore(child, depth - 1);
                if (score > best) best = score;
            }
            return best;
        }

        // ── Calibration hook (built now, run when real data lands) ────────────
        // Coarse grid-search of (ε, lookahead) minimising squared error between
        // simulated APS and observed APS over a labelled set. Returns the best
        // config plus the residual error. Until this is run against real YAK
        // player data the analyzer should report ApsCalibrated = false.
        public struct CalibrationResult
        {
            public float Epsilon;
            public int   Lookahead;
            public float Error;     // sum of squared APS error over the set
            public bool  HasData;   // false when the labelled set was empty
        }

        public CalibrationResult Calibrate(
            IReadOnlyList<(YakLevelModel model, float observedAps)> labelled,
            int runs = 400, int seed = 12345,
            int[] lookaheads = null, float epsMin = 0f, float epsMax = 0.5f, float epsStep = 0.02f)
        {
            var best = new CalibrationResult { Epsilon = Config.Default.Epsilon, Lookahead = 1, Error = float.PositiveInfinity, HasData = false };
            if (labelled == null || labelled.Count == 0) return best;
            best.HasData = true;
            lookaheads ??= new[] { 1, 2 };

            foreach (int la in lookaheads)
            for (float eps = epsMin; eps <= epsMax + 1e-6f; eps += epsStep)
            {
                float err = 0f;
                foreach (var (model, observed) in labelled)
                {
                    var res = Estimate(model, new Config { Epsilon = eps, Lookahead = la, Runs = runs, Seed = seed });
                    float simAps = float.IsPositiveInfinity(res.Aps) ? ApsCap : Math.Min(res.Aps, ApsCap);
                    float obs    = Math.Min(observed, ApsCap);
                    float d = simAps - obs;
                    err += d * d;
                }
                if (err < best.Error) { best.Error = err; best.Epsilon = eps; best.Lookahead = la; }
            }
            return best;
        }
    }
}
