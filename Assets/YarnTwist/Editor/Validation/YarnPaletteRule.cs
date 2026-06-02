using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Validates Palettes. Placement is gated by the Add Palette action, but later
    // edits (deleting/repainting a covered box, resizing the grid) can invalidate a
    // palette — so re-check here and block export: each palette's 3x3 must be fully
    // in-bounds, cover only boxes, and not overlap another palette.
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Validation/Palette")]
    public sealed class YarnPaletteRule : ValidationRuleBase
    {
        public override ValidationScope Scope => ValidationScope.Level;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            var grid = ctx.Grid;
            if (grid == null) yield break;

            var palettes = YarnPalettes.All(ctx.Document);

            for (int i = 0; i < palettes.Count; i++)
            {
                var p = palettes[i];
                int n = i + 1;

                if (p.CenterX - 1 < 0 || p.CenterX + 1 >= grid.Width ||
                    p.CenterY - 1 < 0 || p.CenterY + 1 >= grid.Height)
                {
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Palette {n} at ({p.CenterX},{p.CenterY}) extends outside the grid.");
                    continue;
                }

                foreach (var c in YarnPalettes.CoveredCells(p.Center))
                    if (!YarnPalettes.IsBox(grid.Get(c.X, c.Y)))
                    {
                        yield return new ValidationEntry(Id, ValidationSeverity.Error,
                            $"Palette {n} at ({p.CenterX},{p.CenterY}) covers a non-box cell ({c.X},{c.Y}); all 9 cells must be boxes.");
                        break;
                    }

                for (int j = i + 1; j < palettes.Count; j++)
                {
                    var q = palettes[j];
                    if (System.Math.Abs(p.CenterX - q.CenterX) <= 2 &&
                        System.Math.Abs(p.CenterY - q.CenterY) <= 2)
                        yield return new ValidationEntry(Id, ValidationSeverity.Error,
                            $"Palette {n} overlaps Palette {j + 1}; palettes cannot overlap.");
                }
            }
        }
    }
}
