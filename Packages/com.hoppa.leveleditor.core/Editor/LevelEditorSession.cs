using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class LevelEditorSession : IDisposable
    {
        public GameProfile Profile { get; }
        public LevelDocument Document { get; private set; }
        public CellTypeRegistry CellTypes { get; }
        public ValidationRuleRegistry ValidationRules { get; }
        public ValidationReport LastValidation { get; private set; }

        public ICellTypeDefinition ActiveCellType { get; set; }
        public ICellData BrushTemplate { get; set; }
        public CellRef? SelectedCell { get; set; }
        public GridEditTool ActiveTool { get; set; } = GridEditTool.Paint;
        public HashSet<CellRef> MultiSelection { get; } = new HashSet<CellRef>();

        public void ClearMultiSelection() => MultiSelection.Clear();
        public ICellData CopiedCell { get; private set; }
        public CellRef? CopiedCellRef { get; private set; }
        public bool IsDirty { get; private set; }
        public string FilePath { get; set; }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        private readonly Stack<string> _undoStack = new Stack<string>();
        private readonly Stack<string> _redoStack = new Stack<string>();
        private readonly JsonLevelSerializer _serializer = new JsonLevelSerializer();

        public LevelEditorSession(GameProfile profile, LevelDocument document)
        {
            Profile = profile;
            Document = document;
            CellTypes = profile.BuildRegistry();
            ValidationRules = profile.BuildValidationRegistry();
        }

        public void SetCell(int x, int y, ICellData cell)
        {
            Document.Grid.Set(x, y, cell);
            IsDirty = true;
        }

        public void MarkClean()  => IsDirty = false;
        public void MarkDirty()  => IsDirty = true;

        public void RunValidation()
        {
            var ctx = new ValidationContext(Document, Profile.ColorPalette);
            LastValidation = ValidationRules.RunAll(ctx);
        }

        // Call before any mutation to record the pre-mutation state.
        public void PushUndoSnapshot()
        {
            _undoStack.Push(_serializer.Save(Document, CellTypes));
            _redoStack.Clear();
        }

        public bool Undo()
        {
            if (!CanUndo) return false;
            _redoStack.Push(_serializer.Save(Document, CellTypes));
            Document = _serializer.Load(_undoStack.Pop(), CellTypes);
            IsDirty = true;
            RunValidation();
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo) return false;
            _undoStack.Push(_serializer.Save(Document, CellTypes));
            Document = _serializer.Load(_redoStack.Pop(), CellTypes);
            IsDirty = true;
            RunValidation();
            return true;
        }

        // Copies the currently selected cell into the clipboard.
        public void CopySelectedCell()
        {
            if (!SelectedCell.HasValue) return;
            var cell = Document.Grid.Get(SelectedCell.Value.X, SelectedCell.Value.Y);
            CopiedCell    = cell != null ? CloneCell(cell) : null;
            CopiedCellRef = cell != null ? SelectedCell : (CellRef?)null;
        }

        // Pastes the clipboard cell onto the currently selected position. Returns true on success.
        public bool PasteCopiedCell()
        {
            if (CopiedCell == null || !SelectedCell.HasValue) return false;
            PushUndoSnapshot();
            SetCell(SelectedCell.Value.X, SelectedCell.Value.Y, CloneCell(CopiedCell));
            RunValidation();
            return true;
        }

        // Pastes the clipboard cell onto every cell in MultiSelection. Returns true on success.
        public bool PasteToMultiSelection()
        {
            if (CopiedCell == null || MultiSelection.Count == 0) return false;
            PushUndoSnapshot();
            foreach (var cellRef in MultiSelection)
                SetCell(cellRef.X, cellRef.Y, CloneCell(CopiedCell));
            RunValidation();
            return true;
        }

        // Deep-clones a single cell by round-tripping through the serializer.
        public ICellData CloneCell(ICellData cell)
        {
            if (cell == null) return null;
            var tempDoc = new LevelDocument
            {
                SchemaVersion = "tmp", LevelId = "tmp",
                Grid = new GridData<ICellData>(1, 1)
            };
            tempDoc.Grid.Set(0, 0, cell);
            var loaded = _serializer.Load(_serializer.Save(tempDoc, CellTypes), CellTypes);
            return loaded.Grid.Get(0, 0);
        }

        // Produces a fresh clone of BrushTemplate by round-tripping through the serializer.
        // Falls back to CreateDefault() if template is uninitialised.
        public ICellData CloneBrushTemplate()
        {
            if (BrushTemplate == null || ActiveCellType == null)
                return ActiveCellType?.CreateDefault();

            var tempDoc = new LevelDocument
            {
                SchemaVersion = "tmp", LevelId = "tmp",
                Grid = new GridData<ICellData>(1, 1)
            };
            tempDoc.Grid.Set(0, 0, BrushTemplate);
            var loaded = _serializer.Load(_serializer.Save(tempDoc, CellTypes), CellTypes);
            return loaded.Grid.Get(0, 0);
        }

        public void Dispose() { }

        public static LevelEditorSession CreateEmpty(GameProfile profile)
        {
            var grid     = new GridData<ICellData>(profile.GridWidth, profile.GridHeight);
            int fillIdx  = profile.CellTypes.Count > 1 ? 1 : 0;
            var fillDef  = profile.CellTypes.Count > 0 ? profile.CellTypes[fillIdx] : null;
            if (fillDef != null)
                for (int i = 0; i < grid.Cells.Length; i++)
                    grid.Cells[i] = fillDef.CreateDefault();

            var doc = new LevelDocument
            {
                SchemaVersion = profile.SchemaId + ".v1",
                LevelId       = "level_001",
                DisplayName   = "Untitled",
                Metadata      = new LevelMetadata
                {
                    Author     = Environment.UserName,
                    CreatedAt  = DateTime.UtcNow.ToString("o"),
                    ModifiedAt = DateTime.UtcNow.ToString("o"),
                },
                Grid = grid,
            };
            return new LevelEditorSession(profile, doc);
        }
    }
}
