using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class MultiSelectPanel : IEditorPanel
    {
        private const float HeaderH  = 26f;
        private const float LabelH   = 16f;
        private const float BtnH     = 20f;
        private const float BtnGap   = 3f;
        private const float Pad      = 8f;

        private static readonly Color HeaderBg  = new Color(0.17f, 0.21f, 0.33f);
        private static readonly Color Accent     = new Color(0.30f, 0.65f, 1.00f);
        private static readonly Color LabelColor = new Color(0.55f, 0.68f, 0.85f);
        private static readonly Color DeselBg   = new Color(0.22f, 0.24f, 0.32f);
        private static readonly Color HideBg    = new Color(0.30f, 0.22f, 0.40f);
        private static readonly Color UnhideBg  = new Color(0.22f, 0.32f, 0.24f);

        public void OnGUI(Rect rect, LevelEditorSession session)
        {
            int count = session.MultiSelection.Count;

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, HeaderH), HeaderBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), Accent);
            GUI.Label(new Rect(rect.x + Pad, rect.y + 5f, rect.width - Pad * 2f, HeaderH - 10f),
                $"MULTI-SELECT  ({count} cell{(count == 1 ? "" : "s")})", EditorStyles.boldLabel);

            float x  = rect.x + Pad;
            float y  = rect.y + HeaderH + 4f;
            float cw = rect.width - Pad * 2f;

            var palette = session.Profile?.ColorPalette;

            // ── Color section ────────────────────────────────────────────
            if (palette != null)
            {
                var oldColor = GUI.contentColor;
                GUI.contentColor = LabelColor;
                GUI.Label(new Rect(x, y, cw, LabelH), "Color", EditorStyles.miniLabel);
                GUI.contentColor = oldColor;
                y += LabelH;

                float swatchH = ColorSwatchDrawer.MeasureHeight(palette, cw);
                var   swatchRect = new Rect(x, y, cw, swatchH);

                string pickedColor = ColorSwatchDrawer.Draw(swatchRect, palette, selectedId: null);
                if (pickedColor != null)
                {
                    session.PushUndoSnapshot();
                    foreach (var cellRef in session.MultiSelection)
                    {
                        var cell = session.Document.Grid.Get(cellRef.X, cellRef.Y);
                        if (cell is IColoredCell colored)
                        {
                            colored.ColorId = pickedColor;
                            session.Document.Grid.Set(cellRef.X, cellRef.Y, cell);
                        }
                    }
                    session.MarkDirty();
                    session.RunValidation();
                }
                y += swatchH + 4f;
            }

            // ── Change type section ──────────────────────────────────────
            if (session.Profile?.CellTypes != null && session.Profile.CellTypes.Count > 0)
            {
                var oldColor = GUI.contentColor;
                GUI.contentColor = LabelColor;
                GUI.Label(new Rect(x, y, cw, LabelH), "Change Type", EditorStyles.miniLabel);
                GUI.contentColor = oldColor;
                y += LabelH;

                var types = session.Profile.CellTypes;
                int typeCount   = types.Count;
                float btnW      = Mathf.Floor((cw - BtnGap * (Mathf.Min(typeCount, 3) - 1)) / Mathf.Min(typeCount, 3));
                int   perRow    = Mathf.Max(1, Mathf.FloorToInt((cw + BtnGap) / (btnW + BtnGap)));
                float rowX      = x;
                int   colInRow  = 0;

                for (int i = 0; i < typeCount; i++)
                {
                    var def = types[i];
                    if (GUI.Button(new Rect(rowX, y, btnW, BtnH), def.DisplayName, EditorStyles.miniButton))
                    {
                        session.PushUndoSnapshot();
                        foreach (var cellRef in session.MultiSelection)
                        {
                            var oldCell = session.Document.Grid.Get(cellRef.X, cellRef.Y);
                            var newCell = def.CreateDefault();
                            if (oldCell is IColoredCell oldC && newCell is IColoredCell newC)
                                newC.ColorId = oldC.ColorId;
                            session.Document.Grid.Set(cellRef.X, cellRef.Y, newCell);
                        }
                        session.MarkDirty();
                        session.RunValidation();
                    }

                    colInRow++;
                    if (colInRow >= perRow)
                    {
                        colInRow = 0;
                        rowX  = x;
                        y    += BtnH + BtnGap;
                    }
                    else
                    {
                        rowX += btnW + BtnGap;
                    }
                }
                if (colInRow > 0) y += BtnH + BtnGap;
                y += 2f;
            }

            // ── Hidden toggle ────────────────────────────────────────────
            bool anyHideable = false;
            foreach (var cellRef in session.MultiSelection)
            {
                if (session.Document.Grid.Get(cellRef.X, cellRef.Y) is IHideableCell)
                { anyHideable = true; break; }
            }

            if (anyHideable)
            {
                var oldColor = GUI.contentColor;
                GUI.contentColor = LabelColor;
                GUI.Label(new Rect(x, y, cw, LabelH), "Visibility", EditorStyles.miniLabel);
                GUI.contentColor = oldColor;
                y += LabelH;

                float halfW = Mathf.Floor((cw - BtnGap) * 0.5f);

                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = HideBg;
                if (GUI.Button(new Rect(x, y, halfW, BtnH), "Hide", EditorStyles.miniButton))
                {
                    session.PushUndoSnapshot();
                    foreach (var cellRef in session.MultiSelection)
                    {
                        if (session.Document.Grid.Get(cellRef.X, cellRef.Y) is IHideableCell h)
                        { h.Hidden = true; session.Document.Grid.Set(cellRef.X, cellRef.Y, (ICellData)h); }
                    }
                    session.MarkDirty();
                    session.RunValidation();
                }

                GUI.backgroundColor = UnhideBg;
                if (GUI.Button(new Rect(x + halfW + BtnGap, y, halfW, BtnH), "Unhide", EditorStyles.miniButton))
                {
                    session.PushUndoSnapshot();
                    foreach (var cellRef in session.MultiSelection)
                    {
                        if (session.Document.Grid.Get(cellRef.X, cellRef.Y) is IHideableCell h)
                        { h.Hidden = false; session.Document.Grid.Set(cellRef.X, cellRef.Y, (ICellData)h); }
                    }
                    session.MarkDirty();
                    session.RunValidation();
                }
                GUI.backgroundColor = oldBg;
                y += BtnH + BtnGap + 2f;
            }

            // ── Deselect All ─────────────────────────────────────────────
            var old = GUI.backgroundColor;
            GUI.backgroundColor = DeselBg;
            if (GUI.Button(new Rect(x, y, cw, BtnH), "✕  Deselect All", EditorStyles.miniButton))
                session.ClearMultiSelection();
            GUI.backgroundColor = old;
        }
    }
}
