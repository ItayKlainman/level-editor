using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Base type the inspector type-filters on. GameProfile._levelAnalyzer is
    // typed as LevelAnalyzerAsset so designers can only drop in concrete
    // analyzer assets (not arbitrary ScriptableObjects).
    public abstract class LevelAnalyzerAsset : ScriptableObject, ILevelAnalyzer
    {
        public abstract LevelAnalysisResult Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req);
    }
}
