using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.YAK.Editor
{
    // Walks a YAKDifficultyCurveConfig and mass-produces numbered, difficulty-scaled
    // YAK levels through the EXISTING generate→autofill→analyze pipeline. For each
    // level it assembles a transient, tier-configured copy of the wired GameProfile
    // (via YAKTierProfileBuilder — never mutates on-disk assets) and calls the
    // unchanged profile.LevelGenerator.Generate. Output is level_N.json (+ .png +
    // stats) routed through the existing BatchReviewWindow staging convention.
    public static class YAKCurveBatchHarness
    {
        public static string RunCurve(YAKDifficultyCurveConfig curve, GameProfile baseProfile,
                                      int attemptsPerLevel, string stagingRoot)
        {
            if (curve == null || baseProfile == null) return null;
            var errors = curve.Validate();
            if (errors.Count > 0)
            {
                Debug.LogError("[YAKCurve] invalid curve: " + string.Join("; ", errors));
                return null;
            }
            if (baseProfile.LevelGenerator == null || baseProfile.LevelAnalyzer == null)
            {
                Debug.LogError("[YAKCurve] profile needs a Level Generator and Analyzer wired.");
                return null;
            }

            int total = curve.TotalLevels();
            if (total <= 0) return null;

            string root = stagingRoot ?? Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "YAK_Batch");
            string stagingDir = Path.Combine(root, "curve_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(stagingDir);

            var serializer = new JsonLevelSerializer();
            var registry = baseProfile.BuildRegistry();
            var rng = new System.Random(Environment.TickCount);

            for (int i = 1; i <= total; i++)
            {
                var tier = curve.TierForLevel(i);
                if (tier == null) continue;
                var tp = YAKTierProfileBuilder.Build(baseProfile, tier);
                try
                {
                    LevelDocument best = null; LevelAnalysisResult bestAn = null;
                    float bestDelta = float.MaxValue; bool accepted = false;

                    for (int a = 0; a < Mathf.Max(1, attemptsPerLevel) && !accepted; a++)
                    {
                        int seed = rng.Next(1, int.MaxValue);
                        LevelGeneratorResult gen;
                        try { gen = tp.Profile.LevelGenerator.Generate(
                            new LevelGeneratorRequest { Seed = seed, TargetAPS = tier.TargetAps }, tp.Profile); }
                        catch (Exception e) { Debug.LogError($"[YAKCurve] L{i} generate threw: {e.Message}"); continue; }

                        var doc = gen?.Document;
                        if (doc?.Grid == null) continue;

                        var an = tp.Profile.LevelAnalyzer.Analyze(doc, tp.Profile,
                            new AnalysisRequest { RolloutCount = 120, Seed = seed });
                        float delta = an.Status == AnalysisStatus.Solvable
                            ? Mathf.Abs(an.ApsEstimate - tier.TargetAps) : float.MaxValue;

                        if (gen.Succeeded) { best = doc; bestAn = an; bestDelta = delta; accepted = true; }
                        else if (delta < bestDelta) { best = doc; bestAn = an; bestDelta = delta; }
                    }

                    if (best == null) { Debug.LogWarning($"[YAKCurve] L{i}: no candidate produced."); continue; }

                    string id = $"level_{i}";
                    best.LevelId = id;
                    bool offTarget = !accepted;

                    File.WriteAllText(Path.Combine(stagingDir, id + ".json"), serializer.Save(best, registry));

                    var png = LevelThumbnail.RenderPng(best, baseProfile.ColorPalette, cellPixels: 4);
                    if (png != null) File.WriteAllBytes(Path.Combine(stagingDir, id + ".png"), png);

                    BatchStaging.WriteStats(Path.Combine(stagingDir, id + BatchStaging.StatsSuffix), new LevelStats
                    {
                        id = id,
                        status = bestAn != null ? bestAn.Status.ToString() : "Unknown",
                        solvable = bestAn != null && bestAn.Solvable,
                        aps = bestAn != null ? bestAn.ApsEstimate : 0f,
                        band = bestAn != null ? bestAn.Band : 0,
                        distinctColors = 0,
                        tier = tier.Name,
                        targetAps = tier.TargetAps,
                        offTarget = offTarget,
                    });
                }
                finally { tp.Cleanup(); }
            }

            Debug.Log($"[YAKCurve] generated {total} levels → {stagingDir}");
            AssetDatabase.Refresh();
            return stagingDir;
        }
    }
}
