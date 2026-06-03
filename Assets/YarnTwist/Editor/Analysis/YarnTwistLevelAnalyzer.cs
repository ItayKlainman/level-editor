using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    // Two difficulty signals:
    //  • WinPathCount (Mode.Count) — exact perfect-information count via a
    //    memoized DFS. Precise but explodes combinatorially, so it caps.
    //  • WinRate (Mode.WinRate, or RolloutCount>0 under Count) — imperfect-
    //    information Monte-Carlo: the fraction of myopic-player playouts that
    //    win. Never caps, scales to any grid size, and — because the simulated
    //    player can't see hidden spools until they reach a column head — it
    //    actually reflects the hidden-spool ratio.
    //
    // The hot path is allocation-free: colors are interned to small integer
    // indices, the belt is a fixed-size int[] multiset, and per-node state is
    // snapshotted onto the stack and restored by undo (no per-node Dictionary
    // or array allocation, no List/sort in the hash).
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

            int capacity   = req?.ConveyorCapacityOverride ?? DefaultCapacity;
            int cap        = req?.WinPathCap     ?? 10_000;
            long timeout   = req?.TimeoutMs      ?? 5_000;
            var  mode      = req?.Mode           ?? AnalysisMode.Count;
            int rollouts   = req?.RolloutCount   ?? 0;
            int lookahead  = Math.Max(0, req?.PlayerLookahead ?? 4);

            // Materialise grid into a flat tappable-item list + parse top section.
            var items   = BuildItems(doc.Grid);
            var columns = ParseTopSection(doc.TopSection);

            // Fast invariant check: every color present in items has at least one
            // spool of that color somewhere in the columns.
            if (!ColorCoverageOk(items, columns, out var missing))
            {
                result.FailureReason = $"color '{missing}' has no matching spools";
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            // Build cell-access maps so the DFS can model the unlock mechanic:
            // only the top row is accessible initially; tapping a box unlocks its
            // 4 orthogonal neighbours.
            int gW = doc.Grid.Width, gH = doc.Grid.Height;
            var gridItemIdx  = new int[gW * gH];
            var gridCellOpen = new bool[gW * gH]; // true = permanently empty (not wall)
            for (int k = 0; k < gridItemIdx.Length; k++) gridItemIdx[k] = -1;
            for (int j = 0; j < items.Count; j++)
                gridItemIdx[items[j].Y * gW + items[j].X] = j;
            for (int cy = 0; cy < gH; cy++)
            for (int cx = 0; cx < gW; cx++)
            {
                var cell = doc.Grid.Get(cx, cy);
                if (!(cell is YarnWallCell) && gridItemIdx[cy * gW + cx] < 0)
                    gridCellOpen[cy * gW + cx] = true;
            }

            // Intern colors → small int indices over the union of every color
            // that can enter the belt or sit on a spool.
            var model = Intern(items, columns, gH, gW, gridItemIdx, gridCellOpen);

            if (req != null && req.RecordSolution)
            {
                // First-solution search: record one concrete winning tap-ordering.
                var rctx = new SearchContext(model, capacity, cap, timeout, AnalysisMode.Solvable, sw, record: true);
                rctx.Run();
                result.Solvable       = rctx.Solution != null;
                result.StatesExplored = rctx.StatesExplored;
                if (rctx.Solution != null)
                    result.SolutionSteps = FormatSolution(model, rctx.Solution);
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                if (!result.Solvable && string.IsNullOrEmpty(result.FailureReason))
                    result.FailureReason = "no winning sequence exists";
                return result;
            }

            if (mode == AnalysisMode.WinRate)
            {
                // Imperfect-information only — skip the exhaustive DFS.
                int k = rollouts > 0 ? rollouts : 200;
                var rc = new RolloutContext(model, capacity, lookahead);
                double wr = rc.Run(k);
                result.WinRate       = wr;
                result.RolloutsRun   = k;
                result.Solvable      = wr > 0.0; // a winning playout proves solvability; the converse is not guaranteed
                result.WinPathCount  = 0;
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                if (!result.Solvable && string.IsNullOrEmpty(result.FailureReason))
                    result.FailureReason = "no playout won (may still be solvable)";
                return result;
            }

            // Exhaustive DFS (Count / Solvable).
            var ctx = new SearchContext(model, capacity, cap, timeout, mode, sw);
            ctx.Run();

            result.Solvable       = ctx.PathCount > 0;
            result.WinPathCount   = Math.Min(ctx.PathCount, cap);
            result.CountWasCapped = ctx.PathCount >= cap || ctx.TimedOut;
            result.StatesExplored = ctx.StatesExplored;

            // Optionally layer the imperfect-information signal on top of the
            // exact count in a single call.
            if (rollouts > 0)
            {
                var rc = new RolloutContext(model, capacity, lookahead);
                result.WinRate     = rc.Run(rollouts);
                result.RolloutsRun = rollouts;
            }

            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            if (!result.Solvable && string.IsNullOrEmpty(result.FailureReason))
                result.FailureReason = "no winning sequence exists";
            return result;
        }

        // ── Parsing ─────────────────────────────────────────────────────

        private struct Item
        {
            public ItemKind  Kind;
            public string    ColorId;      // for Box / ArrowBox
            public int       PrereqIndex;  // for ArrowBox: index into items[] of the box that must be tapped first; -1 if none
            public int       PartnerIndex; // for connected Box: index of the reciprocally-linked box; -1 if none
            public List<string> Queue;     // for Tunnel
            public int       X, Y;         // grid coordinates (for solution display)
        }

        private enum ItemKind { Box, ArrowBox, Tunnel }

        private static List<Item> BuildItems(GridData<ICellData> grid)
        {
            // Pass 1: collect (x, y) → tappable index for boxes and arrow boxes.
            var items = new List<Item>();
            var idxAt = new Dictionary<long, int>(); // y * width + x → items index

            int W = grid.Width;
            long Key(int x, int y) => (long)y * W + x;

            // Scan top-to-bottom so item indices match the player's visual row order
            // (row 0 from the player = grid top = data y = Height-1).
            for (int y = grid.Height - 1; y >= 0; y--)
            for (int x = 0; x < W;                x++)
            {
                var cell = grid.Get(x, y);
                if (cell is YarnBoxCell b)
                {
                    idxAt[Key(x, y)] = items.Count;
                    items.Add(new Item { Kind = ItemKind.Box, ColorId = b.ColorId, PrereqIndex = -1, PartnerIndex = -1, X = x, Y = y });
                }
                else if (cell is YarnArrowBoxCell ab)
                {
                    idxAt[Key(x, y)] = items.Count;
                    items.Add(new Item { Kind = ItemKind.ArrowBox, ColorId = ab.ColorId, PrereqIndex = -1, PartnerIndex = -1, X = x, Y = y });
                }
                else if (cell is YarnTunnelCell t && t.Queue != null && t.Queue.Count > 0)
                {
                    idxAt[Key(x, y)] = items.Count;
                    items.Add(new Item {
                        Kind = ItemKind.Tunnel,
                        ColorId = null,
                        PrereqIndex = -1,
                        PartnerIndex = -1,
                        Queue = new List<string>(t.Queue),
                        X = x, Y = y,
                    });
                }
            }

            // Pass 2: resolve arrow-box prerequisites by direction → neighbor item.
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < W;          x++)
            {
                if (grid.Get(x, y) is YarnArrowBoxCell ab)
                {
                    int selfIdx = idxAt[Key(x, y)]; // present from Pass 1
                    var (nx, ny) = NeighborOf(x, y, ab.Direction);
                    if (grid.InBounds(nx, ny) && idxAt.TryGetValue(Key(nx, ny), out var nIdx))
                    {
                        var it = items[selfIdx];
                        it.PrereqIndex = nIdx;
                        items[selfIdx] = it;
                    }
                    // else: arrow's neighbor isn't a tappable item — arrow is
                    // permanently unreachable; PrereqIndex stays -1.
                }
            }

            // Pass 3: resolve connected-box partners. Honoured only when the link is
            // reciprocal (the neighbour is a box that points back) — a dangling or
            // non-reciprocal ConnectedDir is treated as an independent box, matching
            // YarnConnectedBoxRule's contract.
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < W;          x++)
            {
                if (grid.Get(x, y) is not YarnBoxCell box || !box.ConnectedDir.HasValue) continue;
                var (nx, ny) = NeighborOf(x, y, box.ConnectedDir.Value);
                if (!grid.InBounds(nx, ny)) continue;
                if (grid.Get(nx, ny) is not YarnBoxCell partner || !partner.ConnectedDir.HasValue) continue;
                var (bx, by) = NeighborOf(nx, ny, partner.ConnectedDir.Value);
                if (bx != x || by != y) continue; // partner doesn't point back → not reciprocal
                if (!idxAt.TryGetValue(Key(nx, ny), out var nIdx)) continue;

                int selfIdx = idxAt[Key(x, y)];
                var it = items[selfIdx];
                it.PartnerIndex = nIdx;
                items[selfIdx] = it;
            }

            return items;
        }

        private static (int x, int y) NeighborOf(int x, int y, YarnDirection d) => d switch
        {
            YarnDirection.Up    => (x, y + 1),
            YarnDirection.Down  => (x, y - 1),
            YarnDirection.Left  => (x - 1, y),
            YarnDirection.Right => (x + 1, y),
            _                   => (x, y),
        };

        // ── Top section ─────────────────────────────────────────────────

        private struct Column
        {
            public List<string> SpoolColors;
            public List<bool>   Hidden;
            // Connected spool partner pointers, parallel to SpoolColors.
            // PartnerCol[s] >= 0 means spool s is locked until spoolHead[PartnerCol[s]]
            // reaches PartnerPos[s]; -1 = unconnected (or an incomplete/corrupt pair,
            // which is degraded to independent — mirrors the connected-box analyzer).
            public List<int>    PartnerCol;
            public List<int>    PartnerPos;
        }

        private static Column[] ParseTopSection(JObject topJson)
        {
            var cols = new Column[Columns];
            for (int i = 0; i < Columns; i++)
                cols[i] = new Column {
                    SpoolColors = new List<string>(), Hidden = new List<bool>(),
                    PartnerCol  = new List<int>(),     PartnerPos = new List<int>(),
                };
            if (topJson == null) return cols;
            var data = topJson.ToObject<YarnTopSectionData>();
            if (data?.Columns == null) return cols;

            // id → member (col, pos) positions, to resolve reciprocal partner pointers.
            var members = new Dictionary<int, List<(int col, int pos)>>();
            for (int i = 0; i < Math.Min(data.Columns.Count, Columns); i++)
                foreach (var s in data.Columns[i].Spools)
                {
                    int pos = cols[i].SpoolColors.Count;
                    cols[i].SpoolColors.Add(s.ColorId);
                    cols[i].Hidden.Add(s.Hidden);
                    cols[i].PartnerCol.Add(-1);
                    cols[i].PartnerPos.Add(-1);
                    if (s.ConnectionId.HasValue)
                    {
                        if (!members.TryGetValue(s.ConnectionId.Value, out var list))
                            members[s.ConnectionId.Value] = list = new List<(int, int)>();
                        list.Add((i, pos));
                    }
                }

            // Only a complete pair (exactly two members) in different columns links;
            // anything else stays unconnected so a half-finished pair never affects play.
            foreach (var kv in members)
            {
                if (kv.Value.Count != 2) continue;
                var a = kv.Value[0];
                var b = kv.Value[1];
                if (a.col == b.col) continue;
                cols[a.col].PartnerCol[a.pos] = b.col; cols[a.col].PartnerPos[a.pos] = b.pos;
                cols[b.col].PartnerCol[b.pos] = a.col; cols[b.col].PartnerPos[b.pos] = a.pos;
            }
            return cols;
        }

        private static bool ColorCoverageOk(List<Item> items, Column[] cols, out string missing)
        {
            missing = null;
            var spoolColors = new HashSet<string>();
            foreach (var c in cols) foreach (var s in c.SpoolColors) spoolColors.Add(s);
            foreach (var it in items)
            {
                if (it.Kind == ItemKind.Tunnel)
                {
                    foreach (var q in it.Queue)
                        if (!spoolColors.Contains(q)) { missing = q; return false; }
                }
                else
                {
                    if (!spoolColors.Contains(it.ColorId)) { missing = it.ColorId; return false; }
                }
            }
            return true;
        }

        // ── Interned model (shared by DFS + rollouts) ───────────────────

        // Immutable, allocation-free representation passed to both search
        // strategies. All colors are small int indices.
        private sealed class Model
        {
            public int        ItemCount;
            public ItemKind[]  Kind;
            public int[]       Color;     // Box/ArrowBox color index; -1 for tunnel
            public int[]       Prereq;    // ArrowBox prereq item index; -1 otherwise
            public int[]       Partner;   // connected-box reciprocal partner item index; -1 otherwise
            public int[][]     Queue;     // Tunnel queue (interned); null otherwise
            public int[]       X, Y;      // grid coordinates per item (data space, y=0=bottom)
            public int         GridHeight; // grid rows — used to flip Y for player-facing display
            public int         GridWidth;
            public int[]       GridItemIdx;  // flat [y*W+x] → item index, or -1 if no tappable item
            public bool[]      GridCellOpen; // flat [y*W+x] → true if permanently empty (not wall)
            public int[][]     ColSpool;  // [Columns][] interned spool colors, head→back
            public bool[][]    ColHidden; // [Columns][] hidden flags, head→back
            public int[][]     ColPartnerCol; // [Columns][] connected-spool partner column; -1 if none
            public int[][]     ColPartnerPos; // [Columns][] connected-spool partner data index; -1 if none
            public int         NumColors;
            public string[]    ColorNames; // index → original ColorId (for solution display)
            public ulong       StructHash; // deterministic seed source for rollouts
        }

        private static Model Intern(List<Item> items, Column[] cols,
            int gridHeight = 0, int gridWidth = 0,
            int[] gridItemIdx = null, bool[] gridCellOpen = null)
        {
            var map = new Dictionary<string, int>();
            int Id(string c)
            {
                if (string.IsNullOrEmpty(c)) return -1;
                if (!map.TryGetValue(c, out var idx)) { idx = map.Count; map[c] = idx; }
                return idx;
            }

            // Intern spool colors first so column arrays are valid.
            var colSpool      = new int[Columns][];
            var colHidden     = new bool[Columns][];
            var colPartnerCol = new int[Columns][];
            var colPartnerPos = new int[Columns][];
            for (int k = 0; k < Columns; k++)
            {
                int n = cols[k].SpoolColors.Count;
                colSpool[k]      = new int[n];
                colHidden[k]     = new bool[n];
                colPartnerCol[k] = new int[n];
                colPartnerPos[k] = new int[n];
                for (int s = 0; s < n; s++)
                {
                    colSpool[k][s]      = Id(cols[k].SpoolColors[s]);
                    colHidden[k][s]     = cols[k].Hidden[s];
                    colPartnerCol[k][s] = cols[k].PartnerCol[s];
                    colPartnerPos[k][s] = cols[k].PartnerPos[s];
                }
            }

            int m = items.Count;
            var kind    = new ItemKind[m];
            var color   = new int[m];
            var prereq  = new int[m];
            var partner = new int[m];
            var queue   = new int[m][];
            var xs      = new int[m];
            var ys      = new int[m];
            for (int i = 0; i < m; i++)
            {
                kind[i]    = items[i].Kind;
                prereq[i]  = items[i].PrereqIndex;
                partner[i] = items[i].PartnerIndex;
                xs[i]      = items[i].X;
                ys[i]      = items[i].Y;
                if (items[i].Kind == ItemKind.Tunnel)
                {
                    color[i] = -1;
                    var q = items[i].Queue;
                    var arr = new int[q.Count];
                    for (int j = 0; j < q.Count; j++) arr[j] = Id(q[j]);
                    queue[i] = arr;
                }
                else
                {
                    color[i] = Id(items[i].ColorId);
                    queue[i] = null;
                }
            }

            // Reverse the intern map so solution steps can show original color names.
            var names = new string[map.Count];
            foreach (var kv in map) names[kv.Value] = kv.Key;

            var model = new Model
            {
                ItemCount = m,
                Kind = kind, Color = color, Prereq = prereq, Partner = partner, Queue = queue,
                X = xs, Y = ys,
                GridHeight = gridHeight, GridWidth = gridWidth,
                GridItemIdx = gridItemIdx, GridCellOpen = gridCellOpen,
                ColSpool = colSpool, ColHidden = colHidden,
                ColPartnerCol = colPartnerCol, ColPartnerPos = colPartnerPos,
                NumColors = map.Count,
                ColorNames = names,
            };
            model.StructHash = ComputeStructHash(model);
            return model;
        }

        private static ulong ComputeStructHash(Model md)
        {
            ulong h = 1469598103934665603UL; const ulong P = 1099511628211UL;
            for (int i = 0; i < md.ItemCount; i++)
            {
                h = (h ^ (ulong)(int)md.Kind[i]) * P;
                h = (h ^ (ulong)(md.Color[i] + 2)) * P;
                h = (h ^ (ulong)(md.Prereq[i] + 2)) * P;
                h = (h ^ (ulong)(md.Partner[i] + 2)) * P;
                if (md.Queue[i] != null)
                    foreach (var q in md.Queue[i]) h = (h ^ (ulong)(q + 2)) * P;
            }
            for (int k = 0; k < Columns; k++)
                for (int s = 0; s < md.ColSpool[k].Length; s++)
                {
                    h = (h ^ (ulong)(md.ColSpool[k][s] + 2)) * P;
                    h = (h ^ (ulong)(md.ColHidden[k][s] ? 1u : 0u)) * P;
                    h = (h ^ (ulong)(md.ColPartnerCol[k][s] + 2)) * P;
                    h = (h ^ (ulong)(md.ColPartnerPos[k][s] + 2)) * P;
                }
            return h;
        }

        // ── Solution formatting ─────────────────────────────────────────

        // Turns a recorded tap sequence (item indices) into human-readable
        // tap-order lines. Tunnel queue positions are reconstructed by a
        // per-tunnel counter since each tunnel can be tapped multiple times.
        private static List<string> FormatSolution(Model md, List<int> path)
        {
            var steps = new List<string>(path.Count);
            var tunnelPos = new Dictionary<int, int>();
            for (int s = 0; s < path.Count; s++)
            {
                int i = path[s];
                int n = s + 1;
                // Flip Y so (x,0) = top row from the player's perspective
                // (data y=0 is the bottom row; player sees data y=Height-1 first).
                int displayY = md.GridHeight > 0 ? md.GridHeight - 1 - md.Y[i] : md.Y[i];
                if (md.Kind[i] == ItemKind.Tunnel)
                {
                    tunnelPos.TryGetValue(i, out var q);
                    int len = md.Queue[i].Length;
                    string color = md.ColorNames[md.Queue[i][q]];
                    steps.Add($"{n}. Tap Tunnel ({md.X[i]},{displayY}) → {color} ({q + 1}/{len})");
                    tunnelPos[i] = q + 1;
                }
                else
                {
                    string kind  = md.Kind[i] == ItemKind.ArrowBox ? "Arrow-box" : "Box";
                    string color = md.ColorNames[md.Color[i]];
                    steps.Add($"{n}. Tap {color} {kind} ({md.X[i]},{displayY})");
                }
            }
            return steps;
        }

        // ── Exhaustive DFS ──────────────────────────────────────────────

        private sealed class SearchContext
        {
            private readonly Model        _md;
            private readonly int          _capacity;
            private readonly int          _cap;
            private readonly long         _timeoutMs;
            private readonly AnalysisMode _mode;
            private readonly Stopwatch    _sw;

            // Mutable state — fixed-size, no per-node allocation.
            private readonly bool[] _tapped;
            private readonly int[]  _queueIdx;
            private readonly int[]  _spoolHead;   // [Columns]
            private readonly int[]  _spoolFill;   // [Columns]
            private readonly int[]  _bag;         // [NumColors]
            private int             _bagSum;
            private readonly Dictionary<ulong, long> _memo = new Dictionary<ulong, long>();

            // Solution recording (first winning tap-ordering).
            private readonly bool   _record;
            private readonly List<int> _path;       // live tap stack while recording
            private readonly int[]  _recordScratch; // scratch buffer for demand-sorted ordering (overwritten per node)
            private bool            _stop;          // set once a solution is captured

            public long PathCount;
            public long StatesExplored;
            public bool TimedOut;
            public List<int> Solution;              // captured item-index sequence, or null

            public SearchContext(Model md, int capacity, int cap, long timeoutMs, AnalysisMode mode, Stopwatch sw, bool record = false)
            {
                _md = md;
                _capacity = capacity; _cap = cap; _timeoutMs = timeoutMs;
                _mode = mode; _sw = sw;
                _record = record;
                _path         = record ? new List<int>(md.ItemCount) : null;
                _recordScratch = record ? new int[md.ItemCount]       : null;
                _tapped    = new bool[md.ItemCount];
                _queueIdx  = new int[md.ItemCount];
                _spoolHead = new int[Columns];
                _spoolFill = new int[Columns];
                _bag       = new int[md.NumColors];
            }

            public void Run() => PathCount = DfsMemo();

            private long DfsMemo()
            {
                if (_stop || TimedOut) return 0;
                if (_sw.ElapsedMilliseconds > _timeoutMs) { TimedOut = true; return 0; }
                StatesExplored++;

                if (_bagSum > _capacity) return 0;

                bool allConsumed = AllConsumed();
                bool allCleared  = AllSpoolsCleared();
                if (allConsumed && allCleared && _bagSum == 0)
                {
                    if (_record && Solution == null) { Solution = new List<int>(_path); _stop = true; }
                    return 1;
                }
                if (allConsumed) return 0;

                // Memoisation is bypassed while recording: a cached count would
                // return without re-walking the path to a winning leaf.
                ulong stateKey = 0;
                if (!_record)
                {
                    stateKey = HashState();
                    if (_memo.TryGetValue(stateKey, out var cached)) return cached;
                }

                long total = 0;

                // Snapshot mutable state once per node (small fixed-size copies).
                Span<int> savedBag  = stackalloc int[_md.NumColors];
                for (int c = 0; c < _md.NumColors; c++) savedBag[c] = _bag[c];
                Span<int> savedHead = stackalloc int[Columns];
                Span<int> savedFill = stackalloc int[Columns];
                for (int k = 0; k < Columns; k++) { savedHead[k] = _spoolHead[k]; savedFill[k] = _spoolFill[k]; }
                int savedBagSum = _bagSum;

                if (_record)
                {
                    // Recording mode: explore items in demand-score order (highest first)
                    // so the captured solution prioritises taps that match current column
                    // heads, producing a natural, easy-to-follow sequence.
                    //
                    // Collect tappable items into _recordScratch, sort, then snapshot into
                    // a stack-local span so recursive DfsMemo calls cannot corrupt the order.
                    int nc = 0;
                    for (int i = 0; i < _md.ItemCount; i++)
                        if (IsTappable(i)) _recordScratch[nc++] = i;

                    // Insertion sort by demand score (descending). n ≤ itemCount (~50).
                    for (int a = 1; a < nc; a++)
                    {
                        int key = _recordScratch[a];
                        int keyD = DemandForItem(key);
                        int b = a - 1;
                        while (b >= 0 && DemandForItem(_recordScratch[b]) < keyD)
                        { _recordScratch[b + 1] = _recordScratch[b]; b--; }
                        _recordScratch[b + 1] = key;
                    }

                    // Snapshot to stack: recursive calls overwrite _recordScratch.
                    Span<int> order = nc > 0 ? stackalloc int[nc] : Span<int>.Empty;
                    for (int j = 0; j < nc; j++) order[j] = _recordScratch[j];

                    for (int j = 0; j < nc; j++)
                    {
                        if (_mode == AnalysisMode.Solvable && total >= 1) break;
                        int i = order[j];

                        bool savedTapped  = _tapped[i];
                        int  savedQueue   = _queueIdx[i];
                        int  partner      = _md.Partner[i];
                        bool savedPartner = partner >= 0 && _tapped[partner];

                        _path.Add(i);
                        ApplyTap(i);
                        long sub = DfsMemo();
                        total = Math.Min(_cap, total + sub);

                        for (int c = 0; c < _md.NumColors; c++) _bag[c] = savedBag[c];
                        for (int k = 0; k < Columns; k++) { _spoolHead[k] = savedHead[k]; _spoolFill[k] = savedFill[k]; }
                        _bagSum = savedBagSum;
                        _tapped[i] = savedTapped;
                        if (partner >= 0) _tapped[partner] = savedPartner;
                        _queueIdx[i] = savedQueue;
                        _path.RemoveAt(_path.Count - 1);

                        if (_stop || TimedOut) break;
                    }
                }
                else
                {
                    for (int i = 0; i < _md.ItemCount; i++)
                    {
                        if (!IsTappable(i)) continue;
                        if (total >= _cap) break;
                        if (_mode == AnalysisMode.Solvable && total >= 1) break;

                        bool savedTapped  = _tapped[i];
                        int  savedQueue   = _queueIdx[i];
                        int  partner      = _md.Partner[i];
                        bool savedPartner = partner >= 0 && _tapped[partner];

                        ApplyTap(i);
                        long sub = DfsMemo();
                        total = Math.Min(_cap, total + sub);

                        for (int c = 0; c < _md.NumColors; c++) _bag[c] = savedBag[c];
                        for (int k = 0; k < Columns; k++) { _spoolHead[k] = savedHead[k]; _spoolFill[k] = savedFill[k]; }
                        _bagSum      = savedBagSum;
                        _tapped[i]   = savedTapped;
                        if (partner >= 0) _tapped[partner] = savedPartner;
                        _queueIdx[i] = savedQueue;

                        if (_stop || TimedOut) break;
                    }
                }

                if (!_record) _memo[stateKey] = total;
                return total;
            }

            // Compact 64-bit state hash — alloc-free. Colors are interned ints,
            // so the belt is hashed in index order with no List/sort. Still not
            // a perfect hash (collisions possible but rare); acceptable for the
            // small puzzles we care about.
            private ulong HashState()
            {
                ulong h = 1469598103934665603UL; const ulong P = 1099511628211UL;
                for (int i = 0; i < _md.ItemCount; i++)
                {
                    int v = _md.Kind[i] == ItemKind.Tunnel ? _queueIdx[i] : (_tapped[i] ? 1 : 0);
                    h = (h ^ (ulong)v) * P;
                }
                for (int k = 0; k < Columns; k++)
                {
                    h = (h ^ (ulong)_spoolHead[k]) * P;
                    h = (h ^ (ulong)_spoolFill[k]) * P;
                }
                for (int c = 0; c < _md.NumColors; c++)
                    if (_bag[c] != 0)
                    {
                        h = (h ^ (ulong)c) * P;
                        h = (h ^ (ulong)_bag[c]) * P;
                    }
                return h;
            }

            private bool AllConsumed()
            {
                for (int i = 0; i < _md.ItemCount; i++)
                {
                    if (_md.Kind[i] == ItemKind.Tunnel)
                    { if (_queueIdx[i] < _md.Queue[i].Length) return false; }
                    else if (!_tapped[i]) return false;
                }
                return true;
            }

            private bool AllSpoolsCleared()
            {
                for (int k = 0; k < Columns; k++)
                    if (_spoolHead[k] < _md.ColSpool[k].Length) return false;
                return true;
            }

            private bool IsTappable(int i)
            {
                switch (_md.Kind[i])
                {
                    case ItemKind.Box:
                        if (_tapped[i]) return false;
                        // Only the canonical (lower-index) half of a connected pair is a
                        // move; tapping it co-activates the partner, so offering the partner
                        // too would double-count and split the search.
                        if (_md.Partner[i] >= 0 && _md.Partner[i] < i) return false;
                        return IsAccessible(i);
                    case ItemKind.ArrowBox:
                        return !_tapped[i] && IsAccessible(i)
                            && _md.Prereq[i] >= 0 && _tapped[_md.Prereq[i]];
                    case ItemKind.Tunnel:
                        return _queueIdx[i] < _md.Queue[i].Length && IsAccessible(i);
                    default: return false;
                }
            }

            // ── Unlock / accessibility ──────────────────────────────────────
            // A cell is accessible if it is in the top row (data y = GridHeight-1,
            // closest to the belt) OR at least one orthogonal neighbour is "open":
            // either permanently empty or already cleared by the player.

            private bool IsAccessible(int i)
            {
                if (_md.GridItemIdx == null) return true; // no grid map (tests with tiny grids)
                int x = _md.X[i], y = _md.Y[i];
                if (y == _md.GridHeight - 1) return true;
                return IsNeighbourOpen(x + 1, y) || IsNeighbourOpen(x - 1, y)
                    || IsNeighbourOpen(x, y + 1) || IsNeighbourOpen(x, y - 1);
            }

            private bool IsNeighbourOpen(int nx, int ny)
            {
                if ((uint)nx >= (uint)_md.GridWidth || (uint)ny >= (uint)_md.GridHeight) return false;
                int flat = ny * _md.GridWidth + nx;
                int idx  = _md.GridItemIdx[flat];
                return idx >= 0 ? IsCellCleared(idx) : _md.GridCellOpen[flat];
            }

            private bool IsCellCleared(int idx)
                => _md.Kind[idx] == ItemKind.Tunnel ? _queueIdx[idx] > 0 : _tapped[idx];

            // ── Demand scoring for recording mode ──────────────────────────
            // Scores how many belt-balls an item would feed toward visible column
            // needs. Higher score → tap earlier for a natural, followable solution.
            private const int SolutionLookahead = 2;

            private int DemandForItem(int i)
            {
                int c = _md.Kind[i] == ItemKind.Tunnel
                    ? _md.Queue[i][_queueIdx[i]]
                    : _md.Color[i];
                int score = DemandForColor(c);
                int p = _md.Partner[i];
                if (p >= 0) score += DemandForColor(_md.Color[p]);
                return score;
            }

            private int DemandForColor(int c)
            {
                int score = 0;
                for (int k = 0; k < Columns; k++)
                {
                    int len = _md.ColSpool[k].Length;
                    for (int off = 0; off <= SolutionLookahead; off++)
                    {
                        int pos = _spoolHead[k] + off;
                        if (pos >= len) break;
                        if (off > 0 && _md.ColHidden[k][pos]) break;
                        if (_md.ColSpool[k][pos] == c)
                        {
                            score += SolutionLookahead - off + 1; // head=3, 1-deep=2, 2-deep=1
                            break; // only first match per column counts
                        }
                    }
                }
                return score;
            }

            private void ApplyTap(int i)
            {
                if (_md.Kind[i] == ItemKind.Tunnel)
                {
                    int color = _md.Queue[i][_queueIdx[i]];
                    _queueIdx[i]++;
                    _bag[color] += BallsPerItem; _bagSum += BallsPerItem;
                }
                else
                {
                    _tapped[i] = true;
                    _bag[_md.Color[i]] += BallsPerItem; _bagSum += BallsPerItem;
                    int p = _md.Partner[i];
                    if (p >= 0 && !_tapped[p])
                    {
                        // Connected pair: tapping one activates both — both release their
                        // balls and clear together (then a single ResolveMatches below).
                        _tapped[p] = true;
                        _bag[_md.Color[p]] += BallsPerItem; _bagSum += BallsPerItem;
                    }
                }
                ResolveMatches(_md, _bag, ref _bagSum, _spoolHead, _spoolFill);
            }
        }

        // ── Shared match resolution ─────────────────────────────────────

        // Greedy continuous-circulation match: each column consumes balls of its
        // current head color until the head spool fills (then advances) or runs
        // out. Repeats until no column can make progress. Maintains bagSum.
        private static void ResolveMatches(Model md, int[] bag, ref int bagSum, int[] spoolHead, int[] spoolFill)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int k = 0; k < Columns; k++)
                {
                    while (spoolHead[k] < md.ColSpool[k].Length)
                    {
                        int head = spoolHead[k];

                        // Connected spool: locked until its partner column's head
                        // reaches the partner spool's position. spoolHead is monotonic,
                        // so once the partner has reached (>=) its position the chain
                        // stays broken — the lock is a pure, latching function of
                        // spoolHead, which keeps memoisation correct and alloc-free.
                        int pc = md.ColPartnerCol[k][head];
                        if (pc >= 0 && spoolHead[pc] < md.ColPartnerPos[k][head])
                            break;

                        int color = md.ColSpool[k][head];
                        int have  = bag[color];
                        if (have <= 0) break;
                        int need = BallsPerSpool - spoolFill[k];
                        int take = Math.Min(need, have);
                        spoolFill[k] += take;
                        bag[color]   = have - take;
                        bagSum      -= take;
                        changed = true;
                        if (spoolFill[k] == BallsPerSpool) { spoolHead[k]++; spoolFill[k] = 0; }
                        else break;
                    }
                }
            }
        }

        // ── Monte-Carlo rollouts (imperfect information) ─────────────────

        // Simulates a myopic player who taps grid items to feed the conveyor.
        // The player can see each column's head spool (always) plus contiguous
        // non-hidden spools within a lookahead window; a hidden spool is unknown
        // until it reaches the head (covered-until-head) and blocks vision of
        // everything behind it. The player feeds whichever tappable color is most
        // wanted by the visible spools; when nothing visible wants any available
        // color it must guess. More hidden spools ⇒ more guesses ⇒ lower win rate.
        private sealed class RolloutContext
        {
            private readonly Model _md;
            private readonly int   _capacity;
            private readonly int   _lookahead;

            // Reusable per-rollout state.
            private readonly bool[] _tapped;
            private readonly int[]  _queueIdx;
            private readonly int[]  _spoolHead;
            private readonly int[]  _spoolFill;
            private readonly int[]  _bag;
            private readonly double[] _demand;
            private readonly int[]  _candidates;
            private int             _bagSum;

            public RolloutContext(Model md, int capacity, int lookahead)
            {
                _md = md; _capacity = capacity; _lookahead = lookahead;
                _tapped    = new bool[md.ItemCount];
                _queueIdx  = new int[md.ItemCount];
                _spoolHead = new int[Columns];
                _spoolFill = new int[Columns];
                _bag       = new int[md.NumColors];
                _demand    = new double[md.NumColors];
                _candidates = new int[md.ItemCount];
            }

            public double Run(int rollouts)
            {
                if (rollouts <= 0) return 0.0;
                int wins = 0;
                for (int k = 0; k < rollouts; k++)
                {
                    // Deterministic per (level, rollout) so WinRate is reproducible.
                    var rng = new System.Random(unchecked((int)(_md.StructHash ^ (ulong)(k * 2654435761u))));
                    if (Playout(rng)) wins++;
                }
                return (double)wins / rollouts;
            }

            private bool Playout(System.Random rng)
            {
                Array.Clear(_tapped, 0, _tapped.Length);
                Array.Clear(_queueIdx, 0, _queueIdx.Length);
                Array.Clear(_spoolHead, 0, Columns);
                Array.Clear(_spoolFill, 0, Columns);
                Array.Clear(_bag, 0, _bag.Length);
                _bagSum = 0;

                // Hard ceiling on taps: every item is consumed at most once
                // (queue entries count individually). Prevents any pathological loop.
                int maxTaps = _md.ItemCount;
                for (int i = 0; i < _md.ItemCount; i++)
                    if (_md.Kind[i] == ItemKind.Tunnel) maxTaps += _md.Queue[i].Length - 1;

                for (int step = 0; step <= maxTaps + 1; step++)
                {
                    if (_bagSum > _capacity) return false;

                    bool allConsumed = AllConsumed();
                    bool allCleared  = AllSpoolsCleared();
                    if (allConsumed && allCleared && _bagSum == 0) return true;

                    // Gather tappable items.
                    int nc = 0;
                    for (int i = 0; i < _md.ItemCount; i++)
                        if (IsTappable(i)) _candidates[nc++] = i;
                    if (nc == 0) return false; // deadlock — not won

                    int choice = ChooseTap(rng, nc);
                    ApplyTap(choice);
                }
                return false;
            }

            private int ChooseTap(System.Random rng, int nc)
            {
                // Build visible demand per color.
                for (int c = 0; c < _md.NumColors; c++) _demand[c] = 0.0;
                for (int k = 0; k < Columns; k++)
                {
                    int len = _md.ColSpool[k].Length;
                    for (int off = 0; off <= _lookahead; off++)
                    {
                        int pos = _spoolHead[k] + off;
                        if (pos >= len) break;
                        // Covered-until-head: the head (off==0) is always visible;
                        // a hidden spool deeper in is unknown and blocks vision behind it.
                        if (off > 0 && _md.ColHidden[k][pos]) break;
                        _demand[_md.ColSpool[k][pos]] += (_lookahead - off + 1);
                    }
                }

                // Pick the tappable item whose feed color is most wanted.
                double best = -1.0;
                int bestCount = 0;
                for (int j = 0; j < nc; j++)
                {
                    int i = _candidates[j];
                    double d = FeedDemand(i);
                    if (d > best) { best = d; bestCount = 1; }
                    else if (d == best) bestCount++;
                }

                if (best <= 0.0)
                {
                    // Nothing visible wants any available color — blind guess.
                    return _candidates[rng.Next(nc)];
                }

                // Random tie-break among the argmax.
                int pick = rng.Next(bestCount);
                for (int j = 0; j < nc; j++)
                {
                    int i = _candidates[j];
                    if (FeedDemand(i) == best && pick-- == 0) return i;
                }
                return _candidates[0]; // unreachable
            }

            // Visible demand a tap would feed: its own head/box color plus, for a connected
            // box, its partner's color — the pair releases both colors in one tap.
            private double FeedDemand(int i)
            {
                int col = _md.Kind[i] == ItemKind.Tunnel ? _md.Queue[i][_queueIdx[i]] : _md.Color[i];
                double d = _demand[col];
                int p = _md.Partner[i];
                if (p >= 0) d += _demand[_md.Color[p]];
                return d;
            }

            private bool AllConsumed()
            {
                for (int i = 0; i < _md.ItemCount; i++)
                {
                    if (_md.Kind[i] == ItemKind.Tunnel)
                    { if (_queueIdx[i] < _md.Queue[i].Length) return false; }
                    else if (!_tapped[i]) return false;
                }
                return true;
            }

            private bool AllSpoolsCleared()
            {
                for (int k = 0; k < Columns; k++)
                    if (_spoolHead[k] < _md.ColSpool[k].Length) return false;
                return true;
            }

            private bool IsTappable(int i)
            {
                switch (_md.Kind[i])
                {
                    case ItemKind.Box:
                        if (_tapped[i]) return false;
                        if (_md.Partner[i] >= 0 && _md.Partner[i] < i) return false;
                        return IsAccessible(i);
                    case ItemKind.ArrowBox:
                        return !_tapped[i] && IsAccessible(i)
                            && _md.Prereq[i] >= 0 && _tapped[_md.Prereq[i]];
                    case ItemKind.Tunnel:
                        return _queueIdx[i] < _md.Queue[i].Length && IsAccessible(i);
                    default: return false;
                }
            }

            private bool IsAccessible(int i)
            {
                if (_md.GridItemIdx == null) return true;
                int x = _md.X[i], y = _md.Y[i];
                if (y == _md.GridHeight - 1) return true;
                return IsNeighbourOpen(x + 1, y) || IsNeighbourOpen(x - 1, y)
                    || IsNeighbourOpen(x, y + 1) || IsNeighbourOpen(x, y - 1);
            }

            private bool IsNeighbourOpen(int nx, int ny)
            {
                if ((uint)nx >= (uint)_md.GridWidth || (uint)ny >= (uint)_md.GridHeight) return false;
                int flat = ny * _md.GridWidth + nx;
                int idx  = _md.GridItemIdx[flat];
                return idx >= 0 ? (_md.Kind[idx] == ItemKind.Tunnel ? _queueIdx[idx] > 0 : _tapped[idx])
                                : _md.GridCellOpen[flat];
            }

            private void ApplyTap(int i)
            {
                if (_md.Kind[i] == ItemKind.Tunnel)
                {
                    int color = _md.Queue[i][_queueIdx[i]];
                    _queueIdx[i]++;
                    _bag[color] += BallsPerItem; _bagSum += BallsPerItem;
                }
                else
                {
                    _tapped[i] = true;
                    _bag[_md.Color[i]] += BallsPerItem; _bagSum += BallsPerItem;
                    int p = _md.Partner[i];
                    if (p >= 0 && !_tapped[p])
                    {
                        // Connected pair: tapping one activates both — both release their
                        // balls and clear together (then a single ResolveMatches below).
                        _tapped[p] = true;
                        _bag[_md.Color[p]] += BallsPerItem; _bagSum += BallsPerItem;
                    }
                }
                ResolveMatches(_md, _bag, ref _bagSum, _spoolHead, _spoolFill);
            }
        }
    }
}
