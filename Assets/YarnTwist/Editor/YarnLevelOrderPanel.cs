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

        protected override void Draw(Rect rect, LevelEditorSession session)
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

                var evt = Event.current;
                if (evt.type == EventType.ContextClick && elementRect.Contains(evt.mousePosition))
                {
                    int captured = index;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Move to position…"), false, () => PromptMove(captured));
                    menu.AddItem(new GUIContent("Remove level"),      false, () => RemoveAt(captured));
                    menu.ShowAsContext();
                    evt.Use();
                }
            };

            _list.onRemoveCallback = list => RemoveAt(list.index);
        }

        // Right-click → "Move to position…": jump a level to an arbitrary slot without a long
        // drag. The target is the final 1-based slot (matching the #N column). In-memory only —
        // the user still clicks Apply Order to renumber + write the master JSON, same as a drag.
        private void PromptMove(int index)
        {
            if (_entries == null || index < 0 || index >= _entries.Count) return;
            var entry = _entries[index];
            MoveToPositionDialog.Show(entry.Label, index + 1, _entries.Count, targetSlot =>
            {
                if (MoveEntry(_entries, index, targetSlot - 1))
                {
                    BuildList();
                    _list.index = targetSlot - 1;
                }
            });
        }

        private void RemoveAt(int index)
        {
            if (_entries == null || index < 0 || index >= _entries.Count) return;
            var entry = _entries[index];
            bool confirmed = EditorUtility.DisplayDialog(
                "Remove Level",
                $"Permanently remove Level {entry.OriginalKey} from the master JSON?\n\nThis cannot be undone.",
                "Remove", "Cancel");
            if (!confirmed) return;
            _entries.RemoveAt(index);
            WriteToFile();
            BuildList();
        }

        // Pure reorder: move the item at `from` so it lands at index `to`, shifting the rest.
        // Returns false on no-op / out-of-range (both indices are 0-based). Unit-tested.
        public static bool MoveEntry<T>(List<T> list, int from, int to)
        {
            if (list == null) return false;
            if (from < 0 || from >= list.Count) return false;
            if (to   < 0 || to   >= list.Count) return false;
            if (from == to) return false;

            var item = list[from];
            list.RemoveAt(from);
            list.Insert(to, item);
            return true;
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

        // Tiny modal prompt for a target slot. Kept separate from the row drawing so the list
        // rendering stays untouched and the move math (MoveEntry) is unit-testable in isolation.
        private sealed class MoveToPositionDialog : EditorWindow
        {
            private string      _label;
            private int         _value;
            private int         _max;
            private Action<int> _onConfirm;
            private bool        _focused;

            public static void Show(string label, int currentSlot, int max, Action<int> onConfirm)
            {
                var win = CreateInstance<MoveToPositionDialog>();
                win.titleContent = new GUIContent("Move Level");
                win._label       = label;
                win._value       = currentSlot;
                win._max         = max;
                win._onConfirm   = onConfirm;
                win.minSize = win.maxSize = new Vector2(340f, 104f);
                win.ShowModalUtility();
            }

            private void OnGUI()
            {
                var evt = Event.current;
                if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape) { Close(); return; }

                GUILayout.Space(8f);
                EditorGUILayout.LabelField($"Move \"{_label}\" to slot:", EditorStyles.boldLabel);
                GUILayout.Space(2f);

                GUI.SetNextControlName("slotField");
                _value = EditorGUILayout.IntField($"Slot (1–{_max})", _value);
                if (!_focused) { EditorGUI.FocusTextInControl("slotField"); _focused = true; }

                bool enterPressed = evt.type == EventType.KeyDown
                    && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter);

                GUILayout.Space(8f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Cancel", GUILayout.Width(90f))) { Close(); return; }
                    if (GUILayout.Button("Move", GUILayout.Width(90f)) || enterPressed)
                    {
                        int clamped = Mathf.Clamp(_value, 1, _max);
                        _onConfirm?.Invoke(clamped);
                        Close();
                    }
                }
            }
        }
    }
}
