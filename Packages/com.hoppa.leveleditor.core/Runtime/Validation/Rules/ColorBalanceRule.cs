using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.LevelEditor.Core
{
    // Validates that for every color, sourceCount * sourceValue == sinkCount * sinkValue.
    // Example (Yarn Sort): source = "ys.box" (value 9), sink = "ys.spool" (value 3)
    // → 1 box (9) balances 3 spools (3 each). Configured per game in the Inspector.
    [CreateAssetMenu(menuName = "Hoppa/Level Editor/Rules/Color Balance", order = 12)]
    public sealed class ColorBalanceRule : ValidationRuleBase
    {
        [SerializeField] private string _sourceCellTypeId;
        [SerializeField] private string _sinkCellTypeId;
        [SerializeField] private int _sourceValuePerCell = 1;
        [SerializeField] private int _sinkValuePerCell = 1;

        public override ValidationScope Scope => ValidationScope.Color;

        // For test / scripted setup.
        public void Configure(string id, string sourceCellTypeId, string sinkCellTypeId,
            int sourceValue = 1, int sinkValue = 1)
        {
            Configure(id);
            _sourceCellTypeId = sourceCellTypeId;
            _sinkCellTypeId   = sinkCellTypeId;
            _sourceValuePerCell = sourceValue;
            _sinkValuePerCell   = sinkValue;
        }

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            if (ctx.Grid?.Cells == null) yield break;

            var sourceTotals = new Dictionary<string, int>();
            var sinkTotals   = new Dictionary<string, int>();

            foreach (var cell in ctx.Grid.Cells)
            {
                if (cell is not IColoredCell colored) continue;
                var colorId = colored.ColorId;
                if (string.IsNullOrEmpty(colorId)) continue;

                if (cell.CellTypeId == _sourceCellTypeId)
                {
                    sourceTotals.TryGetValue(colorId, out var v);
                    sourceTotals[colorId] = v + _sourceValuePerCell;
                }
                else if (cell.CellTypeId == _sinkCellTypeId)
                {
                    sinkTotals.TryGetValue(colorId, out var v);
                    sinkTotals[colorId] = v + _sinkValuePerCell;
                }
            }

            var allColors = new HashSet<string>(sourceTotals.Keys);
            allColors.UnionWith(sinkTotals.Keys);

            foreach (var colorId in allColors)
            {
                sourceTotals.TryGetValue(colorId, out var src);
                sinkTotals.TryGetValue(colorId, out var snk);
                if (src != snk)
                    yield return new ValidationEntry(
                        Id, ValidationSeverity.Error,
                        $"Color \"{colorId}\": source total ({src}) != sink total ({snk}).");
            }
        }
    }
}
