using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;

namespace Hoppa.BusBuddies.Sim
{
    // Immutable, interned, engine-agnostic snapshot of a Bus Buddies level for the
    // simulator / solver / average-player. Plain C# (no UnityEngine) so it is
    // headless-unit-testable.
    //
    // Unlike YAK there is NO gravity, so the grid is kept as a FULL 2D occupancy
    // (int[W*H], -1 = empty) using the y*W+x, row-0-bottom origin convention.
    // Colours are interned to small int indices.
    public sealed class BusLevelModel
    {
        public readonly int W;
        public readonly int H;
        public readonly int ActiveSlots;     // max buses in the Active Row (default 5)
        public readonly int NumColors;
        public readonly string[] ColorNames; // colour index -> original colorId

        public readonly int[] Grid;          // [y*W+x] -> colour index, -1 = empty
        public readonly int   TotalBlocks;   // count of non-empty cells

        // Per-cell plate requirement: [y*W+x] -> pixels that must be picked (globally)
        // before the plate over this cell opens; 0 = not under a plate. A covered cell is
        // unpickable until the global picked-count reaches this value, then it uncovers.
        public readonly int[] PlateReq;

        public readonly int      Columns;        // number of queue columns
        public readonly int[][]  BusColor;       // [col] -> colour index, head->back
        public readonly int[][]  BusCap;         // [col] -> capacity,     head->back
        public readonly bool[][] BusHidden;      // [col] -> hidden flag,  head->back
        public readonly int[][]  BusConnected;   // [col] -> connectedId (-1 none), head->back
        public readonly int      TotalPassengers;
        public readonly int      TotalBuses;

        private BusLevelModel(
            int w, int h, int activeSlots, int numColors, string[] colorNames,
            int[] grid, int totalBlocks,
            int columns, int[][] busColor, int[][] busCap, bool[][] busHidden, int[][] busConnected,
            int totalPassengers, int totalBuses, int[] plateReq)
        {
            W = w; H = h; ActiveSlots = activeSlots; NumColors = numColors; ColorNames = colorNames;
            Grid = grid; TotalBlocks = totalBlocks;
            Columns = columns; BusColor = busColor; BusCap = busCap; BusHidden = busHidden; BusConnected = busConnected;
            TotalPassengers = totalPassengers; TotalBuses = totalBuses;
            PlateReq = (plateReq != null && plateReq.Length == grid.Length) ? plateReq : new int[grid.Length];
        }

        // Build from the editor's working grid + parsed queue data. Cells are read
        // through IColoredCell (not a hardcoded BBPixelCell); anything that is not an
        // IColoredCell with a non-empty ColorId (e.g. BBEmptyCell) is "no block" (-1).
        public static BusLevelModel Build(GridData<ICellData> grid, BusQueueData queue, int activeSlots)
            => Build(grid, queue, activeSlots, null);

        // Overload accepting a per-cell plate requirement map ([y*W+x] -> pixels to pick
        // before opening; 0 = uncovered). Null = no plates.
        public static BusLevelModel Build(GridData<ICellData> grid, BusQueueData queue, int activeSlots, int[] plateReq)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            int Intern(string c)
            {
                if (!map.TryGetValue(c, out var idx)) { idx = map.Count; map[c] = idx; }
                return idx;
            }

            int w = grid?.Width ?? 0;
            int h = grid?.Height ?? 0;
            var gridArr = new int[w * h];
            int totalBlocks = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                var cell = grid.Get(x, y);
                if (cell is IColoredCell col && !string.IsNullOrEmpty(col.ColorId))
                {
                    gridArr[i] = Intern(col.ColorId);
                    totalBlocks++;
                }
                else
                {
                    gridArr[i] = -1;
                }
            }

            int columns = queue?.Columns?.Count ?? 0;
            var busColor = new int[columns][];
            var busCap = new int[columns][];
            var busHidden = new bool[columns][];
            var busConnected = new int[columns][];
            int totalPassengers = 0, totalBuses = 0;
            for (int c = 0; c < columns; c++)
            {
                var buses = queue.Columns[c]?.Buses ?? new List<BusEntry>();
                int nb = buses.Count;
                busColor[c] = new int[nb];
                busCap[c] = new int[nb];
                busHidden[c] = new bool[nb];
                busConnected[c] = new int[nb];
                for (int k = 0; k < nb; k++)
                {
                    var e = buses[k];
                    busColor[c][k] = Intern(e?.ColorId ?? string.Empty);
                    busCap[c][k] = e?.Capacity ?? 0;
                    busHidden[c][k] = e?.Hidden ?? false;
                    busConnected[c][k] = e?.ConnectedId ?? -1;
                    totalPassengers += busCap[c][k];
                    totalBuses++;
                }
            }

            var names = new string[map.Count];
            foreach (var kv in map) names[kv.Value] = kv.Key;

            return new BusLevelModel(
                w, h, activeSlots, map.Count, names,
                gridArr, totalBlocks,
                columns, busColor, busCap, busHidden, busConnected,
                totalPassengers, totalBuses, plateReq);
        }

        // Test/utility factory: build directly from a flat grid + per-column bus
        // arrays. Hidden defaults all-false, connected all -1.
        public static BusLevelModel FromArrays(int[] grid, int w, int h,
            int[][] busColors, int[][] busCaps, int activeSlots, int numColors, string[] colorNames,
            int[] plateReq = null)
        {
            grid ??= Array.Empty<int>();
            busColors ??= Array.Empty<int[]>();
            busCaps ??= Array.Empty<int[]>();

            int totalBlocks = 0;
            for (int i = 0; i < grid.Length; i++) if (grid[i] >= 0) totalBlocks++;

            int columns = busColors.Length;
            var busHidden = new bool[columns][];
            var busConnected = new int[columns][];
            int totalPassengers = 0, totalBuses = 0;
            for (int c = 0; c < columns; c++)
            {
                int nb = busColors[c]?.Length ?? 0;
                busHidden[c] = new bool[nb];
                busConnected[c] = new int[nb];
                for (int k = 0; k < nb; k++)
                {
                    busConnected[c][k] = -1;
                    totalPassengers += busCaps[c]?[k] ?? 0;
                    totalBuses++;
                }
            }

            return new BusLevelModel(
                w, h, activeSlots, numColors, colorNames,
                grid, totalBlocks,
                columns, busColors, busCaps, busHidden, busConnected,
                totalPassengers, totalBuses, plateReq);
        }

        // Balance precheck: a level is solvable ONLY IF every colour's block count
        // equals the total capacity of buses of that colour (sound, cheap). A
        // mismatch proves Unsolvable. Connected/hidden treated as known in 1a.
        public bool IsColorBalanced()
        {
            var blocks = new int[NumColors];
            for (int i = 0; i < Grid.Length; i++)
            {
                int c = Grid[i];
                if (c >= 0 && c < NumColors) blocks[c]++;
            }
            var cap = new int[NumColors];
            for (int col = 0; col < Columns; col++)
                for (int k = 0; k < BusColor[col].Length; k++)
                {
                    int c = BusColor[col][k];
                    if (c >= 0 && c < NumColors) cap[c] += BusCap[col][k];
                }
            for (int i = 0; i < NumColors; i++) if (blocks[i] != cap[i]) return false;
            return true;
        }
    }
}
