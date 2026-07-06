using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.BusBuddies;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace Hoppa.BusBuddies.Editor
{
    // Writes / upserts the Bus Buddies master level config file. Emits the SAME
    // schema YarnKingdom loads:
    //   { "LevelConfigs": { "<intKey>": { ConveyorCount, Width, Height,
    //                                     SpoolColumnConfigs, PixelColors } } }
    // We additionally embed a non-runtime "levelId" field per entry (the source
    // filename without extension) so re-exports of the same level reuse the key.
    // PixelColors is a flat int[] of length Width*Height in row-major bottom-up
    // order (index = y*Width + x); 0 = empty cell.
    //
    // Retyped copy of YAKLevelExporter — BB.Editor deliberately does NOT reference
    // the Hoppa.YAK.Editor assembly, so this carries its OWN colorId→int map
    // (a serialized list of {id,value} pairs, seeded with the 8 game colors). BB
    // colorIds are lowercase; the lookup is case-insensitive; unmapped/empty → 0.
    // Connected pairs (BusEntry.ConnectedId) are intentionally dropped — they are
    // not part of the YarnKingdom schema.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Master Level Exporter")]
    public sealed class BusBuddiesLevelExporter : LevelExporterAsset
    {
        [Serializable]
        public struct ColorIdMapEntry
        {
            public string Id;
            public int    Value;
        }

        [Tooltip("Full path to the master level_config.json the game loads.\nProject-relative (against Application.dataPath/..) or absolute.")]
        [FormerlySerializedAs("_outputDir")]
        [SerializeField] private string _outputPath;

        [Tooltip("Bus Buddies colorId → int ordinal map. Verified against the game's YAKColorType enum. Case-insensitive; unmapped / empty id → 0.")]
        [SerializeField] private List<ColorIdMapEntry> _colorMap = DefaultColorMap();

        [Tooltip("Default ConveyorCount for new levels and a fallback when the level doesn't set one.")]
        [SerializeField] private int _defaultConveyorCount = 5;

        private const string LastConveyorPrefKey = "Hoppa.BusBuddies.LastConveyor";

        // Cached order key — re-read only when the level file path changes.
        private string _orderCacheForPath;
        private string _cachedOrder = "—";

        public override string Name => "BusBuddiesMasterLevelConfig";
        public string OutputPath => ResolveOutputPath();

        private static List<ColorIdMapEntry> DefaultColorMap() => new List<ColorIdMapEntry>
        {
            new ColorIdMapEntry { Id = "blue",   Value = 1 },
            new ColorIdMapEntry { Id = "cyan",   Value = 2 },
            new ColorIdMapEntry { Id = "yellow", Value = 3 },
            new ColorIdMapEntry { Id = "green",  Value = 4 },
            new ColorIdMapEntry { Id = "orange", Value = 6 },
            new ColorIdMapEntry { Id = "pink",   Value = 7 },
            new ColorIdMapEntry { Id = "purple", Value = 8 },
            new ColorIdMapEntry { Id = "red",    Value = 9 },
        };

        // Case-insensitive lookup; unmapped / null / empty id → 0.
        private int ColorToInt(string colorId)
        {
            if (string.IsNullOrEmpty(colorId) || _colorMap == null) return 0;
            foreach (var e in _colorMap)
                if (string.Equals(e.Id, colorId, StringComparison.OrdinalIgnoreCase))
                    return e.Value;
            return 0;
        }

        // ── Summary panel hooks ───────────────────────────────────────

        public override IEnumerable<(string label, string value)> GetSummaryExtras(LevelEditorSession session)
        {
            // Order: find this level's key in the master level_config.json.
            if (!string.IsNullOrEmpty(session.FilePath))
            {
                if (session.FilePath != _orderCacheForPath)
                {
                    _orderCacheForPath = session.FilePath;
                    _cachedOrder = "—";
                    string fileId  = Path.GetFileNameWithoutExtension(session.FilePath);
                    string output  = ResolveOutputPath();
                    if (!string.IsNullOrEmpty(output) && File.Exists(output))
                    {
                        try
                        {
                            var root    = JObject.Parse(File.ReadAllText(output));
                            var configs = root["LevelConfigs"] as JObject;
                            if (configs != null)
                            {
                                foreach (var kvp in configs)
                                {
                                    if (string.Equals(kvp.Value?["levelId"]?.ToString(), fileId,
                                            StringComparison.Ordinal))
                                    { _cachedOrder = kvp.Key; break; }
                                }
                            }
                        }
                        catch { _cachedOrder = "—"; }
                    }
                }
                yield return ("Order", _cachedOrder);
            }

            int columns = 0;
            int totalBuses = 0;
            int pixels = 0;

            if (session.Document?.TopSection != null)
            {
                BusQueueData top = null;
                try   { top = session.Document.TopSection.ToObject<BusQueueData>(); }
                catch { top = null; }

                if (top?.Columns != null)
                {
                    columns = top.Columns.Count;
                    foreach (var col in top.Columns)
                        if (col?.Buses != null) totalBuses += col.Buses.Count;
                }
            }

            var grid = session.Document?.Grid;
            if (grid?.Cells != null)
            {
                foreach (var cell in grid.Cells)
                    if (cell is BBPixelCell) pixels++;
            }

            yield return ("Columns", columns.ToString());
            yield return ("Buses",   totalBuses.ToString());
            yield return ("Pixels",  pixels.ToString());
        }

        public override int ExtraSummaryRowCount => 1;

        public override void DrawExtraSummaryRows(Rect rect, LevelEditorSession session)
        {
            var doc = session.Document;
            float lh = rect.height;

            int current = doc.GameData?["conveyorCount"]?.Value<int>()
                ?? EditorPrefs.GetInt(LastConveyorPrefKey, _defaultConveyorCount);

            const float LabelW = 60f;
            GUI.Label(new Rect(rect.x, rect.y, LabelW, lh), "Conveyor", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            int newVal = EditorGUI.IntField(
                new Rect(rect.x + LabelW + 2f, rect.y, rect.width - LabelW - 2f, lh),
                current, EditorStyles.miniTextField);
            if (EditorGUI.EndChangeCheck())
            {
                newVal = Mathf.Max(1, newVal);
                if (doc.GameData == null) doc.GameData = new JObject();
                doc.GameData["conveyorCount"] = newVal;
                EditorPrefs.SetInt(LastConveyorPrefKey, newVal);
                session.MarkDirty();
            }
        }

        // ── Export ───────────────────────────────────────────────────

        private string ResolveOutputPath()
        {
            if (string.IsNullOrEmpty(_outputPath)) return _outputPath;
            if (Path.IsPathRooted(_outputPath))    return _outputPath;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", _outputPath))
                       .Replace('\\', '/');
        }

        public void SetTestDependencies(string outputPath, int defaultConveyorCount,
                                        List<ColorIdMapEntry> colorMap = null)
        {
            _outputPath           = outputPath;
            _defaultConveyorCount = defaultConveyorCount;
            if (colorMap != null) _colorMap = colorMap;
            else if (_colorMap == null || _colorMap.Count == 0) _colorMap = DefaultColorMap();
        }

        public override bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath)
        {
            string resolvedOutput = ResolveOutputPath();

            if (string.IsNullOrEmpty(resolvedOutput))
            {
                Debug.LogWarning("[BusBuddiesLevelExporter] Output path is not set on the exporter asset.");
                return false;
            }
            if (string.IsNullOrEmpty(jsonFilePath))
            {
                Debug.LogWarning("[BusBuddiesLevelExporter] No file path — save the level before exporting.");
                return false;
            }

            var grid = document.Grid;
            if (grid == null)
            {
                Debug.LogWarning("[BusBuddiesLevelExporter] Document has no grid.");
                return false;
            }

            var fileNameNoExt = Path.GetFileNameWithoutExtension(jsonFilePath);
            var allMatches    = Regex.Matches(fileNameNoExt, @"\d+");
            if (allMatches.Count == 0)
            {
                Debug.LogWarning($"[BusBuddiesLevelExporter] Could not parse integer key from filename '{fileNameNoExt}'.");
                return false;
            }
            string levelKey = int.Parse(allMatches[allMatches.Count - 1].Value).ToString();

            // Read existing master file or start fresh.
            JObject root = null;
            if (File.Exists(resolvedOutput))
            {
                try   { root = JObject.Parse(File.ReadAllText(resolvedOutput)); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BusBuddiesLevelExporter] Could not parse '{resolvedOutput}'; starting fresh. ({ex.Message})");
                    root = null;
                }
            }
            if (root == null)
                root = new JObject { ["LevelConfigs"] = new JObject() };
            if (root["LevelConfigs"] == null)
                root["LevelConfigs"] = new JObject();

            int conveyorCount = document.GameData?["conveyorCount"]?.Value<int>()
                ?? EditorPrefs.GetInt(LastConveyorPrefKey, _defaultConveyorCount);

            // Upsert LevelConfigs — preserve the key assigned by Apply Order if this
            // levelId already exists, otherwise fall back to the filename-derived key.
            var levelConfigsObj = (JObject)root["LevelConfigs"];
            string existingKey  = null;
            foreach (var kvp in levelConfigsObj)
            {
                if (string.Equals(kvp.Value?["levelId"]?.ToString(), fileNameNoExt, StringComparison.Ordinal))
                { existingKey = kvp.Key; break; }
            }
            string writeKey = existingKey ?? levelKey;

            levelConfigsObj[writeKey] = new JObject
            {
                ["levelId"]            = fileNameNoExt,
                ["ConveyorCount"]      = conveyorCount,
                ["Width"]              = grid.Width,
                ["Height"]             = grid.Height,
                ["SpoolColumnConfigs"] = BuildSpoolColumnConfigs(document.TopSection),
                ["PixelColors"]        = BuildPixelColors(grid),
            };

            // Invalidate the cached Order so the Summary panel refreshes after export.
            _orderCacheForPath = null;

            string dir = Path.GetDirectoryName(resolvedOutput);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(resolvedOutput, root.ToString(Formatting.Indented));
            return true;
        }

        private JArray BuildSpoolColumnConfigs(JObject topSection)
        {
            var array = new JArray();
            BusQueueData top = null;
            if (topSection != null)
            {
                try   { top = topSection.ToObject<BusQueueData>(); }
                catch { top = null; }
            }
            if (top?.Columns == null) return array;

            foreach (var col in top.Columns)
            {
                var spoolConfigs = new JArray();
                if (col?.Buses != null)
                {
                    foreach (var bus in col.Buses)
                    {
                        spoolConfigs.Add(new JObject
                        {
                            ["ColorType"] = ColorToInt(bus.ColorId ?? string.Empty),
                            ["Capacity"]  = bus.Capacity,
                            ["IsHidden"]  = bus.Hidden,
                        });
                    }
                }
                array.Add(new JObject { ["SpoolConfigs"] = spoolConfigs });
            }

            return array;
        }

        private JArray BuildPixelColors(GridData<ICellData> grid)
        {
            // Row-major bottom-up: index = y * Width + x, y=0 is the bottom row.
            // The grid's flat Cells[] array is already laid out this way, so no
            // inversion needed. Empty (BBEmptyCell) / unmapped → 0.
            var array = new JArray();
            int total = grid.Width * grid.Height;
            for (int i = 0; i < total; i++)
            {
                var cell = grid.Cells[i];
                int value = 0;
                if (cell is BBPixelCell pixel && !string.IsNullOrEmpty(pixel.ColorId))
                    value = ColorToInt(pixel.ColorId);
                array.Add(value);
            }
            return array;
        }
    }
}
