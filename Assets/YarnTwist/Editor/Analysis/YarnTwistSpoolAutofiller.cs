using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Auto-fills the top section of a partially-painted YarnTwist level so
    // that color balance is satisfied by construction and the resulting
    // puzzle's measured difficulty lands in a Difficulty-targeted band.
    //
    // Search strategy:
    //  • Stage 1 — a parallel sweep of random spool arrangements (deterministic:
    //    per-candidate seeds are pre-derived from the root seed and the lowest-
    //    index in-band hit always wins, regardless of thread timing).
    //  • Stage 2 — if no arrangement hits the band, sequential hill-climbing
    //    (color swaps that preserve balance and the hidden mask) from the best
    //    candidate, nudging it toward the band far faster than blind rerolls.
    //
    // Difficulty signal is metric-aware: the exact win-path count when it is
    // available (uncapped, small levels), falling back to the never-capping
    // Monte-Carlo win-rate when the count caps (big levels) — which is also how
    // the hidden-spool ratio earns its effect on measured difficulty.
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Analysis/Yarn Twist Spool Auto-fill")]
    public sealed class YarnTwistSpoolAutofiller : LevelCompleterAsset
    {
        private const int Columns       = 4;
        private const int BallsPerSpool = 3;
        private const int BallsPerItem  = 9;

        [Tooltip("Configuration asset with Difficulty curves, caps and timeouts.")]
        [SerializeField] private YarnTwistSpoolAutofillConfig _config;

        // Tests inject an analyzer instance via reflection; production code reads
        // _profile.LevelAnalyzer. Both code paths land here.
        [NonSerialized] private LevelAnalyzerAsset _analyzerOverride;

        // Resolved per-Complete band parameters, shared by the accept/distance helpers.
        private float _winPathTarget, _winPathLow, _winPathHigh;
        private float _winRateTarget, _winRateLow, _winRateHigh;

        public override LevelCompletionResult Complete(LevelDocument doc, GameProfile profile, CompletionRequest req)
        {
            var sw = Stopwatch.StartNew();
            var result = new LevelCompletionResult();

            if (_config == null)
            {
                result.FailureReason = "no YarnTwistSpoolAutofillConfig assigned";
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            var analyzer = _analyzerOverride ?? profile?.LevelAnalyzer;
            if (analyzer == null)
            {
                result.FailureReason = "no analyzer wired on profile";
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            int difficulty = Mathf.Clamp(req?.Difficulty ?? 5, 1, 10);
            int rootSeed = (req != null && req.Seed != 0)
                ? req.Seed
                : new System.Random().Next(1, int.MaxValue);
            result.SeedUsed = rootSeed;

            int capacity = req?.ConveyorCapacityOverride ?? _config.DefaultConveyorCapacity;

            // ── Inventory: per-color item count from grid ────────────────
            var perColor = new Dictionary<string, int>();
            if (doc?.Grid != null)
            {
                foreach (var cell in doc.Grid.Cells)
                {
                    switch (cell)
                    {
                        case YarnBoxCell      b: Bump(perColor, b.ColorId, 1); break;
                        case YarnArrowBoxCell a: Bump(perColor, a.ColorId, 1); break;
                        case YarnTunnelCell   t:
                            if (t.Queue != null)
                                foreach (var q in t.Queue) Bump(perColor, q, 1);
                            break;
                    }
                }
            }

            // Empty grid → empty top section, trivially succeeds.
            if (perColor.Count == 0)
            {
                var emptyTop = new YarnTopSectionData();
                for (int i = 0; i < Columns; i++) emptyTop.Columns.Add(new YarnSpoolColumn());
                result.TopSection = JObject.FromObject(emptyTop);
                result.Succeeded = true;
                result.CandidatesTried = 0;
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            // ── Flat spool list: 3 spools per item-color ────────────────
            var flatColors = new List<string>();
            foreach (var kv in perColor)
                for (int i = 0; i < kv.Value * BallsPerItem / BallsPerSpool; i++)
                    flatColors.Add(kv.Key);

            // ── Resolve target bands ────────────────────────────────────
            float hiddenPct = Mathf.Clamp01(_config.HiddenSpoolRatio.Evaluate(difficulty) / 100f);
            int hiddenN = Mathf.RoundToInt(flatColors.Count * hiddenPct);

            _winPathTarget = _config.WinPathTargetByDifficulty.Evaluate(difficulty);
            _winPathLow    = _winPathTarget * (1f - _config.WinPathTolerance);
            _winPathHigh   = _winPathTarget * (1f + _config.WinPathTolerance);

            _winRateTarget = Mathf.Clamp01(_config.WinRateTargetByDifficulty.Evaluate(difficulty));
            _winRateLow    = Mathf.Clamp01(_winRateTarget - _config.WinRateTolerance);
            _winRateHigh   = Mathf.Clamp01(_winRateTarget + _config.WinRateTolerance);

            // ── Stage 1: parallel random sampling ───────────────────────
            int attempts = Math.Max(1, _config.MaxRerollAttempts);
            var rootRng = new System.Random(rootSeed);
            var seeds = new int[attempts];
            for (int a = 0; a < attempts; a++) seeds[a] = rootRng.Next(1, int.MaxValue);

            var evaluated = new CandResult[attempts];
            int dop = _config.MaxDegreeOfParallelism > 0
                ? _config.MaxDegreeOfParallelism
                : Math.Max(1, Environment.ProcessorCount);

            var options = new ParallelOptions { MaxDegreeOfParallelism = dop };
            try
            {
                Parallel.For(0, attempts, options, (a, state) =>
                {
                    if (sw.ElapsedMilliseconds > _config.TotalTimeoutMs) { state.Stop(); return; }
                    BuildCandidate(seeds[a], flatColors, hiddenN, out var colors, out var hidden);
                    var top = BuildTop(colors, hidden);
                    var analysis = Analyze(analyzer, doc, profile, top, capacity);
                    evaluated[a] = new CandResult { Colors = colors, Hidden = hidden, Top = top, Analysis = analysis };
                });
            }
            catch (AggregateException ex)
            {
                // Surface analyzer faults without losing the whole sweep.
                UnityEngine.Debug.LogError("YarnTwistSpoolAutofiller stage-1 fault: " + ex.Flatten());
            }

            // Deterministic reduction over the evaluated candidates (index order).
            CandResult best = null;
            double bestDist = double.PositiveInfinity;
            for (int a = 0; a < attempts; a++)
            {
                var c = evaluated[a];
                if (c == null) continue; // not evaluated (timed out)
                BumpHist(result.CandidatePathCountHistogram, c.Analysis.WinPathCount);
                result.CandidatesTried++;

                if (InBand(c.Analysis))
                {
                    result.TopSection = c.Top;
                    result.Analysis   = c.Analysis;
                    result.Succeeded  = true;
                    sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                    return result;
                }

                double d = Distance(c.Analysis);
                if (d < bestDist) { bestDist = d; best = c; }
                // Guarantee a best-effort layout even if every candidate is
                // unsolvable (Distance == +inf): keep the first one seen.
                if (best == null) { best = c; bestDist = d; }
            }

            // ── Stage 2: guided hill-climbing from the best candidate ───
            if (best != null && _config.GuidedSteps > 0
                && sw.ElapsedMilliseconds <= _config.TotalTimeoutMs)
            {
                var climbed = GuidedRefine(analyzer, doc, profile, best, bestDist, capacity, rootSeed, sw, result);
                if (climbed != null)
                {
                    if (InBand(climbed.Analysis))
                    {
                        result.TopSection = climbed.Top;
                        result.Analysis   = climbed.Analysis;
                        result.Succeeded  = true;
                        sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                        return result;
                    }
                    best = climbed; // improved best-so-far
                }
            }

            // ── Out of budget — return best-so-far with Succeeded=false ──
            if (best != null)
            {
                result.TopSection = best.Top;
                result.Analysis   = best.Analysis;
            }
            result.Succeeded = false;
            if (string.IsNullOrEmpty(result.FailureReason))
                result.FailureReason = "no candidate landed in Difficulty band; best-effort returned";
            sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
            return result;
        }

        // ── Acceptance & distance (metric-aware) ────────────────────────

        // A candidate is in-band when its precise win-path count lands in the
        // win-path band (small levels), or — when that count caps — when its
        // win-rate lands in the win-rate band (big levels).
        private bool InBand(LevelAnalysisResult a)
        {
            if (a == null || !a.Solvable) return false;
            if (!a.CountWasCapped)
                return a.WinPathCount >= _winPathLow && a.WinPathCount <= _winPathHigh;
            return _config.RolloutCount > 0 && a.RolloutsRun > 0
                && a.WinRate >= _winRateLow && a.WinRate <= _winRateHigh;
        }

        // Normalized 0..N distance to the applicable target. Capped candidates
        // get a +1 penalty so an exact (uncapped) candidate is always preferred
        // when one exists — exact control beats the imperfect-info fallback.
        private double Distance(LevelAnalysisResult a)
        {
            if (a == null || !a.Solvable) return double.PositiveInfinity;
            if (!a.CountWasCapped)
                return Math.Abs(a.WinPathCount - _winPathTarget) / Math.Max(1f, _winPathTarget);
            double wr = a.RolloutsRun > 0 ? a.WinRate : 0.0;
            return 1.0 + Math.Abs(wr - _winRateTarget);
        }

        // ── Stage 2 guided refinement ───────────────────────────────────

        // Hill-climb from `start`: repeatedly swap two differently-colored
        // spools (preserving color balance and the hidden mask by position),
        // keep the mutation only if it reduces distance to the band. Sequential
        // and deterministic (mutation rng derived from the root seed).
        private CandResult GuidedRefine(
            LevelAnalyzerAsset analyzer, LevelDocument doc, GameProfile profile,
            CandResult start, double startDist, int capacity, int rootSeed,
            Stopwatch sw, LevelCompletionResult result)
        {
            var rng = new System.Random(unchecked(rootSeed ^ 0x5EED5EED));
            var curColors = (string[])start.Colors.Clone();
            var hidden    = start.Hidden; // fixed throughout (preserves hidden ratio)
            var cur = start;
            double curDist = startDist;
            int n = curColors.Length;
            if (n < 2) return cur;

            for (int step = 0; step < _config.GuidedSteps; step++)
            {
                if (sw.ElapsedMilliseconds > _config.TotalTimeoutMs) break;

                // Pick two positions with different colors to swap.
                int p = rng.Next(n);
                int q = rng.Next(n);
                if (curColors[p] == curColors[q]) continue;

                (curColors[p], curColors[q]) = (curColors[q], curColors[p]);

                var top = BuildTop(curColors, hidden);
                var analysis = Analyze(analyzer, doc, profile, top, capacity);
                result.CandidatesTried++;
                BumpHist(result.CandidatePathCountHistogram, analysis.WinPathCount);

                double d = Distance(analysis);
                if (d <= curDist)
                {
                    curDist = d;
                    cur = new CandResult { Colors = (string[])curColors.Clone(), Hidden = hidden, Top = top, Analysis = analysis };
                    if (InBand(analysis)) return cur;
                }
                else
                {
                    // Reject: undo the swap.
                    (curColors[p], curColors[q]) = (curColors[q], curColors[p]);
                }
            }
            return cur;
        }

        // ── Candidate construction ──────────────────────────────────────

        private static void BuildCandidate(int seed, List<string> flatColors, int hiddenN,
            out string[] colors, out bool[] hidden)
        {
            var rng = new System.Random(seed);
            colors = flatColors.ToArray();
            Shuffle(colors, rng);
            hidden = new bool[colors.Length];
            var idxs = new int[colors.Length];
            for (int i = 0; i < idxs.Length; i++) idxs[i] = i;
            Shuffle(idxs, rng);
            for (int i = 0; i < Math.Min(hiddenN, idxs.Length); i++) hidden[idxs[i]] = true;
        }

        private static JObject BuildTop(string[] colors, bool[] hidden)
        {
            var data = new YarnTopSectionData();
            for (int i = 0; i < Columns; i++) data.Columns.Add(new YarnSpoolColumn());
            for (int i = 0; i < colors.Length; i++)
                data.Columns[i % Columns].Spools.Add(new YarnSpoolData { ColorId = colors[i], Hidden = hidden[i] });
            return JObject.FromObject(data);
        }

        private LevelAnalysisResult Analyze(LevelAnalyzerAsset analyzer, LevelDocument doc, GameProfile profile, JObject top, int capacity)
        {
            var candDoc = ShallowCopyWithTop(doc, top);
            return analyzer.Analyze(candDoc, profile, new AnalysisRequest
            {
                Mode = AnalysisMode.Count,
                WinPathCap = _config.WinPathCap,
                TimeoutMs  = _config.PerCandidateTimeoutMs,
                ConveyorCapacityOverride = capacity,
                RolloutCount = _config.RolloutCount,
                PlayerLookahead = _config.PlayerLookahead,
            });
        }

        private sealed class CandResult
        {
            public string[]            Colors;
            public bool[]              Hidden;
            public JObject             Top;
            public LevelAnalysisResult Analysis;
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static void Bump(Dictionary<string, int> d, string key, int amount)
        {
            if (string.IsNullOrEmpty(key)) return;
            d.TryGetValue(key, out var v);
            d[key] = v + amount;
        }

        private static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j   = rng.Next(0, i + 1);
                var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }

        private static void BumpHist(Dictionary<int, int> hist, long count)
        {
            // Log-base-2 bucketed histogram (bucket=0 means count=0).
            int bucket = count <= 0 ? 0 : (int)Math.Log(count, 2) + 1;
            hist.TryGetValue(bucket, out var n);
            hist[bucket] = n + 1;
        }

        private static LevelDocument ShallowCopyWithTop(LevelDocument src, JObject top)
        {
            return new LevelDocument
            {
                SchemaVersion = src.SchemaVersion,
                LevelId       = src.LevelId,
                DisplayName   = src.DisplayName,
                Metadata      = src.Metadata,
                Grid          = src.Grid,
                TopSection    = top,
                GameData      = src.GameData,
            };
        }
    }
}
