using System;
using System.Collections.Generic;
using System.IO;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Overnight batch producer for YAK. Loops YAKLevelGenerator, keeps the
    // survivors (solvable + APS in band + not a near-duplicate), and writes each
    // as a LevelDocument JSON + PNG thumbnail + stats JSON into a dated staging
    // folder for curation in the Batch Review window.
    //
    // Runs inside the Editor (it needs the editor-side pipeline assets), via the
    // menu or headless: Unity -batchmode -quit -executeMethod
    //   Hoppa.YAK.Editor.YAKBatchHarness.RunHeadless
    //
    // NOTE: the image-AI HTTP step is deferred — source images come from the
    // generator config's library folder (or a procedural fallback). A future
    // IImageSource that fetches AI images into that folder drops straight in.
    public static class YAKBatchHarness
    {
        private const string ProfilePath = "Assets/YAK/Data/Config/YAKProfile.asset";

        [MenuItem("Window/Hoppa/YAK/Run Batch (20)")]
        public static void RunBatchMenu() => RunBatch(keep: 20, maxAttempts: 80, apsOverride: null, stagingRoot: null);

        // Headless entry point for -executeMethod.
        public static void RunHeadless() => RunBatch(keep: 30, maxAttempts: 150, apsOverride: null, stagingRoot: null);

        public static string RunBatch(int keep, int maxAttempts, float? apsOverride, string stagingRoot)
        {
            var profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            if (profile == null) { Debug.LogError($"[YAKBatch] Profile not found at {ProfilePath}"); return null; }
            if (profile.LevelGenerator == null || profile.LevelAnalyzer == null)
            {
                Debug.LogError("[YAKBatch] Profile needs both a Level Generator and a Level Analyzer wired.");
                return null;
            }

            var cfg = profile.GeneratorConfig as YAKGeneratorConfig;
            float target = apsOverride ?? (cfg != null ? cfg.TargetAPS : 3f);
            float tol    = cfg != null ? cfg.ApsTolerance : 0.6f;
            int rollouts = cfg != null ? cfg.AnalyzerRollouts : 120;

            string root = stagingRoot ?? Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "YAK_Batch");
            string stagingDir = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(stagingDir);

            var serializer = new JsonLevelSerializer();
            var registry   = profile.BuildRegistry();
            var seenSignatures = new HashSet<ulong>();
            var rng = new System.Random(Environment.TickCount);

            int kept = 0, attempts = 0;
            while (kept < keep && attempts < maxAttempts)
            {
                attempts++;
                int seed = rng.Next(1, int.MaxValue);

                LevelGeneratorResult gen;
                try { gen = profile.LevelGenerator.Generate(new LevelGeneratorRequest { Seed = seed, TargetAPS = target }, profile); }
                catch (Exception e) { Debug.LogError($"[YAKBatch] generate threw: {e.Message}"); continue; }
                var doc = gen?.Document;
                if (doc?.Grid == null) continue;

                // Dedup on the grid's color signature.
                ulong sig = GridSignature(doc);
                if (!seenSignatures.Add(sig)) continue;

                // Stats / filter — single authoritative analysis.
                var an = profile.LevelAnalyzer.Analyze(doc, profile, new AnalysisRequest { RolloutCount = rollouts, Seed = seed });
                bool inBand = an.Status == AnalysisStatus.Solvable && Mathf.Abs(an.ApsEstimate - target) <= tol;
                if (!inBand) continue;

                string id = $"yak_gen_{kept:000}_{seed}";
                doc.LevelId = id;

                File.WriteAllText(Path.Combine(stagingDir, id + ".json"), serializer.Save(doc, registry));

                var png = LevelThumbnail.RenderPng(doc, profile.ColorPalette, cellPixels: 4);
                if (png != null) File.WriteAllBytes(Path.Combine(stagingDir, id + ".png"), png);

                BatchStaging.WriteStats(Path.Combine(stagingDir, id + BatchStaging.StatsSuffix), new LevelStats
                {
                    id = id,
                    status = an.Status.ToString(),
                    solvable = an.Solvable,
                    aps = an.ApsEstimate,
                    band = an.Band,
                    distinctColors = DistinctColors(doc),
                });
                kept++;
            }

            Debug.Log($"[YAKBatch] kept {kept}/{keep} (in {attempts} attempts) → {stagingDir}");
            AssetDatabase.Refresh();
            return stagingDir;
        }

        private static int DistinctColors(LevelDocument doc)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cell in doc.Grid.Cells)
                if (cell is IColoredCell c && !string.IsNullOrEmpty(c.ColorId)) set.Add(c.ColorId);
            return set.Count;
        }

        // Order-sensitive FNV-1a over per-cell colorIds — a fingerprint of the grid
        // for near-duplicate rejection.
        private static ulong GridSignature(LevelDocument doc)
        {
            ulong h = 1469598103934665603UL; const ulong P = 1099511628211UL;
            foreach (var cell in doc.Grid.Cells)
            {
                string id = (cell is IColoredCell c) ? c.ColorId ?? "" : "";
                foreach (char ch in id) h = (h ^ ch) * P;
                h = (h ^ '|') * P;
            }
            return h;
        }
    }
}
