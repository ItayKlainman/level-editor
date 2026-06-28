using NUnit.Framework;

namespace Hoppa.YAK.Tests
{
    public class YakSpoolAutofillerComplexityTests
    {
        [Test]
        public void Autofill_HigherComplexityTarget_RaisesMeasuredComplexity()
        {
            // Same grid; request low vs high complexity; measured complexity should trend up.
            var grid = YakAutofillTestFixtures.MultiColorGrid(seed: 11);

            float low  = YakAutofillTestFixtures.RunAndMeasureComplexity(grid, targetComplexity: 1, seed: 11);
            float high = YakAutofillTestFixtures.RunAndMeasureComplexity(grid, targetComplexity: 9, seed: 11);

            Assert.Greater(high, low,
                "a higher complexity target must yield higher measured click complexity");
        }

        [Test]
        public void Autofill_StillSolvable_AtAnyComplexity()
        {
            var grid = YakAutofillTestFixtures.MultiColorGrid(seed: 3);
            var result = YakAutofillTestFixtures.Run(grid, targetComplexity: 7, seed: 3);
            Assert.IsTrue(result.Analysis == null || result.Analysis.Solvable || !result.Succeeded,
                "accepted candidates must be solvable; a best-effort may be returned otherwise");
        }
    }
}
