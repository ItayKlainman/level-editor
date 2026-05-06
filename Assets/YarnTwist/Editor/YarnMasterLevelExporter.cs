using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Master Level Exporter")]
    public sealed class YarnMasterLevelExporter : LevelExporterAsset
    {
        [SerializeField] private string _outputPath;
        [SerializeField] private StringIntMapping _colorMapping;
        [SerializeField] private StringIntMapping _cellTypeMapping;
        [SerializeField] private string _defaultRewardScoreType = "Coin";
        [SerializeField] private int    _defaultRewardAmount    = 10;

        public override string Name => "MasterLevelConfig";
        public string OutputPath => ResolveOutputPath();

        private string ResolveOutputPath()
        {
            if (string.IsNullOrEmpty(_outputPath)) return _outputPath;
            if (Path.IsPathRooted(_outputPath))    return _outputPath;
            // Relative path: resolve from project root (parent of Application.dataPath)
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", _outputPath))
                       .Replace('\\', '/');
        }

        public void SetTestDependencies(string outputPath, StringIntMapping colorMapping,
            StringIntMapping cellTypeMapping, string rewardScoreType, int rewardAmount)
        {
            _outputPath             = outputPath;
            _colorMapping           = colorMapping;
            _cellTypeMapping        = cellTypeMapping;
            _defaultRewardScoreType = rewardScoreType;
            _defaultRewardAmount    = rewardAmount;
        }

        public override bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath)
        {
            string resolvedOutput = ResolveOutputPath();

            // Validate dependencies
            if (string.IsNullOrEmpty(resolvedOutput))
            {
                Debug.LogWarning("[YarnMasterLevelExporter] Output path is not set.");
                return false;
            }
            if (_colorMapping == null)
            {
                Debug.LogWarning("[YarnMasterLevelExporter] Color mapping is not set.");
                return false;
            }
            if (_cellTypeMapping == null)
            {
                Debug.LogWarning("[YarnMasterLevelExporter] Cell type mapping is not set.");
                return false;
            }

            // Derive level key from the saved file name (e.g. "level_005.json" → "5")
            if (string.IsNullOrEmpty(jsonFilePath))
            {
                Debug.LogWarning("[YarnMasterLevelExporter] No file path — save the level before exporting.");
                return false;
            }
            var fileNameNoExt = Path.GetFileNameWithoutExtension(jsonFilePath);
            var allMatches = Regex.Matches(fileNameNoExt, @"\d+");
            if (allMatches.Count == 0)
            {
                Debug.LogWarning($"[YarnMasterLevelExporter] Could not parse integer key from filename '{fileNameNoExt}'.");
                return false;
            }
            string levelKey = int.Parse(allMatches[allMatches.Count - 1].Value).ToString();

            // Read existing file or start fresh
            JObject root = null;
            if (File.Exists(resolvedOutput))
            {
                try
                {
                    root = JObject.Parse(File.ReadAllText(resolvedOutput));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YarnMasterLevelExporter] Could not parse '{resolvedOutput}'; starting fresh. ({ex.Message})");
                    root = null;
                }
            }
            if (root == null)
            {
                root = new JObject
                {
                    ["LevelRewardConfigs"] = new JObject(),
                    ["LevelConfigs"]       = new JObject()
                };
            }

            // Ensure top-level sections exist
            if (root["LevelRewardConfigs"] == null) root["LevelRewardConfigs"] = new JObject();
            if (root["LevelConfigs"] == null)       root["LevelConfigs"]       = new JObject();

            // Build BottomConfigs from grid
            var bottomConfigs = BuildBottomConfigs(document.Grid);

            // Build TopConfigs from TopSection
            var topConfigs = BuildTopConfigs(document.TopSection);

            // Upsert LevelConfigs — preserve the key assigned by Apply Order if this levelId
            // already exists in the file, otherwise fall back to the filename-derived key.
            var levelConfigsObj = (JObject)root["LevelConfigs"];
            string existingKey = null;
            foreach (var kvp in levelConfigsObj)
            {
                if (string.Equals(kvp.Value?["levelId"]?.ToString(), fileNameNoExt, StringComparison.Ordinal))
                { existingKey = kvp.Key; break; }
            }
            string writeKey = existingKey ?? levelKey;
            levelConfigsObj[writeKey] = new JObject
            {
                ["levelId"]       = fileNameNoExt,
                ["BottomConfigs"] = bottomConfigs,
                ["TopConfigs"]    = topConfigs
            };

            // Stub LevelRewardConfigs[writeKey] only if not already present
            var rewardConfigsObj = (JObject)root["LevelRewardConfigs"];
            if (rewardConfigsObj[writeKey] == null)
            {
                rewardConfigsObj[writeKey] = new JObject
                {
                    ["WinReward"] = new JArray
                    {
                        new JObject
                        {
                            ["ScoreType"]   = _defaultRewardScoreType,
                            ["ScoreAmount"] = _defaultRewardAmount
                        }
                    }
                };
            }

            // Write output
            string dir = Path.GetDirectoryName(resolvedOutput);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(resolvedOutput, root.ToString(Formatting.Indented));
            return true;
        }

        private JArray BuildBottomConfigs(GridData<ICellData> grid)
        {
            var array = new JArray();
            if (grid == null) return array;

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var cell = grid.Get(x, y);
                    if (cell == null) continue;

                    var entry = new JObject();
                    entry["Position"] = new JObject { ["x"] = x, ["y"] = y };

                    int bottomType = _cellTypeMapping.Get(cell.CellTypeId, 0);
                    entry["BottomType"] = bottomType;

                    if (cell is YarnBoxCell boxCell)
                    {
                        entry["ColorType"] = _colorMapping.Get(boxCell.ColorId, 0);
                        entry["Hidden"]    = boxCell.Hidden;
                    }
                    else if (cell is YarnArrowBoxCell arrowBoxCell)
                    {
                        entry["ColorType"] = _colorMapping.Get(arrowBoxCell.ColorId, 0);
                        entry["Hidden"]    = false;
                        entry["Direction"] = arrowBoxCell.Direction.ToString();
                    }
                    else if (cell is YarnTunnelCell tunnelCell)
                    {
                        entry["ColorType"] = 0;
                        var queue = new JArray();
                        if (tunnelCell.Queue != null)
                        {
                            foreach (var colorId in tunnelCell.Queue)
                            {
                                queue.Add(new JObject
                                {
                                    ["ColorType"] = _colorMapping.Get(colorId, 0),
                                    ["Hidden"]    = false
                                });
                            }
                        }
                        entry["Queue"] = queue;
                    }
                    else
                    {
                        // YarnEmptyCell, YarnWallCell, or any other cell — no color
                        entry["ColorType"] = 0;
                    }

                    array.Add(entry);
                }
            }

            return array;
        }

        private JArray BuildTopConfigs(JObject topSection)
        {
            var array = new JArray();

            YarnTopSectionData topData = null;
            if (topSection != null)
            {
                try
                {
                    topData = topSection.ToObject<YarnTopSectionData>();
                }
                catch
                {
                    topData = null;
                }
            }
            if (topData == null)
                topData = new YarnTopSectionData { Columns = new System.Collections.Generic.List<YarnSpoolColumn>() };

            if (topData.Columns.Count > 4)
                Debug.LogWarning($"[YarnMasterLevelExporter] TopSection has {topData.Columns.Count} columns; only first 4 will be exported.");

            for (int i = 0; i < 4; i++)
            {
                YarnSpoolColumn column = (i < topData.Columns.Count) ? topData.Columns[i] : null;
                var winderConfigs = new JArray();

                if (column != null && column.Spools != null)
                {
                    foreach (var spool in column.Spools)
                    {
                        winderConfigs.Add(new JObject
                        {
                            ["ColorType"] = _colorMapping.Get(spool.ColorId ?? string.Empty, 0),
                            ["Hidden"]    = spool.Hidden
                        });
                    }
                }

                array.Add(new JObject
                {
                    ["Index"]         = i,
                    ["WinderConfigs"] = winderConfigs
                });
            }

            return array;
        }
    }
}