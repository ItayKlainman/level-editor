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
    // Win-path DFS simulator for YarnTwist. Counts the number of distinct
    // tap-orderings of grid items (boxes / arrowboxes / tunnel queue entries)
    // that empty the grid and clear every spool without overflowing the
    // conveyor.
    //
    // State is hashed for memoization. The belt is modelled as a multiset of
    // colors (order on the belt is irrelevant for win-counting under the
    // continuous-circulation match rule).
    //
    // Task 4 supports basic boxes only. Arrow boxes and tunnels land in Tasks 5–6.
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Analysis/Yarn Twist Level Analyzer")]
    public sealed class YarnTwistLevelAnalyzer : LevelAnalyzerAsset
    {
        private const int Columns        = 4;
        private const int BallsPerSpool  = 3;
        private const int BallsPerItem   = 9;
        private const int DefaultCapacity = 24;

        public override LevelAnalysisResult Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req)
        {
            var sw = Stopwatch.StartNew();
            var result = new LevelAnalysisResult();

            if (doc == null || doc.Grid == null)
            {
                result.FailureReason = "document or grid is null";
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            int capacity = req?.ConveyorCapacityOverride ?? DefaultCapacity;
            int cap      = req?.WinPathCap     ?? 10_000;
            long timeout = req?.TimeoutMs      ?? 5_000;
            var  mode    = req?.Mode           ?? AnalysisMode.Count;

            // Materialise grid into a flat tappable-item list + parse top section.
            var items = BuildItems(doc.Grid);
            var columns = ParseTopSection(doc.TopSection);

            // Fast invariant check: every color present in items has at least one
            // spool of that color somewhere in the columns.
            if (!ColorCoverageOk(items, columns, out var missing))
            {
                result.FailureReason = $"color '{missing}' has no matching spools";
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            // Run DFS.
            var ctx = new SearchContext(items, columns, capacity, cap, timeout, mode, sw);
            ctx.Run();

            result.Solvable       = ctx.PathCount > 0;
            result.WinPathCount   = Math.Min(ctx.PathCount, cap);
            result.CountWasCapped = ctx.PathCount >= cap || ctx.TimedOut;
            result.StatesExplored = ctx.StatesExplored;
            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            if (!result.Solvable && string.IsNullOrEmpty(result.FailureReason))
                result.FailureReason = "no winning sequence exists";
            return result;
        }

        // ── Parsing ─────────────────────────────────────────────────────

        private struct Item
        {
            public ItemKind Kind;
            public string   ColorId;     // for Box / ArrowBox / single-queue-entry
            public int      PrereqIndex; // for ArrowBox: index into items[] of the box that must be tapped first; -1 if none
            public List<string> Queue;   // for Tunnel: queue of colors (in order). For Box/ArrowBox: null.
        }

        private enum ItemKind { Box, ArrowBox, Tunnel }

        private static List<Item> BuildItems(GridData<ICellData> grid)
        {
            // Task 4: only boxes. Arrow boxes and tunnels land in Tasks 5 and 6.
            var items = new List<Item>();
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width;  x++)
            {
                var cell = grid.Get(x, y);
                if (cell is YarnBoxCell b)
                    items.Add(new Item { Kind = ItemKind.Box, ColorId = b.ColorId, PrereqIndex = -1 });
            }
            return items;
        }

        // ── Top section ─────────────────────────────────────────────────

        private struct Column { public List<string> SpoolColors; }

        private static Column[] ParseTopSection(JObject topJson)
        {
            var cols = new Column[Columns];
            for (int i = 0; i < Columns; i++) cols[i] = new Column { SpoolColors = new List<string>() };
            if (topJson == null) return cols;
            var data = topJson.ToObject<YarnTopSectionData>();
            if (data?.Columns == null) return cols;
            for (int i = 0; i < Math.Min(data.Columns.Count, Columns); i++)
                foreach (var s in data.Columns[i].Spools)
                    cols[i].SpoolColors.Add(s.ColorId);
            return cols;
        }

        private static bool ColorCoverageOk(List<Item> items, Column[] cols, out string missing)
        {
            missing = null;
            var spoolColors = new HashSet<string>();
            foreach (var c in cols) foreach (var s in c.SpoolColors) spoolColors.Add(s);
            foreach (var it in items)
                if (!spoolColors.Contains(it.ColorId))
                {
                    missing = it.ColorId;
                    return false;
                }
            return true;
        }

        // ── DFS ─────────────────────────────────────────────────────────

        private sealed class SearchContext
        {
            private readonly List<Item> _items;
            private readonly Column[]   _cols;
            private readonly int        _capacity;
            private readonly int        _cap;
            private readonly long       _timeoutMs;
            private readonly AnalysisMode _mode;
            private readonly Stopwatch  _sw;

            // Mutable state.
            private readonly bool[] _tapped;          // per-item
            private readonly int[]  _spoolHead;       // [Columns]
            private readonly int[]  _spoolFill;       // [Columns]
            private readonly Dictionary<string, int> _bag = new Dictionary<string, int>();

            public long PathCount;
            public long StatesExplored;
            public bool TimedOut;

            public SearchContext(List<Item> items, Column[] cols, int capacity, int cap, long timeoutMs, AnalysisMode mode, Stopwatch sw)
            {
                _items = items; _cols = cols;
                _capacity = capacity; _cap = cap; _timeoutMs = timeoutMs;
                _mode = mode; _sw = sw;
                _tapped    = new bool[items.Count];
                _spoolHead = new int[Columns];
                _spoolFill = new int[Columns];
            }

            public void Run() => Dfs();

            private void Dfs()
            {
                if (TimedOut) return;
                if (_sw.ElapsedMilliseconds > _timeoutMs) { TimedOut = true; return; }
                StatesExplored++;

                int bagSum = 0;
                foreach (var v in _bag.Values) bagSum += v;
                if (bagSum > _capacity) return;

                bool allTapped = true;
                for (int i = 0; i < _items.Count; i++) if (!_tapped[i]) { allTapped = false; break; }
                bool allSpoolsCleared = true;
                for (int k = 0; k < Columns; k++)
                    if (_spoolHead[k] < _cols[k].SpoolColors.Count) { allSpoolsCleared = false; break; }

                if (allTapped && allSpoolsCleared && bagSum == 0)
                {
                    PathCount++;
                    return;
                }
                if (allTapped) return; // no producer left but state isn't winning

                for (int i = 0; i < _items.Count; i++)
                {
                    if (_tapped[i]) continue;
                    if (!IsTappable(i)) continue;
                    if (PathCount >= _cap) return;
                    if (_mode == AnalysisMode.Solvable && PathCount >= 1) return;

                    // Snapshot state.
                    var savedBag = new Dictionary<string, int>(_bag);
                    var savedHead = (int[])_spoolHead.Clone();
                    var savedFill = (int[])_spoolFill.Clone();

                    ApplyTap(i);
                    _tapped[i] = true;

                    Dfs();

                    // Restore.
                    _tapped[i] = false;
                    _bag.Clear(); foreach (var kv in savedBag) _bag[kv.Key] = kv.Value;
                    Array.Copy(savedHead, _spoolHead, Columns);
                    Array.Copy(savedFill, _spoolFill, Columns);

                    if (TimedOut) return;
                    if (PathCount >= _cap) return;
                    if (_mode == AnalysisMode.Solvable && PathCount >= 1) return;
                }
            }

            private bool IsTappable(int i)
            {
                // Task 4: only boxes. Arrow / tunnel land in Tasks 5–6.
                return _items[i].Kind == ItemKind.Box;
            }

            private void ApplyTap(int i)
            {
                var it = _items[i];
                AddToBag(it.ColorId, BallsPerItem);
                ResolveMatches();
            }

            private void AddToBag(string color, int count)
            {
                _bag.TryGetValue(color, out var n);
                _bag[color] = n + count;
            }

            private void ResolveMatches()
            {
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int k = 0; k < Columns; k++)
                    {
                        while (_spoolHead[k] < _cols[k].SpoolColors.Count)
                        {
                            var color = _cols[k].SpoolColors[_spoolHead[k]];
                            if (!_bag.TryGetValue(color, out var have) || have <= 0) break;
                            int need = BallsPerSpool - _spoolFill[k];
                            int take = Math.Min(need, have);
                            _spoolFill[k] += take;
                            _bag[color] = have - take;
                            changed = true;
                            if (_spoolFill[k] == BallsPerSpool)
                            {
                                _spoolHead[k]++;
                                _spoolFill[k] = 0;
                            }
                            else break;
                        }
                    }
                }
            }
        }
    }
}
