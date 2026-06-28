using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Generic curation window for a batch staging folder: shows each candidate
    // LevelDocument as a thumbnail + stats, supports multi-select, and imports the
    // chosen ones into a levels folder via the normal file path. Game-agnostic —
    // it loads documents through the active GameProfile's cell registry.
    public sealed class BatchReviewWindow : EditorWindow
    {
        [MenuItem("Window/Hoppa/Batch Review")]
        public static void Open() => GetWindow<BatchReviewWindow>("Batch Review");

        private const string StagingPrefKey = "Hoppa.LevelEditor.BatchStagingDir";
        private const string TargetPrefKey  = "Hoppa.LevelEditor.BatchTargetDir";
        private const string ProfileGuidPrefKey = "Hoppa.LevelEditor.ProfileGuid";

        private GameProfile _profile;
        private string _stagingDir;
        private string _targetDir;

        private List<BatchCandidate> _candidates = new List<BatchCandidate>();
        private readonly HashSet<string> _selected = new HashSet<string>();
        private readonly Dictionary<string, Texture2D> _thumbs = new Dictionary<string, Texture2D>();
        private Vector2 _scroll;
        private string _message;

        private const float ThumbSize = 110f;
        private const float CellW = 150f;
        private const float CellH = 168f;

        private void OnEnable()
        {
            _stagingDir = EditorPrefs.GetString(StagingPrefKey, "");
            _targetDir  = EditorPrefs.GetString(TargetPrefKey, "");
            if (_profile == null)
            {
                var guid = EditorPrefs.GetString(ProfileGuidPrefKey, "");
                if (!string.IsNullOrEmpty(guid))
                    _profile = AssetDatabase.LoadAssetAtPath<GameProfile>(AssetDatabase.GUIDToAssetPath(guid));
            }
            if (!string.IsNullOrEmpty(_stagingDir)) Refresh();
        }

        private void OnDisable() => ClearThumbs();

        private void OnGUI()
        {
            DrawHeader();
            if (_candidates.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(_stagingDir)
                        ? "Pick a staging folder produced by a batch run."
                        : "No candidates found in the staging folder.",
                    MessageType.Info);
                return;
            }
            DrawGrid();
            if (!string.IsNullOrEmpty(_message))
                EditorGUILayout.HelpBox(_message, MessageType.None);
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(2);
            _profile = (GameProfile)EditorGUILayout.ObjectField("Game Profile", _profile, typeof(GameProfile), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Staging folder");
                EditorGUILayout.SelectableLabel(_stagingDir, EditorStyles.textField, GUILayout.Height(18));
                if (GUILayout.Button("Browse…", GUILayout.Width(70)))
                {
                    var dir = EditorUtility.OpenFolderPanel("Batch staging folder", _stagingDir ?? Application.dataPath, "");
                    if (!string.IsNullOrEmpty(dir)) { _stagingDir = dir; EditorPrefs.SetString(StagingPrefKey, dir); Refresh(); }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Import target");
                EditorGUILayout.SelectableLabel(_targetDir, EditorStyles.textField, GUILayout.Height(18));
                if (GUILayout.Button("Browse…", GUILayout.Width(70)))
                {
                    var dir = EditorUtility.OpenFolderPanel("Import target folder", _targetDir ?? Application.dataPath, "");
                    if (!string.IsNullOrEmpty(dir)) { _targetDir = dir; EditorPrefs.SetString(TargetPrefKey, dir); }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh")) Refresh();
                if (GUILayout.Button("Select All")) { foreach (var c in _candidates) _selected.Add(c.Id); }
                if (GUILayout.Button("Select None")) _selected.Clear();
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_selected.Count}/{_candidates.Count} selected", EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(_selected.Count == 0 || string.IsNullOrEmpty(_targetDir)))
                {
                    if (GUILayout.Button($"Import Selected ▸", GUILayout.Width(130))) ImportSelected();
                }
            }
            EditorGUILayout.Space(2);
        }

        private void DrawGrid()
        {
            int perRow = Mathf.Max(1, Mathf.FloorToInt((position.width - 16f) / CellW));
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _candidates.Count; i += perRow)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < i + perRow && j < _candidates.Count; j++)
                        DrawCell(_candidates[j]);
                    GUILayout.FlexibleSpace();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawCell(BatchCandidate c)
        {
            using (new EditorGUILayout.VerticalScope(GUI.skin.box, GUILayout.Width(CellW), GUILayout.Height(CellH)))
            {
                var thumb = GetThumb(c);
                var rect = GUILayoutUtility.GetRect(ThumbSize, ThumbSize, GUILayout.Width(CellW - 12));
                if (thumb != null) GUI.DrawTexture(rect, thumb, ScaleMode.ScaleToFit);
                else EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));

                bool sel = _selected.Contains(c.Id);
                bool newSel = GUILayout.Toggle(sel, " " + c.Id, EditorStyles.miniBoldLabel);
                if (newSel != sel) { if (newSel) _selected.Add(c.Id); else _selected.Remove(c.Id); }

                if (c.Stats != null)
                {
                    var col = c.Stats.solvable ? new Color(0.6f, 0.85f, 0.6f) : new Color(1f, 0.5f, 0.45f);
                    var old = GUI.contentColor; GUI.contentColor = col;
                    GUILayout.Label($"{c.Stats.status} · APS {c.Stats.aps:0.0}", EditorStyles.miniLabel);
                    GUI.contentColor = old;
                    GUILayout.Label($"band {c.Stats.band} · {c.Stats.distinctColors} colors", EditorStyles.miniLabel);
                    if (c.Stats.complexity > 0f)
                        GUILayout.Label($"cplx {c.Stats.complexity:0.0}", EditorStyles.miniLabel);
                    if (!string.IsNullOrEmpty(c.Stats.tier))
                        GUILayout.Label($"Tier: {c.Stats.tier}" + (c.Stats.offTarget ? "  OFF-TARGET" : ""),
                            c.Stats.offTarget ? EditorStyles.miniBoldLabel : EditorStyles.miniLabel);
                }
                else GUILayout.Label("(no stats)", EditorStyles.miniLabel);
            }
        }

        private Texture2D GetThumb(BatchCandidate c)
        {
            if (_thumbs.TryGetValue(c.Id, out var t)) return t;
            Texture2D tex = null;
            if (c.ThumbnailPath != null && File.Exists(c.ThumbnailPath))
            {
                tex = new Texture2D(2, 2) { filterMode = FilterMode.Point };
                if (!tex.LoadImage(File.ReadAllBytes(c.ThumbnailPath))) { DestroyImmediate(tex); tex = null; }
            }
            if (tex == null && _profile != null)
            {
                try
                {
                    var doc = new JsonLevelSerializer().Load(File.ReadAllText(c.JsonPath), _profile.BuildRegistry());
                    tex = LevelThumbnail.Render(doc, _profile.ColorPalette, 4);
                }
                catch { /* leave null */ }
            }
            _thumbs[c.Id] = tex;
            return tex;
        }

        // Called by external harnesses (e.g. YAKDifficultyCurveWindow) to point
        // the review window at a freshly-generated staging folder immediately.
        public void LoadStagingDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            _stagingDir = dir;
            EditorPrefs.SetString(StagingPrefKey, dir);
            Refresh();
        }

        private void Refresh()
        {
            ClearThumbs();
            _candidates = BatchStaging.Scan(_stagingDir);
            _selected.RemoveWhere(id => !_candidates.Exists(c => c.Id == id));
            _message = null;
        }

        private void ImportSelected()
        {
            int n = 0;
            foreach (var c in _candidates)
                if (_selected.Contains(c.Id))
                {
                    BatchStaging.Import(c.JsonPath, _targetDir);
                    n++;
                }
            AssetDatabase.Refresh();
            _message = $"Imported {n} level(s) to {_targetDir}";
        }

        private void ClearThumbs()
        {
            foreach (var kv in _thumbs) if (kv.Value != null) DestroyImmediate(kv.Value);
            _thumbs.Clear();
        }
    }
}
