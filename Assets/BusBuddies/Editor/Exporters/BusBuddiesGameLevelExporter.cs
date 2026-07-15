using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Exports a level to the REAL Bus Buddies game schema (Eliran's LevelConfig),
    // ONE FILE PER LEVEL — the game loads each via
    //   ConfigManager.LoadLocalConfig<LevelConfig>("Levels/level_<N>")
    //   → Resources.Load<TextAsset>("Configs/Levels/level_<N>") + JsonConvert.
    //
    // File shape (PascalCase, Newtonsoft, enums as int ordinals):
    //   {
    //     "SlotsAmount": <int>,          // active-row slots (editor "conveyorCount")
    //     "Width": <int>, "Height": <int>,
    //     "BusColumnConfigs": [ { "BusConfigs": [ { "ColorType": <int>, "Capacity": <int>, "BusType": 1? } ] } ],
    //     "PixelColors": [ <BUBColorType ordinal>, ... ]   // index = y*Width + x, y=0 bottom row
    //   }
    // BusType is written only when Hidden (== 1); omitted otherwise → deserializes to None.
    //
    // Distinct from BusBuddiesLevelExporter (which writes the older single-file
    // master {"LevelConfigs":{...}} YAK-shaped schema). This one matches the game's
    // current LevelConfig class + its per-file Resources/Configs/Levels layout.
    //
    // Color map mirrors the game's BUBColorType enum (identical to the YAK palette):
    // Blue=1 … Red=9 … Black=17 … BrownVeryDark=36. Lookup is case-insensitive, so it
    // accepts both YAK PascalCase ids ("Red") and BB lowercase ids ("red"); unmapped → 0 (None).
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Game Level Exporter (per-file)")]
    public sealed class BusBuddiesGameLevelExporter : LevelExporterAsset
    {
        [Tooltip("Output DIRECTORY for per-level files (level_<N>.json). Project-relative (against Application.dataPath/..) or absolute. Copy the results into the game's Assets/_BUB/Resources/Configs/Levels.")]
        [SerializeField] private string _outputDir = "StagingExports/BusBuddies/Levels";

        [Tooltip("Active-row slot count written as SlotsAmount when the level doesn't specify one (GameData[\"conveyorCount\"]).")]
        [SerializeField] private int _defaultSlots = 5;

        public override string Name => "BusBuddiesGameLevel";
        public string OutputDir => ResolveDir();

        // BUBColorType (game enum) → int ordinal. Keyed by the enum's own names; the
        // lookup is case-insensitive so YAK PascalCase and BB lowercase ids both resolve.
        private static readonly Dictionary<string, int> ColorMap =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "None", 0 }, { "Blue", 1 }, { "Cyan", 2 }, { "Yellow", 3 }, { "Green", 4 },
            { "Magenta", 5 }, { "Orange", 6 }, { "Pink", 7 }, { "Purple", 8 }, { "Red", 9 },
            { "BlueDark", 10 }, { "GreenLime", 11 }, { "Turquoise", 12 }, { "PurpleBright", 13 },
            { "White", 14 }, { "Grey", 15 }, { "DarkGrey", 16 }, { "Black", 17 }, { "Brown", 18 },
            { "BrownDark", 19 }, { "BrownLight", 20 }, { "Skin", 21 }, { "GreyLight", 22 },
            { "GreyDark", 23 }, { "BlueOcean", 24 }, { "BlueRoyal", 25 }, { "BlueSky", 26 },
            { "GreenDark", 27 }, { "GreenGrass", 28 }, { "Gold", 29 }, { "OrangeRed", 30 },
            { "OrangeLight", 31 }, { "PurpleDark", 32 }, { "TurquoiseLight", 33 }, { "PinkDark", 34 },
            { "YellowPale", 35 }, { "BrownVeryDark", 36 },
        };

        private static int ColorToInt(string colorId)
            => !string.IsNullOrEmpty(colorId) && ColorMap.TryGetValue(colorId, out var v) ? v : 0;

        // Reverse of ColorMap (ordinal → canonical PascalCase BUBColorType name),
        // built once from the SAME dictionary so importer/exporter can never drift.
        private static readonly Dictionary<int, string> InverseColorMap = BuildInverse();

        private static Dictionary<int, string> BuildInverse()
        {
            var inv = new Dictionary<int, string>();
            foreach (var kv in ColorMap) inv[kv.Value] = kv.Key; // distinct ordinals → 1:1
            return inv;
        }

        // Public name↔ordinal accessors so the round-trip importer reuses this map
        // rather than hand-rolling a second one. colorId lookup is case-insensitive.
        public static int ColorIdToOrdinal(string colorId) => ColorToInt(colorId);

        // ordinal → canonical BUBColorType name ("Blue", "BrownVeryDark", …). Returns
        // false for an unmapped ordinal. Ordinal 0 maps to "None". Callers that want
        // the palette id lowercase it (palette ids are the lowercased enum names).
        public static bool TryOrdinalToColorName(int ordinal, out string colorName)
            => InverseColorMap.TryGetValue(ordinal, out colorName);

        public void SetTestDependencies(string outputDir, int defaultSlots)
        {
            _outputDir = outputDir;
            _defaultSlots = defaultSlots;
        }

        private string ResolveDir()
        {
            if (string.IsNullOrEmpty(_outputDir)) return _outputDir;
            if (Path.IsPathRooted(_outputDir)) return _outputDir;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", _outputDir)).Replace('\\', '/');
        }

        public override bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath)
        {
            string dir = ResolveDir();
            if (string.IsNullOrEmpty(dir))
            {
                Debug.LogWarning("[BusBuddiesGameLevelExporter] Output directory is not set.");
                return false;
            }
            if (string.IsNullOrEmpty(jsonFilePath))
            {
                Debug.LogWarning("[BusBuddiesGameLevelExporter] No file path — save the level before exporting.");
                return false;
            }
            var grid = document.Grid;
            if (grid == null)
            {
                Debug.LogWarning("[BusBuddiesGameLevelExporter] Document has no grid.");
                return false;
            }

            // level_<N>.json — N is the last integer in the source filename.
            string fileNameNoExt = Path.GetFileNameWithoutExtension(jsonFilePath);
            var matches = Regex.Matches(fileNameNoExt, @"\d+");
            if (matches.Count == 0)
            {
                Debug.LogWarning($"[BusBuddiesGameLevelExporter] No integer level number in filename '{fileNameNoExt}'.");
                return false;
            }
            int levelNum = int.Parse(matches[matches.Count - 1].Value);

            int slots = document.GameData?["conveyorCount"]?.Value<int>() ?? _defaultSlots;

            var config = new JObject
            {
                ["SlotsAmount"] = slots,
                ["Width"] = grid.Width,
                ["Height"] = grid.Height,
                ["BusColumnConfigs"] = BuildBusColumnConfigs(document.TopSection),
                ["PixelColors"] = BuildPixelColors(grid),
                ["HiddenPixels"] = BuildHiddenPixels(grid),
            };

            Directory.CreateDirectory(dir);
            string outPath = Path.Combine(dir, "level_" + levelNum + ".json");
            File.WriteAllText(outPath, config.ToString(Formatting.Indented));
            return true;
        }

        // BusQueueData.Columns → BusColumnConfigs[ BusConfigs[ {ColorType, Capacity, BusType?} ] ].
        private static JArray BuildBusColumnConfigs(JObject topSection)
        {
            var columns = new JArray();
            BusQueueData top = null;
            if (topSection != null)
            {
                try { top = topSection.ToObject<BusQueueData>(); }
                catch { top = null; }
            }
            if (top?.Columns == null) return columns;

            foreach (var col in top.Columns)
            {
                var busConfigs = new JArray();
                if (col?.Buses != null)
                {
                    foreach (var bus in col.Buses)
                    {
                        var entry = new JObject
                        {
                            ["ColorType"] = ColorToInt(bus.ColorId ?? string.Empty),
                            ["Capacity"] = bus.Capacity,
                        };
                        if (bus.Hidden) entry["BusType"] = 1; // BusType.Hidden; omitted → None
                        busConfigs.Add(entry);
                    }
                }
                columns.Add(new JObject { ["BusConfigs"] = busConfigs });
            }
            return columns;
        }

        // Sparse hidden-pixel indices. MIRRORS the game's BUBPixelService, which tests
        // HiddenPixels membership against `position = x * width + y` — DELIBERATELY the
        // transpose of PixelColors' `y * width + x`. BB grids are square, so a hidden cell
        // at (x,y) maps to x*Width+y and the game conceals exactly that cell.
        public static JArray BuildHiddenPixels(GridData<ICellData> grid)
        {
            var array = new JArray();
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width;  x++)
            {
                if (grid.Get(x, y) is BBPixelCell p && p.Hidden)
                    array.Add(x * grid.Width + y);
            }
            return array;
        }

        // Flat BUBColorType ordinals, index = y*Width + x (y=0 bottom row) — matching
        // BUBPixelService's `index = y * width + x`. The grid's Cells[] is already in this
        // order. Any colored cell contributes its color; empty/unmapped → 0 (None).
        private static JArray BuildPixelColors(GridData<ICellData> grid)
        {
            var array = new JArray();
            int total = grid.Width * grid.Height;
            for (int i = 0; i < total; i++)
            {
                var cell = grid.Cells[i];
                int value = 0;
                if (cell is IColoredCell colored && !string.IsNullOrEmpty(colored.ColorId))
                    value = ColorToInt(colored.ColorId);
                array.Add(value);
            }
            return array;
        }
    }
}
