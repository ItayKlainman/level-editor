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

        [Tooltip("Optional importers for foreign level formats (e.g. the game's shipped schema).\nWhen one recognizes a file, Open loads it and Save round-trips through the matching exporter\ninstead of the editor's internal format. Leave empty for editor-native levels only.")]
        [SerializeField] private List<LevelImporterAsset> _importers = new List<LevelImporterAsset>();

        [Tooltip("Optional: assign a TopSectionPanel subclass script to show a game-specific region above the grid.\nExample: SpoolColumnsTopSectionPanel — leave empty for no top section.")]
        [SerializeField] private MonoScript _topSectionScript;

        [Tooltip("Optional: assign a TopSectionPanel subclass script to show a game-specific region BELOW the grid (e.g. a spool queue that lives at the bottom of the game screen).\nThe abstract base type is the same as top section — the slot it renders in is decided by which field you assign it to.")]
        [SerializeField] private MonoScript _bottomSectionScript;

        [Tooltip("Optional: assign a ProfileRightPanel subclass script to render a game-specific section in the RIGHT column (beneath Spool Analysis).\nExample: BusBuddiesDifficultyPanel — leave empty for none.")]
        [SerializeField] private MonoScript _rightPanelScript;

        [Tooltip("Optional: assign a ProfileLeftPanel subclass script to render a game-specific section in the LEFT column (between the cell/brush palette and TOOLS).\nExample: BusBuddiesMoreOptionsPanel — leave empty for none.")]
        [SerializeField] private MonoScript _leftPanelScript;

        [Tooltip("Optional: assign an EditorPanelAsset to enable the ⇅ Order tab in the toolbar.")]
        [SerializeField] private EditorPanelAsset _orderPanel;

        [Tooltip("Optional: assign a LevelGeneratorAsset subclass to enable the Generate toolbar button.\nExample: YarnTwistLevelGenerator")]
        [SerializeField] private LevelGeneratorAsset _levelGenerator;

        [Tooltip("Optional: tuning ScriptableObject for the assigned LevelGenerator. The generator panel's Advanced foldout renders this asset's default inspector.\nExample: YarnTwistGeneratorConfig")]
        [SerializeField] private ScriptableObject _generatorConfig;

        [Tooltip("Optional: assign a LevelAnalyzerAsset subclass to enable the Spool Analysis side panel and report win-path counts for the current document.\nExample: YarnTwistLevelAnalyzer")]
        [SerializeField] private LevelAnalyzerAsset _levelAnalyzer;

        [Tooltip("Optional: assign a LevelCompleterAsset subclass to enable the 'Auto-fill' button in the Spool Analysis panel. Generates parts of the level (e.g. the top section) from the hand-painted grid + a Difficulty knob.\nExample: YarnTwistSpoolAutofiller")]
        [SerializeField] private LevelCompleterAsset _levelCompleter;

        [Tooltip("Optional: assign a CanvasOverlayAsset subclass to draw a game-specific overlay on top of the grid canvas (e.g. multi-cell region annotations).\nExample: YarnPaletteOverlay")]
        [SerializeField] private CanvasOverlayAsset _canvasOverlay;

        [Tooltip("Optional per-profile 'flag paint' tool (null = profile has no flag mechanic).\nWhen set, the TOOLS palette shows a toggle and GridEditTool.Hide is enabled.\nExample: BusBuddiesHiddenPixelPainter")]
        [SerializeField] private CellFlagPainterAsset _flagPainter;

        [Tooltip("Optional per-profile 'region drag' tool (null = profile has no region mechanic).\nWhen set, the TOOLS palette shows a toggle and GridEditTool.Region is enabled; press-drag-release on the grid hands the swept cell rectangle to the tool.\nExample: BusBuddiesPlateRegionTool")]
        [SerializeField] private RegionToolAsset _regionTool;

        [Tooltip("Optional: assign an ImageToGridAsset subclass to enable the 🖼 Image toolbar mode — convert a source image into a level grid quantized to this profile's palette.\nExample: YAKImageToGrid")]
        [SerializeField] private ImageToGridAsset _imageToGrid;

        [Tooltip("Generic per-profile extension data. Used by Layer 2 implementations that need profile-scoped configuration outside the typed fields above. Look up by type via GetExtension<T>().")]
        [SerializeField] private List<ScriptableObject> _extensions = new List<ScriptableObject>();

        [Tooltip("Layout flag: when TRUE the top-section panel (e.g. the spool columns) is rendered BELOW the grid instead of above it, and the grid anchors to the TOP. Use for games whose spool/queue region lives at the bottom of the game screen (e.g. Color Wool Sort).\nDefault FALSE keeps the classic top-section → grid → bottom-section layout (YarnTwist / YAK unchanged).")]
        [SerializeField] private bool _spoolsBelowGrid;

        public string SchemaId => _schemaId;
        public ColorPaletteAsset ColorPalette => _colorPalette;
        public int GridWidth => _gridWidth;
        public int GridHeight => _gridHeight;
        public IReadOnlyList<CellTypeDefinition> CellTypes => _cellTypes;
        public IReadOnlyList<ValidationRuleBase> Rules => _rules;
        public IReadOnlyList<LevelExporterAsset> Exporters => _exporters;
        public IReadOnlyList<LevelImporterAsset> Importers => _importers;
        public IEditorPanel OrderPanel => _orderPanel;
        public ILevelGenerator LevelGenerator => _levelGenerator;
        public ScriptableObject GeneratorConfig => _generatorConfig;
        public LevelAnalyzerAsset  LevelAnalyzer  => _levelAnalyzer;
        public LevelCompleterAsset LevelCompleter => _levelCompleter;
        public CanvasOverlayAsset  CanvasOverlay  => _canvasOverlay;
        public ImageToGridAsset    ImageToGrid    => _imageToGrid;

        // Optional per-profile "flag paint" tool (null = profile has no flag mechanic).
        // When set, the TOOLS palette shows a toggle and GridEditTool.Hide is enabled.
        public CellFlagPainterAsset FlagPainter => _flagPainter;

        // Optional per-profile "region drag" tool (null = profile has no region mechanic).
        // When set, the TOOLS palette shows a toggle and GridEditTool.Region is enabled.
        public RegionToolAsset RegionTool => _regionTool;

        // Layout flag: render the top-section panel below the grid (grid top-anchored)
        // instead of the classic above-the-grid layout. Default false.
        public bool SpoolsBelowGrid => _spoolsBelowGrid;

        // Generic typed-lookup over the _extensions list. Returns the first
        // ScriptableObject that is assignable to T, or null. Use for Layer 2
        // configuration data that should be attached to the profile itself
        // (not to a Layer 2 asset).
        public T GetExtension<T>() where T : ScriptableObject
        {
            foreach (var e in _extensions)
                if (e is T t) return t;
            return null;
        }

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

        public TopSectionPanel CreateTopSection() => InstantiateSection(_topSectionScript);
        public TopSectionPanel CreateBottomSection() => InstantiateSection(_bottomSectionScript);

        // Instantiate the profile's optional right-column panel (a ProfileRightPanel
        // subclass). Returns null when no valid script is assigned.
        public ProfileRightPanel CreateRightPanel()
        {
            if (_rightPanelScript == null) return null;
            var type = _rightPanelScript.GetClass();
            if (type == null || !typeof(ProfileRightPanel).IsAssignableFrom(type)) return null;
            try   { return (ProfileRightPanel)Activator.CreateInstance(type); }
            catch { return null; }
        }

        // Instantiate the profile's optional left-column panel (a ProfileLeftPanel
        // subclass). Returns null when no valid script is assigned.
        public ProfileLeftPanel CreateLeftPanel()
        {
            if (_leftPanelScript == null) return null;
            var type = _leftPanelScript.GetClass();
            if (type == null || !typeof(ProfileLeftPanel).IsAssignableFrom(type)) return null;
            try   { return (ProfileLeftPanel)Activator.CreateInstance(type); }
            catch { return null; }
        }

        private static TopSectionPanel InstantiateSection(MonoScript script)
        {
            if (script == null) return new EmptyTopSectionPanel();
            var type = script.GetClass();
            if (type == null || !typeof(TopSectionPanel).IsAssignableFrom(type))
                return new EmptyTopSectionPanel();
            try   { return (TopSectionPanel)Activator.CreateInstance(type); }
            catch { return new EmptyTopSectionPanel(); }
        }
    }
}
