using System.Collections.Generic;
using System.Linq;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Validates Connected Buses: every bus carrying a ConnectedId (>= 0) must be one of
    // exactly two members forming a pair. The game pairs any two buses by coordinate, so
    // there is NO adjacency constraint — but same-column pairs (can never both be head)
    // and link-crossing soft-locks are rejected. Scope = Level.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Validation/Connected Bus")]
    public sealed class BBConnectedBusRule : ValidationRuleBase
    {
        public override ValidationScope Scope => ValidationScope.Level;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            var queue = ctx.Document?.TopSection?.ToObject<BusQueueData>();
            if (queue?.Columns == null) yield break;

            BusConnection.BuildConnInfo(queue, out var members, out _);
            if (members.Count == 0) yield break;

            var displayNum = members.Keys.OrderBy(k => k)
                .Select((id, i) => (id, n: i + 1))
                .ToDictionary(t => t.id, t => t.n);

            foreach (var kv in members.OrderBy(k => k.Key))
            {
                int n = displayNum[kv.Key];
                var mem = kv.Value;

                if (mem.Count == 1)
                {
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Connected Bus Pair {n} is incomplete. Select a second bus to connect.");
                    continue;
                }
                if (mem.Count > 2)
                {
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Connected Bus Pair {n} has {mem.Count} members; a pair must have exactly two.");
                    continue;
                }
                if (mem[0].col == mem[1].col)
                {
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Connected Bus Pair {n} links two buses in the same column; they can never both reach the front. Connect buses in different columns.");
                }
            }

            if (BusConnection.ConnectionsDeadlock(queue))
                yield return new ValidationEntry(Id, ValidationSeverity.Error,
                    "Connected buses form a soft-lock: a connected pair can never both reach the front (links cross). Re-order or re-connect so the links don't cross.");
        }
    }
}
