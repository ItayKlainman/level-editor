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

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Master Level Exporter")]
    public sealed class YarnMasterLevelExporter : LevelExporterAsset
    {
        [SerializeField] private string _outputPath;
        [SerializeField] private StringIntMapping _colorMapping;
        [SerializeField] private StringIntMapping _cellTypeMapping;
        [SerializeField] private string _defaultRewardScoreType = "Coin";
        [SerializeField] private int    _defaultRewardAmount    = 40;

        private const string LastCoinRewardPrefKey = "Hoppa.YarnTwist.LastCoinReward";

        // Mirrors the game's YATLevelType enum; written to LevelConfig.LevelType as the enum name.
        private static readonly string[] LevelTypeOptions = { "None", "Hard", "SuperHard" };

        // Cached order key — re-read only when the level file path changes.
        private string _orderCacheForPath;
        private string _cachedOrder = "—";

        public override string Name => "MasterLevelConfig";
        public string OutputPath => ResolveOutputPath();

        // ── Summary panel hooks ───────────────────────────────────────

        public override IEnumerable<(string label, string value)> GetSummaryExtras(LevelEditorSession session)
        {
            // Order: find this level's key in level_config.json.
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
        }

        public override int ExtraSummaryRowCount => 1;

        public override void DrawExtraSummaryRows(Rect rect, LevelEditorSession session)
        {
            var doc = session.Document;
            float lh = rect.height / ExtraSummaryRowCount; // one row per metadata field
            const float LabelW = 60f;

            // Row 0 — coin reward
            float fieldX = rect.x + LabelW + 2f;
            float fieldW = rect.width - LabelW - 2f;
            int current = doc.GameData?["coinReward"]?.Value<int>()
                ?? EditorPrefs.GetInt(LastCoinRewardPrefKey, _defaultRewardAmount);

            GUI.Label(new Rect(rect.x, rect.y, LabelW, lh), "Coins", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            int newVal = EditorGUI.IntField(
                new Rect(fieldX, rect.y, fieldW, lh), current, EditorStyles.miniTextField);
            if (EditorGUI.EndChangeCheck())
            {
                if (doc.GameData == null) doc.GameData = new JObject();
                doc.GameData["coinReward"] = newVal;
                EditorPrefs.SetInt(LastCoinRewardPrefKey, newVal);
                session.MarkDirty();
            }

            // Difficulty-type (LevelType) dropdown intentionally removed from the Summary
            // panel — it was clutter. Export still writes LevelType (defaults to "None"
            // from GameData["levelType"]); set it elsewhere if a level needs Hard/SuperHard.
        }

        // ── Export ───────────────────────────────────────────────────

        private string ResolveOutputPath()
        {
            if (string.IsNullOrEmpty(_outputPath)) return _outputPath;
            if (Path.IsPathRooted(_outputPath))    return _outputPath;
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
            if (string.IsNullOrEmpty(jsonFilePath))
            {
                Debug.LogWarning("[YarnMasterLevelExporter] No file path — save the level before exporting.");
                return false;
            }

            var fileNameNoExt = Path.GetFileNameWithoutExtension(jsonFilePath);
            var allMatches    = Regex.Matches(fileNameNoExt, @"\d+");
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
                try   { root = JObject.Parse(File.ReadAllText(resolvedOutput)); }
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

            if (root["LevelRewardConfigs"] == null) root["LevelRewardConfigs"] = new JObject();
            if (root["LevelConfigs"]       == null) root["LevelConfigs"]       = new JObject();

            var bottomConfigs = BuildBottomConfigs(document.Grid, document);
            var topConfigs    = BuildTopConfigs(document.TopSection);

            // Upsert LevelConfigs — preserve the key assigned by Apply Order if this levelId
            // already exists, otherwise fall back to the filename-derived key.
            var levelConfigsObj = (JObject)root["LevelConfigs"];
            string existingKey  = null;
            foreach (var kvp in levelConfigsObj)
            {
                if (string.Equals(kvp.Value?["levelId"]?.ToString(), fileNameNoExt, StringComparison.Ordinal))
                { existingKey = kvp.Key; break; }
            }
            string writeKey = existingKey ?? levelKey;

            string levelType = document.GameData?["levelType"]?.ToString();
            if (string.IsNullOrEmpty(levelType) || Array.IndexOf(LevelTypeOptions, levelType) < 0)
                levelType = LevelTypeOptions[0];

            levelConfigsObj[writeKey] = new JObject
            {
                ["levelId"]       = fileNameNoExt,
                ["LevelType"]     = levelType,
                ["BottomConfigs"] = bottomConfigs,
                ["TopConfigs"]    = topConfigs
            };

            // Always write reward config so per-level coin reward is honoured on every export.
            int coinReward = document.GameData?["coinReward"]?.Value<int>()
                ?? EditorPrefs.GetInt(LastCoinRewardPrefKey, _defaultRewardAmount);

            var rewardConfigsObj = (JObject)root["LevelRewardConfigs"];
            rewardConfigsObj[writeKey] = new JObject
            {
                ["WinReward"] = new JArray
                {
                    new JObject
                    {
                        ["ScoreType"]   = _defaultRewardScoreType,
                        ["ScoreAmount"] = coinReward
                    }
                }
            };

            // Invalidate the cached order key so the Summary panel refreshes after export.
            _orderCacheForPath = null;

            string dir = Path.GetDirectoryName(resolvedOutput);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(resolvedOutput, root.ToString(Formatting.Indented));
            return true;
        }

        private JArray BuildBottomConfigs(GridData<ICellData> grid, LevelDocument document)
        {
            var array = new JArray();
            if (grid == null) return array;

            // Palette centers → the game derives the 3x3 cover and hides those boxes.
            // We mark only the center cell with ExtraFeatureBottomType + PaletteAmount.
            var paletteCenters = new System.Collections.Generic.Dictionary<(int, int), int>();
            foreach (var p in YarnPalettes.All(document))
                paletteCenters[(p.CenterX, p.CenterY)] = p.Amount;

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
                        if (boxCell.ConnectedDir.HasValue)
                        {
                            // Connected box → game's BottomType.ConnectedBox (6) + reciprocal
                            // Direction string. The partner cell emits its own reciprocal entry.
                            entry["BottomType"] = _cellTypeMapping.Get("yt.connectedbox", 6);
                            entry["Direction"]  = boxCell.ConnectedDir.Value.ToString();
                        }
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
                        entry["Direction"] = tunnelCell.OutputDirection.ToString();
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
                        entry["ColorType"] = 0;
                    }

                    // Palette center → mark the covering box with the cover feature. CanPlace
                    // allows arrow-box centers too, so this must live outside the box-only branch
                    // (otherwise a palette on an arrow box is silently dropped on export).
                    if (YarnPalettes.IsBox(cell) && paletteCenters.TryGetValue((x, y), out int paletteAmount))
                    {
                        entry["ExtraFeatureBottomType"] = "Palette";
                        entry["PaletteAmount"]          = paletteAmount;
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
                try   { topData = topSection.ToObject<YarnTopSectionData>(); }
                catch { topData = null; }
            }
            if (topData == null)
                topData = new YarnTopSectionData { Columns = new System.Collections.Generic.List<YarnSpoolColumn>() };

            if (topData.Columns.Count > 4)
                Debug.LogWarning($"[YarnMasterLevelExporter] TopSection has {topData.Columns.Count} columns; only first 4 will be exported.");

            // Pre-pass: map each ConnectionId to its member positions. Only ids with
            // exactly two members are emitted as connected (an incomplete/corrupt pair
            // exports as unconnected; validation already errors on it).
            var connMembers = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<(int col, int idx)>>();
            for (int i = 0; i < 4 && i < topData.Columns.Count; i++)
            {
                var spools = topData.Columns[i]?.Spools;
                if (spools == null) continue;
                for (int s = 0; s < spools.Count; s++)
                {
                    int? id = spools[s].ConnectionId;
                    if (id == null) continue;
                    if (!connMembers.TryGetValue(id.Value, out var list))
                        connMembers[id.Value] = list = new System.Collections.Generic.List<(int, int)>();
                    list.Add((i, s));
                }
            }

            for (int i = 0; i < 4; i++)
            {
                YarnSpoolColumn column      = (i < topData.Columns.Count) ? topData.Columns[i] : null;
                var             winderConfigs = new JArray();

                if (column?.Spools != null)
                {
                    for (int s = 0; s < column.Spools.Count; s++)
                    {
                        var spool  = column.Spools[s];
                        var winder = new JObject
                        {
                            ["ColorType"] = _colorMapping.Get(spool.ColorId ?? string.Empty, 0),
                            ["Hidden"]    = spool.Hidden
                        };

                        // Connected spool → emit the game's WinderType + reciprocal
                        // partner pointer (column index + winder index = partner's data
                        // index within its column's WinderConfigs array).
                        if (spool.ConnectionId.HasValue
                            && connMembers.TryGetValue(spool.ConnectionId.Value, out var members)
                            && members.Count == 2)
                        {
                            var partner = members[0] == (i, s) ? members[1] : members[0];
                            winder["WinderType"]           = "ConnectedWinders";
                            winder["ConnectedColumnIndex"] = partner.col;
                            winder["ConnectedWinderIndex"] = partner.idx;
                        }

                        winderConfigs.Add(winder);
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
