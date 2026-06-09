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

        [Tooltip("Configuration asset with APS curves, caps and timeouts.")]
        [SerializeField] private YarnTwistSpoolAutofillConfig _config;

        // Spool-side mechanics the auto-fill can produce. The generic Auto-fill panel
        // renders one checkbox per name; checked ⇒ included. (Grid mechanics are
        // painter-authored, so they are not listed here.)
        public const string ToggleHidden    = "Hidden Spools";
        public const string ToggleConnected = "Connected Spools";
        private static readonly string[] _mechanicToggles = { ToggleHidden, ToggleConnected };
        public override System.Collections.Generic.IReadOnlyList<string> MechanicToggles => _mechanicToggles;

        // A mechanic is included only when the request explicitly says so. The
        // Auto-fill panel always sends a full dict (checkboxes default checked), so
        // designers get mechanics ON by default; programmatic callers that pass no
        // dict get the conservative no-extra-mechanics behaviour.
        private static bool ToggleOn(CompletionRequest req, string name)
            => req?.MechanicToggles != null
               && req.MechanicToggles.TryGetValue(name, out var on)
               && on;

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

            int rootSeed = (req != null && req.Seed != 0)
                ? req.Seed
                : new System.Random().Next(1, int.MaxValue);
            result.SeedUsed = rootSeed;

            int capacity = req?.ConveyorCapacityOverride ?? _config.DefaultConveyorCapacity;

            // ── Inventory: per-color item count from grid ────────────────
            // Connected boxes are ordinary YarnBoxCells — each still contributes its own
            // 9 balls (= 3 spools) of its own color, so color balance is unchanged by the
            // connection. The pair's "clear together" effect on solvability/difficulty is
            // handled entirely by the analyzer (see YarnTwistLevelAnalyzer.Partner); this
            // autofiller scores candidates via analyzer.Analyze on the real grid, so it
            // stays correct by delegation — do NOT special-case connected boxes here.
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
            // Primary knob is APS (Attempts Per Solve, 1–6). When a request omits it
            // we fall back to the legacy Difficulty curves so older callers still work.
            if (req?.TargetAPS.HasValue == true)
            {
                float aps = Mathf.Clamp(req.TargetAPS.Value, 1f, 6f);
                _winPathTarget = _config.WinPathTargetByAPS.Evaluate(aps);
                _winRateTarget = Mathf.Clamp01(_config.WinRateTargetByAPS.Evaluate(aps));
            }
            else
            {
                int difficulty = Mathf.Clamp(req?.Difficulty ?? 5, 1, 10);
                _winPathTarget = _config.WinPathTargetByDifficulty.Evaluate(difficulty);
                _winRateTarget = Mathf.Clamp01(_config.WinRateTargetByDifficulty.Evaluate(difficulty));
            }
            _winPathLow    = _winPathTarget * (1f - _config.WinPathTolerance);
            _winPathHigh   = _winPathTarget * (1f + _config.WinPathTolerance);
            _winRateLow    = Mathf.Clamp01(_winRateTarget - _config.WinRateTolerance);
            _winRateHigh   = Mathf.Clamp01(_winRateTarget + _config.WinRateTolerance);

            // ── Mechanic toggles → how many hidden / connected the fill produces ──
            float hiddenPct = ToggleOn(req, ToggleHidden)
                ? Mathf.Clamp01(_config.HiddenSpoolPercent / 100f) : 0f;
            int hiddenN = Mathf.RoundToInt(flatColors.Count * hiddenPct);
            int connectedPairs = ToggleOn(req, ToggleConnected)
                ? Mathf.Max(0, _config.ConnectedSpoolPairs) : 0;

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
                    BuildCandidate(seeds[a], flatColors, hiddenN, connectedPairs, out var colors, out var hidden, out var conn);
                    var top = BuildTop(colors, hidden, conn);
                    var analysis = Analyze(analyzer, doc, profile, top, capacity);
                    evaluated[a] = new CandResult { Colors = colors, Hidden = hidden, Conn = conn, Top = top, Analysis = analysis };
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
            var conn      = start.Conn;   // fixed throughout (connections are positional, color-blind)
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

                var top = BuildTop(curColors, hidden, conn);
                var analysis = Analyze(analyzer, doc, profile, top, capacity);
                result.CandidatesTried++;
                BumpHist(result.CandidatePathCountHistogram, analysis.WinPathCount);

                double d = Distance(analysis);
                if (d <= curDist)
                {
                    curDist = d;
                    cur = new CandResult { Colors = (string[])curColors.Clone(), Hidden = hidden, Conn = conn, Top = top, Analysis = analysis };
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

        private static void BuildCandidate(int seed, List<string> flatColors, int hiddenN, int connectedPairs,
            out string[] colors, out bool[] hidden, out int[] conn)
        {
            var rng = new System.Random(seed);
            colors = flatColors.ToArray();
            Shuffle(colors, rng);
            hidden = new bool[colors.Length];
            var idxs = new int[colors.Length];
            for (int i = 0; i < idxs.Length; i++) idxs[i] = i;
            Shuffle(idxs, rng);
            for (int i = 0; i < Math.Min(hiddenN, idxs.Length); i++) hidden[idxs[i]] = true;
            conn = BuildConnections(colors, hidden, connectedPairs, rng);
        }

        // Deterministically assign up to `pairs` connected-spool pairs. A flat spool
        // index i lands at column (i % Columns), so a pair links indices in
        // immediately-adjacent columns (YarnSpoolConnection.CanComplete). Every
        // tentative pair is vetted with ConnectionsDeadlock (color-blind soft-lock
        // check): a pair that would soft-lock is reverted, so a cramped layout simply
        // yields fewer pairs. Returns a per-position ConnectionId array (-1 = none).
        private static int[] BuildConnections(string[] colors, bool[] hidden, int pairs, System.Random rng)
        {
            int n = colors.Length;
            var conn = new int[n];
            for (int i = 0; i < n; i++) conn[i] = -1;
            if (pairs <= 0 || n < 2) return conn;

            int id = 1, made = 0, attempts = 0;
            int maxAttempts = pairs * 30 + 60;
            var partners = new List<int>(n);
            while (made < pairs && attempts++ < maxAttempts)
            {
                int iA = rng.Next(n);
                if (conn[iA] != -1) continue;
                int colA = iA % Columns;
                int colB = colA + (rng.Next(2) == 0 ? -1 : 1);
                if (colB < 0 || colB >= Columns) continue;

                partners.Clear();
                for (int j = 0; j < n; j++)
                    if (conn[j] == -1 && j % Columns == colB) partners.Add(j);
                if (partners.Count == 0) continue;
                int iB = partners[rng.Next(partners.Count)];

                conn[iA] = id; conn[iB] = id;
                if (YarnSpoolConnection.ConnectionsDeadlock(BuildTopData(colors, hidden, conn)))
                {
                    conn[iA] = -1; conn[iB] = -1; continue; // would soft-lock — drop it
                }
                id++; made++;
            }
            return conn;
        }

        private static JObject BuildTop(string[] colors, bool[] hidden, int[] conn)
            => JObject.FromObject(BuildTopData(colors, hidden, conn));

        // Round-robins the flat spool list into Columns columns. Connections (when the
        // 'Connected Spools' toggle is on) are written as a shared ConnectionId on the
        // two linked spools; the analyzer's lock modelling (via Analyze) keeps win-path
        // / win-rate scoring accurate, so connected candidates are scored on their real
        // difficulty rather than assumed.
        private static YarnTopSectionData BuildTopData(string[] colors, bool[] hidden, int[] conn)
        {
            var data = new YarnTopSectionData();
            for (int i = 0; i < Columns; i++) data.Columns.Add(new YarnSpoolColumn());
            for (int i = 0; i < colors.Length; i++)
                data.Columns[i % Columns].Spools.Add(new YarnSpoolData
                {
                    ColorId      = colors[i],
                    Hidden       = hidden[i],
                    ConnectionId = (conn != null && conn[i] >= 0) ? conn[i] : (int?)null,
                });
            return data;
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
            public int[]               Conn;   // per-position ConnectionId, -1 = none
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
