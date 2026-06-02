using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;

namespace Hoppa.YarnTwist.Editor
{
    // One 3x3 Palette cover, anchored on a center cell, with a requirement amount.
    // Covered cells are always the 3x3 around the center (never stored explicitly).
    public struct YarnPalette
    {
        public int CenterX;
        public int CenterY;
        public int Amount;

        public CellRef Center => new CellRef(CenterX, CenterY);
    }

    // UI-agnostic storage + queries for Palettes. Palettes are non-cell document data
    // kept in LevelDocument.GameData["palettes"]; a palette has no id (palettes can't
    // overlap, so the center is its identity) and no stored covered list (always the
    // 3x3 around the center). Mirrors the YarnSpoolConnection helper style.
    public static class YarnPalettes
    {
        public const int DefaultAmount = 5;
        private const string Key = "palettes";

        // A cell counts as a "box" for palette coverage if it is a (connected or plain)
        // box or an arrow box. Empty / wall / tunnel are not boxes.
        public static bool IsBox(ICellData cell) =>
            cell is YarnBoxCell || cell is YarnArrowBoxCell;

        public static List<YarnPalette> All(LevelDocument doc)
        {
            var list = new List<YarnPalette>();
            if (doc?.GameData?[Key] is not JArray arr) return list;
            foreach (var t in arr)
            {
                if (t is not JObject o) continue;
                var c = o["center"] as JObject;
                if (c == null) continue;
                list.Add(new YarnPalette
                {
                    CenterX = c["x"]?.Value<int>() ?? 0,
                    CenterY = c["y"]?.Value<int>() ?? 0,
                    Amount  = o["amount"]?.Value<int>() ?? DefaultAmount,
                });
            }
            return list;
        }

        public static void Write(LevelDocument doc, List<YarnPalette> list)
        {
            doc.GameData ??= new JObject();
            var arr = new JArray();
            foreach (var p in list)
                arr.Add(new JObject
                {
                    ["center"] = new JObject { ["x"] = p.CenterX, ["y"] = p.CenterY },
                    ["amount"] = p.Amount,
                });
            doc.GameData[Key] = arr;
        }

        // The 3x3 cells covered by a palette centered at `center` (may include
        // out-of-bounds refs; callers that care should bounds-check).
        public static IEnumerable<CellRef> CoveredCells(CellRef center)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                yield return new CellRef(center.X + dx, center.Y + dy);
        }

        // The palette whose 3x3 covers `cell`, if any.
        public static bool TryPaletteAt(LevelDocument doc, CellRef cell, out YarnPalette palette)
        {
            foreach (var p in All(doc))
                if (Covers(p, cell)) { palette = p; return true; }
            palette = default;
            return false;
        }

        public static bool Covers(YarnPalette p, CellRef cell) =>
            System.Math.Abs(cell.X - p.CenterX) <= 1 && System.Math.Abs(cell.Y - p.CenterY) <= 1;

        // May a new palette be centered here? Full 3x3 in-bounds, all 9 cells are boxes,
        // and the 3x3 does not overlap an existing palette's 3x3 (two 3x3 regions overlap
        // iff their centers are within 2 on both axes).
        public static bool CanPlace(GridData<ICellData> grid, CellRef center, IEnumerable<YarnPalette> existing)
        {
            if (grid == null) return false;
            if (center.X - 1 < 0 || center.X + 1 >= grid.Width)  return false;
            if (center.Y - 1 < 0 || center.Y + 1 >= grid.Height) return false;

            foreach (var c in CoveredCells(center))
                if (!IsBox(grid.Get(c.X, c.Y))) return false;

            foreach (var ex in existing)
                if (System.Math.Abs(center.X - ex.CenterX) <= 2 &&
                    System.Math.Abs(center.Y - ex.CenterY) <= 2)
                    return false;

            return true;
        }

        public static void Add(LevelDocument doc, CellRef center)
        {
            var list = All(doc);
            list.Add(new YarnPalette { CenterX = center.X, CenterY = center.Y, Amount = DefaultAmount });
            Write(doc, list);
        }

        // Removes the palette covering `cell` (if any).
        public static void Remove(LevelDocument doc, CellRef cell)
        {
            var list = All(doc);
            list.RemoveAll(p => Covers(p, cell));
            Write(doc, list);
        }

        // Sets the requirement amount of the palette covering `cell` (if any).
        public static void SetAmount(LevelDocument doc, CellRef cell, int amount)
        {
            var list = All(doc);
            for (int i = 0; i < list.Count; i++)
                if (Covers(list[i], cell))
                {
                    var p = list[i];
                    p.Amount = System.Math.Max(1, amount);
                    list[i] = p;
                }
            Write(doc, list);
        }
    }
}
