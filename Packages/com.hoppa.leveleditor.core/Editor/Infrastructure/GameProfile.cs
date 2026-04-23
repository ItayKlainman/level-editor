using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Level Editor/Game Profile", order = 0)]
    public sealed class GameProfile : ScriptableObject
    {
        [SerializeField] private string _schemaId;
        [SerializeField] private ColorPaletteAsset _colorPalette;
        [SerializeField] private int _gridWidth = 5;
        [SerializeField] private int _gridHeight = 7;
        [SerializeField] private List<CellTypeDefinition> _cellTypes = new List<CellTypeDefinition>();
        [SerializeField] private List<ValidationRuleBase> _rules = new List<ValidationRuleBase>();

        public string SchemaId => _schemaId;
        public ColorPaletteAsset ColorPalette => _colorPalette;
        public int GridWidth => _gridWidth;
        public int GridHeight => _gridHeight;
        public IReadOnlyList<CellTypeDefinition> CellTypes => _cellTypes;
        public IReadOnlyList<ValidationRuleBase> Rules => _rules;

        public CellTypeRegistry BuildRegistry()
        {
            var registry = new CellTypeRegistry();
            foreach (var def in _cellTypes)
                if (def != null) registry.Register(def);
            return registry;
        }

        public ValidationRuleRegistry BuildValidationRegistry()
        {
            var registry = new ValidationRuleRegistry();
            foreach (var rule in _rules)
                if (rule != null) registry.Register(rule);
            return registry;
        }
    }
}
