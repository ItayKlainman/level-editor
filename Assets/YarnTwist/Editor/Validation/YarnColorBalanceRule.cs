using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Validation/Color Balance")]
    public sealed class YarnColorBalanceRule : ValidationRuleBase
    {
        [SerializeField] private int _ballsPerBox   = 9;
        [SerializeField] private int _ballsPerSpool = 3;

        public override ValidationScope Scope => ValidationScope.Color;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            var balls    = new Dictionary<string, int>();
            var capacity = new Dictionary<string, int>();

            if (ctx.Grid?.Cells != null)
            {
                foreach (var cell in ctx.Grid.Cells)
                {
                    if (cell is YarnBoxCell box)        Add(balls, box.ColorId, _ballsPerBox);
                    if (cell is YarnArrowBoxCell arrow)  Add(balls, arrow.ColorId, _ballsPerBox);
                    if (cell is YarnTunnelCell tunnel)
                        foreach (var id in tunnel.Queue) Add(balls, id, _ballsPerBox);
                }
            }

            if (ctx.Document?.TopSection != null)
            {
                var top = ctx.Document.TopSection.ToObject<YarnTopSectionData>();
                if (top?.Columns != null)
                    foreach (var col in top.Columns)
                        if (col?.Spools != null)
                            foreach (var spool in col.Spools)
                                Add(capacity, spool.ColorId, _ballsPerSpool);
            }

            var colors = new HashSet<string>(balls.Keys);
            colors.UnionWith(capacity.Keys);

            // One row per color: grid is the source of truth.
            // coveredBoxes = how much box-capacity the configured spools provide.
            // neededSpools = how many spools the grid requires.
            // Severity reflects balance so the header turns red on mismatch.
            foreach (var colorId in colors)
            {
                balls.TryGetValue(colorId, out var b);
                capacity.TryGetValue(colorId, out var cap);

                int   gridBoxes    = _ballsPerBox   > 0 ? b   / _ballsPerBox   : 0;
                int   neededSpools = _ballsPerSpool > 0 ? b   / _ballsPerSpool : 0;
                int   configSpools = _ballsPerSpool > 0 ? cap / _ballsPerSpool : 0;
                float coveredBoxes = _ballsPerBox   > 0 ? (float)cap / _ballsPerBox : 0f;

                bool   balanced = b == cap;
                string mark     = balanced ? "✓" : "✗";
                Color? swatch   = ctx.Palette != null && ctx.Palette.TryGetColor(colorId, out var c)
                                  ? c : (Color?)null;
                var severity = balanced ? ValidationSeverity.Info : ValidationSeverity.Error;

                yield return new ValidationEntry(Id, severity,
                    $"{colorId}: {coveredBoxes:F1}/{gridBoxes} Boxes - {configSpools}/{neededSpools} Spools  {mark}",
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
