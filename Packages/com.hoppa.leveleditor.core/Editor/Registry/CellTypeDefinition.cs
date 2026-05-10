using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public interface ICellTypeDefinition
    {
        string TypeId { get; }
        string DisplayName { get; }
        string PaletteGroup { get; }
        Texture2D Icon { get; }
        ICellData CreateDefault();
        void DrawCell(Rect cellRect, ICellData data);
        void DrawInspector(Rect rect, ref ICellData data);
    }

    public abstract class CellTypeDefinition : ScriptableObject, ICellTypeDefinition
    {
        [Tooltip("Unique string ID used in JSON serialization. Must be stable once levels are saved.\nExample: 'demo_box'")]
        [SerializeField] private string _typeId;

        [Tooltip("Human-readable name shown in the palette.\nExample: 'Box'")]
        [SerializeField] private string _displayName;

        [Tooltip("Groups this type under a header in the palette. Leave empty for 'General'.\nExample: 'Tiles'")]
        [SerializeField] private string _paletteGroup;

        [Tooltip("Optional icon shown in the palette. If null, a live cell preview is drawn instead.")]
        [SerializeField] private Texture2D _icon;

        public string TypeId => _typeId;
        public string DisplayName => _displayName;
        public string PaletteGroup => _paletteGroup;
        public Texture2D Icon => _icon;

        // Height the cell popup inspector should allocate for DrawInspector. Override when content needs more room.
        public virtual float InspectorPreferredHeight => 80f;

        public abstract ICellData CreateDefault();
        public abstract void DrawCell(Rect cellRect, ICellData data);
        public abstract void DrawInspector(Rect rect, ref ICellData data);

        // Called immediately after this cell type is painted onto the grid.
        // Override to apply automatic side effects (e.g. clearing a neighbour cell).
        public virtual void OnAfterPlaced(int x, int y, LevelEditorSession session) { }

        // Called immediately after a cell of this type is modified via the inspector.
        // Override to react to property changes (e.g. swapping emptied neighbours on direction change).
        public virtual void OnAfterInspectorChanged(int x, int y, LevelEditorSession session) { }
    }
}
