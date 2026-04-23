using System;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class ToolbarPanel : IEditorPanel
    {
        public event Action OnNew;
        public event Action OnOpen;
        public event Action OnSave;
        public event Action OnSaveAs;
        public event Action OnUndo;
        public event Action OnRedo;
        public event Action OnTestPlay;

        private const float BtnH = 20f;
        private const float Gap  = 4f;

        private static readonly Color DirtyColor    = new Color(1.0f, 0.65f, 0.10f);
        private static readonly Color SepColor      = new Color(1.0f, 1.0f, 1.0f, 0.08f);

        private static readonly GUIContent LabelNew      = new GUIContent("New",     "Create a new empty level");
        private static readonly GUIContent LabelOpen     = new GUIContent("Open",    "Open an existing level (.json)");
        private static readonly GUIContent LabelSave     = new GUIContent("Save",    "Save the level to disk  (Ctrl+S)");
        private static readonly GUIContent LabelSaveAs   = new GUIContent("Save As", "Save to a new file");
        private static readonly GUIContent LabelUndo     = new GUIContent("Undo",    "Undo last paint stroke  (Ctrl+Z)");
        private static readonly GUIContent LabelRedo     = new GUIContent("Redo",    "Redo last undone stroke  (Ctrl+Y)");
        private static readonly GUIContent LabelTestPlay = new GUIContent("▶ Test",  "Save and enter Play Mode");

        public void OnGUI(Rect rect, LevelEditorSession session)
        {
            float y = rect.y + (rect.height - BtnH) * 0.5f;
            float x = rect.x + Gap;

            if (GUI.Button(new Rect(x, y, 44f, BtnH), LabelNew))   OnNew?.Invoke();   x += 44f + Gap;
            if (GUI.Button(new Rect(x, y, 44f, BtnH), LabelOpen))  OnOpen?.Invoke();  x += 44f + Gap;

            // Separator
            EditorGUI.DrawRect(new Rect(x, y, 1f, BtnH), SepColor); x += 1f + Gap;

            using (new EditorGUI.DisabledGroupScope(session == null))
            {
                if (GUI.Button(new Rect(x, y, 44f, BtnH), LabelSave))   OnSave?.Invoke();   x += 44f + Gap;
                if (GUI.Button(new Rect(x, y, 58f, BtnH), LabelSaveAs)) OnSaveAs?.Invoke(); x += 58f + Gap;
            }

            // Separator
            EditorGUI.DrawRect(new Rect(x, y, 1f, BtnH), SepColor); x += 1f + Gap;

            using (new EditorGUI.DisabledGroupScope(session == null || !session.CanUndo))
            {
                if (GUI.Button(new Rect(x, y, 44f, BtnH), LabelUndo)) OnUndo?.Invoke();
                x += 44f + Gap;
            }
            using (new EditorGUI.DisabledGroupScope(session == null || !session.CanRedo))
            {
                if (GUI.Button(new Rect(x, y, 44f, BtnH), LabelRedo)) OnRedo?.Invoke();
                x += 44f + Gap;
            }

            // Separator
            EditorGUI.DrawRect(new Rect(x, y, 1f, BtnH), SepColor); x += 1f + Gap;

            using (new EditorGUI.DisabledGroupScope(session == null))
            {
                if (GUI.Button(new Rect(x, y, 58f, BtnH), LabelTestPlay)) OnTestPlay?.Invoke();
                x += 58f + Gap;
            }

            // Level name
            if (session?.Document != null)
            {
                float nameW = rect.xMax - x - 80f;
                if (nameW > 40f)
                {
                    var nameText = string.IsNullOrEmpty(session.Document.DisplayName)
                        ? session.Document.LevelId
                        : session.Document.DisplayName;
                    GUI.Label(new Rect(x + Gap * 2f, y, nameW, BtnH),
                        new GUIContent(nameText, "Current level"), EditorStyles.miniLabel);
                }
            }

            // Dirty indicator — amber
            if (session != null && session.IsDirty)
            {
                var old = GUI.contentColor;
                GUI.contentColor = DirtyColor;
                GUI.Label(new Rect(rect.xMax - 70f, y, 66f, BtnH),
                    new GUIContent("● Unsaved", "Level has unsaved changes"),
                    EditorStyles.miniLabel);
                GUI.contentColor = old;
            }
        }
    }
}
