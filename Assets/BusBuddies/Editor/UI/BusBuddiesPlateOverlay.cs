using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Draws each rectangular Plate on the grid canvas: a coloured outline + light tint
    // over its covered cells, with the PixelAmount number centred on the rect. The
    // pixels stay visible in the editor (the designer needs to edit them); only the
    // game hides them behind the plate. Adapted from YarnPaletteOverlay.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Plate Overlay")]
    public sealed class BusBuddiesPlateOverlay : CanvasOverlayAsset
    {
        private static readonly Color Outline  = new Color(0.25f, 0.70f, 0.95f, 1.00f);
        private static readonly Color Tint     = new Color(0.25f, 0.70f, 0.95f, 0.20f);
        private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.55f);

        public override void DrawOverlay(LevelEditorSession session, System.Func<CellRef, Rect> cellRect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var doc = session?.Document;
            if (doc?.Grid == null) return;
            var grid = doc.Grid;

            foreach (var p in BusBuddiesPlateConfigs.All(doc))
            {
                Rect bounds = default;
                bool has = false;
                foreach (var c in BusBuddiesPlateConfigs.CoveredCells(p))
                {
                    if (!grid.InBounds(c.X, c.Y)) continue;
                    var cr = cellRect(c);
                    EditorGUI.DrawRect(cr, Tint);
                    bounds = has ? Encapsulate(bounds, cr) : cr;
                    has = true;
                }
                if (!has) continue;

                DrawOutline(bounds, Outline, 2f);

                // PixelAmount over a dark backdrop for legibility, centred on the rect.
                var label = new Rect(bounds.x, bounds.center.y - 10f, bounds.width, 20f);
                EditorGUI.DrawRect(label, Backdrop);
                GUI.Label(label, p.Amount.ToString(), new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize  = 14,
                    normal    = { textColor = Color.white },
                });
            }
        }

        private static Rect Encapsulate(Rect a, Rect b)
        {
            float xMin = Mathf.Min(a.xMin, b.xMin), yMin = Mathf.Min(a.yMin, b.yMin);
            float xMax = Mathf.Max(a.xMax, b.xMax), yMax = Mathf.Max(a.yMax, b.yMax);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private static void DrawOutline(Rect r, Color c, float t)
        {
            EditorGUI.DrawRect(new Rect(r.x,        r.y,        r.width, t),  c);
            EditorGUI.DrawRect(new Rect(r.x,        r.yMax - t, r.width, t),  c);
            EditorGUI.DrawRect(new Rect(r.x,        r.y,        t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y,        t, r.height), c);
        }
    }
}
