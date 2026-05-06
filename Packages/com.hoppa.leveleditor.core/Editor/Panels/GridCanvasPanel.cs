using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class GridCanvasPanel : IEditorPanel
    {
        private const float CellSize = 32f;
        private const float CellGap  = 2f;
        private const float CellStep = CellSize + CellGap;
        private const float Margin   = 32f;

        private Vector2  _scroll;
        private bool     _isDragging;
        private CellRef? _hoverCell;
        private CellRef? _moveSource;
        private float    _gridOffsetX;
        private float    _gridOffsetY;

        public CellRef? HoverCell => _hoverCell;

        private static readonly Color OuterBg          = new Color(0.10f, 0.11f, 0.13f);
        private static readonly Color GridShadow       = new Color(0.00f, 0.00f, 0.00f, 0.55f);
        private static readonly Color GridBorder       = new Color(0.12f, 0.13f, 0.16f);
        private static readonly Color CellBg           = new Color(0.24f, 0.26f, 0.30f);
        private static readonly Color HoverColor       = new Color(1.00f, 1.00f, 1.00f, 0.13f);
        private static readonly Color SelectOutline    = new Color(1.00f, 1.00f, 1.00f, 0.90f);
        private static readonly Color MultiSelOutline  = new Color(0.30f, 0.65f, 1.00f, 0.90f);
        private static readonly Color MoveSourceColor  = new Color(0.30f, 1.00f, 0.40f, 0.90f);
        private static readonly Color MoveTargetColor  = new Color(0.30f, 1.00f, 0.40f, 0.30f);
        private static readonly Color CopiedOutline    = new Color(1.00f, 0.85f, 0.20f, 0.85f);

        public void OnGUI(Rect rect, LevelEditorSession session)
        {
            if (session?.Document?.Grid == null)
            {
                EditorGUI.DrawRect(rect, OuterBg);
                GUI.Label(rect, "No level loaded.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var grid   = session.Document.Grid;
            float totalW = grid.Width  * CellStep + CellGap;
            float totalH = grid.Height * CellStep + CellGap;

            float viewW = Mathf.Max(totalW + Margin * 2f, rect.width);
            float viewH = Mathf.Max(totalH + Margin * 2f, rect.height);
            var viewRect = new Rect(0f, 0f, viewW, viewH);

            _scroll = GUI.BeginScrollView(rect, _scroll, viewRect,
                alwaysShowHorizontal: false, alwaysShowVertical: false);

            EditorGUI.DrawRect(viewRect, OuterBg);

            _gridOffsetX = Mathf.Floor((viewW - totalW) * 0.5f);
            _gridOffsetY = Mathf.Floor((viewH - totalH) * 0.5f);

            EditorGUI.DrawRect(
                new Rect(_gridOffsetX - 5f, _gridOffsetY - 5f, totalW + 10f, totalH + 10f),
                GridShadow);
            EditorGUI.DrawRect(
                new Rect(_gridOffsetX, _gridOffsetY, totalW, totalH),
                GridBorder);

            DrawCells(grid, session);
            HandleEvents(rect, grid, session);

            GUI.EndScrollView();
        }

        private void DrawCells(GridData<ICellData> grid, LevelEditorSession session)
        {
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width;  x++)
            {
                var cell     = grid.Get(x, y);
                var cellRef  = new CellRef(x, y);
                var cellRect = CellRect(x, y, grid.Height, _gridOffsetX, _gridOffsetY);

                EditorGUI.DrawRect(cellRect, CellBg);

                if (cell != null && session.CellTypes.TryGetDefinition(cell.CellTypeId, out var def))
                    def.DrawCell(cellRect, cell);
                else if (cell != null)
                    DrawFallback(cellRect, cell);

                if (_hoverCell == cellRef)
                    EditorGUI.DrawRect(cellRect, HoverColor);

                // Move source: green fill + outline
                if (session.ActiveTool == GridEditTool.Move && _moveSource == cellRef)
                    DrawCellOutline(cellRect, MoveSourceColor, 2f);

                // Move target highlight (hover when source is picked)
                if (session.ActiveTool == GridEditTool.Move && _moveSource.HasValue
                    && _moveSource.Value != cellRef && _hoverCell == cellRef)
                    EditorGUI.DrawRect(cellRect, MoveTargetColor);

                if (session.ActiveTool == GridEditTool.Select
                    && session.CopiedCellRef.HasValue && session.CopiedCellRef.Value == cellRef)
                    DrawCellOutline(cellRect, CopiedOutline, 2f);

                if (session.MultiSelection.Contains(cellRef))
                    DrawCellOutline(cellRect, MultiSelOutline, 2f);

                if (session.SelectedCell == cellRef && !session.MultiSelection.Contains(cellRef))
                    DrawCellOutline(cellRect, SelectOutline, 2f);
            }
        }

        private static void DrawCellOutline(Rect r, Color c, float t = 1f)
        {
            EditorGUI.DrawRect(new Rect(r.x,        r.y,        r.width, t),  c);
            EditorGUI.DrawRect(new Rect(r.x,        r.yMax - t, r.width, t),  c);
            EditorGUI.DrawRect(new Rect(r.x,        r.y,        t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y,        t, r.height), c);
        }

        private static void DrawFallback(Rect rect, ICellData cell)
        {
            var id = cell.CellTypeId;
            GUI.Label(rect, id.Length > 2 ? id.Substring(0, 2) : id, EditorStyles.miniLabel);
        }

        private void HandleEvents(Rect scrollViewRect, GridData<ICellData> grid, LevelEditorSession session)
        {
            var e           = Event.current;
            var mouseInView = new Vector2(e.mousePosition.x + _scroll.x, e.mousePosition.y + _scroll.y);
            _hoverCell = ScreenToCell(mouseInView, grid, _gridOffsetX, _gridOffsetY);

            bool ctrl = e.control || e.command;
            var  inBounds = new Rect(0f, 0f, scrollViewRect.width, scrollViewRect.height);

            switch (e.type)
            {
                case EventType.KeyDown when ctrl && !e.shift && e.keyCode == KeyCode.Z:
                    if (session.Undo()) GUI.changed = true;
                    e.Use();
                    break;

                case EventType.KeyDown when ctrl && (e.keyCode == KeyCode.Y || (e.shift && e.keyCode == KeyCode.Z)):
                    if (session.Redo()) GUI.changed = true;
                    e.Use();
                    break;

                case EventType.KeyDown when ctrl && e.keyCode == KeyCode.C
                    && session.ActiveTool == GridEditTool.Select
                    && session.SelectedCell.HasValue:
                    session.CopySelectedCell();
                    GUI.changed = true;
                    e.Use();
                    break;

                case EventType.KeyDown when ctrl && e.keyCode == KeyCode.V
                    && session.ActiveTool == GridEditTool.Select
                    && session.CopiedCell != null
                    && session.SelectedCell.HasValue:
                    if (session.PasteCopiedCell()) GUI.changed = true;
                    e.Use();
                    break;

                case EventType.MouseDown when inBounds.Contains(e.mousePosition) && e.button == 1:
                    // Right-click: show context popup for the hovered cell
                    if (_hoverCell.HasValue)
                    {
                        session.PushUndoSnapshot();
                        PopupWindow.Show(new Rect(e.mousePosition.x, e.mousePosition.y, 1f, 1f),
                            new GridCellPopup(session, _hoverCell.Value));
                    }
                    GUI.changed = true;
                    e.Use();
                    break;

                case EventType.MouseDown when inBounds.Contains(e.mousePosition) && e.button == 0:
                    if (ctrl && _hoverCell.HasValue)
                    {
                        // Ctrl+click: toggle this cell in the multi-selection, no painting
                        if (!session.MultiSelection.Add(_hoverCell.Value))
                            session.MultiSelection.Remove(_hoverCell.Value);
                        session.SelectedCell = _hoverCell;
                    }
                    else
                    {
                        session.ClearMultiSelection();
                        session.SelectedCell = _hoverCell;
                        switch (session.ActiveTool)
                        {
                            case GridEditTool.Paint:
                                session.PushUndoSnapshot();
                                _isDragging = true;
                                PlaceAt(session, _hoverCell);
                                break;
                            case GridEditTool.Select:
                                _isDragging = true;
                                break;
                            case GridEditTool.Delete:
                                session.PushUndoSnapshot();
                                _isDragging = true;
                                if (_hoverCell.HasValue) EraseAt(session, _hoverCell.Value);
                                break;
                            case GridEditTool.Move:
                                HandleMoveTool(session, _hoverCell);
                                break;
                        }
                    }
                    GUI.changed = true;
                    e.Use();
                    break;

                case EventType.MouseDrag when _isDragging:
                    switch (session.ActiveTool)
                    {
                        case GridEditTool.Paint:
                            PlaceAt(session, _hoverCell);
                            break;
                        case GridEditTool.Delete:
                            if (_hoverCell.HasValue) EraseAt(session, _hoverCell.Value);
                            break;
                        case GridEditTool.Select:
                            session.SelectedCell = _hoverCell;
                            break;
                    }
                    GUI.changed = true;
                    e.Use();
                    break;

                case EventType.MouseUp:
                    _isDragging = false;
                    session.RunValidation();
                    e.Use();
                    break;

                case EventType.KeyDown when e.keyCode == KeyCode.Escape:
                    if (session.MultiSelection.Count > 0)
                    {
                        session.ClearMultiSelection();
                        GUI.changed = true;
                        e.Use();
                    }
                    else if (session.ActiveTool == GridEditTool.Move && _moveSource.HasValue)
                    {
                        _moveSource = null;
                        GUI.changed = true;
                        e.Use();
                    }
                    break;

                case EventType.KeyDown when e.keyCode == KeyCode.Delete && session.SelectedCell.HasValue:
                    session.PushUndoSnapshot();
                    EraseAt(session, session.SelectedCell.Value);
                    session.RunValidation();
                    GUI.changed = true;
                    e.Use();
                    break;
            }
        }

        private void HandleMoveTool(LevelEditorSession session, CellRef? cell)
        {
            if (!cell.HasValue) return;
            if (_moveSource == null)
            {
                _moveSource = cell;
                return;
            }
            if (_moveSource.Value == cell.Value)
            {
                _moveSource = null;
                return;
            }
            // Swap the two cells
            session.PushUndoSnapshot();
            var a = session.Document.Grid.Get(_moveSource.Value.X, _moveSource.Value.Y);
            var b = session.Document.Grid.Get(cell.Value.X, cell.Value.Y);
            session.Document.Grid.Set(_moveSource.Value.X, _moveSource.Value.Y, b);
            session.Document.Grid.Set(cell.Value.X, cell.Value.Y, a);
            session.MarkDirty();
            session.RunValidation();
            _moveSource = null;
        }

        private static void PlaceAt(LevelEditorSession session, CellRef? cellRef)
        {
            if (session.ActiveCellType == null) return;
            if (!cellRef.HasValue) return;
            if (!session.Document.Grid.InBounds(cellRef.Value.X, cellRef.Value.Y)) return;
            session.SetCell(cellRef.Value.X, cellRef.Value.Y, session.CloneBrushTemplate());
        }

        private static void EraseAt(LevelEditorSession session, CellRef cellRef)
        {
            if (!session.Document.Grid.InBounds(cellRef.X, cellRef.Y)) return;
            var emptyDef = session.Profile.CellTypes.Count > 0 ? session.Profile.CellTypes[0] : null;
            session.SetCell(cellRef.X, cellRef.Y, emptyDef?.CreateDefault());
        }

        private static CellRef? ScreenToCell(Vector2 mousePos, GridData<ICellData> grid, float offsetX, float offsetY)
        {
            float localX = mousePos.x - offsetX;
            float localY = mousePos.y - offsetY;
            if (localX < 0f || localY < 0f) return null;
            int x          = Mathf.FloorToInt(localX / CellStep);
            int displayRow = Mathf.FloorToInt(localY / CellStep);
            int y          = grid.Height - 1 - displayRow;
            return grid.InBounds(x, y) ? new CellRef(x, y) : (CellRef?)null;
        }

        private static Rect CellRect(int x, int y, int gridHeight, float offsetX, float offsetY)
        {
            int displayRow = gridHeight - 1 - y;
            return new Rect(
                offsetX + CellGap + x * CellStep,
                offsetY + CellGap + displayRow * CellStep,
                CellSize, CellSize);
        }
    }
}
