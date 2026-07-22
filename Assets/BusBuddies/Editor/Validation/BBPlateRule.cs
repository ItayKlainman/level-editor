using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Validates rectangular Plates (GameData["plateConfigs"]). Placement is gated by the
    // region-drag tool, but later edits (resizing the grid, hand-editing / importing a
    // file) can invalidate a plate — so re-check here and block export: each plate must
    // be fully in-bounds, have Size >= 1 on both axes, a PixelAmount >= 1, and must not
    // overlap another plate. Scope = Level. Mirrors YarnPaletteRule / BBRoadBlockRule.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Validation/Plate")]
    public sealed class BBPlateRule : ValidationRuleBase
    {
        public override ValidationScope Scope => ValidationScope.Level;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            var doc = ctx?.Document;
            if (doc == null) yield break;

            var plates = BusBuddiesPlateConfigs.All(doc);
            if (plates.Count == 0) yield break;

            var grid = ctx.Grid;

            for (int i = 0; i < plates.Count; i++)
            {
                var p = plates[i];
                int n = i + 1;

                if (p.W < 1 || p.H < 1)
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Plate {n} has a zero/negative size ({p.W}x{p.H}); width and height must be at least 1.");

                if (grid != null &&
                    (p.X < 0 || p.Y < 0 || p.X + p.W > grid.Width || p.Y + p.H > grid.Height))
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Plate {n} at ({p.X},{p.Y}) size {p.W}x{p.H} extends outside the grid.");

                if (p.Amount < 1)
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Plate {n} needs a PixelAmount of at least 1 (pixels to pick before it opens).");

                for (int j = i + 1; j < plates.Count; j++)
                {
                    var q = plates[j];
                    if (BusBuddiesPlateConfigs.RectsOverlap(p.X, p.Y, p.W, p.H, q.X, q.Y, q.W, q.H))
                        yield return new ValidationEntry(Id, ValidationSeverity.Error,
                            $"Plate {n} overlaps Plate {j + 1}; plates cannot overlap.");
                }
            }
        }
    }
}
