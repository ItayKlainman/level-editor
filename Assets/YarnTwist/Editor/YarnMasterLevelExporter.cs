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

        internal void SetTestDependencies(string outputPath, StringIntMapping colorMapping,
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
            // Validate dependencies
            if (string.IsNullOrEmpty(_outputPath))
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

            // Parse level key from LevelId (e.g. "level_001" -> 1)
            var match = Regex.Match(document.LevelId ?? string.Empty, @"\d+$");
            if (!match.Success)
            {
                Debug.LogWarning($"[YarnMasterLevelExporter] Could not parse integer key from LevelId '{document.LevelId}'.");
                return false;
            }
            string levelKey = int.Parse(match.Value).ToString();

            // Read existing file or start fresh
            JObject root = null;
            if (File.Exists(_outputPath))
            {
                try
                {
                    root = JObject.Parse(File.ReadAllText(_outputPath));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[YarnMasterLevelExporter] Could not parse '{_outputPath}'; starting fresh. ({ex.Message})");
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

            // Upsert LevelConfigs[levelKey]
            var levelConfigsObj = (JObject)root["LevelConfigs"];
            levelConfigsObj[levelKey] = new JObject
            {
                ["BottomConfigs"] = bottomConfigs,
                ["TopConfigs"]    = topConfigs
            };

            // Stub LevelRewardConfigs[levelKey] only if not already present
            var rewardConfigsObj = (JObject)root["LevelRewardConfigs"];
            if (rewardConfigsObj[levelKey] == null)
            {
                rewardConfigsObj[levelKey] = new JObject
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
            string dir = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_outputPath, root.ToString(Formatting.Indented));
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
