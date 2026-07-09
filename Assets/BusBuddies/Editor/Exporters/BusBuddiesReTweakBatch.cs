using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Re-tweaks the shipped Bus Buddies GAME levels (level_1..level_8) with the NEW
    // difficulty model so their bus counts and capacities are clean, then writes a
    // STAGING preview — it NEVER touches the real game files.
    //
    // Per level: import the game JSON → apply the fixed re-tweak difficulty knobs
    // (Columns = the level's own existing column count) → run the profile's
    // BusBuddiesAutofiller (Complete) → export via BusBuddiesGameLevelExporter into
    //   <editor-core>/StagingExports/BusBuddies/ReTweaked/level_N.json.
    // The picture (PixelColors) is preserved identically; only the buses change.
    //
    // level_test.json is skipped (it has no PixelColors).
    public static class BusBuddiesReTweakBatch
    {
        private const string ProfilePath = "Assets/BusBuddies/Data/Config/BusBuddiesProfile.asset";

        // Real shipped game levels (READ ONLY — never written to).
        public const string DefaultSourceDir =
            "E:/Projects/Hoppa/BusBuddies/Assets/_BUB/Resources/Configs/Levels";

        // Staging output, project-relative to THIS editor-core repo.
        public const string DefaultOutSubdir = "StagingExports/BusBuddies/ReTweaked";

        private const int FirstLevel = 1;
        private const int LastLevel = 8;

        // The fixed re-tweak knobs. Columns is overridden per-level with the level's
        // own existing column count.
        public static BusBuddiesDifficultySettings DefaultSettings() => new BusBuddiesDifficultySettings
        {
            BusesChunks = 4,
            DeviationPercent = 0.2f,
            Columns = 3,          // placeholder — replaced per-level
            Difficulty = 1,
            NoSingleBusColor = true,
            RoundToFive = true,
        };

        [MenuItem("Window/Hoppa/BusBuddies/Re-tweak Game Levels")]
        public static void RunMenu()
            => RunHeadless(DefaultSourceDir, ResolveProjectRelative(DefaultOutSubdir), DefaultSettings());

        // Runs the full batch in one call (menu / -executeMethod). For the heaviest
        // levels prefer ProcessLevelToDisk one level at a time to keep the Editor
        // responsive; this convenience loop keeps everything in memory.
        public static string RunHeadless(string sourceDir, string outDir, BusBuddiesDifficultySettings settings)
        {
            var profile = LoadProfileOrLog();
            if (profile == null) return null;

            Directory.CreateDirectory(outDir);
            var rows = new List<ReTweakRow>();
            for (int n = FirstLevel; n <= LastLevel; n++)
            {
                string src = Path.Combine(sourceDir, "level_" + n + ".json");
                if (!File.Exists(src)) { Debug.LogWarning($"[BB Re-tweak] missing {src}"); continue; }
                rows.Add(ProcessLevel(profile, src, outDir, settings));
            }
            string report = WriteReport(rows, outDir);
            AssetDatabase.Refresh();
            Debug.Log($"[BB Re-tweak] {rows.Count} level(s) → {outDir}\nReport: {report}");
            return outDir;
        }

        // ── Single-level entry so a caller can run one level per script-execute
        //    (keeps each Roslyn call short; the 40×40 level_3 is the heaviest). ──
        public static ReTweakRow ProcessLevel(
            GameProfile profile, string sourceFile, string outDir, BusBuddiesDifficultySettings settingsTemplate)
        {
            var imported = BusBuddiesGameLevelImporter.ImportFile(sourceFile);
            var doc = imported.Document;
            int levelNum = ParseLevelNum(sourceFile);

            // Apply the fixed knobs; Columns = the level's own existing column count.
            var settings = settingsTemplate.Clone();
            settings.Columns = Mathf.Clamp(imported.ColumnCount, 1, 5);
            settings.WriteTo(doc);

            // NEW-model fill via the profile's completer (BusBuddiesAutofiller).
            var completer = profile.LevelCompleter;
            if (completer == null)
                throw new InvalidOperationException("Profile has no LevelCompleter (BusBuddiesAutofiller) wired.");

            var result = completer.Complete(doc, profile, new CompletionRequest
            {
                Seed = levelNum,                                  // deterministic per level
                ConveyorCapacityOverride = imported.SlotsAmount,
            });
            doc.TopSection = result.TopSection;

            // Export to STAGING — filename → level_<N>.json (never into the BB project).
            var exporter = ScriptableObject.CreateInstance<BusBuddiesGameLevelExporter>();
            exporter.SetTestDependencies(outDir, imported.SlotsAmount);
            exporter.Export(doc, profile.BuildRegistry(), sourceFile);
            UnityEngine.Object.DestroyImmediate(exporter);

            var afterBuses = FlattenBuses(result.TopSection);
            var row = new ReTweakRow
            {
                Level  = levelNum,
                Width  = doc.Grid.Width,
                Height = doc.Grid.Height,
                Slots  = imported.SlotsAmount,
                BeforeBuses = imported.OriginalBuses.Count,
                BeforeHist  = Histogram(imported.OriginalBuses.Select(b => b.Capacity)),
                AfterBuses  = afterBuses.Count,
                AfterHist   = Histogram(afterBuses.Select(b => b.Capacity)),
                Solvable    = result.Analysis != null && result.Analysis.Solvable,
                Aps         = result.Analysis != null ? result.Analysis.ApsEstimate : 0f,
                Note        = result.Succeeded ? "" : (result.FailureReason ?? "no solvable arrangement"),
            };
            // Per-color exact-sum invariant check (round-to-5 leaves at most a remainder).
            row.SumMismatch = CheckPerColorSums(imported, afterBuses);
            return row;
        }

        // Reload-safe variant: processes ONE level and persists its row under
        // <outDir>/.rows/row_<N>.json so a later WriteReportFromRows call can
        // assemble the report even across domain reloads. Loads the profile itself.
        public static string ProcessLevelToDisk(string sourceDir, string outDir, BusBuddiesDifficultySettings settings, int n)
        {
            var profile = LoadProfileOrLog();
            if (profile == null) return "profile-not-found";
            string src = Path.Combine(sourceDir, "level_" + n + ".json");
            if (!File.Exists(src)) return "missing:" + src;

            Directory.CreateDirectory(outDir);
            var row = ProcessLevel(profile, src, outDir, settings);

            string rowsDir = Path.Combine(outDir, ".rows");
            Directory.CreateDirectory(rowsDir);
            File.WriteAllText(Path.Combine(rowsDir, "row_" + n + ".json"),
                JsonConvert.SerializeObject(row, Formatting.Indented));
            return row.ToString();
        }

        // Assemble RETWEAK-REPORT.md from all persisted row files (sorted by level).
        public static string WriteReportFromRows(string outDir)
        {
            string rowsDir = Path.Combine(outDir, ".rows");
            var rows = new List<ReTweakRow>();
            if (Directory.Exists(rowsDir))
                foreach (var f in Directory.GetFiles(rowsDir, "row_*.json"))
                    rows.Add(JsonConvert.DeserializeObject<ReTweakRow>(File.ReadAllText(f)));
            rows.Sort((a, b) => a.Level.CompareTo(b.Level));
            string path = WriteReport(rows, outDir);
            AssetDatabase.Refresh();
            return path;
        }

        // ── Report ─────────────────────────────────────────────────────────────
        public static string WriteReport(List<ReTweakRow> rows, string outDir)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Bus Buddies — Game Level Re-tweak Report");
            sb.AppendLine();
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}  ");
            sb.AppendLine("New difficulty knobs: BusesChunks=4, Deviation=0.2, Difficulty=1, " +
                          "NoSingleBusColor=true, RoundToFive=true, Columns=per-level (existing count).  ");
            sb.AppendLine("Picture (PixelColors) preserved identically — only the buses change. STAGING only.");
            sb.AppendLine();
            sb.AppendLine("| level | W×H | slots | BEFORE buses | BEFORE capacities | AFTER buses | AFTER capacities | solvable? | measured APS | notes |");
            sb.AppendLine("|------:|:---:|:-----:|:------------:|:------------------|:-----------:|:-----------------|:---------:|:------------:|:------|");
            foreach (var r in rows)
            {
                string notes = r.Note ?? "";
                if (r.SumMismatch) notes = (notes.Length > 0 ? notes + "; " : "") + "⚠ per-color sum mismatch";
                if (!r.Solvable && notes.Length == 0) notes = "⚠ not solvable";
                sb.AppendLine(
                    $"| {r.Level} | {r.Width}×{r.Height} | {r.Slots} | {r.BeforeBuses} | {r.BeforeHist} | " +
                    $"{r.AfterBuses} | {r.AfterHist} | {(r.Solvable ? "yes" : "**NO**")} | " +
                    $"{r.Aps:0.00} | {notes} |");
            }
            sb.AppendLine();
            int solvable = rows.Count(r => r.Solvable);
            sb.AppendLine($"Solvable: {solvable}/{rows.Count}. " +
                          "Any **NO** or ⚠ row needs a designer decision (colors/slots/difficulty).");

            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, "RETWEAK-REPORT.md");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        [Serializable]
        public sealed class ReTweakRow
        {
            public int Level, Width, Height, Slots;
            public int BeforeBuses; public string BeforeHist;
            public int AfterBuses;  public string AfterHist;
            public bool Solvable;   public float Aps;
            public bool SumMismatch;
            public string Note;

            public override string ToString()
                => $"level {Level} {Width}×{Height} slots {Slots} | before {BeforeBuses} [{BeforeHist}] " +
                   $"→ after {AfterBuses} [{AfterHist}] | solvable={Solvable} APS={Aps:0.00}" +
                   (SumMismatch ? " | SUM-MISMATCH" : "");
        }

        private static GameProfile LoadProfileOrLog()
        {
            var profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            if (profile == null) { Debug.LogError($"[BB Re-tweak] Profile not found at {ProfilePath}"); return null; }
            if (profile.LevelCompleter == null || profile.LevelAnalyzer == null)
            {
                Debug.LogError("[BB Re-tweak] Profile needs a LevelCompleter (autofiller) and a LevelAnalyzer wired.");
                return null;
            }
            return profile;
        }

        private static List<BusEntry> FlattenBuses(JObject topSection)
        {
            var list = new List<BusEntry>();
            if (topSection == null) return list;
            BusQueueData top;
            try { top = topSection.ToObject<BusQueueData>(); } catch { return list; }
            if (top?.Columns == null) return list;
            foreach (var col in top.Columns)
                if (col?.Buses != null) list.AddRange(col.Buses);
            return list;
        }

        // Compact ascending capacity histogram, e.g. "20×3, 25×4, 30×2".
        private static string Histogram(IEnumerable<int> capacities)
        {
            var counts = new SortedDictionary<int, int>();
            foreach (var c in capacities)
            {
                counts.TryGetValue(c, out var n);
                counts[c] = n + 1;
            }
            return counts.Count == 0
                ? "—"
                : string.Join(", ", counts.Select(kv => $"{kv.Key}×{kv.Value}"));
        }

        // Exact-sum-per-color invariant: total capacity per BUB ordinal must match
        // the original picture's pixel count per color. Returns true on ANY mismatch.
        private static bool CheckPerColorSums(BusBuddiesGameLevelImporter.ImportedLevel imported, List<BusEntry> afterBuses)
        {
            var before = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cell in imported.Document.Grid.Cells)
                if (cell is IColoredCell c && !string.IsNullOrEmpty(c.ColorId))
                {
                    before.TryGetValue(c.ColorId, out var n);
                    before[c.ColorId] = n + 1;
                }

            var after = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var b in afterBuses)
            {
                after.TryGetValue(b.ColorId ?? "", out var n);
                after[b.ColorId ?? ""] = n + b.Capacity;
            }

            if (before.Count != after.Count) return true;
            foreach (var kv in before)
                if (!after.TryGetValue(kv.Key, out var sum) || sum != kv.Value) return true;
            return false;
        }

        private static int ParseLevelNum(string path)
        {
            var m = Regex.Matches(Path.GetFileNameWithoutExtension(path), @"\d+");
            return m.Count > 0 ? int.Parse(m[m.Count - 1].Value) : 0;
        }

        private static string ResolveProjectRelative(string rel)
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel)).Replace('\\', '/');
    }
}
