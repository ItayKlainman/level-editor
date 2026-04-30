using System;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Describes one entry in a cell's right-click context popup.
    // Optionally renders a setup UI above the action button (e.g. direction picker before convert).
    public sealed class CellContextAction
    {
        public string Label       { get; }
        public float  OptionsHeight { get; }  // extra height needed by DrawOptions (0 if none)

        private readonly Action<Rect>    _drawOptions;
        private readonly Func<ICellData> _create;

        public CellContextAction(string label, Func<ICellData> create,
            float optionsHeight = 0f, Action<Rect> drawOptions = null)
        {
            Label         = label;
            _create       = create;
            OptionsHeight = optionsHeight;
            _drawOptions  = drawOptions;
        }

        public void DrawOptions(Rect rect) => _drawOptions?.Invoke(rect);
        public ICellData Create()          => _create();
    }
}
