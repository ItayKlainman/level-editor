using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Auto-fills the top section of a partially-painted YarnTwist level so
    // that color balance is satisfied by construction and the resulting
    // puzzle's win-path count lands in a Difficulty-targeted band.
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

            // ── Reroll loop ─────────────────────────────────────────────
            var rng = new System.Random(rootSeed);
            float hiddenPct = Mathf.Clamp01(_config.HiddenSpoolRatio.Evaluate(difficulty) / 100f);
            int hiddenN = Mathf.RoundToInt(flatColors.Count * hiddenPct);
            float target = _config.WinPathTargetByDifficulty.Evaluate(difficulty);
            float bandLow = target * (1f - _config.WinPathTolerance);
            float bandHigh = target * (1f + _config.WinPathTolerance);

            JObject bestTop = null;
            LevelAnalysisResult bestAnalysis = null;
            double bestDist = double.PositiveInfinity;

            for (int attempt = 0; attempt < _config.MaxRerollAttempts; attempt++)
            {
                if (sw.ElapsedMilliseconds > _config.TotalTimeoutMs) break;

                var working = new List<string>(flatColors);
                Shuffle(working, rng);
                var hiddenMask = new bool[working.Count];
                var idxs = Enumerable.Range(0, working.Count).ToList();
                Shuffle(idxs, rng);
                for (int i = 0; i < Math.Min(hiddenN, idxs.Count); i++) hiddenMask[idxs[i]] = true;

                var data = new YarnTopSectionData();
                for (int i = 0; i < Columns; i++) data.Columns.Add(new YarnSpoolColumn());
                for (int i = 0; i < working.Count; i++)
                    data.Columns[i % Columns].Spools.Add(new YarnSpoolData
                    {
                        ColorId = working[i],
                        Hidden  = hiddenMask[i],
                    });

                var topJson = JObject.FromObject(data);
                var candDoc = ShallowCopyWithTop(doc, topJson);

                var analysis = analyzer.Analyze(candDoc, profile, new AnalysisRequest
                {
                    Mode = AnalysisMode.Count,
                    WinPathCap = _config.WinPathCap,
                    TimeoutMs  = _config.PerCandidateTimeoutMs,
                    ConveyorCapacityOverride = capacity,
                });

                BumpHist(result.CandidatePathCountHistogram, analysis.WinPathCount);
                result.CandidatesTried++;

                if (analysis.Solvable && !analysis.CountWasCapped
                    && analysis.WinPathCount >= bandLow && analysis.WinPathCount <= bandHigh)
                {
                    result.TopSection = topJson;
                    result.Analysis = analysis;
                    result.Succeeded = true;
                    sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                    return result;
                }

                double dist = Math.Abs(analysis.WinPathCount - target);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTop = topJson;
                    bestAnalysis = analysis;
                }
            }

            // Out of attempts — return best-so-far with Succeeded=false.
            result.TopSection = bestTop;
            result.Analysis   = bestAnalysis;
            result.Succeeded  = false;
            if (string.IsNullOrEmpty(result.FailureReason))
                result.FailureReason = "no candidate landed in Difficulty band; best-effort returned";
            sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
            return result;
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
