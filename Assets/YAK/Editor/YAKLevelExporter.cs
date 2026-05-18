using System.Collections.Generic;
using System.IO;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Writes one YAK LevelConfig JSON per level. Color values are stored as ints
    // (not enum-name strings) via YAKColorMapping. PixelColors is a flat int[]
    // of length Width*Height in row-major bottom-up order (index = y*Width + x);
    // 0 represents an empty cell.
    [CreateAssetMenu(menuName = "Hoppa/YAK/Level Exporter")]
    public sealed class YAKLevelExporter : LevelExporterAsset
    {
        [Tooltip("Directory the exported per-level JSON files are written to.\nLeave empty until the game project's path is known.")]
        [SerializeField] private string _outputDir;

        [Tooltip("Maps colorId (e.g. 'blue') to its YAKColorType int. Required for export.")]
        [SerializeField] private StringIntMapping _colorMapping;

        [Tooltip("Default ConveyorCount for new levels and a fallback when the level doesn't set one.")]
        [SerializeField] private int _defaultConveyorCount = 5;

        private const string LastConveyorPrefKey = "Hoppa.YAK.LastConveyor";

        public override string Name => "YAKLevelConfig";
        public string OutputDir => ResolveOutputDir();

        // ── Summary panel hooks ───────────────────────────────────────

        public override IEnumerable<(string label, string value)> GetSummaryExtras(LevelEditorSession session)
        {
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

        private string ResolveOutputDir()
        {
            if (string.IsNullOrEmpty(_outputDir)) return _outputDir;
            if (Path.IsPathRooted(_outputDir))    return _outputDir;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", _outputDir))
                       .Replace('\\', '/');
        }

        public void SetTestDependencies(string outputDir, StringIntMapping colorMapping, int defaultConveyorCount)
        {
            _outputDir            = outputDir;
            _colorMapping         = colorMapping;
            _defaultConveyorCount = defaultConveyorCount;
        }

        public override bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath)
        {
            string resolvedDir = ResolveOutputDir();

            if (string.IsNullOrEmpty(resolvedDir))
            {
                Debug.LogWarning("[YAKLevelExporter] Output dir is not set on the exporter asset.");
                return false;
            }
            if (_colorMapping == null)
            {
                Debug.LogWarning("[YAKLevelExporter] Color mapping is not set on the exporter asset.");
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

            int conveyorCount = document.GameData?["conveyorCount"]?.Value<int>()
                ?? EditorPrefs.GetInt(LastConveyorPrefKey, _defaultConveyorCount);

            var root = new JObject
            {
                ["ConveyorCount"]      = conveyorCount,
                ["Width"]              = grid.Width,
                ["Height"]             = grid.Height,
                ["SpoolColumnConfigs"] = BuildSpoolColumnConfigs(document.TopSection),
                ["PixelColors"]        = BuildPixelColors(grid),
            };

            string fileName = Path.GetFileName(jsonFilePath);
            string outPath  = Path.Combine(resolvedDir, fileName).Replace('\\', '/');

            Directory.CreateDirectory(resolvedDir);
            File.WriteAllText(outPath, root.ToString(Formatting.Indented));
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
                            ["ColorType"] = _colorMapping.Get(spool.ColorId ?? string.Empty, 0),
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
            var array = new JArray();
            int total = grid.Width * grid.Height;
            for (int i = 0; i < total; i++)
            {
                var cell = grid.Cells[i];
                int value = 0;
                if (cell is YAKWoolCell wool && !string.IsNullOrEmpty(wool.ColorId))
                    value = _colorMapping.Get(wool.ColorId, 0);
                array.Add(value);
            }
            return array;
        }
    }
}
