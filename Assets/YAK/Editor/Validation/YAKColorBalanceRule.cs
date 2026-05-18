using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Compares wool count per color (grid) against spool capacity sum per color
    // (top section). Emits one entry per color: Info when balanced, Error when not.
    // Hidden spools are included — IsHidden is a runtime reveal flag, not a balance flag.
    [CreateAssetMenu(menuName = "Hoppa/YAK/Validation/Color Balance")]
    public sealed class YAKColorBalanceRule : ValidationRuleBase
    {
        public override ValidationScope Scope => ValidationScope.Color;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            var grid     = new Dictionary<string, int>();
            var capacity = new Dictionary<string, int>();

            if (ctx.Grid?.Cells != null)
            {
                foreach (var cell in ctx.Grid.Cells)
                {
                    if (cell is YAKWoolCell wool && !string.IsNullOrEmpty(wool.ColorId))
                        Add(grid, wool.ColorId, 1);
                }
            }

            if (ctx.Document?.TopSection != null)
            {
                YAKTopSectionData top = null;
                try   { top = ctx.Document.TopSection.ToObject<YAKTopSectionData>(); }
                catch { top = null; }

                if (top?.Columns != null)
                    foreach (var col in top.Columns)
                        if (col?.Spools != null)
                            foreach (var spool in col.Spools)
                                if (!string.IsNullOrEmpty(spool.ColorId))
                                    Add(capacity, spool.ColorId, spool.Capacity);
            }

            var colors = new HashSet<string>(grid.Keys);
            colors.UnionWith(capacity.Keys);

            foreach (var colorId in colors)
            {
                grid.TryGetValue(colorId, out var wool);
                capacity.TryGetValue(colorId, out var cap);

                bool   balanced = wool == cap;
                string mark     = balanced ? "✓" : "✗";
                var    severity = balanced ? ValidationSeverity.Info : ValidationSeverity.Error;

                Color? swatch = ctx.Palette != null && ctx.Palette.TryGetColor(colorId, out var c)
                                  ? c : (Color?)null;

                yield return new ValidationEntry(Id, severity,
                    $"{colorId}: {wool} wool / {cap} capacity  {mark}",
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
