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

            // Info row per color (always shown as a balance table)
            foreach (var colorId in colors)
            {
                balls.TryGetValue(colorId, out var b);
                capacity.TryGetValue(colorId, out var cap);
                string mark = b == cap ? "✓" : "✗";
                yield return new ValidationEntry(Id, ValidationSeverity.Info,
                    $"{colorId}: {b} balls / {cap} cap  {mark}");
            }

            // Error row only for imbalanced colors
            foreach (var colorId in colors)
            {
                balls.TryGetValue(colorId, out var b);
                capacity.TryGetValue(colorId, out var cap);
                if (b != cap)
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Color '{colorId}': {b} balls vs {cap} spool capacity.");
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
