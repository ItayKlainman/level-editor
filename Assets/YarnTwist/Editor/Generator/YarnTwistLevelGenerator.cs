using System;
using System.Collections.Generic;
using System.Linq;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // YarnTwist v1 generator — Option A from the design (layout-first, derive
    // spools from grid totals).
    //
    // The hard constraint is YarnColorBalanceRule: for every color, total balls
    // from grid (boxes + arrowboxes + tunnel queue entries) × 9 must equal total
    // capacity from spools × 3. Since every grid item contributes 9 balls = 3
    // spools, balance is exact by construction when we count grid items per
    // color and emit (count × 3) spools of that color. No remainder math needed.
    //
    // Per-candidate validity is still gated by profile.Rules via
    // LevelGeneratorRunner — that covers arrow-target and tunnel-output rules
    // which are local to placement decisions, not totals.
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Generator/Yarn Twist Level Generator")]
    public sealed class YarnTwistLevelGenerator : LevelGeneratorAsset
    {
        private const int TopSectionColumns = 4; // YarnTopSectionPanel.Columns

        public override LevelGeneratorResult Generate(LevelGeneratorRequest request, GameProfile profile)
        {
            var sw     = System.Diagnostics.Stopwatch.StartNew();
            var result = new LevelGeneratorResult();

            var config = request?.AdvancedConfig as YarnTwistGeneratorConfig;
            if (config == null)
            {
                result.Document = null;
                result.Succeeded = false;
                result.RuleRejectCounts["__no_config__"] = 1;
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }
            if (profile == null || profile.ColorPalette == null)
            {
                result.Document = null;
                result.Succeeded = false;
                result.RuleRejectCounts["__no_profile_or_palette__"] = 1;
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            int rootSeed = request.Seed != 0
                ? request.Seed
                : new System.Random().Next(1, int.MaxValue);
            result.SeedUsed = rootSeed;

            int difficulty  = Mathf.Clamp(request.Difficulty, 1, 10);
            int maxAttempts = Mathf.Max(1, config.MaxRerollAttempts);

            LevelDocument lastCandidate = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                int subSeed = unchecked(rootSeed * 1664525 + attempt * 1013904223 + 1);
                var rng     = new System.Random(subSeed);

                var doc = BuildCandidate(profile, config, difficulty, rng);
                result.CandidatesTried++;
                lastCandidate = doc;

                var eval = LevelGeneratorRunner.Evaluate(doc, profile);
                foreach (var kv in eval.ErrorsByRule)
                {
                    result.RuleRejectCounts.TryGetValue(kv.Key, out var n);
                    result.RuleRejectCounts[kv.Key] = n + kv.Value;
                }

                if (!eval.HasErrors)
                {
                    result.Document  = doc;
                    result.Succeeded = true;
                    break;
                }
            }

            if (result.Document == null)
            {
                result.Document  = lastCandidate;
                result.Succeeded = false;
            }

            // Metadata + game data on the chosen document.
            if (result.Document != null)
            {
                if (result.Document.Metadata == null)
                    result.Document.Metadata = new LevelMetadata();
                if (request.TargetAPS.HasValue)
                    result.Document.Metadata.Aps = request.TargetAPS.Value;
                result.Document.Metadata.ModifiedAt = DateTime.UtcNow.ToString("o");

                if (result.Document.GameData == null)
                    result.Document.GameData = new JObject();
                result.Document.GameData["coinReward"] =
                    Mathf.RoundToInt(config.CoinReward.Evaluate(difficulty));
            }

            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            return result;
        }

        // ── Candidate construction ────────────────────────────────────────

        private static LevelDocument BuildCandidate(
            GameProfile profile,
            YarnTwistGeneratorConfig config,
            int difficulty,
            System.Random rng)
        {
            int width  = config.GridWidthOverride > 0
                ? config.GridWidthOverride
                : Mathf.Max(2, Mathf.RoundToInt(config.GridWidth.Evaluate(difficulty)));
            int height = config.GridHeightOverride > 0
                ? config.GridHeightOverride
                : Mathf.Max(2, Mathf.RoundToInt(config.GridHeight.Evaluate(difficulty)));

            int colorCount  = config.ColorCountOverride > 0
                ? config.ColorCountOverride
                : Mathf.RoundToInt(config.ColorCount.Evaluate(difficulty));
            float wallDens  = Mathf.Clamp01(config.WallDensity.Evaluate(difficulty)  * 0.01f);
            float boxRatio  = Mathf.Clamp01(config.BoxRatio.Evaluate(difficulty)     * 0.01f);
            float arrowRat  = Mathf.Clamp01(config.ArrowBoxRatio.Evaluate(difficulty) * 0.01f);
            int   tunnelN   = Mathf.Max(0, Mathf.RoundToInt(config.TunnelCount.Evaluate(difficulty)));
            float hiddenRat = Mathf.Clamp01(config.HiddenSpoolRatio.Evaluate(difficulty) * 0.01f);

            var palette = profile.ColorPalette.ColorIds.ToList();
            if (palette.Count == 0)
                palette.Add("pink");
            colorCount = Mathf.Clamp(colorCount, 1, palette.Count);
            var colors = palette.Take(colorCount).ToList();

            // ── Grid: empty ──
            var grid = new GridData<ICellData>(width, height);
            for (int i = 0; i < grid.Cells.Length; i++)
                grid.Cells[i] = new YarnEmptyCell();

            int total = width * height;

            // Random visit order.
            var order = Enumerable.Range(0, total).ToList();
            ShuffleInPlace(order, rng);
            int cursor = 0;

            // ── Walls ──
            int wallCount = Mathf.RoundToInt(total * wallDens);
            for (int placed = 0; placed < wallCount && cursor < order.Count; placed++)
            {
                int idx = order[cursor++];
                grid.Set(idx % width, idx / width, new YarnWallCell());
            }

            // ── Tunnels ──
            // Each tunnel picks a non-wall cell + a direction whose neighbor is
            // in-grid and not a wall; the neighbor is forced empty (and tracked
            // so later passes don't overwrite it).
            var forcedEmpty = new HashSet<int>();
            int tunnelsPlaced = 0;
            while (tunnelsPlaced < tunnelN && cursor < order.Count)
            {
                int idx = order[cursor++];
                int x = idx % width, y = idx / width;
                var cell = grid.Get(x, y);
                if (cell is YarnWallCell) continue;
                if (forcedEmpty.Contains(idx)) continue;

                var dirs = new[]
                {
                    YarnDirection.Up, YarnDirection.Down,
                    YarnDirection.Left, YarnDirection.Right,
                };
                ShuffleInPlace(dirs, rng);

                YarnDirection? chosen = null;
                int chosenNIdx = -1;
                foreach (var d in dirs)
                {
                    var (tx, ty) = NeighborOf(x, y, d);
                    if (!grid.InBounds(tx, ty)) continue;
                    if (grid.Get(tx, ty) is YarnWallCell) continue;
                    chosen = d;
                    chosenNIdx = ty * width + tx;
                    break;
                }
                if (chosen == null) continue;

                int queueLen = 1 + rng.Next(0, Mathf.Max(1, config.MaxTunnelQueueLength));
                var queue    = new List<string>(queueLen);
                for (int q = 0; q < queueLen; q++)
                    queue.Add(colors[rng.Next(colors.Count)]);

                grid.Set(x, y, new YarnTunnelCell
                {
                    OutputDirection = chosen.Value,
                    Queue           = queue,
                });
                int nx = chosenNIdx % width, ny = chosenNIdx / width;
                grid.Set(nx, ny, new YarnEmptyCell());
                forcedEmpty.Add(chosenNIdx);
                tunnelsPlaced++;
            }

            // ── Boxes / arrowboxes / empties (over remaining cells) ──
            for (int idx = 0; idx < total; idx++)
            {
                int x = idx % width, y = idx / width;
                var cell = grid.Get(x, y);
                if (cell is YarnWallCell) continue;
                if (cell is YarnTunnelCell) continue;
                if (forcedEmpty.Contains(idx)) continue;

                if (rng.NextDouble() >= boxRatio) continue; // stays empty

                string colorId = colors[rng.Next(colors.Count)];
                if (rng.NextDouble() < arrowRat)
                {
                    grid.Set(x, y, new YarnArrowBoxCell
                    {
                        ColorId   = colorId,
                        Direction = (YarnDirection)rng.Next(4),
                    });
                }
                else
                {
                    grid.Set(x, y, new YarnBoxCell { ColorId = colorId });
                }
            }

            // ── Arrow direction repair ──
            // YarnArrowBoxTargetRule rejects arrows that point off-grid or into
            // a wall. Try the other 3 directions; if none works, demote to box.
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (grid.Get(x, y) is not YarnArrowBoxCell arrow) continue;
                if (IsValidArrowTarget(grid, x, y, arrow.Direction)) continue;

                bool repaired = false;
                foreach (var d in new[] { YarnDirection.Up, YarnDirection.Down,
                                          YarnDirection.Left, YarnDirection.Right })
                {
                    if (IsValidArrowTarget(grid, x, y, d))
                    {
                        arrow.Direction = d;
                        repaired = true;
                        break;
                    }
                }
                if (!repaired)
                    grid.Set(x, y, new YarnBoxCell { ColorId = arrow.ColorId });
            }

            // ── Spool distribution from grid totals ──
            var perColor = new Dictionary<string, int>();
            foreach (var cell in grid.Cells)
            {
                switch (cell)
                {
                    case YarnBoxCell b:      Bump(perColor, b.ColorId, 1); break;
                    case YarnArrowBoxCell a: Bump(perColor, a.ColorId, 1); break;
                    case YarnTunnelCell t:
                        foreach (var q in t.Queue) Bump(perColor, q, 1);
                        break;
                }
            }

            // Each grid item (box/arrowbox/queue entry) = 9 balls = 3 spools.
            var spools = new List<YarnSpoolData>();
            foreach (var kv in perColor)
            {
                int n = kv.Value * 3;
                for (int i = 0; i < n; i++)
                    spools.Add(new YarnSpoolData { ColorId = kv.Key });
            }

            // Hidden ratio applied to the full list before column distribution.
            int hiddenN = Mathf.RoundToInt(spools.Count * hiddenRat);
            var hiddenIdxs = new HashSet<int>();
            var pickOrder  = Enumerable.Range(0, spools.Count).ToList();
            ShuffleInPlace(pickOrder, rng);
            for (int i = 0; i < hiddenN && i < pickOrder.Count; i++)
                hiddenIdxs.Add(pickOrder[i]);
            for (int i = 0; i < spools.Count; i++)
                spools[i].Hidden = hiddenIdxs.Contains(i);

            // Shuffle + round-robin distribute across 4 columns.
            ShuffleInPlace(spools, rng);
            var top = new YarnTopSectionData();
            for (int i = 0; i < TopSectionColumns; i++)
                top.Columns.Add(new YarnSpoolColumn());
            for (int i = 0; i < spools.Count; i++)
                top.Columns[i % TopSectionColumns].Spools.Add(spools[i]);

            // ── Document ──
            var nowIso = DateTime.UtcNow.ToString("o");
            return new LevelDocument
            {
                SchemaVersion = profile.SchemaId + ".v1",
                LevelId       = "generated_" + DateTime.UtcNow.ToString("yyMMdd_HHmmss_fff"),
                DisplayName   = $"Generated · D{difficulty}",
                Metadata = new LevelMetadata
                {
                    Author     = Environment.UserName,
                    CreatedAt  = nowIso,
                    ModifiedAt = nowIso,
                },
                Grid       = grid,
                TopSection = JObject.FromObject(top),
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static (int x, int y) NeighborOf(int x, int y, YarnDirection d) => d switch
        {
            YarnDirection.Up    => (x, y + 1),
            YarnDirection.Down  => (x, y - 1),
            YarnDirection.Left  => (x - 1, y),
            YarnDirection.Right => (x + 1, y),
            _                   => (x, y),
        };

        private static bool IsValidArrowTarget(GridData<ICellData> grid, int x, int y, YarnDirection d)
        {
            var (tx, ty) = NeighborOf(x, y, d);
            if (!grid.InBounds(tx, ty)) return false;
            return grid.Get(tx, ty) is not YarnWallCell;
        }

        private static void Bump(Dictionary<string, int> d, string key, int amount)
        {
            if (string.IsNullOrEmpty(key)) return;
            d.TryGetValue(key, out var v);
            d[key] = v + amount;
        }

        private static void ShuffleInPlace<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j   = rng.Next(0, i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
