using UnityEditor;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.BusBuddies.Editor
{
    // Copy-mirror of YAKDifficultyCurveWindow, bus-scaled (no complexity slider).
    public sealed class BusBuddiesDifficultyCurveWindow : EditorWindow
    {
        private const string ProfilePath = "Assets/BusBuddies/Data/Config/BusBuddiesProfile.asset";
        private BusBuddiesDifficultyCurveConfig _config;
        private Vector2 _scroll;
        private int _attemptsPerLevel = 30;

        [MenuItem("Window/Hoppa/BusBuddies/Difficulty Curve")]
        public static void Open() => GetWindow<BusBuddiesDifficultyCurveWindow>("Difficulty Curve");

        private void OnGUI()
        {
            _config = (BusBuddiesDifficultyCurveConfig)EditorGUILayout.ObjectField(
                "Curve Config", _config, typeof(BusBuddiesDifficultyCurveConfig), false);
            if (_config == null)
            {
                EditorGUILayout.HelpBox("Assign or create a Bus Buddies Difficulty Curve config " +
                    "(Create ▸ Hoppa ▸ BusBuddies ▸ Generator ▸ Bus Buddies Difficulty Curve).", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawPresets();
            EditorGUILayout.Space();
            DrawCurve();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(_config.Summary(), EditorStyles.boldLabel);

            var errors = _config.Validate();
            using (new EditorGUI.DisabledScope(errors.Count > 0))
            {
                _attemptsPerLevel = EditorGUILayout.IntSlider("Attempts / level", _attemptsPerLevel, 1, 200);
                if (GUILayout.Button("Generate Curve", GUILayout.Height(30)))
                    Generate();
            }
            foreach (var e in errors) EditorGUILayout.HelpBox(e, MessageType.Error);
        }

        private void DrawPresets()
        {
            EditorGUILayout.LabelField("Tier Presets", EditorStyles.boldLabel);
            for (int i = 0; i < _config.Presets.Count; i++)
            {
                var p = _config.Presets[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                p.Name = EditorGUILayout.TextField(p.Name);
                if (EditorGUI.EndChangeCheck()) Dirty();
                if (GUILayout.Button("Duplicate", GUILayout.Width(80))) { _config.Duplicate(i); Dirty(); break; }
                if (GUILayout.Button("Delete", GUILayout.Width(60)))   { _config.DeletePreset(i); Dirty(); break; }
                EditorGUILayout.EndHorizontal();
                EditorGUI.BeginChangeCheck();
                p.GridWidth     = EditorGUILayout.IntField("Grid Width", p.GridWidth);
                p.GridHeight    = EditorGUILayout.IntField("Grid Height", p.GridHeight);
                p.MaxColors     = EditorGUILayout.IntField("Max Colors", p.MaxColors);
                p.ConveyorSlots = EditorGUILayout.IntField("Active Bus Slots", p.ConveyorSlots);

                if (p.Difficulty == null) p.Difficulty = new BusBuddiesDifficultySettings();
                var d = p.Difficulty;
                EditorGUILayout.LabelField("Difficulty (designer model)", EditorStyles.miniBoldLabel);
                d.BusesChunks      = EditorGUILayout.IntSlider("Buses Chunks", d.BusesChunks, 1, 10);
                d.DeviationPercent = EditorGUILayout.Slider("Deviation %", d.DeviationPercent, 0f, 1f);
                d.Columns          = EditorGUILayout.IntSlider("Columns", d.Columns, 1, 5);
                d.Difficulty       = EditorGUILayout.IntSlider("Difficulty (dig)", d.Difficulty, 1, 5);
                d.NoSingleBusColor = EditorGUILayout.ToggleLeft("No 1-bus color", d.NoSingleBusColor);
                d.RoundToFive      = EditorGUILayout.ToggleLeft("Round to 5", d.RoundToFive);
                if (EditorGUI.EndChangeCheck()) Dirty();
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("New Tier")) { _config.Presets.Add(new TierPreset()); Dirty(); }
        }

        private void DrawCurve()
        {
            EditorGUILayout.LabelField("Curve (ordered)", EditorStyles.boldLabel);
            var names = _config.Presets.ConvertAll(p => p.Name).ToArray();
            for (int i = 0; i < _config.Curve.Count; i++)
            {
                var seg = _config.Curve[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                int idx = Mathf.Max(0, System.Array.IndexOf(names, seg.TierName));
                idx = EditorGUILayout.Popup(idx, names);
                if (names.Length > 0) seg.TierName = names[Mathf.Clamp(idx, 0, names.Length - 1)];
                seg.LevelCount = EditorGUILayout.IntField(seg.LevelCount, GUILayout.Width(60));
                if (EditorGUI.EndChangeCheck()) Dirty();
                if (GUILayout.Button("↑", GUILayout.Width(24)) && i > 0) { (_config.Curve[i-1], _config.Curve[i]) = (_config.Curve[i], _config.Curve[i-1]); Dirty(); }
                if (GUILayout.Button("↓", GUILayout.Width(24)) && i < _config.Curve.Count-1) { (_config.Curve[i+1], _config.Curve[i]) = (_config.Curve[i], _config.Curve[i+1]); Dirty(); }
                if (GUILayout.Button("✕", GUILayout.Width(24))) { _config.Curve.RemoveAt(i); Dirty(); break; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Add Segment")) { _config.Curve.Add(new CurveSegment()); Dirty(); }
        }

        private void Dirty() { EditorUtility.SetDirty(_config); }

        private void Generate()
        {
            var profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            if (profile == null) { EditorUtility.DisplayDialog("Bus Buddies Curve", "Profile not found at " + ProfilePath, "OK"); return; }
            AssetDatabase.SaveAssets();
            string dir = BusBuddiesCurveBatchHarness.RunCurve(_config, profile, _attemptsPerLevel, null);
            if (dir != null)
            {
                var win = GetWindow<BatchReviewWindow>();
                win.LoadStagingDir(dir);
                win.Show();
                EditorUtility.RevealInFinder(dir);
            }
        }
    }
}
