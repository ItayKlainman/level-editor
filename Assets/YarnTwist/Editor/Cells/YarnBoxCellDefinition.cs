using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Cells/Box")]
    public sealed class YarnBoxCellDefinition : CellTypeDefinition, ICellContextActions
    {
        [SerializeField] private ColorPaletteAsset _palette;

        private static readonly Color OutlineColor = Color.white;
        private static readonly YarnDirection[] AllDirs =
            { YarnDirection.Up, YarnDirection.Down, YarnDirection.Left, YarnDirection.Right };

        public override float InspectorPreferredHeight => 70f;

        public override ICellData CreateDefault() => new YarnBoxCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            if (data is not YarnBoxCell box) return;

            var color = new Color(0.4f, 0.4f, 0.4f);
            if (_palette != null && _palette.TryGetColor(box.ColorId, out var c)) color = c;
            EditorGUI.DrawRect(rect, color);

            if (box.Hidden)
            {
                GUI.Label(rect, "?", new GUIStyle(EditorStyles.boldLabel)
                    { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = Color.white } });
            }

            if (box.ConnectedDir.HasValue)
                DrawConnectionOutline(rect, box.ConnectedDir.Value);
        }

        public override void DrawInspector(Rect rect, ref ICellData data)
        {
            if (data is not YarnBoxCell box) return;

            float lh         = EditorGUIUtility.singleLineHeight + 2f;
            float swatchAreaH = rect.height - lh - 2f;
            var   swatchRect  = new Rect(rect.x, rect.y, rect.width, swatchAreaH);
            box.ColorId = ColorSwatchDrawer.Draw(swatchRect, _palette, box.ColorId);

            box.Hidden = EditorGUI.ToggleLeft(
                new Rect(rect.x, rect.y + swatchAreaH + 2f, rect.width, lh), "Hidden", box.Hidden);
        }

        // ── White outline showing the connection ─────────────────────────
        // Thin border around the whole box + a thicker bar on the edge facing the
        // partner, so a connected pair reads as a continuous white seam between them.
        private static void DrawConnectionOutline(Rect rect, YarnDirection dir)
        {
            const float thin = 2f;
            const float bar  = 4f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thin), OutlineColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thin, rect.width, thin), OutlineColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thin, rect.height), OutlineColor);
            EditorGUI.DrawRect(new Rect(rect.xMax - thin, rect.y, thin, rect.height), OutlineColor);

            switch (dir)
            {
                case YarnDirection.Up:
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, bar), OutlineColor); break;
                case YarnDirection.Down:
                    EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - bar, rect.width, bar), OutlineColor); break;
                case YarnDirection.Left:
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, bar, rect.height), OutlineColor); break;
                case YarnDirection.Right:
                    EditorGUI.DrawRect(new Rect(rect.xMax - bar, rect.y, bar, rect.height), OutlineColor); break;
            }
        }

        // ── Right-click actions: Connect / Un-connect + Convert ──────────
        public IEnumerable<CellContextAction> GetContextActions(CellActionContext context)
        {
            if (context.Cell is not YarnBoxCell box) yield break;

            var grid = context.Session?.Document?.Grid;
            if (grid == null) yield break;

            int x = context.CellRef.X;
            int y = context.CellRef.Y;

            // ── Palette: add a 3x3 cover here, or edit/remove the one covering this box ──
            var cellRef = context.CellRef;
            var doc     = context.Session.Document;
            if (YarnPalettes.TryPaletteAt(doc, cellRef, out var pal))
            {
                int[] amt = { pal.Amount };
                yield return new CellContextAction(
                    label: "Set Palette Requirement",
                    apply: session => YarnPalettes.SetAmount(session.Document, cellRef, amt[0]),
                    optionsHeight: 22f,
                    drawOptions: r =>
                    {
                        GUI.Label(new Rect(r.x, r.y, 96f, 18f), "Required opens", EditorStyles.miniLabel);
                        amt[0] = Mathf.Max(1, EditorGUI.IntField(
                            new Rect(r.x + 98f, r.y, r.width - 98f, 18f), amt[0]));
                    });
                yield return new CellContextAction(
                    label: "Remove Palette",
                    apply: session => YarnPalettes.Remove(session.Document, cellRef));
            }
            else if (YarnPalettes.CanPlace(grid, cellRef, YarnPalettes.All(doc)))
            {
                yield return new CellContextAction(
                    label: "Add Palette (3×3 here)",
                    apply: session => YarnPalettes.Add(session.Document, cellRef));
            }

            if (box.ConnectedDir.HasValue)
            {
                // Already connected — only offer to break the link (clears both halves).
                yield return new CellContextAction(
                    label: "Un-connect",
                    apply: session => Disconnect(session.Document.Grid, x, y));
                yield break;
            }

            // Unconnected: offer a connect action per direction with a valid free box neighbour.
            foreach (var dir in AllDirs)
            {
                var (nx, ny) = Neighbor(x, y, dir);
                if (!grid.InBounds(nx, ny)) continue;
                if (grid.Get(nx, ny) is not YarnBoxCell neighbor) continue;
                if (neighbor.ConnectedDir.HasValue) continue; // target already connected

                var d  = dir;              // capture per-iteration
                int tx = nx, ty = ny;
                yield return new CellContextAction(
                    label: $"Connect Pair: {d}",
                    apply: session => Connect(session.Document.Grid, x, y, tx, ty, d));
            }

            // Existing convert action (unconnected boxes only — converting a connected
            // box would orphan its partner).
            if (context.Registry.TryGetDefinition("yt.arrowbox", out _))
            {
                var colorId = box.ColorId;
                var arrowDir = new[] { YarnDirection.Right };
                yield return new CellContextAction(
                    label: "→ Convert to Arrow Box",
                    create: () => new YarnArrowBoxCell { ColorId = colorId, Direction = arrowDir[0] },
                    optionsHeight: 22f,
                    drawOptions: rect =>
                    {
                        GUI.Label(new Rect(rect.x, rect.y, 64f, 18f), "Direction", EditorStyles.miniLabel);
                        arrowDir[0] = (YarnDirection)EditorGUI.EnumPopup(
                            new Rect(rect.x + 66f, rect.y, rect.width - 66f, 18f), arrowDir[0]);
                    });
            }
        }

        private static void Connect(GridData<ICellData> grid, int sx, int sy, int tx, int ty, YarnDirection dir)
        {
            if (grid.Get(sx, sy) is YarnBoxCell src && grid.Get(tx, ty) is YarnBoxCell dst)
            {
                src.ConnectedDir = dir;
                dst.ConnectedDir = Opposite(dir);
            }
        }

        private static void Disconnect(GridData<ICellData> grid, int x, int y)
        {
            if (grid.Get(x, y) is not YarnBoxCell src || !src.ConnectedDir.HasValue) return;
            var (px, py) = Neighbor(x, y, src.ConnectedDir.Value);
            if (grid.InBounds(px, py) && grid.Get(px, py) is YarnBoxCell partner && partner.ConnectedDir.HasValue)
                partner.ConnectedDir = null;
            src.ConnectedDir = null;
        }

        private static (int x, int y) Neighbor(int x, int y, YarnDirection d) => d switch
        {
            YarnDirection.Up    => (x, y + 1),
            YarnDirection.Down  => (x, y - 1),
            YarnDirection.Left  => (x - 1, y),
            YarnDirection.Right => (x + 1, y),
            _                   => (x, y),
        };

        private static YarnDirection Opposite(YarnDirection d) => d switch
        {
            YarnDirection.Up    => YarnDirection.Down,
            YarnDirection.Down  => YarnDirection.Up,
            YarnDirection.Left  => YarnDirection.Right,
            YarnDirection.Right => YarnDirection.Left,
            _                   => d,
        };
    }
}
