using System;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Serializable, machine-readable winning solution: an ordered list of
    // game-defined action indices (the analyzer's WinPath). Game-agnostic — for
    // YAK a step is the column to tap; for another game it could be an item index.
    //
    // Field names are lowercase and the shape is intentionally flat so the SAME
    // JSON round-trips through both Newtonsoft (editor side) and UnityEngine's
    // JsonUtility (in-game viewer, zero extra dependencies).
    [Serializable]
    public sealed class LevelSolution
    {
        public string schemaVersion = "solution.v1";
        public string levelId;
        public int[]  steps;
    }
}
