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
using UnityEngine.Serialization;

namespace Hoppa.YAK.Editor
{
    // Writes / upserts the YAK master level config file. The runtime expects a
    // single JSON document of the shape:
    //   { "LevelConfigs": { "<intKey>": { ConveyorCount, Width, Height,
    //                                     SpoolColumnConfigs, PixelColors } } }
    // We additionally embed a non-runtime "levelId" field per entry (the source
    // filename without extension) so re-exports of the same level reuse the key
    // assigned by Apply Order. PixelColors is a flat int[] of length Width*Height
    // in row-major bottom-up order (index = y*Width + x); 0 = empty cell.
    [CreateAssetMenu(menuName = "Hoppa/YAK/Master Level Exporter")]
    public sealed class YAKLevelExporter : LevelExporterAsset
    {
        [Tooltip("Full path to the master level_config.json the game loads.\nLeave empty until the game project's path is known.")]
        [FormerlySerializedAs("_outputDir")]
        [SerializeField] private string _outputPath;

        [Tooltip("Single source of truth for colors — drives both the int mapping used at export and the RGB swatches in the editor UI.")]
        [SerializeField] private YAKStaticManagerColorSource _colorSource;

        [Tooltip("Default ConveyorCount for new levels and a fallback when the level doesn't set one.")]
        [SerializeField] private int _defaultConveyorCount = 5;

        private const string LastConveyorPrefKey = "Hoppa.YAK.LastConveyor";

        // Cached order key — re-read only when the level file path changes.
        private string _orderCacheForPath;
        private string _cachedOrder = "—";

        public override string Name => "YAKMasterLevelConfig";
        public string OutputPath => ResolveOutputPath();

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
            int totalSpools = 0;
            int wool = 0;

            if (session.Document?.TopSection != null)
            {
                YAKTopSectionData top = null;
                try   { top = session.Document.TopSection.ToObject<YAKTopSectionData>(); }
                catch { top = null; }

                if (top?.Columns != null)
                {
                    columns = top.Columns.Count;
                    foreach (var col in top.Columns)
                        if (col?.Spools != null) totalSpools += col.Spools.Count;
                }
            }

            var grid = session.Document?.Grid;
            if (grid?.Cells != null)
            {
                foreach (var cell in grid.Cells)
                    if (cell is YAKWoolCell) wool++;
            }

            yield return ("Columns",  columns.ToString());
            yield return ("Spools",   totalSpools.ToString());
            yield return ("Wool",     wool.ToString());
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

        public void SetTestDependencies(string outputPath, YAKStaticManagerColorSource colorSource, int defaultConveyorCount)
        {
            _outputPath           = outputPath;
            _colorSource          = colorSource;
            _defaultConveyorCount = defaultConveyorCount;
        }

        public override bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath)
        {
            string resolvedOutput = ResolveOutputPath();

            if (string.IsNullOrEmpty(resolvedOutput))
            {
                Debug.LogWarning("[YAKLevelExporter] Output path is not set on the exporter asset.");
                return false;
            }
            if (_colorSource == null)
            {
                Debug.LogWarning("[YAKLevelExporter] Color source is not set on the exporter asset.");
                return false;
            }
            if (string.IsNullOrEmpty(jsonFilePath))
            {
                Debug.LogWarning("[YAKLevelExporter] No file path — save the level before exporting.");
                return false;
            }

            var grid = document.Grid;
            if (grid == null)
            {
                Debug.LogWarning("[YAKLevelExporter] Document has no grid.");
                return false;
            }

            var fileNameNoExt = Path.GetFileNameWithoutExtension(jsonFilePath);
            var allMatches    = Regex.Matches(fileNameNoExt, @"\d+");
            if (allMatches.Count == 0)
            {
                Debug.LogWarning($"[YAKLevelExporter] Could not parse integer key from filename '{fileNameNoExt}'.");
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
                    Debug.LogWarning($"[YAKLevelExporter] Could not parse '{resolvedOutput}'; starting fresh. ({ex.Message})");
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
            YAKTopSectionData top = null;
            if (topSection != null)
            {
                try   { top = topSection.ToObject<YAKTopSectionData>(); }
                catch { top = null; }
            }
            if (top?.Columns == null) return array;

            foreach (var col in top.Columns)
            {
                var spoolConfigs = new JArray();
                if (col?.Spools != null)
                {
                    foreach (var spool in col.Spools)
                    {
                        spoolConfigs.Add(new JObject
                        {
                            ["ColorType"] = _colorSource.GetInt(spool.ColorId ?? string.Empty, 0),
                            ["Capacity"]  = spool.Capacity,
                            ["IsHidden"]  = spool.Hidden,
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
            // Matches the framework's GridData default (RowOrder = "bottomUp"); the
            // grid's flat Cells[] array is already laid out this way, so no inversion needed.
            // The game reads via `index = y * width + x` regardless of its column-first
            // iteration loop in YAKYarnPixelColumnPrefabComponent.Init, so this is correct.
            var array = new JArray();
            int total = grid.Width * grid.Height;
            for (int i = 0; i < total; i++)
            {
                var cell = grid.Cells[i];
                int value = 0;
                if (cell is YAKWoolCell wool && !string.IsNullOrEmpty(wool.ColorId))
                    value = _colorSource.GetInt(wool.ColorId, 0);
                array.Add(value);
            }
            return array;
        }
    }
}
