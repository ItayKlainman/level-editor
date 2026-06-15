using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Read/write helper for LevelSolution. Produces indented JSON whose field
    // names match LevelSolution exactly, so the in-game viewer can parse the same
    // file with UnityEngine.JsonUtility (no Newtonsoft dependency in the game).
    public static class SolutionJson
    {
        public static string Serialize(LevelSolution solution)
            => JsonConvert.SerializeObject(solution, Formatting.Indented);

        public static LevelSolution Deserialize(string json)
            => JsonConvert.DeserializeObject<LevelSolution>(json);

        // Builds a LevelSolution from an analyzer win-path and writes it to disk.
        // Returns false (writes nothing) when there is no win-path to export.
        public static bool Write(string path, string levelId, IReadOnlyList<int> winPath)
        {
            if (winPath == null || winPath.Count == 0) return false;
            var steps = new int[winPath.Count];
            for (int i = 0; i < winPath.Count; i++) steps[i] = winPath[i];
            var sol = new LevelSolution { levelId = levelId, steps = steps };
            File.WriteAllText(path, Serialize(sol));
            return true;
        }
    }
}
