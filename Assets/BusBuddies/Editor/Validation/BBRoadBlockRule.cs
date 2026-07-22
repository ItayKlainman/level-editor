using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Validates per-slot Road Blocks (GameData["slotConfigs"]). Each blocked Active-Row
    // slot must carry a release Amount >= 1, sit within the row's slot range
    // [0, slots-1] (slots = GameData["conveyorCount"] ?? 5), and no slot may be blocked
    // twice. Scope = Level. Mirrors BBConnectedBusRule's style.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Validation/Road Block")]
    public sealed class BBRoadBlockRule : ValidationRuleBase
    {
        private const int DefaultSlots = 5;

        public override ValidationScope Scope => ValidationScope.Level;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            var doc = ctx?.Document;
            if (doc == null) yield break;

            var blocks = BusBuddiesSlotConfigs.All(doc);
            if (blocks.Count == 0) yield break;

            int slots = doc.GameData?["conveyorCount"]?.Value<int>() ?? DefaultSlots;

            var seen = new HashSet<int>();
            foreach (var b in blocks)
            {
                // Human slot number for messages (internal index is 0-based).
                int slotLabel = b.SlotIndex + 1;

                if (b.SlotIndex < 0 || b.SlotIndex >= slots)
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Road Block on slot {slotLabel} is out of range; the Active Row has {slots} slot(s) (1..{slots}).");
                else if (!seen.Add(b.SlotIndex))
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Slot {slotLabel} is blocked more than once; each slot may have at most one Road Block.");

                if (b.Amount < 1)
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Road Block on slot {slotLabel} needs an Amount of at least 1 (buses to click before release).");
            }
        }
    }
}
