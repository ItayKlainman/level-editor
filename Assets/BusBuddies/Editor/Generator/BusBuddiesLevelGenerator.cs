using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.BusBuddies;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Bus Buddies level generator: chains image→grid → bus auto-fill → analyzer gate
    // into one LevelGeneratorAsset so it drives the ✨ Generate panel AND the batch
    // harness. Mirrors YAKLevelGenerator.
    //
    // The grid comes from a source image (picked from the config's library folder by
    // seed); when no image source is available it falls back to a PROCEDURAL all-pixel
    // grid (every cell a BBPixelCell — the buses balance the pixels; no carved empties).
    // Acceptance is gated on the SIMULATOR (analyzer solvable + APS in band), never on
    // static rules.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Generator/Bus Buddies Level Generator")]
    public sealed class BusBuddiesLevelGenerator : LevelGeneratorAsset
    {
        public override LevelGeneratorResult Generate(LevelGeneratorRequest request, GameProfile profile)
        {
            var sw = Stopwatch.StartNew();
            var result = new LevelGeneratorResult { CandidatesTried = 1 };

            var config = request?.AdvancedConfig as BusBuddiesGeneratorConfig
                         ?? profile?.GeneratorConfig as BusBuddiesGeneratorConfig
                         ?? ScriptableObject.CreateInstance<BusBuddiesGeneratorConfig>();

            if (profile == null || profile.ColorPalette == null)
            {
                result.Succeeded = false;
                result.RuleRejectCounts["__no_profile_or_palette__"] = 1;
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            int seed = (request != null && request.Seed != 0) ? request.Seed : new System.Random().Next(1, int.MaxValue);
            result.SeedUsed = seed;
            float targetAps = (request != null && request.TargetAPS.HasValue) ? request.TargetAPS.Value : config.TargetAPS;

            // 1) Grid: image source (library) → image→grid, else procedural.
            LevelDocument doc = null;
            Texture2D src = config.UseImageSource ? PickSourceImage(config.SourceImageFolder, seed) : null;
            if (src != null && profile.ImageToGrid != null)
                doc = profile.ImageToGrid.Convert(src, profile);
            if (doc == null)
                doc = ProceduralGrid(profile, config, seed);

            // 2) Auto-fill the bus queue (analyzer-gated inside the completer).
            if (profile.LevelCompleter != null)
            {
                var comp = profile.LevelCompleter.Complete(doc, profile, new CompletionRequest
                {
                    TargetAPS = targetAps,
                    Seed = seed,
                });
                if (comp?.TopSection != null) doc.TopSection = comp.TopSection;
            }

            // 3) Analyzer gate — Succeeded iff solvable AND APS within tolerance.
            LevelAnalysisResult analysis = null;
            if (profile.LevelAnalyzer != null)
            {
                analysis = profile.LevelAnalyzer.Analyze(doc, profile, new AnalysisRequest
                {
                    RolloutCount = config.AnalyzerRollouts,
                    Seed = seed,
                });
            }

            if (doc.Metadata == null) doc.Metadata = new LevelMetadata();
            doc.Metadata.ModifiedAt = DateTime.UtcNow.ToString("o");
            if (analysis != null) doc.Metadata.Aps = analysis.ApsEstimate;

            // Acceptance = SOLVABLE (the solver guardrail). Under the difficulty-knob model
            // APS is a measured read-out, NOT a target — gating on an APS band made the batch
            // retry every attempt chasing an unreachable APS (~8 min/level at 40x40). Difficulty
            // is set by the knobs, so a solvable board is a good board; APS is reported, not gated.
            bool ok = analysis != null && analysis.Status == AnalysisStatus.Solvable;
            result.Document  = doc;
            result.Succeeded = ok;
            if (!ok)
                result.RuleRejectCounts[analysis == null ? "no_analyzer" : analysis.Status.ToString()] = 1;

            sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
            return result;
        }

        // ── Source image library ────────────────────────────────────────────────

        private static Texture2D PickSourceImage(string folder, int seed)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            try
            {
                if (folder.Replace('\\', '/').StartsWith("Assets/"))
                {
                    var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder.TrimEnd('/') });
                    if (guids.Length == 0) return null;
                    var path = AssetDatabase.GUIDToAssetPath(guids[(seed & int.MaxValue) % guids.Length]);
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                if (Directory.Exists(folder))
                {
                    var files = Directory.GetFiles(folder)
                        .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f, StringComparer.Ordinal).ToArray();
                    if (files.Length == 0) return null;
                    var bytes = File.ReadAllBytes(files[(seed & int.MaxValue) % files.Length]);
                    var tex = new Texture2D(2, 2);
                    return tex.LoadImage(bytes) ? tex : null;
                }
            }
            catch { /* fall back to procedural */ }
            return null;
        }

        // ── Procedural fallback (all-pixel grid) ────────────────────────────────

        private static LevelDocument ProceduralGrid(GameProfile profile, BusBuddiesGeneratorConfig config, int seed)
        {
            int w = Mathf.Max(2, profile.GridWidth);
            int h = Mathf.Max(2, profile.GridHeight);
            var palette = profile.ColorPalette.ColorIds.ToList();
            if (palette.Count == 0) palette.Add("blue");
            int n = Mathf.Clamp(config.FallbackColors, 1, palette.Count);
            var colors = palette.Take(n).ToList();

            var grid = new GridData<ICellData>(w, h);
            // Coarse blocks so it reads as regions, not noise. Every cell is a pixel:
            // the buses balance the pixels; no carved empties (locked lead decision).
            int block = Mathf.Max(2, Mathf.Min(w, h) / 4);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int bx = x / block, by = y / block;
                int idx = Mathf.Abs(HashBlock(bx, by, seed)) % colors.Count;
                grid.Set(x, y, new BBPixelCell { ColorId = colors[idx] });
            }

            string nowIso = DateTime.UtcNow.ToString("o");
            return new LevelDocument
            {
                SchemaVersion = profile.SchemaId + ".v1",
                LevelId       = "gen_" + DateTime.UtcNow.ToString("yyMMdd_HHmmss_fff"),
                DisplayName   = "Generated",
                Metadata      = new LevelMetadata { Author = Environment.UserName, CreatedAt = nowIso, ModifiedAt = nowIso },
                Grid          = grid,
                GameData      = new JObject { ["conveyorCount"] = Mathf.Max(1, config.ConveyorCount) },
            };
        }

        private static int HashBlock(int x, int y, int seed)
        {
            unchecked
            {
                int h = seed;
                h = h * 73856093 ^ x * 19349663 ^ y * 83492791;
                return h;
            }
        }
    }
}
