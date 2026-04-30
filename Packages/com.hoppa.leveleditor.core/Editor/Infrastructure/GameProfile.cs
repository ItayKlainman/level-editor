using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Level Editor/Game Profile", order = 0)]
    public sealed class GameProfile : ScriptableObject
    {
        [Tooltip("Unique schema ID for this game. Stamped into every saved level as schemaVersion.\nExample: 'demo'")]
        [SerializeField] private string _schemaId;

        [Tooltip("The color palette for this game. Every colorId referenced in levels must exist here.\nExample: DemoPalette")]
        [SerializeField] private ColorPaletteAsset _colorPalette;

        [Tooltip("Width of the level grid in cells.\nExample: 5")]
        [SerializeField] private int _gridWidth = 5;

        [Tooltip("Height of the level grid in cells.\nExample: 7")]
        [SerializeField] private int _gridHeight = 7;

        [Tooltip("All registered cell types. The FIRST entry must be the 'empty' type used for erase and fill.\nExample: [DemoEmptyCellDef, DemoBoxCellDef]")]
        [SerializeField] private List<CellTypeDefinition> _cellTypes = new List<CellTypeDefinition>();

        [Tooltip("Validation rules run on every level. Results appear in the Validation panel.\nExample: PaletteColorsExistRule")]
        [SerializeField] private List<ValidationRuleBase> _rules = new List<ValidationRuleBase>();

        [Tooltip("Exporters that run on Save and on the explicit 'Export ▸' toolbar button.\nTo integrate a new game: create a LevelExporterAsset subclass for that game, create an asset, and assign it here.\nExample: YarnMasterLevelExporter")]
        [SerializeField] private List<LevelExporterAsset> _exporters = new List<LevelExporterAsset>();

        [Tooltip("Optional: assign a TopSectionPanel subclass script to show a game-specific region above the grid.\nExample: SpoolColumnsTopSectionPanel — leave empty for no top section.")]
        [SerializeField] private MonoScript _topSectionScript;

        public string SchemaId => _schemaId;
        public ColorPaletteAsset ColorPalette => _colorPalette;
        public int GridWidth => _gridWidth;
        public int GridHeight => _gridHeight;
        public IReadOnlyList<CellTypeDefinition> CellTypes => _cellTypes;
        public IReadOnlyList<ValidationRuleBase> Rules => _rules;
        public IReadOnlyList<LevelExporterAsset> Exporters => _exporters;

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

        public TopSectionPanel CreateTopSection()
        {
            if (_topSectionScript == null) return new EmptyTopSectionPanel();
            var type = _topSectionScript.GetClass();
            if (type == null || !typeof(TopSectionPanel).IsAssignableFrom(type))
                return new EmptyTopSectionPanel();
            try   { return (TopSectionPanel)Activator.CreateInstance(type); }
            catch { return new EmptyTopSectionPanel(); }
        }
    }
}
