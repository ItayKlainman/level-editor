using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Compares Pixel Block count per color (grid) against total bus capacity per
    // color (top section). One entry per color: Info when balanced, Error when not.
    // Hidden buses are included — Hidden is a runtime reveal flag, not a balance flag.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Validation/Color Balance")]
    public sealed class BBColorBalanceRule : ValidationRuleBase
    {
        public override ValidationScope Scope => ValidationScope.Color;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            var blocks   = new Dictionary<string, int>();
            var capacity = new Dictionary<string, int>();

            if (ctx.Grid?.Cells != null)
            {
                foreach (var cell in ctx.Grid.Cells)
                    if (cell is BBPixelCell pixel && !string.IsNullOrEmpty(pixel.ColorId))
                        Add(blocks, pixel.ColorId, 1);
            }

            if (ctx.Document?.TopSection != null)
            {
                BusQueueData top = null;
                try   { top = ctx.Document.TopSection.ToObject<BusQueueData>(); }
                catch { top = null; }

                if (top?.Columns != null)
                    foreach (var col in top.Columns)
                        if (col?.Buses != null)
                            foreach (var bus in col.Buses)
                                if (!string.IsNullOrEmpty(bus.ColorId))
                                    Add(capacity, bus.ColorId, bus.Capacity);
            }

            var colors = new HashSet<string>(blocks.Keys);
            colors.UnionWith(capacity.Keys);

            foreach (var colorId in colors)
            {
                blocks.TryGetValue(colorId, out var n);
                capacity.TryGetValue(colorId, out var cap);

                bool   balanced = n == cap;
                string mark     = balanced ? "✓" : "✗";
                var    severity = balanced ? ValidationSeverity.Info : ValidationSeverity.Error;

                Color? swatch = ctx.Palette != null && ctx.Palette.TryGetColor(colorId, out var c)
                                  ? c : (Color?)null;

                yield return new ValidationEntry(Id, severity,
                    $"{colorId}: {n} blocks / {cap} capacity  {mark}",
                    swatch: swatch);
            }
        }

        private static void Add(Dictionary<string, int> d, string key, int amount)
        {
            if (string.IsNullOrEmpty(key)) return;
            d.TryGetValue(key, out var v);
            d[key] = v + amount;
        }
    }
}
