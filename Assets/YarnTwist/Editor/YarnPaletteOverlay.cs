using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Draws each Palette on the grid canvas: a red outline + light tint over its 3x3
    // covered cells, with the requirement-amount number on the center. The boxes stay
    // visible in the editor (the designer needs to edit them); only the game hides them.
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Palette Overlay")]
    public sealed class YarnPaletteOverlay : CanvasOverlayAsset
    {
        private static readonly Color Outline = new Color(0.95f, 0.20f, 0.20f, 1.00f);
        private static readonly Color Tint    = new Color(0.95f, 0.20f, 0.20f, 0.18f);
        private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.45f);

        public override void DrawOverlay(LevelEditorSession session, System.Func<CellRef, Rect> cellRect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var doc = session?.Document;
            if (doc == null) return;

            foreach (var p in YarnPalettes.All(doc))
            {
                Rect bounds = default;
                bool has = false;
                foreach (var c in YarnPalettes.CoveredCells(p.Center))
                {
                    var cr = cellRect(c);
                    EditorGUI.DrawRect(cr, Tint);
                    bounds = has ? Encapsulate(bounds, cr) : cr;
                    has = true;
                }
                if (!has) continue;

                DrawOutline(bounds, Outline, 2f);

                // Requirement amount on the center cell, over a dark backdrop for legibility.
                var center = cellRect(p.Center);
                EditorGUI.DrawRect(new Rect(center.x, center.center.y - 9f, center.width, 18f), Backdrop);
                GUI.Label(center, p.Amount.ToString(), new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize  = 13,
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
