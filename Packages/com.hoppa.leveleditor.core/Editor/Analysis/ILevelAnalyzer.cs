namespace Hoppa.LevelEditor.Core.Editor
{
    // Generic contract: "given a complete level, is it playable and how many
    // distinct winning sequences exist?" Layer 1 does NOT define what
    // 'playable' means — the Layer 2 analyzer subclass knows the game rules.
    public interface ILevelAnalyzer
    {
        LevelAnalysisResult Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req);
    }
}
