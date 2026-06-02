using System;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Abstract ScriptableObject base that lets a Layer-2 game draw a custom overlay
    // on top of the grid canvas — e.g. multi-cell region annotations that no single
    // cell could render on its own. Assign a subclass to GameProfile.CanvasOverlay.
    //
    // DrawOverlay is called by GridCanvasPanel after all cells are drawn, INSIDE the
    // canvas scroll view, so it draws in the same coordinate space as the cells. The
    // cellRect function maps a CellRef to its on-canvas Rect. Guard your drawing to
    // the Repaint event.
    public abstract class CanvasOverlayAsset : ScriptableObject
    {
        public abstract void DrawOverlay(LevelEditorSession session, Func<CellRef, Rect> cellRect);
    }
}
