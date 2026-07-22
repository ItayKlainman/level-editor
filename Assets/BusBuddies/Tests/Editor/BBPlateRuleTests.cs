using System.Linq;
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BBPlateRuleTests
    {
        private static BBPlateRule Rule()
        {
            var r = ScriptableObject.CreateInstance<BBPlateRule>();
            r.Configure("busbuddies.plate");
            return r;
        }

        // Build a document with a grid + a raw plateConfigs array so tests can inject
        // out-of-bounds / bad-size / overlapping states the helper's clamping would
        // otherwise prevent.
        private static ValidationContext Ctx(int w, int h, JArray plateConfigs)
        {
            var doc = new LevelDocument
            {
                Grid = new GridData<ICellData>(w, h),
                GameData = new JObject { ["plateConfigs"] = plateConfigs },
            };
            return new ValidationContext(doc, null);
        }

        private static JObject Plate(int x, int y, int pw, int ph, int amount) =>
            new JObject { ["x"] = x, ["y"] = y, ["w"] = pw, ["h"] = ph, ["amount"] = amount };

        private static int Errors(ValidationContext ctx) =>
            Rule().Evaluate(ctx).Count(e => e.Severity == ValidationSeverity.Error);

        [Test]
        public void CleanPlate_NoErrors()
        {
            Assert.AreEqual(0, Errors(Ctx(10, 10, new JArray { Plate(1, 1, 3, 3, 5) })));
        }

        [Test]
        public void NoPlates_NoErrors()
        {
            Assert.AreEqual(0, Errors(Ctx(10, 10, new JArray())));
        }

        [Test]
        public void OutOfBounds_Errors()
        {
            Assert.Greater(Errors(Ctx(10, 10, new JArray { Plate(8, 8, 3, 3, 5) })), 0);
            Assert.Greater(Errors(Ctx(10, 10, new JArray { Plate(-1, 0, 2, 2, 5) })), 0);
        }

        [Test]
        public void ZeroSize_Errors()
        {
            Assert.Greater(Errors(Ctx(10, 10, new JArray { Plate(0, 0, 0, 3, 5) })), 0);
        }

        [Test]
        public void AmountBelowOne_Errors()
        {
            Assert.Greater(Errors(Ctx(10, 10, new JArray { Plate(0, 0, 2, 2, 0) })), 0);
        }

        [Test]
        public void Overlap_Errors()
        {
            var ctx = Ctx(10, 10, new JArray { Plate(2, 2, 3, 3, 5), Plate(4, 4, 3, 3, 5) });
            Assert.Greater(Errors(ctx), 0);
        }

        [Test]
        public void NonOverlappingFlush_NoErrors()
        {
            // Two plates sharing an edge but not overlapping.
            var ctx = Ctx(10, 10, new JArray { Plate(0, 0, 3, 3, 5), Plate(3, 0, 3, 3, 5) });
            Assert.AreEqual(0, Errors(ctx));
        }
    }
}
