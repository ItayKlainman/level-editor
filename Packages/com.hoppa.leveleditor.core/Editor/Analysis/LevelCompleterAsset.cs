using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public abstract class LevelCompleterAsset : ScriptableObject, ILevelCompleter
    {
        public abstract LevelCompletionResult Complete(LevelDocument doc, GameProfile profile, CompletionRequest req);
    }
}
