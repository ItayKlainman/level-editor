using System;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Describes one entry in a cell's right-click context popup.
    // Optionally renders a setup UI above the action button (e.g. direction picker).
    //
    // An action is one of two flavors:
    //  • Create — returns a replacement cell for the clicked position (single-cell convert).
    //  • Apply  — runs a free-form mutation against the session (may touch several cells,
    //             e.g. connecting two adjacent boxes). The popup wraps it in one undo step.
    public sealed class CellContextAction
    {
        public string Label         { get; }
        public float  OptionsHeight { get; }  // extra height needed by DrawOptions (0 if none)

        private readonly Action<Rect>               _drawOptions;
        private readonly Func<ICellData>            _create;
        private readonly Action<LevelEditorSession> _apply;

        // Convert-clicked-cell action.
        public CellContextAction(string label, Func<ICellData> create,
            float optionsHeight = 0f, Action<Rect> drawOptions = null)
        {
            Label         = label;
            _create       = create;
            OptionsHeight = optionsHeight;
            _drawOptions  = drawOptions;
        }

        // Free-form session mutation (e.g. multi-cell). Applied under a single undo snapshot.
        public CellContextAction(string label, Action<LevelEditorSession> apply,
            float optionsHeight = 0f, Action<Rect> drawOptions = null)
        {
            Label         = label;
            _apply        = apply;
            OptionsHeight = optionsHeight;
            _drawOptions  = drawOptions;
        }

        // True when this action mutates the session directly rather than replacing the clicked cell.
        public bool IsApply => _apply != null;

        public void DrawOptions(Rect rect) => _drawOptions?.Invoke(rect);
        public ICellData Create()          => _create?.Invoke();
        public void Apply(LevelEditorSession session) => _apply?.Invoke(session);
    }
}
