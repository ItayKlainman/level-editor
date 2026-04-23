using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Abstract SO base that wraps an authored level's JSON.
    // Games subclass this (e.g. YarnLevelAsset) for runtime loading and AssetReference support.
    public abstract class LevelAsset : ScriptableObject
    {
        [SerializeField] private string _levelJson;

        public string LevelJson => _levelJson;

        internal void ApplyJson(string json) => _levelJson = json;
    }
}
