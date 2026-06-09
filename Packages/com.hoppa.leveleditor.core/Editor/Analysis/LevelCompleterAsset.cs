using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public abstract class LevelCompleterAsset : ScriptableObject, ILevelCompleter
    {
        public abstract LevelCompletionResult Complete(LevelDocument doc, GameProfile profile, CompletionRequest req);

        // Default: no per-mechanic toggles. Layer-2 completers override to expose
        // their named on/off switches in the Auto-fill panel.
        public virtual IReadOnlyList<string> MechanicToggles => null;
    }
}
