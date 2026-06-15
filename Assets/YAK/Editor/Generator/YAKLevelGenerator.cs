using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // YAK level generator: chains the existing pipeline pieces — image→grid →
    // spool auto-fill → analyzer gate — into one LevelGeneratorAsset so it drives
    // the ✨ Generate panel AND the overnight batch harness.
    //
    // The grid comes from a source image (picked from the config's library folder
    // by seed); when no image source is available it falls back to a procedural
    // grid so generation never hard-fails. Acceptance is gated on the SIMULATOR
    // (analyzer solvable + APS in band) — never on static rules alone.
    [CreateAssetMenu(menuName = "Hoppa/YAK/Generator/YAK Level Generator")]
    public sealed class YAKLevelGenerator : LevelGeneratorAsset
    {
        public override LevelGeneratorResult Generate(LevelGeneratorRequest request, GameProfile profile)
        {
            var sw = Stopwatch.StartNew();
            var result = new LevelGeneratorResult { CandidatesTried = 1 };

            var config = request?.AdvancedConfig as YAKGeneratorConfig
                         ?? profile?.GeneratorConfig as YAKGeneratorConfig
                         ?? ScriptableObject.CreateInstance<YAKGeneratorConfig>();

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

            // 2) Auto-fill spools (analyzer-gated inside the completer).
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

            bool inBand = analysis != null
                          && analysis.Status == AnalysisStatus.Solvable
                          && Mathf.Abs(analysis.ApsEstimate - targetAps) <= config.ApsTolerance;
            result.Document  = doc;
            result.Succeeded = inBand;
            if (!inBand)
            {
                string reason = analysis == null ? "no_analyzer"
                    : analysis.Status != AnalysisStatus.Solvable ? analysis.Status.ToString()
                    : "aps_out_of_band";
                result.RuleRejectCounts[reason] = 1;
            }

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

        // ── Procedural fallback ─────────────────────────────────────────────────

        private static LevelDocument ProceduralGrid(GameProfile profile, YAKGeneratorConfig config, int seed)
        {
            int w = Mathf.Max(2, profile.GridWidth);
            int h = Mathf.Max(2, profile.GridHeight);
            var palette = profile.ColorPalette.ColorIds.ToList();
            if (palette.Count == 0) palette.Add("blue");
            int n = Mathf.Clamp(config.FallbackColors, 1, palette.Count);
            var colors = palette.Take(n).ToList();

            var rng = new System.Random(seed);
            var grid = new GridData<ICellData>(w, h);
            // Coarse blocks so it reads as regions, not noise.
            int block = Mathf.Max(2, Mathf.Min(w, h) / 4);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int bx = x / block, by = y / block;
                int idx = Mathf.Abs(HashBlock(bx, by, seed)) % colors.Count;
                grid.Set(x, y, new YAKWoolCell { ColorId = colors[idx] });
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
