using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public abstract class EditorPanelAsset : ScriptableObject, IEditorPanel
    {
        public abstract string PanelId { get; }
        public abstract void OnGUI(Rect rect, LevelEditorSession session);
    }
}
