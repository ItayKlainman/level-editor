using System.Linq;
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BBRoadBlockRuleTests
    {
        private static BBRoadBlockRule Rule()
        {
            var r = ScriptableObject.CreateInstance<BBRoadBlockRule>();
            r.Configure("busbuddies.road_block");
            return r;
        }

        // Build a document with a given conveyorCount and a raw slotConfigs array so
        // tests can inject out-of-range / duplicate / bad-amount states the helper's
        // clamping would otherwise prevent.
        private static ValidationContext Ctx(int conveyorCount, JArray slotConfigs)
        {
            var doc = new LevelDocument
            {
                GameData = new JObject
                {
                    ["conveyorCount"] = conveyorCount,
                    ["slotConfigs"] = slotConfigs,
                },
            };
            return new ValidationContext(doc, null);
        }

        private static JObject Slot(int slotIndex, int amount) =>
            new JObject { ["slotIndex"] = slotIndex, ["amount"] = amount };

        [Test]
        public void CleanBlock_NoErrors()
        {
            var ctx = Ctx(5, new JArray { Slot(4, 10), Slot(0, 3) });
            var errs = Rule().Evaluate(ctx).Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsEmpty(errs);
        }

        [Test]
        public void NoBlocks_NoErrors()
        {
            var ctx = Ctx(5, new JArray());
            var errs = Rule().Evaluate(ctx).Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsEmpty(errs);
        }

        [Test]
        public void AmountBelowOne_Errors()
        {
            var ctx = Ctx(5, new JArray { Slot(2, 0) });
            var errs = Rule().Evaluate(ctx).Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsNotEmpty(errs);
        }

        [Test]
        public void SlotIndexOutOfRange_Errors()
        {
            var ctx = Ctx(5, new JArray { Slot(5, 10) }); // slots 0..4 valid; 5 is out
            var errs = Rule().Evaluate(ctx).Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsNotEmpty(errs);
        }

        [Test]
        public void NegativeSlotIndex_Errors()
        {
            var ctx = Ctx(5, new JArray { Slot(-1, 10) });
            var errs = Rule().Evaluate(ctx).Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsNotEmpty(errs);
        }

        [Test]
        public void DuplicateSlotIndex_Errors()
        {
            var ctx = Ctx(5, new JArray { Slot(3, 10), Slot(3, 5) });
            var errs = Rule().Evaluate(ctx).Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsNotEmpty(errs);
        }

        [Test]
        public void RangeUsesConveyorCount()
        {
            // slot index 3 is valid at conveyorCount 5, but out of range at 3.
            var okErrs = Rule().Evaluate(Ctx(5, new JArray { Slot(3, 10) }))
                .Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsEmpty(okErrs);

            var badErrs = Rule().Evaluate(Ctx(3, new JArray { Slot(3, 10) }))
                .Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsNotEmpty(badErrs);
        }
    }
}
