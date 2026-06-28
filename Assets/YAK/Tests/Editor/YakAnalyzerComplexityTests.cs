using NUnit.Framework;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.YAK.Tests
{
    public class YakAnalyzerComplexityTests
    {
        [Test]
        public void Analyze_WithoutFlag_LeavesComplexityZero()
        {
            var (doc, profile) = YakAnalyzerTestFixtures.SolvableSmallLevel();
            var analyzer = YakAnalyzerTestFixtures.Analyzer();
            var r = analyzer.Analyze(doc, profile, new AnalysisRequest { RolloutCount = 60, Seed = 5 });
            Assert.AreEqual(0f, r.ComplexityEstimate);
        }

        [Test]
        public void Analyze_WithFlag_PopulatesComplexityForSolvable()
        {
            var (doc, profile) = YakAnalyzerTestFixtures.SolvableSmallLevel();
            var analyzer = YakAnalyzerTestFixtures.Analyzer();
            var r = analyzer.Analyze(doc, profile,
                new AnalysisRequest { RolloutCount = 60, Seed = 5, MeasureComplexity = true });
            Assert.IsTrue(r.Solvable);
            Assert.That(r.ComplexityEstimate, Is.InRange(1f, 10f));
        }
    }
}
