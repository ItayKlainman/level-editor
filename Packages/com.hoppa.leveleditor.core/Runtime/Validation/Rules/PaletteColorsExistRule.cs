using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.LevelEditor.Core
{
    [CreateAssetMenu(menuName = "Hoppa/Level Editor/Rules/Palette Colors Exist", order = 10)]
    public sealed class PaletteColorsExistRule : ValidationRuleBase
    {
        public override ValidationScope Scope => ValidationScope.Color;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            if (ctx.Palette == null || ctx.Grid?.Cells == null) yield break;

            var reported = new HashSet<string>();
            foreach (var cell in ctx.Grid.Cells)
            {
                if (cell is not IColoredCell colored) continue;
                var colorId = colored.ColorId;
                if (string.IsNullOrEmpty(colorId) || reported.Contains(colorId)) continue;
                if (!ctx.Palette.Contains(colorId))
                {
                    reported.Add(colorId);
                    yield return new ValidationEntry(
                        Id, ValidationSeverity.Error,
                        $"Color \"{colorId}\" is used in the level but is not defined in the palette.");
                }
            }
        }
    }
}
