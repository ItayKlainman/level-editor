using System.Collections.Generic;
using Hoppa.LevelEditor.Core;

namespace Hoppa.YAK.Sim
{
    // Immutable, interned, engine-agnostic snapshot of a YAK level for the
    // simulator / solver / average-player. Plain C# (no UnityEngine) so it is
    // fast and headless-unit-testable.
    //
    // Colors are interned to small int indices. The grid is reduced to its
    // essential structure: because YAK consumption is bottom-row-only and
    // gravity pulls a column straight down, each of the W grid columns is just a
    // FIXED bottom→top sequence of wool colors — the order tiles get exposed is
    // independent of play. Empty cells are simply omitted from a column's
    // sequence (a hole closes under gravity), so GridCols[c][0] is always the
    // first tile that column will expose.
    public sealed class YakLevelModel
    {
        public readonly int Width;            // number of grid columns
        public readonly int ConveyorSlots;    // max active spools on the belt at once
        public readonly int NumColors;
        public readonly string[] ColorNames;  // color index -> original colorId

        public readonly int[][] GridCols;     // [gridColumn] -> wool colors, bottom→top
        public readonly int     TotalWool;    // sum of all GridCols lengths

        public readonly int      Columns;     // number of spool columns
        public readonly int[][]  SpoolColor;  // [spoolColumn] -> colors,  head→back
        public readonly int[][]  SpoolCap;    // [spoolColumn] -> capacity, head→back
        public readonly bool[][] SpoolHidden; // [spoolColumn] -> hidden,   head→back
        public readonly int      TotalSpools;

        private YakLevelModel(
            int width, int conveyorSlots, int numColors, string[] colorNames,
            int[][] gridCols, int totalWool,
            int columns, int[][] spoolColor, int[][] spoolCap, bool[][] spoolHidden, int totalSpools)
        {
            Width = width; ConveyorSlots = conveyorSlots; NumColors = numColors; ColorNames = colorNames;
            GridCols = gridCols; TotalWool = totalWool;
            Columns = columns; SpoolColor = spoolColor; SpoolCap = spoolCap; SpoolHidden = spoolHidden;
            TotalSpools = totalSpools;
        }

        // Build from the editor's working document grid + parsed spool data.
        // Cells are read through IColoredCell (not a hardcoded YAKWoolCell) so a
        // future colored cell type slots in without touching the simulator;
        // anything that isn't an IColoredCell with a non-empty ColorId (e.g.
        // YAKEmptyCell) is treated as "no tile" and omitted.
        public static YakLevelModel Build(GridData<ICellData> grid, YAKTopSectionData top, int conveyorSlots)
        {
            var map = new Dictionary<string, int>(System.StringComparer.Ordinal);
            int Intern(string c)
            {
                if (!map.TryGetValue(c, out var idx)) { idx = map.Count; map[c] = idx; }
                return idx;
            }

            int w = grid?.Width ?? 0;
            int h = grid?.Height ?? 0;

            // Grid columns: bottom (y=0) → top, omitting empties.
            var gridCols = new int[w][];
            int totalWool = 0;
            for (int x = 0; x < w; x++)
            {
                var seq = new List<int>(h);
                for (int y = 0; y < h; y++)
                {
                    var cell = grid.Get(x, y);
                    if (cell is IColoredCell col && !string.IsNullOrEmpty(col.ColorId))
                        seq.Add(Intern(col.ColorId));
                }
                gridCols[x] = seq.ToArray();
                totalWool += seq.Count;
            }

            // Spool columns: head (index 0) → back.
            int columns = top?.Columns?.Count ?? 0;
            var spoolColor  = new int[columns][];
            var spoolCap    = new int[columns][];
            var spoolHidden = new bool[columns][];
            int totalSpools = 0;
            for (int c = 0; c < columns; c++)
            {
                var spools = top.Columns[c]?.Spools ?? new List<YAKSpoolEntry>();
                int n = spools.Count;
                spoolColor[c]  = new int[n];
                spoolCap[c]    = new int[n];
                spoolHidden[c] = new bool[n];
                for (int s = 0; s < n; s++)
                {
                    var e = spools[s];
                    spoolColor[c][s]  = Intern(e?.ColorId ?? string.Empty);
                    spoolCap[c][s]    = e?.Capacity ?? 0;
                    spoolHidden[c][s] = e?.Hidden ?? false;
                }
                totalSpools += n;
            }

            var names = new string[map.Count];
            foreach (var kv in map) names[kv.Value] = kv.Key;

            return new YakLevelModel(
                w, conveyorSlots, map.Count, names,
                gridCols, totalWool,
                columns, spoolColor, spoolCap, spoolHidden, totalSpools);
        }

        // Test/utility factory: build a model directly from column arrays (no
        // GridData/top round-trip). gridCols[x] = wool colors bottom→top for grid
        // column x; spoolColors[c]/spoolCaps[c] = colors/capacities head→back for
        // spool column c. Hidden defaults to all-false. Mirrors the fields Build sets.
        public static YakLevelModel FromArrays(int[][] gridCols, int[][] spoolColors, int[][] spoolCaps,
                                               int conveyorSlots, int numColors, string[] colorNames)
        {
            gridCols    ??= System.Array.Empty<int[]>();
            spoolColors ??= System.Array.Empty<int[]>();
            spoolCaps   ??= System.Array.Empty<int[]>();

            int totalWool = 0;
            foreach (var col in gridCols) totalWool += col?.Length ?? 0;

            int columns = spoolColors.Length;
            var spoolHidden = new bool[columns][];
            int totalSpools = 0;
            for (int c = 0; c < columns; c++)
            {
                int n = spoolColors[c]?.Length ?? 0;
                spoolHidden[c] = new bool[n];
                totalSpools += n;
            }

            return new YakLevelModel(
                gridCols.Length, conveyorSlots, numColors, colorNames,
                gridCols, totalWool,
                columns, spoolColors, spoolCaps, spoolHidden, totalSpools);
        }
    }
}
