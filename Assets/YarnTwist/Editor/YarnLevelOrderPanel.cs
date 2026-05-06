using System;
using System.Collections.Generic;
using System.IO;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Level Order Panel")]
    public sealed class YarnLevelOrderPanel : EditorPanelAsset
    {
        [SerializeField] private YarnMasterLevelExporter _exporter;

        public override string PanelId => "yt.level-order";

        private sealed class LevelEntry
        {
            public int    OriginalKey;
            public string Label;
            public JToken LevelConfig;
            public JToken RewardConfig;
        }

        private List<LevelEntry> _entries;
        private ReorderableList  _list;
        private bool             _loaded;
        private Vector2          _scroll;

        public override void OnGUI(Rect rect, LevelEditorSession session)
        {
            if (!_loaded) Reload();

            GUILayout.BeginArea(rect);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Space(8f);
            GUILayout.Label("Level Order Manager", EditorStyles.boldLabel);
            GUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                "Drag levels to the desired order, then click Apply Order to renumber the master JSON.\n" +
                "Saving a level after reordering will update it in-place at its assigned slot.",
                MessageType.Info);

            GUILayout.Space(4f);
            if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
                Reload();

            GUILayout.Space(8f);

            if (_entries == null || _entries.Count == 0)
            {
                EditorGUILayout.HelpBox("No levels found in the master JSON. Export at least one level first.", MessageType.Info);
            }
            else
            {
                _list?.DoLayoutList();

                GUILayout.Space(8f);
                if (GUILayout.Button("Apply Order", GUILayout.Height(28f)))
                    ApplyOrder();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Reload()
        {
            _entries = new List<LevelEntry>();
            _loaded  = true;

            if (_exporter == null)
            {
                Debug.LogWarning("[YarnLevelOrderPanel] No exporter assigned. Set the Exporter field on this asset.");
                return;
            }

            string path = _exporter.OutputPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning($"[YarnLevelOrderPanel] Master JSON not found at '{path}'.");
                return;
            }

            JObject root;
            try { root = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YarnLevelOrderPanel] Could not parse master JSON: {ex.Message}");
                return;
            }

            var levelConfigs  = root["LevelConfigs"]       as JObject ?? new JObject();
            var rewardConfigs = root["LevelRewardConfigs"] as JObject ?? new JObject();

            foreach (var kvp in levelConfigs)
            {
                if (!int.TryParse(kvp.Key, out int key)) continue;
                string label = kvp.Value?["levelId"]?.ToString();
                if (string.IsNullOrEmpty(label)) label = $"Level {key}";
                _entries.Add(new LevelEntry
                {
                    OriginalKey  = key,
                    Label        = label,
                    LevelConfig  = kvp.Value,
                    RewardConfig = rewardConfigs[kvp.Key]
                });
            }
            _entries.Sort((a, b) => a.OriginalKey.CompareTo(b.OriginalKey));

            BuildList();
        }

        private void BuildList()
        {
            _list = new ReorderableList(_entries, typeof(LevelEntry),
                draggable: true, displayHeader: true,
                displayAddButton: false, displayRemoveButton: true);

            _list.drawHeaderCallback = headerRect =>
                EditorGUI.LabelField(headerRect, "Levels  (drag ☰ to reorder)");

            _list.drawElementCallback = (elementRect, index, isActive, isFocused) =>
            {
                var   entry  = _entries[index];
                float lh     = EditorGUIUtility.singleLineHeight;
                float y      = elementRect.y + (elementRect.height - lh) * 0.5f;
                const float IndexW = 30f;

                EditorGUI.LabelField(
                    new Rect(elementRect.x, y, IndexW, lh),
                    $"#{index + 1}", EditorStyles.miniLabel);
                EditorGUI.LabelField(
                    new Rect(elementRect.x + IndexW + 4f, y, elementRect.width - IndexW - 4f, lh),
                    entry.Label);
            };

            _list.onRemoveCallback = list =>
            {
                var entry = _entries[list.index];
                bool confirmed = EditorUtility.DisplayDialog(
                    "Remove Level",
                    $"Permanently remove Level {entry.OriginalKey} from the master JSON?\n\nThis cannot be undone.",
                    "Remove", "Cancel");
                if (!confirmed) return;
                _entries.RemoveAt(list.index);
                WriteToFile();
                BuildList();
            };
        }

        private void ApplyOrder()
        {
            if (_exporter == null || _entries == null || _entries.Count == 0) return;
            if (!WriteToFile()) return;
            EditorUtility.DisplayDialog("Order Applied",
                $"{_entries.Count} level(s) renumbered successfully.", "OK");
            Reload();
        }

        private bool WriteToFile()
        {
            string path = _exporter.OutputPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                EditorUtility.DisplayDialog("Error", $"Master JSON not found at:\n{path}", "OK");
                return false;
            }

            JObject root;
            try { root = JObject.Parse(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Could not parse master JSON:\n{ex.Message}", "OK");
                return false;
            }

            var newLevelConfigs  = new JObject();
            var newRewardConfigs = new JObject();

            for (int i = 0; i < _entries.Count; i++)
            {
                string newKey = (i + 1).ToString();
                newLevelConfigs[newKey] = _entries[i].LevelConfig;
                if (_entries[i].RewardConfig != null)
                    newRewardConfigs[newKey] = _entries[i].RewardConfig;
            }

            root["LevelConfigs"]       = newLevelConfigs;
            root["LevelRewardConfigs"] = newRewardConfigs;

            File.WriteAllText(path, root.ToString(Formatting.Indented));
            AssetDatabase.Refresh();
            return true;
        }
    }
}
