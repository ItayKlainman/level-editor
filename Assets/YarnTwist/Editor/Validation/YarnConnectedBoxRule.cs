using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Validates Connected Boxes: every box with a ConnectedDir must point at an
    // in-bounds regular box that points reciprocally back. The game does NOT
    // validate reverse links at runtime, so the editor must guarantee reciprocity —
    // and this also catches links left dangling when one half of a pair is erased
    // or repainted.
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Validation/Connected Box")]
    public sealed class YarnConnectedBoxRule : ValidationRuleBase
    {
        public override ValidationScope Scope => ValidationScope.Cell;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            if (ctx.Grid?.Cells == null) yield break;

            for (int y = 0; y < ctx.Grid.Height; y++)
            {
                for (int x = 0; x < ctx.Grid.Width; x++)
                {
                    if (ctx.Grid.Get(x, y) is not YarnBoxCell box || !box.ConnectedDir.HasValue)
                        continue;

                    var dir = box.ConnectedDir.Value;
                    var (tx, ty) = Neighbor(x, y, dir);

                    if (!ctx.Grid.InBounds(tx, ty))
                    {
                        yield return new ValidationEntry(Id, ValidationSeverity.Error,
                            $"Connected box at ({x},{y}) points outside the grid.");
                        continue;
                    }

                    if (ctx.Grid.Get(tx, ty) is not YarnBoxCell partner)
                    {
                        yield return new ValidationEntry(Id, ValidationSeverity.Error,
                            $"Connected box at ({x},{y}) is linked to a non-box cell at ({tx},{ty}).");
                        continue;
                    }

                    if (partner.ConnectedDir != Opposite(dir))
                    {
                        yield return new ValidationEntry(Id, ValidationSeverity.Error,
                            $"Connected box at ({x},{y}) is not reciprocally linked — the box at ({tx},{ty}) does not point back.");
                    }
                }
            }
        }

        private static (int x, int y) Neighbor(int x, int y, YarnDirection d) => d switch
        {
            YarnDirection.Up    => (x, y + 1),
            YarnDirection.Down  => (x, y - 1),
            YarnDirection.Left  => (x - 1, y),
            YarnDirection.Right => (x + 1, y),
            _                   => (x, y),
        };

        private static YarnDirection Opposite(YarnDirection d) => d switch
        {
            YarnDirection.Up    => YarnDirection.Down,
            YarnDirection.Down  => YarnDirection.Up,
            YarnDirection.Left  => YarnDirection.Right,
            YarnDirection.Right => YarnDirection.Left,
            _                   => d,
        };
    }
}
