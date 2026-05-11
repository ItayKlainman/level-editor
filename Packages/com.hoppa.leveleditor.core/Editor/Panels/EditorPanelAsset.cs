using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public abstract class EditorPanelAsset : ScriptableObject, IEditorPanel
    {
        public abstract string PanelId { get; }

        // Explicit implementation hides OnGUI from Unity's ScriptableObject reflection,
        // which rejects any OnGUI(params) method it finds on SO subclasses.
        void IEditorPanel.OnGUI(Rect rect, LevelEditorSession session) => Draw(rect, session);

        protected abstract void Draw(Rect rect, LevelEditorSession session);
    }
}
