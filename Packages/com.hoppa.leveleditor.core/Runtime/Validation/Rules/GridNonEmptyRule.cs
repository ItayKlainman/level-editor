using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.LevelEditor.Core
{
    [CreateAssetMenu(menuName = "Hoppa/Level Editor/Rules/Grid Non-Empty", order = 11)]
    public sealed class GridNonEmptyRule : ValidationRuleBase
    {
        [SerializeField] private string _emptyCellTypeId = "core.empty";

        public override ValidationScope Scope => ValidationScope.Level;

        // For test / scripted setup.
        public void Configure(string id, string emptyCellTypeId)
        {
            Configure(id);
            _emptyCellTypeId = emptyCellTypeId;
        }

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            if (ctx.Grid?.Cells == null)
            {
                yield return new ValidationEntry(Id, ValidationSeverity.Error, "Grid is missing.");
                yield break;
            }

            foreach (var cell in ctx.Grid.Cells)
            {
                if (cell != null && cell.CellTypeId != _emptyCellTypeId)
                    yield break; // found at least one content cell — pass
            }

            yield return new ValidationEntry(
                Id, ValidationSeverity.Error,
                "The grid is empty. Place at least one non-empty cell.");
        }
    }
}
