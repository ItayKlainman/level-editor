using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Per-candidate stats a batch run writes alongside each LevelDocument. Generic
    // (game-agnostic) — a game's batch harness fills what it knows. Serialized as
    // <id>.stats.json next to <id>.json (and an optional <id>.png thumbnail).
    [Serializable]
    public sealed class LevelStats
    {
        public string id;
        public string status;        // analyzer AnalysisStatus name
        public bool   solvable;
        public float  aps;
        public int    band;
        public int    distinctColors;
        public string tier;       // difficulty tier name (curve runs); empty for legacy batches
        public float  targetAps;  // the tier's APS target
        public bool   offTarget;  // true = generation could not hit the tier's APS band
    }

    // One reviewable candidate in a staging folder: the level JSON plus optional
    // sidecar stats + thumbnail paths.
    public sealed class BatchCandidate
    {
        public string     Id;
        public string     JsonPath;
        public string     StatsPath;       // may be null
        public string     ThumbnailPath;   // may be null
        public LevelStats Stats;           // may be null
    }

    // Scan / import helpers for a batch staging folder. Pure file logic (no GUI) so
    // it is unit-testable; BatchReviewWindow is a thin shell over this.
    public static class BatchStaging
    {
        public const string StatsSuffix = ".stats.json";

        // Lists candidate levels in `folder`: every *.json that is not a *.stats.json,
        // paired with its sibling <id>.stats.json / <id>.png when present.
        public static List<BatchCandidate> Scan(string folder)
        {
            var list = new List<BatchCandidate>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return list;

            foreach (var path in Directory.GetFiles(folder, "*.json"))
            {
                if (path.EndsWith(StatsSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                string id = Path.GetFileNameWithoutExtension(path);
                string statsPath = Path.Combine(folder, id + StatsSuffix);
                string thumbPath = Path.Combine(folder, id + ".png");

                var cand = new BatchCandidate
                {
                    Id = id,
                    JsonPath = path,
                    StatsPath = File.Exists(statsPath) ? statsPath : null,
                    ThumbnailPath = File.Exists(thumbPath) ? thumbPath : null,
                };
                if (cand.StatsPath != null) cand.Stats = LoadStats(cand.StatsPath);
                list.Add(cand);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        public static LevelStats LoadStats(string statsPath)
        {
            try { return JsonConvert.DeserializeObject<LevelStats>(File.ReadAllText(statsPath)); }
            catch { return null; }
        }

        public static void WriteStats(string statsPath, LevelStats stats)
            => File.WriteAllText(statsPath, JsonConvert.SerializeObject(stats, Formatting.Indented));

        // Copies a candidate's level JSON into `targetFolder`, returning the
        // destination path. Creates the folder if needed. Sidecars are NOT copied
        // (the levels folder only needs the document).
        public static string Import(string jsonPath, string targetFolder, bool overwrite = true)
        {
            Directory.CreateDirectory(targetFolder);
            string dest = Path.Combine(targetFolder, Path.GetFileName(jsonPath));
            File.Copy(jsonPath, dest, overwrite);
            return dest;
        }
    }
}
