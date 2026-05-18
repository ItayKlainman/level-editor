using System;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Small modal popup used by LevelEditorWindow.HandleNew to let the designer
    // pick the grid size when creating a new level. Defaults pre-filled from the
    // active GameProfile. Cancel = no-op.
    public sealed class NewLevelDialog : EditorWindow
    {
        private int    _width;
        private int    _height;
        private bool   _confirmed;
        private Action<int, int> _onConfirm;

        public static void Show(int defaultWidth, int defaultHeight, Action<int, int> onConfirm)
        {
            var win = CreateInstance<NewLevelDialog>();
            win.titleContent = new GUIContent("New Level");
            win._width       = Mathf.Max(1, defaultWidth);
            win._height      = Mathf.Max(1, defaultHeight);
            win._onConfirm   = onConfirm;
            win.minSize      = new Vector2(280f, 130f);
            win.maxSize      = new Vector2(280f, 130f);
            win.position = new Rect(
                EditorGUIUtility.GetMainWindowPosition().center - new Vector2(140f, 65f),
                new Vector2(280f, 130f));
            win.ShowModalUtility();
        }

        private void OnGUI()
        {
            const float Pad = 10f;
            float y = Pad;
            GUI.Label(new Rect(Pad, y, position.width - Pad * 2f, 18f),
                "Grid size for the new level:", EditorStyles.boldLabel);
            y += 24f;

            float labelW = 60f;
            float fieldW = position.width - Pad * 2f - labelW - 4f;

            GUI.Label(new Rect(Pad, y, labelW, 18f), "Width",  EditorStyles.miniLabel);
            _width = Mathf.Max(1, EditorGUI.IntField(
                new Rect(Pad + labelW + 4f, y, fieldW, 18f), _width));
            y += 22f;

            GUI.Label(new Rect(Pad, y, labelW, 18f), "Height", EditorStyles.miniLabel);
            _height = Mathf.Max(1, EditorGUI.IntField(
                new Rect(Pad + labelW + 4f, y, fieldW, 18f), _height));
            y += 28f;

            float btnW = 90f;
            if (GUI.Button(new Rect(position.width - Pad - btnW * 2f - 6f, y, btnW, 22f), "Cancel"))
            {
                _confirmed = false;
                Close();
            }
            if (GUI.Button(new Rect(position.width - Pad - btnW, y, btnW, 22f), "Create"))
            {
                _confirmed = true;
                Close();
            }
        }

        private void OnDestroy()
        {
            if (_confirmed) _onConfirm?.Invoke(_width, _height);
        }
    }
}
