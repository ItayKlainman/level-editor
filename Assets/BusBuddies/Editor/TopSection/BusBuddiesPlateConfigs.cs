using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;

namespace Hoppa.BusBuddies.Editor
{
    // One rectangular "plate" cover placed over a region of the pixel grid. The plate
    // shows a number (Amount) = how many pixels the player must pick before it opens;
    // once opened, the covered pixels become interactable. Coordinates are 0-based,
    // x = column, y = row, BOTTOM-LEFT origin — the SAME editor grid space as
    // PixelColors (X,Y is the MIN / bottom-left corner; W,H is the size).
    public struct BusPlateConfig
    {
        public int X;       // min-corner column (0-based)
        public int Y;       // min-corner row    (0-based, bottom-up)
        public int W;       // width  in cells (>= 1)
        public int H;       // height in cells (>= 1)
        public int Amount;  // pixels to pick before the plate opens (>= 1)
    }

    // UI-agnostic storage + queries for Plates. Plates are non-cell document data kept
    // in LevelDocument.GameData["plateConfigs"] as a JArray of { x, y, w, h, amount }.
    // Mirrors the YarnPalettes / BusBuddiesSlotConfigs GameData-JArray helper style
    // (All/Write/Add/Remove/SetRect/SetAmount/clamp). Sizes clamp to >= 1 and amounts
    // to >= 1 on write.
    public static class BusBuddiesPlateConfigs
    {
        // Prefill amount for a freshly-dragged plate; amounts always clamp to >= 1.
        public const int DefaultAmount = 10;

        private const string Key = "plateConfigs";

        // Reads plates VERBATIM (no clamping) so validation can see raw hand-edited /
        // imported states (e.g. a size-0 or out-of-bounds plate). Clamping happens on
        // the mutation helpers (Add / SetRect / SetAmount) — mirrors BusBuddiesSlotConfigs.
        public static List<BusPlateConfig> All(LevelDocument doc)
        {
            var list = new List<BusPlateConfig>();
            if (doc?.GameData?[Key] is not JArray arr) return list;
            foreach (var t in arr)
            {
                if (t is not JObject o) continue;
                list.Add(new BusPlateConfig
                {
                    X      = o["x"]?.Value<int>() ?? 0,
                    Y      = o["y"]?.Value<int>() ?? 0,
                    W      = o["w"]?.Value<int>() ?? 1,
                    H      = o["h"]?.Value<int>() ?? 1,
                    Amount = o["amount"]?.Value<int>() ?? DefaultAmount,
                });
            }
            return list;
        }

        public static void Write(LevelDocument doc, List<BusPlateConfig> list)
        {
            doc.GameData ??= new JObject();
            var arr = new JArray();
            foreach (var p in list)
                arr.Add(new JObject
                {
                    ["x"]      = p.X,
                    ["y"]      = p.Y,
                    ["w"]      = p.W,
                    ["h"]      = p.H,
                    ["amount"] = p.Amount,
                });
            doc.GameData[Key] = arr;
        }

        // Appends a plate (size + amount clamped to >= 1). No placement validation here
        // (use CanPlace to gate the drag tool); this is the raw store operation.
        public static void Add(LevelDocument doc, int x, int y, int w, int h, int amount = DefaultAmount)
        {
            var list = All(doc);
            list.Add(new BusPlateConfig
            {
                X = x, Y = y,
                W = System.Math.Max(1, w),
                H = System.Math.Max(1, h),
                Amount = System.Math.Max(1, amount),
            });
            Write(doc, list);
        }

        // Removes the plate at list index `index` (no-op if out of range).
        public static void Remove(LevelDocument doc, int index)
        {
            var list = All(doc);
            if (index < 0 || index >= list.Count) return;
            list.RemoveAt(index);
            Write(doc, list);
        }

        // Updates the rectangle of the plate at `index` (size clamps to >= 1).
        public static void SetRect(LevelDocument doc, int index, int x, int y, int w, int h)
        {
            var list = All(doc);
            if (index < 0 || index >= list.Count) return;
            var p = list[index];
            p.X = x; p.Y = y;
            p.W = System.Math.Max(1, w);
            p.H = System.Math.Max(1, h);
            list[index] = p;
            Write(doc, list);
        }

        // Updates the pick-amount of the plate at `index` (clamps to >= 1).
        public static void SetAmount(LevelDocument doc, int index, int amount)
        {
            var list = All(doc);
            if (index < 0 || index >= list.Count) return;
            var p = list[index];
            p.Amount = System.Math.Max(1, amount);
            list[index] = p;
            Write(doc, list);
        }

        // True iff the plate's rectangle covers cell (x,y).
        public static bool Covers(BusPlateConfig p, int x, int y) =>
            x >= p.X && x < p.X + p.W && y >= p.Y && y < p.Y + p.H;

        // Every cell covered by a plate (may include out-of-bounds refs if the plate
        // extends past the grid; callers that care should bounds-check).
        public static IEnumerable<CellRef> CoveredCells(BusPlateConfig p)
        {
            int w = System.Math.Max(1, p.W);
            int h = System.Math.Max(1, p.H);
            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                yield return new CellRef(p.X + dx, p.Y + dy);
        }

        // The FIRST plate whose rectangle covers `cell`, if any.
        public static bool PlateAt(LevelDocument doc, CellRef cell, out BusPlateConfig plate)
        {
            foreach (var p in All(doc))
                if (Covers(p, cell.X, cell.Y)) { plate = p; return true; }
            plate = default;
            return false;
        }

        // Two axis-aligned rectangles overlap iff they intersect on both axes.
        public static bool RectsOverlap(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh) =>
            ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah;

        // May a new plate with this rectangle be placed? Fully in-bounds, size >= 1 on
        // both axes, and it must not overlap any existing plate.
        public static bool CanPlace(GridData<ICellData> grid, int x, int y, int w, int h,
            IEnumerable<BusPlateConfig> existing)
        {
            if (grid == null) return false;
            if (w < 1 || h < 1) return false;
            if (x < 0 || y < 0 || x + w > grid.Width || y + h > grid.Height) return false;

            if (existing != null)
                foreach (var e in existing)
                    if (RectsOverlap(x, y, w, h, e.X, e.Y, e.W, e.H))
                        return false;

            return true;
        }
    }
}
