using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public interface IEditorPanel
    {
        void OnGUI(Rect rect, LevelEditorSession session);
    }
}
