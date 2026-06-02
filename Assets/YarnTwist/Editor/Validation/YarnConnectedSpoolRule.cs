using System.Collections.Generic;
using System.Linq;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Validates Connected Spools: every spool carrying a ConnectionId must be one
    // of exactly two members forming a pair in different, adjacent columns. The
    // game has no notion of an incomplete pair, so the editor must guarantee both
    // halves exist and are adjacent — this also catches a pair left half-finished
    // or orphaned when one member is deleted.
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Validation/Connected Spool")]
    public sealed class YarnConnectedSpoolRule : ValidationRuleBase
    {
        public override ValidationScope Scope => ValidationScope.Level;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            var top = ctx.Document?.TopSection?.ToObject<YarnTopSectionData>();
            if (top?.Columns == null) yield break;

            // Gather every connected spool as (connectionId, column).
            var members = new Dictionary<int, List<int>>(); // id → list of column indices
            for (int c = 0; c < top.Columns.Count; c++)
            {
                var spools = top.Columns[c]?.Spools;
                if (spools == null) continue;
                foreach (var spool in spools)
                {
                    if (spool?.ConnectionId == null) continue;
                    int id = spool.ConnectionId.Value;
                    if (!members.TryGetValue(id, out var list))
                        members[id] = list = new List<int>();
                    list.Add(c);
                }
            }
            if (members.Count == 0) yield break;

            // Display numbers are the ordinal rank among the distinct ids present
            // (so the designer always sees contiguous 1..N).
            var displayNum = members.Keys.OrderBy(k => k)
                .Select((id, i) => (id, n: i + 1))
                .ToDictionary(t => t.id, t => t.n);

            foreach (var kv in members.OrderBy(k => k.Key))
            {
                int n     = displayNum[kv.Key];
                var cols  = kv.Value;

                if (cols.Count == 1)
                {
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Connected Spool Pair {n} is incomplete. Please select a second valid spool.");
                    continue;
                }

                if (cols.Count > 2)
                {
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Connected Spool Pair {n} has {cols.Count} members; a pair must have exactly two.");
                    continue;
                }

                int colA = cols[0], colB = cols[1];
                if (colA == colB)
                {
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Connected Spool Pair {n} links two spools in the same column; they must be in adjacent columns.");
                }
                else if (System.Math.Abs(colA - colB) != 1)
                {
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Connected Spool Pair {n} links non-adjacent columns ({colA + 1} and {colB + 1}); only neighbouring columns can connect.");
                }
            }

            // Structural soft-lock: connections that can never all unlock (e.g. two
            // pairs that cross between the same column-pair). The connect UI blocks
            // creating these, but reordering or moving a connected spool can still
            // produce one, so flag it here and block export.
            if (YarnSpoolConnection.ConnectionsDeadlock(top))
                yield return new ValidationEntry(Id, ValidationSeverity.Error,
                    "Connected spools form a soft-lock: a connected pair can never unlock (links cross). Re-order or re-connect so the links don't cross.");
        }
    }
}
