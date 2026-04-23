using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Stores per-project editor preferences (active GameProfile, last-opened file, etc.).
    // Persisted as a ProjectSettings asset; extended in Phase 3.
    [CreateAssetMenu(menuName = "Hoppa/Level Editor/Editor Settings", order = 2)]
    public sealed class LevelEditorSettings : ScriptableObject
    {
        [SerializeField] private GameProfile _activeProfile;
        [SerializeField] private string _lastOpenedPath;

        public GameProfile ActiveProfile => _activeProfile;
        public string LastOpenedPath
        {
            get => _lastOpenedPath;
            set => _lastOpenedPath = value;
        }
    }
}
