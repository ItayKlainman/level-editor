using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Validation/Tunnel Output")]
    public sealed class YarnTunnelOutputRule : ValidationRuleBase
    {
        public override ValidationScope Scope => ValidationScope.Cell;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            if (ctx.Grid?.Cells == null) yield break;

            for (int y = 0; y < ctx.Grid.Height; y++)
            {
                for (int x = 0; x < ctx.Grid.Width; x++)
                {
                    if (ctx.Grid.Get(x, y) is not YarnTunnelCell tunnel) continue;

                    if (tunnel.Queue.Count == 0)
                    {
                        yield return new ValidationEntry(Id, ValidationSeverity.Warning,
                            $"Tunnel at ({x},{y}) has an empty queue.");
                    }

                    var (tx, ty) = tunnel.OutputDirection switch
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
                            $"Tunnel at ({x},{y}) points {tunnel.OutputDirection} off the grid border — move it inward or change its direction.");
                    }
                    else if (ctx.Grid.Get(tx, ty) is not YarnEmptyCell)
                    {
                        yield return new ValidationEntry(Id, ValidationSeverity.Error,
                            $"Tunnel at ({x},{y}) output cell ({tx},{ty}) must be Empty so items can exit.");
                    }
                }
            }
        }
    }
}
