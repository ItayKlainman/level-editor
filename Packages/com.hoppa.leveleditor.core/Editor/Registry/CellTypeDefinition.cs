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
        [SerializeField] private string _typeId;
        [SerializeField] private string _displayName;
        [SerializeField] private string _paletteGroup;
        [SerializeField] private Texture2D _icon;

        public string TypeId => _typeId;
        public string DisplayName => _displayName;
        public string PaletteGroup => _paletteGroup;
        public Texture2D Icon => _icon;

        public abstract ICellData CreateDefault();
        public abstract void DrawCell(Rect cellRect, ICellData data);
        public abstract void DrawInspector(Rect rect, ref ICellData data);
    }
}
