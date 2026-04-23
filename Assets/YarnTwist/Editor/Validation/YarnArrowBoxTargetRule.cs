using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Validation/Arrow Box Target")]
    public sealed class YarnArrowBoxTargetRule : ValidationRuleBase
    {
        public override ValidationScope Scope => ValidationScope.Cell;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            if (ctx.Grid?.Cells == null) yield break;

            for (int y = 0; y < ctx.Grid.Height; y++)
            {
                for (int x = 0; x < ctx.Grid.Width; x++)
                {
                    if (ctx.Grid.Get(x, y) is not YarnArrowBoxCell arrow) continue;

                    var (tx, ty) = arrow.Direction switch
                    {
                        YarnDirection.Up    => (x, y + 1),
                        YarnDirection.Down  => (x, y - 1),
                        YarnDirection.Left  => (x - 1, y),
                        YarnDirection.Right => (x + 1, y),
                        _                   => (x, y)
                    };

                    if (!ctx.Grid.InBounds(tx, ty))
                    {
                        yield return new ValidationEntry(Id, ValidationSeverity.Error,
                            $"Arrow box at ({x},{y}) points outside the grid.");
                        continue;
                    }

                    if (ctx.Grid.Get(tx, ty) is YarnWallCell)
                        yield return new ValidationEntry(Id, ValidationSeverity.Error,
                            $"Arrow box at ({x},{y}) points to a wall at ({tx},{ty}).");
                }
            }
        }
    }
}
